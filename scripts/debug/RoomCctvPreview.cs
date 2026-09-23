using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.View;

namespace NSP.Debug;

// 작업실 CCTV 미리보기 — 실제 room .tscn + 실제 직원 variant 씬 + 실제 WorkSpot 을 쓴다.
// 게임 로직은 전혀 돌지 않는다(시뮬레이션 없이 보기만 한다).
//
//   방 / 직원 / 인원(1~3) / 애니메이션 / WorkSpot 표시 / 카메라 를 버튼으로 고른다.
//   자유 카메라 : 마우스 좌드래그 = 회전, 휠 = 거리
//   F10 = Developer Hub 로 복귀
public partial class RoomCctvPreview : Node3D
{
    // 방·직원 목록과 CCTV 카메라 값은 실제 FacilityCctvWorld 것을 그대로 읽는다(중복 정의하지 않는다).
    private static Dictionary<string, string> RoomScenes =>
        (Dictionary<string, string>)typeof(FacilityCctvWorld)
            .GetField("RoomScenes", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new Dictionary<string, string>();

    private static Dictionary<string, string> EmployeeScenes =>
        (Dictionary<string, string>)typeof(FacilityCctvWorld)
            .GetField("EmployeeScenes", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new Dictionary<string, string>();

    private static Vector3[] Slots =>
        (Vector3[])typeof(FacilityCctvWorld)
            .GetField("Slots", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? new[] { Vector3.Zero };

    // 공용 라이브러리에 실제로 들어 있는 클립 18개.
    private static readonly string[] Clips =
    {
        "idle", "walk", "talk", "work", "repair", "inspect", "suspicious", "handoff",
        "sit_typing", "sit_assemble", "hammer_work", "lying_idle",
        "carry_box_normal", "carry_box_heavy", "pickup_box", "place_box",
        "isolated_struggle", "isolated_exhausted",
    };

    private readonly List<string> _roomIds = new();
    private readonly List<string> _employeeIds = new();

    private int _room, _employee, _clipIndex;
    private int _count = 2;
    private bool _showSpots = true;
    private bool _snapToSpot = true;
    private bool _freeCamera;

    private Node3D _roomNode;
    private readonly List<Node3D> _actors = new();
    private readonly List<Node3D> _markers = new();
    private Camera3D _cam;
    private Label _info;
    private Vector3 _camPos, _camLook;
    private float _orbitYaw = 0.8f, _orbitPitch = 0.6f, _orbitDist = 6.2f;

    public override void _Ready()
    {
        foreach (var k in RoomScenes.Keys) _roomIds.Add(k);
        foreach (var k in EmployeeScenes.Keys) _employeeIds.Add(k);
        _roomIds.Sort();

        // 실제 CCTV 카메라 값(FacilityCctvWorld 의 export 기본값)을 그대로 가져온다.
        var probe = new FacilityCctvWorld();
        _camPos = probe.CameraPosition;
        _camLook = probe.CameraLookAt;
        float fov = probe.CameraFov;
        probe.Free();

        _cam = new Camera3D { Fov = fov, Current = true };
        AddChild(_cam);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.03f, 0.04f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.82f, 0.85f, 0.9f),
            AmbientLightEnergy = 1.1f,
        };
        _cam.Environment = env;

        var sun = new DirectionalLight3D { LightEnergy = 0.85f };
        AddChild(sun);
        sun.GlobalPosition = new Vector3(3, 4, 3);
        sun.LookAt(new Vector3(-1, 0, -1), Vector3.Up);

        BuildUi();
        Rebuild();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F10 })
        {
            GetTree().ChangeSceneToFile("res://scenes/debug/DeveloperHub.tscn");
            return;
        }
        if (!_freeCamera) return;
        if (e is InputEventMouseMotion m && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            _orbitYaw -= m.Relative.X * 0.006f;
            _orbitPitch = Mathf.Clamp(_orbitPitch + m.Relative.Y * 0.005f, 0.08f, 1.45f);
            AimCamera();
        }
        if (e is InputEventMouseButton { Pressed: true } b)
        {
            if (b.ButtonIndex == MouseButton.WheelUp) _orbitDist = Mathf.Max(1.5f, _orbitDist - 0.4f);
            if (b.ButtonIndex == MouseButton.WheelDown) _orbitDist = Mathf.Min(14f, _orbitDist + 0.4f);
            if (b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) AimCamera();
        }
    }

    // --- 구성 ---------------------------------------------------------------

    private void Rebuild()
    {
        foreach (var a in _actors) a.QueueFree();
        _actors.Clear();
        _markers.Clear();
        _roomNode?.QueueFree();

        string roomId = _roomIds[_room];
        _roomNode = GD.Load<PackedScene>(RoomScenes[roomId]).Instantiate<Node3D>();
        AddChild(_roomNode);

        var spots = new List<RoomWorkSpot>();
        Collect(_roomNode, spots);
        spots.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        string employeeId = _employeeIds[_employee];
        for (int i = 0; i < _count; i++)
        {
            var actor = GD.Load<PackedScene>(EmployeeScenes[employeeId]).Instantiate<Node3D>();
            _roomNode.AddChild(actor);
            _actors.Add(actor);

            var spot = _snapToSpot && i < spots.Count ? spots[i] : null;
            if (spot != null) PlaceOnSpot(actor, spot);
            else
            {
                actor.Position = Slots[i % Slots.Length];
                actor.RotationDegrees = new Vector3(0, 135, 0);
            }
            PlayClip(actor, ClipFor(actor, spot));
        }

        if (_showSpots) BuildSpotMarkers(spots);
        AimCamera();
        RefreshInfo(spots.Count);
    }

    // RoomWorkVisualController 가 도착 후 하는 것과 같은 배치(위치 · 회전 · 앉기/눕기 높이).
    private static void PlaceOnSpot(Node3D actor, RoomWorkSpot spot)
    {
        actor.Position = new Vector3(spot.Position.X, 0, spot.Position.Z);
        actor.Rotation = spot.Rotation;
        var vr = actor.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr == null || !spot.IsSeated) return;

        string clip = spot.AnimationName ?? "";
        if (clip.StartsWith("lying") || clip.StartsWith("isolated_"))
        {
            vr.Position = vr.Position with { Y = spot.SeatHeight + 0.12f };
            return;
        }
        float hipY = actor.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips")?.Position.Y ?? 0.9f;
        vr.Position = vr.Position with { Y = spot.SeatHeight - hipY };
    }

    // 애니메이션을 '자동'으로 두면 그 자리의 클립을, 아니면 고른 클립을 재생한다.
    private string ClipFor(Node3D actor, RoomWorkSpot spot)
    {
        if (_clipIndex >= 0) return Clips[_clipIndex];
        bool female = actor.GetNodeOrNull("VisualRoot/RigRoot/Hips/Torso/Chest/BustMeshL") != null;
        return spot?.ClipFor(female) ?? "idle";
    }

    private static void PlayClip(Node3D actor, string clip)
    {
        var ap = actor.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (ap == null || !ap.HasAnimation(clip)) return;
        ap.Play(clip);
    }

    private void BuildSpotMarkers(List<RoomWorkSpot> spots)
    {
        foreach (var s in spots)
        {
            var mark = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.02f, RadialSegments = 12 },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.2f, 1f, 0.7f, 0.55f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            };
            _roomNode.AddChild(mark);
            mark.Position = s.Position + new Vector3(0, 0.02f, 0);

            var label = new Label3D
            {
                Text = $"{s.Id}\n{s.AnimationName}",
                FontSize = 42,
                PixelSize = 0.0016f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(0.55f, 1f, 0.85f),
                NoDepthTest = true,
            };
            _roomNode.AddChild(label);
            label.Position = s.Position + new Vector3(0, 0.55f, 0);
            _markers.Add(mark);
            _markers.Add(label);
        }
    }

    private static void Collect(Node n, List<RoomWorkSpot> o)
    {
        if (n is RoomWorkSpot s) o.Add(s);
        foreach (var c in n.GetChildren()) Collect(c, o);
    }

    private void AimCamera()
    {
        if (!_freeCamera)
        {
            _cam.Position = _camPos;
            _cam.LookAt(_camLook, Vector3.Up);
            return;
        }
        var target = new Vector3(0, 0.9f, 0);
        _cam.Position = target + new Vector3(
            Mathf.Sin(_orbitYaw) * Mathf.Cos(_orbitPitch),
            Mathf.Sin(_orbitPitch),
            Mathf.Cos(_orbitYaw) * Mathf.Cos(_orbitPitch)) * _orbitDist;
        _cam.LookAt(target, Vector3.Up);
    }

    // --- UI -----------------------------------------------------------------

    private void BuildUi()
    {
        var layer = new CanvasLayer { Layer = 120 };
        AddChild(layer);

        var col = new VBoxContainer { Position = new Vector2(14, 12) };
        col.AddThemeConstantOverride("separation", 4);
        layer.AddChild(col);

        var top = Row(col);
        var back = new Button { Text = "◀ DEV HUB (F10)" };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/debug/DeveloperHub.tscn");
        top.AddChild(back);

        var roomRow = Row(col);
        roomRow.AddChild(new Label { Text = "방 " });
        for (int i = 0; i < _roomIds.Count; i++)
        {
            int idx = i;
            var b = new Button { Text = _roomIds[i].Replace("_room", "") };
            b.Pressed += () => { _room = idx; Rebuild(); };
            roomRow.AddChild(b);
        }

        var empRow = Row(col);
        empRow.AddChild(new Label { Text = "직원 " });
        for (int i = 0; i < _employeeIds.Count; i++)
        {
            int idx = i;
            var b = new Button { Text = _employeeIds[i] };
            b.Pressed += () => { _employee = idx; Rebuild(); };
            empRow.AddChild(b);
        }
        for (int n = 1; n <= 3; n++)
        {
            int count = n;
            var b = new Button { Text = $"{n}명" };
            b.Pressed += () => { _count = count; Rebuild(); };
            empRow.AddChild(b);
        }

        var optRow = Row(col);
        var spotBtn = new Button { Text = "WorkSpot 표시" };
        spotBtn.Pressed += () => { _showSpots = !_showSpots; Rebuild(); };
        optRow.AddChild(spotBtn);
        var snapBtn = new Button { Text = "작업 자리에 붙이기" };
        snapBtn.Pressed += () => { _snapToSpot = !_snapToSpot; Rebuild(); };
        optRow.AddChild(snapBtn);
        var camBtn = new Button { Text = "카메라: CCTV / 자유" };
        camBtn.Pressed += () => { _freeCamera = !_freeCamera; AimCamera(); RefreshInfo(-1); };
        optRow.AddChild(camBtn);

        var clipRow1 = Row(col);
        clipRow1.AddChild(new Label { Text = "애니 " });
        var autoBtn = new Button { Text = "자동(자리 기본)" };
        autoBtn.Pressed += () => { _clipIndex = -1; Rebuild(); };
        clipRow1.AddChild(autoBtn);
        var clipRow2 = Row(col);
        for (int i = 0; i < Clips.Length; i++)
        {
            int idx = i;
            var b = new Button { Text = Clips[i] };
            b.Pressed += () => { _clipIndex = idx; Rebuild(); };
            (i < 9 ? clipRow1 : clipRow2).AddChild(b);
        }

        _info = new Label();
        _info.AddThemeColorOverride("font_color", new Color(0.62f, 0.92f, 1f));
        col.AddChild(_info);
    }

    private void RefreshInfo(int spotCount)
    {
        if (_info == null) return;
        string clip = _clipIndex < 0 ? "자동(자리 기본)" : Clips[_clipIndex];
        _info.Text = $"{_roomIds[_room]} · {_employeeIds[_employee]} × {_count}명 · {clip} · "
                   + $"카메라 {(_freeCamera ? "자유(좌드래그/휠)" : "실제 CCTV")}"
                   + (spotCount >= 0 ? $" · WorkSpot {spotCount}개" : "");
    }

    private static HBoxContainer Row(Node parent)
    {
        var r = new HBoxContainer();
        r.AddThemeConstantOverride("separation", 4);
        parent.AddChild(r);
        return r;
    }
}
