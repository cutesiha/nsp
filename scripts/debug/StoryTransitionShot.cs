using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// DAY 스토리 전환을 **실제 게임 흐름 그대로** 지켜보고, 단계가 바뀔 때마다 상태를 찍는다.
//
//   godot --path . res://scenes/debug/StoryTransitionShot.tscn -- <저장 폴더>
//
// 연출을 직접 돌리지 않는다. 배치 화면에 들어가면 ShiftFlowController 가 알아서
// StoryBeatSelector → StoryTransition 을 태운다 — 이 도구는 그 옆에서 보기만 한다.
// (직접 돌리면 진짜 스토리와 둘이 겹쳐서, 실제로 플레이할 때와 다른 그림이 나온다.)
//
// 보는 것은 **순서**다(지시서 §1).
//   눈이 감긴다 → 암전에서 생활음 → 눈을 뜨면 모니터2 에 이미 휴게실 → breakroom →
//   확대 → 짧은 침묵 → 첫 대사 … 마지막 대사 → 확대 해제 → Shift Card → 기존 BGM 복귀
//
// 헤드리스로도 돌아간다(표만 나오고 화면은 저장되지 않는다).
public partial class StoryTransitionShot : Node
{
    private string _dir = "";
    private int _shot;
    private string _lastState = "";
    private double _t;
    private readonly List<string> _seen = new();
    private bool _cardShotQueued;
    private double _nextDiag = 10.0;
    private bool _storyStarted;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        // 타이틀을 건너뛰고, **부팅 시 DAY1 진입의 그 날 안내도 함께 건너뛴다.**
        // 이게 없으면 DAY1 스토리가 먼저 돌기 시작하고, 아래에서 DAY3 으로 다시 들어갈 때
        // 두 번째 요청이 접히면서 관찰할 것이 남지 않는다(실제 개발 허브와 같은 길).
        DebugEntryPoint.SkipTitleOnNextBoot();
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        // DAY3 로 맞춘다 — Shift Card 가 "DAY 3 / 봉쇄 코어 44% / 48시간" 을 보여 주는 날이다.
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 3);
        GameState.Instance.AddCoreProgress(44f, "캡처");

        GD.Print("\n\n################ DAY 스토리 전환 — 실제 흐름 관찰 ################");
        GD.Print("    시각    상태                       BGM          눈꺼풀  모니터2  카드");

        // 여기서부터 진짜 흐름이 시작된다(EnterSchedule → PlayDayIntro → 전환 + 스토리).
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);

        // 60초 동안 지켜본다. 대사는 자동으로 넘겨 준다 — 사람이 없으니 아무도 안 누른다.
        double nextAdvance = 3.0;
        while (_t < 110.0)
        {
            await Frame();
            _t += GetProcessDeltaTime();
            Sample();

            // 10초마다 지금 무엇을 기다리는지 적는다 — 멈추면 어디서 멈췄는지 알아야 한다.
            if (_t >= _nextDiag)
            {
                _nextDiag = _t + 10.0;
                var h = StoryCutinHud.Instance;
                var d = StoryCutinDirector.Instance;
                GD.Print($"      · {_t,5:0}s  beat={d?.CurrentBeatId ?? "-"} playing={d?.IsPlaying} " +
                         $"shown={h?.IsShown} choosing={h?.IsChoosing} waiting={h?.IsWaitingForInput} " +
                         $"advances={h?.AdvanceCount}");
            }

            // 대사가 떠 있으면 1.1초마다 한 번 넘긴다(읽는 사람 대신).
            if (_t >= nextAdvance)
            {
                nextAdvance = _t + 1.1;
                var hud = StoryCutinHud.Instance;
                if (hud != null && IsInstanceValid(hud))
                {
                    // 관리자 선택지가 떠 있으면 첫 번째를 고른다 — 아무도 안 고르면 거기서 멈춘다.
                    if (hud.IsChoosing) hud.Choose(0);
                    else if (hud.IsShown) hud.RequestAdvance();
                }
            }

            // 스토리가 **시작한 뒤에** 전부 돌아왔으면 그만 본다.
            // 시작 전에도 상태는 "복귀 완료" 라서, 그것만 보고 끊으면 아무것도 못 본다.
            if (_lastState == "⑤ 대사 진행") _storyStarted = true;
            if (_storyStarted && _lastState == "⑧ 복귀 완료") break;
        }

        GD.Print("\n---------------- 되돌아왔는가 ----------------");
        Ok(BlinkOverlay.Instance?.IsClosed != true, "암전이 풀렸다");
        Ok(ShiftCardView.Instance?.IsShown != true, "Shift Card 가 사라졌다");
        Ok(!StoryCutinDirector.ShowsRestRoom, "모니터2 가 휴게실에서 빠졌다");
        Ok(Sfx.Instance?.CurrentMusic != StoryTransition.StoryMusic,
            $"스토리 BGM 이 남아 있지 않다 (지금 '{Sfx.Instance?.CurrentMusic}')");
        Ok(!StoryTransition.Active, "전환 깃발이 내려갔다");
        Ok(GameState.Instance?.CurrentPhase == GamePhase.Schedule, "배치 화면으로 돌아왔다");

        GD.Print("\n---------------- 지나간 단계 ----------------");
        foreach (string s in _seen) GD.Print($"   {s}");

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    // 지금 어느 단계인가. 바뀐 순간에만 한 줄 찍고 화면을 저장한다.
    private void Sample()
    {
        string state = StateName();
        if (state == _lastState) return;
        _lastState = state;
        if (!_seen.Contains(state)) _seen.Add(state);

        // 페이드인이 있는 화면은 바뀐 그 프레임에 찍으면 거의 투명하다 — 한 번 더 찍는다.
        if (state == "⑦ Shift Card" && !_cardShotQueued)
        {
            _cardShotQueued = true;
            var t = GetTree().CreateTimer(0.9);
            t.Timeout += () => Shot("Shift_Card_표시완료");
        }

        string music = Sfx.Instance?.CurrentMusic ?? "";
        if (music.Length == 0) music = "—";
        bool dark = BlinkOverlay.Instance?.IsClosed ?? false;
        float shut = BlinkOverlay.Instance?.ClosedAmount ?? 0f;
        bool monitor = StoryCutinDirector.ShowsRestRoom;
        bool card = ShiftCardView.Instance?.IsShown ?? false;

        GD.Print($"   {_t,6:0.00}s  {Pad(state, 26)}  {Pad(music, 11)}  {shut * 100,3:0}%    " +
                 $"{(monitor ? "휴게실" : " — ")}    {(card ? "표시" : "—")}");
        Shot(state);
    }

    private static string StateName()
    {
        bool dark = BlinkOverlay.Instance?.IsClosed ?? false;
        float shut = BlinkOverlay.Instance?.ClosedAmount ?? 0f;
        bool monitor = StoryCutinDirector.ShowsRestRoom;
        bool playing = StoryCutinDirector.Instance?.IsPlaying ?? false;
        bool card = ShiftCardView.Instance?.IsShown ?? false;
        bool trans = StoryTransition.Active;

        bool opening = BlinkOverlay.Instance?.IsOpening ?? false;
        if (card) return "⑦ Shift Card";
        if (shut >= 0.99f) return "② 완전 암전";
        if (opening && shut > 0.02f) return "③ 눈 뜨는 중";
        if (dark) return "① 눈 감는 중";
        if (playing) return "⑤ 대사 진행";
        if (monitor && trans) return "④ 휴게실 · 첫 대사 전";
        if (monitor) return "⑥ 대사 사이";
        if (trans) return "⓪ 전환 시작(날숨)";
        return "⑧ 복귀 완료";
    }

    // 한글은 터미널에서 두 칸을 차지한다 — C# 의 ,-26 패딩으로는 표가 어긋난다.
    private static string Pad(string s, int width)
    {
        int w = 0;
        foreach (char c in s) w += c > 0x1100 ? 2 : 1;
        return s + new string(' ', Mathf.Max(1, width - w));
    }

    private void Shot(string state)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) return;   // 헤드리스 — 표만 남는다
        string safe = state.Length > 2 ? state[2..].Replace(" ", "_").Replace("·", "") : state;
        img.SavePng($"{_dir}/story_{_shot:00}_{safe}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
