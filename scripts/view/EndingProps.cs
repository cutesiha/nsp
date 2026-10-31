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

    // ── BAD : 밖에서 문을 친다 → 아무도 없는데 문이 열린다 ─────────────────────
    //
    // 문은 씬에 원래 있는 ControlRoom/Door 를 그대로 움직인다. 손잡이는 문 왼쪽 끝이라
    // 경첩은 **오른쪽 끝**(x = 2.0)이고, 방 안쪽(+Z)으로 열린다 — 그래야 열린 문짝이
    // 오른쪽 벽에 붙어 플레이어 시선에서 문간을 가리지 않는다.
    //
    // 뒷벽은 막힌 판이라 문을 열면 콘크리트가 보인다. 문 뒤에 **빛을 받지 않는 검은 판**을
    // 깔아 둔다 — 닫혀 있을 때는 문짝 속에 묻혀 안 보이고, 열리는 만큼만 드러난다.
    private const float DoorHalfWidth = 0.45f;   // Door_Panel 폭 0.9 의 반
    private Vector3 _doorRest;
    private bool _doorReady;
    private MeshInstance3D _void;
    private float _bang;        // 쾅 한 번이 남긴 떨림(1 → 0)
    private float _openDeg;     // 경첩 각도. + 가 방 안쪽
    private readonly RandomNumberGenerator _rng = new();

    // 돌아보기 전에 불러 둔다 — 문틀이 먼저 서 있어야 붉은 벽 속에서 문이 문으로 읽힌다.
    public void PrepareDoor()
    {
        if (_doorReady || _door == null) return;
        _doorReady = true;
        _doorRest = _door.Position;
        _rng.Randomize();
        // 손잡이를 문짝에 붙인다 — 두드릴 때 같이 떨고, 열릴 때 같이 돈다.
        _handle?.Reparent(_door, true);
        _void = new MeshInstance3D
        {
            Name = "EndingDoorVoid",
            Mesh = new QuadMesh { Size = new Vector2(DoorHalfWidth * 2f, 2.0f) },
            // 벽 앞면(z = -2.55)보다 5mm 앞, 닫힌 문짝(-2.575 ~ -2.525) 속.
            Position = new Vector3(DoorWorld.X, 1.0f, -2.545f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Colors.Black,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_void);

        // 문틀 — 붉은 조명 아래에서는 철문과 콘크리트 벽이 같은 색이 되어 문이 벽에 묻힌다.
        // 어두운 테두리 셋(양옆 · 위)을 둘러 문간의 윤곽을 세운다.
        var frameMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.055f), Metallic = 0.6f, Roughness = 0.5f,
        };
        foreach (var (pos, size) in new[]
                 {
                     (new Vector3(DoorWorld.X - DoorHalfWidth - 0.035f, 1.02f, -2.52f), new Vector3(0.07f, 2.04f, 0.07f)),
                     (new Vector3(DoorWorld.X + DoorHalfWidth + 0.04f, 1.02f, -2.52f), new Vector3(0.07f, 2.04f, 0.07f)),
                     (new Vector3(DoorWorld.X, 2.04f, -2.52f), new Vector3(DoorHalfWidth * 2f + 0.15f, 0.07f, 0.07f)),
                 })
        {
            AddChild(new MeshInstance3D
            {
                Name = "EndingDoorFrame",
                Mesh = new BoxMesh { Size = size },
                Position = pos,
                MaterialOverride = frameMat,
            });
        }
    }

    // 밖에서 한 번 쾅. strength 1 이 가장 세다 — 문짝이 방 쪽으로 튀었다 문틀에 되박히며 잘게 떤다.
    public void BangDoor(float strength)
    {
        PrepareDoor();
        _bang = Mathf.Max(_bang, strength);
    }

    // 경첩 각도를 seconds 동안 그 값으로. 처음엔 느리고 끝에서 더 느려진다(무게 있는 철문).
    public async Task SwingDoor(float toDegrees, float seconds, Node timerHost)
    {
        PrepareDoor();
        float from = _openDeg;
        double t = 0;
        while (t < seconds)
        {
            await timerHost.ToSignal(timerHost.GetTree(), SceneTree.SignalName.ProcessFrame);
            t += timerHost.GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / seconds), 0f, 1f);
            float e = k * k * (3f - 2f * k);
            // 끼이익 — 매끈하게 돌지 않고 아주 조금씩 걸린다.
            _openDeg = Mathf.Lerp(from, toDegrees, e) + Mathf.Sin(k * 31f) * 0.35f * (1f - k);
        }
        _openDeg = toDegrees;
    }

    public override void _Process(double delta)
    {
        if (!_doorReady || _door == null || !IsInstanceValid(_door)) return;
        _bang = Mathf.Max(0f, _bang - (float)delta * 4.2f);
        float b = _bang * _bang;
        float th = Mathf.DegToRad(_openDeg + _rng.RandfRange(-0.5f, 0.5f) * b);
        // 오른쪽 끝(경첩)을 축으로 돌린다. 축이 제자리에 남도록 문 중심을 옮긴다.
        _door.Rotation = new Vector3(_rng.RandfRange(-0.25f, 0.25f) * b * 0.017f, th, 0f);
        _door.Position = _doorRest
                         + new Vector3(DoorHalfWidth - DoorHalfWidth * Mathf.Cos(th), 0f, DoorHalfWidth * Mathf.Sin(th))
                         + new Vector3(_rng.RandfRange(-1f, 1f) * 0.006f, _rng.RandfRange(-1f, 1f) * 0.003f, 0.032f) * b;
    }
}
