using System.Threading.Tasks;
using Godot;

namespace NSP.View;

// 트루엔딩 전용 3D 무대.
//
// 엔딩의 뒷부분은 중앙제어실이 아니라 **다른 장소들**에서 벌어진다 — 처분실, 거대한
// 봉쇄 코어실, 복구되는 시설 복도. 그 장소들을 중앙제어실 3D 안에 지으면 서로 섞이고,
// 엔딩이 끝난 뒤에도 남는다. 그래서 자체 World3D 를 가진 SubViewport 를 하나 세우고
// 그 안에만 짓는다 — 엔딩이 끝나면 뷰포트째로 사라진다(FacilityCctvWorld 와 같은 방식).
//
// 화면에는 그 뷰포트 텍스처를 **전체 화면**으로 깐다(Screen). 중앙제어실로 돌아올 때는
// 이 텍스처를 페이드아웃하면 그 아래 원래 3D 가 그대로 드러난다.
//
// 이 클래스는 '무대'만 책임진다 — 무엇을 언제 보여 줄지는 EndingDirector.True 가 정한다.
// gameplay 맵이 아니므로 충돌체도, 내비게이션도, 게임 로직도 두지 않는다(문서 §29).
public partial class EndingCutsceneStage : Node
{
    public enum Set { None, Restraint, CoreHall, Corridor }

    // 엔딩 컷씬은 중앙제어실 CRT 가 아니라 화면 전체에 깔린다 — 해상도를 그만큼 쓴다.
    private static readonly Vector2I ViewportSize = new(1280, 720);

    private SubViewport _vp;
    private Node3D _world;
    private Camera3D _cam;
    private Vector3 _camLook;

    private Node3D _setRestraint, _setCore, _setCorridor;

    public EndingOffender Offender { get; private set; }

    // 화면 전체를 덮는 컷씬 그림. 엔딩 연출기가 이 알파를 여닫는다.
    public TextureRect Screen { get; private set; }

    // ── 세우기 ───────────────────────────────────────────────────────────

    public void Attach(CanvasLayer layer)
    {
        _vp = new SubViewport
        {
            Size = ViewportSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            OwnWorld3D = true,
            Disable3D = false,
            GuiDisableInput = true,
            TransparentBg = false,
        };
        AddChild(_vp);

        _world = new Node3D { Name = "EndingStageWorld" };
        _vp.AddChild(_world);

        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Black,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.10f, 0.10f, 0.12f),
                AmbientLightEnergy = 0.35f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic,
                GlowEnabled = true,
                GlowIntensity = 0.5f,
                GlowBloom = 0.12f,
            },
        };
        _world.AddChild(env);

        _cam = new Camera3D { Fov = 55f, Near = 0.05f, Far = 200f };
        _world.AddChild(_cam);

        BuildRestraintRoom();
        BuildCoreHall();
        BuildCorridor();
        ShowSet(Set.None);

        // 화면 전체. 처음에는 투명하다 — 연출기가 필요할 때 띄운다.
        Screen = new TextureRect
        {
            Texture = _vp.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        Screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(Screen);
        // **맨 아래로 내린다.** 엔딩의 암전 · 섬광 · 배너는 이 CanvasLayer 에 먼저 붙어
        // 있는데, 나중에 붙은 컷씬 화면이 그 위에 올라가면 암전이 먹지 않는다.
        layer.MoveChild(Screen, 0);
    }

    public void ShowSet(Set set)
    {
        if (_setRestraint != null) _setRestraint.Visible = set == Set.Restraint;
        if (_setCore != null) _setCore.Visible = set == Set.CoreHall;
        if (_setCorridor != null) _setCorridor.Visible = set == Set.Corridor;
        // 아무것도 안 띄울 때는 렌더를 멈춘다 — 엔딩 내내 3D 패스를 하나 더 돌릴 이유가 없다.
        if (_vp != null)
            _vp.RenderTargetUpdateMode = set == Set.None
                ? SubViewport.UpdateMode.Disabled : SubViewport.UpdateMode.Always;
    }

    // ── 카메라 ───────────────────────────────────────────────────────────

    public void SetCam(Vector3 pos, Vector3 look, float fov = 0f)
    {
        if (_cam == null) return;
        if (fov > 0f) _cam.Fov = fov;
        _cam.Position = pos;
        _camLook = look;
        _cam.LookAt(look, Vector3.Up);
    }

    // 카메라가 **실제로 이동**한다. FOV 줌으로 때우지 않는다(문서 §8).
    // 바라보는 점도 같이 옮겨 가므로 매 프레임 LookAt 을 다시 건다.
    public async Task MoveCam(Vector3 to, Vector3 look, double seconds, float toFov = 0f)
    {
        if (_cam == null) return;
        Vector3 from = _cam.Position;
        Vector3 lookFrom = _camLook;
        float fovFrom = _cam.Fov, fovTo = toFov > 0f ? toFov : _cam.Fov;
        double t = 0;
        while (t < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / seconds), 0f, 1f);
            // 가속 · 감속이 있어야 "밀려 나는" 느낌이 난다. 등속은 기계가 움직이는 것처럼 보인다.
            float e = k < 0.5f ? 2f * k * k : 1f - Mathf.Pow(-2f * k + 2f, 2f) / 2f;
            _cam.Position = from.Lerp(to, e);
            _camLook = lookFrom.Lerp(look, e);
            _cam.Fov = Mathf.Lerp(fovFrom, fovTo, e);
            _cam.LookAt(_camLook, Vector3.Up);
        }
        SetCam(to, look);
    }

    // ── 재질 ────────────────────────────────────────────────────────────

    private static StandardMaterial3D Mat(Color albedo, float rough = 0.85f, float metal = 0f) => new()
    {
        AlbedoColor = albedo, Roughness = rough, Metallic = metal,
    };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, string name = "")
    {
        var m = new MeshInstance3D
        {
            Name = string.IsNullOrEmpty(name) ? "Box" : name,
            Mesh = new BoxMesh { Size = size },
            Position = pos,
            MaterialOverride = mat,
        };
        parent.AddChild(m);
        return m;
    }

    private static MeshInstance3D Cyl(Node3D parent, float radius, float height, Vector3 pos, Material mat,
        string name = "")
    {
        var m = new MeshInstance3D
        {
            Name = string.IsNullOrEmpty(name) ? "Cyl" : name,
            Mesh = new CylinderMesh
            {
                TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 12,
            },
            Position = pos,
            MaterialOverride = mat,
        };
        parent.AddChild(m);
        return m;
    }

    // ── ① 처분실 ────────────────────────────────────────────────────────
    //
    // 격리실 맵을 그대로 쓰지 않는다(§3). 거의 아무것도 없는 빈 방이고, 붉은 조명만
    // 중앙을 비춘다 — 주변은 거의 보이지 않아야 가운데의 결번자에게만 눈이 간다.
    private void BuildRestraintRoom()
    {
        _setRestraint = new Node3D { Name = "SetRestraint" };
        _world.AddChild(_setRestraint);

        var concrete = Mat(new Color(0.16f, 0.15f, 0.15f), 0.95f);
        var wall = Mat(new Color(0.11f, 0.105f, 0.11f), 0.95f);
        var steel = Mat(new Color(0.26f, 0.27f, 0.29f), 0.35f, 0.85f);

        Box(_setRestraint, new Vector3(9f, 0.2f, 11f), new Vector3(0f, -0.1f, 0f), concrete, "Floor");
        Box(_setRestraint, new Vector3(9f, 4.2f, 0.2f), new Vector3(0f, 2.1f, -3.4f), wall, "WallBack");
        Box(_setRestraint, new Vector3(0.2f, 4.2f, 11f), new Vector3(-4.4f, 2.1f, 0f), wall, "WallL");
        Box(_setRestraint, new Vector3(0.2f, 4.2f, 11f), new Vector3(4.4f, 2.1f, 0f), wall, "WallR");
        Box(_setRestraint, new Vector3(9f, 0.2f, 11f), new Vector3(0f, 4.2f, 0f), wall, "Ceiling");

        // 붉은 조명 둘 — 중앙만 떨어뜨린다.
        _setRestraint.AddChild(new SpotLight3D
        {
            Name = "KeyRed",
            Position = new Vector3(0f, 3.7f, 0.4f),
            RotationDegrees = new Vector3(-90f, 0f, 0f),
            LightColor = new Color(1f, 0.22f, 0.16f),
            LightEnergy = 2.3f,
            SpotRange = 9f,
            SpotAngle = 32f,
            SpotAngleAttenuation = 1.4f,
            ShadowEnabled = true,
        });
        _setRestraint.AddChild(new OmniLight3D
        {
            Name = "RimRed",
            Position = new Vector3(0f, 1.3f, -2.2f),
            LightColor = new Color(0.95f, 0.16f, 0.12f),
            LightEnergy = 0.7f,
            OmniRange = 5.5f,
            ShadowEnabled = false,
        });

        // 철창 — 카메라가 **그 사이로** 빠져나간다. 그래서 x = 0 에 틈을 둔다(§8).
        var bars = new Node3D { Name = "Bars", Position = new Vector3(0f, 0f, 3.2f) };
        _setRestraint.AddChild(bars);
        for (int i = -5; i <= 5; i++)
        {
            if (i == 0) continue;                       // 가운데 틈 — 카메라가 지나는 자리
            float x = Mathf.Sign(i) * (0.30f + (Mathf.Abs(i) - 1) * 0.46f);
            Cyl(bars, 0.045f, 4.0f, new Vector3(x, 2.0f, 0f), steel, $"Bar{i}");
        }
        Box(bars, new Vector3(9f, 0.12f, 0.12f), new Vector3(0f, 3.95f, 0f), steel, "BarTop");
        Box(bars, new Vector3(9f, 0.12f, 0.12f), new Vector3(0f, 0.06f, 0f), steel, "BarBottom");

        // 결번자 — 팔을 묶인 채 중앙에.
        Offender = new EndingOffender();
        _setRestraint.AddChild(Offender);
    }

    // ── ② 거대한 봉쇄 코어실 ─────────────────────────────────────────────
    //
    // gameplay 코어실(작업실)을 쓰지 않는다 — 그 방은 작업실 크기라 "거대함" 이 없다(§10).
    // 여기서 중요한 것은 디테일이 아니라 **스케일**이다.
    private void BuildCoreHall()
    {
        _setCore = new Node3D { Name = "SetCoreHall" };
        _world.AddChild(_setCore);

        var floor = Mat(new Color(0.21f, 0.21f, 0.22f), 0.7f, 0.1f);
        var wall = Mat(new Color(0.17f, 0.175f, 0.19f), 0.8f);
        var steel = Mat(new Color(0.34f, 0.35f, 0.38f), 0.35f, 0.9f);
        var dark = Mat(new Color(0.12f, 0.12f, 0.14f), 0.6f, 0.5f);

        Box(_setCore, new Vector3(34f, 0.4f, 40f), new Vector3(0f, -0.2f, 0f), floor, "Floor");
        Box(_setCore, new Vector3(34f, 22f, 0.5f), new Vector3(0f, 11f, -16f), wall, "WallBack");
        Box(_setCore, new Vector3(0.5f, 22f, 40f), new Vector3(-17f, 11f, 0f), wall, "WallL");
        Box(_setCore, new Vector3(0.5f, 22f, 40f), new Vector3(17f, 11f, 0f), wall, "WallR");
        Box(_setCore, new Vector3(34f, 0.5f, 40f), new Vector3(0f, 22f, 0f), wall, "Ceiling");

        // 벽면 세로 홈 — 스케일을 읽게 하는 유일한 장치다(비교 대상이 없으면 크기를 못 느낀다).
        for (int i = -5; i <= 5; i++)
        {
            Box(_setCore, new Vector3(0.4f, 20f, 0.4f), new Vector3(i * 3f, 10f, -15.6f), dark);
            Box(_setCore, new Vector3(0.4f, 20f, 0.4f), new Vector3(-16.6f, 10f, i * 3.4f), dark);
            Box(_setCore, new Vector3(0.4f, 20f, 0.4f), new Vector3(16.6f, 10f, i * 3.4f), dark);
        }

        // 받침대 · 계단.
        Box(_setCore, new Vector3(12f, 0.5f, 12f), new Vector3(0f, 0.25f, -3f), steel, "Base");
        Box(_setCore, new Vector3(8f, 0.5f, 8f), new Vector3(0f, 0.75f, -3f), steel, "Base2");
        Box(_setCore, new Vector3(5f, 0.5f, 5f), new Vector3(0f, 1.25f, -3f), steel, "Base3");

        // 코어 본체 — 아주 크게. 처음에는 꺼져 있다.
        _coreMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.62f, 0.70f),
            EmissionEnabled = true,
            Emission = new Color(0.72f, 0.85f, 1f),
            EmissionEnergyMultiplier = 0f,
            Roughness = 0.25f,
            Metallic = 0.1f,
        };
        _core = new MeshInstance3D
        {
            Name = "Core",
            Mesh = new SphereMesh { Radius = 3.2f, Height = 6.4f, RadialSegments = 32, Rings = 20 },
            Position = new Vector3(0f, 8.2f, -3f),
            MaterialOverride = _coreMat,
        };
        _setCore.AddChild(_core);

        _coreLight = new OmniLight3D
        {
            Name = "CoreLight",
            Position = _core.Position,
            LightColor = new Color(0.72f, 0.86f, 1f),
            LightEnergy = 0f,
            OmniRange = 34f,
            ShadowEnabled = false,
        };
        _setCore.AddChild(_coreLight);

        // 코어를 감싸는 링 셋 — 강화 연출에서 제자리로 정렬된다.
        _ringMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.40f, 0.42f, 0.46f),
            Roughness = 0.3f, Metallic = 0.9f,
            EmissionEnabled = true, Emission = new Color(0.5f, 0.78f, 1f), EmissionEnergyMultiplier = 0f,
        };
        for (int i = 0; i < 3; i++)
        {
            var ring = new MeshInstance3D
            {
                Name = $"Ring{i}",
                Mesh = new TorusMesh { InnerRadius = 4.0f + i * 0.5f, OuterRadius = 4.35f + i * 0.5f, Rings = 40 },
                Position = _core.Position,
                MaterialOverride = _ringMat,
            };
            _setCore.AddChild(ring);
            _rings[i] = ring;
        }

        // 위아래 고정 장치 — 강화 때 코어 쪽으로 물린다.
        _clampTop = Box(_setCore, new Vector3(3.2f, 1.6f, 3.2f), new Vector3(0f, 14.6f, -3f), steel, "ClampTop");
        _clampBottom = Box(_setCore, new Vector3(3.2f, 1.6f, 3.2f), new Vector3(0f, 1.6f, -3f), steel, "ClampBottom");

        // 지지 기둥 넷(사선).
        foreach (var (x, z) in new[] { (-4.6f, 1.2f), (4.6f, 1.2f), (-4.6f, -7.2f), (4.6f, -7.2f) })
        {
            var pillar = Box(_setCore, new Vector3(0.6f, 15f, 0.6f), new Vector3(x, 7.5f, z), steel);
            pillar.RotationDegrees = new Vector3(z > -3f ? -7f : 7f, 0f, x < 0f ? 7f : -7f);
        }

        // 난간 — 사람 키 높이의 기준점.
        for (int i = -4; i <= 4; i++)
            Cyl(_setCore, 0.05f, 1.1f, new Vector3(i * 1.7f, 0.55f, 5.2f), steel);
        Box(_setCore, new Vector3(14f, 0.08f, 0.08f), new Vector3(0f, 1.1f, 5.2f), steel, "Rail");

        // 바닥 둘레 조명 — 강화 연출에서 하나씩 켜진다.
        for (int i = 0; i < 10; i++)
        {
            double a = Mathf.Tau * i / 10.0;
            var pos = new Vector3(Mathf.Cos((float)a) * 7.4f, 0.06f, -3f + Mathf.Sin((float)a) * 7.4f);
            var mat = Glow(new Color(0.35f, 0.62f, 0.85f), 0f);
            _floorLampMats[i] = mat;
            Box(_setCore, new Vector3(0.9f, 0.12f, 0.9f), pos, mat, $"FloorLamp{i}");
        }

        // 천장 보조광 — 코어가 꺼져 있을 때 공간 윤곽만 겨우 보이게.
        _setCore.AddChild(new OmniLight3D
        {
            Name = "HallFill",
            Position = new Vector3(0f, 17f, 2f),
            LightColor = new Color(0.55f, 0.62f, 0.75f),
            LightEnergy = 0.9f,
            OmniRange = 40f,
            ShadowEnabled = false,
        });

        // 구(球)를 붙잡고 있는 설비 — 받침 · 구속 프레임 · 냉각 · 배선 · 입자.
        BuildCoreRig();
    }

    private MeshInstance3D _core, _clampTop, _clampBottom;
    private StandardMaterial3D _coreMat, _ringMat;
    private OmniLight3D _coreLight;
    private readonly MeshInstance3D[] _rings = new MeshInstance3D[3];
    private readonly StandardMaterial3D[] _floorLampMats = new StandardMaterial3D[10];

    // ── ③ 복도 · 설비(몽타주) ───────────────────────────────────────────
    //
    // 세 컷을 한 공간에 몰아 짓는다 — 격벽, 문 조명이 줄줄이 켜지는 복도,
    // 꺼져 있던 벽 패널, 그리고 작업 기계. 카메라만 옮겨 가며 찍는다.
    private void BuildCorridor()
    {
        _setCorridor = new Node3D { Name = "SetCorridor" };
        _world.AddChild(_setCorridor);

        var floor = Mat(new Color(0.19f, 0.19f, 0.20f), 0.8f);
        var wall = Mat(new Color(0.165f, 0.17f, 0.18f), 0.85f);
        var steel = Mat(new Color(0.32f, 0.33f, 0.36f), 0.35f, 0.85f);
        var dark = Mat(new Color(0.09f, 0.09f, 0.10f), 0.7f, 0.3f);

        Box(_setCorridor, new Vector3(5f, 0.3f, 34f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_setCorridor, new Vector3(0.3f, 4f, 34f), new Vector3(-2.5f, 2f, 0f), wall, "WallL");
        Box(_setCorridor, new Vector3(0.3f, 4f, 34f), new Vector3(2.5f, 2f, 0f), wall, "WallR");
        Box(_setCorridor, new Vector3(5f, 0.3f, 34f), new Vector3(0f, 4f, 0f), wall, "Ceiling");

        // 복도 문 넷 — 각 문 위에 작은 표시등. 몽타주 B 에서 **하나씩** 켜진다(§14).
        for (int i = 0; i < 4; i++)
        {
            float z = -3f - i * 5.2f;
            Box(_setCorridor, new Vector3(0.12f, 2.4f, 1.6f), new Vector3(-2.33f, 1.2f, z), dark, $"Door{i}");
            Box(_setCorridor, new Vector3(0.1f, 0.1f, 1.7f), new Vector3(-2.3f, 2.5f, z), steel);
            var mat = Glow(new Color(0.35f, 1f, 0.55f), 0f);
            _doorLampMats[i] = mat;
            Box(_setCorridor, new Vector3(0.08f, 0.16f, 0.9f), new Vector3(-2.28f, 2.5f, z), mat, $"DoorLamp{i}");
            var lamp = new OmniLight3D
            {
                Name = $"DoorLight{i}",
                Position = new Vector3(-1.9f, 2.4f, z),
                LightColor = new Color(0.45f, 1f, 0.62f),
                LightEnergy = 0f,
                OmniRange = 4.6f,
                ShadowEnabled = false,
            };
            _setCorridor.AddChild(lamp);
            _doorLights[i] = lamp;
        }

        // 격벽 — 복도 끝. 몽타주 A 에서 실제로 **내려와 닫힌다**(§13).
        _bulkhead = Box(_setCorridor, new Vector3(5.2f, 4.4f, 0.4f), new Vector3(0f, 6.6f, -24f), steel, "Bulkhead");
        for (int i = -2; i <= 2; i++)
            Box(_bulkhead, new Vector3(0.25f, 4.2f, 0.1f), new Vector3(i * 1.0f, 0f, 0.26f), dark);
        _bulkheadLockMat = Glow(new Color(0.35f, 1f, 0.55f), 0f);
        Box(_setCorridor, new Vector3(0.5f, 0.28f, 0.14f), new Vector3(2.0f, 2.3f, -23.7f), _bulkheadLockMat, "Lock");

        // 벽 패널 — 몽타주 C. 꺼져 있다가 부팅되고, 그 화면 안에 GUIDE-0 이 뜬다(§15).
        var panelFrame = Box(_setCorridor, new Vector3(0.14f, 1.5f, 2.2f), new Vector3(2.4f, 1.9f, 4f), steel, "Panel");
        _panelMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.Black,
            EmissionEnabled = true, Emission = new Color(0.2f, 0.9f, 0.95f), EmissionEnergyMultiplier = 0f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _panelScreen = new MeshInstance3D
        {
            Name = "PanelScreen",
            Mesh = new QuadMesh { Size = new Vector2(1.9f, 1.25f) },
            Position = new Vector3(-0.08f, 0f, 0f),
            RotationDegrees = new Vector3(0f, -90f, 0f),
            MaterialOverride = _panelMat,
        };
        panelFrame.AddChild(_panelScreen);

        // 그 화면 안에 뜰 GUIDE-0 얼굴. 패널이 켜지기 전에는 숨어 있다.
        // 초상 그림은 흰 실루엣이다 — 그대로 쓰면 하얀 덩어리로 보인다.
        // 패널 CRT 의 인광색으로 물들여야 "화면 안에 GUIDE-0 이 떴다"로 읽힌다.
        _panelFaceMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.34f, 0.92f, 0.86f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true, Emission = new Color(0.34f, 0.92f, 0.86f),
            EmissionEnergyMultiplier = 0.9f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _panelFace = new MeshInstance3D
        {
            Name = "PanelFace",
            Mesh = new QuadMesh { Size = new Vector2(1.0f, 1.0f) },
            Position = new Vector3(-0.1f, 0.03f, 0f),
            RotationDegrees = new Vector3(0f, -90f, 0f),
            MaterialOverride = _panelFaceMat,
            Visible = false,
        };
        panelFrame.AddChild(_panelFace);

        _panelLight = new OmniLight3D
        {
            Name = "PanelLight",
            Position = new Vector3(1.6f, 1.9f, 4f),
            LightColor = new Color(0.4f, 0.9f, 1f),
            LightEnergy = 0f,
            OmniRange = 4f,
            ShadowEnabled = false,
        };
        _setCorridor.AddChild(_panelLight);

        // 작업 기계 — 몽타주 D. 멈춰 있다가 덜컹거리며 돌아간다(§16).
        _machine = new Node3D { Name = "Machine", Position = new Vector3(-0.2f, 0f, 11f) };
        _setCorridor.AddChild(_machine);
        Box(_machine, new Vector3(2.4f, 1.5f, 1.6f), new Vector3(0f, 0.75f, 0f), steel, "Body");
        Box(_machine, new Vector3(2.0f, 0.18f, 1.3f), new Vector3(0f, 1.6f, 0f), dark, "Top");
        _machineArm = Box(_machine, new Vector3(0.3f, 1.1f, 0.3f), new Vector3(0.8f, 2.1f, 0f), steel, "Arm");
        _machineLampMat = Glow(new Color(1f, 0.55f, 0.2f), 0f);
        Box(_machine, new Vector3(0.22f, 0.22f, 0.06f), new Vector3(-0.8f, 1.2f, 0.82f), _machineLampMat, "Lamp");
        // 기계 **옆**에 둔다. 복도 앞쪽에 두면 기계는 어둠 속에 남고 램프만 보인다.
        _machineLight = new OmniLight3D
        {
            Name = "MachineLight",
            Position = new Vector3(1.3f, 2.3f, 12.2f),
            LightColor = new Color(1f, 0.74f, 0.45f),
            LightEnergy = 0f,
            OmniRange = 7.5f,
            ShadowEnabled = false,
        };
        _setCorridor.AddChild(_machineLight);

        // 복도 기본광 — 아주 약하게(아직 복구 전이다). 복도가 24m 라 하나로는 끝까지
        // 닿지 않는다. 격벽 쪽에 하나 더 둔다 — 안 그러면 닫히는 문이 보이지도 않는다.
        foreach (var (z, e) in new[] { (2f, 0.55f), (-12f, 0.6f), (-20.5f, 1.6f) })
            _setCorridor.AddChild(new OmniLight3D
            {
                Name = $"CorridorFill{z:0}",
                Position = new Vector3(0f, 3.4f, z),
                LightColor = new Color(0.5f, 0.56f, 0.66f),
                LightEnergy = e,
                OmniRange = 16f,
                ShadowEnabled = false,
            });
    }

    private MeshInstance3D _bulkhead, _panelScreen, _panelFace, _machineArm;
    private Node3D _machine;
    private StandardMaterial3D _panelMat, _panelFaceMat, _bulkheadLockMat, _machineLampMat;
    private OmniLight3D _panelLight, _machineLight;
    private readonly OmniLight3D[] _doorLights = new OmniLight3D[4];
    private readonly StandardMaterial3D[] _doorLampMats = new StandardMaterial3D[4];

    // ── 코어실 연출 ──────────────────────────────────────────────────────

    public void CoreReset()
    {
        CoreAura(0f);
        _coreMat.EmissionEnergyMultiplier = 0.08f;
        _ringMat.EmissionEnergyMultiplier = 0f;
        if (_coreLight != null) _coreLight.LightEnergy = 0.2f;
        foreach (var m in _floorLampMats) if (m != null) m.EmissionEnergyMultiplier = 0f;
        // 링은 제각각 비뚤어진 채로 멈춰 있다 — 정렬되는 것이 '강화'의 첫 신호다.
        if (_rings[0] != null) _rings[0].RotationDegrees = new Vector3(74f, 18f, 0f);
        if (_rings[1] != null) _rings[1].RotationDegrees = new Vector3(18f, 62f, 40f);
        if (_rings[2] != null) _rings[2].RotationDegrees = new Vector3(-48f, 10f, 70f);
        if (_clampTop != null) _clampTop.Position = _clampTop.Position with { Y = 16.4f };
        if (_clampBottom != null) _clampBottom.Position = _clampBottom.Position with { Y = 0.2f };
    }

    // 바닥 조명이 하나씩 켜진다.
    public void CoreFloorLamp(int index, float energy = 2.2f)
    {
        if (index < 0 || index >= _floorLampMats.Length) return;
        _floorLampMats[index].EmissionEnergyMultiplier = energy;
    }

    // 링이 제자리로 정렬된다.
    public void CoreAlignRings(double seconds)
    {
        Vector3[] target = { new(90f, 0f, 0f), new(0f, 0f, 90f), new(0f, 90f, 0f) };
        var t = CreateTween().SetParallel(true);
        for (int i = 0; i < _rings.Length; i++)
        {
            if (_rings[i] == null) continue;
            t.TweenProperty(_rings[i], "rotation_degrees", target[i], seconds)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        }
    }

    // 외곽 금속 구조가 잠금 위치로 내려오고 올라온다.
    public void CoreClamp(double seconds)
    {
        var t = CreateTween().SetParallel(true);
        if (_clampTop != null)
            t.TweenProperty(_clampTop, "position:y", 12.0f, seconds).SetTrans(Tween.TransitionType.Expo)
                .SetEase(Tween.EaseType.In);
        if (_clampBottom != null)
            t.TweenProperty(_clampBottom, "position:y", 4.4f, seconds).SetTrans(Tween.TransitionType.Expo)
                .SetEase(Tween.EaseType.In);
    }

    // 코어 빛이 서서히 강해진다.
    public void CoreCharge(float to, double seconds)
    {
        var t = CreateTween().SetParallel(true);
        // 설비도 같이 살아난다 — 구만 밝아지면 "흰 구체" 로 돌아간다.
        t.TweenMethod(Callable.From<float>(CoreAura), _coreAura, Mathf.Clamp(to / 1.6f, 0f, 1.35f), seconds);
        t.TweenProperty(_coreMat, "emission_energy_multiplier", to, seconds);
        t.TweenProperty(_ringMat, "emission_energy_multiplier", to * 0.35f, seconds);
        if (_coreLight != null) t.TweenProperty(_coreLight, "light_energy", to * 1.6f, seconds);
    }

    // ── 몽타주 연출 ──────────────────────────────────────────────────────

    public void CorridorReset()
    {
        if (_bulkhead != null) _bulkhead.Position = _bulkhead.Position with { Y = 6.6f };
        if (_bulkheadLockMat != null) _bulkheadLockMat.EmissionEnergyMultiplier = 0f;
        foreach (var l in _doorLights) if (l != null) l.LightEnergy = 0f;
        foreach (var m in _doorLampMats) if (m != null) m.EmissionEnergyMultiplier = 0f;
        if (_panelMat != null) _panelMat.EmissionEnergyMultiplier = 0f;
        if (_panelFace != null) _panelFace.Visible = false;
        if (_panelLight != null) _panelLight.LightEnergy = 0f;
        if (_machineLampMat != null) _machineLampMat.EmissionEnergyMultiplier = 0f;
        if (_machineLight != null) _machineLight.LightEnergy = 0f;
        if (_machine != null) _machine.Position = _machine.Position with { X = -0.2f };
        _machineRunning = false;
    }

    // 격벽이 실제로 내려와 닫힌다. 정지 이미지가 아니다(§13).
    public void BulkheadClose(double seconds)
    {
        if (_bulkhead == null) return;
        var t = CreateTween();
        t.TweenProperty(_bulkhead, "position:y", 2.2f, seconds)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    public void BulkheadLocked() { if (_bulkheadLockMat != null) _bulkheadLockMat.EmissionEnergyMultiplier = 3.2f; }

    // 복도 문 조명 하나. 한꺼번에 켜지 않는다 — 연출기가 하나씩 부른다(§14).
    public void DoorLamp(int index)
    {
        if (index < 0 || index >= _doorLights.Length) return;
        if (_doorLights[index] != null) _doorLights[index].LightEnergy = 2.4f;
        if (_doorLampMats[index] != null) _doorLampMats[index].EmissionEnergyMultiplier = 3.0f;
    }

    // 패널 전원. 켜지기만 하고 끝나지 않는다 — 그 화면 안에 GUIDE-0 이 뜬다(§15).
    public void PanelPower(float energy)
    {
        if (_panelMat != null) _panelMat.EmissionEnergyMultiplier = energy;
        if (_panelLight != null) _panelLight.LightEnergy = energy * 1.2f;
    }

    public void PanelShowGuide(Texture2D face)
    {
        if (_panelFace == null || face == null) return;
        _panelFaceMat.AlbedoTexture = face;
        _panelFaceMat.EmissionTexture = face;
        _panelFace.Visible = true;
    }

    public void MachinePower()
    {
        if (_machineLampMat != null) _machineLampMat.EmissionEnergyMultiplier = 3.4f;
        if (_machineLight != null) _machineLight.LightEnergy = 1.6f;
    }

    public void MachineRun(bool on) => _machineRunning = on;

    private bool _machineRunning;
    private float _machineT;

    public override void _Process(double delta)
    {
        TickCoreRig((float)delta);
        if (!_machineRunning || _machine == null) return;
        _machineT += (float)delta;
        // 기계가 아주 미세하게 떤다. 모델 전체가 미친 듯이 흔들리면 안 된다(§16).
        _machine.Position = _machine.Position with { X = -0.2f + Mathf.Sin(_machineT * 38f) * 0.012f };
        if (_machineArm != null)
            _machineArm.Position = _machineArm.Position with { Y = 2.1f + Mathf.Sin(_machineT * 3.1f) * 0.22f };
    }
}
