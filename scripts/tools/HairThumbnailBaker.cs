using System.Collections.Generic;
using Godot;

namespace NSP.Tools;

// 헤어 목록에 쓸 **실제 모델 썸네일**을 굽는다. 파일 이름만 늘어놓지 않기 위한 것이다
// (지시서 §4). 한 번 구워 두면 스튜디오는 PNG 만 읽는다.
//
//   godot --path . res://scenes/tools/HairThumbnailBaker.tscn
//
// 창 모드로만 돌아간다 — 헤드리스는 그림을 그리지 않는다. 한 프레임에 하나씩,
// 같은 SubViewport 에 메시를 갈아 끼우며 굽기 때문에 장면에는 늘 하나만 올라간다.
public partial class HairThumbnailBaker : Node
{
    public const string OutDir = "res://assets/characters/hair/thumbs";
    private const int Size = 192;

    private HairCatalog _cat;
    private SubViewport _sub;
    private MeshInstance3D _mi;
    private Node3D _pivot;
    private Camera3D _cam;

    private readonly List<HairEntry> _queue = new();
    private int _at = -1;
    private int _wait;
    private int _ok, _fail;

    public override void _Ready()
    {
        _cat = HairCatalog.Load();
        foreach (HairEntry e in _cat.Entries) _queue.Add(e);
        DirAccess.MakeDirRecursiveAbsolute(OutDir);

        _sub = new SubViewport
        {
            Size = new Vector2I(Size, Size),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_sub);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.10f, 0.105f, 0.12f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.66f, 0.68f, 0.74f),
            AmbientLightEnergy = 1.3f,
        };
        _sub.AddChild(new WorldEnvironment { Environment = env });

        var key = new DirectionalLight3D { LightEnergy = 1.7f, ShadowEnabled = false };
        key.RotationDegrees = new Vector3(-28f, 35f, 0f);
        _sub.AddChild(key);
        var rim = new DirectionalLight3D
        {
            LightEnergy = 1.0f, ShadowEnabled = false,
            LightColor = new Color(0.8f, 0.86f, 1f),
        };
        rim.RotationDegrees = new Vector3(14f, -150f, 0f);
        _sub.AddChild(rim);

        _pivot = new Node3D();
        _sub.AddChild(_pivot);
        _cam = new Camera3D { Fov = 36f };
        _pivot.AddChild(_cam);

        _mi = new MeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.33f, 0.25f, 0.21f),
                Roughness = 0.6f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            },
        };
        _sub.AddChild(_mi);

        GD.Print($"################ 썸네일 {_queue.Count}개 굽기 → {OutDir}");
    }

    public override void _Process(double delta)
    {
        if (_wait > 0) { _wait--; return; }

        if (_at >= 0 && _at < _queue.Count) Grab(_queue[_at]);

        _at++;
        if (_at >= _queue.Count)
        {
            GD.Print($"################ 썸네일 끝 — 성공 {_ok}개 · 실패 {_fail}개");
            GetTree().Quit();
            return;
        }
        Stage(_queue[_at]);
        _wait = 2;
    }

    // 헤어 하나를 화면 가운데 꽉 차게 세운다. 3/4 각도라 앞머리와 뒤통수가 같이 보인다.
    private void Stage(HairEntry e)
    {
        var mesh = ResourceLoader.Load<Mesh>(e.MeshPath);
        _mi.Mesh = mesh;
        if (mesh == null) return;

        Aabb box = mesh.GetAabb();
        _mi.Position = -box.GetCenter();
        float r = box.Size.Length() * 0.5f;
        _pivot.RotationDegrees = new Vector3(-12f, 35f, 0f);
        _cam.Position = new Vector3(0f, 0f, r / Mathf.Tan(Mathf.DegToRad(_cam.Fov * 0.5f)) * 1.12f);
    }

    private void Grab(HairEntry e)
    {
        Image img = _sub.GetTexture()?.GetImage();
        if (img == null) { _fail++; return; }
        Error err = img.SavePng($"{OutDir}/{e.Id}.png");
        if (err == Error.Ok) _ok++;
        else { _fail++; GD.Print($"  !! {e.Id} 저장 실패: {err}"); }
    }
}
