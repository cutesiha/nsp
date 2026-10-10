using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// CCTV 점프스케어 3종을 눈으로 확인한다(지시서 §6-4).
//
//   godot --path . res://scenes/debug/ScareShot.tscn -- <저장 폴더>
//
// 확인할 것
//   · 서로 다른 모델 · 각도 · 거리 — 같은 그림을 재탕하지 않는가
//   · 오른쪽 CCTV 를 거의 채우되 **왼쪽 시설 맵을 덮지 않는가**
//   · 연출이 끝나면 화면이 평소로 돌아오는가
public partial class ScareShot : Node
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
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(0.8);

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
        await Seconds(0.8);

        var ids = sim.GetActiveEmployeeIds();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.0);

        GD.Print("\n\n################ CCTV 점프스케어 ################");
        var world = FacilityCctvWorld.Instance;
        Ok(world != null, "CCTV 월드가 있다");
        if (world == null) { GetTree().Quit(); return; }

        // 평소 화면 한 장 — 비교용.
        ShotCctv("normal");

        // 세 배우를 차례로 카메라 앞에 세운다. 자동 추첨을 기다리지 않는다.
        foreach (var (id, dist, side, yaw) in new[]
                 {
                     ("absentee", 0.82f, 0.00f, 4f),
                     ("infant", 0.58f, 0.16f, -22f),
                     ("spider", 0.74f, -0.20f, 196f),
                 })
        {
            var def = MonsterThreatSystem.Monsters.FirstOrDefault(m => m.MonsterId == id);
            Ok(def != null, $"{id} 데이터가 있다");
            if (def == null) continue;

            string clip = !string.IsNullOrEmpty(def.AnimJumpscare) ? def.AnimJumpscare
                : !string.IsNullOrEmpty(def.AnimRun) ? def.AnimRun : def.AnimIdle;

            CCTVMonitorView.Instance?.FlashGlitch(0.9f);
            bool up = world.ShowScare(def, clip, dist, side, yaw, 0.90f);
            Ok(up, $"{id} 를 카메라 앞에 세웠다");
            await Seconds(0.35);
            ShotCctv($"{id}_scare");
            Shot($"{id}_room");
            world.HideScare();
            await Seconds(0.8);
        }

        ShotCctv("after");
        Ok(world.ScareForTest?.Root?.Visible != true, "끝난 뒤 화면에서 사라졌다");
        Ok(FacilitySimulation.Instance.Threats.Current == null, "위협은 하나도 생기지 않았다");

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        img.SavePng($"{_dir}/scare_{_shot:00}_{name}.png");
        _shot++;
    }

    private void ShotCctv(string name)
    {
        var tex = ControlRoom3DController.Instance?.CctvViewport?.GetTexture();
        if (tex == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        tex.GetImage()?.SavePng($"{_dir}/scare_{_shot:00}_{name}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
