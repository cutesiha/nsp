using Godot;

namespace NSP.Data;

// 괴물이 나오는 외곽 구역의 종류. 미니맵 바깥쪽 표기와 소리 선택에만 쓴다.
public enum ThreatZoneKind
{
    // 외곽 대피 통로 — 시설 바깥과 이어진 폐쇄된 피난로.
    EvacuationPassage,
    // 환기 덕트 — 좁은 덕트와 천장 점검구.
    VentilationShaft,
    // 배관 · 케이블 통로 — 설비 사이에 숨은 점검 통로.
    CablePassage,
}

// 괴물 전용 출현 구역.
//
// **작업실이 아니다.** RoomDef 로 만들지 않는 이유가 그것이다 — 작업실로 등록하면
// 배치표 · 업무 · 사고 · 추리 자료에 전부 섞여 들어간다. 여기 있는 것은
// "괴물이 어디서 나와서 어느 접근 복도로 들어오는가" 하나뿐이고,
// 직원 길찾기(FacilitySimulation)는 이 데이터를 아예 모른다.
[GlobalClass]
public partial class ThreatZoneDef : Resource
{
    [Export] public string ZoneId = "";
    [Export] public string DisplayName = "";
    [Export] public string CodeName = "";          // 지도 외곽 표기용 영문 약호
    [Export] public ThreatZoneKind Kind = ThreatZoneKind.EvacuationPassage;

    // 미니맵 정규화 좌표(0~1). 작업실 바깥 가장자리에 작은 표기로만 찍는다.
    [Export] public Vector2 MapAnchor = new(0.5f, 0.5f);

    // 이 구역에서 나온 것이 들어갈 수 있는 중앙제어실 접근 복도(CorridorDef.SegmentId).
    // 둘 이상이면 그중 하나를 고른다 — 같은 길로만 오면 두 번째부터는 사건이 아니다.
    [Export] public string[] ApproachSegmentIds = System.Array.Empty<string>();

    // 출현 구역에서 접근 복도 입구까지 걸리는 시간(초). 이 동안은 카메라에 잡히지 않고
    // 소리만 들린다 — "뭔가 오고 있다" 를 알리되 어디로 올지는 아직 모르는 구간이다.
    [Export] public float OuterTravelSeconds = 9f;
}
