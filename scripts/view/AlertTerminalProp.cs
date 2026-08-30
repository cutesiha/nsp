using Godot;
using NSP.Core;

namespace NSP.View;

// 책상 위 경고 단말기 — 낡은 연구시설 / 저예산 산업 장비 느낌. 판정 로직은 전혀 없고,
// AlertTerminalView(SENSOR 화면)를 SubViewport로 화면 쿼드에 투사한다(DeskScheduleBoard 방식).
// 위쪽에 경찰차식 회전 경광등: 심각/금기=빨강 고속, 일반 사고=노랑 중속, 평상시=하양 저속,
// 사망자 발생 시=검정(소등). SpotLight를 계속 Y축 회전시켜 천장을 훑는 삥글삥글 효과.
// [Tool] — 형상을 코드로 만들지만 에디터 뷰포트에도 보이게 한다.
[Tool]
public partial class AlertTerminalProp : Node3D
{
    private AlertTerminalView _ui;
    private StandardMaterial3D _lamp1Mat, _lamp2Mat;
    private Node3D _beaconPivot;
    private SpotLight3D _beaconLight;
    private StandardMaterial3D _beaconDomeMat, _beaconBulbMat;
    private float _blinkPhase;
    private float _beaconAngle;

    public override void _Ready()
    {
        if (GetChildCount() > 0) return; // 스크립트 리로드 시 중복 생성 방지

        var caseMat = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.40f, 0.33f), Roughness = 0.85f, Metallic = 0.1f };
        var darkMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.11f), Roughness = 0.7f };

        // 본체 — 살짝 뒤로 기운 상자 + 앞쪽 경사 패널. (기존보다 1.4배 정도 큼)
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.27f, 0.16f, 0.18f) },
            Position = new Vector3(0f, 0.08f, -0.01f),
            MaterialOverride = caseMat,
        };
        AddChild(body);

        var facePlate = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.26f, 0.145f, 0.025f) },
            Position = new Vector3(0f, 0.105f, 0.078f),
            RotationDegrees = new Vector3(-22f, 0f, 0f),
            MaterialOverride = darkMat,
        };
        AddChild(facePlate);

        // 표시창(SubViewport 투사).
        var vp = new SubViewport
        {
            Size = new Vector2I(320, 220),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            Disable3D = true,
            TransparentBg = false,
        };
        AddChild(vp);
        _ui = new AlertTerminalView();
        vp.AddChild(_ui);

        var screen = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.19f, 0.11f) },
            Position = new Vector3(-0.022f, 0.115f, 0.093f),
            RotationDegrees = new Vector3(-22f, 0f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoTexture = vp.GetTexture(),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            },
        };
        AddChild(screen);

        // 스피커 그릴.
        var grille = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.04f, 0.07f, 0.01f) },
            Position = new Vector3(0.098f, 0.105f, 0.083f),
            RotationDegrees = new Vector3(-22f, 0f, 0f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Roughness = 0.9f },
        };
        AddChild(grille);
        for (int i = 0; i < 5; i++)
            grille.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.034f, 0.005f, 0.008f) },
                Position = new Vector3(0f, -0.026f + i * 0.013f, 0.004f),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f) },
            });

        // 상태 LED 2개(작은 표시등).
        _lamp1Mat = LampMat(new Color(0.2f, 0.9f, 0.3f));
        _lamp2Mat = LampMat(new Color(0.6f, 0.5f, 0.15f));
        AddChild(Lamp(_lamp1Mat, new Vector3(-0.1f, 0.165f, 0.07f)));
        AddChild(Lamp(_lamp2Mat, new Vector3(-0.07f, 0.165f, 0.07f)));

        BuildBeacon();
    }

    // 경찰차식 회전 경광등: 받침 + 반투명 돔 + 그 안에서 도는 SpotLight + 발광 전구.
    private void BuildBeacon()
    {
        var baseMat = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.1f), Roughness = 0.6f };
        var beaconBase = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.028f, BottomRadius = 0.032f, Height = 0.014f, RadialSegments = 16 },
            Position = new Vector3(-0.045f, 0.175f, -0.01f),
            MaterialOverride = baseMat,
        };
        AddChild(beaconBase);

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
            Mesh = new SphereMesh { Radius = 0.026f, Height = 0.044f, RadialSegments = 16, Rings = 8, IsHemisphere = true },
            Position = new Vector3(0f, 0.007f, 0f),
            MaterialOverride = _beaconDomeMat,
        };
        beaconBase.AddChild(dome);

        _beaconPivot = new Node3D { Position = new Vector3(0f, 0.012f, 0f) };
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
            Position = new Vector3(0.012f, 0f, 0f),
            RotationDegrees = new Vector3(0f, -90f, 0f), // +X 방향(수평)으로 쏴서 돌 때 벽/천장을 훑는다
            LightColor = new Color(1f, 1f, 1f),
            LightEnergy = 2.0f,
            SpotRange = 6f,
            SpotAngle = 22f,
            SpotAngleAttenuation = 1.2f,
        };
        _beaconPivot.AddChild(_beaconLight);
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
        Mesh = new SphereMesh { Radius = 0.011f, Height = 0.022f, RadialSegments = 8, Rings = 5 },
        Position = pos,
        MaterialOverride = mat,
    };

    private enum Beacon { White, Yellow, Red, Black }

    public override void _Process(double delta)
    {
        if (_ui == null) return;

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
