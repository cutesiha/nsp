using Godot;
using NSP.View;

namespace NSP.Debug;

// 괴물 동작 캡처 — 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/GhostPoseShot.tscn -- <저장 폴더>
//
// 실제 작업실 씬과 실제 CCTV 카메라 값을 그대로 쓰고, 동작마다
//   · 충돌하는 순간
//   · 동작이 끝난 뒤의 정지 자세
// 를 PNG 로 남긴다. 머리가 벽·기계·바닥에 닿는 높이와 관통 여부를 눈으로 확인하기 위한 것이다.
public partial class GhostPoseShot : Node3D
{
    private const float GhostScale = 3.2f;
    private const float StandY = 0.499f * GhostScale;

    private Camera3D _cam;
    private Node3D _entity;
    private GhostBehaviour _ghost;
    private Node3D _room;
    private bool _impacted;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        var probe = new FacilityCctvWorld();
        _cam = new Camera3D { Fov = probe.CameraFov, Current = true };
        AddChild(_cam);
        _cam.Position = probe.CameraPosition;
        _cam.LookAt(probe.CameraLookAt, Vector3.Up);
        probe.Free();

        _cam.Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.03f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.80f, 0.84f, 0.92f),
            AmbientLightEnergy = 1.15f,
        };
        var sun = new DirectionalLight3D { LightEnergy = 0.9f };
        AddChild(sun);
        sun.GlobalPosition = new Vector3(3, 4, 3);
        sun.LookAt(new Vector3(-1, 0, -1), Vector3.Up);

        _room = GD.Load<PackedScene>("res://scenes/rooms/room_maintenance.tscn").Instantiate<Node3D>();
        AddChild(_room);

        _entity = GD.Load<PackedScene>("res://scenes/props/entity.tscn").Instantiate<Node3D>();
        _entity.Scale = Vector3.One * GhostScale;
        AddChild(_entity);
        var rig = EntityRig.Build(_entity);

        _ghost = new GhostBehaviour();
        _ghost.Attach(_entity, rig, GhostScale, StandY);
        _ghost.Impact += (_, __) => _impacted = true;

        // 먼저 아무것도 하지 않은 기본 자세 — 리그가 제대로 갈렸는지 확인용.
        _ghost.Reset(new Vector3(0.7f, StandY, 0.5f));
        rig.Reset();
        _entity.Position = new Vector3(0.7f, StandY, 0.5f);
        _entity.RotationDegrees = new Vector3(0f, 135f, 0f);
        await Frames(6);
        Save(dir, "ghost_00_neutral.png");

        foreach (var pose in new[]
                 {
                     GhostPose.WallHeadbang, GhostPose.MachineHeadbang,
                     GhostPose.FloorHeadbang, GhostPose.RapidHeadbang, GhostPose.TwistedStare,
                 })
            await Shoot(pose, dir);

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Shoot(GhostPose pose, string dir)
    {
        _ghost.Reset(new Vector3(0.7f, StandY, 0.5f));
        if (!_ghost.Force(pose, _cam.Position, null, _room))
        {
            GD.Print($"   {pose} — 시작할 수 없음");
            return;
        }

        _impacted = false;
        int shot = 0;
        float t = 0f;
        // 시퀀스를 돌리며 충돌 프레임과 마지막 정지 자세를 찍는다.
        while (_ghost.Busy && t < 25f)
        {
            await Frame();
            float d = (float)GetProcessDeltaTime();
            t += d;
            _ghost.Tick(d, _cam.Position, null, _room);

            if (!_impacted || shot >= 2) continue;
            _impacted = false;
            shot++;
            await Frame();
            Save(dir, $"ghost_{pose}_impact{shot}.png");
        }

        // 동작이 끝난 뒤의 자세(바로 멀쩡해지지 않는지 확인).
        await Frames(4);
        Save(dir, $"ghost_{pose}_after.png");
        GD.Print($"   {pose} — {t:0.0}초 · 충돌 캡처 {shot}장");
    }

    private void Save(string dir, string file) =>
        GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
