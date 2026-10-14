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

// 관리자 패드 「지침」 그림 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/PadManualShot.tscn
//
// 실제 게임 화면을 찍어 res://assets/pad/manual/*.png(960×540)로 저장한다 — data/pad/manual.tres 가
// 이 파일 이름을 쓴다. 새 그림을 더 좋은 스크린샷으로 바꾸고 싶으면 같은 이름으로 덮어쓰면 된다.
// 저장한 뒤에는 에디터를 한 번 열거나 `godot --headless --import` 로 가져오기(import)를 돌린다.
public partial class PadManualShot : Node
{
    public const string Dir = "res://assets/pad/manual";
    private static readonly Vector2I Out = new(960, 540);

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Dir));
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.2);
        var sim = FacilitySimulation.Instance;
        var map = ScheduleMapView.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || map == null || ctl == null) { GD.PrintErr("씬이 뜨지 않았다"); GetTree().Quit(); return; }

        // DAY2 로 — 방치하면 고장 나는 업무(설비 수리)가 뜨는 첫날이다(개발 허브와 같은 경로).
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance.GoToNextDay();
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        // ── 배치 콘솔 ─────────────────────────────────────────────
        var roster = sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "medical_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Seconds(0.8);
        SaveScreen("schedule.png");

        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ctl.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Seconds(6.0);   // 이동 · 작업이 조금 쌓이게

        // ── 책상 전경 · 두 모니터 ──────────────────────────────────
        sim.SetSurveillanceTarget("core_room");
        await Seconds(1.0);
        SaveScreen("desk.png");
        SaveViewport(ctl.FacilityViewport, "facility_map.png");
        SaveViewport(ctl.CctvViewport, "cctv.png");

        foreach (var (room, file) in new[]
                 {
                     ("power_room", "cctv_power.png"), ("medical_room", "medical.png"),
                     ("vent_room", "vent.png"), ("isolation_room", "isolation.png"),
                 })
        {
            sim.SetSurveillanceTarget(room);
            await Seconds(1.2);
            SaveViewport(ctl.CctvViewport, file);
        }
        MakeAnomaly("cctv_power.png", "anomaly.png");
        sim.SetSurveillanceTarget("core_room");

        // ── 전화기 · 전력 패널 근접 ────────────────────────────────
        var cam = new Camera3D { Fov = 42f };
        AddChild(cam);
        var main = GetViewport().GetCamera3D();
        var phone = GetTree().Root.FindChild("Telephone", true, false) as Node3D;
        if (phone != null)
        {
            var p = phone.GlobalPosition;
            cam.GlobalPosition = p + new Vector3(0.16f, 0.26f, 0.34f);
            cam.LookAt(p + new Vector3(0f, 0.06f, 0f), Vector3.Up);
            cam.MakeCurrent();
            await Frames(4);
            SaveScreen("phone.png");
        }
        var panel = GetTree().Root.FindChild("PowerSwitchPanel", true, false) as Node3D;
        if (panel != null)
        {
            var b = panel.GlobalTransform.Basis;
            var p = panel.GlobalPosition;
            cam.GlobalPosition = p + b.Z.Normalized() * 0.42f + Vector3.Up * 0.16f;
            cam.LookAt(p + Vector3.Up * 0.08f, Vector3.Up);
            cam.MakeCurrent();
            await Frames(4);
            SaveScreen("power_panel.png");
        }
        main?.MakeCurrent();
        await Frames(3);

        // ── ☆ 핀이 찍힌 시설 로그 줄 ────────────────────────────────
        // 사고 하나 — 업무를 방치 시간 끝까지 밀어 실제 경로로 고장 나게 한다(그 방을 보고 있으니 스냅샷도 남는다).
        await ForceIncident(sim);
        var overlay = Day1HistoryOverlay.Instance;
        overlay?.OpenLog();
        await Seconds(0.5);
        var rows = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(), GameState.Instance?.CurrentDay ?? 1);
        var pin = rows.Select(InterviewEvidenceBoard.FromLogRow).LastOrDefault(e => e != null);
        if (pin != null) ClueBoard.Pin(pin);
        else GD.PrintErr("핀을 찍을 로그 줄이 없다");
        await Seconds(2.4);   // 토스트가 사라진 뒤
        SaveScreen("pin_log.png");
        overlay?.CloseWindow();
        await Seconds(0.4);

        // ── 이 패드 · 직원 앱 ───────────────────────────────────────
        var pad = AdminPad3D.Instance;
        if (pad != null)
        {
            pad.Open();
            for (int i = 0; i < 300 && !pad.IsHeld; i++) await Frame();
            pad.View?.OpenApp(PadView.Tab.Staff, fade: false);
            await Seconds(0.5);
            SaveScreen("staff_app.png");
            pad.Close();
            for (int i = 0; i < 300 && pad.IsOpen; i++) await Frame();
        }

        // ── 정산 · 휴게시간 · 심문 ─────────────────────────────────
        flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.SetStage(flow, "Ending");
        DebugEntryPoint.Call(flow, "EndShiftSequence");
        for (int i = 0; i < 1200 && DebugEntryPoint.Stage(flow) != "Report"; i++) await Frame();
        await Seconds(2.0);
        SaveViewport(ctl.ReportViewport, "report.png", alignTop: true);

        DebugEntryPoint.Call(flow, "RequestRestFromReport");
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Rest; i++) await Frame();
        await Seconds(2.5);
        SaveScreen("rest.png");

        // 심문 — 직원 하나를 고르고 수화기를 든다(실제 게임의 경로).
        var rosterView = RestRosterView.Instance;
        string who = roster.FirstOrDefault() ?? "";
        rosterView?.GetType().GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(rosterView, new object[] { who });
        await Seconds(0.6);
        Phone3D.Instance?.GetType().GetMethod("StartOutgoing", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(Phone3D.Instance, null);
        for (int i = 0; i < 600 && PhoneCallHud.Instance?.IsOpen != true; i++) await Frame();
        await Seconds(3.0);
        SaveScreen("interview.png");

        GD.Print($"saved → {ProjectSettings.GlobalizePath(Dir)}");
        GetTree().Quit();
    }

    // 화면 전체(플레이어 시점)를 960×540 으로.
    private void SaveScreen(string file) => Save(GetViewport().GetTexture().GetImage(), file);

    // CRT 한 대의 화면(4:3)을 16:9 로 잘라 저장(가운데 · 또는 위쪽 기준).
    private void SaveViewport(SubViewport vp, string file, bool alignTop = false) =>
        Save(vp?.GetTexture()?.GetImage(), file, alignTop);

    private static void Save(Image img, string file, bool alignTop = false)
    {
        if (img == null || img.IsEmpty()) { GD.PrintErr($"그림을 얻지 못했다: {file}"); return; }
        img = Crop169(img, alignTop);
        img.Resize(Out.X, Out.Y, Image.Interpolation.Lanczos);
        img.SavePng(ProjectSettings.GlobalizePath($"{Dir}/{file}"));
        GD.Print($"   {file}");
    }

    private static Image Crop169(Image img, bool alignTop)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        float want = 16f / 9f;
        if (Mathf.Abs((float)w / h - want) < 0.01f) return img;
        if ((float)w / h > want)
        {
            int nw = Mathf.RoundToInt(h * want);
            return img.GetRegion(new Rect2I((w - nw) / 2, 0, nw, h));
        }
        int nh = Mathf.RoundToInt(w / want);
        return img.GetRegion(new Rect2I(0, alignTop ? 0 : (h - nh) / 2, w, nh));
    }

    // 방치하면 고장 나는 업무 하나를 골라, 그 방을 CCTV 로 보면서 방치 시간 끝까지 민다.
    // 판정은 시뮬레이션이 평소대로 한다(고장 로그 · 사고 자료 · 스냅샷 모두 실제 경로).
    public static SpawnedTask FindNeglectTask(FacilitySimulation sim)
    {
        foreach (string room in sim.GetRoomIds())
            foreach (var st in sim.GetActiveTasksForRoom(room))
                if (st.Status == SpawnedTaskStatus.Active && !st.Recurring && !st.IsRepair
                    && sim.GetTaskDef(st.TaskId) is { HasNeglectConsequence: true })
                    return st;
        return null;
    }

    private async System.Threading.Tasks.Task ForceIncident(FacilitySimulation sim)
    {
        SpawnedTask st = FindNeglectTask(sim);
        // 아직 없으면 근무 시계를 다음 예정 업무(DAY2 설비 수리 36초) 직전까지 당긴다 — 발생은 시뮬레이션이 한다.
        if (st == null && GameState.Instance.DayTimeSeconds < 35.5f)
            GameState.Instance.AdvanceDayTime(35.5f - GameState.Instance.DayTimeSeconds);
        for (int i = 0; i < 60 && (st ??= FindNeglectTask(sim)) == null; i++) await Seconds(0.5);
        if (st == null) { GD.PrintErr("방치할 업무가 없다 — 사고를 만들지 못했다"); return; }
        sim.SetSurveillanceTarget(st.RoomId);
        await Seconds(1.2);
        st.Elapsed = st.TimeLimitSeconds + 0.5f;
        await Seconds(2.5);
    }

    // 이상 개체 — 스포일러가 없게 실루엣 + 노이즈로만. 발전실 CCTV 한 컷 위에 그린다.
    private static void MakeAnomaly(string baseFile, string file)
    {
        var img = Image.LoadFromFile(ProjectSettings.GlobalizePath($"{Dir}/{baseFile}"));
        if (img == null || img.IsEmpty()) return;
        img.Convert(Image.Format.Rgb8);
        int w = img.GetWidth(), h = img.GetHeight();
        var src = (Image)img.Duplicate();
        var rng = new RandomNumberGenerator { Seed = 7 };
        float cx = w * 0.60f, s = h;
        // 실루엣의 짙기(0~1) — 머리 · 좁은 어깨 · 길고 가는 몸 · 늘어진 두 팔. 가장자리는 흐리게.
        float Blob(float x, float y, float ox, float oy, float rx, float ry)
        {
            float dx = (x - ox) / rx, dy = (y - oy) / ry;
            return Mathf.Clamp((1.15f - Mathf.Sqrt(dx * dx + dy * dy)) / 0.35f, 0f, 1f);
        }
        float Figure(float x, float y) => Mathf.Max(
            Mathf.Max(Blob(x, y, cx, s * 0.29f, s * 0.045f, s * 0.055f), Blob(x, y, cx, s * 0.58f, s * 0.065f, s * 0.26f)),
            Mathf.Max(Blob(x, y, cx - s * 0.085f, s * 0.60f, s * 0.018f, s * 0.24f), Blob(x, y, cx + s * 0.085f, s * 0.62f, s * 0.018f, s * 0.25f)));
        for (int y = 0; y < h; y++)
        {
            bool band = rng.Randf() < 0.05f;
            int shift = band ? rng.RandiRange(-18, 18) : 0;   // 줄 밀림(신호 불안정)
            for (int x = 0; x < w; x++)
            {
                int sx = Mathf.Clamp(x + shift, 0, w - 1);
                var c = src.GetPixel(sx, y) * 0.42f;
                float n = rng.Randf();
                c = c.Lerp(new Color(n, n, n), band ? 0.5f : 0.16f);
                float f = Figure(sx, y) * (0.82f + 0.18f * rng.Randf());
                c = c.Lerp(new Color(0.01f, 0.012f, 0.016f), f * 0.92f);
                c.A = 1f;
                img.SetPixel(x, y, c);
            }
        }
        img.SavePng(ProjectSettings.GlobalizePath($"{Dir}/{file}"));
        GD.Print($"   {file}");
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
