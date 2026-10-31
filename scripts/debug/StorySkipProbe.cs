using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Prologue;
using NSP.View;

namespace NSP.Debug;

// 「SKIP ≫」 가 **실제로 되는지** 본다. 두 번 고쳤는데 두 번 다 안 됐던 자리라,
// 눈으로 보지 않고는 됐다고 말할 수 없다.
//
//   godot --path . res://scenes/debug/StorySkipProbe.tscn -- <저장 폴더>
//
// 확인하는 것 — SKIP 은 **한 단계씩** 건너뛴다(사용자 지시 2026-10-14).
//   ① 프롤로그 SKIP  → 가상 시뮬(DAY0 교육)이 시작된다
//   ② 가상 시뮬 SKIP → DAY1 스토리가 뜬다
//   ③ DAY 스토리 SKIP → 실제 DAY1 근무배치
//   ④ 그리고 **남은 비트가 카메라 화면 전체에 뜨지 않는다** — 스킵한 뒤 한참을 두고
//      봐도 컷인이 다시 살아나지 않아야 한다(모니터 밖으로 튀어나오던 버그).
public partial class StorySkipProbe : Node
{
    private string _dir = "";
    private int _pass, _fail;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _ = Run();
    }

    private async System.Threading.Tasks.Task Run()
    {
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        var flow = await WaitFor(() => DebugEntryPoint.FindFlow(GetTree()), 1800);
        Ok(flow != null, "ShiftFlowController 를 찾았다");
        if (flow == null) { Done(); return; }

        // 타이틀이 떠 있는 상태에서 「근무 개시」를 누른 것과 같다.
        await Frames(120);
        DebugEntryPoint.Call(flow, "OnStartPressed");

        var pro = await WaitFor(() => PrologueDirector.Instance is { IsRunning: true }
            ? PrologueDirector.Instance : null, 1800);
        Ok(pro != null, "프롤로그가 돌기 시작했다");
        if (pro == null) { Done(); return; }

        // 버튼은 창이 하나 뜨고 한 프레임 뒤에 보인다(_Process 가 켠다).
        await Frames(10);
        var hud = StorySkipHud.Instance;
        Ok(hud != null, "SKIP 창이 올라와 있다");
        Ok(hud?.ButtonVisible == true, "프롤로그 동안 SKIP 버튼이 보인다");
        Shot("01_프롤로그");

        // **진짜 버튼을 누른다** — 내부 메서드를 직접 부르지 않는다. 눌렀을 때
        // 실제로 연결돼 있는지까지 같이 확인해야 한다.
        var btn = hud?.FindChild("StorySkipButton", true, false) as Button;
        Ok(btn != null, "버튼 노드를 찾았다");
        ClickAt(btn);

        // ── ① 프롤로그 SKIP → 가상 시뮬(DAY0) ──────────────────────────
        bool reached = await WaitUntil(() =>
            GameState.Instance?.CurrentPhase == GamePhase.Schedule, 1200);
        Ok(reached, "배치 화면까지 갔다");
        Ok(PrologueDirector.Instance?.IsRunning != true, "프롤로그가 뒤에서 계속 돌지 않는다");
        Ok(GameState.Instance?.CurrentDay == 0,
            $"DAY0(가상 시뮬) 이다 - DAY1 로 건너뛰지 않았다 (지금 DAY {GameState.Instance?.CurrentDay})");

        bool tutorial = await WaitUntil(() => TutorialDirector.Instance is { IsRunning: true }, 1500);
        Ok(tutorial, "가상 시뮬이 시작됐다");
        await Frames(10);
        Shot("02_가상시뮬");

        // ── ② 가상 시뮬 SKIP → DAY1 ───────────────────────────────────
        Ok(StorySkipHud.Instance?.ButtonVisible == true, "가상 시뮬 동안에도 SKIP 버튼이 있다");
        ClickAt(StorySkipHud.Instance?.FindChild("StorySkipButton", true, false) as Button);

        bool day1 = await WaitUntil(() => GameState.Instance?.CurrentDay == 1
            && GameState.Instance?.CurrentPhase == GamePhase.Schedule, 1200);
        Ok(day1, $"DAY1 로 넘어갔다 (지금 DAY {GameState.Instance?.CurrentDay})");
        Ok(TutorialDirector.Instance?.IsRunning != true, "가상 시뮬이 뒤에서 계속 돌지 않는다");

        bool dayStory = await WaitUntil(() => StoryCutinDirector.Instance is { IsPlaying: true }, 1200);
        Ok(dayStory, "DAY1 스토리 컷인이 뜬다");
        await Frames(3);
        Shot("03_DAY1_스토리");

        // ── ③ DAY 스토리 SKIP → 실제 근무배치 ──────────────────────────
        Ok(StorySkipHud.Instance?.ButtonVisible == true, "그 컷인 동안에도 SKIP 버튼이 있다");
        ClickAt(StorySkipHud.Instance?.FindChild("StorySkipButton", true, false) as Button);
        bool cutStopped = await WaitUntil(() => StoryCutinDirector.Instance?.IsPlaying != true, 600);
        Ok(cutStopped, "DAY 스토리가 끊긴다");

        // ── ④ 남은 비트가 되살아나지 않는가 ────────────────────────────
        //
        // 여기가 이번에 고친 버그다. 예전에는 끊긴 뒤 다음 비트가 그대로 이어 재생됐고,
        // 그때는 이미 모니터에서 나온 뒤라 대사와 스탠딩이 **카메라 화면 전체**에 떴다.
        bool cameBack = false;
        bool hudCameBack = false;
        for (int i = 0; i < 240; i++)
        {
            await Frame();
            if (StoryCutinDirector.Instance is { IsPlaying: true }) cameBack = true;
            if (StoryCutinHud.Instance?.IsShown == true) hudCameBack = true;
        }
        Ok(!cameBack, "끊은 뒤 남은 비트가 다시 재생되지 않는다");
        Ok(!hudCameBack, "스토리 창이 화면 전체에 다시 뜨지 않는다");
        Ok(StoryCutinDirector.Instance?.Skipped == true, "SKIP 표시가 남아 있다");

        Shot("04_스토리_건너뛴_뒤");
        Ok(GameState.Instance?.CurrentPhase == GamePhase.Schedule,
            "그 자리가 실제 근무배치다");
        Ok(GameState.Instance?.CurrentDay == 1, "DAY1 이다");

        Done();
    }

    // **진짜 마우스로 누른다.** EmitSignal 로는 누가 입력을 가로채는지 알 수 없다 —
    // 실제로 막혀 있던 원인이 바로 그것이었다(PrologueAdvanceInput 이 _Input 단계에서
    // 왼쪽 클릭을 통째로 먹어, 버튼까지 닿지 않았다).
    private void ClickAt(Button b)
    {
        if (b == null) return;
        var p = b.GetGlobalRect().GetCenter();
        foreach (bool down in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = p, GlobalPosition = p,
                ButtonIndex = MouseButton.Left, Pressed = down,
            }, inLocalCoords: true);
    }

    // ── 도구 ────────────────────────────────────────────────────────

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (그림 없음 - {name})"); return; }
        img.SavePng($"{_dir}/skip_{name}.png");
    }

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private void Done()
    {
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    private async System.Threading.Tasks.Task<T> WaitFor<T>(System.Func<T> get, int frames) where T : class
    {
        for (int i = 0; i < frames; i++)
        {
            if (get() is { } v) return v;
            await Frame();
        }
        return null;
    }

    private async System.Threading.Tasks.Task<bool> WaitUntil(System.Func<bool> ready, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (ready()) return true;
            await Frame();
        }
        return false;
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
