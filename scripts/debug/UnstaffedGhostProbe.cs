using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 비워 둔 발전실의 무인 사고가 **귀신이 나와 있는 동안에도** 제때 터지는가 — 실제 근무 흐름 그대로.
//
//   godot --path . res://scenes/debug/UnstaffedGhostProbe.tscn -- [day N] [speed S]
//
// 2026-10-30 사용자 제보: 일부러 발전실을 비워 두었더니 사고 경고가 0초가 됐는데도 귀신이
// 떠날 때까지 사고가 나지 않았다. 발전실 근무자를 빼고, 남은 시간이 거의 없을 때 귀신을
// 세워 매초 상태를 찍는다. 반드시 창 모드로 돌린다.
public partial class UnstaffedGhostProbe : Node
{
    private const string Power = "power_room";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        if (DisplayServer.GetName() == "headless") { GD.PrintErr("창 모드로 돌릴 것."); GetTree().Quit(1); return; }
        var args = OS.GetCmdlineUserArgs();
        int day = 1; float speed = 3f;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "day") int.TryParse(args[i + 1], out day);
            if (args[i] == "speed") float.TryParse(args[i + 1], out speed);
        }

        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 2400 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        for (int d = 1; d < day; d++) GameState.Instance?.GoToNextDay();
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Frames(10);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 1800 && DebugEntryPoint.Stage(flow) != "Shift"; i++) await Frame();

        var sim = FacilitySimulation.Instance;
        var canStart = typeof(FacilitySimulation).GetMethod("CanStartNewIncident", BindingFlags.NonPublic | BindingFlags.Instance);

        // 발전실 근무자를 다른 방으로 뺀다 — 사용자가 한 그대로.
        foreach (string id in sim.GetEmployeeIds().ToList())
        {
            var st = sim.GetEmployeeState(id);
            if (st == null || st.AssignedRoomId != Power) continue;
            string to = sim.GetRoomIds().First(r => r != Power && sim.CanSendTo(r)
                && sim.GetEmployeeIds().Any(o => sim.GetEmployeeState(o)?.AssignedRoomId == r));
            sim.AssignToRoom(id, to);
            GD.Print($"   발전실 근무자 {id} → {to}");
        }

        GD.Print($"\n######## 단기 근무 DAY{GameState.Instance?.CurrentDay} — 발전실 비움 + 귀신 ########");
        Engine.TimeScale = speed;
        var def = sim.GetRoomDef(Power);
        float limit = RoomStaffing.UnstaffedAccidentSeconds(Power, def);
        GD.Print($"   발전실 무인 사고 한도 {limit:0.0}초");

        float lastPrint = -1f;
        bool forced = false, accident = false;
        float accidentAt = -1f, zeroSince = -1f;
        while (GameState.Instance?.CurrentPhase == GamePhase.Live)
        {
            await Frame();
            float now = GameState.Instance.DayTimeSeconds;
            var room = sim.GetRoomState(Power);
            float remaining = limit - room.UnstaffedTimer;
            if (!forced && !sim.Ghost.Active && remaining <= 8f && !sim.HasRepairPending(Power))
            {
                string at = sim.GetRoomIds().FirstOrDefault(r => r != Power && sim.IsRoomActive(r) && sim.OnDutyCount(r) > 0) ?? "";
                forced = sim.Ghost.ForceAppear(at, sim);
                GD.Print($"   @{now,6:0.0}  귀신 강제 등장 → {at} ({forced})");
            }
            if (remaining <= 0.01f && zeroSince < 0f && !sim.HasRepairPending(Power)) zeroSince = now;
            if (!accident && sim.HasRepairPending(Power))
            {
                accident = true; accidentAt = now;
                GD.Print($"   @{now,6:0.0}  ● 발전실 사고 발생 (0초 도달 후 {(zeroSince < 0 ? 0 : now - zeroSince):0.0}초)");
            }
            if (now - lastPrint >= 1f)
            {
                lastPrint = now;
                bool ok = (bool)canStart.Invoke(sim, null);
                var act = string.Join(", ", IncidentTracker.Active.Select(x => $"{x.RoomId}:{x.Title}{(x.IsAnomaly ? "(이상)" : "")}"));
                GD.Print($"   @{now,6:0.0}  발전실 남은 {remaining,5:0.0} · 인원 {sim.OnDutyCount(Power)} · 수리 {sim.HasRepairPending(Power)} · " +
                         $"새사고가능 {ok} · 귀신 {(sim.Ghost.Active ? sim.Ghost.ActiveRoomId : "-")} · 위협 {(sim.Threats.Current != null ? sim.Threats.Current.SegmentId : "-")} · " +
                         $"사고목록 [{act}] · 마지막사고 {IncidentTracker.LastIncidentAt:0.0}");
            }
            if (accident && now - accidentAt > 5f) break;
            if (now > 125f) break;
        }
        Engine.TimeScale = 1f;
        GD.Print(accident ? "   결과: 사고 발생" : "   결과: 사고가 끝내 나지 않음");
        GetTree().Quit();
    }

    private System.Threading.Tasks.Task Frames(int n)
    {
        async System.Threading.Tasks.Task Go() { for (int i = 0; i < n; i++) await Frame(); }
        return Go();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
}
