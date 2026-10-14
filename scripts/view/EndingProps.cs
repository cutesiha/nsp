using System.Threading.Tasks;
using Godot;

namespace NSP.View;

// 엔딩에서만 쓰는 방 안의 프롭 — 문 쪽 연출 전부.
//
// 게임 내내 플레이어는 두 모니터만 봤다. 그래서 **문** 은 이 게임에서 한 번도 본 적 없는
// 방향이고, 거기에 무언가가 서 있거나 지나가는 것만으로 충분히 무섭다.
// 디테일은 필요 없다 — 역광에 비치는 윤곽 하나면 된다.
//
// 씬의 빈 컨테이너 HorrorProps 밑에 붙는다(엔딩이 끝나면 통째로 사라진다).
//
// 주의: 뒷벽(Wall_Back)은 z = -2.6 의 **막힌 판**이다. 복도 쪽에 광원을 두면 벽에 가려
// 아무것도 보이지 않는다. 그래서 "복도의 빛"과 "그림자"를 문 창 **바로 앞면**에 그린다 —
// 창 하나만 밝아지고 그 안을 무언가가 가리는 것으로 읽힌다.
public partial class EndingProps : Node3D
{
    // 문(ControlRoom/Door)은 (1.55, 0, -2.55), 창은 그 로컬 (0, 1.55, 0.01) 에 있다.
    private static readonly Vector3 DoorWorld = new(1.55f, 0f, -2.55f);
    private static readonly Vector3 WindowWorld = new(1.55f, 1.55f, -2.54f);

    private Node3D _door;
    private MeshInstance3D _corridor;   // 복도 쪽 밝은 면(역광판)
    private MeshInstance3D _figure;     // 사람 형체의 검은 실루엣
    private MeshInstance3D _handle;     // 문 손잡이
    private OmniLight3D _corridorLight;
    private StandardMaterial3D _corridorMat;

    // 카메라가 돌아볼 지점 — 문에 난 작은 창.
    public Vector3 DoorLookPoint => WindowWorld;

    public void Attach(ControlRoom3DController ctl)
    {
        var host = ctl?.GetTree()?.Root?.FindChild("HorrorProps", true, false) as Node3D
                   ?? ctl?.GetNodeOrNull<Node3D>("HorrorProps");
        (host ?? (Node)ctl ?? this).AddChild(this);
        _door = ctl?.GetNodeOrNull<Node3D>("ControlRoom/Door");
        Build();
    }

    public override void _Ready()
    {
        if (_corridor == null) Build();
    }

    private void Build()
    {
        if (_corridor != null) return;

        // 복도 쪽 면 — 문 뒤에서 들어오는 빛. 켜지기 전에는 보이지 않는다.
        _corridorMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.85f, 0.82f, 0.74f),
            EmissionEnabled = true,
            Emission = new Color(0.95f, 0.90f, 0.80f),
            EmissionEnergyMultiplier = 1.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        // 창 바로 앞면(z = -2.505)에 창과 같은 크기로 깐다 — 이것이 "복도의 빛"이다.
        _corridor = new MeshInstance3D
        {
            Name = "EndingCorridorPanel",
            Mesh = new QuadMesh { Size = new Vector2(0.30f, 0.30f) },
            Position = WindowWorld with { Z = -2.505f },
            MaterialOverride = _corridorMat,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_corridor);

        // 사람 형체 — 창 안을 가리는 윤곽. 어깨(사각) + 머리(구)뿐이다.
        _figure = new MeshInstance3D
        {
            Name = "EndingDoorFigure",
            Mesh = new QuadMesh { Size = new Vector2(0.145f, 0.30f) },
            Position = new Vector3(DoorWorld.X, 1.47f, -2.50f),
            MaterialOverride = Silhouette(),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_figure);
        _figure.AddChild(new MeshInstance3D
        {
            Name = "Head",
            Mesh = new SphereMesh { Radius = 0.040f, Height = 0.090f, RadialSegments = 10, Rings = 6 },
            Position = new Vector3(0f, 0.155f, 0.004f),
            MaterialOverride = Silhouette(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // 방 안으로 새어 드는 빛. 벽 뒤가 아니라 **문 앞**에 둔다(뒤에 두면 벽이 가린다).
        _corridorLight = new OmniLight3D
        {
            Name = "EndingCorridorLight",
            Position = new Vector3(DoorWorld.X, 1.55f, -2.35f),
            LightColor = new Color(1f, 0.93f, 0.80f),
            LightEnergy = 0f,
            OmniRange = 2.6f,
            ShadowEnabled = false,
            Visible = false,
        };
        AddChild(_corridorLight);

        // 손잡이 — 문에는 원래 손잡이 메시가 없다. 어두운 금속 막대 하나만 세운다.
        _handle = new MeshInstance3D
        {
            Name = "EndingDoorHandle",
            Mesh = new BoxMesh { Size = new Vector3(0.035f, 0.035f, 0.16f) },
            Position = new Vector3(DoorWorld.X - 0.33f, 1.02f, -2.47f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.22f, 0.23f, 0.25f), Metallic = 0.8f, Roughness = 0.35f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_handle);
    }

    private static StandardMaterial3D Silhouette() => new()
    {
        AlbedoColor = new Color(0.012f, 0.012f, 0.015f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // ── TRUE : 재난 당일. 문 창에 사람 형체가 **서 있다**. 움직이지 않는다. ──────
    public void ShowStandingFigure(bool on, float corridorEnergy = 2.2f)
    {
        if (_corridor == null) return;
        _corridorMat.EmissionEnergyMultiplier = corridorEnergy;
        _corridorMat.AlbedoColor = _corridorMat.AlbedoColor with { A = 1f };
        _corridor.Visible = on;
        _figure.Visible = on;
        _figure.Position = _figure.Position with { X = DoorWorld.X };
        if (_corridorLight != null)
        {
            _corridorLight.Visible = on;
            _corridorLight.LightEnergy = on ? 0.45f : 0f;
        }
    }

    // ── BAD : 복도 쪽에서 빛이 들어온다(문 창만 밝아진다). ──────────────────
    public void SetCorridorLight(bool on, float energy = 0.55f)
    {
        if (_corridor == null) return;
        _corridorMat.EmissionEnergyMultiplier = energy;
        _corridor.Visible = on;
        if (_corridorLight != null)
        {
            _corridorLight.Visible = on;
            _corridorLight.LightEnergy = on ? energy * 0.55f : 0f;
        }
    }

    // 그림자가 창 앞을 한 번 지나간다. 발소리는 없다.
    public async Task PassShadow(float seconds, Node timerHost)
    {
        if (_figure == null) return;
        _figure.Visible = true;
        float from = DoorWorld.X + 0.22f, to = DoorWorld.X - 0.22f;
        double t = 0;
        while (t < seconds)
        {
            await timerHost.ToSignal(timerHost.GetTree(), SceneTree.SignalName.ProcessFrame);
            t += timerHost.GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / seconds), 0f, 1f);
            _figure.Position = _figure.Position with { X = Mathf.Lerp(from, to, k) };
        }
        _figure.Visible = false;
        _figure.Position = _figure.Position with { X = DoorWorld.X };
    }

    // 문 손잡이가 한 번 돌아갔다 돌아온다.
    public async Task TurnHandle(Node timerHost)
    {
        if (_handle == null) return;
        var t = CreateTween();
        t.TweenProperty(_handle, "rotation_degrees:z", -15f, 0.35).SetTrans(Tween.TransitionType.Sine);
        t.TweenInterval(0.4);
        t.TweenProperty(_handle, "rotation_degrees:z", 0f, 0.45).SetTrans(Tween.TransitionType.Sine);
        await timerHost.ToSignal(timerHost.GetTree().CreateTimer(1.25), SceneTreeTimer.SignalName.Timeout);
    }

    public void AllOff()
    {
        if (_corridor != null) _corridor.Visible = false;
        if (_figure != null) _figure.Visible = false;
        if (_corridorLight != null) { _corridorLight.Visible = false; _corridorLight.LightEnergy = 0f; }
    }
}
