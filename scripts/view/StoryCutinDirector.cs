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

    // 컷인 때문에 지금 근무 시간이 멈춰 있는가.
    public static bool PausesGameplay => Instance is { IsPlaying: true, _pauseGameplay: true };
    // 컷인 때문에 지금 엿들은 대화 자막이 멈춰 있는가.
    public static bool SuppressesAmbientDialogue =>
        Instance is { IsPlaying: true, _suppressAmbient: true };

    private bool _pauseGameplay;
    private bool _suppressAmbient;
    private bool _lockedInput;
    private StoryCutinHud _hud;

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        // 씬이 내려가도 정지 · 잠금이 다음 씬으로 넘어가지 않게 한다.
        Release();
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

    // 묶음을 끝까지 돌린다. 띄울 수 없는 상황이면 아무것도 하지 않고 false.
    public async Task<bool> Play(StoryBeat beat)
    {
        if (beat == null || beat.Lines.Count == 0) return false;

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
            if (ControlRoom3DController.Instance != null)
            {
                ControlRoom3DController.Instance.SetInputLocked(true);
                _lockedInput = true;
            }

            _hud.BeginBeat();
            foreach (var line in beat.Lines)
            {
                if (!IsPlaying) break;
                await PlayLine(line);
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
        if (IsInstanceValid(_hud)) _hud.HideNow();
        Release();
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
