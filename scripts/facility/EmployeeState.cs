using System.Collections.Generic;
using Godot;

namespace NSP.Facility;

public class ConversationTurn
{
    public string Role;
    public string Text;
}

public class EmployeeState
{
    public string EmployeeId;
    // 스트레스 1~50. 46 이상이면 기절 상태가 되어 의무실로 강제 송환되고 당일 업무 불가.
    public float Stress = 1f;
    // 기절 — 스트레스 46 이상에서 발동. **스스로는 한 발짝도 움직이지 못한다.**
    // 다른 직원이 실제로 와서 업거나 부축하거나 안아서 의무실 침대에 눕혀야만
    // 회복이 시작된다(FaintRescueSystem).
    public bool Incapacitated = false;
    // 회복까지 남은 시간. 침대에 눕혀지기 전까지는 흐르지 않는다.
    public float FaintRecoverTimer;

    // 기절 흐름의 어느 단계인가. Incapacitated 하나로 "바닥인지 · 이송 중인지 ·
    // 침대인지" 를 추측하지 않기 위해 따로 둔다.
    public FaintPhase Faint = FaintPhase.None;
    public float FaintPhaseTimer;
    // 앞으로 쓰러졌는가(뒤로 쓰러지면 false). CCTV 가 쓰러지는 동작을 고를 때 쓴다.
    public bool FellForward = true;
    // 나를 발견해 상태를 살피는 사람 / 나를 옮기고 있는 사람. 각각 한 명뿐이다.
    public string ResponderId = "";
    public string TransporterId = "";
    // 관리자가 이 구조를 직접 지시했는가(직접 보냈다면 전화로 다시 묻지 않는다).
    public bool RescueWasDispatched;
    // 눕혀진 침대 자리(MedicalBedSpot1/2). 두 환자가 같은 침대를 쓰지 않게 한다.
    public string MedicalBedSpotId = "";

    // 내가 지금 업고 있는 환자. 비어 있지 않으면 업무도 전화도 하지 않는다.
    public string CarryingVictimId = "";
    // 환자를 눕힌 뒤 돌아갈 작업실.
    public string TransportReturnRoomId = "";
    // 관리자가 "쓰러진 사람이 있는 방" 으로 직접 보낸 경우 그 방 id.
    // 이 지시로 도착하면 전화로 다시 묻지 않고 바로 구조한다(직접 보낸 뜻이 분명하므로).
    public string RescueDispatchRoomId = "";
    public string CurrentRoomId;
    public string AssignedRoomId = "";
    // 근무가 시작된 순간의 배치(FacilitySimulation.RecordShiftStart). 대화 쪽 동선 시간표의 0초 위치다 —
    // 배치표 로그는 근무 시작 때 지워지고, AssignedRoomId 는 근무 중 재배치로 바뀌기 때문이다.
    // 그날 근무에 나오지 않았으면 빈 값.
    public string ShiftStartRoomId = "";
    public int ShiftStartDay = -1;
    public string TargetRoomId;
    public List<string> PathQueue = new();
    public Vector2 Position;
    public bool IsMoving;
    // 통로가 직선이 아닐 때(엘보 통로) 방 중심으로 직행하기 전에 먼저 들르는 꺾임 지점.
    // null이면 이번 구간은 직선 통로라 바로 방 중심으로 이동한다.
    public Vector2? ElbowWaypoint;
    public bool Alive = true;
    // 정전(CCTV 미가동) 중 사망은 즉시 발견되지 않는다 — 전력이 복구되는 순간에야 발견된다.
    // 평소(정전 아닐 때) 사망은 바로 발견되므로 기본값은 true.
    public bool DiscoveredDead = true;
    // 격리 — 명령을 받은 순간 true 가 된다(업무·방해공작에서 즉시 빠진다).
    // "실제로 침대에 묶였는가" 는 아래 Isolation 단계가 따로 들고 있다.
    public bool Isolated = false;
    public string PreIsolationRoomId = "";

    // 격리 절차의 앞단 — 명령을 받은 것과 격리실에 실제로 들어간 것은 다른 상태다.
    // 침대·압박밴드 단계는 CCTV 쪽(RoomWorkVisualController.Isolation.cs)이 따로 가진다.
    public IsolationPhase Isolation = IsolationPhase.None;
    public float IsolationPhaseTimer;
    // 근무 시작 후 배치된 자리에 처음 도착했는지. 도착 전까지는 원래 속도로 걷고,
    // 도착한 뒤의 모든 이동(재배치·사고 확인 등)은 근무 중 저속으로 걷는다.
    public bool InitialDeployDone = false;

    // 괴물이 사라진 직후 아직 업무에 손을 못 대는 시각(근무 시각, 초). 이 시각 전에는 업무 게이지에 기여하지 않는다.
    // GhostHauntSystem 이 소멸/사고 순간에 PostGhostRecovery 표로 건다. 새 근무가 시작되면 0.
    public float WorkBlockedUntil;

    // 오늘의 한마디(표시용). 기분과 함께 하루에 한 번 정해진다.
    public string DailyRemark = "";
    public string PreviousRemark = "";

    // 오늘의 기분상태 — 직원 본인이 근무 전에 적어 내는 자기보고다. 수치 스탯이 아니라
    // 플레이어가 배치 전에 읽는 추리 단서이며, 매 DAY DailyMoodSystem 이 다시 고른다.
    // 기분 자체에 대해서는 거짓말하지 않는다(약하게/모호하게 적을 수는 있다).
    public string DailyMood = "";
    // 어제 적어 낸 기분. 같은 표현이 연달아 반복되지 않게 하는 데만 쓴다.
    public string PreviousMood = "";

    public List<ConversationTurn> ConversationHistory = new();
}
