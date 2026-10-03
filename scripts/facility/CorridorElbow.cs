using Godot;

namespace NSP.Facility;

// 두 방을 잇는 통로가 어디서 한 번 직각으로 꺾이는가.
//
// 직원 이동(FacilitySimulation)과 미니맵의 회색 통로(FacilityMinimap)가 반드시 같은 점을 써야
// 걷는 길과 그려진 길이 겹친다 — 그래서 규칙을 여기 한 곳에만 둔다.
//
// 꺾는 자리 후보는 두 개다: (위쪽 방 X, 아래쪽 방 Y) / (아래쪽 방 X, 위쪽 방 Y).
// 그중 중앙 제어실(허브)에서 더 먼 쪽을 고른다. 그러면
//   · 중앙 제어실 ↔ 작업실 통로는 안쪽 '살'이 되고
//   · 작업실 ↔ 작업실 통로는 바깥쪽 '고리'가 되어
// 서로 겹치지 않는다.
public static class CorridorElbow
{
    public static Vector2? Compute(Vector2 a, Vector2 b, Vector2 hub)
    {
        if (Mathf.IsEqualApprox(a.X, b.X) || Mathf.IsEqualApprox(a.Y, b.Y)) return null;

        Vector2 upper = a.Y <= b.Y ? a : b;
        Vector2 lower = a.Y <= b.Y ? b : a;
        var inner = new Vector2(upper.X, lower.Y);
        var outer = new Vector2(lower.X, upper.Y);
        return outer.DistanceSquaredTo(hub) > inner.DistanceSquaredTo(hub) + 0.01f ? outer : inner;
    }
}
