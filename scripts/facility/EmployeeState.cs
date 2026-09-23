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
    // 기절 — 스트레스 46 이상에서 발동. 의무실로 옮겨져 업무를 못 하다가
    // Config.StressFaintRecoverySeconds 가 지나면 회복해 원래 배치로 돌아간다.
    public bool Incapacitated = false;
    public float FaintRecoverTimer;
    public string CurrentRoomId;
    public string AssignedRoomId = "";
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
    public bool Isolated = false;
    public string PreIsolationRoomId = "";
    // 근무 시작 후 배치된 자리에 처음 도착했는지. 도착 전까지는 원래 속도로 걷고,
    // 도착한 뒤의 모든 이동(재배치·사고 확인 등)은 근무 중 저속으로 걷는다.
    public bool InitialDeployDone = false;

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
