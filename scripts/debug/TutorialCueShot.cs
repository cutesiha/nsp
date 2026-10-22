using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// DAY0 교육 연출 확인용 캡처 — 창 모드로 실행한다.
//
//   godot --path . res://scenes/debug/TutorialCueShot.tscn -- <저장 폴더>
//
// 교육 흐름을 처음부터 돌리지 않고, 교육이 쓰는 연출만 그 자리에서 하나씩 켜고 찍는다.
//   ① 배치 화면 화살표(토끼 카드 → 코어실)     ② 근무 화면 화살표(작업실 · 근무 종료 · 전화기)
//   ③ 스위치 기기 확대 각도 · POWER 글자 자리    ④ 레버 시연 · 차폐 시연
//   ⑤ 방해공작 시연(경보 + CCTV ERROR)           ⑥ 휴게실 토끼 아이콘 화살표
public partial class TutorialCueShot : Node
{
    private string _dir = "";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        // DAY1 배치 화면은 스토리 컷인이 모니터2 를 당겨 화살표 캡처를 가린다 — DAY2 로 들어간다.
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance.GoToNextDay();
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.2);

        // ① 배치 화면 — 토끼 카드 옆 → 코어실 옆.
        var map = ScheduleMapView.Instance;
        TutorialArrow.PointAt(() => TutorialArrow.BesideOnMonitor(map, map.RosterCardOf("rabbit")));
        await Seconds(0.9);
        Shot("01_arrow_roster");
        // 배치 화면에 들어오면 그날 스토리 컷인이 모니터2 를 당긴다 — 접고 자리로 돌아온다.
        NSP.View.StoryCutinDirector.Instance?.Abort();
        ctl.ClearFocus(0.2f);
        await Seconds(0.6);
        TutorialArrow.PointAt(() => TutorialArrow.BesideOnMonitor(map, map.CellOf("core_room")));
        await Seconds(0.9);
        Shot("02_arrow_core");
        TutorialArrow.Clear();

        var roster = sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "core_room", "guard_room", "power_room", "core_room", "guard_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Seconds(0.3);
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ctl.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Seconds(0.8);

        // ② 근무 화면 — 작업실 상자 · 근무 종료 버튼 · 전화기.
        var fm = FacilityMonitorView.Instance;
        TutorialArrow.PointAt(() => TutorialArrow.BesideOnMonitor(fm.Minimap, fm.Minimap.RoomBoxOf("power_room")));
        await Seconds(0.9);
        Shot("03_arrow_room_power");
        TutorialArrow.PointAt(() => TutorialArrow.BesideOnMonitor(fm.Minimap, fm.Minimap.RoomBoxOf("guard_room")));
        await Seconds(0.9);
        Shot("04_arrow_room_guard");
        TutorialArrow.PointAt(() => TutorialArrow.BesideOnMonitor(fm.EndShiftButton,
            new Rect2(Vector2.Zero, fm.EndShiftButton.Size)));
        await Seconds(0.9);
        Shot("05_arrow_endshift");
        TutorialArrow.PointAt(() => TutorialArrow.AtWorld(Phone3D.Instance.GlobalPosition + Vector3.Up * 0.16f));
        await Seconds(0.9);
        Shot("06_arrow_phone");
        TutorialArrow.Clear();
        await Seconds(0.4);
        Shot("07_seat");

        // ③ 스위치 기기 확대 — 위에서 내려다보는가, POWER 글자가 기기 위에 앉아 있는가.
        ctl.FocusPowerPanel(0.3f);
        await Seconds(1.0);
        Shot("08_power_focus");
        GameState.Instance.TriggerPowerAccident(1);
        await Seconds(1.2);
        Shot("09_power_focus_accident");

        // ④ 레버 시연 — 조명을 내린 순간 / 차폐 시연.
        PowerSwitchPanel.DemoSet(PowerConsumer.Lighting, false);
        await Seconds(0.6);
        Shot("10_power_light_off");
        PowerSwitchPanel.DemoSet(PowerConsumer.Lighting, true);
        await Seconds(0.4);
        var net = sim.Corridors;
        sim.SetCorridorSurveillance("corridor_west");
        net.Select("corridor_west");
        PowerSwitchPanel.DemoSet(PowerConsumer.Barrier, true);
        net.Seal("corridor_west", out string why);
        GD.Print($"   차폐 시연 — {(string.IsNullOrEmpty(why) ? "닫힘" : why)}");
        await Seconds(1.3);
        Shot("11_power_barrier_up");
        ctl.ClearFocus(0.3f);
        await Seconds(0.8);
        Shot("12_barrier_demo_seat");
        PowerSwitchPanel.DemoSet(PowerConsumer.Barrier, false);
        sim.SetSurveillanceTarget("core_room");
        await Seconds(1.0);

        // ⑤ 방해공작 시연.
        FacilityAlertHud.Instance?.ShowCriticalAlert("⚠ 발전 계통에서 비정상적인 손상이 감지되었습니다!");
        CCTVMonitorView.Instance?.SetDemoError(true);
        await Seconds(0.35);
        Shot("13_sabotage_alert");
        await Seconds(1.2);
        Shot("14_sabotage_error");
        CCTVMonitorView.Instance?.SetDemoError(false);
        await Seconds(0.5);

        // 모니터2 확대.
        ctl.FocusMonitor(2, 0.3f);
        await Seconds(0.8);
        Shot("15_focus_monitor2");
        ctl.ClearFocus(0.3f);
        GameState.Instance.RepairPowerAccident();
        await Seconds(0.6);

        // ⑥ 휴게실 — 토끼 아이콘 옆.
        DebugEntryPoint.SetStage(flow, "Report");
        DebugEntryPoint.Call(flow, "RequestRestFromReport");
        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Rest; i++) await Frame();
        await Seconds(2.0);
        // 교육이 실제로 쓰는 자리 계산(겹치지 않는 쪽 고르기)을 그대로 부른다.
        var aim = typeof(NSP.Prologue.TutorialDirector).GetMethod("RestCallArrowAim",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var dir = NSP.Prologue.TutorialDirector.Instance;
        TutorialArrow.PointAt(() => (TutorialArrow.Aim?)aim?.Invoke(dir, null));
        await Seconds(0.9);
        Shot("16_arrow_rest_rabbit");
        TutorialArrow.Clear();

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    private void Shot(string name)
    {
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/{name}.png");
        GD.Print($"   shot {name}");
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
