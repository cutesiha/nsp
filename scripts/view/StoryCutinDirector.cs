using System;
using System.Threading.Tasks;
using Godot;

namespace NSP.View;

// 스토리 컷인 한 묶음(StoryBeat)을 돌린다.
//
// 진행 방식은 TutorialDirector 와 같다 — 한 줄 띄우고, 플레이어가 넘길 때까지 폴링하고,
// 다음 줄로 간다. 새 대화 엔진이 아니다. 대사는 호출부가 StoryBeat 로 넘겨 준다.
//
// 이 디렉터가 켜는 것은 셋뿐이고, 셋 다 반드시 제자리로 돌아간다(문서 §38).
//   · 근무 시뮬레이션 정지   (ControlRoom3DController / MainSceneController 가 이 값을 본다)
//   · CCTV 엿들은 대화 자막 정지   (CCTVMonitorView 가 이 값을 본다)
//   · 책상 위 기기 입력 잠금   (기존 ControlRoom3DController.SetInputLocked)
//
// 예외 · 씬 전환 · 강제 종료 어느 쪽으로 빠져나가도 Release() 를 지나간다
// (try/finally + _ExitTree).
public partial class StoryCutinDirector : Node
{
    public static StoryCutinDirector Instance { get; private set; }

    public bool IsPlaying { get; private set; }
    public string CurrentBeatId { get; private set; } = "";

    // 지금 모니터2 안에서 도는 스토리인가(DAY1~5 메인 스토리).
    // FacilityCctvWorld 가 이 값을 보고 근무 중에도 휴게실을 비춘다.
    //
    // 대사가 도는 중(IsPlaying)인지는 보지 않는다. 전환 연출이 **첫 대사보다 먼저**
    // 모니터2 를 휴게실로 바꿔 두기 때문이다 — 관리자가 눈을 뜨는 순간 화면에 이미
    // 휴게실이 떠 있어야 한다(지시서 §1). IsPlaying 까지 보면 그 구간이 빈 화면이 된다.
    public static bool ShowsRestRoom => Instance is { _onMonitor: true };

    private bool _onMonitor;
    private SubViewport _prevRightScreen;

    // 컷인 때문에 지금 근무 시간이 멈춰 있는가.
    public static bool PausesGameplay => Instance is { IsPlaying: true, _pauseGameplay: true };
    // 컷인 때문에 지금 엿들은 대화 자막이 멈춰 있는가.
    public static bool SuppressesAmbientDialogue =>
        Instance is { IsPlaying: true, _suppressAmbient: true };

    private bool _pauseGameplay;
    private bool _suppressAmbient;
    private bool _lockedInput;
    private StoryCutinHud _hud;
    // 지금 도는 비트의 배역표(@witness · @heard1 …). 비트마다 새로 만든다.
    private StoryRoleCast _cast;

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        // 씬이 내려가도 정지 · 잠금 · 모니터 확대가 다음 씬으로 넘어가지 않게 한다.
        Release();
        LeaveMonitor(null);
        IsPlaying = false;
        if (Instance == this) Instance = null;
    }

    // 왜 컷인을 띄울 수 없는지. 띄울 수 있으면 빈 문자열.
    //
    // 관리자와 직원의 직접 대화 수단은 전화기다(문서 §2.1). 통화 중에 컷인을 겹쳐
    // 띄우면 그 규칙이 흐려지므로, 이 Phase 에서는 아예 막는다.
    // 이 판정은 절대 예외를 던지면 안 된다 — 가드가 터지면 연출이 아니라 게임이 멈춘다.
    // PhoneCallHud 는 씬이 내려갈 때 static Instance 를 비우지 않으므로 유효성까지 본다.
    public string BlockedReason()
    {
        if (IsPlaying) return "이미 컷인이 돌고 있습니다";
        var phone = PhoneCallHud.Instance;
        if (phone != null && IsInstanceValid(phone) && phone.IsOpen)
            return "통화 중에는 컷인을 띄우지 않습니다";
        var hud = StoryCutinHud.Instance;
        if (hud == null || !IsInstanceValid(hud)) return "StoryCutinHud 가 씬에 없습니다";
        return "";
    }

    public bool CanPlay => BlockedReason().Length == 0;

    // DAY1~5 메인 스토리 — **모니터2 영상 안**에서 돌린다(연출 규칙 §1 · §3).
    //
    //   중앙제어실 기본 화면 → 모니터2 를 확대 → 그 화면에 휴게실 CCTV →
    //   대화 → 끝나면 확대를 풀고 제자리로.
    //
    // 카메라는 기존 ControlRoom3DController.FocusMonitor / ClearFocus 를 그대로 쓴다.
    // 새 카메라 시스템을 만들지 않는다(§7).
    // 모니터2 로 들어간다. 한 DAY 의 비트 여러 개를 이어 재생하는 동안 **한 번만** 부른다 —
    // 비트마다 확대했다 풀면 카메라가 사이사이 튄다. 돌려놓는 것은 ExitMonitor 다.
    //
    // focusNow=false 면 화면만 휴게실로 바꾸고 **카메라는 아직 그대로 둔다.**
    // 전환 연출(StoryTransition)이 그 순서를 쓴다 — 눈을 뜨는 순간 모니터2 에 이미
    // 휴게실이 떠 있어야 하고, 확대는 그 다음이다(지시서 §1).
    public bool EnterMonitor(bool focusNow = true)
    {
        if (_onMonitor)
        {
            if (focusNow) FocusStoryMonitor();
            return true;
        }
        var ctl = ControlRoom3DController.Instance;
        var view = StoryMonitorView.Instance;
        if (ctl == null || view == null || !IsInstanceValid(view))
        {
            // 모니터 화면을 찾지 못하면 연출만 포기한다 — 스토리는 그래도 흘러야 한다.
            GD.PushWarning("StoryCutinDirector: 모니터2 스토리 화면을 찾지 못해 기본 화면으로 띄웁니다.");
            return false;
        }
        _onMonitor = true;
        _prevRightScreen = ctl.RightScreenViewport;
        ctl.SetRightScreen(ctl.StoryViewport);
        if (focusNow) ctl.FocusMonitor(2, MonitorFocusSeconds);
        StoryCutinHud.Instance?.SetRenderTarget(view.StoryLayer);
        return true;
    }

    // 카메라만 모니터2 로 들여보낸다. EnterMonitor(focusNow: false) 뒤에 부른다.
    // 모니터에 들어가 있지 않으면 아무 일도 하지 않는다 — 스토리 밖에서 화면을 확대하지 않는다.
    public void FocusStoryMonitor()
    {
        if (!_onMonitor) return;
        ControlRoom3DController.Instance?.FocusMonitor(2, MonitorFocusSeconds);
    }

    public void ExitMonitor() => LeaveMonitor(null);

    // 한 묶음만 모니터2 에서 돌린다(검사 · 단발 연출용).
    public async Task<bool> PlayOnMonitor(StoryBeat beat)
    {
        EnterMonitor();
        try { return await Play(beat); }
        finally { ExitMonitor(); }
    }

    // 확대 · 복귀에 쓰는 시간. 느리면 매 DAY 시작이 답답해진다(§7 — 0.2~0.5초).
    private const float MonitorFocusSeconds = 0.35f;

    // 모니터 연출을 전부 제자리로. 몇 번 불러도 안전하다 — 스토리가 어떻게 끝나든
    // 확대가 남아 있으면 그 뒤로 아무것도 조작할 수 없다(§17 · §18).
    private void LeaveMonitor(ControlRoom3DController ctl)
    {
        if (!_onMonitor) return;
        _onMonitor = false;
        StoryCutinHud.Instance?.SetRenderTarget(null);
        ctl ??= ControlRoom3DController.Instance;
        if (ctl == null) return;
        if (_prevRightScreen != null && IsInstanceValid(_prevRightScreen)) ctl.SetRightScreen(_prevRightScreen);
        _prevRightScreen = null;
        ctl.ClearFocus(MonitorFocusSeconds);
    }

    // 묶음을 끝까지 돌린다. 띄울 수 없는 상황이면 아무것도 하지 않고 false.
    //
    // closingSilenceSeconds — 마지막 대사가 끝난 뒤 스탠딩이 사라지기 전까지의 침묵(지시서 §8).
    // 그 DAY 스토리의 **마지막 비트에만** 넣는다. 비트마다 넣으면 대화 중간에 끊긴다.
    public async Task<bool> Play(StoryBeat beat, double closingSilenceSeconds = 0.0)
    {
        if (beat == null || beat.Steps.Count == 0) return false;

        string blocked = BlockedReason();
        if (blocked.Length > 0)
        {
            GD.Print($"StoryCutinDirector: 컷인을 건너뜁니다 — {blocked} (beat={beat.BeatId})");
            return false;
        }

        _hud = StoryCutinHud.Instance;
        IsPlaying = true;
        CurrentBeatId = beat.BeatId ?? "";
        _pauseGameplay = beat.PauseGameplay;
        _suppressAmbient = beat.SuppressAmbientDialogue;

        try
        {
            // 컷인이 떠 있는 동안 책상 위 기기를 잠근다 — 대사를 넘기는 클릭이 전화기나
            // 모니터까지 눌러 버리지 않게.
            // 모니터 안에서 도는 스토리는 책상을 잠그지 않는다 — 확대 상태라 기기를 누를 일이
            // 없고, 잠그면 메인 UI 의 선택지 클릭까지 같이 막힌다.
            if (!_onMonitor && ControlRoom3DController.Instance != null)
            {
                ControlRoom3DController.Instance.SetInputLocked(true);
                _lockedInput = true;
            }

            // 배역(@witness · @heard1 …)을 오늘의 실제 인물로 바꾼다. 비트 한 번에 한 번만
            // 만든다 — 줄마다 다시 만들면 같은 배역이 줄마다 다른 사람이 된다.
            _cast = StoryRoleCast.ForDay(NSP.Core.GameState.Instance?.CurrentDay ?? 0);

            _hud.BeginBeat();
            foreach (var step in beat.Steps)
            {
                if (!IsPlaying) break;
                if (step.IsChoice) { await PlayChoice(step.Choice); continue; }
                var line = _cast.Apply(step.Line);
                if (Speaks(line)) await PlayLine(line);
            }

            // 마지막 직원의 입이 닫히고 나서 잠깐 아무 소리도 없다 — 그 뒤에 스탠딩이 빠진다.
            if (IsPlaying && IsInstanceValid(_hud) && closingSilenceSeconds > 0.0)
            {
                NSP.View.EmployeeMouthAnimator.Reset();
                double left = closingSilenceSeconds;
                while (left > 0.0 && IsPlaying) { await NextFrame(); left -= GetProcessDeltaTime(); }
            }
            if (IsPlaying && IsInstanceValid(_hud)) await _hud.EndBeat();
            return true;
        }
        catch (OperationCanceledException)
        {
            // Abort — 조용히 끝난다.
            return false;
        }
        finally
        {
            Release();
            IsPlaying = false;
            CurrentBeatId = "";
        }
    }

    // 그 줄을 지금 읽을 수 있는가. say? 로 적힌 줄은 말할 사람이 자리에 없으면 건너뛴다
    // (죽은 직원이 말하거나, 격리된 직원이 휴게실에 앉아 있는 일이 없어야 한다).
    // say 로 적은 줄은 건너뛰지 않는다 — 그런 줄까지 빠지면 대화가 허공에 대답하게 된다.
    public static bool Speaks(StoryLine line)
    {
        if (line == null) return false;
        if (!line.Optional || line.SpeakerEmployeeId.Length == 0) return true;
        return IsPresent(line.SpeakerEmployeeId);
    }

    // 휴게실 자리에 실제로 앉아 있는가(사망 · 기절 · 격리면 아니다).
    public static bool IsPresent(string employeeId)
    {
        var st = NSP.Facility.FacilitySimulation.Instance?.GetEmployeeState(employeeId);
        return st is { Alive: true, Incapacitated: false, Isolated: false };
    }

    // ── 관리자 선택 ──────────────────────────────────────────────────
    //
    //   ① 선택지 셋을 한 번에 띄우고 고를 때까지 기다린다(근무는 멈춘 채)
    //   ② 고른 문장을 관리자가 한 말로 잠깐 띄운다
    //   ③ 그 선택지의 직원 반응을 이어 재생한다
    //
    // 고른 결과는 기록만 남는다 — 어떤 수치도 바꾸지 않는다.
    private async Task PlayChoice(StoryChoice choice)
    {
        if (choice == null || choice.Options.Count == 0) return;

        _hud.ShowChoice(choice);
        await Until(() => _hud.ChosenIndex >= 0);
        int pick = Mathf.Clamp(_hud.ChosenIndex, 0, choice.Options.Count - 1);
        _hud.HideChoice();

        var option = choice.Options[pick];
        StoryChoiceHistory.Record(NSP.Core.GameState.Instance?.CurrentDay ?? 0,
            choice.ChoiceId, pick, option.Text);

        // 관리자가 한 말. 세계관상 휴게실 전화 너머의 목소리라 스탠딩 없이 자막만 띄운다.
        await PlayLine(new StoryLine { SpeakerEmployeeId = "", Text = option.Text, HoldSeconds = ManagerLineSeconds });

        foreach (var raw in option.Branch)
        {
            if (!IsPlaying) break;
            var line = _cast?.Apply(raw) ?? raw;
            if (Speaks(line)) await PlayLine(line);
        }
    }

    // 고른 답을 읽을 시간. 넘기기를 기다리지 않고 지나간다 — 관리자 자신이 한 말이다.
    private const double ManagerLineSeconds = 1.6;

    private async Task PlayLine(StoryLine line)
    {
        _hud.ShowLine(line);

        if (line.HoldSeconds > 0)
        {
            // 입력 없이 넘어가는 짧은 반응 컷. 다 찍힌 뒤부터 센다.
            int before = _hud.AdvanceCount;
            await Until(() => _hud.LineComplete || _hud.AdvanceCount != before);
            double left = line.HoldSeconds;
            while (left > 0 && IsPlaying && _hud.AdvanceCount == before)
            {
                await NextFrame();
                left -= GetProcessDeltaTime();
            }
            if (IsPlaying && IsInstanceValid(_hud)) _hud.ConsumeLine();
            return;
        }

        // 기본 — 플레이어가 넘길 때까지 기다린다.
        //   1차 클릭 = 문장 즉시 완성, 2차 클릭 = 다음 줄 (StoryCutinHud.RequestAdvance)
        int start = _hud.AdvanceCount;
        await Until(() => _hud.AdvanceCount != start);
    }

    // 도중에 끊는다(씬 전환 · 개발 허브 복귀 · 예외). 남은 줄은 돌리지 않는다.
    public void Abort()
    {
        if (!IsPlaying) return;
        IsPlaying = false;
        if (IsInstanceValid(_hud)) { _hud.HideChoice(); _hud.HideNow(); }
        Release();
        LeaveMonitor(null);
        CurrentBeatId = "";
    }

    // 켜 둔 것을 전부 제자리로. 몇 번 불러도 안전하다.
    private void Release()
    {
        _pauseGameplay = false;
        _suppressAmbient = false;
        if (_lockedInput)
        {
            ControlRoom3DController.Instance?.SetInputLocked(false);
            _lockedInput = false;
        }
        if (IsInstanceValid(_hud)) _hud.HideChoice();
        if (IsInstanceValid(_hud) && _hud.IsShown) _hud.HideNow();
    }

    // --- await 헬퍼 (TutorialDirector 와 같은 방식) -------------------------

    private async Task NextFrame()
    {
        if (!IsPlaying || !IsInstanceValid(this)) throw new OperationCanceledException();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsPlaying || !IsInstanceValid(this)) throw new OperationCanceledException();
    }

    private async Task Until(Func<bool> condition)
    {
        while (IsPlaying && IsInstanceValid(this) && IsInstanceValid(_hud) && !condition())
            await NextFrame();
        if (!IsPlaying || !IsInstanceValid(this)) throw new OperationCanceledException();
    }
}
