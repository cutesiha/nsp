using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 정상 직원도 배치된 방 안에서 가끔 "평소와 조금 다른" 행동을 한다.
//
// 이게 없으면 설비 앞에 다가서는 행동 자체가 곧 범인 표시가 된다. 그래서 올빼미는
// 절차대로 패널을 점검하고, 고양이는 효율이 떨어진 이유를 확인하고, 토끼는 궁금해서
// 장비를 들여다본다 — 결번자의 전조와 **완전히 같은 모양**으로 남는다.
//
// 중요한 규칙 하나: **이 시스템은 직원을 움직이지 않는다.**
// 방 간 이동 권한은 오직 플레이어에게 있다. 여기서 하는 일은 배치된 자리 안에서의
// 짧은 행동뿐이며, 그 흔적은 CCTV 와 같은 방 직원의 기억에만 남는다.
public sealed class EmployeeBehaviorSystem
{
    // 디버그(§)에서만 읽는다. 플레이어 화면에는 절대 올리지 않는다.
    public static IReadOnlyList<string> DebugFalseLeads = new List<string>();

    private readonly List<string> _falseLeads = new();
    private readonly HashSet<string> _actedToday = new();
    private float _nextCheckAt;
    private int _movesToday;

    public int MovesToday => _movesToday;
    public IReadOnlyList<string> FalseLeads => _falseLeads;

    public void Reset()
    {
        _falseLeads.Clear();
        _actedToday.Clear();
        _nextCheckAt = 0f;
        _movesToday = 0;
        DebugFalseLeads = _falseLeads;
    }

    public void Tick(float delta, FacilitySimulation sim)
    {
        if (sim == null || !DayFeatures.AutoIncidentsEnabled) return;
        var ops = OpsProfile.Today;
        if (ops == null || _movesToday >= ops.MaxReactionMovesPerDay) return;

        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        if (_nextCheckAt <= 0f) _nextCheckAt = ops.ReactionMoveGapSeconds;
        if (now < _nextCheckAt) return;
        _nextCheckAt = now + ops.ReactionMoveGapSeconds;

        string pick = Pick(sim);
        if (string.IsNullOrEmpty(pick)) return;

        var st = sim.GetEmployeeState(pick);
        string room = st.CurrentRoomId;
        sim.MarkSuspiciousAction(room, pick);
        var seen = sim.RecordOddBehaviour(pick, room, Reason(pick));
        _actedToday.Add(pick);
        _movesToday++;

        _falseLeads.Add($"{Name(sim, pick)} {sim.RoomDisplayName(room)}에서 설비 확인" +
                        (seen.Count > 0 ? $" ({string.Join(",", seen.Select(x => Name(sim, x)))} 목격)" : ""));
        DebugFalseLeads = _falseLeads;
    }

    // 사고가 났을 때도 사람은 반응한다 — 다만 자리를 뜨지는 않는다.
    // 그 방에 있던 직원이 하던 일을 멈추고 설비 쪽을 본다.
    public void OnIncident(FacilitySimulation sim, string roomId, OpsProfileDef ops)
    {
        if (sim == null || ops == null || string.IsNullOrEmpty(roomId)) return;
        if (!DayFeatures.AutoIncidentsEnabled) return;

        var room = sim.GetRoomState(roomId);
        string watcher = room?.OccupantEmployeeIds.FirstOrDefault(id =>
            EmployeeTraits.Get(id).ObservationalAwareness >= 2
            || EmployeeTraits.Get(id).Curiosity >= 2);
        if (string.IsNullOrEmpty(watcher)) return;
        sim.MarkSuspiciousAction(roomId, watcher);
    }

    // 배치된 자리에서 설비를 들여다볼 만한 사람. 성향이 높을수록 자주 걸린다.
    private string Pick(FacilitySimulation sim)
    {
        string best = "";
        int bestScore = 0;
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (_actedToday.Contains(id)) continue;
            if (id == GameState.Instance?.SaboteurEmployeeId) continue;   // 결번자는 자기 계획이 있다
            var st = sim.GetEmployeeState(id);
            if (st is not { Alive: true, Isolated: false, Incapacitated: false }) continue;
            if (st.IsMoving || string.IsNullOrEmpty(st.AssignedRoomId)) continue;

            var tr = EmployeeTraits.Get(id);
            int score = tr.Curiosity * 2 + tr.ObservationalAwareness + tr.ReportsAnomaly;
            if (score <= bestScore) continue;
            bestScore = score;
            best = id;
        }
        return bestScore >= 5 ? best : "";
    }

    // 그 사람이라면 왜 설비 앞에 섰을까 — 전부 정상적인 이유다.
    private static string Reason(string employeeId) => employeeId switch
    {
        "owl" => "절차대로 설비 상태를 점검했다",
        "cat" => "효율이 떨어진 이유를 직접 확인했다",
        "jellyfish" => "소리가 나서 잠깐 하던 일을 멈췄다",
        "rabbit" => "궁금해서 장비를 들여다봤다",
        "crow" => "설비 앞에서 한동안 상태를 지켜봤다",
        _ => "주변을 한 번 둘러봤다",
    };

    private static string Name(FacilitySimulation sim, string employeeId) =>
        sim.GetEmployeeDef(employeeId)?.Codename ?? employeeId;
}
