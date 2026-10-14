using System.Collections.Generic;
using Godot;

namespace NSP.Debug;

// 머리카락을 갈아 끼운 기존 직원 모델을 눈으로 확인한다.
//
//   godot --path . res://scenes/debug/EmployeeHairShot.tscn -- <저장폴더>
//
// 여섯 명을 정면·측면·뒤에서 한 장씩. 가면과 몸은 그대로여야 하고, 머리카락만
// 바뀌어 있어야 한다.
public partial class EmployeeHairShot : Node
{
    private static readonly (string Id, string Scene)[] Employees =
    {
        ("rabbit", "RabbitEmployee3D"), ("cat", "CatEmployee3D"), ("fox", "FoxEmployee3D"),
        ("sheep", "SheepEmployee3D"), ("wolf", "WolfEmployee3D"), ("dog", "DogEmployee3D"),
    };

    private static readonly (string Tag, float Yaw, bool Face)[] Views =
    {
        ("front", 0f, true), ("side", 72f, true), ("back", 180f, true), ("full", 16f, false),
    };

    private Node3D _stage, _camTarget, _camPivot;
    private Camera3D _cam;
    private string _dir = "user://emphair";
    private int _at = -1, _wait;
    private readonly List<string> _saved = new();
    private Node3D _pending;
    private bool _pendingFace;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        if (args.Length > 0) _dir = args[0];
        DirAccess.MakeDirRecursiveAbsolute(_dir);

        var world = new Node3D();
        AddChild(world);
        world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.12f, 0.125f, 0.14f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.60f, 0.62f, 0.68f),
                AmbientLightEnergy = 1.15f,
            },
        });
        var key = new DirectionalLight3D { LightEnergy = 1.5f, ShadowEnabled = true };
        key.RotationDegrees = new Vector3(-32f, 34f, 0f);
        world.AddChild(key);
        var fill = new DirectionalLight3D { LightEnergy = 0.7f };
        fill.RotationDegrees = new Vector3(-10f, -130f, 0f);
        world.AddChild(fill);

        _camTarget = new Node3D();
        world.AddChild(_camTarget);
        _camPivot = new Node3D();
        _camTarget.AddChild(_camPivot);
        _cam = new Camera3D { Fov = 40f };
        _camPivot.AddChild(_cam);

        _stage = new Node3D();
        world.AddChild(_stage);

        GD.Print($"################ 직원 헤어 교체 결과 캡처 → {_dir}");
        _wait = 16;
    }

    public override void _Process(double delta)
    {
        if (_pending != null)
        {
            float headY = _pending.GlobalPosition.Y;
            if (headY > 0.1f)
            {
                _camTarget.Position = new Vector3(0f,
                    _pendingFace ? headY - 0.04f : headY * 0.56f, 0f);
                _pending = null;
            }
        }
        if (_wait-- > 0) return;
        if (_at >= 0) Grab(_at);

        _at++;
        int total = Employees.Length * Views.Length;
        if (_at >= total)
        {
            GD.Print($"################ 끝 — {_saved.Count}장");
            GetTree().Quit();
            return;
        }

        (string id, string scene) = Employees[_at / Views.Length];
        (string tag, float yaw, bool face) = Views[_at % Views.Length];

        foreach (Node c in _stage.GetChildren()) { _stage.RemoveChild(c); c.QueueFree(); }
        var packed = ResourceLoader.Load<PackedScene>(
            $"res://scenes/cctv_characters/employees/{scene}.tscn");
        if (packed == null) { GD.Print($"  !! {scene} 를 열지 못했다"); _wait = 1; return; }
        var inst = packed.Instantiate<Node3D>();
        _stage.AddChild(inst);

        // 남녀 베이스는 키가 다르다. 머리 높이를 노드에서 직접 읽되, 트리에 막 붙인
        // 노드의 전역 좌표는 아직 갱신 전이라 한 프레임 뒤에 잡는다.
        _pending = inst.GetNodeOrNull<Node3D>(
            "VisualRoot/RigRoot/Hips/Torso/Chest/Neck/Head/HairAnchor");
        _pendingFace = face;
        _camPivot.RotationDegrees = new Vector3(face ? -2f : -6f, 180f + yaw, 0f);
        _cam.Position = new Vector3(0f, 0f, face ? 0.72f : 2.6f);

        if (_at % Views.Length == 0) GD.Print($"  {id}");
        _wait = 4;
    }

    private void Grab(int i)
    {
        Image img = GetViewport().GetTexture()?.GetImage();
        if (img == null) return;
        (string id, _) = Employees[i / Views.Length];
        (string tag, _, _) = Views[i % Views.Length];
        string p = $"{_dir}/{id}_{tag}.png";
        if (img.SavePng(p) == Error.Ok) _saved.Add(p);
    }
}
