using System;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그 진행자.
//   기록 영상(왼쪽 CRT) → 대재난 → 머리 충격 → 암전/이명 → 의식 회복
//   → 권한 승계 콘솔(오른쪽 CRT) → 승계 완료 창 → GUIDE-0.exe 등장
//     (이때부터 화면이 셋으로 나뉜다: 왼쪽 CRT = 얼굴 / 오른쪽 CRT = 그림·도형 / 화면 아래 = 대사)
//   → 선택지 설명(세 질문) → DAY 0 예고
//
// 콘솔부터는 모니터를 확대하지 않고 제어실 전체 화면(실시간 운영과 같은 시점)에서 진행한다.
// 그동안 GUIDE-0 의 대사는 화면 아래 자막 띠(GuideSubtitleHud)로도 같이 나온다.
// 문구·이미지·효과음은 전부 docs/NSP_PROLOGUE_RUNTIME.md 가 가지고 있고, 여기는 순서만 맡는다.
// ShiftFlowController 가 시작 화면 다음에 이 진행자를 부르고, Finished 를 받아 DAY0 배치로 넘어간다.
public partial class PrologueDirector : Node
{
    public static PrologueDirector Instance { get; private set; }

    [Export] public NodePath ControllerPath = "..";
    [Export] public NodePath TitleOverlayPath = "../TitleOverlay";

    public event Action Finished;
    public bool IsRunning { get; private set; }

    // 첫 브리핑 선택지(무슨 일이 / 나는 누구 / 내가 할 일). 문구·답변은 전부 데이터 파일에 있다.
    private const string MainMenuId = "g_main";
    // 콘솔이 끝난 뒤 잠깐 떴다 닫히는 권한 승계 완료 창.
    private const string AuthorityWindowId = "authority_done";

    private ControlRoom3DController _ctl;
    private TitleOverlay _title;

    public override void _Ready()
    {
        Instance = this;
        _ctl = GetNodeOrNull<ControlRoom3DController>(ControllerPath);
        _title = GetNodeOrNull<TitleOverlay>(TitleOverlayPath);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Play()
    {
        if (IsRunning) return;
        IsRunning = true;
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        var cutscene = CutscenePlayer.Instance;
        var guide = GuideHologramView.Instance;
        // CRT 안의 View 들은 SubViewport 에서 지연 생성된다 — 준비될 때까지 기다린다.
        for (int i = 0; i < 120 && (cutscene == null || guide == null); i++)
        {
            await NextFrame();
            cutscene = CutscenePlayer.Instance;
            guide = GuideHologramView.Instance;
        }
        if (cutscene == null || guide == null)
        {
            GD.PushWarning("PrologueDirector: 컷씬/GUIDE-0 화면을 찾지 못해 프롤로그를 건너뜁니다.");
            Complete();
            return;
        }

        // 자막 띠는 끈다 — 프롤로그 동안에는 홀로그램이 정면에 보인다.
        GuideSubtitleHud.Instance?.SetActive(false);

        // 두 CRT 에 프롤로그 전용 프로그램을 건다.
        cutscene.Clear();
        guide.ClearConsole();
        guide.HideHologram();
        _ctl?.SetLeftScreen(_ctl.CutsceneViewport);
        _ctl?.SetRightScreen(_ctl.GuideViewport);

        // ── #1 : 검은 화면 → 두 화면이 켜지고 기록 영상 재생 ──────────────
        _ctl?.SetScreenBrightness(0.02f);
        await Wait(0.6);
        Sfx.Instance?.Play("switch", -6f);
        var bt = CreateTween();
        bt.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 0.02f, 1.0f, 0.9)
          .SetTrans(Tween.TransitionType.Sine);
        await Wait(0.9);

        _ctl?.FocusMonitor(1);   // "모니터1 확대버전 화면"
        await Wait(0.7);

        await PlayCutscene(cutscene, "prologue_archive");

        // ── #2 : 대재난 ────────────────────────────────────────────────
        await PlayCutscene(cutscene, "prologue_disaster");

        // ── 충격 → 완전 암전 → 이명 → 의식 회복 ─────────────────────────
        // 곧바로 콘솔로 넘어가면 '게임 시스템으로 순간이동' 한 느낌이 난다. 한 박자 둔다.
        _title?.FadeToBlack(0.5f);
        await Wait(0.7);
        cutscene.Clear();
        _ctl?.SetScreenBrightness(0.0f);
        _ctl?.ClearFocus();

        // 삐———— 이명. 그 밑으로 숨소리 · 멀리서 튀는 전기 · 먹먹한 경보음.
        Sfx.Instance?.Play("tinnitus", -6f);
        await Wait(0.35);
        Sfx.Instance?.Play("breath_faint", -13f);
        Sfx.Instance?.Play("electric_arc", -22f);
        Sfx.Instance?.Play("alarm", -26f);
        await Wait(1.35);

        // 화면이 천천히 돌아온다 — 어두워진 실제 중앙제어실.
        _ctl?.ResetCameraCollapse();   // 책상에 엎어진 자세를 정상으로 되돌린다
        _title?.FadeFromBlack(1.3f);
        await Wait(1.5);

        // ── #3 : 어두운 제어실에서 오른쪽 CRT 하나만 '틱' 하고 켜진다 ────────
        // 모니터를 확대하지 않는다 — 실시간 운영과 같은 제어실 전체 화면에서 진행한다.
        GuideSubtitleHud.Instance?.SetActive(true);
        guide.LineShown += OnGuideLine;
        Sfx.Instance?.Play("relay_click", -4f);
        Sfx.Instance?.Play("crt_on", -7f);
        var bt2 = CreateTween();
        bt2.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 0.0f, 1.0f, 0.55)
           .SetTrans(Tween.TransitionType.Sine);
        // 승계 콘솔 동안 켜지는 건 오른쪽 CRT 뿐이다.
        _ctl?.SetScreenBrightnessFor("01", 0.03f);
        await Wait(1.0);

        await PlayConsole(guide, "authority_transfer");
        await Wait(0.6);

        // 콘솔이 사라지고 승계 완료 창이 떴다 닫힌다 — 여기까지가 '권한 승계'다.
        await PlayWindow(guide, AuthorityWindowId);
        await Wait(0.35);

        // 그 다음에야 GUIDE-0.exe 가 뜬다(별개의 사건).
        // 여기서부터 화면이 셋으로 나뉜다.
        //   모니터 1 = GUIDE-0 얼굴 / 모니터 2 = 그림·도형이 도는 창 / 화면 아래 = 대사
        guide.ClearConsole();
        guide.SetCompact(true);
        GuideFaceView.Instance?.SetShown(true);
        _ctl?.SetLeftScreen(_ctl.GuideFaceViewport);
        Sfx.Instance?.Play("relay_click", -6f);
        Sfx.Instance?.Play("crt_on", -10f);
        var bt3 = CreateTween();
        bt3.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightnessFor("01", v)), 0.03f, 1.0f, 0.5)
           .SetTrans(Tween.TransitionType.Sine);
        await Wait(0.55);

        // 마지막 대사가 다 찍히는 순간 바로 선택지를 띄운다(추가 클릭 없음).
        await ShowGuide(guide, "g_intro", null, completeWhenTyped: true);

        // ── 선택지 : 세 질문을 각각 한 번씩 모두 확인해야 다음으로 넘어간다 ──
        //    순서는 자유. 이미 확인한 질문은 체크 표시 + 비활성으로 남는다.
        guide.ResetMenuProgress();
        while (!guide.AllOptionsAnswered(MainMenuId))
        {
            var pick = await ShowMenu(guide, MainMenuId);
            if (pick == null) break;
            // 답변이 끝나는 순간 다시 선택지로 — 여기서도 추가 클릭이 필요 없다.
            await ShowGuide(guide, pick.GuideId, null, completeWhenTyped: true);
            if (pick.IsFinal) break;
        }

        // ── #4 : 안내 완료 → DAY 0 예고 ───────────────────────────────
        await ShowGuide(guide, "g_day0_open");
        await Wait(0.3);

        guide.HideHologram();
        guide.SetCompact(false);
        GuideFaceView.Instance?.SetShown(false);
        guide.LineShown -= OnGuideLine;
        GuideSubtitleHud.Instance?.SetActive(false);
        GuideSubtitleHud.Instance?.Clear();
        _ctl?.ClearFocus();
        _title?.FlashBanner("DAY 0   비상 관리자 교육");
        await Wait(1.4);

        Complete();
    }

    // 제어실 전체 화면에서 진행하는 동안 오른쪽 CRT 는 멀리 있다 — 같은 문장을 화면 아래에도 띄운다.
    private static void OnGuideLine(string text) => GuideSubtitleHud.Instance?.SetLine(text);

    private void Complete()
    {
        IsRunning = false;
        Finished?.Invoke();
    }

    // --- await 헬퍼 -------------------------------------------------------

    private async Task NextFrame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private static Task PlayCutscene(CutscenePlayer player, string id)
    {
        var tcs = new TaskCompletionSource();
        void Handler()
        {
            player.Finished -= Handler;
            tcs.TrySetResult();
        }
        player.Finished += Handler;
        player.Play(id);
        return tcs.Task;
    }

    private static Task PlayWindow(GuideHologramView guide, string id)
    {
        var tcs = new TaskCompletionSource();
        guide.PlayWindow(id, () => tcs.TrySetResult());
        return tcs.Task;
    }

    private static Task PlayConsole(GuideHologramView guide, string id)
    {
        var tcs = new TaskCompletionSource();
        guide.PlayConsole(id, () => tcs.TrySetResult());
        return tcs.Task;
    }

    // completeWhenTyped: 마지막 대사가 다 찍히는 순간 완료로 본다(뒤에 선택지가 이어질 때).
    public static Task ShowGuide(GuideHologramView guide, string id,
        System.Collections.Generic.Dictionary<string, string> replacements = null,
        bool completeWhenTyped = false)
    {
        var tcs = new TaskCompletionSource();
        if (replacements != null) guide.ShowGuide(id, replacements, () => tcs.TrySetResult(), completeWhenTyped);
        else guide.ShowGuide(id, () => tcs.TrySetResult(), completeWhenTyped);
        return tcs.Task;
    }

    private static Task<PrologueScript.MenuOption> ShowMenu(GuideHologramView guide, string id)
    {
        var tcs = new TaskCompletionSource<PrologueScript.MenuOption>();
        guide.ShowMenu(id, opt => tcs.TrySetResult(opt));
        return tcs.Task;
    }
}
