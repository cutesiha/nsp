using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// A-3 환풍기 정지 연출 검증.
//
//   godot --path . res://scenes/debug/VentStageTest.tscn --quit-after 30000
//
// 설계 문서 13절의 순서가 실제로 일어나는지 본다:
//   팬이 느려짐 → 피치 감소 → 정지 → 갑자기 공간이 너무 조용해짐 → 복구
//
// 소리를 귀로 들을 수는 없으므로, 그 소리를 만드는 값(피치 목표 · 볼륨 목표)을 시간축으로 찍는다.
public partial class VentStageTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && FacilitySimulation.Instance == null; i++) await Frame();
        await Seconds(1.0);

        var atmos = FindAtmos();
        if (atmos == null) { GD.PrintErr("ControlRoomAtmosphere 를 찾지 못했다"); GetTree().Quit(); return; }

        GD.Print("\n\n################ A-3 환풍기 정지 연출 ################");
        GameState.Instance.ResetRun(2);
        GameState.Instance.SetPhase(GamePhase.Live);
        await Seconds(0.6);

        float idlePitch = atmos.VentPitchTarget;
        GD.Print($"   평상시 피치 목표 = {idlePitch:0.00}");
        Check(!atmos.VentStopping, "평소에는 정지 연출이 걸려 있지 않다");

        // ── 고장 ──
        atmos.KillVent();
        Check(atmos.VentStopping, "고장을 감지하면 정지 연출이 시작된다");

        float p05 = await PitchAfter(atmos, 0.5);
        float p15 = await PitchAfter(atmos, 1.0);
        float p32 = await PitchAfter(atmos, 1.7);
        GD.Print($"   피치 목표: 0.5초 {p05:0.00} → 1.5초 {p15:0.00} → 3.2초 {p32:0.00}");
        Check(p05 < idlePitch - 0.02f, "느려지기 시작한다");
        Check(p15 < p05, "계속 느려진다");
        Check(p32 <= 0.36f, $"다 느려지면 0.35 에 닿는다 ({p32:0.00})");

        // ── 정적 ──
        Check(atmos.VentHushActive, "멎은 직후 정적이 걸린다");
        await Seconds(1.0);
        Check(atmos.VentHushActive, "정적이 이어진다");
        await Seconds(2.2);
        Check(!atmos.VentHushActive, "2.5초쯤 뒤 정적이 풀린다");

        // ── 복구 ──
        atmos.RestoreVent();
        Check(!atmos.VentStopping, "복구하면 정지 연출이 풀린다");
        await Seconds(1.2);
        float back = atmos.VentPitchTarget;
        GD.Print($"   복구 뒤 피치 목표 = {back:0.00}");
        Check(back > 0.9f, $"회전이 정상으로 돌아온다 ({back:0.00})");

        // 다시 고장 내면 정적이 또 걸린다(한 번 쓰고 끝나지 않는다).
        atmos.KillVent();
        await Seconds(3.4);
        Check(atmos.VentHushActive, "다음 고장에도 정적이 다시 걸린다");

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task<float> PitchAfter(ControlRoomAtmosphere a, double s)
    {
        await Seconds(s);
        return a.VentPitchTarget;
    }

    private ControlRoomAtmosphere FindAtmos()
    {
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(GetTree().Root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is ControlRoomAtmosphere a) return a;
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        return null;
    }

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
