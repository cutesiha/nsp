using Godot;

namespace NSP.Data;

// DAY1 근무의 "언제 어떤 업무가 발생하는가" 스케줄 한 줄. 기존 EmployeeDef / RoomDef /
// TaskDef / TabooDef 와 동일하게 [GlobalClass] Resource + .tres 로 관리한다.
// DAY1 프로토타입에서는 data/spawns/*.tres 를 SpawnAtSeconds 순으로 고정 실행한다
// (완전 랜덤화 금지 — 테스트 플레이어들이 비슷한 흐름을 겪게 한다).
[GlobalClass]
public partial class TaskSpawnDef : Resource
{
    // 발생시킬 TaskDef 의 TaskId.
    [Export] public string TaskId = "";

    // 근무 시작(DayTimeSeconds=0) 기준 몇 초 뒤에 발생하는가.
    [Export] public float SpawnAtSeconds = 0f;

    // true 면 상시 업무(코어 수리·자재 생산처럼 계속 돌아가는 것). 제한시간/실패 없이
    // 게이지가 차면 효과 적용 후 다시 0부터 순환한다.
    [Export] public bool Recurring = false;

    // 선택 필드. 비우면 TaskDef.RoomId 를 사용한다. 검증·가독성용으로만 채운다.
    [Export] public string RoomId = "";
}
