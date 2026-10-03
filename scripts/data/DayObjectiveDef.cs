using Godot;
using Godot.Collections;

namespace NSP.Data;

// 오늘의 업무 한 줄이 무엇을 재는가.
// 새 판정 규칙을 만들지 않는다 — 전부 이미 돌고 있는 값(코어 복구율·경고 처리 수·
// 사고 수·작업실 상태·직원 상태)을 읽기만 한다.
public enum DayObjectiveType
{
    // 봉쇄 코어 '누적' 복구율(%)이 TargetValue 이상.
    CoreProgress,
    // 이번 근무에서 경고를 TargetValue 회 이상 안정화.
    WarningsResolved,
    // 경고 안정화 + 실제 고장 수리를 합쳐 TargetValue 회 이상 해결.
    IncidentsResolved,
    // 이번 근무에 발생한 실제 고장이 TargetValue 회 '이하'(적을수록 좋다).
    BreakdownsAtMost,
    // 근무를 마치는 시점에 TargetRooms 의 작업실이 전부 정상 가동.
    RoomsOperational,
    // 직원 전원이 근무 가능 상태(사망·격리·기절 없음).
    AllStaffAble,
    // 직원 전원 생존.
    AllStaffAlive,
}

[GlobalClass]
public partial class DayObjectiveDef : Resource
{
    [Export] public string ObjectiveId = "";
    [Export] public string DisplayText = "";
    [Export] public DayObjectiveType Type = DayObjectiveType.CoreProgress;
    [Export] public float TargetValue = 0f;
    // 필수 업무만 근무 종료 조건으로 쓴다. 선택 업무는 업무평가에만 반영된다.
    [Export] public bool Required = true;
    // RoomsOperational 전용 — 정상이어야 하는 작업실들.
    [Export] public Array<string> TargetRooms = new();
}
