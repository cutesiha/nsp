using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 차폐문을 **열어 둔 채** 괴물을 들여보내 관리자 사망 연출을 눈으로 확인한다.
//
//   godot --path . res://scenes/debug/DeathShot.tscn -- <저장 폴더>
//
// 확인할 것(지시서 §7)
//   · 침입 직전에 인기척 신호가 먼저 온다 — 예고 없는 즉사 금지
//   · 괴물이 **실제 모델로** 카메라 쪽으로 돌진한다
//   · 끝나면 다음 날로 넘어가지 않고 별도의 사망 기록 화면에서 멈춘다
public partial class DeathShot : Node
{
    private string _dir = "";
    private string _monsterId = "absentee";
    private int _shot;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        // 두 번째 인자로 개체를 고른다(absentee · infant · spider). 비우면 결번자.
        _monsterId = args.Length > 1 ? args[1] : "absentee";
        _ = Run();
    }

    private async System.Threading.Tasks.Task Run()
    {
        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        var sim = FacilitySimulation.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 3);
        GameModes.ForceSelect(GameMode.Competition);
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        var ids = sim.GetActiveEmployeeIds();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);

        GD.Print("\n\n################ 관리자 사망 ################");

        // 문은 열어 둔다 — 막지 못했을 때 어떻게 되는지를 보는 장면이다.
        var seg = sim.Corridors.ById("corridor_north");
        sim.Corridors.Unseal(seg.Id);
        Ok(!seg.Sealed, "북측 차폐문이 열려 있다");

        var t = sim.Threats.ForceSpawn(_monsterId, "corridor_north");
        Ok(t != null, $"{_monsterId} 가 북측 복도에 들어섰다");
        if (t == null) { GetTree().Quit(); return; }

        int dayBefore = GameState.Instance?.CurrentDay ?? 0;
        t.Phase = ThreatPhase.Approach;
        t.Progress = 0.86f;
        sim.SetCorridorSurveillance("corridor_north");
        await Seconds(0.8);
        Shot("01_문앞까지");

        // 문 앞 → (경고 유예) → 침입.
        for (int i = 0; i < 900 && t.Phase != ThreatPhase.AtDoor; i++) await Frame();
        Ok(t.Phase == ThreatPhase.AtDoor, "문 앞에 섰다(아직 들어오지 않았다)");
        Shot("02_문앞_유예");

        for (int i = 0; i < 1800 && !AdminDeathDirector.IsPlaying; i++) await Frame();
        Ok(AdminDeathDirector.IsPlaying, "침입 — 사망 연출이 시작됐다");

        // 돌진 구간을 촘촘히 찍는다.
        for (int i = 0; i < 6; i++)
        {
            await Seconds(0.12);
            Shot($"03_돌진_{i}");
        }
        await Seconds(0.45);
        Shot("04_충격");

        // 기록 화면.
        for (int i = 0; i < 600 && AdminDeathDirector.IsPlaying; i++) await Frame();
        await Seconds(1.2);
        Shot("05_사망기록");
        Ok(GameState.Instance?.CurrentPhase == GamePhase.Result, "근무가 멈춘 상태로 남아 있다");
        Ok(GameState.Instance?.CurrentDay == dayBefore, "다음 날로 넘어가지 않았다");

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        img.SavePng($"{_dir}/death_{_shot:00}_{name}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
