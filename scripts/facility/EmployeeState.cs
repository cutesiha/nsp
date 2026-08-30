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
    public float Stress;
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

    public List<ConversationTurn> ConversationHistory = new();
}
