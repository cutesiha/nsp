using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 개발 허브가 각 DAY 의 '근무 배치' 로 들어갔을 때 그 날에 맞는 것이 켜지는가.
//
//   godot --path . res://scenes/debug/DebugHubBootTest.tscn --quit-after 90000
//
// 허브는 타이틀을 건너뛰느라 **먼저 DAY1 배치로 열린 뒤** 원하는 날짜로 다시 들어간다.
// 그 첫 진입에서 '그 날의 안내' 가 돌면 DAY0 으로 가려는데 DAY1 패드 안내가 떠서
// 패드가 손에 올라오고, 그 상태로 교육이 시작돼 배치표를 누를 수 없었다.
public partial class DebugHubBootTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        GD.Print("\n\n################ 개발 허브 — 근무 배치 진입 ################");
        await EnterSchedule(0);
        Check(GameState.Instance?.CurrentDay == 0, $"DAY0 으로 들어갔다 ({GameState.Instance?.CurrentDay})");
        Check(NSP.Prologue.TutorialDirector.Instance?.IsRunning == true, "GUIDE-0 교육이 돌기 시작했다");
        Check(AdminPad3D.Instance?.IsOpen != true, "DAY1 패드 안내가 끼어들지 않았다");

        // 시작만 하고 첫 줄에서 멈추던 적이 있다 — 넘기기가 실제로 먹히는지 끝까지 본다.
        // (GuideHologramView 의 사라지는 트윈 콜백이 방금 시작한 대사를 지웠다.)
        string first = NSP.Prologue.GuideSubtitleHud.Instance?.CurrentLine ?? "";
        for (int i = 0; i < 10; i++) { PushSpace(); await Seconds(0.5); }
        string now = NSP.Prologue.GuideSubtitleHud.Instance?.CurrentLine ?? "";
        Check(!string.IsNullOrEmpty(first), $"첫 대사가 떴다 — \"{first}\"");
        Check(now != first, $"대사가 넘어간다 — \"{now}\"");

        await EnterSchedule(1);
        Check(GameState.Instance?.CurrentDay == 1, $"DAY1 으로 들어갔다 ({GameState.Instance?.CurrentDay})");
        Check(NSP.Prologue.TutorialDirector.Instance?.IsRunning != true, "DAY1 에는 교육이 돌지 않는다");
        // 첫날이다 — 전원이 시작값(StressMin) 그대로여야 한다.
        // 허브가 표시 확인용으로 주의 · 위험을 섞어 넣으면 여기서 걸린다.
        float start = Config.Instance.Data.StressMin;
        bool allFresh = true;
        foreach (string id in FacilitySimulation.Instance.GetActiveEmployeeIds())
        {
            float s = FacilitySimulation.Instance.GetEmployeeState(id)?.Stress ?? 0f;
            if (s > start) { allFresh = false; GD.Print($"      {id} = {s:0.##}"); }
        }
        Check(allFresh, $"DAY1 직원 스트레스가 전부 시작값({start:0}) 이다");

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // 허브(DeveloperHub.Launch)가 하는 것과 같은 순서.
    private async System.Threading.Tasks.Task EnterSchedule(int day)
    {
        foreach (Node c in GetChildren()) { RemoveChild(c); c.QueueFree(); }
        await Frame();
        DebugEntryPoint.SkipTitleOnNextBoot();
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", day <= 0 ? 0 : 1);
        for (int d = 1; d < day; d++) GameState.Instance?.GoToNextDay();
        DebugEntryPoint.Call(flow, "ClearAllAssignments");
        DebugEntryPoint.SeedState(new DebugEntryPoint.Request { Day = day, Label = $"DAY{day}" });
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        for (int i = 0; i < 180; i++) await Frame();
    }

    private static void PushSpace()
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Space, PhysicalKeycode = Key.Space, Pressed = false });
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
