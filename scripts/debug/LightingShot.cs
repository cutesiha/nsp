using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 중앙제어실 조명 확인용 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/LightingShot.tscn -- <저장 폴더>
//
// 메인 씬을 "DAY1 바로 시작" 경로로 띄워 배치 단계 / 근무(Live) 화면을 PNG 로 저장하고,
// 근무 중 몇 초간의 평균·최저 FPS 를 출력한 뒤 종료한다.
public partial class LightingShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        GD.Print($"renderer = {ProjectSettings.GetSetting("rendering/renderer/rendering_method")} · {RenderingServer.GetVideoAdapterName()}");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        var scene = GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate();
        AddChild(scene);

        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        // 성능 비교용 옵션: -- <폴더> noshadow / noglow / nothing
        string variant = args.Length > 1 ? args[1] : "";
        if (variant is "noshadow" or "nothing")
            foreach (var l in scene.FindChildren("*ScreenLight", "SpotLight3D", true, false).Cast<SpotLight3D>()) l.ShadowEnabled = false;
        if (variant is "noglow" or "nothing")
            ((WorldEnvironment)scene.FindChild("WorldEnvironment", true, false)).Environment.GlowEnabled = false;
        if (variant != "") GD.Print("variant = " + variant);
        await Frames(120);
        Save(dir, $"lighting_schedule{(variant == "" ? "" : "_" + variant)}.png");
        foreach (var l in GetTree().Root.FindChildren("*", "Light3D", true, false).Cast<Light3D>())
            GD.Print($"light {l.GetPath()} vis={l.IsVisibleInTree()} e={l.LightEnergy:0.00} shadow={l.ShadowEnabled}");

        var ctl = ControlRoom3DController.Instance;
        var map = ScheduleMapView.Instance;
        var sim = FacilitySimulation.Instance;
        if (map != null && sim != null && ctl != null)
        {
            sim.AssignToRoom("wolf", "core_room");
            await Frames(3);
            var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
            var vp = ctl.ScheduleMapViewport;
            vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
            await Frame();
            vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        }
        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Frames(150);
        await Frames(30);
        Save(dir, $"lighting_live{(variant == "" ? "" : "_" + variant)}.png");

        // FPS — 근무 중 4초.
        var samples = new System.Collections.Generic.List<double>();
        ulong t0 = Time.GetTicksMsec();
        ulong last = t0;
        while (Time.GetTicksMsec() - t0 < 4000)
        {
            await Frame();
            ulong now = Time.GetTicksMsec();
            samples.Add(now - last);
            last = now;
        }
        double avgMs = samples.Average();
        GD.Print($"FPS avg = {1000.0 / avgMs:0.0} · worst frame = {samples.Max():0} ms · frames = {samples.Count}");
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private void Save(string dir, string file)
        => GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
