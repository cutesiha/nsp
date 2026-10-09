using Godot;

namespace NSP.Data;

// 복도 구간의 성격. 미니맵에서 굵기 · 색 · 표시가 이 값으로 갈린다.
public enum CorridorKind
{
    // 일반 통로 — 두 작업실을 잇는 보통 복도.
    Minor,
    // 주요 연결통로 — 시설 물류가 오가는 굵은 복도. 막히면 돌아가는 길이 길다.
    Trunk,
    // 중앙제어실 접근 통로 — 관리자가 있는 방으로 들어오는 길.
    ControlAccess,
}

// 복도 한 구간의 **메타데이터**.
//
// 중요: 여기에 "어느 방과 어느 방이 이어져 있는가" 를 새로 정의하지 않는다.
// 통행 그래프의 진실원은 지금까지와 똑같이 RoomDef.ConnectedRoomIds 하나뿐이고,
// 이 리소스는 그 간선 위에 이름 · 차폐문 · 카메라 같은 정보를 얹기만 한다.
// (RoomA/RoomB 는 "어느 간선에 얹을지" 를 가리키는 열쇠이지 새 연결이 아니다 —
//  실제 그래프에 없는 조합을 적으면 CorridorNet 이 경고를 남기고 무시한다.)
[GlobalClass]
public partial class CorridorDef : Resource
{
    // 저장 · 조작에 쓰는 고정 id. 예: corridor_north
    [Export] public string SegmentId = "";
    [Export] public string DisplayName = "";

    // 이 메타데이터가 얹힐 간선. 순서는 상관없다(무향 간선).
    [Export] public string RoomA = "";
    [Export] public string RoomB = "";

    [Export] public CorridorKind Kind = CorridorKind.Minor;

    // 차폐문이 설치된 구간인가. false 면 아무리 골라도 닫히지 않는다.
    [Export] public bool IsBlockable;

    // 연동 복도 카메라 id(예: CAM-H01). 비어 있으면 카메라 없음.
    [Export] public string CctvCameraId = "";

    // 괴물이 중앙제어실로 접근할 때 쓰는 레인인가(PHASE B 에서 쓴다).
    [Export] public bool ThreatLane;

    // 문이 완전히 닫히거나 열리는 데 걸리는 시간(초).
    [Export] public float DriveSeconds = 0.8f;

    // 닫아 둘 수 있는 최대 시간(초). 이 시간이 지나면 자동으로 열린다 —
    // 문을 내려 두기만 해도 무적이 되는 것을 막는다. 0 이하면 제한 없음.
    [Export] public float MaxSealSeconds = 9f;

    // 한 번 열린 뒤 다시 닫기까지의 대기(초). 연타로 문을 깜빡이지 못하게 한다.
    [Export] public float ReengageCooldownSeconds = 3f;
}
