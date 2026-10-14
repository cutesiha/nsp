using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 스토리 선택지를 **마우스로** 고를 수 있는가 — 모니터2 를 확대한 상태에서.
//
//   godot --path . res://scenes/debug/StoryChoiceClickTest.tscn --quit-after 8000
//
// 증상: 메인 스토리는 모니터2 를 확대한 채 돈다. 그 상태에서는 화면 대부분이 모니터
// 평면이라, ControlRoom3DController._Input 이 클릭을 먼저 받아 모니터 뷰포트로 넘기고
// SetInputAsHandled() 로 먹어 버렸다 — 선택지 버튼(CanvasLayer)까지 오지 못했다.
// 그래서 숫자키로만 고를 수 있었고, 확대를 풀면 눌렸다.
//
// 선택지를 고르지 못하면 비트가 끝나지 않고, 그 동안 StoryCutinDirector.PausesGameplay
// 가 켜진 채로 남아 **근무 전체가 멈춘다**(코어 0% · 아무 사건도 안 일어남).
// 그래서 이 검사는 "눌리는가" 뿐 아니라 "눌린 뒤 정지가 풀리는가"까지 본다.
//
// 창 모드로 실행해야 한다(헤드리스는 3D 레이캐스트에 쓸 화면이 없다).
public partial class StoryChoiceClickTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        GD.Print("\n\n################ 스토리 선택지 클릭 ################");

        DebugEntryPoint.SkipTitleOnNextBoot();
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        for (int i = 0; i < 240; i++) await Frame();

        // 근무 중으로 들어간다(컷인 정지가 실제로 근무를 멈추는지 보려면 Live 여야 한다).
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        for (int i = 0; i < 180; i++) await Frame();
        if (AdminPad3D.Instance?.IsOpen == true) AdminPad3D.Instance.Close();
        for (int i = 0; i < 120; i++) await Frame();

        Check(GameState.Instance?.CurrentPhase == GamePhase.Live, "DAY1 근무 중이다");

        var hud = StoryCutinHud.Instance;
        var dir = StoryCutinDirector.Instance;
        Check(hud != null && dir != null, "컷인 HUD · 디렉터가 있다");
        if (hud == null || dir == null) { Done(); return; }

        // 선택지가 있는 비트를 모니터2 위에서 돌린다(메인 스토리와 같은 경로).
        var beat = new StoryBeat { BeatId = "test_choice_click" };
        beat.Add("rabbit", "관리자님, 어느 쪽으로 할까요? (확인용 문장)");
        var choice = beat.AddChoice("test_click_choice");
        choice.Prompt = "어떻게 하시겠습니까?";
        choice.Options.Add(new StoryChoiceOption { Text = "첫 번째 (확인용)" });
        choice.Options.Add(new StoryChoiceOption { Text = "두 번째 (확인용)" });
        choice.Options.Add(new StoryChoiceOption { Text = "세 번째 (확인용)" });

        var task = dir.PlayOnMonitor(beat);
        // 선택지가 뜰 때까지(대사 타이핑이 끝나야 뜬다).
        for (int i = 0; i < 1800 && !hud.IsChoosing; i++)
        {
            if (hud.IsWaitingForInput && !hud.IsTyping) hud.RequestAdvance();
            await Frame();
        }
        Check(hud.IsChoosing, "선택지가 떴다");
        if (!hud.IsChoosing) { dir.Abort(); await task; Done(); return; }

        Check(StoryCutinDirector.PausesGameplay, "고르는 동안 근무가 멈춰 있다");
        float clockBefore = GameState.Instance.DayTimeSeconds;

        // 모니터2 가 확대되어 있는가(이 상태가 문제의 전제였다).
        var ctl = ControlRoom3DController.Instance;
        bool zoomed = ctl != null && IsFocused(ctl);
        GD.Print($"   모니터2 확대 상태 = {zoomed}");

        // 두 번째 버튼의 화면 좌표 한가운데를 실제로 클릭한다.
        var btn = FindChoiceButton(hud, 1);
        Check(btn != null, "두 번째 선택지 버튼을 찾았다");
        if (btn == null) { dir.Abort(); await task; Done(); return; }

        Vector2 center = btn.GetGlobalRect().GetCenter();
        GD.Print($"   클릭 지점 = {center} (버튼 사각형 {btn.GetGlobalRect()})");
        ClickAt(center);
        for (int i = 0; i < 180 && hud.IsChoosing; i++) await Frame();

        Check(!hud.IsChoosing, "클릭으로 선택지가 닫혔다");
        Check(hud.ChosenIndex == 1, $"두 번째 선택지가 골라졌다 (index={hud.ChosenIndex})");

        // 비트가 끝나고 정지가 풀리는가 — 여기가 막히면 근무 전체가 얼어붙는다.
        for (int i = 0; i < 2400 && dir.IsPlaying; i++)
        {
            if (i % 400 == 0)
                GD.Print($"   [대기 {i}] playing={dir.IsPlaying} beat={dir.CurrentBeatId} " +
                         $"shown={hud.IsShown} typing={hud.IsTyping} waiting={hud.IsWaitingForInput} " +
                         $"choosing={hud.IsChoosing} text=\"{hud.CurrentText}\"");
            await Frame();
        }
        await task;
        Check(!dir.IsPlaying, "비트가 끝났다");
        Check(!StoryCutinDirector.PausesGameplay, "근무 정지가 풀렸다");

        for (int i = 0; i < 120; i++) await Frame();
        float clockAfter = GameState.Instance.DayTimeSeconds;
        Check(clockAfter > clockBefore, $"근무 시계가 다시 흐른다 ({clockBefore:0.0} → {clockAfter:0.0})");

        Done();
    }

    private static bool IsFocused(ControlRoom3DController ctl)
    {
        var f = typeof(ControlRoom3DController).GetField("_focusedNode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return f?.GetValue(ctl) != null;
    }

    private static Button FindChoiceButton(StoryCutinHud hud, int index)
    {
        var f = typeof(StoryCutinHud).GetField("_choiceButtons",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f?.GetValue(hud) is not System.Collections.Generic.List<Button> list) return null;
        return index >= 0 && index < list.Count ? list[index] : null;
    }

    // 실제 마우스 누름/뗌을 창에 밀어 넣는다(게임이 받는 것과 같은 경로).
    private void ClickAt(Vector2 pos)
    {
        var down = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true,
            Position = pos, GlobalPosition = pos, ButtonMask = MouseButtonMask.Left,
        };
        GetViewport().PushInput(down);
        var up = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false,
            Position = pos, GlobalPosition = pos,
        };
        GetViewport().PushInput(up);
    }

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private void Check(bool ok, string label)
    {
        if (ok) { _pass++; GD.Print($"  OK   {label}"); }
        else { _fail++; GD.Print($"  FAIL {label}"); }
    }

    private void Done()
    {
        GD.Print($"\n################ 통과 {_pass} · 실패 {_fail} ################\n");
        if (_fail > 0) GD.PushError($"StoryChoiceClickTest: {_fail}건 실패");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }
}
