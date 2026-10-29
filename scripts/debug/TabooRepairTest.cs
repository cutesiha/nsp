using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;

namespace NSP.Debug;

// DAY2 부터 걸리는 인원 금기(코어실 · 발전실 최대 1명)와 2인 수리가 같이 성립하는가.
//
//   godot --headless --path . res://scenes/debug/TabooRepairTest.tscn --quit-after 60000
//
// 전에는 수리하러 2명을 보내면 6초 뒤 금기가 터져(작업 속도 감소 · 발전실 금기 사건) 수리가
// 끝나지 않는 것처럼 보였다. 수리 중에는 금기가 쉬고, 수리가 끝난 뒤 2명이 남아 있으면 다시 걸린다.
public partial class TabooRepairTest : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;
    private int _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n################ 인원 금기 × 2인 수리 ################");
        Check("power_room", "taboo_power_headcount_limit");
        Check("core_room", "taboo_core_headcount_limit");
        GD.Print(_fail == 0 ? "\nALL PASS" : $"\n{_fail} FAIL");
        GetTree().Quit();
    }

    private void Check(string room, string tabooId)
    {
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        GameState.Instance.SetPhase(GamePhase.Live);
        TabooRuleSystem.Instance.ActivateDailyTaboos(new[] { tabooId });

        var def = _sim.GetRoomDef(room);
        int need = RoomStaffing.RepairMinWorkers(room, def);
        var ids = _sim.GetActiveEmployeeIds().Take(need).ToList();
        foreach (string id in ids) _sim.AssignToRoom(id, room);
        for (int i = 0; i < 6000 && ids.Any(x => _sim.GetEmployeeState(x).CurrentRoomId != room); i++) _sim.Tick(Step);

        _sim.TriggerTutorialAccident(room, need);
        RepairApprovalSystem.ResetAll();

        float t = 0f;
        while (t < 240f && _sim.HasRepairPending(room))
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);
            t += Step;
        }
        int during = Violations(room);
        Ok(need >= 2, $"{room}: 수리에 {need}명이 필요하다");
        Ok(!_sim.HasRepairPending(room), $"{room}: 2명이 수리를 끝냈다 ({t:0.0}초)");
        Ok(during == 0, $"{room}: 수리하는 동안 인원 금기가 터지지 않았다 (위반 {during})");

        // 수리가 끝났는데도 2명이 계속 남아 있으면 금기는 다시 걸린다.
        for (float s = 0f; s < 10f; s += Step) { _sim.Tick(Step); GameState.Instance.AdvanceDayTime(Step); }
        Ok(Violations(room) > during, $"{room}: 수리 후에도 2명이 남아 있으면 금기가 다시 걸린다");
    }

    private static int Violations(string room) =>
        EventLog.Instance?.GetAllEntries().Count(e => e.EventType == LogEventType.TabooViolation && e.RoomId == room) ?? 0;

    private void Ok(bool ok, string what)
    {
        if (!ok) _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
