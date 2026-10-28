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
    public new enum Set { None, Restraint, CoreHall, Corridor }

    // 엔딩 컷씬은 중앙제어실 CRT 가 아니라 화면 전체에 깔린다 — 해상도를 그만큼 쓴다.
    private static readonly Vector2I ViewportSize = new(1280, 720);

    private SubViewport _vp;
    private Node3D _world;
    private Camera3D _cam;
    private Vector3 _camLook;

    private Node3D _setRestraint, _setCore, _setCorridor;

    public EndingOffender Offender { get; private set; }

    // 성능 조정용 창구(디버그 계측 전용). 게임 코드는 쓰지 않는다.
    public SubViewport StageViewport => _vp;

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
            // 그림자 아틀라스 — 기본값 2048 은 이 무대에 과하다. 천장등 그림자는 작고
            // 흐릿하게만 보이면 되는데, 전방위 광원 하나가 큐브맵 여섯 면을 그린다.
            // 1024 로 줄이면 내장 그래픽에서 체감이 크고, 보이는 그림자는 거의 그대로다.
            PositionalShadowAtlasSize = 1024,
            // 3D 는 뷰포트 크기의 80% 로 그리고 늘린다(1280×720 → 1024×576).
            //
            // 이 그림은 **어차피** 1280×720 으로 그려 1920×1080 창에 늘려 깔린다. 게다가
            // 그 위에 필름 그레인 · 주사선이 덮인다. 내부 해상도를 조금 내려도 눈에 거의
            // 안 보이는 반면, 측정에서는 프레임의 1/4~1/3 이 여기서 빠졌다 —
            // 드로우 콜이 100개뿐인 장면도 50ms 였다는 건 비용이 '칠하는 면적' 에 있다는 뜻이다.
            //
            // 더 선명하게 보고 싶으면 이 값 하나만 1f 로 올리면 된다.
            Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear,
            Scaling3DScale = 0.8f,
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
        // 글로우는 단계마다 화면을 한 번씩 흐려 가며 쌓는다. 1 · 2 단계는 거의 원본
        // 해상도라 가장 비싸면서(측정에서 글로우 전체가 프레임의 1/5 였다) 번짐이 좁아
        // 눈에 거의 안 보인다. 넓게 퍼지는 3~5 단계만 남긴다 — 발광체가 번지는 느낌은
        // 그대로이고 비싼 쪽만 빠진다.
        var glowEnv = env.Environment;
        glowEnv.SetGlowLevel(1, 0f);
        glowEnv.SetGlowLevel(2, 0f);
        glowEnv.SetGlowLevel(3, 1f);
        glowEnv.SetGlowLevel(4, 1f);
        glowEnv.SetGlowLevel(5, 0.6f);
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

    // ── 메시 공유 ────────────────────────────────────────────────────────
    //
    // 상자마다 BoxMesh 를 새로 만들면 치수가 **똑같은** 이음매 · 걸레받이 · 볼트 수백 개가
    // 전부 남남이 되어, 엔진이 하나로 묶어 그릴 수 없다(드로우 콜이 그 수만큼 나간다).
    // 치수를 열쇠로 캐시해 같은 치수는 같은 메시를 쓰게 하면, 같은 재질끼리 자동으로
    // 묶여 나간다 — 보이는 그림은 한 픽셀도 바뀌지 않는다.
    //
    // 크기를 Scale 로 돌리지 않는 이유: 일부 호출부가 결과 노드의 Scale 을 직접 쓴다
    // (가늘어지는 철탑 등). 치수는 메시에 두고 Scale 은 호출부 몫으로 남긴다.
    private static readonly System.Collections.Generic.Dictionary<(int, int, int), BoxMesh> _boxCache = new();
    private static readonly System.Collections.Generic.Dictionary<(int, int, int, int), CylinderMesh> _cylCache = new();

    // 0.1 mm 단위로 끊어 열쇠를 만든다. 사람 눈에 같은 치수면 같은 메시다.
    private static int Q(float v) => Mathf.RoundToInt(v * 10000f);

    private static BoxMesh SharedBox(Vector3 size)
    {
        var key = (Q(size.X), Q(size.Y), Q(size.Z));
        if (_boxCache.TryGetValue(key, out var hit)) return hit;
        return _boxCache[key] = new BoxMesh { Size = size };
    }

    private static CylinderMesh SharedCyl(float radius, float height, int seg, float bottom = -1f)
    {
        if (bottom < 0f) bottom = radius;
        var key = (Q(radius), Q(bottom), Q(height), seg);
        if (_cylCache.TryGetValue(key, out var hit)) return hit;
        return _cylCache[key] = new CylinderMesh
        {
            TopRadius = radius, BottomRadius = bottom, Height = height, RadialSegments = seg,
        };
    }

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, string name = "")
    {
        var m = new MeshInstance3D
        {
            Name = string.IsNullOrEmpty(name) ? "Box" : name,
            Mesh = SharedBox(size),
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
            Mesh = SharedCyl(radius, height, 12),
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

    // ── 가려진 3D 는 그리지 않는다 ───────────────────────────────────────
    //
    // 컷씬 화면(Screen)은 창을 **통째로** 덮는 불투명한 그림이다. 그런데 그 뒤의
    // 중앙제어실 3D 는 그동안에도 계속 그려진다 — 보이지도 않는 방을 한 프레임도 빠짐없이
    // 두 번째로 렌더링하는 셈이다. 프롤로그·엔딩이 유독 끊겼던 가장 큰 이유가 이것이다
    // (내장 그래픽에서 장면당 40ms 가까이 여기서 나갔다).
    //
    // 노드를 숨기지 않고 **창 뷰포트의 3D 패스만** 끈다. 숨기면 각자 Visible 을 켜고 끄는
    // 다른 연출과 싸우게 되고, 되돌릴 때 무엇이 켜져 있었는지 복원할 수 없다.
    // CRT 화면(SubViewport)들은 각자의 뷰포트라 그대로 돈다 — 컷씬이 모니터 안에서
    // 돌아가는 구간(Monitor 뷰)은 Screen 이 덮지 않으므로 여기 걸리지도 않는다.
    private bool _mainWorldOff;
    // 무대는 둘일 수 있다 — 프롤로그가 세운 것이 남아 있는 채로 엔딩이 하나 더 세운다.
    // 하나라도 덮고 있으면 끈다. 각자 켜고 끄면 두 무대가 매 프레임 서로를 되돌린다.
    private static int _coverCount;

    private void CoverMainWorld(bool off)
    {
        if (_mainWorldOff == off) return;
        var win = GetViewport();
        if (win == null) return;
        _mainWorldOff = off;
        _coverCount = Mathf.Max(0, _coverCount + (off ? 1 : -1));
        bool any = _coverCount > 0;
        win.Disable3D = any;
        // CRT 화면(SubViewport)들도 같이 멈춘다 — 덮여 있는 동안 보일 리가 없다.
        ControlRoom3DController.WorldCovered = any;
    }

    public override void _ExitTree() => CoverMainWorld(false);

    public override void _Process(double delta)
    {
        // 완전히 덮였을 때만 끈다 — 페이드 중에는 뒤가 비쳐야 한다.
        CoverMainWorld(Screen is { Visible: true } && Screen.Modulate.A >= 0.985f);

        TickCoreRig((float)delta);
        TickMachine((float)delta);
    }
}
