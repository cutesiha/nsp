using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 대화창 이름띠 · 휴게실 지도 · 늑대 스탠딩 확인용 캡처 — 창 모드로 실행한다.
//
//   godot --path . res://scenes/debug/DialogueBandShot.tscn -- <저장 폴더>
//
// ① 통화창(근무 중)  ② 스토리 컷인(화면 위 · 모니터2 안)  ③ GUIDE-0 자막 띠
// ④ 휴게시간 모니터1 휴게실 지도  ⑤ 늑대 스탠딩(모니터2 안) 을 차례로 찍는다.
// 이름 줄과 이름띠의 실제 자리(화면 좌표)도 로그로 남긴다.
public partial class DialogueBandShot : Node
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

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance.GoToNextDay();
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);
        StoryCutinDirector.Instance?.Abort();
        // ⓪ 근무배치 오른쪽 화면 — 작업실 정보 표.
        var mapView = NSP.Ui.ScheduleMapView.Instance;
        var click = typeof(NSP.Ui.ScheduleMapView).GetMethod("OnRoomClicked", BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var rid in new[] { "medical_room", "power_room", "core_room" })
        {
            click?.Invoke(mapView, new object[] { rid });
            await Seconds(0.5);
            ControlRoom3DController.Instance?.ScheduleStaffViewport?.GetTexture()?.GetImage()?.SavePng($"{_dir}/00_room_{rid}.png");
        }
        var sim = FacilitySimulation.Instance;
        foreach (var id in sim.GetActiveEmployeeIds()) sim.AssignToRoom(id, "core_room");
        var map = NSP.Ui.ScheduleMapView.Instance;
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ControlRoom3DController.Instance.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);
        Day1HistoryOverlay.Instance?.CloseWindow();
        StoryCutinDirector.Instance?.Abort();
        ControlRoom3DController.Instance?.ClearFocus(0.1f);
        await Seconds(1.0);

        await Seconds(2.5);
        Shot("00b_seat_live");
        // ① 통화창.
        PhoneCallHud.Instance?.Open("wolf");
        await Seconds(1.6);
        Shot("01_call");
        PhoneCallHud.Instance?.RequestClose();
        await Seconds(0.6);

        // ② 스토리 컷인 — 화면 위(전체화면 자리).
        var screenBeat = new StoryBeat { BeatId = "debug_screen" }
            .Add("wolf", "발전기 출력, 정상 범위로 돌아왔습니다.", CutinSide.Left)
            .Add("rabbit", "진짜 시작하는 거예요?", CutinSide.Right);
        _ = StoryCutinDirector.Instance?.Play(screenBeat);
        await Seconds(2.4);
        Shot("02_story_screen");
        StoryCutinDirector.Instance?.Abort();
        StoryCutinHud.Instance?.HideNow();
        await Seconds(0.6);

        // ③ GUIDE-0 자막 띠.
        var sub = NSP.Prologue.GuideSubtitleHud.Instance;
        sub?.SetActive(true);
        sub?.SetLine("근무를 종료하고 나서, 직원들을 심문할 수 있습니다. /n우리는 괴물을 찾아야 하니까요.");
        await Seconds(0.8);
        Shot("04_guide_subtitle");
        sub?.Clear();
        sub?.SetActive(false);

        // ④ 휴게시간 — 모니터1 휴게실 지도.
        DebugEntryPoint.SetStage(flow, "Report");
        DebugEntryPoint.Call(flow, "RequestRestFromReport");
        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Rest; i++) await Frame();
        await Seconds(2.0);
        StoryCutinDirector.Instance?.Abort();
        ControlRoom3DController.Instance?.ClearFocus(0.1f);
        await Seconds(0.8);
        Shot("05_rest_seat");
        ControlRoom3DController.Instance?.FocusMonitor(1, 0.2f);
        await Seconds(0.8);
        Shot("06_rest_map");
        ControlRoom3DController.Instance?.RestRosterViewport?.GetTexture()?.GetImage()?.SavePng($"{_dir}/06b_rest_map_raw.png");

        // ⑤ 늑대 스탠딩 — 모니터2 안 스토리(휴게실 CCTV 위).
        ControlRoom3DController.Instance?.ClearFocus(0.1f);
        await Seconds(0.4);
        var beat = new StoryBeat { BeatId = "debug_wolf" }
            .Add("wolf", "둘이 붙었으니 끝난 겁니다.", CutinSide.Right)
            .Add("rabbit", "정말요?", CutinSide.Left);
        _ = StoryCutinDirector.Instance?.PlayOnMonitor(beat);
        await Seconds(2.5);
        ControlRoom3DController.Instance?.FocusMonitor(2, 0.2f);
        await Seconds(1.0);
        Shot("07_story_monitor_wolf");

        // 늑대 가면 칸 보정 세기 비교(StandingMaskLift) — 같은 머티리얼에 값만 바꿔 찍는다.
        var wolf = GD.Load<NSP.Data.EmployeeDef>("res://data/employees/wolf.tres");
        if (NSP.View.CrtArt.MaterialFor(wolf) is { } wm)
        {
            var wolfVp = StoryCutinHud.Instance?.RightPortrait?.GetViewport();
            foreach (float lift in new[] { 0.6f, 0.8f, 1f })
            {
                wm.SetShaderParameter("mask_lift", lift);
                await Seconds(0.4);
                wolfVp?.GetTexture()?.GetImage()?.SavePng($"{_dir}/07_sub_lift{lift:0.0}.png");
                Shot($"07_wolf_lift{lift:0.0}");
            }
            wm.SetShaderParameter("mask_lift", wolf.StandingMaskLift);
        }

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
