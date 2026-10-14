using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace NSP.Tools;

// 원본 헤어팩을 **헤어 하나 = 파일 하나** 로 쪼갠다.
//
// Blender 가 설치돼 있지 않아서(확인함) GLB 변환 경로는 쓰지 못한다. 대신
//   · 남성 FBX  — Godot 자체 임포터(ufbx)가 읽은 씬을 노드·표면 단위로 훑는다
//   · 여성 OBJ  — 122MB 를 Godot 임포터에 맡기면 통째로 한 덩어리가 되므로 폴더를
//                 .gdignore 하고 직접 스트리밍 파싱한다
// 둘 다 **원본 파일은 건드리지 않는다**. 결과만 res:// 아래에 새로 쓴다.
//
// 지시서는 "그룹 하나가 반드시 헤어스타일 하나는 아니다" 라고 경고했다. 그래서 쪼갠 뒤
// 기하 지문으로 중복을 찾아내고(같은 메시가 위치만 옮겨 두 번 들어 있는 경우), 재질
// 이름으로 수염을 가려낸다. 어림짐작이 아니라 실제 정점을 비교한 결과다.
public static class HairSourceSplitter
{
    public const string MaleFbx = "res://assets/characters/source/male_hair/hair.fbx";
    public const string FemaleObj = "res://assets/characters/source/female_hair/nv+toufa.obj";
    public const string MaleBaseGltf = "res://assets/characters/source/male_base/Superhero_Male_FullBody.gltf";
    public const string FemaleBaseGltf = "res://assets/characters/source/female_base/Superhero_Female_FullBody.gltf";

    public const string OutRoot = "res://assets/characters/hair";

    // OBJ 헤더가 "centimeters" 라고 적어 뒀다. 미터로 내린다.
    private const float ObjToMeters = 0.01f;

    // 머리통에 씌워지는 "윗부분"(정수리 뚜껑)의 두께. **높이의 비율이 아니라 절대값**이다.
    //
    // 비율로 잡으면 길이에 따라 재는 부위가 달라진다 — 22cm 짜리 짧은 머리는 정수리만
    // 잡히는데 50cm 짜리 긴 머리는 턱 아래까지 잡혀서 폭이 20cm 넘게 부풀고, 그 값으로
    // 크기를 맞추면 긴 머리가 머리통보다 작아진다. 꼭대기에서 일정 깊이만 본다.
    private const float CapDepth = 0.08f;

    // ── 남성 FBX ────────────────────────────────────────────────────────

    public static List<HairEntry> SplitMalePack(List<string> notes, Action<string> log)
    {
        var made = new List<HairEntry>();
        var packed = ResourceLoader.Load<PackedScene>(MaleFbx);
        if (packed == null) { notes.Add($"{MaleFbx} 를 불러오지 못했다"); return made; }
        var root = packed.Instantiate<Node3D>();

        // 팩 자체 번호(노드 이름이 "1".."10")대로 훑는다 — 원본 순서를 보존한다.
        var nodes = root.GetChildren().OfType<MeshInstance3D>()
            .Where(m => m.Mesh != null)
            .OrderBy(m => int.TryParse(m.Name.ToString(), out int i) ? i : 9999)
            .ThenBy(m => m.Name.ToString())
            .ToList();

        var raws = new List<Raw>();
        foreach (MeshInstance3D mi in nodes)
        {
            Transform3D t = mi.Transform;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                Raw r = Bake(mi.Mesh, s, t);
                if (r == null) continue;
                r.Node = mi.Name.ToString();
                r.Surface = s;
                r.Material = mi.Mesh.SurfaceGetMaterial(s)?.ResourceName ?? "";
                r.IsBeard = r.Material.ToLower().Contains("beard");
                raws.Add(r);
            }
        }
        root.QueueFree();

        MarkDuplicates(raws, notes, log);

        // 번호는 **실제로 분리된 개수** 에 맞춘다. 복제본은 번호를 먹지 않는다.
        int hair = 0, beard = 0;
        foreach (Raw r in raws)
        {
            if (r.TwinOf != null)
            {
                notes.Add($"FBX 노드 '{r.Node}' 표면 {r.Surface}(재질 '{r.Material}')은 " +
                          $"{r.TwinOf.Id} 과 같은 메시다 — 정점 {r.Verts.Count}개가 정렬 후 1mm " +
                          "안에서 일치한다(팩 안에 위치만 바꿔 두 번 들어 있다). 번호를 주지 않았다");
                continue;
            }
            string id = r.IsBeard ? $"MB-{++beard:00}" : $"M-{++hair:00}";
            r.Id = id;
            made.Add(Finish(r, id, "male", MaleFbx, r.IsBeard ? "beard" : "hair", log));
        }
        log($"남성 팩: 노드 {nodes.Count}개 · 표면 {raws.Count}개 → " +
            $"헤어 {hair}개 · 수염 {beard}개 (복제본 {raws.Count(r => r.TwinOf != null)}개 제외)");
        return made;
    }

    // ── 여성 OBJ ────────────────────────────────────────────────────────

    public static List<HairEntry> SplitFemalePack(List<string> notes, Action<string> log)
    {
        var made = new List<HairEntry>();
        string abs = ProjectSettings.GlobalizePath(FemaleObj);
        if (!File.Exists(abs)) { notes.Add($"{FemaleObj} 가 없다"); return made; }

        List<Raw> raws = ReadObjGroups(abs, log);
        log($"여성 팩: 이름 있는 그룹 {raws.Count}개를 읽었다");

        // 격자로 늘어놓은 팩이다. 같은 칸에 두 그룹이 걸치면 "한 헤어가 여러 메시" 라는
        // 뜻이므로 합친다 — 실제로 겹치는지 보고 판단한다.
        List<Raw> merged = MergeOverlapping(raws, notes, log);

        // 아래 줄부터 왼쪽→오른쪽으로 번호를 준다. 팩 안에서 눈으로 찾기 쉬운 순서다.
        var ordered = merged
            .OrderBy(r => Mathf.RoundToInt(r.PackCenter.Y / 40f))
            .ThenBy(r => r.PackCenter.X)
            .ToList();

        MarkDuplicates(ordered, notes, log);

        int n = 0;
        foreach (Raw r in ordered)
        {
            if (r.TwinOf != null)
            {
                notes.Add($"OBJ 그룹 '{r.Node}' 은 {r.TwinOf.Id} 과 같은 메시다 — " +
                          "격자 칸만 다르다. 번호를 주지 않았다");
                continue;
            }
            string id = $"F-{++n:000}";
            r.Id = id;
            made.Add(Finish(r, id, "female", FemaleObj, "hair", log));
        }
        log($"여성 팩: 헤어 {made.Count}개 (복제본 {ordered.Count(r => r.TwinOf != null)}개 제외)");
        return made;
    }

    // 스트리밍 파싱. v/vt/vn 은 파일 전체에서 1부터 이어지는 색인이므로, 객체가 끝날 때마다
    // 지금까지 읽은 개수를 기준점으로 밀어 둔다. 메모리에는 **현재 객체 하나만** 올린다.
    private static List<Raw> ReadObjGroups(string abs, Action<string> log)
    {
        var done = new List<Raw>();
        var vs = new List<Vector3>(); var ts = new List<Vector2>(); var ns = new List<Vector3>();
        int baseV = 0, baseT = 0, baseN = 0;
        string cur = null;
        var faces = new List<int[]>();
        CultureInfo ci = CultureInfo.InvariantCulture;
        long lines = 0;

        void Flush()
        {
            if (cur != null && faces.Count > 0)
                done.Add(FromObj(cur, vs, ts, ns, faces, baseV, baseT, baseN));
            baseV += vs.Count; baseT += ts.Count; baseN += ns.Count;
            vs.Clear(); ts.Clear(); ns.Clear(); faces.Clear(); cur = null;
        }

        foreach (string raw in File.ReadLines(abs))
        {
            if (++lines % 1000000 == 0) log($"  ... {lines / 1000000}00만 줄");
            if (raw.Length < 2) continue;
            char c0 = raw[0];
            if (c0 == 'v')
            {
                char c1 = raw[1];
                if (c1 == ' ') { Vector3 p = Split3(raw, 2, ci); vs.Add(p); }
                else if (c1 == 't') { Vector3 p = Split3(raw, 3, ci); ts.Add(new Vector2(p.X, p.Y)); }
                else if (c1 == 'n') { Vector3 p = Split3(raw, 3, ci); ns.Add(p); }
            }
            else if (c0 == 'f' && raw[1] == ' ')
            {
                var corners = new List<int>(12);
                int i = 2;
                while (i < raw.Length)
                {
                    while (i < raw.Length && raw[i] == ' ') i++;
                    if (i >= raw.Length) break;
                    int v = 0, t = 0, nn = 0, slot = 0;
                    for (; i < raw.Length && raw[i] != ' '; i++)
                    {
                        char ch = raw[i];
                        if (ch == '/') { slot++; continue; }
                        if (ch < '0' || ch > '9') continue;
                        int d = ch - '0';
                        if (slot == 0) v = v * 10 + d;
                        else if (slot == 1) t = t * 10 + d;
                        else nn = nn * 10 + d;
                    }
                    corners.Add(v); corners.Add(t); corners.Add(nn);
                }
                if (corners.Count >= 9) faces.Add(corners.ToArray());
            }
            else if (c0 == 'g' && raw[1] == ' ')
            {
                string name = raw.Substring(2).Trim();
                if (name == "default") Flush();
                else cur = name;
            }
        }
        Flush();
        return done;
    }

    private static Vector3 Split3(string s, int from, CultureInfo ci)
    {
        float a = 0f, b = 0f, c = 0f;
        int got = 0, i = from;
        while (i < s.Length && got < 3)
        {
            while (i < s.Length && s[i] == ' ') i++;
            int st = i;
            while (i < s.Length && s[i] != ' ') i++;
            if (i <= st) break;
            float f = float.Parse(s.AsSpan(st, i - st), NumberStyles.Float, ci);
            if (got == 0) a = f; else if (got == 1) b = f; else c = f;
            got++;
        }
        return new Vector3(a, b, c);
    }

    // OBJ 한 그룹 → 정점 배열. v/vt/vn 조합이 다르면 다른 정점이다(OBJ 규칙).
    private static Raw FromObj(string name, List<Vector3> vs, List<Vector2> ts, List<Vector3> ns,
                               List<int[]> faces, int baseV, int baseT, int baseN)
    {
        var r = new Raw { Node = name, Surface = 0, Material = "" };
        var map = new Dictionary<long, int>(vs.Count * 2);
        foreach (int[] f in faces)
        {
            int corners = f.Length / 3;
            var local = new int[corners];
            for (int k = 0; k < corners; k++)
            {
                int vi = f[k * 3] - 1 - baseV;
                int ti = f[k * 3 + 1] - 1 - baseT;
                int nni = f[k * 3 + 2] - 1 - baseN;
                if (vi < 0 || vi >= vs.Count) { local[k] = -1; continue; }
                long key = ((long)vi << 42) ^ ((long)(ti + 1) << 21) ^ (uint)(nni + 1);
                if (!map.TryGetValue(key, out int idx))
                {
                    idx = r.Verts.Count;
                    map[key] = idx;
                    r.Verts.Add(vs[vi] * ObjToMeters);
                    r.Norms.Add(nni >= 0 && nni < ns.Count ? ns[nni] : Vector3.Up);
                    r.Uvs.Add(ti >= 0 && ti < ts.Count ? new Vector2(ts[ti].X, 1f - ts[ti].Y) : Vector2.Zero);
                }
                local[k] = idx;
            }
            // 사각형 이상은 부채꼴로 쪼갠다. 감는 방향은 Godot 쪽 앞면 규칙에 맞게 뒤집는다.
            for (int k = 1; k + 1 < corners; k++)
            {
                if (local[0] < 0 || local[k] < 0 || local[k + 1] < 0) continue;
                r.Index.Add(local[0]); r.Index.Add(local[k + 1]); r.Index.Add(local[k]);
            }
        }
        r.PackCenter = Center(r.Verts) / ObjToMeters;   // 격자 위치는 원본 단위로 본다
        return r;
    }

    // 격자 칸이 겹치는 그룹끼리 묶는다. 하나도 겹치지 않으면 그룹 = 헤어다.
    private static List<Raw> MergeOverlapping(List<Raw> raws, List<string> notes, Action<string> log)
    {
        var boxes = raws.Select(Box).ToList();
        var parent = Enumerable.Range(0, raws.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

        int pairs = 0;
        for (int i = 0; i < raws.Count; i++)
            for (int j = i + 1; j < raws.Count; j++)
            {
                // XY 평면(=팩을 늘어놓은 평면)에서만 본다. Z 는 모든 헤어가 같은 범위다.
                Aabb a = boxes[i], b = boxes[j];
                bool ox = a.Position.X < b.End.X && b.Position.X < a.End.X;
                bool oy = a.Position.Y < b.End.Y && b.Position.Y < a.End.Y;
                if (!ox || !oy) continue;
                pairs++;
                int ra = Find(i), rb = Find(j);
                if (ra != rb) parent[ra] = rb;
            }

        if (pairs == 0)
        {
            log("  격자가 겹치는 그룹이 없다 — 그룹 하나 = 헤어 하나로 확인");
            notes.Add($"여성 팩의 그룹 {raws.Count}개는 격자 칸이 서로 겹치지 않는다. " +
                      "실측으로 '그룹 하나 = 헤어스타일 하나' 를 확인하고 그대로 분리했다");
            return raws;
        }

        var groups = new Dictionary<int, List<Raw>>();
        for (int i = 0; i < raws.Count; i++)
        {
            int r = Find(i);
            if (!groups.TryGetValue(r, out List<Raw> l)) groups[r] = l = new List<Raw>();
            l.Add(raws[i]);
        }
        var joined = new List<Raw>();
        foreach (List<Raw> l in groups.Values)
        {
            if (l.Count == 1) { joined.Add(l[0]); continue; }
            joined.Add(Join(l));
            notes.Add($"그룹 {l.Count}개({string.Join(", ", l.Select(x => x.Node))})는 같은 칸을 " +
                      "차지하므로 한 헤어스타일로 합쳤다");
        }
        log($"  겹치는 쌍 {pairs}개 → 그룹 {raws.Count}개를 헤어 {joined.Count}개로 묶었다");
        return joined;
    }

    private static Raw Join(List<Raw> l)
    {
        var r = new Raw { Node = string.Join("+", l.Select(x => x.Node)) };
        foreach (Raw p in l)
        {
            int off = r.Verts.Count;
            r.Verts.AddRange(p.Verts); r.Norms.AddRange(p.Norms); r.Uvs.AddRange(p.Uvs);
            foreach (int i in p.Index) r.Index.Add(i + off);
        }
        r.PackCenter = Center(r.Verts) / ObjToMeters;
        return r;
    }

    // ── 공통 ────────────────────────────────────────────────────────────

    private sealed class Raw
    {
        public string Id;
        public string Node = "", Material = "";
        public int Surface;
        public bool IsBeard;
        public Vector3 PackCenter;
        public Raw TwinOf;        // 팩 안에 같은 메시가 먼저 있었다면 그것
        public Vector3[] Sorted;  // 비교용 — 원점을 맞추고 정렬한 정점
        public readonly List<Vector3> Verts = new();
        public readonly List<Vector3> Norms = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<int> Index = new();
    }

    private static Raw Bake(Mesh mesh, int surface, Transform3D t)
    {
        var arr = mesh.SurfaceGetArrays(surface);
        var vs = arr[(int)Mesh.ArrayType.Vertex].As<Vector3[]>();
        if (vs == null || vs.Length == 0) return null;
        var ns = arr[(int)Mesh.ArrayType.Normal].As<Vector3[]>();
        var uv = arr[(int)Mesh.ArrayType.TexUV].As<Vector2[]>();
        var idx = arr[(int)Mesh.ArrayType.Index].As<int[]>();

        var r = new Raw();
        Basis nb = t.Basis;
        for (int i = 0; i < vs.Length; i++)
        {
            r.Verts.Add(t * vs[i]);
            r.Norms.Add(ns != null && i < ns.Length ? (nb * ns[i]).Normalized() : Vector3.Up);
            r.Uvs.Add(uv != null && i < uv.Length ? uv[i] : Vector2.Zero);
        }
        if (idx != null && idx.Length > 0) r.Index.AddRange(idx);
        else for (int i = 0; i < vs.Length; i++) r.Index.Add(i);
        r.PackCenter = Center(r.Verts);
        return r;
    }

    // 원점을 AABB 중심으로 옮기고, 메시를 저장하고, 목록에 필요한 수치를 잰다.
    private static HairEntry Finish(Raw r, string id, string sex, string src, string kind,
                                    Action<string> log)
    {
        Vector3 c = Box(r).GetCenter();
        var verts = new Vector3[r.Verts.Count];
        for (int i = 0; i < verts.Length; i++) verts[i] = r.Verts[i] - c;

        Aabb box = Of(verts);
        float crownTop = CrownTop(verts, box);
        float capY = crownTop - Mathf.Min(CapDepth, box.Size.Y * 0.45f);
        var cap = new List<Vector3>();
        foreach (Vector3 v in verts) if (v.Y >= capY && v.Y <= crownTop) cap.Add(v);
        Aabb capBox = cap.Count > 8 ? Of(cap) : box;

        string dir = kind == "beard" ? "beard" : sex;
        string path = $"{OutRoot}/{dir}/{id}.res";
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());

        var mesh = new ArrayMesh { ResourceName = id };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = r.Norms.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = r.Uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = r.Index.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        Error err = ResourceSaver.Save(mesh, path, ResourceSaver.SaverFlags.Compress);
        if (err != Error.Ok) log($"  !! {id} 저장 실패: {err}");

        float area = 0f;
        for (int i = 0; i + 2 < r.Index.Count; i += 3)
            area += (verts[r.Index[i + 1]] - verts[r.Index[i]])
                .Cross(verts[r.Index[i + 2]] - verts[r.Index[i]]).Length() * 0.5f;
        Vector3 s = box.Size;
        float boxArea = 2f * (s.X * s.Y + s.Y * s.Z + s.Z * s.X);

        return new HairEntry
        {
            Id = id, Kind = kind, Sex = sex, Name = NameOf(id, box.Size.Y),
            MeshPath = path, SourceFile = src, SourceNode = r.Node,
            SourceSurface = r.Surface, SourceMaterial = r.Material,
            Verts = verts.Length, Tris = r.Index.Count / 3,
            Size = box.Size, CapCenter = capBox.GetCenter(), CapSize = capBox.Size,
            AreaRatio = boxArea > 0.0001f ? area / boxArea : 0f,
            LengthClass = Length(box.Size.Y),
        };
    }

    // 사람이 알아볼 이름. 원본에 헤어 이름이 없으므로(재질 이름이 "1.002" 수준이다)
    // 번호 + 실측 길이로 짓는다. "컬리" 같은 말을 임의로 붙이지 않는다.
    private static string NameOf(string id, float h)
        => $"{id} · {Korean(Length(h))} ({h * 100f:F0}cm)";

    private static string Length(float h) => h < 0.26f ? "short" : h < 0.40f ? "medium" : "long";

    public static string Korean(string lengthClass) => lengthClass switch
    {
        "short" => "짧은 머리",
        "medium" => "중간 길이",
        _ => "긴 머리",
    };

    // 정수리가 어디까지인가.
    //
    // AABB 의 꼭대기를 정수리로 치면 **묶은 머리** 가 있는 헤어에서 어긋난다 — 꼭대기가
    // 머리끈 끝이라서, 거기를 정수리에 맞추면 머리카락 전체가 얼굴까지 내려앉는다.
    // 그래서 위에서부터 1cm 씩 훑으며 "충분히 넓은" 첫 층을 찾는다. 묶은 머리·뿔처럼
    // 가느다란 것은 건너뛰고, 머리통을 덮는 넓은 층에서 멈춘다.
    private static float CrownTop(IReadOnlyList<Vector3> verts, Aabb box)
    {
        const float Bin = 0.01f;
        const float WideEnough = 0.6f;
        int bins = Mathf.Max(1, Mathf.CeilToInt(box.Size.Y / Bin));
        if (bins < 4) return box.End.Y;

        var lo = new float[bins]; var hi = new float[bins]; var n = new int[bins];
        for (int i = 0; i < bins; i++) { lo[i] = float.MaxValue; hi[i] = float.MinValue; }
        foreach (Vector3 v in verts)
        {
            int b = Mathf.Clamp((int)((v.Y - box.Position.Y) / Bin), 0, bins - 1);
            if (v.X < lo[b]) lo[b] = v.X;
            if (v.X > hi[b]) hi[b] = v.X;
            n[b]++;
        }

        float Width(int b) => n[b] >= 8 ? hi[b] - lo[b] : 0f;

        // 넓이 기준은 윗부분에서만 잡는다 — 긴 머리의 어깨 언저리 퍼짐에 끌려가지 않게.
        int from = (int)(bins * 0.5f);
        float widest = 0f;
        for (int b = from; b < bins; b++) widest = Mathf.Max(widest, Width(b));
        if (widest <= 0f) return box.End.Y;

        for (int b = bins - 1; b > from; b--)
            if (Width(b) >= widest * WideEnough)
                return box.Position.Y + (b + 1) * Bin;
        return box.End.Y;
    }

    private static Aabb Box(Raw r) => Of(r.Verts);

    private static Aabb Of(IReadOnlyList<Vector3> v)
    {
        if (v.Count == 0) return new Aabb();
        var b = new Aabb(v[0], Vector3.Zero);
        for (int i = 1; i < v.Count; i++) b = b.Expand(v[i]);
        return b;
    }

    private static Vector3 Center(IReadOnlyList<Vector3> v)
    {
        if (v.Count == 0) return Vector3.Zero;
        Vector3 s = Vector3.Zero;
        foreach (Vector3 p in v) s += p;
        return s / v.Count;
    }

    // 같은 메시가 위치만 옮겨 두 번 들어 있는가.
    //
    // 정점 **순서**는 믿을 수 없다. 실제로 이 FBX 안의 복제본들은 같은 정점을 다른
    // 순서로 담고 있었다(순서대로 비교하면 17cm 어긋나지만 정렬 후에는 0m 일치). 그래서
    // 원점을 맞추고 정렬한 뒤 비교한다.
    private static bool SameGeometry(Raw a, Raw b)
    {
        if (a.Verts.Count != b.Verts.Count || a.Index.Count != b.Index.Count) return false;
        if (!Box(a).Size.IsEqualApprox(Box(b).Size)) return false;
        Vector3[] sa = SortedCentered(a), sb = SortedCentered(b);
        for (int i = 0; i < sa.Length; i++)
            if (sa[i].DistanceTo(sb[i]) > 0.001f) return false;
        return true;
    }

    private static Vector3[] SortedCentered(Raw r)
    {
        if (r.Sorted != null) return r.Sorted;
        Vector3 c = Box(r).GetCenter();
        return r.Sorted = r.Verts.Select(v => v - c)
            .OrderBy(v => v.X).ThenBy(v => v.Y).ThenBy(v => v.Z).ToArray();
    }

    // 팩 안에서 먼저 나온 것을 원본으로 보고, 뒤에 나온 같은 메시를 복제본으로 표시한다.
    private static void MarkDuplicates(List<Raw> raws, List<string> notes, Action<string> log)
    {
        int found = 0;
        for (int i = 0; i < raws.Count; i++)
            for (int j = 0; j < i; j++)
            {
                if (raws[j].TwinOf != null) continue;      // 복제본의 복제본으로 묶지 않는다
                if (!SameGeometry(raws[j], raws[i])) continue;
                raws[i].TwinOf = raws[j];
                found++;
                break;
            }
        if (found > 0) log($"  같은 메시가 두 번 들어 있는 것 {found}개를 찾았다");
    }

    // ── 베이스 몸체 실측 ────────────────────────────────────────────────

    public static HairBaseEntry MeasureBase(string sex, string scenePath,
                                            List<string> notes, Action<string> log)
    {
        var packed = ResourceLoader.Load<PackedScene>(scenePath);
        if (packed == null) { notes.Add($"{scenePath} 를 불러오지 못했다"); return null; }
        var root = packed.Instantiate<Node3D>();
        Skeleton3D skel = FindSkeleton(root);
        if (skel == null) { notes.Add($"{scenePath} 에 Skeleton3D 가 없다"); root.QueueFree(); return null; }

        int head = skel.FindBone("Head");
        if (head < 0) { notes.Add($"{scenePath} 에 Head 본이 없다"); root.QueueFree(); return null; }
        float headY = skel.GetBoneGlobalRest(head).Origin.Y;

        // 몸통 메시(정점이 가장 많은 것)에서 머리 본보다 위에 있는 정점만 모은다.
        // = 두개골. 헤어를 여기에 맞춘다(지시서 §5 "머리 크기를 기준으로 자동 정렬").
        MeshInstance3D body = null;
        int best = -1;
        foreach (MeshInstance3D mi in skel.GetChildren().OfType<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            int n = mi.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].As<Vector3[]>()?.Length ?? 0;
            if (n > best) { best = n; body = mi; }
        }
        var all = body?.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].As<Vector3[]>();
        float top = 0f;
        if (all != null) foreach (Vector3 v in all) if (v.Y > top) top = v.Y;

        var skull = new List<Vector3>();
        var cranium = new List<Vector3>();
        // 귀는 머리통 폭을 2cm 넘게 부풀린다. 머리카락은 귀를 덮지 않으므로 크기를 맞출
        // 기준은 **귀 위쪽** 이어야 한다. 머리 본과 정수리의 중간을 귀선으로 본다.
        float earLine = headY + (top - headY) * 0.5f;
        if (all != null)
            foreach (Vector3 v in all)
            {
                if (v.Y < headY + 0.03f) continue;
                skull.Add(v);
                if (v.Y >= earLine) cranium.Add(v);
            }
        Aabb sb = Of(skull), cb = cranium.Count > 32 ? Of(cranium) : Of(skull);
        root.QueueFree();

        log($"{sex} 베이스: 키 {top:F3}m · Head 본 y={headY:F3} · " +
            $"머리통 {sb.Size.Snapped(Vector3.One * 0.001f)} @ {sb.GetCenter().Snapped(Vector3.One * 0.001f)} " +
            $"· 귀 위 {cb.Size.Snapped(Vector3.One * 0.001f)} @ {cb.GetCenter().Snapped(Vector3.One * 0.001f)} " +
            $"(정점 {skull.Count}/{cranium.Count}개)");

        return new HairBaseEntry
        {
            Sex = sex, ScenePath = scenePath, HeadBone = "Head",
            SkullCenter = sb.GetCenter(), SkullSize = sb.Size,
            CraniumCenter = cb.GetCenter(), CraniumSize = cb.Size,
            BodyHeight = top,
        };
    }

    public static Skeleton3D FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (Node c in n.GetChildren())
        {
            Skeleton3D r = FindSkeleton(c);
            if (r != null) return r;
        }
        return null;
    }
}
