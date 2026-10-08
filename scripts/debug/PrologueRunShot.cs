using System.Reflection;
using Godot;
using NSP.Prologue;
using NSP.View;

namespace NSP.Debug;

// 프롤로그 **전체**를 실제 경로로 돌리며 일정 간격으로 찍는다. 창 모드로 실행한다.
//
//   godot --path . res://scenes/debug/PrologueRunShot.tscn -- <저장 폴더> [시작초] [끝초] [간격] [읽는초]
//
// 타이틀 · 메뉴를 거치지 않고 ShiftFlowController 의 실제 프롤로그 진입(OnStartPressed)을
// 그대로 호출한다 — 기록(모니터1) → SIGNAL LOST → 전체 화면 재난 → 1인칭 피격까지
// 눈으로 확인하기 위한 것이다(지시서 §M).
//
// 대사가 있는 슬라이드는 설계상 플레이어 입력을 기다린다(CutscenePlayer.IsWaitingForInput).
// 그래서 사람이 읽고 누르는 것을 흉내내어 [읽는초] 마다 RequestAdvance 를 부른다.
public partial class PrologueRunShot : Node
{
    public override void _Ready() => _ = Run();

    private double _read = 1.2;
    private double _nextClick;

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        double from = args.Length > 1 ? args[1].ToFloat() : 2.0;
        double to = args.Length > 2 ? args[2].ToFloat() : 90.0;
        double step = args.Length > 3 ? args[3].ToFloat() : 2.0;
        if (args.Length > 4) _read = args[4].ToFloat();

        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        // 타이틀 연출을 기다리지 않고 바로 '새 근무 시작'을 누른 것과 같은 경로로 들어간다.
        var flow = await WaitFor(() => GetTree().Root.FindChild("ShiftFlowController", true, false));
        // OnStartPressed 는 Stage.Title 에서만 받는다 — 타이틀 부팅이 끝날 때까지 기다린다.
        // (일찍 부르면 무시되고, 나중에 타이틀이 스스로 시작해 프롤로그가 두 번 돈다.)
        for (int i = 0; i < 1800 && DebugEntryPoint.Stage(flow) != "Title"; i++) await Frame();
        await Frames(6);
        flow?.GetType().GetMethod("OnStartPressed", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(flow, null);
        for (int i = 0; i < 600 && DebugEntryPoint.Stage(flow) != "Prologue"; i++) await Frame();

        _start = Time.GetTicksMsec() / 1000.0;
        for (double at = from; at <= to; at += step)
        {
            await Until(at);
            Save(dir, $"p{at:000.0}");
        }
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private double _start = -1;

    private async System.Threading.Tasks.Task Until(double seconds)
    {
        if (_start < 0) _start = Time.GetTicksMsec() / 1000.0;
        while (Time.GetTicksMsec() / 1000.0 - _start < seconds)
        {
            await Frame();
            ReadAndClick();
        }
    }

    // 사람 대신 읽고 넘긴다. 첫 호출은 타이핑을 다 드러내고, 다음 호출이 실제로 넘어간다.
    private void ReadAndClick()
    {
        var p = CutscenePlayer.Instance;
        double now = Time.GetTicksMsec() / 1000.0;
        // 타이핑 중에는 건드리지 않는다 — 글자가 다 떠야 연출을 눈으로 볼 수 있다.
        if (p == null || !p.IsWaitingForInput || p.IsTypingNow) { _nextClick = now + _read; return; }
        if (now < _nextClick) return;
        _nextClick = now + _read;
        p.RequestAdvance();
    }

    private void Save(string dir, string name) =>
        GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");

    private async System.Threading.Tasks.Task<Node> WaitFor(System.Func<Node> get)
    {
        for (int i = 0; i < 600; i++)
        {
            if (get() is { } n) return n;
            await Frame();
        }
        return null;
    }

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
