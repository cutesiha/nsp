using System.Reflection;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 직원 사망 연출 확인(창 모드) — 실제 근무(DAY2)에서 직원 하나를 죽이고, 비명이 울리는 동안의
// 화면을 몇 장 찍는다. 비명 길이 · 떨림 · 붉은 점멸이 소리와 함께 끝나는지 로그로 남긴다.
//   godot --path . res://scenes/debug/DeathScreamProbe.tscn -- <저장 폴더> [직원id]
public partial class DeathScreamProbe : Node
{
    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        if (DisplayServer.GetName() == "headless") { GD.PrintErr("창 모드로 돌릴 것."); GetTree().Quit(1); return; }
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        string victim = args.Length > 1 ? args[1] : "rabbit";

        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 2400 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance?.GoToNextDay();
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Frames(10);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 1800 && DebugEntryPoint.Stage(flow) != "Shift"; i++) await Frame();
        for (int i = 0; i < 180; i++) await Frame();

        var sim = FacilitySimulation.Instance;
        var st = sim.GetEmployeeState(victim);
        GD.Print($"\n######## 사망 연출 — {victim} ({sim.GetEmployeeDef(victim)?.Gender}) @ {st?.CurrentRoomId} ########");
        Shot(dir, "death_0_before");
        typeof(FacilitySimulation).GetMethod("KillEmployee", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(sim, new object[] { victim, st?.CurrentRoomId ?? "" });

        double start = Time.GetTicksMsec() / 1000.0;
        double soundEnd = -1, fxEnd = -1;
        int shot = 1;
        double nextShot = start + 0.25;
        while (Time.GetTicksMsec() / 1000.0 - start < 16.0)
        {
            await Frame();
            double now = Time.GetTicksMsec() / 1000.0;
            if (soundEnd < 0 && Sfx.Instance?.DeathScreamPlaying != true) soundEnd = now - start;
            if (fxEnd < 0 && !DeathScreamFx.Running) fxEnd = now - start;
            if (shot <= 4 && now >= nextShot) { Shot(dir, $"death_{shot}"); shot++; nextShot = now + 0.45; }
            if (soundEnd >= 0 && fxEnd >= 0 && now - start > 1.0) break;
        }
        float tremor = ControlRoom3DController.Instance?.Rig?.Tremor ?? -1f;
        GD.Print($"   비명 끝 {soundEnd:0.00}초 · 연출 끝 {fxEnd:0.00}초 · 끝난 뒤 떨림 {tremor:0.00}");
        await Frames(20);
        Shot(dir, "death_9_after");
        GetTree().Quit();
    }

    private void Shot(string dir, string name)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(System.IO.Path.Combine(dir, name + ".png"));
    }

    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
