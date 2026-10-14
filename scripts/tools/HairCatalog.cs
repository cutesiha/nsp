using System.Collections.Generic;
using System.Linq;
using Godot;

namespace NSP.Tools;

// 헤어 카탈로그 — 원본 팩에서 **실제로 분리된** 헤어 하나하나의 명세.
//
// 스튜디오는 이 파일만 읽는다. 122MB OBJ 나 FBX 를 다시 파싱하지 않고, 목록에 필요한
// 수치(크기 · 머리통에 닿는 부분 · 길이 · 풍성함)를 전부 여기서 가져온다. 그래서
// 헤어 100개가 있어도 목록을 띄우는 데 메시를 하나도 올리지 않는다(지시서 §4 성능).
public sealed class HairEntry
{
    public string Id = "";              // M-01 · F-001 · MB-01
    public string Kind = "hair";        // hair · beard
    public string Sex = "female";       // male · female
    public string Name = "";            // 화면에 띄우는 이름
    public string MeshPath = "";        // res://assets/characters/hair/...
    public string SourceFile = "";
    public string SourceNode = "";      // FBX 노드 이름 · OBJ 그룹 이름
    public int SourceSurface;
    public string SourceMaterial = "";
    public int Verts, Tris;

    public Vector3 Size;                // 원점(=AABB 중심) 기준 전체 크기, 미터
    public Vector3 CapCenter;           // 머리통에 씌워지는 윗부분의 중심
    public Vector3 CapSize;             // 그 윗부분의 크기 — 자동 맞춤의 기준
    public float AreaRatio;             // 표면적 / AABB 표면적 — 주름이 많으면 커진다
    public string LengthClass = "short"; // short · medium · long

    public Godot.Collections.Dictionary ToDict() => new()
    {
        ["id"] = Id, ["kind"] = Kind, ["sex"] = Sex, ["name"] = Name,
        ["mesh_path"] = MeshPath,
        ["source_file"] = SourceFile, ["source_node"] = SourceNode,
        ["source_surface"] = SourceSurface, ["source_material"] = SourceMaterial,
        ["verts"] = Verts, ["tris"] = Tris,
        ["size"] = V(Size), ["cap_center"] = V(CapCenter), ["cap_size"] = V(CapSize),
        ["area_ratio"] = Snap(AreaRatio),
        ["length_class"] = LengthClass,
    };

    public static HairEntry FromDict(Godot.Collections.Dictionary d) => new()
    {
        Id = (string)d["id"], Kind = (string)d["kind"], Sex = (string)d["sex"],
        Name = (string)d["name"], MeshPath = (string)d["mesh_path"],
        SourceFile = (string)d["source_file"], SourceNode = (string)d["source_node"],
        SourceSurface = (int)d["source_surface"], SourceMaterial = (string)d["source_material"],
        Verts = (int)d["verts"], Tris = (int)d["tris"],
        Size = RV(d["size"]), CapCenter = RV(d["cap_center"]), CapSize = RV(d["cap_size"]),
        AreaRatio = (float)d["area_ratio"],
        LengthClass = (string)d["length_class"],
    };

    private static Godot.Collections.Array V(Vector3 v)
        => new() { Snap(v.X), Snap(v.Y), Snap(v.Z) };

    private static Vector3 RV(Variant v)
    {
        var a = v.As<Godot.Collections.Array>();
        return a == null || a.Count < 3 ? Vector3.Zero
            : new Vector3((float)a[0], (float)a[1], (float)a[2]);
    }

    private static float Snap(float f) => Mathf.Snapped(f, 0.00001f);
}

// 베이스 몸체 하나의 명세 — 머리 본과 **실측한 머리통 크기**.
// 자동 맞춤은 이 수치에 헤어를 맞춘다. "머리 크기를 기준으로 자동 정렬"(지시서 §5).
public sealed class HairBaseEntry
{
    public string Sex = "male";
    public string ScenePath = "";
    public string HeadBone = "Head";
    public Vector3 SkullCenter;
    public Vector3 SkullSize;

    // 귀 위쪽 — 머리카락이 실제로 씌워지는 부분. 머리통 전체 폭은 귀가 끼어서 2cm 넘게
    // 부풀어 있어 그대로 쓰면 헤어가 커진다.
    public Vector3 CraniumCenter;
    public Vector3 CraniumSize;

    public float BodyHeight;

    public Godot.Collections.Dictionary ToDict() => new()
    {
        ["sex"] = Sex, ["scene_path"] = ScenePath, ["head_bone"] = HeadBone,
        ["skull_center"] = V(SkullCenter), ["skull_size"] = V(SkullSize),
        ["cranium_center"] = V(CraniumCenter), ["cranium_size"] = V(CraniumSize),
        ["body_height"] = Mathf.Snapped(BodyHeight, 0.00001f),
    };

    public static HairBaseEntry FromDict(Godot.Collections.Dictionary d)
    {
        var e = new HairBaseEntry
        {
            Sex = (string)d["sex"], ScenePath = (string)d["scene_path"],
            HeadBone = (string)d["head_bone"],
            SkullCenter = RV(d, "skull_center"), SkullSize = RV(d, "skull_size"),
            CraniumCenter = RV(d, "cranium_center"), CraniumSize = RV(d, "cranium_size"),
            BodyHeight = (float)d["body_height"],
        };
        if (e.CraniumSize == Vector3.Zero) { e.CraniumSize = e.SkullSize; e.CraniumCenter = e.SkullCenter; }
        return e;
    }

    private static Godot.Collections.Array V(Vector3 v) => new()
    {
        Mathf.Snapped(v.X, 0.00001f), Mathf.Snapped(v.Y, 0.00001f), Mathf.Snapped(v.Z, 0.00001f),
    };

    private static Vector3 RV(Godot.Collections.Dictionary d, string k)
    {
        if (!d.TryGetValue(k, out Variant v)) return Vector3.Zero;
        var a = v.As<Godot.Collections.Array>();
        return a == null || a.Count < 3 ? Vector3.Zero
            : new Vector3((float)a[0], (float)a[1], (float)a[2]);
    }
}

public sealed class HairCatalog
{
    public const string Path = "res://assets/characters/hair/hair_catalog.json";

    public readonly List<HairEntry> Entries = new();
    public readonly List<HairBaseEntry> Bases = new();
    public readonly List<string> Notes = new();   // 분리 과정에서 생긴 특이사항

    public IEnumerable<HairEntry> Hairs(string sex)
        => Entries.Where(e => e.Kind == "hair" && e.Sex == sex);

    // 팩 하나가 바라보는 방향(초기 Y 회전).
    //
    // 정점 분포로 앞뒤를 추정해 봤지만 믿을 수 없었다 — 앞머리가 있는 헤어는 질량이
    // 오히려 얼굴 쪽으로 쏠려서 반대로 나온다. 그래서 **화면으로 확인한 값**을 적어 둔다.
    // 두 팩 모두 뒤통수가 -Z 를 향하고 있고, 베이스 모델은 +Z 를 보므로 돌릴 필요가 없다.
    // 팩을 새로 추가하면 여기에 한 줄 적으면 된다. 개별 조정은 스튜디오의 ROTATION Y 로 한다.
    public static float PackYaw(string sourceFile) => sourceFile switch
    {
        _ => 0f,
    };

    public HairEntry ById(string id) => Entries.FirstOrDefault(e => e.Id == id);
    public HairBaseEntry Base(string sex) => Bases.FirstOrDefault(b => b.Sex == sex);

    public Error Save(string path = Path)
    {
        var root = new Godot.Collections.Dictionary
        {
            ["version"] = 1,
            ["bases"] = new Godot.Collections.Array(Bases.Select(b => Variant.From(b.ToDict()))),
            ["hair"] = new Godot.Collections.Array(Entries.Select(e => Variant.From(e.ToDict()))),
            ["notes"] = new Godot.Collections.Array(Notes.Select(n => Variant.From(n))),
        };
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null) return FileAccess.GetOpenError();
        f.StoreString(Json.Stringify(root, "  "));
        return Error.Ok;
    }

    public static HairCatalog Load(string path = Path)
    {
        var c = new HairCatalog();
        if (!FileAccess.FileExists(path)) return c;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return c;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return c;
        var root = parsed.As<Godot.Collections.Dictionary>();
        if (root.TryGetValue("bases", out Variant b))
            foreach (Variant v in b.As<Godot.Collections.Array>())
                c.Bases.Add(HairBaseEntry.FromDict(v.As<Godot.Collections.Dictionary>()));
        if (root.TryGetValue("hair", out Variant h))
            foreach (Variant v in h.As<Godot.Collections.Array>())
                c.Entries.Add(HairEntry.FromDict(v.As<Godot.Collections.Dictionary>()));
        if (root.TryGetValue("notes", out Variant n))
            foreach (Variant v in n.As<Godot.Collections.Array>()) c.Notes.Add(v.AsString());
        return c;
    }
}
