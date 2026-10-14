using Godot;

namespace NSP.Debug;

// 작업실 외형 점검용 캡처 — 실제 CCTV 카메라 값으로 방마다 한 장씩 찍는다.
//   godot --path . res://scenes/debug/RoomLookShot.tscn -- <저장 폴더>
public partial class RoomLookShot : Node
{
    private static readonly (string Id, string Path)[] Rooms =
    {
        ("isolation_room", "res://scenes/rooms/nsp_isolation_room.tscn"),
        ("core_room", "res://scenes/rooms/room_core.tscn"),
        ("guard_room", "res://scenes/rooms/room_guard.tscn"),
        ("medical_room", "res://scenes/rooms/room_medical.tscn"),
        ("vent_room", "res://scenes/rooms/room_vent.tscn"),
        ("maintenance_room", "res://scenes/rooms/room_maintenance.tscn"),
        ("storage_room", "res://scenes/rooms/room_storage.tscn"),
        ("rest_room", "res://scenes/rooms/room_rest.tscn"),
    };

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        var vp = new SubViewport
        {
            Size = new Vector2I(960, 720),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = true,
            Disable3D = false,
        };
        AddChild(vp);
        vp.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.1f, 0.11f, 0.14f),
                AmbientLightEnergy = 0.3f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
            },
        });
        var cam = new Camera3D { Fov = 58f, Current = true };
        vp.AddChild(cam);
        cam.GlobalPosition = new Vector3(3.5f, 3.05f, 3.5f);
        cam.LookAt(new Vector3(-0.4f, 0.5f, -0.5f), Vector3.Up);

        foreach (var (id, path) in Rooms)
        {
            if (!ResourceLoader.Exists(path)) { GD.Print($"LOOK {id} 없음"); continue; }
            var room = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
            if (room == null) continue;
            // 마감은 방 씬 안의 Dressing 노드가 스스로 붙인다 — 여기서 부르지 않는다.
            vp.AddChild(room);
            await Seconds(0.45);
            vp.GetTexture()?.GetImage()?.SavePng($"{dir}/{id}.png");
            room.QueueFree();
            await Seconds(0.15);
        }
        GD.Print("LOOK saved");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), "timeout");
}
