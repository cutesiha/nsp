using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// NSP 개발 전용 허브. 이 씬 하나만 F6 로 실행하면 게임의 주요 상태로 곧장 들어간다.
//
// 원칙: 화면을 흉내 내지 않는다. 버튼은 실제 MainScene3D_Test 를 띄우고,
// 실제 ShiftFlowController / FacilitySimulation / 실제 View 를 그대로 쓴다.
// (여기서 만드는 것은 이 메뉴 하나뿐이다.)
//
//   F10  실행 중인 화면을 닫고 이 허브로 복귀
public partial class DeveloperHub : Node
{
    private const string MainScenePath = "res://scenes/main/MainScene3D_Test.tscn";

    private static readonly (string Label, string Path)[] StandaloneScenes =
    {
        ("작업실 CCTV / 작업 애니메이션", "res://scenes/debug/RoomCctvPreview.tscn"),
        ("직원 6인 애니메이션", "res://scenes/cctv_characters/preview/EmployeeCctvPreview.tscn"),
        ("배치 콘솔(ScheduleConsoleTest)", "res://scenes/debug/ScheduleConsoleTest.tscn"),
        ("휴게 증거(RestEvidenceTest)", "res://scenes/debug/RestEvidenceTest.tscn"),
        ("DAY1 운영(Day1OpsTest)", "res://scenes/debug/Day1OpsTest.tscn"),
        ("DAY2 흐름(Day2FlowTest)", "res://scenes/debug/Day2FlowTest.tscn"),
        ("엔딩 캡처(EndingShot · 커맨드라인)", "res://scenes/debug/EndingShot.tscn"),
    };

    private CanvasLayer _menuLayer;
    private Label _status;
    private Node _running;          // 지금 띄워 둔 MainScene
    private DevHubOverlay _overlay;
    // 진입 절차가 도는 중에 다른 버튼을 눌러도 두 절차가 겹치지 않게 하는 표.
    private int _generation;
    private int _day = 1;
    private readonly System.Collections.Generic.List<Button> _dayButtons = new();

    public override void _Ready()
    {
        BuildUi();
        SetProcessInput(true);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F10 }) ReturnToHub();
    }

    // --- 진입 ---------------------------------------------------------------

    private async void Enter(DebugEntryPoint.Request req)
    {
        int gen = ++_generation;
        if (_running != null) CloseRunning();
        DebugEntryPoint.Pending = req;
        DebugEntryPoint.BackupEndingState();
        _menuLayer.Visible = false;
        Log($"진입: {req.Label}");

        if (req.Phase == DebugEntryPoint.Phase.Title)
        {
            // 엔딩을 본 직후처럼 — 마지막 엔딩 기록을 임시로 덮고 타이틀부터 정상 부팅한다.
            // (원래 값은 F10 복귀 때 되돌린다.)
            EndingState.Record(req.TitleAfter);
            EndingState.PendingWake = req.TitleAfter;
            LoadMain();
            return;
        }

        // 타이틀 · 프롤로그는 건너뛴다(게임에 이미 있는 '건너뛰기' 경로를 그대로 쓴다).
        DebugEntryPoint.SkipTitleOnNextBoot();
        LoadMain();

        var flow = await WaitFor(() => DebugEntryPoint.FindFlow(GetTree()));
        if (gen != _generation) return;
        if (flow == null) { Log("!! ShiftFlowController 를 찾지 못했다"); return; }
        await WaitUntil(() => GameState.Instance?.CurrentPhase == GamePhase.Schedule);
        if (gen != _generation) return;

        // 요청한 날짜까지 '실제 진행 경로'로 올린다 — DayFeatures / OpsProfile /
        // Objective / 작업실 해금이 전부 CurrentDay 에서 나오므로 이것만으로 충분하다.
        DebugEntryPoint.CallStatic("StartNewRun", req.Day <= 0 ? 0 : 1);
        for (int d = 1; d < req.Day; d++) GameState.Instance?.GoToNextDay();
        DebugEntryPoint.Call(flow, "ClearAllAssignments");
        DebugEntryPoint.SeedState(req);

        // 엔딩은 배치를 거치지 않는다 — 그 자리에서 실제 EndingDirector 를 돌린다.
        if (req.Phase == DebugEntryPoint.Phase.Ending)
        {
            DebugEntryPoint.Call(flow, "StartEnding");
            Log($"{req.Label} · CORE {GameState.Instance?.CoreProgress:0.0}%");
            return;
        }

        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        await Frames(6);
        if (gen != _generation) return;
        if (req.Phase == DebugEntryPoint.Phase.Schedule) { Log(Where(req)); return; }

        // 근무 — 배치는 ControlRoom3DController.BeginShift() 안의 AutoStaff() 가 알아서 한다
        // (아무도 배치돼 있지 않을 때만 도는 실제 게임 코드다).
        DebugEntryPoint.Call(flow, "EnterShift");
        await WaitUntil(() => DebugEntryPoint.Stage(flow) == "Shift");
        if (gen != _generation) return;
        if (req.Phase == DebugEntryPoint.Phase.Shift) { Log(Where(req)); return; }

        // 보고서 — 잠깐 실제 근무를 돌려 증감이 남게 한 뒤 실제 종료 시퀀스를 탄다.
        await Wait(2.0);
        if (gen != _generation) return;
        GameState.Instance?.AddCoreProgress(5f, "디버그 근무");
        GameState.Instance?.AddMaterials(4);
        DebugEntryPoint.SetStage(flow, "Ending");
        DebugEntryPoint.Call(flow, "EndShiftSequence");
        await WaitUntil(() => DebugEntryPoint.Stage(flow) == "Report");
        if (gen != _generation) return;
        if (req.Phase == DebugEntryPoint.Phase.Report) { Log(Where(req)); return; }

        // 휴게 / 심문.
        DebugEntryPoint.Call(flow, "RequestRestFromReport");
        await Frames(30);
        Log(Where(req));
    }

    private string Where(DebugEntryPoint.Request req)
    {
        var gs = GameState.Instance;
        return $"{req.Label} · {DayFeatures.DayLabel(gs?.CurrentDay ?? 0)} · CORE {gs?.CoreProgress:0.0}% " +
               $"· 자재 {gs?.Materials} · 방해자 {Saboteur()}";
    }

    private static string Saboteur()
    {
        string id = GameState.Instance?.SaboteurEmployeeId ?? "";
        if (string.IsNullOrEmpty(id)) return "없음";
        return FacilitySimulation.Instance?.GetEmployeeDef(id)?.Codename ?? id;
    }

    private void LoadMain()
    {
        _running = GD.Load<PackedScene>(MainScenePath).Instantiate();
        AddChild(_running);
        // 실행 중 화면 위에 [DEV HUB] 복귀 버튼을 얹는다(허브로 들어왔을 때만 뜬다).
        _overlay = new DevHubOverlay();
        _overlay.ReturnRequested += ReturnToHub;
        AddChild(_overlay);
    }

    private void CloseRunning()
    {
        _generation++;                 // 진행 중이던 진입 절차를 무효로 만든다
        if (_running != null) { RemoveChild(_running); _running.QueueFree(); _running = null; }
        if (_overlay != null) { RemoveChild(_overlay); _overlay.QueueFree(); _overlay = null; }
    }

    private void ReturnToHub()
    {
        if (_running == null && _menuLayer.Visible) return;
        CloseRunning();
        // 테스트로 덮어썼던 user:// 진행 기록을 원래대로 돌려놓는다.
        DebugEntryPoint.RestoreEndingState();
        DebugEntryPoint.Pending = null;
        Sfx.Instance?.FadeOutMusic(0.2f);
        _menuLayer.Visible = true;
        Log("허브로 복귀");
    }

    private void Open(string scenePath)
    {
        if (!ResourceLoader.Exists(scenePath)) { Log("!! 없는 씬: " + scenePath); return; }
        DebugEntryPoint.Pending = null;
        GetTree().ChangeSceneToFile(scenePath);
    }

    // --- 대기 헬퍼 ----------------------------------------------------------

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task WaitUntil(System.Func<bool> done, int maxFrames = 900)
    {
        for (int i = 0; i < maxFrames && !done(); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<Node> WaitFor(System.Func<Node> get, int maxFrames = 900)
    {
        Node n = null;
        for (int i = 0; i < maxFrames && (n = get()) == null; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        return n;
    }

    private void Log(string text)
    {
        GD.Print("[DEV HUB] " + text);
        if (_status != null) _status.Text = text;
    }

    // --- UI (개발용이라 꾸미지 않는다) --------------------------------------

    private void BuildUi()
    {
        _menuLayer = new CanvasLayer { Layer = 200 };
        AddChild(_menuLayer);

        var bg = new ColorRect { Color = new Color(0.07f, 0.08f, 0.10f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menuLayer.AddChild(bg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.OffsetLeft = 24; scroll.OffsetTop = 18; scroll.OffsetRight = -24; scroll.OffsetBottom = -48;
        _menuLayer.AddChild(scroll);

        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(col);

        col.AddChild(Title("NSP DEVELOPER HUB", 26));
        col.AddChild(Hint("버튼을 누르면 실제 MainScene3D_Test 가 그대로 뜬다.  F10 = 이 화면으로 복귀"));

        // ── DAY 선택 ───────────────────────────────────────────────
        col.AddChild(Title("GAME FLOW", 18));
        var dayRow = Row(col);
        for (int d = 0; d <= 5; d++)
        {
            int day = d;
            var b = new Button { Text = d == 0 ? "DAY 0 (교육)" : $"DAY {d}", CustomMinimumSize = new Vector2(112, 34) };
            b.Pressed += () => SelectDay(day);
            dayRow.AddChild(b);
            _dayButtons.Add(b);
        }

        var phaseRow = Row(col);
        Btn(phaseRow, "배치", () => Enter(Make(DebugEntryPoint.Phase.Schedule, "배치")), 120);
        Btn(phaseRow, "근무", () => Enter(Make(DebugEntryPoint.Phase.Shift, "근무")), 120);
        Btn(phaseRow, "Shift Report", () => Enter(Make(DebugEntryPoint.Phase.Report, "근무 보고서")), 130);
        Btn(phaseRow, "휴게 / 심문", () => Enter(Make(DebugEntryPoint.Phase.Rest, "휴게/심문")), 130);
        col.AddChild(Hint("DAY0 은 배치를 누르면 GUIDE-0 교육이 실제로 시작된다(이후 단계는 교육이 이끈다). "
                        + "DAY5 는 휴게시간이 없어 보고서에서 곧장 엔딩으로 간다."));

        // ── 최종 ───────────────────────────────────────────────────
        col.AddChild(Title("FINAL", 18));
        var finalRow = Row(col);
        Btn(finalRow, "DAY5 FINAL REPORT — 성공(100%)",
            () => Enter(new DebugEntryPoint.Request
            { Day = 5, Phase = DebugEntryPoint.Phase.Report, Core = 95f, Label = "DAY5 최종 보고서(성공)" }), 250);
        Btn(finalRow, "DAY5 FINAL REPORT — 실패(78.7%)",
            () => Enter(new DebugEntryPoint.Request
            { Day = 5, Phase = DebugEntryPoint.Phase.Report, Core = 73.7f, Label = "DAY5 최종 보고서(실패)" }), 250);

        var endRow = Row(col);
        Btn(endRow, "TRUE END (코어 100%)",
            () => Enter(new DebugEntryPoint.Request
            { Day = 5, Phase = DebugEntryPoint.Phase.Ending, Core = 100f, Label = "TRUE END" }), 200);
        Btn(endRow, "BAD END (코어 83.7%)",
            () => Enter(new DebugEntryPoint.Request
            { Day = 5, Phase = DebugEntryPoint.Phase.Ending, Core = 83.7f, Label = "BAD END" }), 200);
        col.AddChild(Hint("엔딩은 실제 EndingDirector 를 그대로 돌린다 — 암전 · CRT 재부팅 · "
                        + "복구 시퀀스 · GUIDE-0 · 배너 · 최종 근무 기록까지 전부 나온다."));

        // ── 타이틀 ─────────────────────────────────────────────────
        col.AddChild(Title("TITLE", 18));
        var titleRow = Row(col);
        Btn(titleRow, "기본 타이틀", () => Enter(new DebugEntryPoint.Request
        { Phase = DebugEntryPoint.Phase.Title, TitleAfter = EndingState.Kind.None, Label = "기본 타이틀" }), 180);
        Btn(titleRow, "TRUE END 이후 타이틀", () => Enter(new DebugEntryPoint.Request
        { Phase = DebugEntryPoint.Phase.Title, TitleAfter = EndingState.Kind.True, Label = "TRUE 이후 타이틀" }), 200);
        Btn(titleRow, "BAD END 이후 타이틀", () => Enter(new DebugEntryPoint.Request
        { Phase = DebugEntryPoint.Phase.Title, TitleAfter = EndingState.Kind.Bad, Label = "BAD 이후 타이틀" }), 200);
        col.AddChild(Hint("눈 뜨는 연출(PendingWake)까지 포함한다. user:// 의 실제 진행 기록은 "
                        + "F10 으로 허브에 돌아오는 순간 원래 값으로 되돌린다."));

        // ── 프리뷰 ─────────────────────────────────────────────────
        col.AddChild(Title("VISUAL PREVIEW · 기존 테스트 씬", 18));
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 4);
        col.AddChild(grid);
        foreach (var (label, path) in StandaloneScenes)
        {
            string p = path;
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(250, 30) };
            b.Disabled = !ResourceLoader.Exists(p);
            b.Pressed += () => Open(p);
            grid.AddChild(b);
        }

        var bar = new PanelContainer();
        bar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bar.OffsetTop = -40; bar.OffsetLeft = 12; bar.OffsetRight = -12; bar.OffsetBottom = -6;
        _menuLayer.AddChild(bar);
        _status = new Label { Text = "준비됨", VerticalAlignment = VerticalAlignment.Center };
        bar.AddChild(_status);

        SelectDay(1);
    }

    private DebugEntryPoint.Request Make(DebugEntryPoint.Phase phase, string what) => new()
    {
        Day = _day,
        Phase = phase,
        // 화면을 확인하기 좋은 코어 복구율(개발용 값 — 밸런스와 무관하다).
        Core = _day <= 0 ? 0f : Mathf.Min(18f * (_day - 1), 95f),
        Label = $"{DayFeatures.DayLabel(_day)} {what}",
    };

    private void SelectDay(int day)
    {
        _day = day;
        for (int i = 0; i < _dayButtons.Count; i++)
        {
            bool on = i == day;
            _dayButtons[i].AddThemeColorOverride("font_color",
                on ? new Color(1f, 0.85f, 0.35f) : new Color(0.85f, 0.87f, 0.9f));
            _dayButtons[i].Text = (on ? "▶ " : "") + (i == 0 ? "DAY 0 (교육)" : $"DAY {i}");
        }
        Log($"선택한 DAY = {DayFeatures.DayLabel(day)}");
    }

    private static HBoxContainer Row(Node parent)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", 6);
        parent.AddChild(r);
        return r;
    }

    private static void Btn(Node parent, string text, System.Action onPressed, int width)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(width, 34) };
        b.Pressed += onPressed;
        parent.AddChild(b);
    }

    private static Label Title(string text, int size)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", new Color(0.62f, 0.92f, 1f));
        return l;
    }

    private static Label Hint(string text)
    {
        var l = new Label { Text = "  " + text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", new Color(0.62f, 0.66f, 0.72f));
        return l;
    }
}
