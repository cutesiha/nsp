using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 기존 게임 상태 → CCTV 표현 동작. **읽기만 한다.**
//
// 시뮬레이션·업무·방해공작 로직은 여기서 건드리지 않는다. 판정은 전부 그쪽에서 이미 끝났고,
// 이 파일은 그 결과를 보고 "화면에서 어떤 동작으로 보일지"만 고른다.
// (애니메이션이 게임 판정을 만들지 않는다 — 방향은 언제나 게임 → 화면 한 쪽이다.)
public static class CctvActionResolver
{
    // 방해공작이 실행된 직후 이 시간 동안만 suspicious 를 보여준다.
    private const float SabotageWindowSeconds = 3.5f;

    // 업무 종류 판별은 런타임 구조 필드를 먼저 쓴다(SpawnedTask.IsRepair).
    // 점검류는 구조상 구분이 없어 TaskId(데이터 키, 표시 이름 아님)로 고른다.
    private static readonly string[] InspectTaskIdParts = { "_check", "_review", "_monitoring" };

    public static CctvEmployeeAction Resolve(FacilitySimulation sim, EmployeeState st, string employeeId,
                                             string roomId, string talkingA, string talkingB)
    {
        if (sim == null || st == null) return CctvEmployeeAction.Idle;

        // 격리 — 다른 모든 행동보다 우선한다. 격리 중에는 talk/work/repair 로 바뀌지 않는다.
        if (st.Isolated) return CctvEmployeeAction.Isolated;

        // 기절 — 업무 불가 상태이므로 서 있는 것으로만 보인다.
        if (st.Incapacitated) return CctvEmployeeAction.Idle;

        // 방 사이 이동 중.
        if (st.IsMoving) return CctvEmployeeAction.Walking;

        // 방해공작이 '실행된' 짧은 순간. 기존 sabotage 이벤트를 읽기만 한다.
        var plan = sim.Saboteur;
        if (plan is { HasActed: true }
            && employeeId == GameState.Instance?.SaboteurEmployeeId
            && plan.ActedRoomId == roomId
            && (GameState.Instance?.DayTimeSeconds ?? 0f) - plan.ActedAtSeconds <= SabotageWindowSeconds)
            return CctvEmployeeAction.Suspicious;

        // 대화 중이어도 동작은 바꾸지 않는다 — 하던 업무를 계속하면서 이야기하는 쪽이 자연스럽다.
        // (talk 클립은 라이브러리에 그대로 남아 있다. 되살리려면 여기서 Talking 을 돌려주면 된다.)

        // 이 방에서 지금 진행 중인 업무.
        var task = sim.GetPrimarySpawnedTask(roomId);
        if (task != null)
        {
            if (task.IsRepair) return CctvEmployeeAction.Repairing;
            foreach (var part in InspectTaskIdParts)
                if (task.TaskId.Contains(part)) return CctvEmployeeAction.Inspecting;
            return CctvEmployeeAction.Working;
        }

        return CctvEmployeeAction.Idle;
    }
}
