using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 책상 위 배치 확인용 — 앉은 자리에서 본 화면 한 장. 창 모드로 실행한다.
//
//   godot --path . res://scenes/debug/DeskLayoutShot.tscn -- <저장 폴더>
//
// 기기를 옮기거나 패드 각도(AdminPad3D.CradleLeanDeg)를 바꾸면 이걸 찍어
// MONITOR 01·02 아랫단이 가려지지 않는지 눈으로 확인한다.
public partial class DeskLayoutShot : Node
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
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance.GoToNextDay();
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        var map = ScheduleMapView.Instance;
        var roster = sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "core_room", "maintenance_room", "guard_room", "storage_room", "power_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Seconds(0.5);
        // 근무 배치 화면 — 오른쪽 모니터(직원 카드)의 스트레스 줄을 확인한다.
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/schedule.png");
        ctl.ScheduleStaffViewport?.GetTexture()?.GetImage()?.SavePng($"{_dir}/schedule_staff.png");
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ctl.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Seconds(1.2);

        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/desk_seated.png");

        // 기록 창 — 시설 로그(띠 시간표) · 대화 기록. 둘 다 홀로그램 창이어야 한다.
        var hist = Day1HistoryOverlay.Instance;
        DialogueHistory.Instance?.AddEntry("manager", "관리자", DialogueEntryType.PlayerChoice,
            "코어실 쪽 소리 들었나?", DialogueConversationType.OutgoingCall, "dog");
        DialogueHistory.Instance?.AddEntry("dog", "강아지", DialogueEntryType.NpcResponse,
            "들었습니다. 배관 쪽인 것 같은데, 지금 확인하러 가도 됩니까?",
            DialogueConversationType.OutgoingCall, "manager");
        DialogueHistory.Instance?.AddEntry("cat", "고양이", DialogueEntryType.NpcLine,
            "저는 계속 정비실에 있었어요. 아무도 안 지나갔습니다.",
            DialogueConversationType.IncomingCall, "manager");

        hist?.OpenLog();
        await Seconds(0.8);
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/window_log.png");
        hist?.OpenDialogue();
        await Seconds(0.8);
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/window_dialogue.png");
        hist?.CloseWindow();
        await Seconds(0.4);

        // ESC 일시정지 창 — 기록 창 · 설정 창과 같은 홀로그램 창이어야 한다.
        PauseMenu.Instance?.Open();
        await Seconds(0.6);
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/window_pause.png");
        PauseMenu.Instance?.Close();
        await Seconds(0.4);

        // 수리 승인 요청 · 미로가 거치대 위 패드에서 읽히는지도 같은 시점에서 본다.
        // 작은 교란 — 손댄 곳(정비실)과 증상이 난 곳(발전실)이 다르다.
        // 화면에 "TAMPER" 같은 시스템명이 뜨지 않고, 로그에는 결과 한 줄만 남아야 한다.
        sim.TriggerTamper("maintenance_room", sim.GetActiveEmployeeIds().First(), "power_room");
        await Seconds(1.0);
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/tamper_notice.png");
        hist?.OpenLog();
        await Seconds(0.8);
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/tamper_log.png");
        hist?.CloseWindow();
        await Seconds(0.4);

        sim.TriggerTutorialAccident("power_room", 2);
        for (int i = 0; i < 300 && RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking; i++) await Frame();
        await Seconds(0.6);
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/desk_approval.png");
        RepairApprovalSystem.Approve();
        await Seconds(0.6);
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/desk_maze.png");

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
