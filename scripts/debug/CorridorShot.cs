using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 복도 미니맵을 실제 근무 화면에서 찍는다.
//
//   godot --path . res://scenes/debug/CorridorShot.tscn -- <저장 폴더>
//
// 헤드리스로는 그림이 나오지 않는다 — 창 모드로 돌려야 한다.
// 왼쪽 CRT(시설 미니맵) 뷰포트를 통째로 저장하므로 글자 크기를 그대로 확인할 수 있다.
public partial class CorridorShot : Node
{
    private string _dir = "";
    private int _shot;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _ = Run();
    }

    private async System.Threading.Tasks.Task Run()
    {
        DebugEntryPoint.SkipTitleOnNextBoot();
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        var sim = FacilitySimulation.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 2);
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        var ids = sim.GetActiveEmployeeIds();
        string[] rooms = { "storage_room", "power_room", "maintenance_room", "guard_room", "core_room", "vent_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(4.0);   // 초기 배치 이동이 끝나기를 기다린다

        GD.Print("\n\n################ 복도 미니맵 ################");
        var net = sim.Corridors;
        GD.Print($"   구간 {net.Segments.Count}개 · 차폐 가능 {net.Blockable.Count()}개 · 선택 {net.SelectedId}");
        Shot("01_평상시");
        ProbeLane();

        // 차폐 전력을 올리고 동측 접근 복도를 닫는다.
        if (!GameState.Instance.IsConsumerPowered(PowerConsumer.Barrier))
            GameState.Instance.TryTogglePower(PowerConsumer.Barrier);
        net.Select("corridor_east");
        await Seconds(0.3);
        Shot("02_통로선택");

        net.Seal("corridor_east", out string why);
        GD.Print($"   닫기 — {(string.IsNullOrEmpty(why) ? "수락" : why)}");
        await Seconds(0.4);
        Shot("03_닫히는중");
        await Seconds(1.0);
        var seg = net.ById("corridor_east");
        Ok(seg.Sealed, $"동측 접근 복도 {seg.StatusText}");
        Shot("04_차폐됨");

        // 저장고 직원을 정비실로 보내 우회를 눈으로 본다.
        string who = ids[0];
        sim.AssignToRoom(who, "maintenance_room");
        await Seconds(2.0);
        var st = sim.GetEmployeeState(who);
        GD.Print($"   {who}: {st.CurrentRoomId} → {st.TargetRoomId} (이동중 {st.IsMoving})");
        Shot("05_우회중");

        // 다른 구간을 골라 본다 — 문은 움직이지 않아야 한다.
        net.Select("corridor_north");
        await Seconds(0.4);
        Ok(seg.Sealed, "다른 통로를 골라도 닫힌 문은 그대로");
        Shot("06_다른통로선택");

        net.Unseal("corridor_east");
        await Seconds(1.2);
        Ok(!seg.Sealed, "열림");
        Shot("07_열림");

        // ── 복도 CCTV ───────────────────────────────────────────────
        GD.Print("\n   ── 복도 카메라 ──");
        var world = FacilityCctvWorld.Instance;
        Ok(world != null, "CCTV 3D 월드를 찾았다");

        foreach (string id in new[] { "corridor_north", "corridor_west", "corridor_east", "corridor_south_seal" })
        {
            var s = net.ById(id);
            sim.SetCorridorSurveillance(id);
            await Seconds(0.9);
            Ok(sim.SurveillanceCorridorId == id && string.IsNullOrEmpty(sim.SurveillanceTargetRoomId),
                $"{s.CctvCameraId} 로 전환 — 작업실 감시는 꺼진다(관측 게이지도 안 찬다)");
            ShotCctv($"10_{s.CctvCameraId}_먼시야");
        }

        // 두 번째 구도 — 차폐문 근접.
        if (world != null) world.CorridorNearView = true;
        sim.SetCorridorSurveillance("corridor_east");
        await Seconds(0.9);
        ShotCctv("13_CAM_문앞");

        // 문이 닫히는 모습이 영상에 실제로 보인다.
        net.Seal("corridor_east", out _);
        await Seconds(0.4);
        ShotCctv("14_닫히는중");
        await Seconds(1.0);
        Ok(net.ById("corridor_east").Sealed, "영상 안의 셔터가 내려왔다");
        ShotCctv("15_차폐됨");

        // CCTV 전원을 끄면 복도도 NO SIGNAL — 복도만 계속 보이면 안 된다.
        GameState.Instance.TryTogglePower(PowerConsumer.CctvWatch);
        await Seconds(0.8);
        Ok(!GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch), "CCTV 전원 OFF");
        ShotCctv("16_NO_SIGNAL");
        // 영상이 꺼져도 문 판정은 계속 돈다.
        float before = net.ById("corridor_east").SealedSeconds;
        await Seconds(1.0);
        Ok(net.ById("corridor_east").SealedSeconds > before,
            "화면이 꺼져 있어도 차폐 시간은 계속 흐른다 (영상과 판정은 별개)");
        GameState.Instance.TryTogglePower(PowerConsumer.CctvWatch);

        // 작업실로 돌아간다.
        if (world != null) world.CorridorNearView = false;
        sim.SetSurveillanceTarget("core_room");
        await Seconds(1.0);
        Ok(string.IsNullOrEmpty(sim.SurveillanceCorridorId), "작업실 카메라로 되돌아왔다");
        ShotCctv("17_작업실_복귀");

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    // 경비실↔정비실 통로가 **그림에서도** 보이는가를 픽셀로 확인한다.
    // (남측 격벽을 올린 뒤로 그 띠가 안 보이는 것처럼 느껴져, 눈이 아니라 값으로 센다.)
    private void ProbeLane()
    {
        var img = ControlRoom3DController.Instance?.FacilityViewport?.GetTexture()?.GetImage();
        if (img == null) { GD.Print("   (헤드리스 — 픽셀 확인 생략)"); return; }
        var map = FacilityMonitorView.Instance?.MinimapForTest;
        if (map == null) { GD.Print("   (미니맵을 찾지 못했다)"); return; }

        // 미니맵 로컬 좌표 → 뷰포트 좌표.
        // **스케일까지 들어간 변환**을 써야 한다 — SubViewport 안의 UI 는 AddScaledView 가
        // 통째로 확대해 두므로, 로컬 좌표를 그냥 더하면 수십 px 어긋난 자리를 찍게 된다.
        var xf = map.GetGlobalTransformWithCanvas();
        Vector2 lane = xf * map.LaneMidForTest("guard_room", "maintenance_room");
        Vector2 empty = xf * (map.LaneMidForTest("guard_room", "maintenance_room") + new Vector2(0f, 22f));
        var cl = img.GetPixel((int)lane.X, (int)lane.Y);
        var ce = img.GetPixel((int)empty.X, (int)empty.Y);
        GD.Print($"      경비실↔정비실 띠 픽셀 {cl}  /  옆 빈 자리 {ce}");
        Ok(cl.V > ce.V + 0.02f, $"그 통로가 화면에 실제로 그려져 있다 (밝기 {cl.V:0.000} > {ce.V:0.000})");
    }

    // 오른쪽 CRT(CCTV) 뷰포트.
    private void ShotCctv(string name)
    {
        var tex = ControlRoom3DController.Instance?.CctvViewport?.GetTexture();
        if (tex == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        tex.GetImage()?.SavePng($"{_dir}/corridor_{_shot:00}_{name}.png");
        _shot++;
    }

    // 왼쪽 CRT(시설 미니맵) 뷰포트만 저장한다.
    private void Shot(string name)
    {
        var tex = ControlRoom3DController.Instance?.FacilityViewport?.GetTexture();
        if (tex == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        tex.GetImage()?.SavePng($"{_dir}/corridor_{_shot:00}_{name}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
