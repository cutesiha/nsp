using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.Ui;

namespace NSP.View;

// 시작 화면 → 근무 배치 → 메인 근무 → 근무 종료/보고 → 휴게(인터뷰) → 다음 날 배치 …
// 를 "한 공간에서 이어지는" 루프로 묶는다. 씬 전환(ChangeSceneToFile)은 최종 결과로 갈 때
// 한 번만 쓴다. 같은 3D 중앙제어실 안에서:
//   - 시작 화면 : 어두운 제어실 + TitleOverlay, CRT OFF
//   - 근무 배치 : 카메라가 책상의 DeskScheduleBoard 를 내려다봄
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
    [Export] public NodePath CeilingLightPath = "../ControlRoom/Lights/CeilingLight";
    [Export] public NodePath FillLightPath = "../ControlRoom/Lights/FillLight";
    [Export] public NodePath ArmsPath = "../ControlRoom/PlayerCharacter";

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

    private enum Stage { Boot, Title, Schedule, Booting, Shift, Ending, Report, Rest, DayTransition, Final }
    private Stage _stage = Stage.Boot;

    private ControlRoom3DController _ctl;
    private SeatedCameraRig _rig;
    private TitleOverlay _title;
    private DeskScheduleBoard _board;
    private OmniLight3D _ceiling, _fill;
    private Node3D _arms;
    private readonly System.Collections.Generic.List<Node3D> _clutter = new();
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
            _title.StartRequested += EnterSchedule;
            _title.QuitRequested += () => GetTree().Quit();
            _title.ShowTitle();
        }
        if (_board != null)
        {
            _board.StartRequested += EnterShift;
            _board.SetActive(false);
        }

        _stage = Stage.Title;
        Sfx.Instance?.CrossfadeMusic("startbgm1", 1.5f, loop: true); // 시작화면 BGM (루프)
    }

    public override void _Process(double delta)
    {
        if (!_wiredViews) WireLateSignals();

        // 근무 시간이 다 되면(시계가 종료 시간 도달) 자동으로 근무를 종료한다.
        if (_stage == Stage.Shift && GameState.Instance != null)
        {
            float limit = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
            if (GameState.Instance.DayTimeSeconds >= limit)
                RequestEndShift();
        }
    }

    // 왼쪽/오른쪽 CRT 안의 View 들은 SubViewport 안에서 지연 생성되므로, 준비될 때까지
    // 매 프레임 확인하다가 한 번만 연결한다(ControlRoomInteraction 의 기존 관례와 동일).
    private void WireLateSignals()
    {
        if (FacilityMonitorView.Instance == null || ShiftReportView.Instance == null || RestRosterView.Instance == null)
            return;

        FacilityMonitorView.Instance.EndShiftRequested += RequestEndShift;
        ShiftReportView.Instance.ContinueRequested += RequestRestFromReport;
        RestRosterView.Instance.NextRequested += RequestNextFromRest;
        _wiredViews = true;
    }

    // --- 시작 → 배치 -----------------------------------------------------

    private void EnterSchedule()
    {
        if (_stage is not (Stage.Title or Stage.DayTransition)) return;

        // 타이틀에서 시작하는 것은 새 게임이다. 이전 테스트에서 SetSaboteur를 썼더라도
        // 그 값이 남지 않게 모든 런 상태를 비운 뒤 이번 판의 방해자를 새로 뽑는다.
        if (_stage == Stage.Title)
            StartNewRun();

        _stage = Stage.Schedule;

        _title?.FadeOut();
        TabooRuleSystem.Instance?.ActivateDailyTaboos(ControlRoom3DController.DailyTabooIds);
        GameState.Instance?.SetPhase(GamePhase.Schedule);
        // 시작화면 BGM 페이드아웃 + 근무배치 BGM(rest_time) 페이드인. 다음 날 복귀 때도
        // 같은 곡을 처음부터 다시 페이드해 씬 전환을 체감시킨다(restartIfSame).
        Sfx.Instance?.CrossfadeMusic("rest_time", 0.9f, loop: true, restartIfSame: true);

        var lt = CreateTween();
        lt.SetParallel(true);
        if (_ceiling != null) lt.TweenProperty(_ceiling, "light_energy", _ceilBase * 0.72f, 1.1);
        if (_fill != null) lt.TweenProperty(_fill, "light_energy", _fillBase, 1.1);

        // 책상 위를 정리 — 장비를 치우고 배치표를 편다.
        foreach (var n in _clutter) n.Visible = false;
        if (_arms != null) _arms.Visible = false;

        _board?.SetActive(true);
        _ctl?.SetInputLocked(false);
        _ctl?.SetModalSurface(_board);

        if (_board != null)
            _rig?.FocusOnScreen(_board.SurfaceCenterWorld, _board.SurfaceNormalWorld, BoardFocusDistance, 0.7f);
    }

    private static void StartNewRun()
    {
        var state = GameState.Instance;
        var sim = FacilitySimulation.Instance;
        if (state == null || sim == null) return;

        state.ResetRun();
        sim.ResetRun();
        EventLog.Instance?.ClearAll();
        DialogueHistory.Instance?.ClearAll();

        state.AssignRandomSaboteur(sim.GetEmployeeIds());
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
        _board?.PlayDismiss();
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

    private void RequestNextFromRest()
    {
        if (_stage != Stage.Rest) return;

        bool finalDay = (GameState.Instance?.CurrentDay ?? 1) >= (Config.Instance?.Data?.MaxDays ?? 5);
        if (finalDay)
        {
            _stage = Stage.Final;
            GoToFinalResult();
        }
        else
        {
            _stage = Stage.DayTransition;
            AdvanceToNextDay();
        }
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
