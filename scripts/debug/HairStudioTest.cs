using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Tools;

namespace NSP.Debug;

// 캐릭터 헤어 스튜디오 검증 (지시서 §10 완료 조건).
//
//   godot --headless --path . res://scenes/debug/HairStudioTest.tscn --quit-after 4000
//
// 보는 것
//   A 카탈로그  — 분리된 개수 · 번호가 비지 않았는가 · 수치가 말이 되는가
//   B 메시      — 117개가 전부 실제로 열리고 삼각형이 들어 있는가
//   C 썸네일    — 전부 구워졌고 열리는가
//   D 베이스    — 남녀 모델 · Head 본 · 귀를 뺀 머리통이 제대로 측정됐는가
//   E 자동 맞춤 — 114개 전부가 머리에 올라가는가(공중에 뜨거나 파묻히지 않는가)
//   F 저장      — 껐다 켜도 복원되는가 · 고르기 전에는 비어 있는가
//   G 스튜디오  — 실제로 떠서 목록·장착·베이스 전환이 되는가
//   H 기존 자산 — 직원 데이터가 그대로인가 · 셔츠가 임포트되는가
public partial class HairStudioTest : Node
{
    private int _pass, _fail;

    // 8명이 실제로 쓰는 헤어. 고르기 전에는 비어 있고, HairAssetPruner 를 돌린 뒤에는
    // 카탈로그가 이것만 남는다 — 두 상태 모두에서 통과해야 한다.
    private HashSet<string> _chosen = new();
    private bool _pruned;

    public override void _Ready()
    {
        HairCatalog cat = HairCatalog.Load();
        var store = HairAssignmentStore.Load();
        _chosen = HairAssignmentStore.Characters
            .Where(c => store.HasChoice(c.Id))
            .Select(c => store.Get(c.Id).HairId)
            .ToHashSet();
        _pruned = _chosen.Count > 0 && cat.Entries.Count <= _chosen.Count;

        SectionA(cat);
        SectionB(cat);
        SectionC(cat);
        SectionD(cat);
        SectionE(cat);
        SectionF();
        SectionG();
        SectionH();
        SectionI(cat);

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 카탈로그 ─────────────────────────────────────────────────────

    private void SectionA(HairCatalog cat)
    {
        GD.Print("\n#### A. 카탈로그");
        Ok(cat.Entries.Count > 0, $"카탈로그를 읽었다 ({cat.Entries.Count}개)");
        Ok(cat.Bases.Count == 2, $"베이스 2개 (실제 {cat.Bases.Count})");

        var male = cat.Hairs("male").ToList();
        var female = cat.Hairs("female").ToList();
        var beard = cat.Entries.Where(e => e.Kind == "beard").ToList();
        GD.Print($"      남성 {male.Count} · 여성 {female.Count} · 수염 {beard.Count}" +
                 (_pruned ? "  (고른 것만 남기고 정리된 상태)" : "  (팩 전체)"));

        if (_pruned)
        {
            // 정리한 뒤에는 "고른 것만 남아 있는가" 가 조건이다. 번호는 당연히 띄엄띄엄하다.
            Ok(cat.Entries.Count == _chosen.Count,
                $"고른 헤어 {_chosen.Count}개만 남아 있다 (카탈로그 {cat.Entries.Count}개)");
            Ok(_chosen.All(id => cat.ById(id) != null),
                "8명이 쓰는 헤어가 전부 카탈로그에 있다 (" + string.Join(", ", _chosen.OrderBy(x => x)) + ")");
        }
        else
        {
            Ok(male.Count >= 5, $"남성 헤어가 분리됐다 ({male.Count}개)");
            Ok(female.Count >= 50, $"여성 헤어가 분리됐다 ({female.Count}개)");
            // 번호는 실제로 분리된 개수에 맞춰야 한다 — 중간이 비면 안 된다.
            Ok(male.Select(e => e.Id).SequenceEqual(Enumerable.Range(1, male.Count).Select(i => $"M-{i:00}")),
                "남성 번호가 M-01 부터 빈칸 없이 이어진다");
            Ok(female.Select(e => e.Id).SequenceEqual(Enumerable.Range(1, female.Count).Select(i => $"F-{i:000}")),
                "여성 번호가 F-001 부터 빈칸 없이 이어진다");
        }
        Ok(cat.Entries.Select(e => e.Id).Distinct().Count() == cat.Entries.Count, "번호가 겹치지 않는다");

        int odd = 0, capBad = 0, named = 0;
        foreach (HairEntry e in cat.Entries)
        {
            if (e.Size.Y < 0.03f || e.Size.Y > 1.0f || e.Size.X < 0.02f) odd++;
            if (e.CapSize.X > e.Size.X + 0.0001f || e.CapSize.Y > e.Size.Y + 0.0001f) capBad++;
            if (!string.IsNullOrEmpty(e.Name) && !string.IsNullOrEmpty(e.SourceNode)) named++;
        }
        Ok(odd == 0, $"크기가 말이 되는 범위다 (벗어난 것 {odd}개)");
        Ok(capBad == 0, $"정수리 뚜껑이 전체보다 크지 않다 (어긋난 것 {capBad}개)");
        Ok(named == cat.Entries.Count, "전부 이름과 원본 출처를 들고 있다");

        var byLen = cat.Entries.Where(e => e.Kind == "hair").GroupBy(e => e.LengthClass)
            .ToDictionary(g => g.Key, g => g.Count());
        GD.Print($"      길이 분류 — " + string.Join(" · ",
            byLen.Select(k => $"{HairSourceSplitter.Korean(k.Key)} {k.Value}")));
        Ok(byLen.Count >= (_pruned ? 1 : 2), "길이 분류가 붙어 있다");

        if (!_pruned)
        {
            var longest = cat.Hairs("female").OrderByDescending(e => e.AreaRatio)
                .Where(e => e.LengthClass == "long").Take(5).ToList();
            Ok(longest.Count == 5, "'긴·풍성' 필터에 걸리는 후보가 있다 (" +
                string.Join(", ", longest.Select(e => $"{e.Id} {e.AreaRatio:F2}")) + ")");
        }
    }

    // ── B. 메시 파일 ────────────────────────────────────────────────────

    private void SectionB(HairCatalog cat)
    {
        GD.Print("\n#### B. 분리된 메시");
        int missing = 0, empty = 0, tris = 0;
        foreach (HairEntry e in cat.Entries)
        {
            if (!ResourceLoader.Exists(e.MeshPath)) { missing++; continue; }
            var m = ResourceLoader.Load<Mesh>(e.MeshPath);
            if (m == null || m.GetSurfaceCount() == 0) { empty++; continue; }
            var idx = m.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Index].As<int[]>();
            int t = (idx?.Length ?? 0) / 3;
            if (t == 0) { empty++; continue; }
            tris += t;
            if (t != e.Tris) empty++;
        }
        Ok(missing == 0, $"메시 파일이 전부 있다 (없는 것 {missing}개)");
        Ok(empty == 0, $"전부 열리고 카탈로그의 삼각형 수와 맞는다 (어긋난 것 {empty}개)");
        GD.Print($"      전체 삼각형 {tris:N0}개");

        // 원본 파일은 건드리지 않았는가.
        Ok(FileAccess.FileExists(HairSourceSplitter.FemaleObj), "원본 OBJ 가 그대로 있다");
        Ok(FileAccess.FileExists(HairSourceSplitter.MaleFbx), "원본 FBX 가 그대로 있다");
    }

    // ── C. 썸네일 ───────────────────────────────────────────────────────

    private void SectionC(HairCatalog cat)
    {
        GD.Print("\n#### C. 썸네일");
        int missing = 0, broken = 0;
        foreach (HairEntry e in cat.Entries)
        {
            string p = $"{HairThumbnailBaker.OutDir}/{e.Id}.png";
            if (!FileAccess.FileExists(p)) { missing++; continue; }
            var tex = ResourceLoader.Exists(p) ? ResourceLoader.Load<Texture2D>(p) : null;
            int w = tex?.GetWidth() ?? Image.LoadFromFile(p)?.GetWidth() ?? 0;
            if (w < 64) broken++;
        }
        Ok(missing == 0, $"헤어마다 썸네일이 있다 (없는 것 {missing}개 / 전체 {cat.Entries.Count}개)");
        Ok(broken == 0, $"썸네일이 전부 열린다 (깨진 것 {broken}개)");
    }

    // ── D. 베이스 ───────────────────────────────────────────────────────

    private void SectionD(HairCatalog cat)
    {
        GD.Print("\n#### D. 베이스 몸체");
        foreach (string sex in new[] { "male", "female" })
        {
            HairBaseEntry b = cat.Base(sex);
            if (b == null) { Ok(false, $"{sex} 베이스가 카탈로그에 없다"); continue; }

            var packed = ResourceLoader.Load<PackedScene>(b.ScenePath);
            Ok(packed != null, $"{sex} 베이스 모델을 불러온다");
            if (packed == null) continue;
            var root = packed.Instantiate<Node3D>();
            Skeleton3D skel = HairSourceSplitter.FindSkeleton(root);
            Ok(skel != null, $"{sex} 에 Skeleton3D 가 있다");
            Ok(skel != null && skel.FindBone(b.HeadBone) >= 0, $"{sex} 에 '{b.HeadBone}' 본이 있다");
            Ok(b.BodyHeight > 1.5f && b.BodyHeight < 2.1f, $"{sex} 키가 사람 범위다 ({b.BodyHeight:F3}m)");
            Ok(b.SkullSize.X > 0.1f && b.SkullSize.X < 0.3f, $"{sex} 머리통 폭 {b.SkullSize.X:F3}m");
            Ok(b.CraniumSize.Y < b.SkullSize.Y,
                $"{sex} 귀 위쪽은 머리통보다 낮게 잡혔다 ({b.CraniumSize.Y:F3} < {b.SkullSize.Y:F3})");
            root.QueueFree();
        }
    }

    // ── E. 자동 맞춤 ────────────────────────────────────────────────────

    private void SectionE(HairCatalog cat)
    {
        GD.Print("\n#### E. 자동 맞춤 — 114개 전부 머리에 올라가는가");
        foreach (string sex in new[] { "male", "female" })
        {
            HairBaseEntry b = cat.Base(sex);
            if (b == null) continue;
            var packed = ResourceLoader.Load<PackedScene>(b.ScenePath);
            var root = packed.Instantiate<Node3D>();
            Skeleton3D skel = HairSourceSplitter.FindSkeleton(root);
            Transform3D rest = skel.GetBoneGlobalRest(skel.FindBone(b.HeadBone));

            float skullTop = b.SkullCenter.Y + b.SkullSize.Y * 0.5f;
            int tooHigh = 0, tooLow = 0, offCenter = 0, clamped = 0;
            var worst = new List<string>();

            foreach (HairEntry e in cat.Hairs(sex))
            {
                HairFit.Auto(e, b, rest, HairCatalog.PackYaw(e.SourceFile),
                             out Vector3 pos, out Vector3 rot, out float scale);
                if (scale <= HairFit.MinScale + 0.001f || scale >= HairFit.MaxScale - 0.001f) clamped++;

                // 머리 본 기준 변환을 다시 뼈대 공간으로 풀어 정수리 높이를 본다.
                Transform3D world = rest * HairFit.Compose(pos, rot, Vector3.One * scale);
                float crownTop = (world * new Vector3(0f, e.CapCenter.Y + e.CapSize.Y * 0.5f, 0f)).Y;
                float dy = crownTop - skullTop;
                if (dy > 0.02f) { tooHigh++; worst.Add($"{e.Id}+{dy * 100f:F1}cm"); }
                if (dy < -0.02f) { tooLow++; worst.Add($"{e.Id}{dy * 100f:F1}cm"); }

                Vector3 mid = world.Origin;
                if (Mathf.Abs(mid.X - b.CraniumCenter.X) > 0.12f ||
                    Mathf.Abs(mid.Z - b.CraniumCenter.Z) > 0.18f) offCenter++;
            }
            int n = cat.Hairs(sex).Count();
            Ok(tooHigh == 0 && tooLow == 0,
                $"{sex} {n}개의 정수리가 머리 꼭대기에 2cm 안으로 붙는다 " +
                $"(뜬 것 {tooHigh} · 파묻힌 것 {tooLow}{(worst.Count > 0 ? " — " + string.Join(", ", worst.Take(5)) : "")})");
            Ok(offCenter == 0, $"{sex} 전부 머리 중심 위에 있다 (벗어난 것 {offCenter}개)");
            Ok(clamped <= 2, $"{sex} 배율이 한계까지 밀린 것이 거의 없다 ({clamped}개)");
            root.QueueFree();
        }
    }

    // ── F. 저장 ─────────────────────────────────────────────────────────

    private void SectionF()
    {
        GD.Print("\n#### F. 선택 결과 저장");
        const string tmp = "user://hair_assignments_test.json";
        if (FileAccess.FileExists(tmp)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(tmp));

        var fresh = HairAssignmentStore.Load(tmp);
        Ok(HairAssignmentStore.Characters.All(c => !fresh.HasChoice(c.Id)),
            "고르기 전에는 아무 캐릭터도 확정돼 있지 않다");
        Ok(HairAssignmentStore.Characters.Length == 8, "캐릭터가 8명이다");

        HairAssignment a = fresh.Get("sheep");
        a.Body = "female"; a.HairId = "F-004";
        a.HairPath = "res://assets/characters/hair/female/F-004.res";
        a.ColorHex = "#684A39"; a.ColorName = "갈색";
        a.Position = new Vector3(0.001f, 0.12f, -0.004f);
        a.RotationDeg = new Vector3(0f, 0f, 2f);
        a.Scale = Vector3.One * 0.93f;
        Ok(fresh.Save(tmp) == Error.Ok, "저장된다");

        var back = HairAssignmentStore.Load(tmp);
        HairAssignment r = back.Get("sheep");
        Ok(r.HairId == "F-004" && r.Body == "female", "껐다 켜도 헤어와 베이스가 그대로다");
        Ok(r.ColorHex == "#684A39" && r.ColorName == "갈색", "색이 그대로다");
        Ok(r.Position.IsEqualApprox(new Vector3(0.001f, 0.12f, -0.004f)), "위치가 그대로다");
        Ok(r.RotationDeg.IsEqualApprox(new Vector3(0f, 0f, 2f)), "회전이 그대로다");
        Ok(Mathf.IsEqualApprox(r.Scale.X, 0.93f), "크기가 그대로다");
        Ok(back.HasChoice("sheep") && !back.HasChoice("wolf"), "고른 캐릭터만 확정으로 센다");
        Ok(!string.IsNullOrEmpty(r.HairPath) && r.HairPath.StartsWith("res://"),
            "헤어 리소스 경로가 프로젝트 경로로 남는다");

        Ok(HairAssignmentStore.ResPath.StartsWith("res://assets/characters/hair"),
            $"평소 저장 위치가 프로젝트 안이다 ({HairAssignmentStore.ResPath})");
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(tmp));
    }

    // ── G. 스튜디오 ─────────────────────────────────────────────────────

    private void SectionG()
    {
        GD.Print("\n#### G. 스튜디오 동작");
        var packed = ResourceLoader.Load<PackedScene>("res://scenes/tools/CharacterHairStudio.tscn");
        Ok(packed != null, "스튜디오 씬이 있다");
        if (packed == null) return;

        var studio = packed.Instantiate<CharacterHairStudio>();
        AddChild(studio);
        Ok(studio.Catalog.Entries.Count > 0, "스튜디오가 카탈로그를 읽었다");

        // 열자마자 관리자 자리에서, **저장해 둔 것이 있으면 그대로** 복원돼야 한다.
        // (저장 전이면 둘 다 빈 값이라 같은 조건으로 통과한다.)
        HairAssignment saved = studio.Store.Get("admin");
        Ok(studio.CurrentBody == saved.Body && studio.CurrentHairId == saved.HairId,
            $"관리자 자리로 열리고 저장된 선택을 복원한다 " +
            $"({saved.Body}/{(saved.HairId == "" ? "미선택" : saved.HairId)})");

        // 남성 베이스에서도 여성 팩을 고를 수 있어야 한다 — 남성 팩이 9개뿐이라
        // 그것만으로는 고를 거리가 없다.
        int maleListed = studio.ListedCount;
        int all = studio.Catalog.Entries.Count(e => e.Kind == "hair");
        Ok(maleListed == all, $"남성 베이스에 남녀 헤어가 모두 올라온다 ({maleListed}/{all}개)");
        int maleOwn = studio.Catalog.Hairs("male").Count();
        Ok(maleOwn == 0 || studio.ListedIds.Take(maleOwn).All(id => id.StartsWith("M-")),
            "남성 베이스에서는 남성 팩이 앞에 온다");
        Ok(studio.ListedIds.Any(id => id.StartsWith("F-")),
            "남성 베이스에서도 여성 팩을 고를 수 있다");

        // 목록에 실제로 있는 것으로 고른다 — 정리된 카탈로그에서도 돌아야 한다.
        string anyMale = studio.ListedIds.FirstOrDefault(id => id.StartsWith("M-")) ?? studio.ListedIds.First();
        studio.PickForTest("admin", "male", anyMale);
        Ok(studio.CurrentHairId == anyMale, $"헤어를 고르면 선택이 바뀐다 ({anyMale})");
        Ok(studio.HairNode is { Visible: true, Mesh: not null }, "고른 헤어가 실제로 장착된다");
        Ok(studio.HairNode.GetParent() is BoneAttachment3D ba && ba.BoneName == "Head",
            "머리 본(BoneAttachment3D 'Head') 아래에 붙는다");
        Ok(studio.CurrentScale > 0.2f, $"자동 맞춤 배율이 들어간다 ({studio.CurrentScale:F3})");
        Vector3 fitted = studio.CurrentPos;

        string anyFemale = studio.Catalog.Hairs("female").Select(e => e.Id).FirstOrDefault(id => id != anyMale);
        studio.PickForTest("sheep", "female", anyFemale);
        Ok(studio.CurrentBody == "female", "양을 고르면 베이스가 데이터대로 여성이 된다");
        Ok(studio.ListedCount == all, $"여성 베이스에서도 전부 보인다 ({studio.ListedCount}/{all}개)");
        Ok(studio.ListedIds.First().StartsWith("F-"), "여성 베이스에서는 여성 팩이 앞에 온다");
        Ok(studio.CurrentHairId == anyFemale && studio.HairNode.Visible,
            $"여성 헤어가 장착된다 ({anyFemale})");
        Ok(!studio.CurrentPos.IsEqualApprox(fitted), "헤어마다 맞춤값이 다르게 나온다");

        // 장면에는 고른 헤어 하나만 올라가야 한다 — 수십 개를 동시에 올리지 않는다.
        int meshes = CountMeshes(studio.BaseSkeleton);
        Ok(meshes <= 5, $"3D 장면에 올라간 메시가 적다 ({meshes}개 — 몸·눈·눈썹 + 헤어 1개)");

        RemoveChild(studio);
        studio.QueueFree();
    }

    private static int CountMeshes(Node n)
    {
        if (n == null) return 0;
        int c = n is MeshInstance3D ? 1 : 0;
        foreach (Node k in n.GetChildren()) c += CountMeshes(k);
        return c;
    }

    // ── H. 기존 자산 ────────────────────────────────────────────────────

    private void SectionH()
    {
        GD.Print("\n#### H. 기존 자산 · 다음 단계 준비물");
        var expect = new Dictionary<string, string>
        {
            ["rabbit"] = "토끼", ["cat"] = "고양이", ["fox"] = "여우",
            ["sheep"] = "양", ["wolf"] = "늑대", ["dog"] = "강아지",
        };
        int kept = 0, bodyOk = 0;
        foreach ((string id, string codename) in expect)
        {
            EmployeeDef def = HairAssignmentStore.LoadDef(id);
            if (def != null && def.Codename == codename) kept++;
            string body = HairAssignmentStore.DefaultBody(id);
            bool wantFemale = def != null && def.Gender.Contains("여");
            if (body == (wantFemale ? "female" : "male")) bodyOk++;
        }
        Ok(kept == expect.Count, $"직원 6명의 ID·코드네임이 게임 데이터 그대로다 ({kept}/6)");
        Ok(bodyOk == expect.Count, $"초기 베이스가 데이터의 성별을 따른다 ({bodyOk}/6)");
        Ok(HairAssignmentStore.Characters.Any(c => c.Id == "admin") &&
           HairAssignmentStore.Characters.Any(c => c.Id == "director"),
            "관리자·총괄관리자도 목록에 있다");

        // 관리자 의상은 이번에 입히지 않는다 — 임포트가 되는지만 본다(지시서 §1).
        const string shirt = "res://assets/characters/source/admin_outfit/Shirt OBJ.obj";
        Ok(ResourceLoader.Exists(shirt), "관리자 셔츠 OBJ 가 프로젝트에 들어왔다");
        if (ResourceLoader.Exists(shirt))
        {
            var m = ResourceLoader.Load<Mesh>(shirt);
            int surf = m?.GetSurfaceCount() ?? 0;
            Ok(surf > 0, $"셔츠가 메시로 임포트된다 (표면 {surf}개 — 질감은 다음 단계)");
        }
        Ok(HairCatalog.Load().Entries.All(e => !e.MeshPath.Contains("source/")),
            "분리본은 원본 폴더 밖에 따로 쓴다");
    }

    // ── I. 8명의 최종 선택 ──────────────────────────────────────────────
    //
    // 고르지 않은 헤어를 지운 뒤에도 8명이 쓰는 것은 전부 멀쩡해야 한다.
    // 디스크에 남은 메시와 카탈로그가 정확히 일치해야 한다 — 한쪽만 지워지면 안 된다.

    private void SectionI(HairCatalog cat)
    {
        GD.Print("\n#### I. 8명의 최종 선택");
        var store = HairAssignmentStore.Load();
        int ready = 0;
        foreach ((string id, _, _) in HairAssignmentStore.Characters)
        {
            string label = HairAssignmentStore.Label(id);
            if (!store.HasChoice(id)) { Ok(false, $"{label} — 아직 안 골랐다"); continue; }
            HairAssignment a = store.Get(id);
            HairEntry e = cat.ById(a.HairId);
            var mesh = ResourceLoader.Exists(a.HairPath) ? ResourceLoader.Load<Mesh>(a.HairPath) : null;
            bool good = e != null && mesh is { } m && m.GetSurfaceCount() > 0;
            if (good) ready++;
            Ok(good, $"{label} — {a.HairId} ({a.Body}, {a.ColorHex}) 메시가 살아 있다");
        }
        Ok(ready == 8, $"8명 전원이 쓸 수 있는 상태다 ({ready}/8)");

        // 지우다 만 것이 없는가 — 디스크 ↔ 카탈로그.
        var onDisk = new List<string>();
        foreach (string sub in new[] { "male", "female", "beard" })
        {
            string dir = $"{HairSourceSplitter.OutRoot}/{sub}";
            if (!DirAccess.DirExistsAbsolute(dir)) continue;
            foreach (string f in DirAccess.GetFilesAt(dir))
                if (f.EndsWith(".res")) onDisk.Add($"{dir}/{f}");
        }
        var inCat = cat.Entries.Select(e => e.MeshPath).ToHashSet();
        var orphan = onDisk.Where(p => !inCat.Contains(p)).ToList();
        var ghost = inCat.Where(p => !onDisk.Contains(p)).ToList();
        Ok(orphan.Count == 0, $"카탈로그에 없는 메시가 남아 있지 않다 ({orphan.Count}개)");
        Ok(ghost.Count == 0, $"카탈로그가 없는 파일을 가리키지 않는다 ({ghost.Count}개)");
        GD.Print($"      디스크 {onDisk.Count}개 · 카탈로그 {cat.Entries.Count}개");
    }

    // ── ─────────────────────────────────────────────────────────────────

    private void Ok(bool cond, string what)
    {
        if (cond) { _pass++; GD.Print($"  [PASS] {what}"); }
        else { _fail++; GD.Print($"  [FAIL] {what}"); }
    }
}
