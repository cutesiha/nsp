using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;

namespace NSP.Core;

// data/ops/*.tres 에 있는 하루치 운영 규칙을 읽어 "오늘 쓸 값"을 돌려준다.
//
// Config(전역 상수)와 역할이 다르다 — 여기 있는 값은 전부 "그 DAY 의" 값이고,
// DAY2~5 는 같은 형식의 .tres 를 하나 더 넣는 것으로 끝난다.
// 해당 DAY 의 프로필이 없으면 그보다 작은 DAY 중 가장 큰 것을 쓴다.
public static class OpsProfile
{
    private const string Folder = "res://data/ops";

    private static readonly List<OpsProfileDef> _profiles = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        foreach (string path in ResourceDir.ListFiles(Folder, ".tres"))
        {
            var res = GD.Load<OpsProfileDef>(path);
            if (res != null) _profiles.Add(res);
        }
        _profiles.Sort((a, b) => a.Day.CompareTo(b.Day));
        if (_profiles.Count == 0)
            GD.PushWarning($"OpsProfile: {Folder} 에서 운영 규칙을 찾지 못했습니다.");
    }

    // 오늘의 운영 규칙. 없으면 null 이므로 호출부는 항상 ?. 로 접근한다.
    public static OpsProfileDef Today => For(GameState.Instance?.CurrentDay ?? 1);

    public static OpsProfileDef For(int day)
    {
        EnsureLoaded();
        OpsProfileDef best = null;
        foreach (var p in _profiles)
        {
            if (p.Day > day) break;
            best = p;
        }
        // 해당하는 날이 없으면 null. DAY0(가상 시뮬레이션)이 여기 해당하며,
        // 그 날은 운영 규칙이 아예 적용되지 않고 예전 동작 그대로 돈다.
        return best;
    }

    // 오늘 그 작업실의 인원 효과표. 없으면 null.
    public static RoomOpsDef Room(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return null;
        var profile = Today;
        if (profile == null) return null;
        foreach (var r in profile.Rooms)
            if (r != null && r.RoomId == roomId) return r;
        return null;
    }

    public static IEnumerable<RoomOpsDef> AllRooms() =>
        Today?.Rooms.Where(r => r != null) ?? Enumerable.Empty<RoomOpsDef>();

    // 인원수별 표에서 값을 읽는다. 표 끝을 넘는 인원은 마지막 값을 쓴다.
    // (그래서 "3명 이상은 효율이 거의 안 오른다"를 마지막 두 칸으로 표현할 수 있다.)
    public static float Curve(float[] table, int headcount, float fallback = 1f)
    {
        if (table == null || table.Length == 0) return fallback;
        int i = Mathf.Clamp(headcount, 0, table.Length - 1);
        return table[i];
    }

    // 테스트에서 데이터를 다시 읽게 한다.
    public static void Reload()
    {
        _profiles.Clear();
        _loaded = false;
        EnsureLoaded();
    }
}
