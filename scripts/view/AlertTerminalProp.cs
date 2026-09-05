using Godot;
using NSP.Core;

namespace NSP.View;

// 책상 위 경고 단말기 — 낡은 연구시설 / 저예산 산업 장비 느낌. 판정 로직은 전혀 없고,
// AlertTerminalView(SENSOR 화면)를 SubViewport로 화면 쿼드에 투사한다(DeskScheduleBoard 방식).
// 위쪽에 경찰차식 회전 경광등: 심각/금기=빨강 고속, 일반 사고=노랑 중속, 평상시=하양 저속,
// 사망자 발생 시=검정(소등). SpotLight를 계속 Y축 회전시켜 천장을 훑는 삥글삥글 효과.
// [Tool] — 형상을 코드로 만들지만 에디터 뷰포트에도 보이게 한다.
[Tool]
public partial class AlertTerminalProp : Node3D, IProjectionSurface
{
    private AlertTerminalView _ui;
    private SubViewport _screenVp;
    private StandardMaterial3D _lamp1Mat, _lamp2Mat;
    private Node3D _beaconPivot;
    private SpotLight3D _beaconLight;
    private StandardMaterial3D _beaconDomeMat, _beaconBulbMat;
    private float _blinkPhase;
    private float _beaconAngle;
    private bool _built;
    private Node3D _attachmentRoot;

    // sencor.glb 화면 맞춤값 (SensorModel 로컬 = glb 단위). 필요하면 여기만 만진다.
    // 화면 비율(560:300)에 맞춘 가로 긴 표시창. SensorModel 의 X 스케일이 커진 만큼
    // 실제 화면은 더 넓어진다 — 몸체를 키우기보다 화면 면적을 넓히는 쪽을 우선한다.
    [Export] public Vector2 ScreenSize = new(0.76f, 0.50f);
    [Export] public Vector3 ScreenOffset = new(0.01f, 0.50f, 0.19f);
    [Export] public float ScreenTiltDeg = -26.6f;
    [Export] public Vector3 BeaconOffset = new(0.16f, 0.87f, -0.18f);
    [Export] public Vector3 LedOffset = new(0.44f, 0.6f, 0.04f);

    public SubViewport TargetViewport => _screenVp;

    // 화면 쿼드에 마우스 레이를 쏴서 센서 화면(SubViewport)의 2D 좌표를 구한다.
    // 화살표 버튼을 실제로 누를 수 있게 하기 위한 것으로, 판정은 화면 안 UI 가 한다.
    public bool TryProjectRay(Vector3 rayOrigin, Vector3 rayDir, bool clamp, out Vector2 canvasPos)
    {
        canvasPos = Vector2.Zero;
        if (_screen == null || _screenVp == null || !IsVisibleInTree()) return false;

        Transform3D inv = _screen.GlobalTransform.AffineInverse();
        Vector3 lo = inv * rayOrigin;
        Vector3 ld = inv.Basis * rayDir;
        if (Mathf.Abs(ld.Z) < 1e-6f) return false;

        float t = -lo.Z / ld.Z;
        if (t < 0f) return false;

        Vector3 hit = lo + ld * t;
        // 쿼드는 로컬 크기가 ScreenSize 이므로 그 크기로 정규화한다.
        float u = hit.X / ScreenSize.X + 0.5f;
        float v = 0.5f - hit.Y / ScreenSize.Y;

        bool inside = u is >= 0f and <= 1f && v is >= 0f and <= 1f;
        if (!inside && !clamp) return false;

        u = Mathf.Clamp(u, 0f, 1f);
        v = Mathf.Clamp(v, 0f, 1f);
        canvasPos = new Vector2(u * _screenVp.Size.X, v * _screenVp.Size.Y);
        return inside || clamp;
    }

    private MeshInstance3D _screen;

    public override void _Ready()
    {
        if (_built) return;
        _built = true;
        // 센서 본체만 축소된 GLB 인스턴스이므로, 화면/LED/경광등도 반드시 같은
        // 로컬 좌표계의 자식으로 넣어야 모델 위에 정확히 붙는다.
        _attachmentRoot = GetNodeOrNull<Node3D>("SensorModel") ?? this;

        // 표시창(SubViewport 투사). 렌더 해상도 = 논리 캔버스 × UiScale (글자도 같은 배율로 확대).
        var logical = new Vector2I(560, 300);
        var vp = new SubViewport
        {
            Size = ControlRoom3DController.ViewportSize(logical),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            Disable3D = true,
            TransparentBg = false,
        };
        _screenVp = vp;
        _attachmentRoot.AddChild(vp);
        _ui = new AlertTerminalView();
        ControlRoom3DController.AddScaledView(vp, _ui, logical);

        _screen = new MeshInstance3D
        {
            // sencor.glb의 기울어진 앞면 = 하나의 평면(메시에서 실측: z ≈ -0.043x - 0.5013y + 0.358,
            // 즉 뒤로 26.6° 젖혀짐). 이 평면 위, CRT 유리 바로 앞에 불투명 경고 화면을 올려
            // 모델에 구워진 초록 글자를 완전히 덮고 AlertSystem의 현재 경고만 보이게 한다.
            // 값 조정: SensorModel 로컬 좌표(글b 단위). 프레임(베젤 z≈0.31)이 가장자리를 가려줌.
            Mesh = new QuadMesh { Size = new Vector2(ScreenSize.X, ScreenSize.Y) },
            Position = ScreenOffset,
            RotationDegrees = new Vector3(ScreenTiltDeg, 0f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoTexture = vp.GetTexture(),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            },
        };
        _attachmentRoot.AddChild(_screen);

        // GLB에 이미 구워진 경광등 돔/LED 위에 '동작하는' 버전을 겹쳐 올린다.
        // (돔 실측 중심 x≈0.16, z≈-0.18, 밑동 y≈0.87 / LED 스택 화면 오른쪽 x≈0.42)
        _lamp1Mat = LampMat(new Color(0.2f, 0.9f, 0.3f));
        _lamp2Mat = LampMat(new Color(0.6f, 0.5f, 0.15f));
        var lamp1 = Lamp(_lamp1Mat, LedOffset);
        var lamp2 = Lamp(_lamp2Mat, LedOffset + new Vector3(0f, -0.1f, 0f));
        lamp1.Scale = RoundFix;
        lamp2.Scale = RoundFix;
        _attachmentRoot.AddChild(lamp1);
        _attachmentRoot.AddChild(lamp2);

        BuildBeacon();
    }

    // 경찰차식 회전 경광등: 받침 + 반투명 돔 + 그 안에서 도는 SpotLight + 발광 전구.
    private void BuildBeacon()
    {
        var baseMat = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f), Roughness = 0.6f };
        var beaconBase = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.085f, BottomRadius = 0.1f, Height = 0.045f, RadialSegments = 16 },
            Position = BeaconOffset,
            // 몸체를 가로로 늘렸기 때문에 그대로 두면 경광등이 타원으로 찌그러진다.
            Scale = RoundFix,
            MaterialOverride = baseMat,
        };
        _attachmentRoot.AddChild(beaconBase);

        _beaconDomeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.9f, 0.9f, 0.9f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = new Color(0.8f, 0.8f, 0.8f),
            EmissionEnergyMultiplier = 0.5f,
            Roughness = 0.2f,
        };
        var dome = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.085f, Height = 0.14f, RadialSegments = 16, Rings = 8, IsHemisphere = true },
            Position = new Vector3(0f, 0.022f, 0f),
            MaterialOverride = _beaconDomeMat,
        };
        beaconBase.AddChild(dome);

        _beaconPivot = new Node3D { Position = new Vector3(0f, 0.04f, 0f) };
        beaconBase.AddChild(_beaconPivot);

        _beaconBulbMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.1f, 0.1f, 0.1f),
            EmissionEnabled = true,
            Emission = new Color(1f, 1f, 1f),
            EmissionEnergyMultiplier = 3f,
        };
        _beaconPivot.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.014f, 0.01f) },
            MaterialOverride = _beaconBulbMat,
        });

        _beaconLight = new SpotLight3D
        {
            Position = new Vector3(0.04f, 0f, 0f),
            RotationDegrees = new Vector3(0f, -90f, 0f), // +X 방향(수평)으로 쏴서 돌 때 벽/천장을 훑는다
            LightColor = new Color(1f, 1f, 1f),
            LightEnergy = 2.0f,
            SpotRange = 6f,
            SpotAngle = 22f,
            SpotAngleAttenuation = 1.2f,
        };
        _beaconPivot.AddChild(_beaconLight);
    }

    // SensorModel 의 비균등 스케일(가로만 확대)을 되돌리는 보정값.
    private Vector3 RoundFix
    {
        get
        {
            var s = _attachmentRoot?.Scale ?? Vector3.One;
            return s.X > 0.001f ? new Vector3(s.Y / s.X, 1f, s.Z / s.X) : Vector3.One;
        }
    }

    private static StandardMaterial3D LampMat(Color c) => new()
    {
        AlbedoColor = new Color(0.04f, 0.04f, 0.04f),
        EmissionEnabled = true,
        Emission = c,
        EmissionEnergyMultiplier = 1.0f,
    };

    private static MeshInstance3D Lamp(StandardMaterial3D mat, Vector3 pos) => new()
    {
        Mesh = new SphereMesh { Radius = 0.035f, Height = 0.07f, RadialSegments = 8, Rings = 5 },
        Position = pos,
        MaterialOverride = mat,
    };

    private enum Beacon { White, Yellow, Red, Black }

    public override void _Process(double delta)
    {
        if (_ui == null) return;

        // 성능: 근무 배치 단계처럼 단말기가 책상에서 치워져 있으면 화면을 그리지 않는다.
        if (_screenVp != null)
        {
            var want = IsVisibleInTree() ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
            if (_screenVp.RenderTargetUpdateMode != want) _screenVp.RenderTargetUpdateMode = want;
        }

        // ── 화면 옆 소형 LED (기존 동작) ──
        var sev = _ui.CurrentSeverity;
        bool failure = _ui.InFailureFlash;
        float freq = failure ? 10f : sev == AlertSeverity.Critical ? 6f : sev == AlertSeverity.Warning ? 2.5f : 0f;
        _blinkPhase += (float)delta * freq;
        float k = freq > 0f ? 0.5f + 0.5f * Mathf.Sin(_blinkPhase) : 1f;
        Color warnCol = failure || sev == AlertSeverity.Critical ? new Color(0.95f, 0.3f, 0.1f)
            : sev == AlertSeverity.Warning ? new Color(0.9f, 0.65f, 0.12f)
            : new Color(0.15f, 0.55f, 0.2f);
        _lamp1Mat.Emission = freq > 0f ? warnCol : new Color(0.15f, 0.6f, 0.22f);
        _lamp1Mat.EmissionEnergyMultiplier = freq > 0f ? 0.6f + 2.6f * k : 0.7f;
        _lamp2Mat.EmissionEnergyMultiplier = freq > 0f ? 0.4f + 2.2f * k : 0.5f;
        _lamp2Mat.Emission = freq > 0f ? warnCol : new Color(0.55f, 0.45f, 0.14f);

        // ── 회전 경광등 ──
        Beacon b = _ui.DeathSeen ? Beacon.Black
            : failure || sev == AlertSeverity.Critical ? Beacon.Red
            : sev == AlertSeverity.Warning ? Beacon.Yellow
            : Beacon.White;

        (Color col, float spin, float energy) = b switch
        {
            Beacon.Red => (new Color(1f, 0.12f, 0.08f), 9.0f, 2.6f),
            Beacon.Yellow => (new Color(1f, 0.7f, 0.1f), 4.5f, 2.0f),
            Beacon.Black => (new Color(0.05f, 0.05f, 0.06f), 0.3f, 0.05f),
            _ => (new Color(0.95f, 0.95f, 1f), 1.4f, 1.1f),
        };

        _beaconAngle += (float)delta * spin;
        if (_beaconPivot != null) _beaconPivot.Rotation = new Vector3(0f, _beaconAngle, 0f);

        if (_beaconLight != null)
        {
            _beaconLight.LightColor = col;
            _beaconLight.LightEnergy = energy;
        }
        if (_beaconBulbMat != null)
        {
            _beaconBulbMat.Emission = col;
            _beaconBulbMat.EmissionEnergyMultiplier = b == Beacon.Black ? 0.1f : 3.5f;
        }
        if (_beaconDomeMat != null)
        {
            _beaconDomeMat.Emission = col;
            _beaconDomeMat.EmissionEnergyMultiplier = b == Beacon.Black ? 0.05f : 0.9f;
            _beaconDomeMat.AlbedoColor = new Color(col.R, col.G, col.B, b == Beacon.Black ? 0.6f : 0.35f);
        }
    }
}
