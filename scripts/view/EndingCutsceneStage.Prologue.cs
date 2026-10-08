using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 프롤로그용 무대 — 트루엔딩이 쓰는 그 무대(EndingCutsceneStage)에 세트를 더한다.
//
// **거대한 봉쇄 코어실은 새로 만들지 않는다.** 엔딩이 쓰는 SetCoreHall 을 그대로 쓰고,
// 프롤로그에서는 "정상 가동 → 출력 저하 → 0% → 폭발 → 손상" 상태만 따로 얹는다.
// 플레이어가 트루엔딩에서 같은 공간을 다시 볼 때 "프롤로그에서 터졌던 그 코어구나" 로
// 읽혀야 하기 때문이다(지시서 §K).
//
// 나머지(연구 구역 · 브리핑실 · 생활 구역 · 연구실 · 복도)는 프롤로그 컷씬에만 나오므로
// 여기서 짓는다. 처음 쓸 때 한 번만 짓고(지연 생성), 엔딩만 플레이하면 아예 만들지 않는다.
//
// 이 파일은 partial 이다 — 엔딩 쪽 파일을 건드리지 않는다.
public partial class EndingCutsceneStage
{
    public enum PSet { None, ArchiveHall, Briefing, Habitat, Lab, Corridor, Surface }

    private Node3D _pArchive, _pBriefing, _pHabitat, _pLab, _pCorridor, _pSurface;
    private PSet _pSet = PSet.None;

    // 프롤로그 세트 하나를 띄운다. 엔딩 세트는 전부 내린다.
    public void ShowPrologueSet(PSet set)
    {
        _pSet = set;
        if (set != PSet.None) ShowSet(Set.None);   // 엔딩 세트 끄기 + 렌더 멈춤 해제는 아래에서

        if (set == PSet.ArchiveHall && _pArchive == null) BuildArchiveHall();
        if (set == PSet.Briefing && _pBriefing == null) BuildBriefing();
        if (set == PSet.Habitat && _pHabitat == null) BuildHabitat();
        if (set == PSet.Lab && _pLab == null) BuildLab();
        if (set == PSet.Corridor && _pCorridor == null) BuildPrologueCorridor();
        if (set == PSet.Surface && _pSurface == null) BuildSurface();

        if (_pArchive != null) _pArchive.Visible = set == PSet.ArchiveHall;
        if (_pBriefing != null) _pBriefing.Visible = set == PSet.Briefing;
        if (_pHabitat != null) _pHabitat.Visible = set == PSet.Habitat;
        if (_pLab != null) _pLab.Visible = set == PSet.Lab;
        if (_pCorridor != null) _pCorridor.Visible = set == PSet.Corridor;
        if (_pSurface != null) _pSurface.Visible = set == PSet.Surface;
        // 지상은 하늘 · 안개 · 환경광이 통째로 다르다 — 들어갈 때 켜고 나올 때 되돌린다.
        SurfaceAtmosphere(set == PSet.Surface);

        if (_vp != null)
            _vp.RenderTargetUpdateMode = set == PSet.None
                ? _vp.RenderTargetUpdateMode : SubViewport.UpdateMode.Always;
    }

    // 코어실을 띄울 때는 엔딩 쪽 ShowSet 을 그대로 쓴다 — 프롤로그 세트만 내려 준다.
    public void ShowCoreHallForPrologue()
    {
        if (_pArchive != null) _pArchive.Visible = false;
        if (_pBriefing != null) _pBriefing.Visible = false;
        if (_pHabitat != null) _pHabitat.Visible = false;
        if (_pLab != null) _pLab.Visible = false;
        if (_pCorridor != null) _pCorridor.Visible = false;
        if (_pSurface != null) _pSurface.Visible = false;
        SurfaceAtmosphere(false);
        _pSet = PSet.None;
        ShowSet(Set.CoreHall);
    }

    public Node3D PrologueSetRoot(PSet set) => set switch
    {
        PSet.ArchiveHall => _pArchive,
        PSet.Briefing => _pBriefing,
        PSet.Habitat => _pHabitat,
        PSet.Lab => _pLab,
        PSet.Corridor => _pCorridor,
        PSet.Surface => _pSurface,
        _ => null,
    };

    // ── 시설 표면 꾸미기 ────────────────────────────────────────────────
    //
    // 바닥 · 벽을 단색 상자 하나로 두면 어떤 조명을 줘도 "기본 머티리얼 테스트맵" 으로
    // 보인다. 형태(로우폴리)는 그대로 두고 **표면의 정보량**만 올린다 :
    //   · 패널 이음매      — 평면을 끊어 주는 가장 싼 방법
    //   · 걸레받이 · 때    — 벽과 바닥이 만나는 선을 눌러 공간의 모서리를 만든다
    //   · 얼룩 · 닳은 자국 — 운영 중인 시설의 건조한 사용감
    // 폐허처럼 지저분하게는 하지 않는다. 차갑고 무미건조한 쪽이다.

    private static readonly Color GrimeDark = new(0.020f, 0.021f, 0.024f);

    private StandardMaterial3D _seamMat, _baseMat, _stainMat;

    private StandardMaterial3D Seam => _seamMat ??= new StandardMaterial3D
    {
        AlbedoColor = GrimeDark with { A = 0.55f },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private StandardMaterial3D Baseboard => _baseMat ??= Mat(new Color(0.10f, 0.105f, 0.12f), 0.95f);

    private StandardMaterial3D Stain => _stainMat ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.03f, 0.028f, 0.026f, 0.30f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // 긴 복도형 공간의 벽 · 바닥을 꾸민다. (halfW, h) = 안쪽 치수, z0~z1 = 길이 방향 범위.
    private void DressCorridorSurfaces(Node3D root, float halfW, float h, float z0, float z1,
        float seamEvery = 2.4f)
    {
        // 벽과 바닥이 만나는 선 · 천장과 만나는 선을 눌러 준다. 코너가 있어야 공간이 선다.
        float len = z1 - z0, mid = (z0 + z1) * 0.5f;
        foreach (float sx in new[] { -1f, 1f })
        {
            Box(root, new Vector3(0.1f, 0.22f, len), new Vector3(sx * (halfW - 0.05f), 0.11f, mid),
                Baseboard, "Baseboard");
            // 벽 아래쪽이 조금 더 눌린 느낌 — 때가 타는 자리다.
            Box(root, new Vector3(0.02f, 0.5f, len), new Vector3(sx * (halfW - 0.02f), 0.46f, mid),
                Stain, "WallGrime");
            // 천장 모서리.
            Box(root, new Vector3(0.02f, 0.3f, len), new Vector3(sx * (halfW - 0.02f), h - 0.15f, mid),
                Stain, "CeilGrime");
        }

        // 패널 이음매 — 벽과 바닥을 일정 간격으로 끊는다.
        int n = Mathf.Max(1, Mathf.RoundToInt(len / seamEvery));
        for (int i = 0; i <= n; i++)
        {
            float z = z0 + len * i / n;
            foreach (float sx in new[] { -1f, 1f })
                Box(root, new Vector3(0.02f, h - 0.3f, 0.035f),
                    new Vector3(sx * (halfW - 0.02f), (h - 0.3f) * 0.5f + 0.1f, z), Seam, "WallSeam");
            Box(root, new Vector3(halfW * 2f - 0.2f, 0.004f, 0.04f), new Vector3(0f, 0.012f, z),
                Seam, "FloorSeam");
        }
        // 바닥 가운데를 따라가는 이음매 한 줄.
        Box(root, new Vector3(0.04f, 0.004f, len), new Vector3(0f, 0.012f, mid), Seam, "FloorSeamMid");
    }

    // 네모난 방의 벽 · 바닥. (halfX, halfZ) = 안쪽 반치수.
    private void DressRoomSurfaces(Node3D root, float halfX, float halfZ, float h, Vector3 at,
        float seamEvery = 2.2f)
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)(halfX * 97f + halfZ * 31f + h * 13f);

        foreach (float sx in new[] { -1f, 1f })
        {
            Box(root, new Vector3(0.1f, 0.22f, halfZ * 2f), at + new Vector3(sx * (halfX - 0.05f), 0.11f, 0f),
                Baseboard, "Baseboard");
            Box(root, new Vector3(0.02f, 0.45f, halfZ * 2f), at + new Vector3(sx * (halfX - 0.02f), 0.44f, 0f),
                Stain, "WallGrime");
        }
        foreach (float sz in new[] { -1f, 1f })
        {
            Box(root, new Vector3(halfX * 2f, 0.22f, 0.1f), at + new Vector3(0f, 0.11f, sz * (halfZ - 0.05f)),
                Baseboard, "Baseboard");
            Box(root, new Vector3(halfX * 2f, 0.45f, 0.02f), at + new Vector3(0f, 0.44f, sz * (halfZ - 0.02f)),
                Stain, "WallGrime");
        }

        int nz = Mathf.Max(1, Mathf.RoundToInt(halfZ * 2f / seamEvery));
        for (int i = 0; i <= nz; i++)
        {
            float z = -halfZ + halfZ * 2f * i / nz;
            foreach (float sx in new[] { -1f, 1f })
                Box(root, new Vector3(0.02f, h - 0.3f, 0.035f),
                    at + new Vector3(sx * (halfX - 0.02f), (h - 0.3f) * 0.5f + 0.1f, z), Seam, "WallSeam");
        }
        int nx = Mathf.Max(1, Mathf.RoundToInt(halfX * 2f / seamEvery));
        for (int i = 0; i <= nx; i++)
        {
            float x = -halfX + halfX * 2f * i / nx;
            foreach (float sz in new[] { -1f, 1f })
                Box(root, new Vector3(0.035f, h - 0.3f, 0.02f),
                    at + new Vector3(x, (h - 0.3f) * 0.5f + 0.1f, sz * (halfZ - 0.02f)), Seam, "WallSeam");
            Box(root, new Vector3(0.04f, 0.004f, halfZ * 2f), at + new Vector3(x, 0.012f, 0f),
                Seam, "FloorSeam");
        }

        // 바닥에 희미한 얼룩 몇 — 완전 단색이면 바닥이 아니라 색면으로 보인다.
        for (int i = 0; i < 7; i++)
        {
            float s = rng.RandfRange(0.5f, 1.8f);
            Box(root, new Vector3(s, 0.003f, s * rng.RandfRange(0.6f, 1.4f)),
                at + new Vector3(rng.RandfRange(-halfX + 0.6f, halfX - 0.6f), 0.009f,
                                 rng.RandfRange(-halfZ + 0.6f, halfZ - 0.6f)), Stain, "FloorStain");
        }
    }

    // ── 공용 조각 ────────────────────────────────────────────────────────

    private static readonly Color Cool = new(0.80f, 0.86f, 1f);
    private static readonly Color Warm = new(1f, 0.90f, 0.76f);

    // 천장 형광등 한 줄 — 발광 판 + 실제 광원. 재난 때 이 재질만 건드리면 전부 깜빡인다.
    private StandardMaterial3D Strip(Node3D parent, Vector3 pos, float len, Color col, float energy,
        List<StandardMaterial3D> mats, List<Light3D> lights, float lightEnergy = 1.3f)
    {
        var mat = Glow(col, energy);
        Box(parent, new Vector3(0.5f, 0.08f, len), pos, mat, "Strip");
        mats?.Add(mat);
        // 몇 개 건너 하나씩 그림자를 켠다 — 테이블 밑 · 문 옆에 그늘이 생겨야 공간이 선다.
        bool shadow = (_shadowTurn++ % 3) == 0;
        var l = new OmniLight3D
        {
            Position = pos with { Y = pos.Y - 0.3f },
            LightColor = col, LightEnergy = lightEnergy, OmniRange = 11f,
            ShadowEnabled = shadow,
            ShadowBias = 0.06f,
            DistanceFadeEnabled = false,
        };
        parent.AddChild(l);
        lights?.Add(l);
        return mat;
    }

    private int _shadowTurn;
    private readonly List<StandardMaterial3D> _pLampMats = new();
    private readonly List<Light3D> _pLights = new();
    private readonly List<float> _pLightBase = new();

    // 조명이 한 번에 깜빡인다(재난 시작 · 연구실 충격). 0 = 꺼짐, 1 = 정상.
    public void PrologueLights(float k)
    {
        for (int i = 0; i < _pLights.Count; i++)
            if (_pLights[i] != null) _pLights[i].LightEnergy = _pLightBase[i] * k;
        foreach (var m in _pLampMats) if (m != null) m.EmissionEnergyMultiplier = 2.6f * k;
    }

    private void CaptureLightBase()
    {
        _pLightBase.Clear();
        foreach (var l in _pLights) _pLightBase.Add(l?.LightEnergy ?? 0f);
    }

    // ── ① 연구 구역 전경 (archive_01_facility) ───────────────────────────
    //
    // 밝고 · 정상이고 · 사람이 있고 · 기계가 돌아간다. 긴 홀 하나면 충분하다 —
    // 카메라가 느리게 밀고 들어가는 프레임 안만 설득력 있으면 된다(§5).
    private void BuildArchiveHall()
    {
        _pArchive = new Node3D { Name = "PSetArchiveHall" };
        _world.AddChild(_pArchive);

        var floor = Mat(new Color(0.30f, 0.315f, 0.335f), 0.72f, 0.04f);
        var wall = Mat(new Color(0.345f, 0.36f, 0.385f), 0.88f);
        var steel = Mat(new Color(0.26f, 0.275f, 0.30f), 0.52f, 0.85f);
        var dark = Mat(new Color(0.24f, 0.25f, 0.28f), 0.6f, 0.4f);
        var screen = Glow(new Color(0.42f, 0.78f, 0.95f), 1.5f);

        Box(_pArchive, new Vector3(22f, 0.4f, 44f), new Vector3(0f, -0.2f, 0f), floor, "Floor");
        Box(_pArchive, new Vector3(22f, 7f, 0.4f), new Vector3(0f, 3.5f, -22f), wall, "WallBack");
        DressCorridorSurfaces(_pArchive, 11f, 7f, -22f, 22f, 3.6f);
        Box(_pArchive, new Vector3(0.4f, 7f, 44f), new Vector3(-11f, 3.5f, 0f), wall, "WallL");
        Box(_pArchive, new Vector3(0.4f, 7f, 44f), new Vector3(11f, 3.5f, 0f), wall, "WallR");
        Box(_pArchive, new Vector3(22f, 0.4f, 44f), new Vector3(0f, 7f, 0f), wall, "Ceiling");

        // 바닥 유도선 — 공간의 깊이를 읽게 한다.
        var line = Glow(new Color(0.95f, 0.78f, 0.25f), 0.5f);
        Box(_pArchive, new Vector3(0.14f, 0.02f, 42f), new Vector3(-3.2f, 0.02f, 0f), line);
        Box(_pArchive, new Vector3(0.14f, 0.02f, 42f), new Vector3(3.2f, 0.02f, 0f), line);

        // 천장등 — 두 줄.
        for (int i = 0; i < 9; i++)
        {
            float z = 18f - i * 4.6f;
            Strip(_pArchive, new Vector3(-4.4f, 6.6f, z), 3.4f, Cool, 2.6f, _pLampMats, _pLights);
            Strip(_pArchive, new Vector3(4.4f, 6.6f, z), 3.4f, Cool, 2.6f, _pLampMats, _pLights);
        }

        // 양옆 연구 설비 — 콘솔 · 탱크 · 배관.
        for (int i = 0; i < 7; i++)
        {
            float z = 15f - i * 5f;
            foreach (float side in new[] { -1f, 1f })
            {
                var bay = new Node3D { Position = new Vector3(side * 8.2f, 0f, z) };
                _pArchive.AddChild(bay);
                Box(bay, new Vector3(2.6f, 1.1f, 3.0f), new Vector3(0f, 0.55f, 0f), dark, "Console");
                Box(bay, new Vector3(2.4f, 0.1f, 2.8f), new Vector3(0f, 1.12f, 0f), dark, "Top");
                var sc = new MeshInstance3D
                {
                    Mesh = new QuadMesh { Size = new Vector2(1.6f, 0.9f) },
                    Position = new Vector3(-side * 1.2f, 1.75f, 0f),
                    // 화면은 가운데 통로를 본다(바깥을 보면 카메라에서 모서리만 보인다).
                    RotationDegrees = new Vector3(0f, side > 0f ? -90f : 90f, 0f),
                    MaterialOverride = screen,
                };
                bay.AddChild(sc);
                Box(bay, new Vector3(0.12f, 1.4f, 0.12f), new Vector3(0f, 1.9f, -1.3f), steel);
                if (i % 2 == 0) Cyl(bay, 0.55f, 3.4f, new Vector3(side * 1.4f, 1.7f, 1.9f), steel, "Tank");
                // 콘솔 앞면 표시등 — 통로에서 보이는 작은 색점이 공간을 살아 있게 만든다.
                Box(bay, new Vector3(0.1f, 0.1f, 0.5f), new Vector3(-side * 1.26f, 1.0f, -0.9f),
                    Glow(new Color(0.35f, 1f, 0.55f), 2.2f));
                Box(bay, new Vector3(0.1f, 0.1f, 0.3f), new Vector3(-side * 1.26f, 0.78f, 0.6f),
                    Glow(new Color(1f, 0.72f, 0.25f), 1.8f));
            }
        }

        // 천장 배관 — 위쪽이 비면 공간이 가벼워 보인다.
        for (int i = -3; i <= 3; i++)
            Cyl(_pArchive, 0.16f, 42f, new Vector3(i * 2.6f, 6.2f, 0f), steel).RotationDegrees =
                new Vector3(90f, 0f, 0f);

        // 안쪽 큰 창 — 끝이 막혀 보이지 않게.
        Box(_pArchive, new Vector3(9f, 3.2f, 0.12f), new Vector3(0f, 3.4f, -21.7f),
            Glow(new Color(0.55f, 0.82f, 1f), 1.1f), "FarWindow");

        CaptureLightBase();
    }

    // ── ② 브리핑실 (archive_02_director) ─────────────────────────────────
    //
    // 총괄 관리자가 카메라를 보고 말하는 작은 방. 녹화된 안전교육 영상처럼 보여야 하므로
    // 정면 키 라이트 하나와 뒤쪽 패널만 둔다(§6).
    private void BuildBriefing()
    {
        _pBriefing = new Node3D { Name = "PSetBriefing" };
        _world.AddChild(_pBriefing);

        var floor = Mat(new Color(0.195f, 0.20f, 0.22f), 0.80f);
        var wall = Mat(new Color(0.165f, 0.185f, 0.215f), 0.90f);
        var steel = Mat(new Color(0.25f, 0.265f, 0.29f), 0.52f, 0.85f);

        Box(_pBriefing, new Vector3(10f, 0.3f, 10f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pBriefing, new Vector3(10f, 4.2f, 0.3f), new Vector3(0f, 2.1f, -3.2f), wall, "WallBack");
        Box(_pBriefing, new Vector3(0.3f, 4.2f, 10f), new Vector3(-5f, 2.1f, 0f), wall, "WallL");
        Box(_pBriefing, new Vector3(0.3f, 4.2f, 10f), new Vector3(5f, 2.1f, 0f), wall, "WallR");
        DressRoomSurfaces(_pBriefing, 5f, 5f, 4.2f, Vector3.Zero, 2.0f);
        Box(_pBriefing, new Vector3(10f, 0.3f, 10f), new Vector3(0f, 4.2f, 0f), wall, "Ceiling");

        // 뒤쪽 기관 표식 패널.
        Box(_pBriefing, new Vector3(4.6f, 1.9f, 0.1f), new Vector3(0f, 2.3f, -3.0f),
            Mat(new Color(0.14f, 0.17f, 0.21f), 0.8f), "Backdrop");
        Box(_pBriefing, new Vector3(3.0f, 0.1f, 0.06f), new Vector3(0f, 1.62f, -2.94f),
            Glow(new Color(0.45f, 0.8f, 1f), 1.2f));
        Box(_pBriefing, new Vector3(0.9f, 0.9f, 0.06f), new Vector3(-1.6f, 2.6f, -2.94f),
            Glow(new Color(0.45f, 0.8f, 1f), 0.9f));

        // 연단.
        Box(_pBriefing, new Vector3(1.5f, 1.05f, 0.7f), new Vector3(0f, 0.52f, -0.55f), steel, "Podium");
        Box(_pBriefing, new Vector3(1.6f, 0.08f, 0.8f), new Vector3(0f, 1.08f, -0.55f),
            Mat(new Color(0.18f, 0.19f, 0.22f), 0.5f));

        // 정면 키 라이트 + 약한 뒷광.
        var key = new SpotLight3D
        {
            Position = new Vector3(1.1f, 3.1f, 2.6f),
            LightColor = Warm, LightEnergy = 4.0f, SpotRange = 11f, SpotAngle = 42f,
            SpotAngleAttenuation = 0.6f, ShadowEnabled = false,
        };
        key.LookAt(new Vector3(0f, 1.5f, -0.6f), Vector3.Up);
        _pBriefing.AddChild(key);
        _pLights.Add(key);
        _pBriefing.AddChild(new OmniLight3D
        {
            Position = new Vector3(-1.8f, 2.6f, -2.2f),
            LightColor = Cool, LightEnergy = 1.1f, OmniRange = 8f, ShadowEnabled = false,
        });
        CaptureLightBase();
    }

    // ── ③ 생활 구역 (archive_03_habitat) ─────────────────────────────────
    private void BuildHabitat()
    {
        _pHabitat = new Node3D { Name = "PSetHabitat" };
        _world.AddChild(_pHabitat);

        var floor = Mat(new Color(0.265f, 0.245f, 0.22f), 0.82f);
        var wall = Mat(new Color(0.30f, 0.285f, 0.26f), 0.90f);
        var steel = Mat(new Color(0.25f, 0.265f, 0.29f), 0.55f, 0.78f);
        var wood = Mat(new Color(0.235f, 0.175f, 0.12f), 0.62f);

        Box(_pHabitat, new Vector3(14f, 0.3f, 16f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pHabitat, new Vector3(14f, 4.4f, 0.3f), new Vector3(0f, 2.2f, -8f), wall, "WallBack");
        Box(_pHabitat, new Vector3(0.3f, 4.4f, 16f), new Vector3(-7f, 2.2f, 0f), wall, "WallL");
        DressRoomSurfaces(_pHabitat, 7f, 8f, 4.4f, Vector3.Zero, 2.2f);
        Box(_pHabitat, new Vector3(0.3f, 4.4f, 16f), new Vector3(7f, 2.2f, 0f), wall, "WallR");
        Box(_pHabitat, new Vector3(14f, 0.3f, 16f), new Vector3(0f, 4.4f, 0f), wall, "Ceiling");

        // 식탁 셋 + 의자.
        for (int i = 0; i < 3; i++)
        {
            float x = -4f + i * 4f;
            Box(_pHabitat, new Vector3(2.4f, 0.1f, 1.2f), new Vector3(x, 0.78f, -1.5f), wood, "Table");
            foreach (float dx in new[] { -0.8f, 0.8f })
            {
                Cyl(_pHabitat, 0.06f, 0.78f, new Vector3(x + dx, 0.39f, -1.5f), steel);
                Box(_pHabitat, new Vector3(0.5f, 0.07f, 0.5f), new Vector3(x + dx, 0.45f, -0.4f), steel, "Chair");
                Cyl(_pHabitat, 0.05f, 0.45f, new Vector3(x + dx, 0.22f, -0.4f), steel);
            }
        }

        // 벽 사물함 줄 · 자판기.
        for (int i = 0; i < 6; i++)
            Box(_pHabitat, new Vector3(0.9f, 2.0f, 0.5f), new Vector3(-5.6f + i * 1.0f, 1.0f, -7.5f), steel, "Locker");
        Box(_pHabitat, new Vector3(1.1f, 2.1f, 0.8f), new Vector3(5.6f, 1.05f, -7.4f), steel, "Vendor");
        Box(_pHabitat, new Vector3(0.85f, 1.1f, 0.06f), new Vector3(5.6f, 1.35f, -6.98f),
            Glow(new Color(0.9f, 0.75f, 0.35f), 1.4f));

        for (int i = 0; i < 4; i++)
            Strip(_pHabitat, new Vector3(0f, 4.0f, 5f - i * 4f), 5.0f, new Color(1f, 0.95f, 0.86f), 2.2f,
                _pLampMats, _pLights, 1.5f);
        CaptureLightBase();
    }

    // ── ④ 연구실 (disaster_04_lab_wreck) ────────────────────────────────
    //
    // 대재난의 물리적 충격을 처음 보여 주는 방. 깨질 유리 칸막이와 넘어질 소품이 있어야 한다(§13).
    private void BuildLab()
    {
        _pLab = new Node3D { Name = "PSetLab" };
        _world.AddChild(_pLab);

        var floor = Mat(new Color(0.215f, 0.225f, 0.245f), 0.75f);
        var wall = Mat(new Color(0.255f, 0.265f, 0.29f), 0.90f);
        var steel = Mat(new Color(0.26f, 0.275f, 0.30f), 0.52f, 0.85f);
        var dark = Mat(new Color(0.16f, 0.17f, 0.20f), 0.55f, 0.4f);

        Box(_pLab, new Vector3(12f, 0.3f, 12f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pLab, new Vector3(12f, 4.4f, 0.3f), new Vector3(0f, 2.2f, -6f), wall, "WallBack");
        Box(_pLab, new Vector3(0.3f, 4.4f, 12f), new Vector3(-6f, 2.2f, 0f), wall, "WallL");
        DressRoomSurfaces(_pLab, 6f, 6f, 4.4f, Vector3.Zero, 2.0f);
        Box(_pLab, new Vector3(0.3f, 4.4f, 12f), new Vector3(6f, 2.2f, 0f), wall, "WallR");
        Box(_pLab, new Vector3(12f, 0.3f, 12f), new Vector3(0f, 4.4f, 0f), wall, "Ceiling");

        // 실험대 둘 — 위에 올려 둔 소품이 흔들리고 떨어진다.
        _labProps.Clear();
        for (int t = 0; t < 2; t++)
        {
            float z = -2.2f + t * 3.2f;
            Box(_pLab, new Vector3(5.2f, 0.12f, 1.3f), new Vector3(-1.4f, 0.92f, z), steel, "Bench");
            for (int i = 0; i < 4; i++)
                Cyl(_pLab, 0.05f, 0.9f, new Vector3(-3.6f + i * 1.5f, 0.45f, z), steel);
            for (int i = 0; i < 3; i++)
            {
                var prop = Box(_pLab, new Vector3(0.22f, 0.3f, 0.22f),
                    new Vector3(-3.0f + i * 1.3f + t * 0.4f, 1.13f, z), dark, "Prop");
                _labProps.Add(prop);
            }
            var mon = Box(_pLab, new Vector3(0.9f, 0.6f, 0.08f), new Vector3(0.6f + t * 0.3f, 1.3f, z - 0.4f),
                dark, "Monitor");
            Box(mon, new Vector3(0.8f, 0.5f, 0.02f), new Vector3(0f, 0f, 0.06f),
                Glow(new Color(0.4f, 0.8f, 0.95f), 1.3f));
            _labProps.Add(mon);
        }

        // 유리 칸막이 — 이 장면의 주인공. 깨지면 숨기고 파편을 뿌린다.
        _labGlassMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.65f, 0.82f, 0.88f, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.08f, Metallic = 0.1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _labGlass = Box(_pLab, new Vector3(0.06f, 2.6f, 7.0f), new Vector3(3.3f, 1.4f, -0.6f),
            _labGlassMat, "GlassWall");
        Box(_pLab, new Vector3(0.12f, 0.12f, 7.1f), new Vector3(3.3f, 2.72f, -0.6f), steel);
        Box(_pLab, new Vector3(0.12f, 0.12f, 7.1f), new Vector3(3.3f, 0.1f, -0.6f), steel);
        Box(_pLab, new Vector3(0.12f, 2.6f, 0.12f), new Vector3(3.3f, 1.4f, 2.9f), steel);

        // 뒷벽 설비 — 선반 · 배관 · 표시등. 빈 벽이 보이면 방이 세트처럼 보인다.
        Box(_pLab, new Vector3(9f, 0.1f, 0.5f), new Vector3(-1f, 2.3f, -5.6f), steel, "Shelf");
        Box(_pLab, new Vector3(9f, 0.1f, 0.5f), new Vector3(-1f, 1.6f, -5.6f), steel, "Shelf2");
        for (int i = 0; i < 7; i++)
        {
            Box(_pLab, new Vector3(0.3f, 0.45f, 0.3f), new Vector3(-4.6f + i * 1.2f, 2.58f, -5.6f), dark);
            Box(_pLab, new Vector3(0.26f, 0.3f, 0.26f), new Vector3(-4.4f + i * 1.2f, 1.8f, -5.6f), dark);
        }
        Cyl(_pLab, 0.14f, 11.5f, new Vector3(-5.4f, 3.9f, 0f), steel).RotationDegrees = new Vector3(90f, 0f, 0f);
        Cyl(_pLab, 0.1f, 11.5f, new Vector3(-4.9f, 4.05f, 0f), steel).RotationDegrees = new Vector3(90f, 0f, 0f);
        Box(_pLab, new Vector3(0.1f, 0.14f, 0.5f), new Vector3(-5.9f, 1.9f, 1.5f),
            Glow(new Color(0.35f, 1f, 0.55f), 2.0f));

        for (int i = 0; i < 3; i++)
            Strip(_pLab, new Vector3(-1.5f, 4.1f, 3f - i * 3.2f), 4.2f, Cool, 2.4f, _pLampMats, _pLights, 1.6f);
        CaptureLightBase();
    }

    private readonly List<MeshInstance3D> _labProps = new();
    private MeshInstance3D _labGlass;
    private StandardMaterial3D _labGlassMat;

    public IReadOnlyList<MeshInstance3D> LabProps => _labProps;
    public MeshInstance3D LabGlass => _labGlass;

    // ── ⑤ 프롤로그 복도 (직원 도주 · 차폐문 · CCTV) ──────────────────────
    //
    // 엔딩 몽타주 복도와 따로 둔다 — 그쪽은 '복구되는 시설'이고 여기는 '무너지는 시설'이라
    // 길이 · 조명 · 문 구성이 다르다.
    private void BuildPrologueCorridor()
    {
        _pCorridor = new Node3D { Name = "PSetCorridor" };
        _world.AddChild(_pCorridor);

        var floor = Mat(new Color(0.235f, 0.25f, 0.265f), 0.80f);
        var wall = Mat(new Color(0.285f, 0.30f, 0.325f), 0.90f);
        var steel = Mat(new Color(0.24f, 0.255f, 0.28f), 0.52f, 0.85f);
        var dark = Mat(new Color(0.15f, 0.16f, 0.19f), 0.6f, 0.4f);

        Box(_pCorridor, new Vector3(5.2f, 0.3f, 56f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pCorridor, new Vector3(0.3f, 4.2f, 56f), new Vector3(-2.6f, 2.1f, 0f), wall, "WallL");
        Box(_pCorridor, new Vector3(0.3f, 4.2f, 56f), new Vector3(2.6f, 2.1f, 0f), wall, "WallR");
        Box(_pCorridor, new Vector3(5.2f, 0.3f, 56f), new Vector3(0f, 4.2f, 0f), wall, "Ceiling");

        DressCorridorSurfaces(_pCorridor, 2.6f, 4.2f, -28f, 28f);

        // 문 · 배관 · 천장등을 일정 간격으로. 달릴 때 흘러가는 것이 있어야 속도가 읽힌다.
        for (int i = 0; i < 14; i++)
        {
            float z = 24f - i * 4f;
            Strip(_pCorridor, new Vector3(0f, 4.0f, z), 2.2f, Cool, 2.4f, _pLampMats, _pLights, 1.25f);
            float side = i % 2 == 0 ? -1f : 1f;
            Box(_pCorridor, new Vector3(0.1f, 2.3f, 1.4f), new Vector3(side * 2.44f, 1.15f, z), dark, "Door");
            Box(_pCorridor, new Vector3(0.08f, 0.12f, 1.5f), new Vector3(side * 2.4f, 2.4f, z), steel);
            Cyl(_pCorridor, 0.1f, 3.9f, new Vector3(side * 2.3f, 3.7f, z), steel).RotationDegrees =
                new Vector3(90f, 0f, 0f);
        }

        // 차폐문 — 복도 안쪽. 실제로 내려와 닫힌다.
        _pBulkFrame = new Node3D { Position = new Vector3(0f, 0f, -20f) };
        _pCorridor.AddChild(_pBulkFrame);
        Box(_pBulkFrame, new Vector3(5.6f, 0.5f, 0.9f), new Vector3(0f, 4.3f, 0f), steel, "Lintel");
        Box(_pBulkFrame, new Vector3(0.5f, 4.2f, 0.9f), new Vector3(-2.75f, 2.1f, 0f), steel);
        Box(_pBulkFrame, new Vector3(0.5f, 4.2f, 0.9f), new Vector3(2.75f, 2.1f, 0f), steel);
        _pBulkhead = Box(_pBulkFrame, new Vector3(5.0f, 4.0f, 0.42f), new Vector3(0f, 6.2f, 0f),
            Mat(new Color(0.35f, 0.37f, 0.40f), 0.3f, 0.9f), "Bulkhead");
        for (int i = -2; i <= 2; i++)
            Box(_pBulkhead, new Vector3(0.22f, 3.8f, 0.08f), new Vector3(i * 1.0f, 0f, 0.26f), dark);
        // 경고등 둘 — 닫히는 동안 점멸한다.
        _pBulkWarnMat = Glow(new Color(1f, 0.55f, 0.1f), 0f);
        Box(_pBulkFrame, new Vector3(0.4f, 0.22f, 0.1f), new Vector3(-1.6f, 4.3f, 0.5f), _pBulkWarnMat);
        Box(_pBulkFrame, new Vector3(0.4f, 0.22f, 0.1f), new Vector3(1.6f, 4.3f, 0.5f), _pBulkWarnMat);
        _pBulkWarnLight = new OmniLight3D
        {
            Position = new Vector3(0f, 3.9f, 1.2f), LightColor = new Color(1f, 0.5f, 0.12f),
            LightEnergy = 0f, OmniRange = 9f, ShadowEnabled = false,
        };
        _pBulkFrame.AddChild(_pBulkWarnLight);

        // 차폐문 **너머**의 짧은 구간 — 뒤가 새까마면 닫히는 문이 허공에 떠 보인다.
        Box(_pCorridor, new Vector3(5.2f, 0.3f, 10f), new Vector3(0f, -0.15f, -25.5f), floor, "FarFloor");
        Box(_pCorridor, new Vector3(0.3f, 4.2f, 10f), new Vector3(-2.6f, 2.1f, -25.5f), wall);
        Box(_pCorridor, new Vector3(0.3f, 4.2f, 10f), new Vector3(2.6f, 2.1f, -25.5f), wall);
        Box(_pCorridor, new Vector3(5.2f, 0.3f, 10f), new Vector3(0f, 4.2f, -25.5f), wall);
        Box(_pCorridor, new Vector3(5.2f, 4.2f, 0.3f), new Vector3(0f, 2.1f, -30.4f), wall, "FarEnd");
        _pCorridor.AddChild(new OmniLight3D
        {
            Name = "FarFill", Position = new Vector3(0f, 3.2f, -24f),
            LightColor = new Color(0.55f, 0.6f, 0.7f), LightEnergy = 1.1f, OmniRange = 12f, ShadowEnabled = false,
        });

        // 비상등 — 재난 전환에서 켜진다.
        for (int i = 0; i < 7; i++)
        {
            var m = Glow(new Color(1f, 0.16f, 0.1f), 0f);
            _pRedMats.Add(m);
            Box(_pCorridor, new Vector3(0.3f, 0.18f, 0.18f), new Vector3(-2.38f, 3.3f, 20f - i * 7f), m);
            var l = new OmniLight3D
            {
                Position = new Vector3(-2.0f, 3.2f, 20f - i * 7f), LightColor = new Color(1f, 0.14f, 0.09f),
                LightEnergy = 0f, OmniRange = 6.5f, ShadowEnabled = false,
            };
            _pCorridor.AddChild(l);
            _pRedLights.Add(l);
        }

        CaptureLightBase();
    }

    private Node3D _pBulkFrame;
    private MeshInstance3D _pBulkhead;
    private StandardMaterial3D _pBulkWarnMat;
    private OmniLight3D _pBulkWarnLight;
    private readonly List<StandardMaterial3D> _pRedMats = new();
    private readonly List<OmniLight3D> _pRedLights = new();

    // 비상등(붉은) 세기. 0 = 꺼짐.
    public void PrologueEmergency(float k)
    {
        foreach (var m in _pRedMats) if (m != null) m.EmissionEnergyMultiplier = 3.2f * k;
        foreach (var l in _pRedLights) if (l != null) l.LightEnergy = 1.5f * k;
    }

    public void BulkheadWarn(float k)
    {
        if (_pBulkWarnMat != null) _pBulkWarnMat.EmissionEnergyMultiplier = 3.4f * k;
        if (_pBulkWarnLight != null) _pBulkWarnLight.LightEnergy = 2.2f * k;
    }

    public void PrologueBulkheadReset()
    {
        if (_pBulkhead != null) _pBulkhead.Position = _pBulkhead.Position with { Y = 6.2f };
        BulkheadWarn(0f);
    }

    // 차폐문이 실제로 내려온다.
    public void PrologueBulkheadClose(double seconds)
    {
        if (_pBulkhead == null) return;
        var t = CreateTween();
        t.TweenProperty(_pBulkhead, "position:y", 2.0f, seconds)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
    }

    public float BulkheadY => _pBulkhead?.Position.Y ?? 0f;

    // ── 카메라 흔들림 ────────────────────────────────────────────────────
    //
    // ── 끊을 수 있는 카메라 이동 ────────────────────────────────────────
    //
    // MoveCam 은 끝날 때까지 매 프레임 카메라를 덮어쓰고, 중간에 멈출 방법이 없다.
    // 엔딩은 컷 하나가 끝까지 재생되니 문제가 없지만, 프롤로그는 플레이어가 대사를
    // 넘기는 순간 다음 컷으로 건너뛴다. 그러면 앞 컷이 띄워 둔 이동이 **다음 컷의
    // SetCam 을 계속 덮어써서** 엉뚱한 데를 비춘다(연구실 컷이 천장을 보던 원인).
    // 그래서 프롤로그는 세대 번호로 끊을 수 있는 이쪽을 쓴다.
    private int _camGen;

    public void CancelCamMove() => _camGen++;

    public async Task MoveCamCut(Vector3 to, Vector3 look, double seconds, float toFov = 0f)
    {
        if (_cam == null) return;
        int gen = ++_camGen;
        Vector3 from = _cam.Position;
        Vector3 lookFrom = _camLook;
        float fovFrom = _cam.Fov, fovTo = toFov > 0f ? toFov : _cam.Fov;
        double t = 0;
        while (t < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (gen != _camGen || _cam == null) return;   // 컷이 바뀌었다 — 손을 뗀다
            t += GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / seconds), 0f, 1f);
            float e = k < 0.5f ? 2f * k * k : 1f - Mathf.Pow(-2f * k + 2f, 2f) / 2f;
            _cam.Position = from.Lerp(to, e);
            _camLook = lookFrom.Lerp(look, e);
            _cam.Fov = Mathf.Lerp(fovFrom, fovTo, e);
            _cam.LookAt(_camLook, Vector3.Up);
        }
    }

    // 카메라의 **위치**를 흔들면 MoveCam 이 매 프레임 덮어써서 이동 중에는 흔들리지 않는다.
    // h_offset / v_offset 은 변환을 건드리지 않고 투영만 민다 — 이동과 같이 써도 안 싸운다.
    public void ShakeOffset(Vector3 v)
    {
        if (_cam == null) return;
        _cam.HOffset = v.X;
        _cam.VOffset = v.Y;
    }

    // ── 프롤로그 : 코어 상태 ─────────────────────────────────────────────
    //
    // 엔딩이 쓰는 그 코어다. 출력(%)에 따라 빛 · 링 · 바닥등이 함께 무너진다 —
    // 게이지 숫자만 내려가는 것이 아니라 **3D 코어 자체가** 출력 저하를 보여야 한다(§21).
    public void PrologueCoreOutput(float pct)
    {
        float k = Mathf.Clamp(pct / 100f, 0f, 1f);
        if (_coreMat != null)
        {
            // 2.4 까지 올리면 구(球)가 통째로 하얗게 날아가 형태가 사라진다.
            _coreMat.EmissionEnergyMultiplier = 0.15f + 1.35f * k;
            // 출력이 낮아질수록 푸른빛이 빠지고 탁한 주황으로 넘어간다.
            _coreMat.Emission = new Color(0.72f, 0.85f, 1f).Lerp(new Color(1f, 0.42f, 0.16f), 1f - k);
        }
        if (_coreLight != null)
        {
            _coreLight.LightEnergy = 0.2f + 3.1f * k;
            _coreLight.LightColor = new Color(0.72f, 0.86f, 1f).Lerp(new Color(1f, 0.45f, 0.2f), 1f - k);
        }
        if (_ringMat != null) _ringMat.EmissionEnergyMultiplier = 0.9f * k;
        // 바닥등은 출력에 비례해 하나씩 꺼진다.
        int on = Mathf.RoundToInt(k * 10f);
        for (int i = 0; i < 10; i++)
            if (_floorLampMats[i] != null) _floorLampMats[i].EmissionEnergyMultiplier = i < on ? 2.0f : 0f;
        _coreWobble = 1f - k;
    }

    // 링이 제자리를 못 잡고 떨리는 정도(출력이 낮을수록 커진다). 연출기가 매 프레임 돌린다.
    private float _coreWobble;
    private float _coreT;

    public void TickCore(float delta)
    {
        if (_core == null || _coreWobble <= 0.001f) return;
        _coreT += delta;
        float w = _coreWobble;
        for (int i = 0; i < _rings.Length; i++)
        {
            if (_rings[i] == null) continue;
            Vector3 b = i switch
            {
                0 => new Vector3(90f, 0f, 0f),
                1 => new Vector3(0f, 0f, 90f),
                _ => new Vector3(0f, 90f, 0f),
            };
            float f = 7f + i * 2.3f;
            _rings[i].RotationDegrees = b + new Vector3(
                Mathf.Sin(_coreT * f) * 9f * w,
                Mathf.Sin(_coreT * (f * 0.7f) + 1.1f) * 9f * w,
                Mathf.Sin(_coreT * (f * 1.3f) + 2.2f) * 7f * w);
        }
        _core.Position = CoreCenter + new Vector3(
            Mathf.Sin(_coreT * 23f) * 0.07f, Mathf.Sin(_coreT * 19f + 0.8f) * 0.06f, 0f) * w;
    }

    // 0% 직후의 정적 — 빛이 완전히 죽는다. 폭발 직전 한 박자(§22).
    public void PrologueCoreSilence()
    {
        if (_coreMat != null) _coreMat.EmissionEnergyMultiplier = 0f;
        if (_ringMat != null) _ringMat.EmissionEnergyMultiplier = 0f;
        if (_coreLight != null) _coreLight.LightEnergy = 0f;
        foreach (var m in _floorLampMats) if (m != null) m.EmissionEnergyMultiplier = 0f;
        _coreWobble = 0f;
    }

    // 폭발 — 멀쩡한 코어를 숨기고 손상된 코어를 꺼낸다. 전환은 섬광 · 연기가 가린다(§26).
    public void PrologueCoreBreach()
    {
        if (_coreDamaged == null) BuildDamagedCore();
        if (_core != null) _core.Visible = false;
        foreach (var r in _rings) if (r != null) r.Visible = false;
        if (_coreDamaged != null) _coreDamaged.Visible = true;
        if (_breachLight != null) _breachLight.LightEnergy = 5.5f;
        CoreHallFill(0.5f, new Color(1f, 0.55f, 0.32f));
        if (_coreSmoke != null) _coreSmoke.Emitting = true;
        if (_coreSparks != null) _coreSparks.Emitting = true;
    }

    public void PrologueCoreRestoreIntact()
    {
        if (_core != null) _core.Visible = true;
        foreach (var r in _rings) if (r != null) r.Visible = true;
        if (_coreDamaged != null) _coreDamaged.Visible = false;
        if (_breachLight != null) _breachLight.LightEnergy = 0f;
        if (_coreSmoke != null) _coreSmoke.Emitting = false;
        if (_coreSparks != null) _coreSparks.Emitting = false;
    }

    private Node3D _coreDamaged;
    private OmniLight3D _breachLight;
    private GpuParticles3D _coreSmoke, _coreSparks;

    public GpuParticles3D CoreSmoke => _coreSmoke;
    public GpuParticles3D CoreSparks => _coreSparks;

    // 손상된 코어 — 깨진 껍데기 조각 + 속에서 타는 빛. 실시간 파쇄는 하지 않는다(§26).
    private void BuildDamagedCore()
    {
        _coreDamaged = new Node3D { Name = "CoreDamaged", Position = CoreCenter, Visible = false };
        _setCore.AddChild(_coreDamaged);

        var shell = Mat(new Color(0.13f, 0.12f, 0.12f), 0.9f, 0.3f);
        var ember = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.08f, 0.03f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.38f, 0.1f),
            EmissionEnergyMultiplier = 2.2f,
        };
        // 속 — 꺼져 가는 불덩이.
        _coreDamaged.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 2.1f, Height = 4.2f, RadialSegments = 16, Rings = 10 },
            MaterialOverride = ember,
        });
        // 껍데기 조각 — 작게 여러 개. 크게 몇 개만 두면 검은 덩어리로 보인다.
        for (int i = 0; i < 16; i++)
        {
            double a = Mathf.Tau * i / 16.0;
            float r = 2.7f + (i % 3) * 0.25f;
            _coreDamaged.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.25f, 1.05f, 0.3f) },
                Position = new Vector3(Mathf.Cos((float)a) * r, Mathf.Sin((float)a * 2.3f) * 1.9f,
                    Mathf.Sin((float)a) * r),
                RotationDegrees = new Vector3(i * 23f, i * 41f, i * 17f),
                MaterialOverride = shell,
            });
        }
        // 부러진 링 조각.
        for (int i = 0; i < 3; i++)
            _coreDamaged.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 4.0f + i * 0.5f, OuterRadius = 4.35f + i * 0.5f, Rings = 24 },
                RotationDegrees = new Vector3(70f + i * 26f, i * 33f, 18f + i * 40f),
                MaterialOverride = shell,
            });

        _breachLight = new OmniLight3D
        {
            Position = Vector3.Zero,
            LightColor = new Color(1f, 0.42f, 0.16f),
            LightEnergy = 0f,
            OmniRange = 30f,
            ShadowEnabled = false,
        };
        _coreDamaged.AddChild(_breachLight);

        _coreSmoke = Smoke(_coreDamaged, Vector3.Zero, 7.0f, 46);
        _coreSparks = Sparks(_coreDamaged, Vector3.Zero, 5.5f, 70);
    }

    // ── 입자 ────────────────────────────────────────────────────────────

    public GpuParticles3D Smoke(Node3D parent, Vector3 pos, float scale, int amount)
    {
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 65f,
            InitialVelocityMin = scale * 0.24f,
            InitialVelocityMax = scale * 0.52f,
            Gravity = new Vector3(0f, 0.25f, 0f),
            ScaleMin = 0.6f * scale,
            ScaleMax = 1.5f * scale,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = scale * 0.35f,
        };
        var p = new GpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = 4.5,
            Emitting = false,
            ProcessMaterial = mat,
            DrawPass1 = new QuadMesh { Size = new Vector2(1f, 1f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.2f, 0.195f, 0.19f, 0.42f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                DisableReceiveShadows = true,
            },
        };
        parent.AddChild(p);
        return p;
    }

    public GpuParticles3D Sparks(Node3D parent, Vector3 pos, float scale, int amount)
    {
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0f, 0.3f, 0f),
            Spread = 180f,
            InitialVelocityMin = scale * 1.5f,
            InitialVelocityMax = scale * 4.5f,
            Gravity = new Vector3(0f, -9.0f, 0f),
            ScaleMin = 0.03f * scale,
            ScaleMax = 0.09f * scale,
            Damping = new Vector2(1.2f, 2.4f),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = scale * 0.3f,
        };
        var p = new GpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = 1.3,
            Explosiveness = 0.35f,
            Emitting = false,
            ProcessMaterial = mat,
            DrawPass1 = new BoxMesh { Size = new Vector3(0.06f, 0.06f, 0.3f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.82f, 0.45f),
                EmissionEnabled = true,
                Emission = new Color(1f, 0.75f, 0.3f),
                EmissionEnergyMultiplier = 5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        parent.AddChild(p);
        return p;
    }

    // 작은 파편(금속 · 콘크리트) — 폭발 순간 한 번에 터진다.
    public GpuParticles3D Debris(Node3D parent, Vector3 pos, float scale, int amount)
    {
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0f, 0.4f, 1f),
            Spread = 85f,
            InitialVelocityMin = 6f * scale,
            InitialVelocityMax = 18f * scale,
            Gravity = new Vector3(0f, -11f, 0f),
            ScaleMin = 0.05f * scale,
            ScaleMax = 0.2f * scale,
            AngularVelocityMin = -520f,
            AngularVelocityMax = 520f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = scale * 0.8f,
        };
        var p = new GpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = 2.6,
            Explosiveness = 1f,
            OneShot = true,
            Emitting = false,
            ProcessMaterial = mat,
            DrawPass1 = new BoxMesh { Size = new Vector3(0.22f, 0.12f, 0.3f) },
            MaterialOverride = Mat(new Color(0.3f, 0.3f, 0.32f), 0.6f, 0.5f),
        };
        parent.AddChild(p);
        return p;
    }

    // 코어실 기본광 — 엔딩에서는 '아직 복구 전'이라 어둡지만, 프롤로그의 정상 가동 중에는
    // 공간이 보여야 "거대하다" 가 읽힌다.
    public void CoreHallFill(float energy, Color? color = null)
    {
        var l = _setCore?.GetNodeOrNull<OmniLight3D>("HallFill");
        if (l == null) return;
        l.LightEnergy = energy;
        if (color.HasValue) l.LightColor = color.Value;
    }

    // 프롤로그 전용 바닥 보조광 — 정상 가동 중에는 난간 · 바닥 · 사람이 보여야 한다.
    private OmniLight3D _pCoreFloorFill;

    public void CoreFloorFill(float energy, Color? color = null)
    {
        if (_pCoreFloorFill == null)
        {
            _pCoreFloorFill = new OmniLight3D
            {
                Name = "PrologueCoreFloorFill",
                Position = new Vector3(0f, 3.4f, 6f),
                LightColor = new Color(0.66f, 0.74f, 0.88f),
                OmniRange = 30f,
                ShadowEnabled = false,
            };
            _setCore.AddChild(_pCoreFloorFill);
        }
        _pCoreFloorFill.LightEnergy = energy;
        if (color.HasValue) _pCoreFloorFill.LightColor = color.Value;
    }

    public Node3D CoreHallRoot => _setCore;
    public static readonly Vector3 CoreCenter = new(0f, 8.2f, -3f);
}
