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
        "../ControlRoom/AlertTerminal",
    };
    [Export] public float BoardFocusDistance = 0.42f;

    // 새 게임을 프롤로그 + DAY0 교육부터 시작할지. 끄면 예전처럼 곧장 DAY1 배치로 들어간다
    // (개발 중 DAY1 만 반복 테스트할 때 인스펙터에서 끄면 된다).
    [Export] public bool PlayPrologue = true;

    // 테스트용 '프롤로그/튜토리얼 건너뛰기' 버튼(화면 오른쪽 위). 누르면 곧장 DAY1 근무 배치로 간다.
    // 출시 빌드에서 숨기려면 인스펙터에서 끈다.
    [Export] public bool ShowSkipButton = true;

    // 건너뛰기를 누르면 씬을 다시 불러오면서 이 값을 켠다 — 다시 뜬 컨트롤러가 타이틀 대신
    // 곧장 DAY1 배치로 들어간다. 진행 중인 프롤로그/교육의 비동기 흐름을 하나하나 끊는 대신
    // 씬째 새로 시작하는 편이 남는 상태(가이드 창 · 교육용 대사 후크 등)가 없어 안전하다.
    private static bool _skipToDay1Pending;
    private CanvasLayer _skipLayer;
    private Button _skipButton;

    private enum Stage { Boot, Title, Prologue, Schedule, Booting, Shift, Ending, Report, Rest, DayTransition, Final }
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
        if (wake != EndingState.Kind.None) _ = WakeAtTitle(wake == EndingState.Kind.Bad);
        // 예전 책상 위 종이 배치표. Phase 0 에서 배치는 CRT 콘솔(ScheduleMapView)로 옮겼다 —
        // 노드는 씬에 남아 있지만 켜지 않는다.
        _board?.SetActive(false);

        _stage = Stage.Title;
        // 시작 화면·근무 배치·휴게시간은 같은 곡으로 통일한다.
        // 실시간 근무에 들어갈 때만 페이드아웃되고 ControlRoomAtmosphere의 환경음이 대신한다.
        Sfx.Instance?.CrossfadeMusic("rest_time", 1.5f, loop: true);

        BuildSkipButton();
        if (skipBoot) BootStraightToDay1();
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

    // --- 테스트용: 프롤로그 / 튜토리얼 건너뛰기 ------------------------------

    private void BuildSkipButton()
    {
        if (!ShowSkipButton) return;
        _skipLayer = new CanvasLayer { Layer = 125 };   // 자막 띠(112) · 통화창(114) 위
        AddChild(_skipLayer);
        _skipButton = MonitorUi.Button("프롤로그 · 튜토리얼 건너뛰기  ▶▶", new Color(1f, 0.80f, 0.36f),
            ViewFont.Default, OnSkipPressed, ViewFont.FS(14));
        _skipButton.AnchorLeft = 1f; _skipButton.AnchorRight = 1f;
        _skipButton.OffsetLeft = -400f; _skipButton.OffsetRight = -20f;
        // 글자가 길어도 화면 밖(오른쪽)으로 자라지 않고 왼쪽으로 늘어난다.
        _skipButton.GrowHorizontal = Control.GrowDirection.Begin;
        _skipButton.OffsetTop = 16f; _skipButton.OffsetBottom = 58f;
        _skipButton.MouseFilter = Control.MouseFilterEnum.Stop;
        _skipButton.TooltipText = "테스트용 — 곧장 DAY1 근무 배치로 이동";
        _skipLayer.AddChild(_skipButton);
        RefreshSkipButton();
    }

    // 타이틀 · 프롤로그 · DAY0 교육 동안만 보인다. DAY1 배치에 들어서면 사라진다.
    private void RefreshSkipButton()
    {
        if (_skipButton == null) return;
        bool show = _stage is Stage.Title or Stage.Prologue
                    || (DayFeatures.IsTutorialDay && _stage != Stage.Boot);
        if (_skipButton.Visible != show) _skipButton.Visible = show;
    }

    private void OnSkipPressed()
    {
        // 씬 밖에 남는 정적 상태를 먼저 걷는다(가이드 얼굴창 · 입 · 교육용 대사 후크 · 통화 입).
        GuideCornerFace.ShowAll(false);
        GuideCornerFace.SetLifted(false);
        GuideMouthAnimator.Reset();
        EmployeeMouthAnimator.Reset();
        NSP.Dialogue.LocalDialogueGenerator.ScriptedAnswerOverride = null;
        Sfx.Instance?.StopVoiceBlip();
        GameState.Instance?.SetPhase(GamePhase.Prep);

        _skipToDay1Pending = true;
        GetTree().ReloadCurrentScene();
    }

    // 다시 뜬 씬에서 타이틀을 건너뛰고 DAY1 배치로 곧장 들어간다.
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

    public override void _Process(double delta)
    {
        if (!_wiredViews) WireLateSignals();
        if (!_wiredSchedule && ScheduleMapView.Instance != null)
        {
            // 배치 콘솔도 CRT SubViewport 안에서 지연 생성된다 — 준비되면 한 번만 연결한다.
            ScheduleMapView.Instance.StartPressed += EnterShift;
            _wiredSchedule = true;
        }
        RefreshSkipButton();
        TickStressCautionHint();

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
        _ctl?.SetScreenBrightness(0.02f);
        var crt = CreateTween();
        crt.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 0.02f, 1.0f, 0.5)
           .SetTrans(Tween.TransitionType.Sine);

        // 자막 띠는 항상 화면 아래 같은 자리에 둔다(단계마다 옮기면 눈이 따라가지 못한다).
        NSP.Prologue.GuideSubtitleHud.Instance?.SetTopAligned(false);
        _ctl?.SetModalSurface(null);
        _ctl?.SetInputLocked(false);
        _rig?.ReturnToSeat(0.6f);

        // DAY0 = GUIDE-0 가 진행하는 관리자 교육. 배치표가 열린 직후부터 시작한다.
        if (DayFeatures.IsTutorialDay) TutorialDirector.Instance?.BeginDay0();
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

        // DAY0 교육에는 방해자가 없다 — DAY1 근무가 시작될 때 ControlRoom3DController 가 뽑는다.
        if (!DayFeatures.SaboteurActive) return;

        state.AssignRandomSaboteur(sim.GetActiveEmployeeIds());
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

        // 개발용 — 오늘 결번자가 어떤 조건으로 움직였고 어떤 단서가 남았는지.
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
        // 마지막 날 — FINAL SHIFT REPORT 의 [계속] 은 휴게시간이 아니라 엔딩으로 간다.
        if ((GameState.Instance?.CurrentDay ?? 1) >= (Config.Instance?.Data?.MaxDays ?? 5))
        {
            StartEnding();
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

        bool finalDay = (GameState.Instance?.CurrentDay ?? 1) >= (Config.Instance?.Data?.MaxDays ?? 5);
        RestRosterView.Instance?.Present(finalDay);

        await SwapScreensWithFlicker(() =>
        {
            _ctl?.SetLeftScreen(_ctl.RestRosterViewport);
            _ctl?.SetRightScreen(_ctl.InterviewViewport);
        });
    }

    // --- 휴게 → 다음 날 배치 / 최종 결과 -----------------------------------

    // DAY0 교육이 끝나면 TutorialDirector 가 직접 DAY1 로 넘긴다("그럼 이제, DAY 1 근무를 시작합니다").
    public void AdvanceFromTutorial() => RequestNextFromRest();

    private void RequestNextFromRest()
    {
        if (_stage != Stage.Rest) return;

        bool finalDay = (GameState.Instance?.CurrentDay ?? 1) >= (Config.Instance?.Data?.MaxDays ?? 5);
        if (finalDay)
        {
            StartEnding();
        }
        else
        {
            _stage = Stage.DayTransition;
            AdvanceToNextDay();
        }
    }

    // 엔딩 — 코어 100% 여부 하나로만 갈린다(EndingDirector). 끝나면 최종 근무 기록 → 변한 시작 화면.
    private void StartEnding()
    {
        if (_stage == Stage.Final) return;
        _stage = Stage.Final;
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
