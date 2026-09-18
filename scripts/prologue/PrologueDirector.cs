using System;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그 진행자.
//   기록 영상(왼쪽 CRT) → 대재난 → 암전 → 권한 승계 콘솔(오른쪽 CRT) → GUIDE-0 등장
//   → 선택지 설명 → DAY 0 예고
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

        // 플레이어가 충격을 받고 쓰러진다 — 화면 전체 암전.
        _title?.FadeToBlack(0.5f);
        await Wait(0.7);
        cutscene.Clear();
        _ctl?.SetScreenBrightness(0.02f);
        _ctl?.ResetCameraCollapse();   // 책상에 엎어진 자세를 정상으로 되돌린다
        _ctl?.ClearFocus();
        await Wait(0.9);

        // ── #3 : 어두운 제어실에서 오른쪽 CRT 하나만 켜진다 ────────────────
        _title?.FadeFromBlack(0.8f);
        await Wait(0.5);
        Sfx.Instance?.Play("relay_click", -6f);
        var bt2 = CreateTween();
        bt2.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 0.02f, 1.0f, 0.8)
           .SetTrans(Tween.TransitionType.Sine);
        _ctl?.FocusMonitor(2);
        await Wait(0.9);

        await PlayConsole(guide, "authority_transfer");
        await Wait(0.5);

        // GUIDE-0 등장. 마지막 대사가 다 찍히는 순간 바로 선택지를 띄운다(추가 클릭 없음).
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
        _ctl?.ClearFocus();
        _title?.FlashBanner("DAY 0   비상 관리자 교육");
        await Wait(1.4);

        Complete();
    }

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
