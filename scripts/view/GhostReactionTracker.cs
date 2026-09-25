using System.Collections.Generic;
using NSP.Facility;

namespace NSP.View;

// 괴물 반응의 "지금 몇 초째인가"를 직원마다 기억한다. **표현 전용이다 — 읽기만 한다.**
//
// CCTV 는 한 번에 한 방만 보여주지만, 반응은 보고 있든 말든 진행된다. 그래서 이 기록은
// 화면 밖의 직원까지 매 프레임 갱신한다. 양이 이미 주저앉은 뒤에 관리자가 그 방을 열면
// 주저앉는 장면을 다시 틀지 않고 웅크린 채 떠는 모습부터 보여준다.
//
// 기준은 "괴물 등장 1회"다. 같은 등장 동안에는 첫 반응을 한 번만 한다.
// 더 높은 상태(격리·기절·실제 이동·장기 공황)가 되면 반응도 회복도 즉시 끊는다.
public sealed class GhostReactionTracker
{
    public enum Stage { None, Reacting, Recovering }

    public sealed class State
    {
        public Stage Stage;
        public int Instance = -1;                 // 어느 등장에 대한 반응인가
        public int Serial;                        // 반응이 새로 시작될 때마다 오른다(방을 나갔다 들어와도)
        public CctvEmployeeAction Action;         // 그 사람의 반응 종류
        public float Elapsed;                     // 반응 시작 후(초)
        public float RecoverElapsed;              // 회복 시작 후(초)
    }

    private readonly Dictionary<string, State> _states = new();
    private int _instance;
    private int _serial;
    private bool _wasActive;
    private string _lastRoom = "";

    // 지금(또는 마지막) 괴물 등장의 번호. 등장할 때마다 1씩 오른다.
    public int Instance => _instance;

    public State Get(string employeeId) => _states.GetValueOrDefault(employeeId);

    public void Tick(FacilitySimulation sim, float delta)
    {
        if (sim == null) return;

        var ghost = sim.Ghost;
        bool active = ghost is { Active: true };
        string room = active ? ghost.ActiveRoomId : "";
        if (active && (!_wasActive || room != _lastRoom)) _instance++;
        _wasActive = active;
        _lastRoom = room;

        foreach (string id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (!_states.TryGetValue(id, out var s)) _states[id] = s = new State();

            // 화면과 똑같은 판단을 그대로 쓴다 — 우선순위를 여기서 따로 만들지 않는다.
            bool onShift = st is { Alive: true } && (st.Isolated || sim.IsOnDuty(id));
            var action = onShift
                ? CctvActionResolver.Resolve(sim, st, id, st.Isolated ? "isolation_room" : st.CurrentRoomId, "", "")
                : CctvEmployeeAction.Idle;

            if (GhostReactionProfiles.IsGhostReaction(action))
            {
                if (s.Stage != Stage.Reacting || s.Instance != _instance || s.Action != action)
                {
                    s.Stage = Stage.Reacting;
                    s.Instance = _instance;
                    s.Action = action;
                    s.Elapsed = 0f;
                    s.Serial = ++_serial;
                }
                else s.Elapsed += delta;
                continue;
            }

            bool canRecover = onShift && !st.Incapacitated && IsCalmWork(action);
            switch (s.Stage)
            {
                case Stage.Reacting:
                    // 괴물이 사라졌다(또는 이 사람이 그 방에 없다) — 바로 일로 끊지 않고 회복부터.
                    s.Stage = canRecover && (GhostReactionProfiles.Get(s.Action)?.RecoverSeconds ?? 0f) > 0f
                        ? Stage.Recovering : Stage.None;
                    s.RecoverElapsed = 0f;
                    break;
                case Stage.Recovering:
                    s.RecoverElapsed += delta;
                    if (!canRecover || s.RecoverElapsed >= (GhostReactionProfiles.Get(s.Action)?.RecoverSeconds ?? 0f))
                        s.Stage = Stage.None;
                    break;
            }
        }
    }

    // 회복 연출을 덮어써도 되는 평범한 상태. 이동·대화·방해공작·격리 같은 실제 상태는 그쪽이 먼저다.
    private static bool IsCalmWork(CctvEmployeeAction a) =>
        a is CctvEmployeeAction.Idle or CctvEmployeeAction.Working
          or CctvEmployeeAction.Repairing or CctvEmployeeAction.Inspecting;
}
