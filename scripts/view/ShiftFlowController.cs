using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Prologue;
using NSP.Taboo;
using NSP.Ui;

namespace NSP.View;

// 시작 화면 → 근무 배치 → 메인 근무 → 근무 종료/보고 → 휴게(인터뷰) → 다음 날 배치 …
// 를 "한 공간에서 이어지는" 루프로 묶는다. 씬 전환(ChangeSceneToFile)은 최종 결과로 갈 때
// 한 번만 쓴다. 같은 3D 중앙제어실 안에서:
//   - 시작 화면 : 어두운 제어실 + TitleOverlay, CRT OFF
//   - 근무 배치 : 두 CRT 가 배치 콘솔이 된다(왼쪽 = 시설 지도 배치 / 오른쪽 = 직원·작업실 정보)
//   - 근무 부팅 : 카메라 정면 복귀 + 조명 안정 + CRT 부팅 + "NIGHT SHIFT START"
//   - 근무 종료 : 왼쪽 CRT 가 ShiftReportView 로 전환("SHIFT COMPLETE")
//   - 휴게시간 : 왼쪽 CRT = RestRosterView(명단), 오른쪽 CRT = InterviewCCTVView(선택 직원).
//     실제 대화는 기존 Phone3D/PhoneCallHud/CallBubble 을 그대로 재사용.
//   - 다음 날  : Day < MaxDays 면 배치 단계로 복귀(같은 공간), 마지막 날이면 결과 화면으로.
// 게임 로직/시뮬레이션은 전혀 새로 만들지 않는다 — FacilitySimulation/GameState 그대로 사용.
public partial class ShiftFlowController : Node
{
    [Export] public NodePath ControllerPath = "..";
    [Export] public NodePath RigPath = "../PlayerSeatRig";
    [Export] public NodePath TitleOverlayPath = "../TitleOverlay";
    [Export] public NodePath DeskBoardPath = "../ControlRoom/DeskScheduleBoard";
    // 천장광 비활성 상태 — Lights 그룹이 숨겨져 있어 이 밝기 조절은 화면에 효과가 없다.
    [Export] public NodePath CeilingLightPath = "../ControlRoom/Lights/CeilingLight";
    [Export] public NodePath FillLightPath = "../ControlRoom/Lights/FillLight";
    [Export] public NodePath ArmsPath = "../ControlRoom/PlayerCharacter";
    // 타이틀 화면 2 — 중앙제어실 전체가 타이틀. 없으면 기존 TitleOverlay 메뉴로 되돌아간다.
    [Export] public NodePath TitleRoomPath = "../TitleRoomDirector";

    // 근무 배치 단계에서 책상 위를 치운다(배치표만 남긴다). 근무 시작 시 되돌린다.
    [Export] public NodePath[] DeskClutterPaths =
    {
        "../ControlRoom/Keyboard",
        "../ControlRoom/Telephone",
        "../ControlRoom/ControlPanel",
        "../ControlRoom/PowerSwitchPanel",
        "../ControlRoom/AdminPad",
    };
    [Export] public float BoardFocusDistance = 0.42f;

    // 새 게임을 프롤로그 + DAY0 교육부터 시작할지. 끄면 예전처럼 곧장 DAY1 배치로 들어간다
    // (개발 중 DAY1 만 반복 테스트할 때 인스펙터에서 끄면 된다).
    [Export] public bool PlayPrologue = true;

    // 개발 전용 — 다음에 뜨는 컨트롤러가 타이틀 · 프롤로그를 건너뛰고 곧장 DAY1 배치로 들어간다.
    // 게임 화면에는 이 값을 켜는 버튼이 없다(출시 빌드의 '건너뛰기' 버튼은 없앴다).
    // 개발 허브(DebugEntryPoint)와 scripts/debug 의 검사 · 캡처 씬만 리플렉션으로 켠다.
    // 한 번 쓰면 바로 꺼진다.
    private static bool _skipToDay1Pending;
    // 타이틀을 건너뛴 첫 배치 진입에서 "그 날의 안내"를 한 번만 건너뛴다(DebugEntryPoint 가 켠다).
    private static bool _skipScheduleIntroOnce;

    // Verdict = DAY5 마지막 절차(최종 격리 보고서). Rest/Report 다음, 엔딩 바로 앞이다.
    private enum Stage { Boot, Title, Prologue, Schedule, Booting, Shift, Ending, Report, Rest, Verdict, DayTransition, Final }
    private Stage _stage = Stage.Boot;

    private ControlRoom3DController _ctl;
    private SeatedCameraRig _rig;
    private TitleOverlay _title;
    private DeskScheduleBoard _board;
    private TitleRoomDirector _titleRoom;
    private OmniLight3D _ceiling, _fill;
    private Node3D _arms;
    private readonly System.Collections.Generic.List<Node3D> _clutter = new();
    private bool _wiredSchedule;
    private float _ceilBase = 1.1f, _fillBase = 0.4f;

    private bool _wiredViews;
    private float _coreAtShiftStart;
    private int _materialsAtShiftStart;

    public override void _ExitTree()
    {
        NSP.Facility.FacilitySimulation.Tampered -= OnTampered;
        NSP.Facility.FacilitySimulation.Sabotaged -= OnSabotaged;
    }

    public override void _Ready()
    {
        _ctl = GetNodeOrNull<ControlRoom3DController>(ControllerPath);
        _rig = GetNodeOrNull<SeatedCameraRig>(RigPath);
        _title = GetNodeOrNull<TitleOverlay>(TitleOverlayPath);
        _board = GetNodeOrNull<DeskScheduleBoard>(DeskBoardPath);
        _titleRoom = GetNodeOrNull<TitleRoomDirector>(TitleRoomPath);
        _ceiling = GetNodeOrNull<OmniLight3D>(CeilingLightPath);
        _fill = GetNodeOrNull<OmniLight3D>(FillLightPath);
        _arms = GetNodeOrNull<Node3D>(ArmsPath);
        foreach (var p in DeskClutterPaths)
        {
            var n = GetNodeOrNull<Node3D>(p);
            if (n != null) _clutter.Add(n);
        }

        if (_ceiling != null) { _ceilBase = _ceiling.LightEnergy; _ceiling.LightEnergy = _ceilBase * 0.26f; }
        if (_fill != null) { _fillBase = _fill.LightEnergy; _fill.LightEnergy = _fillBase * 0.3f; }

        GameState.Instance?.SetPhase(GamePhase.Prep);
        _ctl?.SetInputLocked(true);
        NSP.Facility.FacilitySimulation.Tampered += OnTampered;
        NSP.Facility.FacilitySimulation.Sabotaged += OnSabotaged;

        if (_title != null)
        {
            _title.StartRequested += OnStartPressed;
            _title.QuitRequested += () => GetTree().Quit();
            // TitleOverlay.UseLegacyTitle 이 꺼져 있으면 여기서 메뉴는 뜨지 않는다
            // (암전/배너용 레이어로만 남는다).
            _title.ShowTitle();
        }
        // 제어실 자체를 타이틀로 쓴다 — 두 CRT + 책상 장비가 메뉴 역할을 한다.
        bool skipBoot = _skipToDay1Pending;
        _skipToDay1Pending = false;
        // 엔딩 → 최종 기록 → [타이틀로] 로 돌아온 경우: 암전에서 시작해 "다시 눈을 뜬다".
        var wake = skipBoot ? EndingState.Kind.None : EndingState.PendingWake;
        EndingState.PendingWake = EndingState.Kind.None;
        if (wake != EndingState.Kind.None) _title?.FadeToBlack(0.01f);
        if (_titleRoom != null)
        {
            _titleRoom.StartRequested += OnStartPressed;
            if (!skipBoot) _titleRoom.Begin();
        }
        // 충격으로 깨어나는 엔딩(또 당한 것처럼) : BAD · LOOSE. 조용히 눈을 뜨는 엔딩 : TRUE · LATE.
        if (wake != EndingState.Kind.None)
            _ = WakeAtTitle(wake is EndingState.Kind.Bad or EndingState.Kind.Loose);
        // 예전 책상 위 종이 배치표. Phase 0 에서 배치는 CRT 콘솔(ScheduleMapView)로 옮겼다 —
        // 노드는 씬에 남아 있지만 켜지 않는다.
        _board?.SetActive(false);

        _stage = Stage.Title;
        // 시작 화면·근무 배치·휴게시간은 같은 곡으로 통일한다.
        // 실시간 근무에 들어갈 때만 페이드아웃되고 ControlRoomAtmosphere의 환경음이 대신한다.
        Sfx.Instance?.CrossfadeMusic("rest_time", 1.5f, loop: true);

        if (skipBoot) BootStraightToDay1();
    }

    // 개발 전용(_skipToDay1Pending) — 타이틀을 건너뛰고 DAY1 배치로 곧장 들어간다.
    private async void BootStraightToDay1()
    {
        // 컨트롤러의 AfterReady(CallDeferred)가 CRT 를 초기 상태로 돌려놓을 때까지 기다린다.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(this) || _stage != Stage.Title) return;

        // 다음 날 배치로 넘어갈 때와 같은 화면 상태 — 근무가 시작되면 EnterShift 가 다시 켠다.
        _ctl?.SetLeftScreen(_ctl.FacilityViewport);
        _ctl?.SetRightScreen(_ctl.CctvViewport);
        _ctl?.SetScreenBrightness(0.02f);
        AmbientOverlay.Instance?.SetSceneIntensity(0.15f);

        EnterSchedule();   // Stage.Title 에서 들어오면 StartNewRun(1) 로 DAY1 새 게임을 연다
    }

    private async System.Threading.Tasks.Task WakeAtTitle(bool harsh)
    {
        // 타이틀 부팅(CRT · 조명 세팅)이 먼저 끝나게 두 프레임 기다린다.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        var pd = PrologueDirector.Instance;
        if (pd != null) await pd.PlayWake(harsh, TitleRoomDirector.EndingScreenNoise);
        else _title?.FadeFromBlack(1.2f);
    }

    public override void _Process(double delta)
    {
        if (!_wiredViews) WireLateSignals();
        if (!_wiredSchedule && ScheduleMapView.Instance != null)
        {
            // 배치 콘솔도 CRT SubViewport 안에서 지연 생성된다 — 준비되면 한 번만 연결한다.
            ScheduleMapView.Instance.StartPressed += EnterShift;
            _wiredSchedule = true;
        }
        TickStressCautionHint();
        TickVentFaultHint();

        // 최대 근무시간이 다 되면 필수 업무를 못 끝냈어도 근무가 끝난다(막히지 않게).
        // 필수 업무를 끝냈다고 저절로 끝나지는 않는다 — 더 일할지는 플레이어가 고른다.
        if (_stage == Stage.Shift && GameState.Instance != null && !DayFeatures.IsTutorialDay)
        {
            if (GameState.Instance.DayTimeSeconds >= DayObjectives.MaxShiftSeconds)
            {
                _title?.FlashBanner("근무 가능 시간이 종료되었습니다", 44, 1.1);
                RequestEndShift();
            }
        }
    }

    // --- 스트레스 '주의' 최초 1회 안내 -------------------------------------

    // 표현 전용이다. 스트레스 수치도 구간 판정도 FacilitySimulation 이 이미 끝냈고,
    // 여기서는 "주의 구간에 들어간 직원이 생겼는가"만 읽어 GUIDE-0 한 줄을 띄운다.
    // 한 회차에 한 번만 뜬다 — 그 뒤로는 주의/위험이 몇 번이 되든 다시 말하지 않는다.
    private bool _stressHintShown;

    private void TickStressCautionHint()
    {
        if (_stressHintShown || _stage != Stage.Shift) return;
        // DAY0 교육과 스트레스가 잠겨 있는 날에는 뜨지 않는다.
        if (DayFeatures.IsTutorialDay || !DayFeatures.StressEnabled) return;

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        foreach (var id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st == null || !st.Alive || st.Isolated) continue;
            if (sim.StressBandName(st) != "주의") continue;

            _stressHintShown = true;   // 먼저 세운다 — 다음 프레임에 두 번 뜨지 않게.
            ShowStressCautionHint(sim.GetEmployeeDef(id)?.Codename ?? id);
            return;
        }
    }

    // ── 작은 교란 첫 안내 ───────────────────────────────────────────────
    //
    // 증상이 난 방과 원인이 시작된 방이 다른 교란이 **처음** 화면에 나타난 직후 한 줄만.
    // 하루에 한 번뿐이고, 같은 방에서 난 교란(원격이 아닌 것)은 설명할 것이 없으니 넘긴다.
    // GUIDE-0 는 범인을 모른다 — "누가 조작했다" 라고 말하지 않는다.
    private int _crossSignalSaidDay = -1;

    private void OnTampered(string originRoomId, string affectedRoomId)
    {
        if (_stage != Stage.Shift || DayFeatures.IsTutorialDay) return;
        if (originRoomId == affectedRoomId) return;              // 원격이 아니면 설명할 것이 없다
        int day = GameState.Instance?.CurrentDay ?? 0;
        if (_crossSignalSaidDay == day) return;
        _crossSignalSaidDay = day;
        _ = PlayGuideLines("ops_cross_signal");
    }

    // ── 첫 환기 정지 안내 ───────────────────────────────────────────────
    //
    // 환기실은 "비워 두면 스트레스가 오른다"가 아니다 — **고장이 나야** 오른다
    // (FacilitySimulation.TickVentilationFault). 그래서 설명도 그 고장이 실제로 난
    // 뒤에 한 번만 한다(문서 §19 — 상태 변화가 먼저, 설명은 그 다음).
    // 한 판에 한 번뿐이고, 스트레스가 잠긴 날과 교육일에는 뜨지 않는다.
    private static bool _ventHintSaid;

    public static void ResetVentFaultNotice() => _ventHintSaid = false;

    private void TickVentFaultHint()
    {
        if (_ventHintSaid || _stage != Stage.Shift) return;
        if (DayFeatures.IsTutorialDay || !DayFeatures.StressEnabled) return;
        if (GameState.Instance?.VentilationDown != true) return;
        _ventHintSaid = true;
        _ = PlayGuideLines("ops_vent_down");
    }

    // ── 첫 방해공작 안내 ────────────────────────────────────────────────
    //
    // "이건 그냥 고장이 아니다" 를 **처음 한 번만** 말한다(문서 §21). 한 판에 한 번뿐이고,
    // 교육(DAY0)에는 방해자가 없으므로 뜨지 않는다.
    //
    // GUIDE-0 는 범인을 모른다. 누구인지도, 몇 명인지도 말하지 않는다 —
    // 그 판단은 휴게시간에 관리자가 기록과 진술을 맞대어 직접 한다.
    private static bool _firstSabotageSaid;

    public static void ResetFirstSabotageNotice() => _firstSabotageSaid = false;

    private void OnSabotaged(string roomId)
    {
        if (_stage != Stage.Shift || DayFeatures.IsTutorialDay) return;
        if (_firstSabotageSaid) return;
        _firstSabotageSaid = true;
        _ = PlayGuideLines("ops_first_sabotage");
    }

    // ── 시스템 해금 안내 ────────────────────────────────────────────────
    //
    // 잠겨 있던 시스템이 열리는 날, 배치표 앞에서 한 묶음만 읽어 준다. 교육에서 미리 가르치지
    // 않는다 — 그날 쓸 수 없는 것을 먼저 배우면 정작 열렸을 때 기억나지 않는다.
    private static readonly System.Collections.Generic.HashSet<string> _unlockSaid = new();

    private void ShowUnlockGuideIfDue()
    {
        var gs = GameState.Instance;
        var cfg = Config.Instance?.Data;
        if (gs == null || cfg == null || DayFeatures.IsTutorialDay) return;
        if (gs.CurrentDay != cfg.StressUnlockDay) return;
        if (!_unlockSaid.Add($"stress|{gs.CurrentDay}")) return;
        _ = PlayGuideLines("unlock_stress");
    }

    // @guide 블록의 줄을 자막 띠로 차례대로 읽는다. 화면은 빼앗지 않는다.
    private async System.Threading.Tasks.Task PlayGuideLines(string guideId)
    {
        var block = NSP.Prologue.PrologueScript.GetGuide(guideId);
        if (block == null || block.Beats.Count == 0) return;
        var hud = NSP.Prologue.GuideSubtitleHud.Instance;
        var face = NSP.Prologue.GuideArt.Portrait("normal", out bool mouthless);
        NSP.Prologue.GuideCornerFace.SetPortraitAll("normal", face, mouthless);
        NSP.Prologue.GuideCornerFace.ShowAll(true);
        hud?.SetTopAligned(false);
        hud?.SetActive(true);
        Sfx.Instance?.Play("alert_beep3", -14f);

        // 부른 자리(배치 · 근무)에 머무는 동안만 이어 읽는다. 예전에는 조건이 Schedule 로
        // 고정돼 있어, 근무 중에 부르면 두 번째 줄부터 통째로 잘려 나갔다.
        var startedAt = _stage;
        foreach (var beat in block.Beats)
        {
            if (beat.Kind != NSP.Prologue.PrologueScript.GuideBeatKind.Line || string.IsNullOrEmpty(beat.Value)) continue;
            hud?.SetLine(beat.Value);
            await Wait(5.5);
            if (!IsInstanceValid(this) || _stage != startedAt) break;
        }
        if (!IsInstanceValid(this)) return;
        hud?.Clear();
        hud?.SetActive(false);
        NSP.Prologue.GuideCornerFace.ShowAll(false);
    }

    // 화면을 빼앗지 않는다. 근무를 그대로 두고 자막 띠 한 줄 + 구석 얼굴창만 잠깐 띄운다.
    // 문구는 코드가 아니라 런타임 문서(NSP_PROLOGUE_RUNTIME.md)가 소유한다.
    private async void ShowStressCautionHint(string employeeName)
    {
        string text = NSP.Prologue.PrologueScript.GetScripted("hint_stress_caution");
        if (string.IsNullOrEmpty(text)) return;

        var hud = NSP.Prologue.GuideSubtitleHud.Instance;
        var face = NSP.Prologue.GuideArt.Portrait("normal", out bool mouthless);
        NSP.Prologue.GuideCornerFace.SetPortraitAll("normal", face, mouthless);
        NSP.Prologue.GuideCornerFace.ShowAll(true);
        hud?.SetTopAligned(false);
        hud?.SetActive(true);
        hud?.SetLine(text.Replace("{NAME}", employeeName));
        Sfx.Instance?.Play("alert_beep3", -12f);

        await Wait(6.5);
        if (!IsInstanceValid(this)) return;
        // 교육이 자막 띠를 쓰고 있는 중이라면 건드리지 않는다(DAY0 에서는 애초에 안 뜬다).
        if (NSP.Prologue.TutorialDirector.Instance?.IsRunning == true) return;
        hud?.Clear();
        hud?.SetActive(false);
        NSP.Prologue.GuideCornerFace.ShowAll(false);
    }

    // 왼쪽/오른쪽 CRT 안의 View 들은 SubViewport 안에서 지연 생성되므로, 준비될 때까지
    // 매 프레임 확인하다가 한 번만 연결한다(ControlRoomInteraction 의 기존 관례와 동일).
    private void WireLateSignals()
    {
        if (FacilityMonitorView.Instance == null || ShiftReportView.Instance == null || RestRosterView.Instance == null)
            return;

        FacilityMonitorView.Instance.EndShiftRequested += RequestEndShift;
        if (Day1HistoryOverlay.Instance != null)
            Day1HistoryOverlay.Instance.EndShiftRequested += RequestEndShift;
        ShiftReportView.Instance.ContinueRequested += RequestRestFromReport;
        RestRosterView.Instance.NextRequested += RequestNextFromRest;
        _wiredViews = true;
    }

    // --- 시작 → 배치 -----------------------------------------------------

    // 시작 화면의 '근무 시작' — 프롤로그를 먼저 재생하고, 끝나면 DAY0 배치로 넘어간다.
    private void OnStartPressed()
    {
        if (_stage != Stage.Title) return;

        // 타이틀에서 시작하는 것은 새 게임이다. 이전 테스트에서 SetSaboteur를 썼더라도
        // 그 값이 남지 않게 모든 런 상태를 비운다.
        StartNewRun(PlayPrologue ? 0 : 1);
        _stressHintShown = false;
        // 시작 화면에 남아 있던 결말의 흔적(붉은 CRT / 아침빛)도 새 근무와 함께 지운다.
        EndingState.Clear();
        EndingState.PendingWake = EndingState.Kind.None;

        if (!PlayPrologue || PrologueDirector.Instance == null)
        {
            EnterSchedule();
            return;
        }

        _stage = Stage.Prologue;
        _title?.FadeOut();
        GameState.Instance?.SetPhase(GamePhase.Prep);
        Sfx.Instance?.FadeOutMusic(1.0f);
        _ctl?.SetInputLocked(false);
        // 프롤로그 동안 책상 위는 그대로 두고 두 CRT 만 쓴다.
        PrologueDirector.Instance.Finished += OnPrologueFinished;
        PrologueDirector.Instance.Play();
    }

    private void OnPrologueFinished()
    {
        if (PrologueDirector.Instance != null)
            PrologueDirector.Instance.Finished -= OnPrologueFinished;
        if (_stage != Stage.Prologue) return;
        _stage = Stage.DayTransition;   // EnterSchedule 의 진입 조건을 맞춘다
        EnterSchedule();
    }

    private void EnterSchedule()
    {
        if (_stage is not (Stage.Title or Stage.DayTransition)) return;

        // 프롤로그를 끄고 곧장 시작한 경우에도 새 게임 초기화는 반드시 한 번 지난다.
        if (_stage == Stage.Title)
        {
            StartNewRun(1);
            _stressHintShown = false;
        }

        _stage = Stage.Schedule;

        _title?.FadeOut();
        TabooRuleSystem.Instance?.ActivateDailyTaboos(ControlRoom3DController.TodayTabooIds());
        // 오늘의 기분상태는 근무 배치 화면에 들어오는 순간 하루에 한 번만 새로 정해진다.
        FacilitySimulation.Instance?.RollDailyMoods();
        // 어제 기절한 채로 날이 바뀐 직원은 배치표가 열리는 순간 의무실 자리를 차지한다.
        // (예전에는 일반 작업실에도 못 들어가고 의무실에도 없어서 진행이 막혔다.)
        FacilitySimulation.Instance?.PlaceFaintedInMedical();
        GameState.Instance?.SetPhase(GamePhase.Schedule);
        // 시작 화면부터 같은 곡을 이어 재생한다. 다음 날 배치 진입 때는 앞 단계에서 곡을
        // 페이드아웃했으므로 여기서 다시 루프로 시작한다.
        Sfx.Instance?.CrossfadeMusic("rest_time", 0.9f, loop: true);

        var lt = CreateTween();
        lt.SetParallel(true);
        if (_ceiling != null) lt.TweenProperty(_ceiling, "light_energy", _ceilBase * 0.72f, 1.1);
        if (_fill != null) lt.TweenProperty(_fill, "light_energy", _fillBase, 1.1);

        // 배치는 두 CRT 콘솔에서 한다 — 왼쪽 = 시설 지도 배치, 오른쪽 = 직원·작업실 정보.
        // 입력은 근무·휴게 때와 같은 CRT 경로(레이캐스트 → 화면)로 들어간다(숫자키 확대도 그대로).
        if (_arms != null) _arms.Visible = false;
        _ctl?.ScheduleMap?.Rebuild();
        _ctl?.SetLeftScreen(_ctl.ScheduleMapViewport);
        _ctl?.SetRightScreen(_ctl.ScheduleStaffViewport);
        // 0.02 는 "꺼진 화면" 이 아니라 "아주 어두운 화면" 이다 — 글자가 다 읽힌다.
        _ctl?.SetScreenBrightness(0f);
        var crt = CreateTween();
        crt.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 0f, 1.0f, 0.5)
           .SetTrans(Tween.TransitionType.Sine);

        // 자막 띠는 항상 화면 아래 같은 자리에 둔다(단계마다 옮기면 눈이 따라가지 못한다).
        NSP.Prologue.GuideSubtitleHud.Instance?.SetTopAligned(false);
        _ctl?.SetModalSurface(null);
        _ctl?.SetInputLocked(false);
        _rig?.ReturnToSeat(0.6f);

        // 개발 허브 · 캡처 도구는 타이틀을 건너뛰어 DAY1 배치로 들어온 뒤, 원하는 날짜로
        // 다시 StartNewRun → EnterSchedule 한다. 그 첫 진입의 '그 날의 안내'는 날짜가 아직
        // DAY1 이라 틀린 것이 나온다 — DAY0 교육으로 가려는데 DAY1 패드 안내가 떠서 패드가
        // 손에 올라오고, 그 상태로 교육이 시작돼 배치표를 누를 수 없었다.
        if (_skipScheduleIntroOnce) { _skipScheduleIntroOnce = false; }
        else _ = PlayDayIntro();
    }

    // 그 날 처음 보는 것들 — 메인 스토리가 먼저, 안내가 그 다음이다.
    //
    // 스토리(NSP_MAIN_STORY_DAY1_DAY5.md)는 배치에 손대기 전에 흐른다. 직원들이 서로
    // 나누는 말이고, 관리자는 CCTV 로 엿듣는 입장이다 — 그래서 관리자에게 말을 거는
    // 안내(패드 힌트 · 해금 안내)와 섞이면 안 되고, 반드시 끝난 뒤에 이어진다.
    private async System.Threading.Tasks.Task PlayDayIntro()
    {
        // 두 CRT 가 배치 화면으로 바뀌고 밝기 트윈(0.5초)이 끝날 때까지 기다린다.
        // 컷인은 "3D 관제 화면 위에" 뜨는 연출이라 배경이 아직 까만 상태에서 시작하면
        // 스탠딩만 허공에 떠 있는 그림이 된다.
        await Wait(0.8);
        if (!IsInstanceValid(this) || _stage != Stage.Schedule) return;

        await NSP.View.StoryBeatSelector.PlayDayStart(this);
        // 스토리가 흐르는 동안 플레이어가 근무를 시작해 버렸을 수도 있다.
        if (!IsInstanceValid(this) || _stage != Stage.Schedule) return;

        // DAY0 = GUIDE-0 가 진행하는 관리자 교육. 배치표가 열린 직후부터 시작한다.
        if (DayFeatures.IsTutorialDay) TutorialDirector.Instance?.BeginDay0();
        // DAY1 = 교육이 끝나고 처음 혼자 앉는 날. 패드를 꺼내 주고 "지침은 여기서 다시 본다"를 알린다.
        else if (GameState.Instance?.CurrentDay == 1) await ShowDay1PadHint();
        // 오늘 새로 열린 시스템이 있으면 그 자리에서 한 묶음만 읽어 준다.
        else ShowUnlockGuideIfDue();
    }

    // 배치표가 뜨고 화면이 밝아진 뒤, 패드를 꺼내며 그 위에 한 줄 안내를 띄운다.
    // 안내는 스스로 사라지고 패드는 손에 남는다 — 내려놓는 것은 관리자가 정한다.
    private async System.Threading.Tasks.Task ShowDay1PadHint()
    {
        var tree = GetTree();
        if (tree == null) return;
        // 화면이 밝아지는 연출(0.5초) 뒤에 꺼낸다.
        await ToSignal(tree.CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
        if (!IsInstanceValid(this) || _stage != Stage.Schedule) return;

        var pad = AdminPad3D.Instance;
        if (pad != null && pad.CanOpen()) pad.Open();
        // 패드가 손에 올라오는 동안 말풍선이 따라 올라온다.
        await ToSignal(tree.CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
        if (!IsInstanceValid(this) || _stage != Stage.Schedule) return;
        PadHintBubble.Show("근무 지침은 패드에서 다시 확인할 수 있습니다.", 2f);
    }

    private static void StartNewRun(int startDay)
    {
        var state = GameState.Instance;
        var sim = FacilitySimulation.Instance;
        if (state == null || sim == null) return;

        state.ResetRun(startDay);
        sim.ResetRun();
        EventLog.Instance?.ClearAll();
        DialogueHistory.Instance?.ClearAll();
        // 지난 판의 추궁 메모가 새 근무의 휴게실에 남아 있으면 안 된다.
        ConfrontMarks.Clear();
        // 한 판에 한 번만 뜨는 안내들 — 새 판에서는 다시 뜬다.
        ResetFirstSabotageNotice();
        ResetVentFaultNotice();
        // 메인 스토리도 마찬가지다. "이미 재생한 비트" 기록을 지워야 DAY1 자기소개부터 다시 흐른다.
        NSP.View.StoryBeatSelector.ResetRun();

        // DAY0 교육에는 방해자가 없다 — DAY1 근무가 시작될 때 ControlRoom3DController 가 뽑는다.
        if (!DayFeatures.SaboteurActive) return;

        state.AssignSaboteurForMode(sim.GetActiveEmployeeIds());
        string id = state.SaboteurEmployeeId;
        if (string.IsNullOrEmpty(id)) return;

        string name = sim.GetEmployeeDef(id)?.Codename ?? id;
        GD.Print($"[방해자가 배정 되었습니다: {name}]");
    }

    // --- 배치 → 근무 -----------------------------------------------------

    private async void EnterShift()
    {
        if (_stage != Stage.Schedule) return;
        _stage = Stage.Booting;
        Sfx.Instance?.FadeOutMusic(0.9f); // 근무배치 BGM 페이드아웃 — 근무화면엔 BGM 없음(환경음이 대신)

        _ctl?.SetModalSurface(null);
        NSP.Prologue.GuideSubtitleHud.Instance?.SetTopAligned(false);
        _rig?.ReturnToSeat(0.6f);
        foreach (var n in _clutter) n.Visible = true;
        if (_arms != null) _arms.Visible = true;

        await Wait(0.45);

        var lt = CreateTween();
        lt.SetParallel(true);
        if (_ceiling != null) lt.TweenProperty(_ceiling, "light_energy", _ceilBase, 0.6);
        if (_fill != null) lt.TweenProperty(_fill, "light_energy", _fillBase, 0.6);

        // 이번 근무의 시작 지점(코어/자재) — 종료 보고서에서 증감을 보여주기 위한 스냅샷.
        _coreAtShiftStart = GameState.Instance?.CoreProgress ?? 0f;
        _materialsAtShiftStart = GameState.Instance?.Materials ?? 0;

        Sfx.Instance?.Play("switch", -4f);
        var bt = CreateTween();
        bt.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 0.02f, 1.0f, 0.55)
          .SetTrans(Tween.TransitionType.Sine);

        _title?.FlashBanner("NIGHT SHIFT START");

        await Wait(0.75);
        _ctl?.BeginShift();

        // 근무 화면은 여기서 확실히 건다. (DAY0 는 오른쪽 CRT 가 GUIDE-0 안내 전용이다.)
        // 튜토리얼 대사 진행 상황과 무관하게 항상 실행되어야 모니터가 비지 않는다.
        if (_ctl != null)
        {
            _ctl.SetLeftScreen(_ctl.FacilityViewport);
            // 교육일에도 오른쪽은 CCTV 다 — GUIDE-0 는 그 화면 구석의 작은 창으로만 뜬다.
            _ctl.SetRightScreen(_ctl.CctvViewport);
        }
        // 근무는 언제나 코어실에서 시작한다. 그러지 않으면 배치 단계에서 마지막으로 눌러 본
        // 방(격리실 같은)이 그대로 남아, 근무 첫 화면이 아무 상관 없는 방이 된다.
        FacilitySimulation.Instance?.SetSurveillanceTarget(FacilitySimulation.CoreRoomIdPublic);
        // 「오늘은 무사히」가 보는 하루치 집계(기절 · 사망)를 여기서 0 으로 되돌린다.
        AchievementManager.Instance?.OnShiftStart();
        _stage = Stage.Shift;
    }

    // --- 근무 → 종료/보고 -------------------------------------------------

    private void RequestEndShift()
    {
        if (_stage != Stage.Shift) return;
        _stage = Stage.Ending;
        EndShiftSequence();
    }

    private async void EndShiftSequence()
    {
        // 미로를 푸는 중에 근무가 끝날 수 있다 — 그대로 두면 패드가 미로 화면에 멈춰 선다.
        RepairApprovalSystem.AbortForShiftEnd();

        // 선택 업무는 여기서 업무평가 점수로만 바뀐다 — 다음 날 능력치나 확률에는
        // 전혀 손대지 않는다(마지막 날 관리자 평가 등급에만 반영).
        int earned = DayObjectives.OptionalCompleted();
        if (earned > 0) GameState.Instance?.AddEvaluation(earned);

        // 5일 누적(최종 근무 기록 화면) — 오늘 기록은 다음 근무 시작에 지워지므로 지금 더해 둔다.
        int tabooToday = 0;
        foreach (var e in EventLog.Instance?.GetAllEntries() ?? new System.Collections.Generic.List<LogEntry>())
            if (e.EventType == LogEventType.TabooViolation) tabooToday++;
        GameState.Instance?.AddShiftTotals(tabooToday, IncidentTracker.OpenedCount);

        // 필수 업무를 못 끝낸 채 시간이 다 됐는가. 게임을 멈추지는 않지만 기록은 남는다.
        GameState.Instance?.RecordShiftObjectives(
            DayObjectives.RequiredTotal - DayObjectives.RequiredDone);

        // 「첫 야근」·「오늘은 무사히」 — 근무가 정상 종료된 이 지점에서 한 번 판정한다.
        AchievementManager.Instance?.NoteShiftEnded();

        // 개발용 — 오늘 결번 개체가 어떤 조건으로 움직였고 어떤 단서가 남았는지.
        FacilitySimulation.Instance?.PrintSaboteurDebug();

        GameState.Instance?.SetPhase(GamePhase.Settlement);
        AmbientOverlay.Instance?.SetSceneIntensity(0.1f);
        // 근무 정산에는 BGM 없음 — 완료 효과음(띠링!)만.
        Sfx.Instance?.Play("shift_complete", -3f);

        _title?.FlashBanner("SHIFT COMPLETE");

        await Wait(0.5);

        ShiftReportView.Instance?.Present(_coreAtShiftStart, _materialsAtShiftStart);
        await SwapScreensWithFlicker(() => _ctl?.SetLeftScreen(_ctl.ReportViewport));

        _stage = Stage.Report;
    }

    // --- 보고 → 휴게(인터뷰) -----------------------------------------------

    private async void RequestRestFromReport()
    {
        if (_stage != Stage.Report) return;
        // 마지막 날 — FINAL SHIFT REPORT 의 [계속] 은 휴게시간을 건너뛰고 최종 보고서로 간다.
        // 다만 대회용 3일은 마지막 날에도 휴게시간을 한 번 더 거친다(GameModes.FinalDayRest).
        // 그 휴게시간의 [최종 결과 확인 ▶] 이 RequestNextFromRest → EnterVerdict 로 잇는다.
        if ((GameState.Instance?.CurrentDay ?? 1) >= GameModes.MaxDays && !GameModes.FinalDayRest)
        {
            EnterVerdict();
            return;
        }
        _stage = Stage.Rest;

        GameState.Instance?.SetPhase(GamePhase.Rest);
        // 근무 정산 BGM 페이드아웃 + 휴게시간 BGM(rest_time) 페이드인.
        Sfx.Instance?.CrossfadeMusic("rest_time", 0.9f, loop: true, restartIfSame: true);

        var lt = CreateTween();
        lt.SetParallel(true);
        if (_ceiling != null) lt.TweenProperty(_ceiling, "light_energy", _ceilBase * 0.55f, 1.0);
        if (_fill != null) lt.TweenProperty(_fill, "light_energy", _fillBase * 0.7f, 1.0);

        bool finalDay = (GameState.Instance?.CurrentDay ?? 1) >= GameModes.MaxDays;
        RestRosterView.Instance?.Present(finalDay);

        await SwapScreensWithFlicker(() =>
        {
            _ctl?.SetLeftScreen(_ctl.RestRosterViewport);
            _ctl?.SetRightScreen(_ctl.InterviewViewport);
        });
    }

    // --- 휴게 → 다음 날 배치 / 최종 결과 -----------------------------------

    // DAY0 교육이 끝나면 TutorialDirector 가 직접 DAY1 로 넘긴다("그럼 이제, DAY 1 근무를 시작합니다").
    // 교육이 끝났다. 다른 날의 전환과 달리 여기서만 "눈을 감았다 뜨면 진짜 근무" 를 보여 준다.
    public async void AdvanceFromTutorial()
    {
        if (_stage != Stage.Rest) return;
        _stage = Stage.DayTransition;
        // 추궁 표식은 그날 심문의 메모다 — 하루가 끝나면 지운다.
        ConfrontMarks.Clear();
        // 교육(DAY0)에서 만들어진 대화 상태를 실전으로 끌고 가지 않는다.
        // 대부분 날짜로 걸러지지만, 남은 것이 DAY1 첫 심문의 말투·자료에 섞이면 원인을 찾기 어렵다.
        NSP.Dialogue.DialogueClaimState.ResetAll();
        NSP.Dialogue.PlayerKnownEvidence.ResetAll();
        NSP.Dialogue.DialoguePatternMemory.ResetAll();
        NSP.Dialogue.ShiftMemory.Invalidate();
        NSP.Dialogue.InterviewReplyPlanner.Reset();
        await PlayWakeIntoFirstShift();
    }

    // 컴퓨터가 꺼지듯 화면이 완전히 검게 죽고 → 눈을 감고 → **감은 동안** 다음 날 배치
    // 화면이 켜지고 → 눈을 뜬다.
    //
    // 순서가 핵심이다. 눈을 뜬 뒤에 화면이 켜지면 "화면이 바뀌었다" 로 읽히고,
    // 감은 동안 켜져 있어야 "눈을 떠 보니 다음 날이 시작돼 있었다" 가 된다.
    private async System.Threading.Tasks.Task PlayWakeIntoFirstShift()
    {
        // ① 전원이 꺼진다. 0.02 가 아니라 **0** 이어야 한다 — 0.02 는 글자가 다 읽힌다.
        Sfx.Instance?.Play("switch", -6f);
        Sfx.Instance?.FadeOutMusic(0.5f);
        var off = CreateTween();
        off.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 1f, 0f, 0.45)
           .SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.In);
        await Wait(0.55);
        _ctl?.SetScreenBrightness(0f);

        // ② 눈을 감는다.
        var lid = EyelidOverlay.Attach(this);
        if (lid != null) await lid.Close(0.8f);
        else await Wait(0.8);

        // ③ 감고 있는 동안 다음 날이 준비되고, 배치 화면이 켜진다.
        GameState.Instance?.GoToNextDay();
        ClearAllAssignments();
        if (_ceiling != null) _ceiling.LightEnergy = _ceilBase * 0.26f;
        if (_fill != null) _fill.LightEnergy = _fillBase * 0.3f;
        AmbientOverlay.Instance?.SetSceneIntensity(0.15f);
        EnterSchedule();               // 여기서 두 CRT 가 DAY1 배치로 바뀌고 밝기가 다시 올라간다
        Sfx.Instance?.Play("ding", -2f);
        Sfx.Instance?.Play("crt_on", -8f);
        await Wait(0.85);              // 화면이 다 켜질 때까지 감은 채로 기다린다

        // ④ 눈을 뜬다. 뜨는 동안 배너가 가운데에서 드러난다.
        _title?.FlashBanner($"DAY {GameState.Instance?.CurrentDay ?? 1} 근무 시작");
        if (lid != null) { await lid.Open(1.0f); lid.Done(); }
    }

    private void RequestNextFromRest()
    {
        if (_stage != Stage.Rest) return;

        bool finalDay = (GameState.Instance?.CurrentDay ?? 1) >= GameModes.MaxDays;
        if (finalDay)
        {
            EnterVerdict();
        }
        else
        {
            _stage = Stage.DayTransition;
            // 추궁 표식은 그날 심문의 메모다 — 하루가 끝나면 지운다.
            ConfrontMarks.Clear();
            AdvanceToNextDay();
        }
    }

    // --- DAY5 마지막 절차 : 최종 격리 보고서 -------------------------------

    // GUIDE-0 이 먼저 말하고(@guide final_report), 왼쪽 CRT 에 보고서가 뜬다.
    // 제출 전까지는 ESC · 뒤로가기 · 화면 확대 전환이 전부 막힌다(FinalReportView.IsOpen).
    private async void EnterVerdict()
    {
        if (_stage is Stage.Verdict or Stage.Final) return;
        _stage = Stage.Verdict;
        GameState.Instance?.SetPhase(GamePhase.Settlement);
        Sfx.Instance?.FadeOutMusic(1.0f);
        ConfrontMarks.Clear();

        var guide = NSP.Prologue.GuideHologramView.Instance;
        NSP.Prologue.GuideSubtitleHud.Instance?.SetActive(true);
        NSP.Prologue.GuideCornerFace.ShowAll(true);
        if (guide != null)
        {
            // 자막 띠는 **스스로 글자를 가져오지 않는다** — 홀로그램 창이 한 줄씩 띄울 때마다
            // LineShown 으로 받아 그린다(DAY0 교육이 쓰는 것과 같은 연결).
            // 이 구독이 없으면 띠를 켜 두어도 끝까지 비어 있고, GUIDE-0 의 마지막 안내가
            // 한 글자도 보이지 않은 채 지목 화면으로 넘어간다. 여기가 그 버그였다.
            void OnLine(string text) => NSP.Prologue.GuideSubtitleHud.Instance?.SetLine(text);
            guide.LineShown += OnLine;
            try
            {
                // "5일간의 근무가 종료되었습니다" 는 모드마다 날수가 다르다 — 대본의 {DAYS} 를 채운다.
                await NSP.Prologue.PrologueDirector.ShowGuide(guide, "final_report",
                    new System.Collections.Generic.Dictionary<string, string>
                    { ["DAYS"] = GameModes.MaxDays.ToString() });
                // 결번 개체가 이미 죽어 그 카드를 고를 수 없는 판 — 그 사실만 한 줄 덧붙인다.
                if (SaboteurAlreadyGone()) await NSP.Prologue.PrologueDirector.ShowGuide(guide, "final_report_gone");
            }
            finally { guide.LineShown -= OnLine; }
        }
        NSP.Prologue.GuideCornerFace.ShowAll(false);
        NSP.Prologue.GuideSubtitleHud.Instance?.SetActive(false);
        NSP.Prologue.GuideSubtitleHud.Instance?.Clear();
        if (!IsInstanceValid(this) || _stage != Stage.Verdict) return;

        var view = FinalReportView.Instance;
        if (view == null)
        {
            GD.PushWarning("ShiftFlowController: 최종 보고서 화면을 찾지 못해 바로 엔딩으로 넘어갑니다.");
            StartEnding();
            return;
        }
        view.Submitted += OnVerdictSubmitted;
        view.Present();
        await SwapScreensWithFlicker(() => _ctl?.SetLeftScreen(_ctl.VerdictViewport));
        _ctl?.FocusMonitor(1, 0.5f);
        _ctl?.SetFocusLocked(true);
    }

    // 지목 후보가 될 수 없는 경우 — 결번 개체가 이미 사망했다.
    private static bool SaboteurAlreadyGone()
    {
        var sim = FacilitySimulation.Instance;
        string id = GameState.Instance?.SaboteurEmployeeId ?? "";
        if (sim == null || string.IsNullOrEmpty(id)) return false;
        return !(sim.GetEmployeeState(id)?.Alive ?? true);
    }

    private async void OnVerdictSubmitted()
    {
        if (_stage != Stage.Verdict) return;
        var view = FinalReportView.Instance;
        if (view != null) view.Submitted -= OnVerdictSubmitted;
        await Wait(1.2);
        _ctl?.SetFocusLocked(false);
        _ctl?.ClearFocus(0.6f);
        await Wait(0.7);
        StartEnding();
    }

    // 엔딩 — 코어 100% × 최종 보고서의 지목으로 네 갈래(EndingDirector).
    // 끝나면 최종 근무 기록 → 변한 시작 화면.
    private void StartEnding()
    {
        if (_stage == Stage.Final) return;
        _stage = Stage.Final;
        _ctl?.SetFocusLocked(false);
        if (_arms != null) _arms.Visible = false;
        var ending = new EndingDirector();
        AddChild(ending);
        ending.Play(_ctl, _title);
    }

    private async void GoToFinalResult()
    {
        Sfx.Instance?.FadeOutMusic(0.9f);
        AmbientOverlay.Instance?.SetSceneIntensity(1f);
        _title?.FadeToBlack(0.7f);
        await Wait(0.8);
        GetTree().ChangeSceneToFile("res://scenes/result/ResultScreen.tscn");
    }

    private async void AdvanceToNextDay()
    {
        _title?.FadeToBlack(0.5f);
        Sfx.Instance?.FadeOutMusic(0.5f); // 휴게시간 BGM 페이드아웃(암전과 함께) — 배치 진입 시 다시 페이드인
        await Wait(0.55);

        GameState.Instance?.GoToNextDay();
        ClearAllAssignments();

        // CRT 를 다시 시설/CCTV 로 되돌리고, 다음 부팅 전까지는 꺼둔다.
        _ctl?.SetLeftScreen(_ctl.FacilityViewport);
        _ctl?.SetRightScreen(_ctl.CctvViewport);
        _ctl?.SetScreenBrightness(0.02f);
        AmbientOverlay.Instance?.SetSceneIntensity(0.15f);

        if (_ceiling != null) _ceiling.LightEnergy = _ceilBase * 0.26f;
        if (_fill != null) _fill.LightEnergy = _fillBase * 0.3f;

        _title?.FadeFromBlack(0.5f);
        await Wait(0.2);

        EnterSchedule();
    }

    private void ClearAllAssignments()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        foreach (var id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st != null && !string.IsNullOrEmpty(st.AssignedRoomId))
                sim.ClearAssignment(id);
        }
    }

    private async System.Threading.Tasks.Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    // CRT 안의 프로그램이 바뀌는 순간(보고서/휴게 전환) 짧은 노이즈로 "채널이 바뀐다"는
    // 느낌을 준다 — 완전히 다른 화면으로 컷 되는 느낌을 줄인다.
    private async System.Threading.Tasks.Task SwapScreensWithFlicker(System.Action swap)
    {
        if (_ctl == null) { swap(); return; }
        Sfx.Instance?.Play("switch", -8f);
        _ctl.SetScreenNoise(0.5f);
        await Wait(0.09);
        swap();
        await Wait(0.05);
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenNoise(v)), 0.5f, 0.020f, 0.3)
         .SetTrans(Tween.TransitionType.Sine);
    }
}
