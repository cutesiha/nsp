using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 정상 직원도 근무 중에 자리를 뜬다.
//
// 이게 없으면 "근무 중 자리를 옮긴 사람 = 결번자" 가 되어 로그 한 줄로 추리가 끝난다.
// 그래서 사고가 나면 성격에 따라 한두 명이 실제로 움직인다.
//
//   호기심이 많다(토끼)      → 사고 난 방을 직접 보러 간다
//   위험을 피한다(해파리)     → 사고 난 방에 있었다면 옆 방으로 물러난다
//   사람을 따라다닌다(여우)   → 대응하러 간 직원 쪽으로 따라 움직인다
//
// 전부 "정상적인 이유가 있는 이동"이고 시스템은 이것을 수상하다고 판정하지 않는다.
// 로그에는 결번자의 이동과 완전히 같은 모양으로 남는다 — 구분은 플레이어의 몫이다.
//
// 로그가 다시 복잡해지지 않게 하루 횟수(OpsProfileDef.MaxReactionMovesPerDay)와
// 간격을 데이터로 제한한다.
public sealed class EmployeeBehaviorSystem
{
    private sealed class Excursion
    {
        public string EmployeeId = "";
        public string HomeRoomId = "";
        public float ReturnAtSeconds;
        public bool Returned;
    }

    private readonly List<Excursion> _out = new();
    // 오늘 이미 한 번 자리를 뜬 직원. 같은 사람만 계속 돌아다니면 의심이 한쪽으로 쏠린다.
    private readonly HashSet<string> _movedOnce = new();
    private int _movesToday;
    private float _nextAllowedAt;
    private readonly List<string> _falseLeads = new();

    // 디버그(§19)에서만 읽는다. 플레이어 화면에는 절대 올리지 않는다.
    public static IReadOnlyList<string> DebugFalseLeads = new List<string>();

    public int MovesToday => _movesToday;
    public IReadOnlyList<string> FalseLeads => _falseLeads;

    public void Reset()
    {
        _out.Clear();
        _movedOnce.Clear();
        _movesToday = 0;
        _nextAllowedAt = 0f;
        _falseLeads.Clear();
        DebugFalseLeads = _falseLeads;
    }

    public void Tick(float delta, FacilitySimulation sim)
    {
        if (sim == null) return;
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;

        for (int i = _out.Count - 1; i >= 0; i--)
        {
            var ex = _out[i];
            if (ex.Returned) { _out.RemoveAt(i); continue; }
            if (now < ex.ReturnAtSeconds) continue;
            ex.Returned = true;
            var st = sim.GetEmployeeState(ex.EmployeeId);
            if (st is { Alive: true, Isolated: false } && st.CurrentRoomId != ex.HomeRoomId)
                sim.MoveEmployeeTo(ex.EmployeeId, ex.HomeRoomId);
        }
    }

    // 사고·경고가 발생한 순간 FacilitySimulation 이 불러 준다.
    // 여기서 "누가 어떻게 반응하는가"만 정하고, 이동 자체는 기존 경로를 그대로 쓴다.
    public void OnIncident(FacilitySimulation sim, string roomId, OpsProfileDef ops)
    {
        if (sim == null || ops == null || string.IsNullOrEmpty(roomId)) return;
        if (!DayFeatures.AutoIncidentsEnabled) return;
        if (_movesToday >= ops.MaxReactionMovesPerDay) return;

        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        if (now < _nextAllowedAt) return;
        // 근무 막바지에 내보내면 돌아오지 못한 채 근무가 끝난다.
        float dayLength = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        if (now > dayLength - ops.ReactionStaySeconds - 10f) return;

        // ① 위험을 피하는 성격이 사고 현장에 있으면 먼저 물러난다.
        if (TryAvoid(sim, roomId, ops, now)) return;
        // ② 호기심 많은 성격이 직접 확인하러 간다.
        TryApproach(sim, roomId, ops, now);
    }

    private bool TryAvoid(FacilitySimulation sim, string roomId, OpsProfileDef ops, float now)
    {
        var room = sim.GetRoomState(roomId);
        if (room == null) return false;

        foreach (string id in room.OccupantEmployeeIds.ToList())
        {
            if (!Eligible(sim, id, ops)) continue;
            if (EmployeeTraits.Get(id).AvoidsDanger < 3) continue;
            // 그 방을 완전히 비우면 대응 자체가 불가능해진다 — 혼자 있으면 버틴다.
            if (sim.OnDutyCount(roomId) <= 1) continue;

            string refuge = NeighborRoom(sim, roomId, id);
            if (string.IsNullOrEmpty(refuge)) continue;
            if (!Send(sim, id, refuge, ops, now)) continue;

            _falseLeads.Add($"{Name(sim, id)} 사고 현장 회피 이동 " +
                            $"{sim.RoomDisplayName(roomId)} → {sim.RoomDisplayName(refuge)}");
            return true;
        }
        return false;
    }

    private void TryApproach(FacilitySimulation sim, string roomId, OpsProfileDef ops, float now)
    {
        string pick = "";
        int bestScore = 0;
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (!Eligible(sim, id, ops)) continue;
            var st = sim.GetEmployeeState(id);
            if (st == null || st.CurrentRoomId == roomId) continue;

            var tr = EmployeeTraits.Get(id);
            int score = tr.Curiosity * 2 + tr.MovementTendency + tr.SocialMovement;
            // 오늘 이미 움직인 사람보다는 아직 자리를 지킨 사람이 다음 차례다.
            if (_movedOnce.Contains(id)) score -= 4;
            // 인접한 방에 있으면 가 보기 쉽다.
            if (NSP.Dialogue.DialogueContextBuilder.IsAdjacent(st.CurrentRoomId, roomId)) score += 2;
            // 자리를 비우면 그 방이 텅 비는 경우는 제외한다.
            if (sim.OnDutyCount(st.CurrentRoomId) <= 1 && sim.Warnings.HasActive(st.CurrentRoomId)) continue;
            if (score <= bestScore) continue;
            bestScore = score;
            pick = id;
        }

        // 성향이 낮은 사람까지 끌어내지는 않는다 — 아무도 안 움직이는 날도 있어야 한다.
        if (string.IsNullOrEmpty(pick) || bestScore < 6) return;
        if (!Send(sim, pick, roomId, ops, now)) return;

        _falseLeads.Add($"{Name(sim, pick)} 사고 확인 이동 → {sim.RoomDisplayName(roomId)}");
    }

    private bool Send(FacilitySimulation sim, string employeeId, string roomId, OpsProfileDef ops, float now)
    {
        var st = sim.GetEmployeeState(employeeId);
        if (st == null) return false;
        string home = string.IsNullOrEmpty(st.AssignedRoomId) ? st.CurrentRoomId : st.AssignedRoomId;
        if (!sim.MoveEmployeeTo(employeeId, roomId)) return false;

        _out.Add(new Excursion
        {
            EmployeeId = employeeId,
            HomeRoomId = home,
            ReturnAtSeconds = now + ops.ReactionStaySeconds,
        });
        _movedOnce.Add(employeeId);
        _movesToday++;
        _nextAllowedAt = now + ops.ReactionMoveGapSeconds;
        DebugFalseLeads = _falseLeads;
        return true;
    }

    // 지금 다른 일에 묶여 있지 않은 직원인가.
    private bool Eligible(FacilitySimulation sim, string employeeId, OpsProfileDef ops)
    {
        var st = sim.GetEmployeeState(employeeId);
        if (st is not { Alive: true, Isolated: false, Incapacitated: false }) return false;
        if (string.IsNullOrEmpty(st.AssignedRoomId)) return false;
        if (st.IsMoving) return false;
        if (_out.Any(x => x.EmployeeId == employeeId && !x.Returned)) return false;
        // 결번자가 이미 자기 계획대로 움직이는 중이면 겹치지 않게 둔다.
        if (employeeId == GameState.Instance?.SaboteurEmployeeId
            && sim.Saboteur.Phase is SaboteurPhase.Approaching or SaboteurPhase.Dwelling) return false;
        // 수리 중인 방을 지키고 있는 사람은 빼내지 않는다.
        if (sim.HasRepairPending(st.CurrentRoomId)) return false;
        return true;
    }

    private static string NeighborRoom(FacilitySimulation sim, string roomId, string employeeId)
    {
        var def = sim.GetRoomDef(roomId);
        if (def == null) return "";
        foreach (string next in def.ConnectedRoomIds)
        {
            if (!sim.IsRoomActive(next)) continue;
            var rd = sim.GetRoomDef(next);
            if (rd == null || rd.IsRestricted) continue;
            if (sim.HasRepairPending(next)) continue;
            return next;
        }
        return "";
    }

    private static string Name(FacilitySimulation sim, string employeeId) =>
        sim.GetEmployeeDef(employeeId)?.Codename ?? employeeId;
}
