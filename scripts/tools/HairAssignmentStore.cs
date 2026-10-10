using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;

namespace NSP.Tools;

// 캐릭터 한 명의 헤어 선택 결과. 지시서 §7 의 항목을 그대로 담는다.
public sealed class HairAssignment
{
    public string CharacterId = "";
    public string Body = "male";            // male · female
    public string HairId = "";
    public string HairPath = "";
    public string ColorHex = "#2B211C";
    public string ColorName = "";
    public Vector3 Position = Vector3.Zero;
    public Vector3 RotationDeg = Vector3.Zero;
    public Vector3 Scale = Vector3.One;

    public Godot.Collections.Dictionary ToDict() => new()
    {
        ["body"] = Body,
        ["hair"] = HairId,
        ["hair_path"] = HairPath,
        ["color"] = ColorHex,
        ["color_name"] = ColorName,
        ["position"] = Arr(Position),
        ["rotation"] = Arr(RotationDeg),
        ["scale"] = Arr(Scale),
    };

    public static HairAssignment FromDict(string id, Godot.Collections.Dictionary d) => new()
    {
        CharacterId = id,
        Body = Str(d, "body", "male"),
        HairId = Str(d, "hair", ""),
        HairPath = Str(d, "hair_path", ""),
        ColorHex = Str(d, "color", "#2B211C"),
        ColorName = Str(d, "color_name", ""),
        Position = Vec(d, "position", Vector3.Zero),
        RotationDeg = Vec(d, "rotation", Vector3.Zero),
        Scale = Vec(d, "scale", Vector3.One),
    };

    private static Godot.Collections.Array Arr(Vector3 v) => new()
    {
        Mathf.Snapped(v.X, 0.0001f), Mathf.Snapped(v.Y, 0.0001f), Mathf.Snapped(v.Z, 0.0001f),
    };

    private static string Str(Godot.Collections.Dictionary d, string k, string fallback)
        => d.TryGetValue(k, out Variant v) ? v.AsString() : fallback;

    private static Vector3 Vec(Godot.Collections.Dictionary d, string k, Vector3 fallback)
    {
        if (!d.TryGetValue(k, out Variant v)) return fallback;
        var a = v.As<Godot.Collections.Array>();
        return a == null || a.Count < 3 ? fallback
            : new Vector3((float)a[0], (float)a[1], (float)a[2]);
    }
}

// 스튜디오에서 고른 결과를 프로젝트 안의 설정 파일로 내보낸다.
//
// 저장 위치는 res:// 다 — 다음 작업(실제 캐릭터 모델 교체)에서 에디터와 게임 코드가
// 같이 읽어야 하므로 프로젝트에 들어 있어야 한다(지시서 §7 · §9). 내보낸 빌드처럼
// res:// 에 쓸 수 없는 환경에서는 user:// 로 떨어진다.
public sealed class HairAssignmentStore
{
    public const string ResPath = "res://assets/characters/hair/hair_assignments.json";
    public const string UserPath = "user://hair_assignments.json";

    public readonly Dictionary<string, HairAssignment> ByCharacter = new();
    public string LastSavedTo = "";

    // 스튜디오에 뜨는 8명. 직원 여섯은 **기존 게임 데이터의 ID** 를 그대로 쓴다.
    public static readonly (string Id, string Label, string Body)[] Characters =
    {
        ("admin", "관리자", "male"),
        ("director", "총괄관리자", "male"),
        ("rabbit", "토끼", ""),
        ("cat", "고양이", ""),
        ("fox", "여우", ""),
        ("sheep", "양", ""),
        ("wolf", "늑대", ""),
        ("dog", "강아지", ""),
    };

    // 직원의 초기 베이스는 데이터의 Gender 에서 읽는다. 코드에 성별을 박지 않는다
    // ("캐릭터 이름 및 성별 임의 변경 금지" — 지시서 §8).
    public static string DefaultBody(string characterId)
    {
        var fixedBody = Characters.FirstOrDefault(c => c.Id == characterId).Body;
        if (!string.IsNullOrEmpty(fixedBody)) return fixedBody;
        EmployeeDef def = LoadDef(characterId);
        if (def != null && def.Gender.Contains("여")) return "female";
        return "male";
    }

    public static EmployeeDef LoadDef(string characterId)
    {
        string p = $"res://data/employees/{characterId}.tres";
        return ResourceLoader.Exists(p) ? ResourceLoader.Load<EmployeeDef>(p) : null;
    }

    public static string Label(string characterId)
    {
        EmployeeDef def = LoadDef(characterId);
        if (def != null && !string.IsNullOrEmpty(def.Codename)) return def.Codename;
        return Characters.FirstOrDefault(c => c.Id == characterId).Label ?? characterId;
    }

    public HairAssignment Get(string id)
    {
        if (ByCharacter.TryGetValue(id, out HairAssignment a)) return a;
        return ByCharacter[id] = new HairAssignment { CharacterId = id, Body = DefaultBody(id) };
    }

    // to 를 주면 그 자리에 쓴다(테스트용). 평소에는 res:// 에 쓰고, 그게 막힌 환경
    // (내보낸 빌드)에서만 user:// 로 떨어진다.
    public Error Save(string to = "")
    {
        var root = new Godot.Collections.Dictionary();
        foreach ((string id, _, _) in Characters)
        {
            if (!ByCharacter.TryGetValue(id, out HairAssignment a)) continue;
            root[id] = a.ToDict();
        }
        string text = Json.Stringify(root, "  ");

        string target = to == "" ? ResPath : to;
        DirAccess.MakeDirRecursiveAbsolute(target.GetBaseDir());
        using var f = FileAccess.Open(target, FileAccess.ModeFlags.Write);
        if (f != null) { f.StoreString(text); LastSavedTo = target; return Error.Ok; }
        if (to != "") return FileAccess.GetOpenError();

        using var u = FileAccess.Open(UserPath, FileAccess.ModeFlags.Write);
        if (u == null) return FileAccess.GetOpenError();
        u.StoreString(text);
        LastSavedTo = UserPath;
        return Error.Ok;
    }

    public static HairAssignmentStore Load(string from = "")
    {
        var s = new HairAssignmentStore();
        string path = from != "" ? from
            : FileAccess.FileExists(ResPath) ? ResPath
            : FileAccess.FileExists(UserPath) ? UserPath : "";
        if (path == "" || !FileAccess.FileExists(path)) return s;
        using var f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (f == null) return s;
        Variant parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return s;
        var root = parsed.As<Godot.Collections.Dictionary>();
        foreach (Variant k in root.Keys)
        {
            string id = k.AsString();
            s.ByCharacter[id] = HairAssignment.FromDict(id, root[k].As<Godot.Collections.Dictionary>());
        }
        s.LastSavedTo = path;
        return s;
    }

    // 고른 적 없는 캐릭터는 비워 둔다 — "제가 직접 지정하기 전에는 임의로 확정하지
    // 마세요"(지시서 §6). 기본값으로 아무 헤어도 넣지 않는다.
    public bool HasChoice(string id) => ByCharacter.TryGetValue(id, out HairAssignment a)
                                        && !string.IsNullOrEmpty(a.HairId);
}
