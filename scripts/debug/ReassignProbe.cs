using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.View;

namespace NSP.Debug;

// 임시 탐침 — "사고가 난 작업실로 / 에서 직원을 옮길 수 있는가" 를 DAY1 · DAY2 에서 각각 본다.
//
//   godot --headless --path . scenes/debug/ReassignProbe.tscn --quit-after 600
public partial class ReassignProbe : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        Probe(1);
        Probe(2);
        GD.Print("\n######## 탐침 끝 ########");
    }

    private void Tick(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private void Probe(int day)
    {
        GD.Print($"\n================ DAY{day} ================");
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
        TabooRuleSystem.Instance?.ActivateDailyTaboos(ControlRoom3DController.TodayTabooIds());

        var plan = new[] { ("core_room", 2), ("power_room", 1), ("maintenance_room", 1),
                           ("guard_room", 1), ("storage_room", 1) };
        var roster = _sim.GetActiveEmployeeIds().ToList();
        var ordered = new Dictionary<string, string>();
        int i = 0;
        foreach (var (room, n) in plan)
            for (int k = 0; k < n && i < roster.Count; k++, i++)
            {
                ordered[roster[i]] = room;
                _sim.AssignToRoom(roster[i], room);
            }

        GameState.Instance.AssignRandomSaboteur(roster);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        Tick(20f);   // 전원 자기 방에 도착

        const string acc = "power_room";
        GD.Print($"[사고 전] {acc} 인원={_sim.OnDutyCount(acc)} / 수리대기={_sim.HasRepairPending(acc)}");
        _sim.TriggerWarningFailure(acc, "발전기 점검");
        Tick(1f);
        DumpRoom(acc);

        // ① 다른 방 사람을 사고 난 방으로
        string donor = roster.FirstOrDefault(id => ordered.GetValueOrDefault(id) == "core_room");
        var dst = _sim.GetEmployeeState(donor);
        bool okIn = _sim.AssignToRoom(donor, acc);
        GD.Print($"\n① {donor}({dst?.CurrentRoomId}) → {acc} : AssignToRoom={okIn}");
        GD.Print($"   배치방={dst?.AssignedRoomId} 목표={dst?.TargetRoomId} 이동중={dst?.IsMoving} " +
                 $"경로={dst?.PathQueue.Count} 기절={dst?.Incapacitated} 격리={dst?.Isolated} 생존={dst?.Alive}");
        Tick(25f);
        GD.Print($"   25초 뒤: 현재방={dst?.CurrentRoomId} 이동중={dst?.IsMoving} / {acc} 인원={_sim.OnDutyCount(acc)}");

        // ② 사고 난 방 사람을 다른 방으로
        string inside = _sim.GetRoomState(acc)?.OccupantEmployeeIds.FirstOrDefault() ?? "";
        if (inside.Length == 0) { GD.Print($"\n② {acc} 에 사람이 없다 — 건너뜀"); return; }
        var ist = _sim.GetEmployeeState(inside);
        bool okOut = _sim.AssignToRoom(inside, "guard_room");
        GD.Print($"\n② {inside}({ist?.CurrentRoomId}) → guard_room : AssignToRoom={okOut}");
        GD.Print($"   배치방={ist?.AssignedRoomId} 목표={ist?.TargetRoomId} 이동중={ist?.IsMoving} " +
                 $"경로={ist?.PathQueue.Count} 기절={ist?.Incapacitated} 운반중={ist?.CarryingVictimId} " +
                 $"구조지시={ist?.RescueDispatchRoomId}");
        Tick(25f);
        GD.Print($"   25초 뒤: 현재방={ist?.CurrentRoomId} 배치방={ist?.AssignedRoomId} 이동중={ist?.IsMoving}");

        // ③ 사고 난 방에 **기절자**가 있을 때 (DAY2 부터 스트레스/기절이 열린다)
        GD.Print($"\n③ 기절 상황 — 스트레스 해금={DayFeatures.StressEnabled}");
        string victim = _sim.GetRoomState(acc)?.OccupantEmployeeIds.FirstOrDefault()
                        ?? roster.FirstOrDefault(id => _sim.GetEmployeeState(id)?.CurrentRoomId == acc) ?? "";
        if (victim.Length == 0)
        {
            // 아무도 없으면 한 명 보내 둔다.
            victim = roster.First(id => _sim.GetEmployeeState(id) is { Incapacitated: false, Alive: true });
            _sim.AssignToRoom(victim, acc);
            Tick(25f);
        }
        _sim.AddStress(victim, 60f, "탐침");
        Tick(2f);
        var vst = _sim.GetEmployeeState(victim);
        GD.Print($"   {victim}: 스트레스={vst?.Stress:0} 기절={vst?.Incapacitated} 현재방={vst?.CurrentRoomId}");

        bool okVictimOut = _sim.AssignToRoom(victim, "guard_room");
        GD.Print($"   ③-a 기절자를 다른 방으로: AssignToRoom={okVictimOut} (기절자는 스스로 못 움직인다)");

        string helper = roster.FirstOrDefault(id => id != victim
            && _sim.GetEmployeeState(id) is { Alive: true, Isolated: false, Incapacitated: false }
            && _sim.GetEmployeeState(id).CurrentRoomId != acc);
        var hst = _sim.GetEmployeeState(helper);
        bool okHelperIn = _sim.AssignToRoom(helper, acc);
        GD.Print($"   ③-b {helper}({hst?.CurrentRoomId}) → {acc}: AssignToRoom={okHelperIn} " +
                 $"구조지시={hst?.RescueDispatchRoomId}");
        Tick(40f);
        hst = _sim.GetEmployeeState(helper);
        GD.Print($"   40초 뒤: {helper} 현재방={hst?.CurrentRoomId} 배치방={hst?.AssignedRoomId} " +
                 $"운반중={hst?.CarryingVictimId} / {acc} 인원={_sim.OnDutyCount(acc)} 수리대기={_sim.HasRepairPending(acc)}");
        vst = _sim.GetEmployeeState(victim);
        GD.Print($"            {victim} 현재방={vst?.CurrentRoomId} 기절={vst?.Incapacitated}");
    }

    private void DumpRoom(string roomId)
    {
        var rs = _sim.GetRoomState(roomId);
        var def = _sim.GetRoomDef(roomId);
        GD.Print($"[사고 후] {roomId} 봉쇄={rs?.Locked} 전력={rs?.PowerOn} CCTV단절={rs?.CctvDisconnected} " +
                 $"정보왜곡={rs?.InfoDistorted} 제한구역={def?.IsRestricted} 오늘사용={_sim.IsRoomActive(roomId)} " +
                 $"배치가능={_sim.CanAssignToRoom(roomId)} 수리대기={_sim.HasRepairPending(roomId)} " +
                 $"점유={string.Join(",", rs?.OccupantEmployeeIds ?? new List<string>())}");
        GD.Print($"         길찾기 core_room→{roomId} = {_sim.FindPath("core_room", roomId).Count} 칸, " +
                 $"{roomId}→guard_room = {_sim.FindPath(roomId, "guard_room").Count} 칸");
    }
}
