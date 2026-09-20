using Godot;
using Godot.Collections;

namespace NSP.Data;

[GlobalClass]
public partial class RoomDef : Resource
{
    [Export] public string RoomId = "";
    [Export] public string DisplayName = "";
    // 실제로 걸어서 오갈 수 있는 통로. 중앙 제어실이 모든 작업실의 허브다.
    [Export] public Array<string> ConnectedRoomIds = new();
    // 벽 하나를 사이에 둔 옆 작업실(소리·진동이 전해지는 범위).
    // 통로로 이어져 있지 않아도 옆방일 수 있어 따로 둔다. 비어 있으면 통로를 그대로 쓴다.
    [Export] public Array<string> AdjacentRoomIds = new();
    [Export] public Vector2 MapPosition = Vector2.Zero;
    [Export] public bool IsCoreRoom = false;
    [Export] public bool IsRestricted = false;
    [Export] public RoomResourceType ManagedResource = RoomResourceType.None;
    // LIGHTING이 꺼져도(정전 등) 이 방에 있는 직원 위치는 계속 보인다.
    [Export] public bool HasEmergencyLighting = false;

    // 이 작업실이 열리는 날. 1 이면 처음부터 사용한다. V3 초반 단순화로 환기실/의무실은
    // DAY1 에 잠겨 있다 — 배치 대상에서 빠지고, 업무도 사고도 발생하지 않는다.
    // 방 정의 자체는 그대로 남아 있으므로 값을 1 로 되돌리면 즉시 예전처럼 동작한다.
    [Export] public int UnlockDay = 1;

    // --- 무인 방치 사고 ------------------------------------------------------
    // 이 방에 근무자가 한 명도 없는 상태가 UnstaffedAccidentSeconds 를 넘기면 사고가 난다.
    // 0 이면 Config.UnstaffedAccidentSecondsDefault 를 쓴다. AccidentConsequence 를
    // 비워 두면(-1) 그 방은 무인 방치로 사고가 나지 않는다(중앙제어실·격리실).
    [Export] public string AccidentName = "";
    [Export] public float UnstaffedAccidentSeconds = 0f;
    [Export] public TabooConsequenceType AccidentConsequence = (TabooConsequenceType)(-1);
    [Export] public float AccidentAmount = 0f;
    // 사고 복구용 업무. RepairSeconds = 기술2·정상 스트레스 1명이 처리할 때 걸리는 초.
    [Export] public string RepairTaskId = "";
    [Export] public float RepairSeconds = 15f;
    [Export] public int RepairMinWorkers = 1;
}
