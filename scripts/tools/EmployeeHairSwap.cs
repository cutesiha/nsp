using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace NSP.Tools;

// 기존 직원 3D 모델의 **머리카락만** 새로 고른 것으로 갈아 끼운다.
//
//   godot --headless --path . res://scenes/tools/EmployeeHairSwap.tscn --quit-after 300        (미리보기)
//   godot --headless --path . res://scenes/tools/EmployeeHairSwap.tscn -- apply --quit-after 300  (실제 적용)
//
// 몸·가면·옷·색은 건드리지 않는다. HairAnchor 아래의 기존 헤어 조각(HairCap/HairBack/…)만
// 지우고, 헤어 스튜디오에서 고른 메시 하나를 그 자리에 넣는다.
//
// 위치·크기는 둘 중 하나로 정해진다.
//
//   ① 스튜디오가 **게임 모델 위에서** 맞춘 값이 있으면 그대로 쓴다(fit: "model").
//      스튜디오가 그 캐릭터의 .tscn 을 그대로 올려 같은 자리(Head/HairAnchor)에
//      붙여 보여 주므로, 거기서 본 그대로가 여기서 들어간다.
//   ② 그런 값이 없으면(옛 저장본 — 헤어팩 두상 기준) 이 머리 크기에 맞춰 자동으로
//      계산하고 180° 돌린다. 머리 구는 지름 0.26m 이고 얼굴이 -Z 를 보는데,
//      헤어는 사람 두상(0.18m)에 +Z 를 보도록 맞춰져 있기 때문이다.
public partial class EmployeeHairSwap : Node
{
    private const string SceneDir = "res://scenes/cctv_characters/employees";
    private const string HairPath = "VisualRoot/RigRoot/Hips/Torso/Chest/Neck/Head/HairAnchor";

    private static readonly (string Id, string Scene)[] Targets =
    {
        ("rabbit", "RabbitEmployee3D.tscn"),
        ("cat", "CatEmployee3D.tscn"),
        ("fox", "FoxEmployee3D.tscn"),
        ("sheep", "SheepEmployee3D.tscn"),
        ("wolf", "WolfEmployee3D.tscn"),
        ("dog", "DogEmployee3D.tscn"),
    };

    // 머리 구의 몇 배로 헤어 정수리를 맞출지. 눈으로 보고 고른 값이다.
    private const float CapWidthFactor = 0.95f;
    private const float Lift = 0.004f;

    public override void _Ready()
    {
        bool apply = OS.GetCmdlineUserArgs().Contains("apply");
        var cat = HairCatalog.Load();
        var store = HairAssignmentStore.Load();

        GD.Print($"################ 직원 헤어 교체 {(apply ? "(적용)" : "(미리보기 — 파일은 그대로)")}");

        if (!Measure(out Vector3 headCenter, out float headW, out float headTop)) return;
        GD.Print($"  기존 머리: 지름 {headW:F3}m · HairAnchor 기준 중심 y={headCenter.Y:F3} · 정수리 y={headTop:F3}");
        GD.Print($"  얼굴이 -Z 를 보므로 헤어는 180도 돌려 넣는다\n");

        int done = 0;
        foreach ((string id, string file) in Targets)
        {
            HairAssignment a = store.Get(id);
            HairEntry e = cat.ById(a.HairId);
            if (e == null)
            {
                GD.Print($"  !! {id}: 고른 헤어 '{a.HairId}' 가 카탈로그에 없다 — 건너뛴다");
                continue;
            }

            Vector3 pos;
            Vector3 rotDeg;
            float scale;
            string how;

            if (a.TunedOnModel)
            {
                // 스튜디오에서 이 모델을 보며 맞춘 값 — 손대지 않는다.
                pos = a.Position;
                rotDeg = a.RotationDeg;
                scale = a.Scale.X;
                how = "스튜디오";
            }
            else
            {
                scale = e.CapSize.X > 0.0001f
                    ? Mathf.Clamp(headW * CapWidthFactor / e.CapSize.X, 0.3f, 4f) : 1f;
                // 180도 돌렸으므로 정수리 중심의 X·Z 도 같이 뒤집힌다.
                var rot = new Basis(Vector3.Up, Mathf.Pi);
                Vector3 cap = rot * (e.CapCenter * scale);
                float crownTop = (e.CapCenter.Y + e.CapSize.Y * 0.5f) * scale;
                pos = new Vector3(
                    headCenter.X - cap.X,
                    headTop + Lift - crownTop,
                    headCenter.Z - cap.Z);
                rotDeg = new Vector3(0f, 180f, 0f);
                how = "자동";
            }

            // 아래쪽만 늘리기 — 세로로만 키우고 정수리 높이는 그대로 둔다.
            // (늘린 만큼 메시 꼭대기가 올라가므로 그만큼 내려서 상쇄한다.)
            float k = Mathf.Max(0.2f, a.StretchY);
            var size = new Vector3(scale, scale * k, scale);
            if (!Mathf.IsEqualApprox(k, 1f))
                pos.Y -= (e.CapCenter.Y + e.CapSize.Y * 0.5f) * scale * (k - 1f);

            GD.Print($"  {id,-7} {a.HairId}  {a.ColorHex}  배율 {scale:F3}" +
                     (Mathf.IsEqualApprox(k, 1f) ? "" : $"×세로{k:F2}") +
                     $"  위치 ({pos.X:F3}, {pos.Y:F3}, {pos.Z:F3})  " +
                     $"회전 ({rotDeg.X:F0}, {rotDeg.Y:F0}, {rotDeg.Z:F0})  [{how}]");

            if (apply && Patch($"{SceneDir}/{file}", id, e, a,
                               HairFit.Compose(pos, rotDeg, size))) done++;
        }

        if (apply) GD.Print($"\n  {done}/{Targets.Length}개 씬을 고쳤다");
        else GD.Print("\n  실제로 쓰려면 끝에 ' -- apply' 를 붙여 다시 돌려라");
        GetTree().Quit();
    }

    // 기존 베이스에서 머리 구와 HairAnchor 의 관계를 **실측** 한다. 숫자를 코드에 박지 않는다.
    private bool Measure(out Vector3 center, out float width, out float top)
    {
        center = Vector3.Zero; width = 0.26f; top = 0.13f;
        var packed = ResourceLoader.Load<PackedScene>("res://scenes/cctv_characters/EmployeeCctvBase.tscn");
        if (packed == null) { GD.Print("  !! 베이스 씬을 열지 못했다"); return false; }
        var root = packed.Instantiate<Node3D>();
        AddChild(root);   // GlobalTransform 은 트리 안에 있어야 읽힌다

        var anchor = root.GetNodeOrNull<Node3D>(HairPath);
        var head = root.GetNodeOrNull<MeshInstance3D>(
            "VisualRoot/RigRoot/Hips/Torso/Chest/Neck/Head/HeadMesh");
        if (anchor == null || head?.Mesh == null)
        { GD.Print("  !! 머리를 찾지 못했다"); RemoveChild(root); root.QueueFree(); return false; }

        Aabb box = head.Mesh.GetAabb();
        // 머리 메시의 상자를 HairAnchor 기준으로 옮긴다.
        Transform3D toAnchor = anchor.GlobalTransform.AffineInverse() * head.GlobalTransform;
        Aabb local = toAnchor * box;
        center = local.GetCenter();
        width = local.Size.X;
        top = local.End.Y;
        RemoveChild(root);
        root.QueueFree();
        return true;
    }

    // ── .tscn 고치기 ────────────────────────────────────────────────────

    private static bool Patch(string scenePath, string id, HairEntry e, HairAssignment a,
                              Transform3D xform)
    {
        using var f = FileAccess.Open(scenePath, FileAccess.ModeFlags.Read);
        if (f == null) { GD.Print($"  !! {scenePath} 를 열지 못했다"); return false; }
        var lines = f.GetAsText().Split('\n').ToList();
        f.Dispose();

        // ① 기존 헤어 노드 블록을 지운다 (HairAnchor 바로 아래 것만).
        var keep = new List<string>();
        bool dropping = false;
        int dropped = 0;
        foreach (string line in lines)
        {
            if (line.StartsWith("["))
            {
                bool isHairNode = line.StartsWith("[node ")
                                  && line.Contains($"parent=\"{HairPath}\"");
                if (isHairNode) { dropping = true; dropped++; continue; }
                dropping = false;
            }
            if (!dropping) keep.Add(line);
        }

        // ② 메시를 ext_resource 로, 색을 sub_resource 로 선언한다.
        string meshId = "hair_new";
        string matId = "HairMatNew";
        int lastExt = keep.FindLastIndex(l => l.StartsWith("[ext_resource "));
        if (lastExt < 0) { GD.Print($"  !! {scenePath}: ext_resource 가 없다"); return false; }
        keep.Insert(lastExt + 1, $"[ext_resource type=\"ArrayMesh\" path=\"{e.MeshPath}\" id=\"{meshId}\"]");

        Color c = Color.FromString(a.ColorHex, Colors.Black);
        var mat = new StringBuilder();
        mat.AppendLine($"[sub_resource type=\"StandardMaterial3D\" id=\"{matId}\"]");
        mat.AppendLine($"albedo_color = Color({c.R:F3}, {c.G:F3}, {c.B:F3}, 1)");
        mat.AppendLine("roughness = 0.72");
        mat.AppendLine("metallic = 0.0");
        mat.AppendLine("cull_mode = 2");   // 머리카락은 얇은 면이라 양면으로 그린다
        int lastSub = keep.FindLastIndex(l => l.StartsWith("[sub_resource "));
        int insertAt = lastSub >= 0 ? NextBlock(keep, lastSub) : lastExt + 2;
        keep.Insert(insertAt, mat.ToString().TrimEnd('\n', '\r'));

        // ③ 새 헤어 노드 한 개를 넣는다.
        Basis b = xform.Basis;
        Vector3 pos = xform.Origin;
        var node = new StringBuilder();
        node.AppendLine();
        node.AppendLine($"[node name=\"Hair\" type=\"MeshInstance3D\" parent=\"{HairPath}\" index=\"0\"]");
        node.AppendLine($"transform = Transform3D({F(b.X.X)}, {F(b.X.Y)}, {F(b.X.Z)}, " +
                        $"{F(b.Y.X)}, {F(b.Y.Y)}, {F(b.Y.Z)}, " +
                        $"{F(b.Z.X)}, {F(b.Z.Y)}, {F(b.Z.Z)}, " +
                        $"{F(pos.X)}, {F(pos.Y)}, {F(pos.Z)})");
        node.AppendLine($"material_override = SubResource(\"{matId}\")");
        node.AppendLine($"mesh = ExtResource(\"{meshId}\")");
        keep.Add(node.ToString().TrimEnd('\n', '\r'));

        // ④ load_steps 를 늘린다 — 리소스를 둘 더 썼다.
        int head0 = keep.FindIndex(l => l.StartsWith("[gd_scene"));
        if (head0 >= 0)
        {
            string h = keep[head0];
            int at = h.IndexOf("load_steps=", StringComparison.Ordinal);
            if (at >= 0)
            {
                int from = at + "load_steps=".Length;
                int to = from;
                while (to < h.Length && char.IsDigit(h[to])) to++;
                if (int.TryParse(h[from..to], out int steps))
                    keep[head0] = h[..from] + (steps + 2) + h[to..];
            }
        }

        using var w = FileAccess.Open(scenePath, FileAccess.ModeFlags.Write);
        if (w == null) { GD.Print($"  !! {scenePath} 에 쓰지 못했다"); return false; }
        w.StoreString(string.Join("\n", keep));
        GD.Print($"          → 기존 헤어 {dropped}개 제거 · {e.Id} 하나로 교체");
        return true;
    }

    private static int NextBlock(List<string> lines, int from)
    {
        for (int i = from + 1; i < lines.Count; i++)
            if (lines[i].StartsWith("[")) return i;
        return lines.Count;
    }

    private static string F(float v) => Mathf.Snapped(v, 0.000001f).ToString("0.######");
}
