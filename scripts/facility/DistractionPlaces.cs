using System.Collections.Generic;

namespace NSP.Facility;

// 작업실마다 "어떤 딴짓을 할 자리가 실제로 있는가". **표현 계층이 재서 올려 둔다.**
//
// 방 구조(설비 배치 · 벽 · 빈 바닥)를 아는 것은 3D 쪽(RoomWorkVisualController)뿐이다.
// 그래서 방 씬을 띄우는 FacilityCctvWorld 가 시작할 때 한 번 재서 이 표에 넣고,
// 시뮬레이션(DistractionSystem)은 딴짓을 고를 때 이 표를 읽는다 —
// 누울 자리가 없는 방에서 눕는 딴짓을 고르지 않게 하는 것이 목적이다.
//
// 표가 비어 있으면(3D 월드가 없는 단독 테스트 등) "모르니 허용" 으로 본다.
// 그 경우에도 실제로 자리를 못 찾으면 표현 계층이 ReportPlaceUnavailable 로 되돌린다.
public static class DistractionPlaces
{
    private static readonly Dictionary<string, HashSet<DistractionPlace>> _table = new();

    public static void Publish(string roomId, IEnumerable<DistractionPlace> places)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        _table[roomId] = new HashSet<DistractionPlace>(places);
    }

    public static bool Known(string roomId) => _table.ContainsKey(roomId);

    public static bool Has(string roomId, DistractionPlace place)
    {
        if (place == DistractionPlace.AtSpot) return true;
        if (string.IsNullOrEmpty(roomId)) return false;
        return !_table.TryGetValue(roomId, out var set) || set.Contains(place);
    }

    public static void Clear() => _table.Clear();
}
