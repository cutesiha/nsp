using Godot;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 접근 복도 **세 개의 생김새**를 CCTV 자리에서 한 장씩 찍는다.
//
//   godot --path . res://scenes/debug/CorridorLookShot.tscn -- <저장 폴더>
//
// 창 모드로 돌려야 한다(헤드리스는 그림을 그리지 않는다).
//
// 보는 것은 둘이다.
//   ① 북 · 서 · 동이 **한눈에 구분되는가** — 조명 색 · 바닥선 · 벽 설비
//   ② 차폐문을 닫았을 때 가운데 틈이 **사람이 지나갈 만큼 벌어져 보이지 않는가**
//
// 시설 시뮬레이션을 띄우지 않는다. CorridorVisual 은 CorridorSegment 한 장만 있으면
// 스스로 복도를 짓는 평범한 클래스라, 여기서는 그 조각만 손으로 만들어 세운다.
public partial class CorridorLookShot : Node
{
    private static readonly (string Id, string Name, CorridorSide Side, string Cam, string Outer)[] Lanes =
    {
        ("corridor_north", "북측 접근 복도", CorridorSide.North, "CAM-N01", "코어실"),
        ("corridor_west", "서측 접근 복도", CorridorSide.West, "CAM-W01", "발전실"),
        ("corridor_east", "동측 접근 복도", CorridorSide.East, "CAM-E01", "저장고"),
    };

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        DirAccess.MakeDirRecursiveAbsolute(dir);

        var world = new Node3D();
        AddChild(world);
        world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.12f, 0.13f, 0.14f),
                AmbientLightEnergy = 0.6f,
            },
        });

        var cam = new Camera3D { Fov = 72f, Current = true };
        world.AddChild(cam);

        foreach (var lane in Lanes)
        {
            var seg = new CorridorSegment
            {
                Id = lane.Id, DisplayName = lane.Name, Side = lane.Side,
                RoomA = "central_office", RoomB = lane.Outer,
                Kind = CorridorKind.ControlAccess, IsBlockable = true,
                CctvCameraId = lane.Cam, ThreatLane = true,
            };

            var vis = new CorridorVisual();
            var root = vis.Build(seg, "중앙제어실", lane.Outer, controlRoomIsA: true);
            world.AddChild(root);
            root.Visible = true;

            // CCTV 는 중앙제어실 쪽 끝에 달려 바깥을 본다 — 게임에서 보는 그 구도다.
            cam.Position = vis.FarCameraPosition;
            cam.LookAt(new Vector3(0f, 1.5f, vis.DoorZ + 6f), Vector3.Up);
            await Frames(6);
            Save(dir, $"{lane.Side}_01_열림.png");

            // 문을 닫는다 — 가운데 틈 폭을 보는 장면.
            seg.State = BarrierState.Sealed;
            seg.Shut = 1f;
            vis.Sync(seg, 0f);
            await Frames(4);
            Save(dir, $"{lane.Side}_02_닫힘.png");

            // 문 바로 앞에서 한 장 더 — 틈 폭이 실제로 얼마나 보이는지.
            cam.Position = new Vector3(0f, 1.6f, vis.DoorZ - 2.6f);
            cam.LookAt(new Vector3(0f, 1.4f, vis.DoorZ), Vector3.Up);
            await Frames(4);
            Save(dir, $"{lane.Side}_03_문앞.png");

            root.QueueFree();
            await Frames(2);
        }

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private void Save(string dir, string file)
    {
        GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);
        GD.Print("   " + file);
    }

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
