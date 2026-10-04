using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 관리자 패드 화면(홈 · 지침 · 단서 · 직원 · 잠금 알림) 캡처. 창 모드로 실행한다.
//
//   godot --path . res://scenes/debug/PadUiShot.tscn -- <저장 폴더>
//
// DAY2 근무에서 사고 예고 → 사고(스냅샷)를 실제 경로로 일으키고, 단서를 여러 장 찍은 뒤
// 패드의 각 화면을 두 가지로 남긴다: 플레이어 시점(screen_*.png) · 패드 화면 원본(pad_*.png).
public partial class PadUiShot : Node
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
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ctl.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.0);
        Day1HistoryOverlay.Instance?.CloseWindow();

        // CCTV 로 방 몇 곳을 지켜본다(시청 기록 = 단서 거리).
        foreach (string room in new[] { "core_room", "guard_room", "storage_room" })
        {
            sim.SetSurveillanceTarget(room);
            await Seconds(3.6);
        }

        // 사고 예고 — 설비 수리 업무를 띄워 고장 12초 전으로.
        GameState.Instance.AdvanceDayTime(Mathf.Max(0f, 35.5f - GameState.Instance.DayTimeSeconds));
        SpawnedTask st = null;
        for (int i = 0; i < 60 && (st = PadManualShot.FindNeglectTask(sim)) == null; i++) await Seconds(0.3);
        var pad = AdminPad3D.Instance;
        if (st != null)
        {
            sim.SetSurveillanceTarget(st.RoomId);
            st.Elapsed = st.TimeLimitSeconds - 13f;
            await Seconds(1.5);
            Save("desk_alert", pad, screen: true);     // 거치 중 — 홈 화면 + 예고 배너 점멸
            pad.Open();
            for (int i = 0; i < 300 && !pad.IsHeld; i++) await Frame();
            await Seconds(0.4);
            Save("home_alert", pad, screen: true);     // 홈 — 알림 배너
            pad.Close();
            for (int i = 0; i < 300 && pad.IsOpen; i++) await Frame();
            // 고장 — 보고 있던 방이라 스냅샷이 남는다.
            st.Elapsed = st.TimeLimitSeconds + 0.5f;
            await Seconds(2.5);
        }
        else GD.PrintErr("사고 예고를 만들지 못했다");

        // 단서 찍기 — 사고 줄 전부 + 이동 · CCTV 자료로 7장 이상.
        var rows = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(), GameState.Instance.CurrentDay);
        for (int i = 0; i < rows.Count; i++)
        {
            var ev = InterviewEvidenceBoard.FromLogRow(rows, i);
            if (ev?.Kind == EvidenceKind.Incident) ClueBoard.Pin(ev);
        }
        var all = roster.SelectMany(id => InterviewEvidenceBoard.BuildAll(id)).ToList();
        foreach (var (kind, n) in new[] { (EvidenceKind.Cctv, 2), (EvidenceKind.Movement, 2), (EvidenceKind.Overheard, 1) })
            foreach (var ev in all.Where(e => e.Kind == kind).Take(n)) ClueBoard.Pin(ev);
        // NO SIGNAL 카드 확인용 — 사고 사본을 보지 않던 방으로 바꿔 스냅샷 없이 핀(렌더 전용 가짜 단서).
        var inc = ClueBoard.Entries.FirstOrDefault(e => e.Evidence.Kind == EvidenceKind.Incident);
        if (inc != null)
        {
            var copy = inc.Evidence.Clone();
            copy.Id += ":nosignal";
            copy.SubjectRoomId = "storage_room";
            ClueBoard.Pin(copy);
        }
        GD.Print($"단서 {ClueBoard.Count}장 · 스냅샷 {ClueBoard.Entries.Count(e => e.Snapshot != null)}장");
        await Seconds(2.4);

        // 사고를 냈으니 수리 승인 요청이 패드를 덮고 있다 — 화면을 찍기 전에 닫는다.
        RepairApprovalSystem.ResetAll();
        pad.Open();
        for (int i = 0; i < 300 && !pad.IsHeld; i++) await Frame();
        await Seconds(0.4);
        var v = pad.View;
        Save("home", pad, screen: true);

        v.OpenApp(PadView.Tab.Manual, fade: false);
        await Seconds(0.4);
        Save("manual_chapters", pad, screen: true);
        foreach (var (ch, pg) in new[] { (0, 0), (2, 3), (4, 0) })
        {
            v.OpenChapter(ch);
            v.SetManualPage(pg);
            await Seconds(0.4);
            Save($"manual_ch{ch + 1}_p{pg + 1}", pad, screen: ch == 0);
        }

        v.OpenApp(PadView.Tab.Clues, fade: false);
        await Seconds(0.4);
        Save("clues_grid", pad, screen: true);
        var first = ClueBoard.Entries.FirstOrDefault(e => e.Snapshot != null) ?? ClueBoard.Entries.FirstOrDefault();
        if (first != null)
        {
            v.OpenDetail(first);
            await Seconds(0.5);
            Save("clue_detail", pad, screen: true);
        }

        v.OpenApp(PadView.Tab.Staff, fade: false);
        await Seconds(0.4);
        Save("staff_grid", pad, screen: true);
        v.OpenStaffDetail(roster.FirstOrDefault() ?? "");
        await Seconds(0.4);
        Save("staff_detail", pad, screen: true);

        // 거치대 위 — 근무 중에는 홈이 켜져 있다. 「단서」 아이콘을 실제 마우스 클릭으로 누르면
        // (제어실 컨트롤러 → 물리 피킹 → 패드 클릭 영역) 단서 앱을 연 채로 들어 올린다.
        pad.Close();
        for (int i = 0; i < 300 && pad.IsOpen; i++) await Frame();
        await Seconds(0.6);
        Save("desk_home", pad, screen: true);
        var cam = GetViewport().GetCamera3D();
        float k = pad.TargetViewport.Size.X / (float)pad.CanvasSize.X;
        Vector2 at = cam.UnprojectPosition(pad.ScreenPointWorld(new Vector2(560, 345) * k));
        GetViewport().PushInput(new InputEventMouseMotion { Position = at, GlobalPosition = at }, true);
        await Frame();
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at }, true);
        await Frame();
        await Frame();
        GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }, true);
        for (int i = 0; i < 300 && !pad.IsHeld; i++) await Frame();
        await Seconds(0.3);
        GD.Print(pad.IsHeld && v.Current == PadView.Tab.Clues
            ? "PASS  거치대 위 「단서」 아이콘 클릭 → 단서 앱을 연 채로 들었다"
            : $"FAIL  거치대 위 아이콘 클릭 (held={pad.IsHeld}, tab={v.Current}, at={at})");
        Save("desk_click_clues", pad, screen: true);

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    private void Save(string name, AdminPad3D pad, bool screen)
    {
        pad?.TargetViewport?.GetTexture()?.GetImage()?.SavePng($"{_dir}/pad_{name}.png");
        if (screen) GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/screen_{name}.png");
        GD.Print($"   {name}");
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
