using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 실제 근무를 **그대로** 타고 가며 귀신이 나타나는 순간의 프레임을 잰다.
//
//   godot --path . res://scenes/debug/GhostRealFlowProbe.tscn -- [day N] [secs S] [novsync]
//
// GhostPerfProbe 는 ForceAppear 로 귀신을 억지로 세운다 — 판정(GhostHauntSystem)이 직접
// 띄우는 길, 직원 반응 · 엿듣기 · 경고 · 튜토리얼이 한꺼번에 도는 실제 장면은 지나치지 않는다.
// 여기서는 근무 흐름(ShiftFlowController)으로 들어가 시뮬레이션이 스스로 돌게 두고,
// 관리자처럼 CCTV 만 돌린다. 반드시 창 모드로 돌린다.
public partial class GhostRealFlowProbe : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        if (DisplayServer.GetName() == "headless") { GD.PrintErr("창 모드로 돌릴 것."); GetTree().Quit(1); return; }
        var args = OS.GetCmdlineUserArgs();
        int day = 1; double secs = 150;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "day") int.TryParse(args[i + 1], out day);
            if (args[i] == "secs") double.TryParse(args[i + 1], out secs);
        }
        if (System.Array.IndexOf(args, "novsync") >= 0)
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 2400 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        for (int d = 1; d < day; d++) GameState.Instance?.GoToNextDay();
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Frames(10);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 1800 && DebugEntryPoint.Stage(flow) != "Shift"; i++) await Frame();

        var sim = FacilitySimulation.Instance;
        GD.Print($"\n######## 실제 근무 DAY{GameState.Instance?.CurrentDay} {secs:0}초 - 귀신 전후 프레임 ########");
        GD.Print($"   품질 {GameSettings.QualityLabel}, 창 {DisplayServer.WindowGetSize()}, vsync {DisplayServer.WindowGetVsyncMode()}");

        string[] channels = { "power_room", "core_room", "storage_room", "guard_room", "maintenance_room", "vent_room" };
        var slow = new List<string>();
        var ring = new Queue<double>();
        double t = 0, nextSwitch = 4, worst = 0;
        int ch = 0, frames = 0, over = 0, ghostFrames = 0, ghostOver = 0;
        bool wasGhost = false, wasDeath = false;
        double ghostSum = 0, calmSum = 0;
        int afterGhost = -1;
        var afterMs = new List<double>();

        while (t < secs)
        {
            ulong a = Time.GetTicksUsec();
            await Frame();
            double ms = (Time.GetTicksUsec() - a) / 1000.0;
            t += GetProcessDeltaTime();
            frames++;
            var g = sim.Ghost;
            bool ghost = g is { Active: true };
            bool death = AdminDeathDirector.IsPlaying;

            if (ghost && !wasGhost)
            {
                GD.Print($"   @{t,6:0.0}초  귀신 등장 - {g.ActiveRoomId}({g.ActiveMonsterId}), 직전 10프레임 {string.Join(" ", ring.ToArray().Select0())}");
                afterGhost = 0; afterMs.Clear();
                // 관리자가 바로 그 방을 본다 — 가장 무거운 경우.
                sim.SetSurveillanceTarget(g.ActiveRoomId);
                nextSwitch = t + 8;
            }
            if (!ghost && wasGhost) GD.Print($"   @{t,6:0.0}초  귀신 퇴장");
            if (death && !wasDeath) GD.Print($"   @{t,6:0.0}초  사망 연출 시작");
            wasGhost = ghost; wasDeath = death;

            if (afterGhost >= 0)
            {
                afterMs.Add(ms);
                if (++afterGhost == 120)
                {
                    GD.Print($"      등장 직후 120프레임: 최악 {Max(afterMs):0.0} ms, {string.Join(" ", afterMs.GetRange(0, 40).Select0())} …");
                    afterGhost = -1;
                }
            }

            ring.Enqueue(ms); if (ring.Count > 10) ring.Dequeue();
            if (ghost) { ghostFrames++; ghostSum += ms; if (ms > 25) ghostOver++; } else calmSum += ms;
            if (ms > 25) { over++; slow.Add($"{ms,7:0.0} ms @{t,6:0.0}초 {(ghost ? $"귀신 {g.ActiveRoomId}({g.ActiveMonsterId}) {g.AliveSeconds:0}초" : "귀신 없음")}{(death ? ", 사망연출" : "")}"); }
            if (ms > worst) worst = ms;

            if (!ghost && t >= nextSwitch)
            {
                nextSwitch = t + 4;
                ch = (ch + 1) % channels.Length;
                sim.SetSurveillanceTarget(channels[ch]);
            }
            if (GameState.Instance?.CurrentPhase != GamePhase.Live && !death && t > 5) break;
        }

        int calmFrames = frames - ghostFrames;
        GD.Print($"\n   {frames}프레임 / {t:0}초, 25ms 넘은 프레임 {over}개, 최악 {worst:0.0} ms");
        GD.Print($"   귀신 있을 때 평균 {(ghostFrames > 0 ? ghostSum / ghostFrames : 0):0.00} ms ({ghostFrames}프레임, 25ms 넘음 {ghostOver}), 없을 때 평균 {(calmFrames > 0 ? calmSum / calmFrames : 0):0.00} ms");
        slow.Sort((x, y) => string.CompareOrdinal(y, x));
        foreach (string s in slow.GetRange(0, System.Math.Min(30, slow.Count))) GD.Print("   " + s);
        GetTree().Quit();
    }

    private static double Max(List<double> xs) { double m = 0; foreach (double x in xs) if (x > m) m = x; return m; }

    private System.Threading.Tasks.Task Frames(int n)
    {
        async System.Threading.Tasks.Task Go() { for (int i = 0; i < n; i++) await Frame(); }
        return Go();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
}

internal static class ProbeFmt
{
    public static IEnumerable<string> Select0(this IEnumerable<double> xs)
    {
        foreach (double x in xs) yield return x.ToString("0");
    }
}
