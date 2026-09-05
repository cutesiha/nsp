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
    // 기절 — 스트레스 46 이상에서 발동. 그날 근무가 끝날 때까지 어떤 업무도 처리하지 못한다.
    // (스트레스가 다시 내려가도 당일에는 복귀하지 않는다.)
    public bool Incapacitated = false;
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

    public List<ConversationTurn> ConversationHistory = new();
}
