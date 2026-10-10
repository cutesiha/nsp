using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 괴물이 **실제 3D 복도 영상에** 보이는지 눈으로 확인한다.
//
//   godot --path . res://scenes/debug/MonsterShot.tscn -- <저장 폴더>
//
// 헤드리스로는 그림이 나오지 않는다 — 창 모드로 돌려야 한다.
// "어두운 화면에 괴물 이미지 한 장" 이 아니라 복도 벽 · 바닥 · 배관 · 조명 · 차폐문과
// 함께, 거리에 따라 점점 커지는 모습이 찍혀야 한다(지시서 §4).
public partial class MonsterShot : Node
{
    private string _dir = "";
    private int _shot;
    private FacilitySimulation _sim;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _ = Run();
    }

    private async System.Threading.Tasks.Task Run()
    {
        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        _sim = FacilitySimulation.Instance;
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

        var ids = _sim.GetActiveEmployeeIds();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) _sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(3.0);

        GD.Print("\n\n################ 괴물 · 복도 CCTV ################");
        var th = _sim.Threats;
        var net = _sim.Corridors;

        // 괴물 세 종을 차례로 북측 복도에 세우고 거리별로 찍는다.
        foreach (string id in new[] { "absentee", "infant", "spider" })
        {
            th.Reset();
            var t = th.ForceSpawn(id, "corridor_north");
            Ok(t != null, $"{id} 를 북측 복도에 띄웠다");
            if (t == null) continue;

            _sim.SetCorridorSurveillance("corridor_north");
            await Seconds(0.6);
            Ok(_sim.SurveillanceCorridorId == "corridor_north", "CAM-N01 로 보는 중");

            // 외곽 구간을 건너뛰고 복도 안으로 들여보낸다.
            t.Phase = ThreatPhase.Approach;
            foreach (float p in new[] { 0.05f, 0.45f, 0.92f })
            {
                t.Progress = p;
                await Seconds(0.5);
                ShotCctv($"{id}_{(int)(p * 100):00}pct");
            }

            var actor = FacilityCctvWorld.Instance?.MonsterForTest;
            Ok(actor != null && actor.MonsterId == id, $"{id} 모델이 월드에 올라와 있다");
            Ok(actor?.Root.Visible == true, "화면에 보이는 상태다");
        }

        // 문을 내린 상태 — 괴물이 문 앞에서 멈춰 두드린다.
        // 세 마리 모두 찍는다. 키가 작은 것(아기 괴물 · 거미)이 닫힌 문짝 뒤에
        // 통째로 가려지지 않는지가 핵심이다(§4 "괴물이 문을 긁는 모습이 보여야 한다").
        var seg = net.ById("corridor_north");
        foreach (string id in new[] { "absentee", "infant", "spider" })
        {
            th.Reset();
            net.Unseal(seg.Id);
            await Seconds(4.2);   // 재가동 쿨다운(3s)이 풀릴 때까지 기다린다
            var t2 = th.ForceSpawn(id, "corridor_north");
            t2.Phase = ThreatPhase.Approach;
            t2.Progress = 1f;
            if (!GameState.Instance.IsConsumerPowered(PowerConsumer.Barrier))
                GameState.Instance.TryTogglePower(PowerConsumer.Barrier);
            net.Select(seg.Id);
            net.Seal(seg.Id, out _);
            await Seconds(1.4);
            Ok(seg.Sealed, $"북측 차폐 ({id})");
            for (int i = 0; i < 400 && t2.Phase != ThreatPhase.Pounding; i++) await Frame();
            await Seconds(0.6);
            Ok(t2.Phase == ThreatPhase.Pounding, $"{id} 가 문을 두드리는 중");

            if (FacilityCctvWorld.Instance != null) FacilityCctvWorld.Instance.CorridorNearView = false;
            _sim.SetCorridorSurveillance("corridor_north");
            await Seconds(0.7);
            ShotCctv($"{id}_pound_far");

            // 문 앞 구도로 한 번 더.
            if (FacilityCctvWorld.Instance != null) FacilityCctvWorld.Instance.CorridorNearView = true;
            _sim.SetCorridorSurveillance("corridor_north");
            await Seconds(0.8);
            ShotCctv($"{id}_pound_near");
        }
        Shot("pounding_room");

        // 서측 · 동측 복도도 한 장씩.
        if (FacilityCctvWorld.Instance != null) FacilityCctvWorld.Instance.CorridorNearView = false;
        foreach (string lane in new[] { "corridor_west", "corridor_east" })
        {
            th.Reset();
            net.Unseal(seg.Id);
            await Seconds(1.0);
            var t3 = th.ForceSpawn("absentee", lane);
            if (t3 == null) continue;
            t3.Phase = ThreatPhase.Approach;
            t3.Progress = 0.6f;
            _sim.SetCorridorSurveillance(lane);
            await Seconds(0.8);
            ShotCctv($"{net.ById(lane).CctvCameraId}_접근중");
        }

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        img.SavePng($"{_dir}/mon_{_shot:00}_{name}.png");
        _shot++;
    }

    private void ShotCctv(string name)
    {
        var tex = ControlRoom3DController.Instance?.CctvViewport?.GetTexture();
        if (tex == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        tex.GetImage()?.SavePng($"{_dir}/mon_{_shot:00}_{name}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
