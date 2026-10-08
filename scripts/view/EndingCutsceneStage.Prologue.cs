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
        AlbedoColor = new Color(0.03f, 0.028f, 0.026f, 0.16f),
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

    private readonly List<float> _pLampBase = new();

    // 조명이 한 번에 깜빡인다(재난 시작 · 연구실 충격). 0 = 꺼짐, 1 = 정상.
    //
    // 발광 재질을 전부 같은 값(2.6)으로 눌러 쓰면 **작고 가까운 등이 통째로 하얗게 탄다** —
    // 등마다 지어질 때의 세기가 다르므로 그 값을 기준으로 비례시킨다.
    public void PrologueLights(float k)
    {
        for (int i = 0; i < _pLights.Count; i++)
            if (_pLights[i] != null && i < _pLightBase.Count) _pLights[i].LightEnergy = _pLightBase[i] * k;
        for (int i = 0; i < _pLampMats.Count; i++)
            if (_pLampMats[i] != null && i < _pLampBase.Count)
                _pLampMats[i].EmissionEnergyMultiplier = _pLampBase[i] * k;
    }

    // **새로 늘어난 것만** 기준값으로 잡는다. 전부 다시 재면, 앞 장면에서 어둡게 눌러 둔
    // 등이 그 어두운 값을 '정상' 으로 기억해 버린다(세트는 처음 쓸 때 지연 생성된다).
    private void CaptureLightBase()
    {
        while (_pLightBase.Count < _pLights.Count)
            _pLightBase.Add(_pLights[_pLightBase.Count]?.LightEnergy ?? 0f);
        while (_pLampBase.Count < _pLampMats.Count)
            _pLampBase.Add(_pLampMats[_pLampBase.Count]?.EmissionEnergyMultiplier ?? 0f);
    }

    // ── ① 연구 구역 전경 (archive_01_facility) ───────────────────────────
    //
    // 밝고 · 정상이고 · 사람이 있고 · 기계가 돌아간다. 긴 홀 하나면 충분하다 —
    // 카메라가 느리게 밀고 들어가는 프레임 안만 설득력 있으면 된다(§5).
    //
    // **재질 언어 : 연구/설비 구역.** 바닥은 에폭시(통로만 닳았다), 벽은 허리 아래
    // 도장 콘크리트 · 위는 볼트로 물린 금속 패널, 기둥에는 하단 보호대와 경고 띠.
    // 전부 같은 회색 · 같은 거칠기로 두면 아무리 채워도 테스트맵으로 보인다.
    private void BuildArchiveHall()
    {
        _pArchive = new Node3D { Name = "PSetArchiveHall" };
        _world.AddChild(_pArchive);
        var rng = new RandomNumberGenerator { Seed = 5150 };

        var floor = Mat(new Color(0.272f, 0.284f, 0.304f), 0.40f, 0.10f);   // 에폭시 — 조금 반사한다
        var wall = Mat(new Color(0.306f, 0.318f, 0.338f), 0.92f);
        var steel = KSteel;
        var dark = KPanelDark;
        var screen = Glow(new Color(0.30f, 0.56f, 0.70f), 1.5f);

        Box(_pArchive, new Vector3(22f, 0.4f, 44f), new Vector3(0f, -0.2f, 0f), floor, "Floor");
        Box(_pArchive, new Vector3(22f, 7f, 0.4f), new Vector3(0f, 3.5f, -22f), wall, "WallBack");
        DressCorridorSurfaces(_pArchive, 11f, 7f, -22f, 22f, 3.6f);
        Box(_pArchive, new Vector3(0.4f, 7f, 44f), new Vector3(-11f, 3.5f, 0f), wall, "WallL");
        Box(_pArchive, new Vector3(0.4f, 7f, 44f), new Vector3(11f, 3.5f, 0f), wall, "WallR");
        Box(_pArchive, new Vector3(22f, 0.4f, 44f), new Vector3(0f, 7f, 0f), wall, "Ceiling");

        // 벽 마감 — 허리 아래 콘크리트 / 몰딩 / 금속 패널 + 볼트.
        DressWallPanels(_pArchive, new Vector3(-10.8f, 0f, 0f), 44f, 7f, false, 1f, 1.25f, 3.6f);
        DressWallPanels(_pArchive, new Vector3(10.8f, 0f, 0f), 44f, 7f, false, -1f, 1.25f, 3.6f);
        DressWallPanels(_pArchive, new Vector3(0f, 0f, -21.8f), 22f, 7f, true, 1f, 1.25f, 3.2f);

        // 바닥 — 통로만 닳아서 반사가 죽는다. "전부 같은 바닥" 을 깨는 가장 싼 방법.
        Box(_pArchive, new Vector3(6.6f, 0.004f, 42f), new Vector3(0f, 0.016f, 0f), KEpoxyWorn, "Walkway");
        for (int i = 0; i < 12; i++)
        {
            // 통로 한가운데가 아니라 **설비 앞** 이 닳는다 — 가운데에 두면 깔아 놓은 판처럼 보인다.
            float sx = i % 2 == 0 ? -1f : 1f;
            Box(_pArchive, new Vector3(rng.RandfRange(0.9f, 2.4f), 0.003f, rng.RandfRange(0.9f, 2.2f)),
                new Vector3(sx * rng.RandfRange(4.6f, 9.4f), 0.02f, rng.RandfRange(-20f, 20f)),
                Stain, "FloorStain");
        }
        // 바닥 유도선 — 공간의 깊이를 읽게 한다.
        var line = Glow(new Color(0.52f, 0.43f, 0.14f), 0.5f);
        Box(_pArchive, new Vector3(0.14f, 0.02f, 42f), new Vector3(-3.2f, 0.024f, 0f), line);
        Box(_pArchive, new Vector3(0.14f, 0.02f, 42f), new Vector3(3.2f, 0.024f, 0f), line);
        // 구역 경계 — 바닥에 찍힌 노란 빗금. 통로에 선 사람의 크기를 읽게 한다.
        foreach (float z in new[] { 11f, -3f, -17f })
            WarnStripe(_pArchive, new Vector3(0f, 0.028f, z), 6.6f, 0.26f, 0.004f, new Vector3(90f, 0f, 0f), 11);

        // ── 구조 기둥 : 벽을 따라 여섯 쌍 ───────────────────────────────
        for (int i = 0; i < 6; i++)
        {
            float z = 16f - i * 7.2f;
            foreach (float side in new[] { -1f, 1f })
            {
                Box(_pArchive, new Vector3(0.78f, 7f, 0.78f), new Vector3(side * 10.2f, 3.5f, z),
                    KConcreteDark, "Pillar");
                DressPillar(_pArchive, new Vector3(side * 10.2f, 0f, z), 0.78f, 7f, side > 0f ? 180f : 0f);
            }
        }

        // ── 천장 ────────────────────────────────────────────────────────
        for (int i = 0; i < 9; i++)
        {
            float z = 18f - i * 4.6f;
            Strip(_pArchive, new Vector3(-4.4f, 6.6f, z), 3.4f, Cool, 2.6f, _pLampMats, _pLights, 2.0f);
            Strip(_pArchive, new Vector3(4.4f, 6.6f, z), 3.4f, Cool, 2.6f, _pLampMats, _pLights, 2.0f);
            // 조명을 매단 행거 — 등이 천장에 그냥 붙어 있으면 평면으로 보인다.
            foreach (float x in new[] { -4.4f, 4.4f })
                foreach (float dz in new[] { -1.3f, 1.3f })
                    Pipe(_pArchive, new Vector3(x, 6.95f, z + dz), new Vector3(x, 6.66f, z + dz),
                        0.022f, KSteelDark, "Hanger", 4);
            // 천장 보 — 가로 방향 구조.
            Box(_pArchive, new Vector3(21.6f, 0.26f, 0.3f), new Vector3(0f, 6.82f, z + 2.3f), KPanelDark, "Beam");
        }
        // 배관 · 덕트 · 케이블 트레이 — 굵기와 높이를 전부 다르게 둔다.
        Duct(_pArchive, new Vector3(-2.0f, 6.25f, -21f), new Vector3(-2.0f, 6.25f, 21f), 0.95f, 0.72f,
            KPanelDark, KSteelDark, 4.2f);
        CableTray(_pArchive, new Vector3(2.4f, 6.3f, -21f), new Vector3(2.4f, 6.3f, 21f), 0.7f, KSteelDark, KCable);
        foreach (var (x, r) in new[] { (-3.6f, 0.17f), (-3.15f, 0.1f), (3.6f, 0.21f), (4.3f, 0.13f) })
            Pipe(_pArchive, new Vector3(x, 6.05f, -21f), new Vector3(x, 6.05f, 21f), r, steel, "Pipe", 8);
        // 배관 행거 · 플랜지.
        for (int i = -5; i <= 5; i++)
            foreach (var x in new[] { -3.6f, 3.6f })
            {
                Pipe(_pArchive, new Vector3(x, 6.75f, i * 3.8f), new Vector3(x, 6.2f, i * 3.8f), 0.025f,
                    KSteelDark, "PipeHanger", 4);
                Cyl(_pArchive, 0.24f, 0.07f, new Vector3(x, 6.05f, i * 3.8f), KSteelDark, "Flange")
                    .RotationDegrees = new Vector3(90f, 0f, 0f);
            }
        // 통로 위에 매달린 구역 표지판.
        foreach (var z in new[] { 8f, -6f })
        {
            var sign = new Node3D { Position = new Vector3(0f, 4.9f, z) };
            _pArchive.AddChild(sign);
            foreach (float sx in new[] { -1.1f, 1.1f })
                Pipe(sign, new Vector3(sx, 0.3f, 0f), new Vector3(sx, 1.9f, 0f), 0.02f, KSteelDark, "SignWire", 4);
            Box(sign, new Vector3(2.6f, 0.52f, 0.06f), Vector3.Zero, KPanelDark, "SignBoard");
            Box(sign, new Vector3(2.68f, 0.06f, 0.09f), new Vector3(0f, 0.28f, 0f), KAlu, "SignCap");
            Box(sign, new Vector3(1.5f, 0.14f, 0.015f), new Vector3(-0.3f, 0.02f, 0.04f), KLabel, "SignText");
            Box(sign, new Vector3(0.3f, 0.3f, 0.02f), new Vector3(0.95f, 0.0f, 0.04f),
                Glow(new Color(0.42f, 0.56f, 0.24f), 0.8f), "SignMark");
        }

        // ── 양옆 연구 설비 ──────────────────────────────────────────────
        for (int i = 0; i < 7; i++)
        {
            float z = 15f - i * 5f;
            foreach (float side in new[] { -1f, 1f })
            {
                var bay = new Node3D { Position = new Vector3(side * 8.2f, 0f, z) };
                _pArchive.AddChild(bay);
                // 콘솔 — 모서리를 깎은 몸체 + 비스듬한 상판 + 하단 받침.
                Box(bay, new Vector3(2.7f, 0.12f, 3.1f), new Vector3(0f, 0.06f, 0f), KSteelDark, "Plinth");
                Chamfer(bay, new Vector3(2.6f, 1.0f, 3.0f), new Vector3(0f, 0.62f, 0f), dark, 0.03f, "Console");
                var top = Box(bay, new Vector3(2.46f, 0.1f, 1.5f), new Vector3(0f, 1.16f, -side * 0.62f),
                    KPanel, "ConsoleTop");
                top.RotationDegrees = new Vector3(side > 0f ? 16f : -16f, 0f, 0f);
                Box(bay, new Vector3(2.5f, 0.06f, 2.9f), new Vector3(0f, 1.15f, 0.1f), KPanelDark, "Deck");
                // 상판 위 조작부 — 버튼 줄 · 노브.
                for (int k = 0; k < 6; k++)
                    Box(top, new Vector3(0.14f, 0.03f, 0.14f), new Vector3(-0.9f + k * 0.36f, 0.06f, 0.2f),
                        k % 2 == 0 ? KAlu : KBrass, "Key");
                Cyl(top, 0.09f, 0.05f, new Vector3(0.95f, 0.07f, -0.2f), KAlu, "Knob");
                // 화면 — 베젤이 있어야 '모니터' 다.
                var mon = new Node3D
                {
                    Position = new Vector3(-side * 1.15f, 1.78f, 0f),
                    RotationDegrees = new Vector3(0f, side > 0f ? -90f : 90f, 0f),
                };
                bay.AddChild(mon);
                Chamfer(mon, new Vector3(1.8f, 1.1f, 0.26f), Vector3.Zero, KPlastic, 0.025f, "MonCase");
                Box(mon, new Vector3(1.66f, 0.96f, 0.03f), new Vector3(0f, 0f, 0.14f), KPlasticDark, "MonBezel");
                Box(mon, new Vector3(1.5f, 0.82f, 0.02f), new Vector3(0f, 0f, 0.16f), screen, "MonScreen");
                Louver(mon, new Vector3(0f, 0.6f, 0.02f), 0.9f, 0.16f, new Vector3(90f, 0f, 0f), 4);
                Box(mon, new Vector3(0.3f, 0.5f, 0.3f), new Vector3(0f, -0.72f, -0.05f), KSteelDark, "MonStand");
                // 설비 랙 — 통풍구와 표시등이 줄줄이.
                var rack = new Node3D { Position = new Vector3(side * 0.85f, 0f, -1.25f) };
                bay.AddChild(rack);
                Chamfer(rack, new Vector3(0.9f, 2.3f, 0.8f), new Vector3(0f, 1.15f, 0f), KPanel, 0.02f, "Rack");
                for (int u = 0; u < 7; u++)
                {
                    Box(rack, new Vector3(0.84f, 0.26f, 0.04f), new Vector3(0f, 0.35f + u * 0.28f, 0.41f),
                        KPanelDark, "RackUnit");
                    Box(rack, new Vector3(0.04f, 0.04f, 0.02f), new Vector3(0.3f, 0.35f + u * 0.28f, 0.44f),
                        Glow(u % 3 == 0 ? new Color(0.30f, 0.72f, 0.40f) : new Color(0.60f, 0.46f, 0.16f), 1.6f),
                        "RackLed");
                }
                Louver(rack, new Vector3(0f, 2.18f, 0.2f), 0.6f, 0.2f, new Vector3(90f, 0f, 0f), 4);
                CableSag(rack, new Vector3(0f, 0.2f, -0.4f), new Vector3(side * 0.9f, 0.08f, -0.9f),
                    0.08f, 0.03f, KCable, 4);
                // 탱크 — 둘 걸러 하나. 받침 · 밴드 · 배관까지.
                if (i % 2 == 0)
                {
                    var tk = new Node3D { Position = new Vector3(side * 1.5f, 0f, 2.0f) };
                    bay.AddChild(tk);
                    Cyl(tk, 0.62f, 3.2f, new Vector3(0f, 1.75f, 0f), KAlu, "Tank");
                    Cyl(tk, 0.70f, 0.18f, new Vector3(0f, 0.22f, 0f), KSteelDark, "TankBase");
                    foreach (float y in new[] { 1.0f, 2.3f })
                        Cyl(tk, 0.66f, 0.12f, new Vector3(0f, y, 0f), KSteelDark, "TankBand");
                    Pipe(tk, new Vector3(0f, 3.35f, 0f), new Vector3(0f, 6.0f, 0f), 0.11f, steel, "TankPipe", 8);
                    Pipe(tk, new Vector3(0f, 0.6f, 0.6f), new Vector3(0f, 0.6f, 1.6f), 0.08f, steel, "TankPipe", 8);
                    Label(tk, new Vector3(-side * 0.63f, 1.9f, 0f), new Vector2(0.34f, 0.46f),
                        new Vector3(0f, side > 0f ? -90f : 90f, 0f));
                }
                // 콘솔 앞면 표시등 — 통로에서 보이는 작은 색점이 공간을 살아 있게 만든다.
                Box(bay, new Vector3(0.1f, 0.1f, 0.5f), new Vector3(-side * 1.26f, 1.0f, -0.9f),
                    Glow(new Color(0.22f, 0.62f, 0.35f), 2.2f));
                Box(bay, new Vector3(0.1f, 0.1f, 0.3f), new Vector3(-side * 1.26f, 0.78f, 0.6f),
                    Glow(new Color(0.62f, 0.45f, 0.16f), 1.8f));
                // 경고 라벨 · 베이 번호.
                Label(bay, new Vector3(-side * 1.32f, 1.35f, 1.2f), new Vector2(0.4f, 0.22f),
                    new Vector3(0f, side > 0f ? -90f : 90f, 0f));
                WarnStripe(bay, new Vector3(-side * 1.33f, 0.2f, 0f), 2.6f, 0.16f, 0.012f,
                    new Vector3(0f, side > 0f ? -90f : 90f, 0f), 9);
            }
        }

        // ── 통로 소품 : 공간이 실제로 어떻게 쓰이는지 드러나는 것들 ─────
        foreach (var (x, z, yaw) in new[] { (-6.3f, 18.5f, 10f), (6.5f, -14.5f, -14f) })
        {
            var gasRack = new Node3D { Position = new Vector3(x, 0f, z), RotationDegrees = new Vector3(0f, yaw, 0f) };
            _pArchive.AddChild(gasRack);
            Box(gasRack, new Vector3(1.9f, 0.1f, 0.7f), new Vector3(0f, 0.05f, 0f), KSteelDark, "RackFloor");
            Pipe(gasRack, new Vector3(-0.9f, 0.1f, 0f), new Vector3(-0.9f, 1.5f, 0f), 0.035f, KSteelDark, "Post", 6);
            Pipe(gasRack, new Vector3(0.9f, 0.1f, 0f), new Vector3(0.9f, 1.5f, 0f), 0.035f, KSteelDark, "Post", 6);
            Pipe(gasRack, new Vector3(-0.9f, 1.1f, 0f), new Vector3(0.9f, 1.1f, 0f), 0.03f, KSteelDark, "Bar", 6);
            for (int k = 0; k < 4; k++)
            {
                Cyl(gasRack, 0.17f, 1.35f, new Vector3(-0.62f + k * 0.41f, 0.78f, 0f),
                    k % 2 == 0 ? Mat(new Color(0.22f, 0.26f, 0.30f), 0.42f, 0.8f) : KAlu, "Cylinder");
                Cyl(gasRack, 0.07f, 0.16f, new Vector3(-0.62f + k * 0.41f, 1.52f, 0f), KBrass, "Valve");
            }
        }
        // 팔레트에 쌓인 상자 · 드럼통 · 손수레.
        foreach (var (x, z, yaw, n) in new[] { (-7.2f, 6.5f, 8f, 3), (7.4f, 9.5f, -22f, 2), (-7.6f, -10.5f, -5f, 4) })
        {
            var pal = new Node3D { Position = new Vector3(x, 0f, z), RotationDegrees = new Vector3(0f, yaw, 0f) };
            _pArchive.AddChild(pal);
            Box(pal, new Vector3(1.3f, 0.12f, 1.1f), new Vector3(0f, 0.06f, 0f), KWood, "Pallet");
            for (int k = 0; k < n; k++)
                Chamfer(pal, new Vector3(0.9f, 0.5f, 0.8f),
                    new Vector3(rng.RandfRange(-0.1f, 0.1f), 0.37f + k * 0.52f, rng.RandfRange(-0.08f, 0.08f)),
                    k % 2 == 0 ? KFoam : KPlastic, 0.015f, "Crate");
        }
        foreach (var (x, z) in new[] { (-9.3f, 2.5f), (-9.3f, 3.4f), (9.4f, -6.5f) })
        {
            Cyl(_pArchive, 0.33f, 0.92f, new Vector3(x, 0.46f, z),
                Mat(new Color(0.20f, 0.19f, 0.17f), 0.78f), "Drum");
            foreach (float y in new[] { 0.28f, 0.64f })
                Cyl(_pArchive, 0.35f, 0.06f, new Vector3(x, y, z), KSteelDark, "DrumRib");
            Label(_pArchive, new Vector3(x, 0.5f, z + 0.34f), new Vector2(0.3f, 0.22f), Vector3.Zero);
        }
        // 손수레 — 사람이 쓰는 물건이 하나 있으면 공간이 '운영 중' 이 된다.
        var cart = new Node3D { Position = new Vector3(5.6f, 0f, 3.5f), RotationDegrees = new Vector3(0f, 118f, 0f) };
        _pArchive.AddChild(cart);
        Box(cart, new Vector3(0.9f, 0.05f, 0.62f), new Vector3(0f, 0.78f, 0f), KPanel, "CartTop");
        Box(cart, new Vector3(0.9f, 0.05f, 0.62f), new Vector3(0f, 0.36f, 0f), KPanel, "CartShelf");
        foreach (float sx in new[] { -0.4f, 0.4f })
            foreach (float sz in new[] { -0.26f, 0.26f })
            {
                Pipe(cart, new Vector3(sx, 0.12f, sz), new Vector3(sx, 0.8f, sz), 0.022f, KAlu, "CartLeg", 6);
                Cyl(cart, 0.07f, 0.04f, new Vector3(sx, 0.07f, sz), KRubber, "Caster")
                    .RotationDegrees = new Vector3(0f, 0f, 90f);
            }
        Pipe(cart, new Vector3(-0.45f, 0.8f, 0.3f), new Vector3(-0.45f, 1.02f, 0.34f), 0.02f, KAlu, "CartHandle", 6);
        Pipe(cart, new Vector3(0.45f, 0.8f, 0.3f), new Vector3(0.45f, 1.02f, 0.34f), 0.02f, KAlu, "CartHandle", 6);
        Pipe(cart, new Vector3(-0.45f, 1.02f, 0.34f), new Vector3(0.45f, 1.02f, 0.34f), 0.02f, KAlu, "CartBar", 6);
        FileBox(cart, new Vector3(-0.2f, 0.805f, 0f), 12f);
        PaperStack(cart, new Vector3(0.25f, 0.805f, 0.05f), -8f, 7, 311);

        // ── 안쪽 끝 : 관측창 ────────────────────────────────────────────
        // 빛나는 상자 하나가 아니라 틀 · 멀리언 · 아래 난간이 있는 창.
        var win = new Node3D { Position = new Vector3(0f, 3.4f, -21.7f) };
        _pArchive.AddChild(win);
        // 틀은 유리 **둘레**로만 두른다 — 통짜 상자로 덮으면 카메라 쪽에서 유리가 가려진다.
        Box(win, new Vector3(9.6f, 0.36f, 0.34f), new Vector3(0f, 1.72f, 0f), KSteelDark, "WinFrameT");
        Box(win, new Vector3(9.6f, 0.36f, 0.34f), new Vector3(0f, -1.72f, 0f), KSteelDark, "WinFrameB");
        Box(win, new Vector3(0.4f, 3.8f, 0.34f), new Vector3(-4.6f, 0f, 0f), KSteelDark, "WinFrameL");
        Box(win, new Vector3(0.4f, 3.8f, 0.34f), new Vector3(4.6f, 0f, 0f), KSteelDark, "WinFrameR");
        Box(win, new Vector3(8.8f, 3.0f, 0.1f), Vector3.Zero, Glow(new Color(0.56f, 0.72f, 0.84f), 1.4f), "WinGlass");
        for (int i = -2; i <= 2; i++)
            Box(win, new Vector3(0.16f, 3.1f, 0.16f), new Vector3(i * 1.76f, 0f, -0.02f), KSteelDark, "Mullion");
        Box(win, new Vector3(8.9f, 0.14f, 0.18f), new Vector3(0f, 0f, -0.02f), KSteelDark, "MullionH");
        Box(win, new Vector3(9.8f, 0.2f, 0.4f), new Vector3(0f, -2.0f, 0.18f), KAlu, "WinSill");
        for (int i = -4; i <= 4; i++)
            Pipe(_pArchive, new Vector3(i * 1.1f, 1.4f, -21.0f), new Vector3(i * 1.1f, 2.5f, -21.0f),
                0.035f, KSteelDark, "GuardPost", 6);
        Pipe(_pArchive, new Vector3(-4.6f, 2.5f, -21.0f), new Vector3(4.6f, 2.5f, -21.0f),
            0.045f, KAlu, "GuardRail", 6);

        // 홀 보조광 — 천장등만으로는 44 m 짜리 홀이 안쪽부터 까맣게 죽는다.
        foreach (var (z, e) in new[] { (14f, 1.5f), (2f, 1.6f), (-10f, 1.7f), (-19f, 2.0f) })
        {
            var fill = new OmniLight3D
            {
                Name = $"HallFill{z:0}", Position = new Vector3(0f, 4.6f, z),
                LightColor = new Color(0.60f, 0.66f, 0.78f), LightEnergy = e,
                OmniRange = 17f, ShadowEnabled = false,
            };
            _pArchive.AddChild(fill);
            _pLights.Add(fill);
        }

        CaptureLightBase();
    }

    // ── ② 총괄 관리자 사무실 (archive_02_director) ───────────────────────
    //
    // "시설 홍보 기록영상에 나오는 총괄 관리자 사무실" 로 읽혀야 한다. 비어 있으면
    // 세트장이 되고, 어지르면 권위가 사라진다 — 정돈됐지만 **실제로 근무하는 방**이다.
    //
    // 뒤쪽에서 혼자 빛나던 가로 막대는 없앴다. 그 자리에는 기관 명판 · 환기 그릴이 들어가고,
    // 빛나는 것(배치도 보드 · 단말)은 양옆으로 밀어 관리자의 실루엣이 죽지 않게 한다.
    //
    // 바닥에 **비워 둬야 하는 자리**가 둘 있다(§35) :
    //   · 관리자가 레버까지 걸어가는 길    x -2.3~0.3 / z -2.8~-0.8
    //   · 비상 차폐 패널이 붙는 왼쪽 벽    x = -2.6  / z -2.9~-1.7
    private void BuildBriefing()
    {
        _pBriefing = new Node3D { Name = "PSetBriefing" };
        _world.AddChild(_pBriefing);

        const float HX = 2.6f, HZ = 3.4f, H = 3.0f;

        // 바닥은 왁스 먹인 비닐 타일 — 벽(거친 도장)과 반사도가 분명히 다르다.
        var floor = Mat(new Color(0.168f, 0.174f, 0.190f), 0.44f, 0.08f);
        var wall = Mat(new Color(0.148f, 0.152f, 0.166f), 0.93f);

        Box(_pBriefing, new Vector3(HX * 2f, 0.3f, HZ * 2f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pBriefing, new Vector3(HX * 2f, H, 0.3f), new Vector3(0f, H * 0.5f, -HZ), wall, "WallBack");
        Box(_pBriefing, new Vector3(0.3f, H, HZ * 2f), new Vector3(-HX, H * 0.5f, 0f), wall, "WallL");
        Box(_pBriefing, new Vector3(0.3f, H, HZ * 2f), new Vector3(HX, H * 0.5f, 0f), wall, "WallR");
        Box(_pBriefing, new Vector3(HX * 2f, 0.3f, HZ * 2f), new Vector3(0f, H, 0f), wall, "Ceiling");
        DressRoomSurfaces(_pBriefing, HX, HZ, H, Vector3.Zero, 1.7f);

        // 벽 마감 — 아래는 도장 콘크리트, 허리에 금속 몰딩, 위는 볼트로 물린 금속 패널.
        DressWallPanels(_pBriefing, new Vector3(0f, 0f, -HZ), HX * 2f, H, true, 1f, 1.05f, 1.6f);
        DressWallPanels(_pBriefing, new Vector3(-HX, 0f, 0f), HZ * 2f, H, false, 1f, 1.05f, 1.7f);
        DressWallPanels(_pBriefing, new Vector3(HX, 0f, 0f), HZ * 2f, H, false, -1f, 1.05f, 1.7f);

        // 바닥 타일 줄눈 — 넓은 색면이 아니라 '깔린 바닥' 으로 보이게.
        var seamDark = Mat(new Color(0.105f, 0.108f, 0.118f), 0.88f);
        for (int i = -4; i <= 4; i++)
        {
            Box(_pBriefing, new Vector3(0.018f, 0.004f, HZ * 2f), new Vector3(i * 0.62f, 0.012f, 0f), seamDark);
            Box(_pBriefing, new Vector3(HX * 2f, 0.004f, 0.018f), new Vector3(0f, 0.012f, i * 0.72f), seamDark);
        }
        // 책상 앞 · 레버로 가는 길목이 닳아 있다 — 사람이 다니는 자리만 반사가 죽는다.
        Box(_pBriefing, new Vector3(1.9f, 0.003f, 1.1f), new Vector3(0f, 0.016f, -1.25f), Stain, "Worn");
        Box(_pBriefing, new Vector3(1.3f, 0.003f, 1.5f), new Vector3(-1.6f, 0.016f, -2.2f), Stain, "Worn");

        // ── 천장 : 보 · 매입 조명 · 배선 ────────────────────────────────
        for (int i = -2; i <= 2; i++)
            Box(_pBriefing, new Vector3(HX * 2f, 0.14f, 0.16f), new Vector3(0f, H - 0.08f, i * 1.3f),
                KPanelDark, "CeilBeam");
        foreach (float z in new[] { 0.55f, -1.55f })
        {
            // 매입 조명 트로퍼 — 테두리는 알루미늄, 안쪽만 빛난다.
            Box(_pBriefing, new Vector3(1.55f, 0.07f, 0.46f), new Vector3(0.1f, H - 0.17f, z), KAlu, "Troffer");
            Strip(_pBriefing, new Vector3(0.1f, H - 0.21f, z), 1.05f, new Color(0.74f, 0.735f, 0.715f), 1.1f,
                _pLampMats, _pLights, 0.70f);
        }
        Pipe(_pBriefing, new Vector3(-HX + 0.2f, H - 0.22f, -HZ + 0.3f),
            new Vector3(-HX + 0.2f, H - 0.22f, HZ - 0.4f), 0.045f, KSteelDark, "Conduit");
        CableSag(_pBriefing, new Vector3(-HX + 0.2f, H - 0.26f, -2.3f), new Vector3(-HX + 0.12f, 1.75f, -2.3f),
            0.04f, 0.022f, KCable, 3);

        // ── 뒷벽 : 기관 명판 · 환기 그릴 (관리자 바로 뒤) ───────────────
        // 관리자 머리 뒤는 **어둡고 조용해야** 한다. 빛나는 것은 전부 좌우로.
        Box(_pBriefing, new Vector3(1.5f, 0.86f, 0.05f), new Vector3(0f, 2.08f, -HZ + 0.18f),
            Mat(new Color(0.082f, 0.085f, 0.095f), 0.72f, 0.35f), "Plaque");
        Box(_pBriefing, new Vector3(1.58f, 0.045f, 0.065f), new Vector3(0f, 2.53f, -HZ + 0.19f), KAlu, "PlaqueCap");
        Box(_pBriefing, new Vector3(1.58f, 0.045f, 0.065f), new Vector3(0f, 1.63f, -HZ + 0.19f), KAlu, "PlaqueSill");
        // 명판 안의 기관 표식 — 금속 각인처럼 아주 약하게만 반사한다.
        Cyl(_pBriefing, 0.19f, 0.02f, new Vector3(-0.44f, 2.12f, -HZ + 0.22f), KBrass, "CrestRing")
            .RotationDegrees = new Vector3(90f, 0f, 0f);
        Cyl(_pBriefing, 0.155f, 0.026f, new Vector3(-0.44f, 2.12f, -HZ + 0.22f),
            Mat(new Color(0.075f, 0.078f, 0.088f), 0.80f), "CrestFace")
            .RotationDegrees = new Vector3(90f, 0f, 0f);
        for (int i = 0; i < 3; i++)
            Box(_pBriefing, new Vector3(0.52f - i * 0.11f, 0.038f, 0.014f),
                new Vector3(0.26f, 2.28f - i * 0.15f, -HZ + 0.21f),
                Mat(new Color(0.235f, 0.240f, 0.250f), 0.55f, 0.6f), "CrestText");
        Louver(_pBriefing, new Vector3(1.12f, 2.62f, -HZ + 0.17f), 0.62f, 0.30f, Vector3.Zero, 5);
        Louver(_pBriefing, new Vector3(-1.12f, 2.62f, -HZ + 0.17f), 0.62f, 0.30f, Vector3.Zero, 5);

        // ── 뒷벽 왼쪽 : 서류 캐비닛 · 파일 박스 · 선반 ──────────────────
        var cab = new Node3D { Position = new Vector3(-1.05f, 0f, -HZ + 0.42f) };
        _pBriefing.AddChild(cab);
        Chamfer(cab, new Vector3(0.95f, 1.32f, 0.56f), new Vector3(0f, 0.66f, 0f), KPanel, 0.018f, "Cabinet");
        for (int i = 0; i < 3; i++)
        {
            Box(cab, new Vector3(0.88f, 0.012f, 0.014f), new Vector3(0f, 0.22f + i * 0.40f, 0.282f),
                KSteelDark, "DrawerSeam");
            Box(cab, new Vector3(0.20f, 0.034f, 0.038f), new Vector3(0f, 0.33f + i * 0.40f, 0.29f), KAlu, "Handle");
            Box(cab, new Vector3(0.13f, 0.055f, 0.006f), new Vector3(-0.28f, 0.33f + i * 0.40f, 0.285f),
                KPaper, "Tag");
        }
        FileBox(cab, new Vector3(-0.21f, 1.325f, 0.02f), -6f);
        FileBox(cab, new Vector3(0.24f, 1.325f, -0.03f), 9f);
        FileBox(cab, new Vector3(0.24f, 1.615f, -0.01f), 3f);
        // 그 위 벽 선반 — 서류철이 꽂혀 있다.
        Box(_pBriefing, new Vector3(1.25f, 0.045f, 0.30f), new Vector3(-1.05f, 2.06f, -HZ + 0.33f), KAlu, "Shelf");
        foreach (float sx in new[] { -0.56f, 0.56f })
            Box(_pBriefing, new Vector3(0.035f, 0.26f, 0.26f), new Vector3(-1.05f + sx, 1.92f, -HZ + 0.31f),
                KSteelDark, "ShelfBracket");
        Binders(_pBriefing, new Vector3(-1.59f, 2.085f, -HZ + 0.33f), 0f, 9, 4411);
        Clipboard(_pBriefing, new Vector3(-2.52f, 1.52f, -HZ + 0.22f), new Vector3(84f, 0f, 3f));

        // ── 뒷벽 오른쪽 : 시설 배치도 보드 + 산업용 단말 ────────────────
        // 도면은 '파란 빛 덩어리' 가 아니라 테두리 · 격자 · 구획이 보이는 **판**이다.
        var board = new Node3D { Position = new Vector3(1.62f, 1.92f, -HZ + 0.17f) };
        _pBriefing.AddChild(board);
        Box(board, new Vector3(1.46f, 1.02f, 0.06f), Vector3.Zero, KSteelDark, "BoardFrame");
        Box(board, new Vector3(1.34f, 0.90f, 0.03f), new Vector3(0f, 0f, 0.035f),
            Mat(new Color(0.055f, 0.075f, 0.098f), 0.55f), "BoardFace");
        var blue = Glow(new Color(0.105f, 0.190f, 0.255f), 0.12f);
        for (int i = 0; i < 6; i++)
            Box(board, new Vector3(1.26f, 0.008f, 0.008f), new Vector3(0f, -0.36f + i * 0.145f, 0.052f), blue);
        for (int i = 0; i < 9; i++)
            Box(board, new Vector3(0.008f, 0.82f, 0.008f), new Vector3(-0.56f + i * 0.14f, 0f, 0.052f), blue);
        // 구획 — 도면처럼 몇 칸만 굵게 둘러친다.
        var blue2 = Glow(new Color(0.185f, 0.330f, 0.430f), 0.26f);
        foreach (var (x, y, w, h2) in new[]
                 { (-0.34f, 0.16f, 0.44f, 0.30f), (0.22f, 0.16f, 0.30f, 0.30f), (-0.10f, -0.26f, 0.72f, 0.22f) })
        {
            Box(board, new Vector3(w, 0.012f, 0.01f), new Vector3(x, y + h2 * 0.5f, 0.055f), blue2);
            Box(board, new Vector3(w, 0.012f, 0.01f), new Vector3(x, y - h2 * 0.5f, 0.055f), blue2);
            Box(board, new Vector3(0.012f, h2, 0.01f), new Vector3(x - w * 0.5f, y, 0.055f), blue2);
            Box(board, new Vector3(0.012f, h2, 0.01f), new Vector3(x + w * 0.5f, y, 0.055f), blue2);
        }
        Box(board, new Vector3(1.40f, 0.05f, 0.09f), new Vector3(0f, 0.56f, 0.01f), KAlu, "BoardHood");
        // 보드 전용 벽등 — 도면만 밝다.
        _pBriefing.AddChild(new SpotLight3D
        {
            Position = new Vector3(1.62f, 2.52f, -HZ + 0.45f),
            RotationDegrees = new Vector3(-62f, 0f, 0f),
            LightColor = new Color(0.72f, 0.80f, 0.96f), LightEnergy = 0.85f,
            SpotRange = 3.2f, SpotAngle = 46f, SpotAngleAttenuation = 1.1f, ShadowEnabled = false,
        });

        // 사이드 테이블 위의 단말 둘 — 하나는 꺼져 있다(꺼진 CRT 도 소품이다).
        var credenza = new Node3D { Position = new Vector3(1.72f, 0f, -HZ + 0.48f) };
        _pBriefing.AddChild(credenza);
        Chamfer(credenza, new Vector3(1.50f, 0.74f, 0.62f), new Vector3(0f, 0.37f, 0f), KPanel, 0.016f, "Credenza");
        Box(credenza, new Vector3(1.54f, 0.04f, 0.66f), new Vector3(0f, 0.755f, 0f), KSteelDark, "CredenzaTop");
        Box(credenza, new Vector3(0.70f, 0.012f, 0.015f), new Vector3(-0.36f, 0.42f, 0.312f), KSteelDark, "DoorSeam");
        Box(credenza, new Vector3(0.70f, 0.012f, 0.015f), new Vector3(0.36f, 0.42f, 0.312f), KSteelDark, "DoorSeam");
        Terminal(credenza, new Vector3(-0.38f, 0.775f, 0.02f), 8f, 1.05f, new Color(0.30f, 0.62f, 0.52f), 0.62f);
        Terminal(credenza, new Vector3(0.40f, 0.775f, -0.02f), -12f, 0.95f, new Color(0.62f, 0.42f, 0.18f), 0f);
        CableSag(credenza, new Vector3(-0.38f, 0.78f, -0.18f), new Vector3(-0.1f, 0.42f, -0.33f), 0.08f, 0.012f, KCable, 4);
        CableSag(credenza, new Vector3(0.40f, 0.78f, -0.18f), new Vector3(0.12f, 0.46f, -0.33f), 0.07f, 0.010f, KCable, 4);

        // ── 뒷벽 왼쪽 구석 : 벽 제어 패널 · 배선 (카메라 프레임 안) ─────
        // 오른쪽 벽의 패널은 이 샷의 화각 밖이라 안 보인다. 프레임 안에도 하나 둔다 —
        // 관리자 사무실에도 시설 설비가 들어와 있어야 '시설 안의 방' 으로 읽힌다.
        WallPanel(_pBriefing, new Vector3(-2.18f, 1.58f, -HZ + 0.12f), Vector3.Zero,
            0.70f, 0.56f, new Color(0.30f, 0.66f, 0.92f), 0.8f, _pLampMats, 313);
        Pipe(_pBriefing, new Vector3(-2.18f, 1.90f, -HZ + 0.10f), new Vector3(-2.18f, H - 0.3f, -HZ + 0.10f),
            0.026f, KSteelDark, "PanelRiser");
        CableSag(_pBriefing, new Vector3(-2.0f, 1.86f, -HZ + 0.12f), new Vector3(-1.62f, 1.42f, -HZ + 0.12f),
            0.08f, 0.012f, KCable, 3);
        Label(_pBriefing, new Vector3(-2.18f, 1.14f, -HZ + 0.11f), new Vector2(0.34f, 0.12f), Vector3.Zero);

        // 휴지통 · 바닥 배선 — 책상 앞 바닥이 비어 보이지 않게.
        Cyl(_pBriefing, 0.17f, 0.42f, new Vector3(-1.28f, 0.21f, -1.55f), KPlasticDark, "Bin");
        Cyl(_pBriefing, 0.185f, 0.035f, new Vector3(-1.28f, 0.43f, -1.55f), KSteelDark, "BinRim");
        Box(_pBriefing, new Vector3(0.9f, 0.025f, 0.12f), new Vector3(-0.3f, 0.025f, -2.05f), KRubber, "CableRamp");

        // ── 오른쪽 벽 : 벽 제어 패널 · 케이블 · 경고 라벨 ───────────────
        WallPanel(_pBriefing, new Vector3(HX - 0.08f, 1.62f, -0.55f), new Vector3(0f, -90f, 0f),
            0.86f, 0.64f, new Color(0.35f, 0.75f, 1f), 0.9f, _pLampMats, 991);
        Pipe(_pBriefing, new Vector3(HX - 0.06f, 2.26f, -0.55f), new Vector3(HX - 0.06f, H - 0.3f, -0.55f),
            0.03f, KSteelDark, "PanelRiser");
        Label(_pBriefing, new Vector3(HX - 0.07f, 2.42f, -1.35f), new Vector2(0.42f, 0.17f),
            new Vector3(0f, -90f, 0f));
        WarnStripe(_pBriefing, new Vector3(HX - 0.07f, 0.28f, 0.9f), 1.6f, 0.14f, 0.012f, new Vector3(0f, -90f, 0f), 7);

        // ── 왼쪽 벽 : 코트걸이 · 소화기 · 라벨 (레버 자리는 비워 둔다) ──
        Pipe(_pBriefing, new Vector3(-HX + 0.07f, 1.86f, 0.55f), new Vector3(-HX + 0.07f, 1.86f, 1.75f),
            0.022f, KAlu, "CoatRail");
        foreach (float z in new[] { 0.78f, 1.08f })
        {
            Pipe(_pBriefing, new Vector3(-HX + 0.07f, 1.86f, z), new Vector3(-HX + 0.19f, 1.70f, z),
                0.012f, KAlu, "Hook");
            Box(_pBriefing, new Vector3(0.10f, 0.62f, 0.30f), new Vector3(-HX + 0.21f, 1.38f, z),
                Mat(new Color(0.148f, 0.160f, 0.172f), 0.95f), "Coat");
        }
        Cyl(_pBriefing, 0.085f, 0.46f, new Vector3(-HX + 0.20f, 0.23f, 1.95f),
            Mat(new Color(0.42f, 0.09f, 0.06f), 0.70f), "Extinguisher");
        Box(_pBriefing, new Vector3(0.07f, 0.10f, 0.07f), new Vector3(-HX + 0.20f, 0.50f, 1.95f), KSteelDark);
        Label(_pBriefing, new Vector3(-HX + 0.07f, 0.95f, 1.95f), new Vector2(0.26f, 0.34f), new Vector3(0f, 90f, 0f));

        // ── 책상 ────────────────────────────────────────────────────────
        // 연단(= 큰 상자)이 아니라 관리자 책상이다. 상판 · 앞치마 · 금속 다리 · 서랍장.
        var desk = DeskUnit(_pBriefing, new Vector3(0f, 0f, -0.74f), 0f, 1.78f, 0.84f, 0.76f,
            Mat(new Color(0.148f, 0.152f, 0.166f), 0.42f, 0.18f), KPanel);

        // 책상 위 — 서류 · 펜꽂이 · 배치도 · 관리용 패드 · 녹음 장비 · 인터폰 · 스탠드.
        PaperStack(desk, new Vector3(-0.60f, 0.785f, 0.10f), -7f, 9, 7321);
        PaperStack(desk, new Vector3(-0.60f, 0.825f, 0.10f), 6f, 5, 991);
        Clipboard(desk, new Vector3(-0.17f, 0.787f, 0.19f), new Vector3(0f, 14f, 0f));
        PenCup(desk, new Vector3(0.33f, 0.782f, 0.22f), 553);
        // 펼쳐 둔 시설 배치도 — 청사진처럼 옅은 파란 선이 몇 줄 보인다.
        var plan = Box(desk, new Vector3(0.66f, 0.004f, 0.46f), new Vector3(0.70f, 0.784f, 0.08f),
            Mat(new Color(0.30f, 0.36f, 0.42f), 0.92f), "Plan");
        plan.RotationDegrees = new Vector3(0f, -9f, 0f);
        var planLine = Glow(new Color(0.40f, 0.60f, 0.78f), 0.30f);
        for (int i = 0; i < 4; i++)
            Box(plan, new Vector3(0.52f, 0.004f, 0.008f), new Vector3(0f, 0.004f, -0.15f + i * 0.10f), planLine);
        Box(plan, new Vector3(0.008f, 0.004f, 0.36f), new Vector3(-0.12f, 0.004f, 0f), planLine);
        // 말아 둔 도면 두 통.
        Pipe(desk, new Vector3(0.62f, 0.815f, -0.26f), new Vector3(1.00f, 0.815f, -0.22f), 0.028f, KPaper, "Roll", 7);
        Pipe(desk, new Vector3(0.60f, 0.868f, -0.24f), new Vector3(0.96f, 0.868f, -0.20f), 0.026f, KPaper, "Roll", 7);
        // 관리용 패드 — 받침에 비스듬히 세워져 있다.
        var pad = new Node3D { Position = new Vector3(-0.86f, 0.79f, -0.14f), RotationDegrees = new Vector3(0f, 22f, 0f) };
        desk.AddChild(pad);
        Box(pad, new Vector3(0.21f, 0.02f, 0.14f), Vector3.Zero, KPlasticDark, "PadStand");
        var padBody = Box(pad, new Vector3(0.26f, 0.19f, 0.016f), new Vector3(0f, 0.10f, -0.035f), KPlasticDark, "Pad");
        padBody.RotationDegrees = new Vector3(-24f, 0f, 0f);
        Box(padBody, new Vector3(0.22f, 0.15f, 0.006f), new Vector3(0f, 0f, 0.012f),
            Glow(new Color(0.28f, 0.56f, 0.47f), 0.70f), "PadScreen");
        // 녹음 장치 — 홍보 영상을 찍고 있는 방이다.
        var rec = new Node3D { Position = new Vector3(0.14f, 0.782f, -0.26f), RotationDegrees = new Vector3(0f, -16f, 0f) };
        desk.AddChild(rec);
        Chamfer(rec, new Vector3(0.24f, 0.075f, 0.17f), new Vector3(0f, 0.037f, 0f), KPlastic, 0.008f, "Recorder");
        foreach (float sx in new[] { -0.055f, 0.055f })
            Cyl(rec, 0.038f, 0.012f, new Vector3(sx, 0.078f, -0.02f), KSteelDark, "Reel")
                .RotationDegrees = new Vector3(90f, 0f, 0f);
        Box(rec, new Vector3(0.09f, 0.016f, 0.014f), new Vector3(0f, 0.080f, 0.055f), KAlu, "RecKeys");
        Box(rec, new Vector3(0.018f, 0.018f, 0.01f), new Vector3(0.082f, 0.080f, 0.055f),
            Glow(new Color(1f, 0.22f, 0.16f), 2.2f), "RecLamp");
        Intercom(desk, new Vector3(-1.08f, 0.782f, -0.30f), 28f);
        // 책상 스탠드 — 따뜻한 작은 광원 하나가 책상 위를 분리해 준다.
        var lampArm = new Node3D { Position = new Vector3(0.96f, 0.78f, -0.30f) };
        desk.AddChild(lampArm);
        Cyl(lampArm, 0.085f, 0.022f, new Vector3(0f, 0.011f, 0f), KSteelDark, "LampBase");
        Pipe(lampArm, new Vector3(0f, 0.02f, 0f), new Vector3(-0.10f, 0.40f, 0.04f), 0.012f, KAlu, "LampStem");
        var hood = Box(lampArm, new Vector3(0.17f, 0.09f, 0.14f), new Vector3(-0.15f, 0.41f, 0.06f), KAlu, "LampHood");
        hood.RotationDegrees = new Vector3(34f, 0f, 0f);
        Box(hood, new Vector3(0.13f, 0.012f, 0.10f), new Vector3(0f, -0.045f, 0f),
            Glow(new Color(1f, 0.88f, 0.68f), 1.35f), "LampGlass");
        _pBriefing.AddChild(new OmniLight3D
        {
            Position = new Vector3(0.80f, 1.05f, -0.86f),
            LightColor = new Color(1f, 0.84f, 0.62f), LightEnergy = 0.85f, OmniRange = 2.1f, ShadowEnabled = false,
        });
        // 책상 밑으로 내려가는 선 — 장비가 전부 '연결되어' 있어야 한다.
        CableSag(desk, new Vector3(-0.86f, 0.74f, -0.30f), new Vector3(-0.55f, 0.10f, -0.42f), 0.10f, 0.009f, KCable, 4);
        CableSag(desk, new Vector3(0.14f, 0.74f, -0.34f), new Vector3(0.40f, 0.08f, -0.42f), 0.12f, 0.009f, KCable, 4);

        // 관리자 의자 — 등받이가 보여야 책상 뒤가 비어 보이지 않는다.
        var chair = new Node3D { Position = new Vector3(0.05f, 0f, -1.34f), RotationDegrees = new Vector3(0f, 8f, 0f) };
        _pBriefing.AddChild(chair);
        Cyl(chair, 0.26f, 0.035f, new Vector3(0f, 0.02f, 0f), KSteelDark, "ChairFoot");
        Pipe(chair, new Vector3(0f, 0.03f, 0f), new Vector3(0f, 0.42f, 0f), 0.035f, KAlu, "ChairStem");
        Chamfer(chair, new Vector3(0.48f, 0.08f, 0.46f), new Vector3(0f, 0.46f, 0f), KPlasticDark, 0.012f, "Seat");
        var backRest = Box(chair, new Vector3(0.46f, 0.52f, 0.07f), new Vector3(0f, 0.78f, -0.22f),
            KPlasticDark, "Back");
        backRest.RotationDegrees = new Vector3(-8f, 0f, 0f);

        // ── 조명 : 키 · 보조 · 뒷광 ─────────────────────────────────────
        // 녹화된 안전교육 영상이므로 정면 키 하나가 지배하되, 방의 구조가 읽힐 만큼은
        // 천장등 · 벽등이 살아 있어야 한다(§6).
        var key = new SpotLight3D
        {
            Position = new Vector3(1.25f, 2.68f, 1.95f),
            LightColor = Warm, LightEnergy = 2.45f, SpotRange = 8.5f, SpotAngle = 33f,
            SpotAngleAttenuation = 1.05f, ShadowEnabled = true, ShadowBias = 0.05f,
        };
        _pBriefing.AddChild(key);              // 트리에 넣은 **뒤에** 조준한다(안 그러면 LookAt 이 실패한다)
        key.LookAt(new Vector3(0f, 1.45f, -0.75f), Vector3.Up);
        _pLights.Add(key);
        // 왼쪽 뒤에서 들어오는 차가운 역광 — 관리자 어깨선을 벽에서 떼어 낸다.
        _pBriefing.AddChild(new OmniLight3D
        {
            Position = new Vector3(-1.55f, 2.35f, -2.45f),
            LightColor = Cool, LightEnergy = 0.85f, OmniRange = 4.8f, ShadowEnabled = false,
        });
        // 캐비닛 쪽 벽등 — 구석이 완전히 죽지 않게.
        _pBriefing.AddChild(new OmniLight3D
        {
            Position = new Vector3(-1.35f, 2.25f, -2.9f),
            LightColor = new Color(0.92f, 0.86f, 0.74f), LightEnergy = 0.55f, OmniRange = 3.4f, ShadowEnabled = false,
        });
        CaptureLightBase();
    }

    // ── ③ 생활 구역 (archive_03_habitat) ─────────────────────────────────
    //
    // **재질 언어 : 생활 공간.** 연구 구역과 정반대로 간다 —
    // 바닥은 반사 없는 무광 비닐, 벽 아래는 따뜻한 도장 · 위는 흡음 패널,
    // 가구는 금속 프레임 + 목재 상판. 금속 패널 · 에폭시 · 경고 띠는 쓰지 않는다.
    // 그래야 같은 시설 안에서도 "여기는 사람이 쉬는 곳" 으로 읽힌다.
    private void BuildHabitat()
    {
        _pHabitat = new Node3D { Name = "PSetHabitat" };
        _world.AddChild(_pHabitat);
        var rng = new RandomNumberGenerator { Seed = 8812 };

        var floor = Mat(new Color(0.258f, 0.236f, 0.208f), 0.95f);         // 무광 비닐 — 반사가 없다
        var wall = Mat(new Color(0.292f, 0.276f, 0.250f), 0.92f);
        var acoustic = Mat(new Color(0.228f, 0.216f, 0.198f), 0.99f);      // 흡음 패널
        var steel = Mat(new Color(0.258f, 0.268f, 0.286f), 0.50f, 0.78f);
        var wood = Mat(new Color(0.246f, 0.182f, 0.124f), 0.68f);
        var woodDark = Mat(new Color(0.168f, 0.122f, 0.082f), 0.74f);

        Box(_pHabitat, new Vector3(14f, 0.3f, 16f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pHabitat, new Vector3(14f, 4.4f, 0.3f), new Vector3(0f, 2.2f, -8f), wall, "WallBack");
        Box(_pHabitat, new Vector3(0.3f, 4.4f, 16f), new Vector3(-7f, 2.2f, 0f), wall, "WallL");
        DressRoomSurfaces(_pHabitat, 7f, 8f, 4.4f, Vector3.Zero, 2.2f);
        Box(_pHabitat, new Vector3(0.3f, 4.4f, 16f), new Vector3(7f, 2.2f, 0f), wall, "WallR");
        Box(_pHabitat, new Vector3(14f, 0.3f, 16f), new Vector3(0f, 4.4f, 0f), wall, "Ceiling");

        // 벽 아래 걸레받이 겸 보호대 — 금속 몰딩이 아니라 목재 띠다(사무 구역과 다르다).
        foreach (var (c0, sz) in new[]
                 {
                     (new Vector3(0f, 0.46f, -7.82f), new Vector3(14f, 0.14f, 0.06f)),
                     (new Vector3(-6.82f, 0.46f, 0f), new Vector3(0.06f, 0.14f, 16f)),
                     (new Vector3(6.82f, 0.46f, 0f), new Vector3(0.06f, 0.14f, 16f)),
                 })
            Box(_pHabitat, sz, c0, woodDark, "ChairRail");
        // 벽 위쪽 흡음 패널 — 격자로 끊는다.
        for (int i = 0; i < 7; i++)
        {
            Box(_pHabitat, new Vector3(1.85f, 1.9f, 0.05f), new Vector3(-6f + i * 2.0f, 2.85f, -7.82f),
                acoustic, "AcousticPanel");
            Box(_pHabitat, new Vector3(0.06f, 2.0f, 0.08f), new Vector3(-5.05f + i * 2.0f, 2.85f, -7.80f),
                woodDark, "PanelRib");
        }
        for (int i = 0; i < 7; i++)
            foreach (float sx in new[] { -1f, 1f })
            {
                Box(_pHabitat, new Vector3(0.05f, 1.9f, 1.85f), new Vector3(sx * 6.82f, 2.85f, -6.6f + i * 2.1f),
                    acoustic, "AcousticPanel");
                Box(_pHabitat, new Vector3(0.08f, 2.0f, 0.06f), new Vector3(sx * 6.80f, 2.85f, -5.6f + i * 2.1f),
                    woodDark, "PanelRib");
            }

        // 바닥 — 입구 고무매트 · 식탁 주변 생활 얼룩. 비닐 바닥은 '닳는' 게 아니라 '때가 탄다'.
        Box(_pHabitat, new Vector3(2.4f, 0.006f, 1.5f), new Vector3(4.6f, 0.016f, 6.4f), KRubber, "EntryMat");
        for (int i = 0; i < 9; i++)
            Box(_pHabitat, new Vector3(rng.RandfRange(0.7f, 2.0f), 0.003f, rng.RandfRange(0.7f, 1.8f)),
                new Vector3(rng.RandfRange(-5.8f, 5.8f), 0.018f, rng.RandfRange(-6.8f, 6.8f)), Stain, "FloorStain");

        // ── 식탁 셋 + 의자 ──────────────────────────────────────────────
        for (int i = 0; i < 3; i++)
        {
            float x = -4f + i * 4f;
            var t = new Node3D { Position = new Vector3(x, 0f, -1.5f) };
            _pHabitat.AddChild(t);
            // 상판 — 두께 · 테두리 · 아래 보강대까지.
            Chamfer(t, new Vector3(2.4f, 0.075f, 1.2f), new Vector3(0f, 0.76f, 0f), wood, 0.018f, "TableTop");
            Box(t, new Vector3(2.2f, 0.05f, 1.0f), new Vector3(0f, 0.715f, 0f), steel, "TableUnder");
            Box(t, new Vector3(1.9f, 0.05f, 0.05f), new Vector3(0f, 0.18f, 0f), steel, "TableBrace");
            foreach (float dx in new[] { -0.9f, 0.9f })
            {
                // 다리 — 통짜 원기둥이 아니라 받침 + 기둥 + 발.
                Box(t, new Vector3(0.1f, 0.68f, 0.1f), new Vector3(dx, 0.37f, 0f), steel, "TableLeg");
                Box(t, new Vector3(0.14f, 0.045f, 0.9f), new Vector3(dx, 0.025f, 0f), steel, "TableFoot");
                Box(t, new Vector3(0.1f, 0.05f, 0.9f), new Vector3(dx, 0.70f, 0f), steel, "TableSpine");
            }
            // 의자 넷 — 앉는 판 · 등받이 · 네 다리.
            for (int k = 0; k < 4; k++)
            {
                float cx = -0.75f + (k % 2) * 1.5f;
                float cz = k < 2 ? 1.1f : -1.1f;
                var ch = new Node3D
                {
                    Position = new Vector3(cx, 0f, cz),
                    RotationDegrees = new Vector3(0f, (k < 2 ? 0f : 180f) + rng.RandfRange(-16f, 16f), 0f),
                };
                t.AddChild(ch);
                Chamfer(ch, new Vector3(0.44f, 0.05f, 0.42f), new Vector3(0f, 0.45f, 0f), wood, 0.01f, "Seat");
                var back = Box(ch, new Vector3(0.42f, 0.42f, 0.045f), new Vector3(0f, 0.70f, -0.19f),
                    wood, "ChairBack");
                back.RotationDegrees = new Vector3(-9f, 0f, 0f);
                foreach (float lx in new[] { -0.18f, 0.18f })
                    foreach (float lz in new[] { -0.17f, 0.17f })
                        Pipe(ch, new Vector3(lx, 0.02f, lz), new Vector3(lx, 0.45f, lz), 0.016f, steel, "Leg", 6);
                Pipe(ch, new Vector3(-0.18f, 0.45f, -0.17f), new Vector3(-0.18f, 0.93f, -0.21f), 0.016f, steel, "BackPost", 6);
                Pipe(ch, new Vector3(0.18f, 0.45f, -0.17f), new Vector3(0.18f, 0.93f, -0.21f), 0.016f, steel, "BackPost", 6);
            }
            // 식탁 위 생활감 — 식판 · 컵 · 조미통 · 펼친 서류.
            if (i != 1)
            {
                var tray = Box(t, new Vector3(0.44f, 0.028f, 0.32f), new Vector3(-0.42f, 0.81f, 0.08f),
                    Mat(new Color(0.20f, 0.185f, 0.165f), 0.90f), "Tray");
                tray.RotationDegrees = new Vector3(0f, rng.RandfRange(-12f, 12f), 0f);
                Cyl(tray, 0.055f, 0.09f, new Vector3(0.12f, 0.058f, -0.06f), KPaper, "Cup");
            }
            Cyl(t, 0.045f, 0.12f, new Vector3(0.1f, 0.855f, 0f), steel, "Shaker");
            Cyl(t, 0.045f, 0.12f, new Vector3(0.2f, 0.855f, 0.04f), KPlasticDark, "Shaker");
            if (i == 2) PaperStack(t, new Vector3(0.6f, 0.80f, -0.1f), 24f, 5, 771);
        }

        // ── 벽면 사물함 줄 ──────────────────────────────────────────────
        for (int i = 0; i < 6; i++)
        {
            var lk = new Node3D { Position = new Vector3(-5.6f + i * 1.0f, 0f, -7.5f) };
            _pHabitat.AddChild(lk);
            Chamfer(lk, new Vector3(0.94f, 2.0f, 0.5f), new Vector3(0f, 1.0f, 0f), steel, 0.014f, "Locker");
            Box(lk, new Vector3(0.9f, 0.025f, 0.03f), new Vector3(0f, 1.18f, 0.252f), KSteelDark, "DoorSeam");
            // 문 둘 — 통풍 슬릿 · 손잡이 · 번호판.
            foreach (float dy in new[] { 0.52f, 1.52f })
            {
                Louver(lk, new Vector3(0f, dy + 0.34f, 0.255f), 0.42f, 0.16f, Vector3.Zero, 4);
                Box(lk, new Vector3(0.055f, 0.14f, 0.035f), new Vector3(0.36f, dy, 0.262f), KAlu, "Handle");
                Box(lk, new Vector3(0.14f, 0.09f, 0.008f), new Vector3(-0.28f, dy + 0.42f, 0.256f), KPaper, "NumPlate");
            }
            // 한 칸은 열려 있다 — 다 닫혀 있으면 아무도 안 쓰는 사물함이다.
            if (i == 4)
            {
                var door = new Node3D { Position = new Vector3(0.45f, 1.52f, 0.25f) };
                lk.AddChild(door);
                door.RotationDegrees = new Vector3(0f, -58f, 0f);
                Box(door, new Vector3(0.88f, 0.92f, 0.03f), new Vector3(-0.44f, 0f, 0f), steel, "OpenDoor");
                Box(lk, new Vector3(0.80f, 0.90f, 0.42f), new Vector3(0f, 1.52f, 0f), KPlasticDark, "Inside");
                Box(lk, new Vector3(0.22f, 0.5f, 0.14f), new Vector3(-0.12f, 1.35f, 0.02f),
                    Mat(new Color(0.22f, 0.24f, 0.26f), 0.95f), "Coat");
            }
        }
        // 사물함 위 잡동사니 — 헬멧 · 상자.
        Cyl(_pHabitat, 0.17f, 0.2f, new Vector3(-4.3f, 2.1f, -7.5f), Mat(new Color(0.42f, 0.34f, 0.10f), 0.7f), "Helmet");
        FileBox(_pHabitat, new Vector3(-1.9f, 2.0f, -7.5f), 14f);

        // ── 자판기 · 급수대 · 조리 카운터 ───────────────────────────────
        var vend = new Node3D { Position = new Vector3(5.6f, 0f, -7.4f) };
        _pHabitat.AddChild(vend);
        Chamfer(vend, new Vector3(1.15f, 2.1f, 0.8f), new Vector3(0f, 1.05f, 0f), steel, 0.02f, "Vendor");
        Box(vend, new Vector3(0.95f, 1.1f, 0.05f), new Vector3(0f, 1.35f, 0.405f), KPlasticDark, "VendGlass");
        for (int r = 0; r < 3; r++)
            for (int cc = 0; cc < 4; cc++)
                Box(vend, new Vector3(0.14f, 0.2f, 0.04f), new Vector3(-0.33f + cc * 0.22f, 0.98f + r * 0.33f, 0.42f),
                    Mat(new Color(rng.RandfRange(0.12f, 0.34f), rng.RandfRange(0.10f, 0.26f),
                        rng.RandfRange(0.08f, 0.22f)), 0.7f), "Item");
        Box(vend, new Vector3(0.95f, 0.26f, 0.05f), new Vector3(0f, 1.96f, 0.405f),
            Glow(new Color(0.52f, 0.40f, 0.16f), 1.1f), "VendSign");
        Box(vend, new Vector3(0.34f, 0.24f, 0.06f), new Vector3(0.36f, 0.58f, 0.41f), KPlasticDark, "CoinPanel");
        Box(vend, new Vector3(0.55f, 0.2f, 0.07f), new Vector3(-0.2f, 0.34f, 0.41f), KSteelDark, "Tray");
        // 급수대.
        var water = new Node3D { Position = new Vector3(6.55f, 0f, -5.1f), RotationDegrees = new Vector3(0f, -90f, 0f) };
        _pHabitat.AddChild(water);
        Chamfer(water, new Vector3(0.44f, 1.0f, 0.42f), new Vector3(0f, 0.5f, 0f), KPlastic, 0.012f, "Dispenser");
        Cyl(water, 0.19f, 0.5f, new Vector3(0f, 1.26f, 0f), KGlassPane, "Bottle");
        Cyl(water, 0.1f, 0.1f, new Vector3(0f, 1.0f, 0f), KPlasticDark, "BottleNeck");
        Box(water, new Vector3(0.1f, 0.06f, 0.1f), new Vector3(0f, 0.72f, 0.21f), KAlu, "Tap");
        // 조리 카운터 — 상판 · 싱크 · 커피 포트 · 선반.
        var counter = new Node3D { Position = new Vector3(-4.0f, 0f, 6.6f), RotationDegrees = new Vector3(0f, 180f, 0f) };
        _pHabitat.AddChild(counter);
        Chamfer(counter, new Vector3(3.4f, 0.88f, 0.7f), new Vector3(0f, 0.44f, 0f), steel, 0.016f, "Counter");
        Box(counter, new Vector3(3.5f, 0.05f, 0.76f), new Vector3(0f, 0.90f, 0f), KAlu, "CounterTop");
        Box(counter, new Vector3(0.7f, 0.1f, 0.5f), new Vector3(-0.9f, 0.88f, 0f), KSteelDark, "Sink");
        Pipe(counter, new Vector3(-0.9f, 0.93f, -0.2f), new Vector3(-0.9f, 1.22f, -0.2f), 0.018f, KAlu, "Faucet", 6);
        Pipe(counter, new Vector3(-0.9f, 1.22f, -0.2f), new Vector3(-0.9f, 1.20f, -0.02f), 0.015f, KAlu, "Spout", 6);
        Chamfer(counter, new Vector3(0.3f, 0.34f, 0.26f), new Vector3(0.75f, 1.08f, 0f), KPlasticDark, 0.01f, "Kettle");
        Cyl(counter, 0.08f, 0.1f, new Vector3(1.2f, 0.96f, 0.1f), KPaper, "Mug");
        Cyl(counter, 0.08f, 0.1f, new Vector3(1.38f, 0.96f, -0.05f), KPaper, "Mug");
        Box(counter, new Vector3(3.2f, 0.05f, 0.26f), new Vector3(0f, 1.66f, -0.22f), wood, "Shelf");
        foreach (float sx in new[] { -1.3f, 1.3f })
            Box(counter, new Vector3(0.05f, 0.24f, 0.24f), new Vector3(sx, 1.52f, -0.22f), steel, "ShelfBracket");
        for (int i = 0; i < 5; i++)
            Cyl(counter, 0.07f, 0.1f, new Vector3(-1.0f + i * 0.45f, 1.74f, -0.22f), KPaper, "Mug");

        // ── 벽 : 게시판 · 시계 · 안내문 ─────────────────────────────────
        var board = new Node3D { Position = new Vector3(2.0f, 1.95f, -7.84f) };
        _pHabitat.AddChild(board);
        Box(board, new Vector3(2.0f, 1.2f, 0.05f), Vector3.Zero, woodDark, "BoardFrame");
        Box(board, new Vector3(1.86f, 1.06f, 0.03f), new Vector3(0f, 0f, 0.03f), KFoam, "Cork");
        for (int i = 0; i < 7; i++)
        {
            var note = Box(board, new Vector3(rng.RandfRange(0.20f, 0.34f), rng.RandfRange(0.24f, 0.36f), 0.008f),
                new Vector3(rng.RandfRange(-0.72f, 0.72f), rng.RandfRange(-0.36f, 0.36f), 0.05f), KPaper, "Notice");
            note.RotationDegrees = new Vector3(0f, 0f, rng.RandfRange(-5f, 5f));
        }
        Cyl(_pHabitat, 0.23f, 0.06f, new Vector3(4.2f, 2.9f, -7.80f), woodDark, "Clock")
            .RotationDegrees = new Vector3(90f, 0f, 0f);
        Cyl(_pHabitat, 0.19f, 0.07f, new Vector3(4.2f, 2.9f, -7.78f), KPaper, "ClockFace")
            .RotationDegrees = new Vector3(90f, 0f, 0f);
        Box(_pHabitat, new Vector3(0.016f, 0.14f, 0.012f), new Vector3(4.2f, 2.96f, -7.74f), KSteelDark, "Hand");
        Box(_pHabitat, new Vector3(0.11f, 0.016f, 0.012f), new Vector3(4.25f, 2.90f, -7.74f), KSteelDark, "Hand");
        Label(_pHabitat, new Vector3(-6.80f, 2.3f, 3.2f), new Vector2(0.34f, 0.5f), new Vector3(0f, 90f, 0f));

        // 화분 둘 — 지하 시설에서 사람이 기어코 들여놓는 것.
        foreach (var (x, z) in new[] { (6.3f, 1.4f), (-6.3f, -3.4f) })
        {
            var pot = new Node3D { Position = new Vector3(x, 0f, z) };
            _pHabitat.AddChild(pot);
            Cyl(pot, 0.26f, 0.42f, new Vector3(0f, 0.21f, 0f), Mat(new Color(0.20f, 0.14f, 0.10f), 0.92f), "Pot");
            Cyl(pot, 0.28f, 0.06f, new Vector3(0f, 0.43f, 0f), Mat(new Color(0.16f, 0.12f, 0.09f), 0.95f), "Soil");
            var leaf = Mat(new Color(0.11f, 0.19f, 0.10f), 0.86f);
            for (int k = 0; k < 7; k++)
            {
                float a = Mathf.Tau * k / 7f;
                var l = Box(pot, new Vector3(0.09f, 0.52f, 0.03f),
                    new Vector3(Mathf.Cos(a) * 0.13f, 0.72f, Mathf.Sin(a) * 0.13f), leaf, "Leaf");
                l.RotationDegrees = new Vector3(Mathf.Sin(a) * 26f, Mathf.RadToDeg(a), -Mathf.Cos(a) * 26f);
            }
        }

        // ── 조명 : 따뜻하고 **균일하지 않게** ───────────────────────────
        // 생활 공간은 등마다 색온도와 세기가 조금씩 달라야 한다. 똑같이 깔면 사무실이 된다.
        var warmCols = new[]
        {
            new Color(1f, 0.93f, 0.82f), new Color(1f, 0.89f, 0.74f),
            new Color(0.98f, 0.95f, 0.88f), new Color(1f, 0.86f, 0.70f),
        };
        for (int i = 0; i < 4; i++)
        {
            float z = 5f - i * 4f;
            // 등 갓 — 천장에 붙은 발광 판이 아니라 매달린 조명이다.
            Box(_pHabitat, new Vector3(5.4f, 0.09f, 0.42f), new Vector3(0f, 4.16f, z), woodDark, "LampHood");
            foreach (float sx in new[] { -2.2f, 2.2f })
                Pipe(_pHabitat, new Vector3(sx, 4.28f, z), new Vector3(sx, 4.12f, z), 0.018f, steel, "LampRod", 4);
            Strip(_pHabitat, new Vector3(0f, 4.06f, z), 5.0f, warmCols[i], 2.0f + (i % 2) * 0.5f,
                _pLampMats, _pLights, 1.1f + (i % 3) * 0.28f);
        }
        // 카운터 위 작업등 하나 — 그 자리만 밝다.
        _pHabitat.AddChild(new OmniLight3D
        {
            Position = new Vector3(-4.0f, 2.3f, 5.9f),
            LightColor = new Color(1f, 0.86f, 0.66f), LightEnergy = 1.1f, OmniRange = 4.2f, ShadowEnabled = false,
        });
        CaptureLightBase();
    }

    // ── ④ 연구실 (disaster_04_lab_wreck) ────────────────────────────────
    //
    // 대재난의 물리적 충격을 처음 보여 주는 방. 깨질 유리 칸막이와 넘어질 소품이 있어야 한다(§13).
    //
    // **재질 언어 : 실험 구역.** 바닥은 에폭시에 배수구와 배선 덮개, 벽은 도장 콘크리트 +
    // 금속 패널, 실험대는 스테인리스 상판 + 각관 프레임 + 하부 수납.
    // 장비는 발광부만 있는 상자가 아니라 케이스 · 통풍구 · 버튼 · 이음매를 갖는다.
    private void BuildLab()
    {
        _pLab = new Node3D { Name = "PSetLab" };
        _world.AddChild(_pLab);
        var rng = new RandomNumberGenerator { Seed = 3307 };

        var floor = Mat(new Color(0.228f, 0.240f, 0.258f), 0.42f, 0.08f);
        var wall = Mat(new Color(0.262f, 0.272f, 0.296f), 0.92f);
        var steel = KSteel;
        var dark = KPanelDark;
        var inox = Mat(new Color(0.340f, 0.352f, 0.372f), 0.22f, 0.95f);   // 스테인리스 상판

        Box(_pLab, new Vector3(12f, 0.3f, 12f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pLab, new Vector3(12f, 4.4f, 0.3f), new Vector3(0f, 2.2f, -6f), wall, "WallBack");
        Box(_pLab, new Vector3(0.3f, 4.4f, 12f), new Vector3(-6f, 2.2f, 0f), wall, "WallL");
        DressRoomSurfaces(_pLab, 6f, 6f, 4.4f, Vector3.Zero, 2.0f);
        Box(_pLab, new Vector3(0.3f, 4.4f, 12f), new Vector3(6f, 2.2f, 0f), wall, "WallR");
        Box(_pLab, new Vector3(12f, 0.3f, 12f), new Vector3(0f, 4.4f, 0f), wall, "Ceiling");
        DressWallPanels(_pLab, new Vector3(0f, 0f, -5.84f), 12f, 4.4f, true, 1f, 1.1f, 2.0f);
        DressWallPanels(_pLab, new Vector3(-5.84f, 0f, 0f), 12f, 4.4f, false, 1f, 1.1f, 2.0f);

        // 바닥 — 배선 덮개 · 배수구 · 장비 앞 닳은 자리.
        Box(_pLab, new Vector3(0.42f, 0.035f, 11f), new Vector3(-5.1f, 0.02f, 0f), KSteelDark, "CableCover");
        for (int i = 0; i < 9; i++)
            Box(_pLab, new Vector3(0.36f, 0.012f, 0.05f), new Vector3(-5.1f, 0.04f, -4.8f + i * 1.2f), KAlu, "CoverSeam");
        foreach (var at in new[] { new Vector3(2.0f, 0f, -3.4f), new Vector3(-3.2f, 0f, 3.0f) })
        {
            Cyl(_pLab, 0.22f, 0.04f, at + new Vector3(0f, 0.025f, 0f), KSteelDark, "Drain");
            Cyl(_pLab, 0.16f, 0.05f, at + new Vector3(0f, 0.03f, 0f), KPlasticDark, "DrainGrate");
        }
        for (int i = 0; i < 8; i++)
            Box(_pLab, new Vector3(rng.RandfRange(0.7f, 1.9f), 0.003f, rng.RandfRange(0.7f, 1.6f)),
                new Vector3(rng.RandfRange(-5f, 2.5f), 0.018f, rng.RandfRange(-5f, 5f)), Stain, "FloorStain");

        // ── 실험대 둘 ───────────────────────────────────────────────────
        _labProps.Clear();
        for (int t = 0; t < 2; t++)
        {
            float z = -2.2f + t * 3.2f;
            var bench = new Node3D { Position = new Vector3(-1.4f, 0f, z) };
            _pLab.AddChild(bench);
            // 상판 — 두께 · 뒷턱 · 아래 테두리.
            Chamfer(bench, new Vector3(5.2f, 0.07f, 1.3f), new Vector3(0f, 0.92f, 0f), inox, 0.016f, "BenchTop");
            Box(bench, new Vector3(5.1f, 0.05f, 1.2f), new Vector3(0f, 0.875f, 0f), steel, "BenchUnder");
            Box(bench, new Vector3(5.2f, 0.14f, 0.05f), new Vector3(0f, 0.99f, -0.62f), inox, "BenchSplash");
            // 각관 프레임 + 하부 수납 + 선반.
            for (int i = 0; i < 4; i++)
            {
                float lx = -1.95f + i * 1.3f;
                Box(bench, new Vector3(0.06f, 0.85f, 0.06f), new Vector3(lx, 0.44f, -0.52f), steel, "Leg");
                Box(bench, new Vector3(0.06f, 0.85f, 0.06f), new Vector3(lx, 0.44f, 0.52f), steel, "Leg");
                Box(bench, new Vector3(0.05f, 0.05f, 1.1f), new Vector3(lx, 0.24f, 0f), steel, "LegTie");
            }
            Box(bench, new Vector3(4.9f, 0.04f, 1.0f), new Vector3(0f, 0.26f, 0f), KPanelDark, "LowShelf");
            // 하부 수납장 둘.
            foreach (float cx in new[] { -1.3f, 1.3f })
            {
                var cab = new Node3D { Position = new Vector3(cx, 0f, 0f) };
                bench.AddChild(cab);
                Chamfer(cab, new Vector3(1.1f, 0.74f, 1.1f), new Vector3(0f, 0.46f, 0f), KPanel, 0.012f, "Cabinet");
                Box(cab, new Vector3(1.04f, 0.012f, 0.014f), new Vector3(0f, 0.56f, 0.56f), KSteelDark, "Seam");
                Box(cab, new Vector3(0.22f, 0.03f, 0.03f), new Vector3(0f, 0.68f, 0.57f), KAlu, "Handle");
                Box(cab, new Vector3(0.012f, 0.68f, 0.014f), new Vector3(0f, 0.38f, 0.56f), KSteelDark, "DoorGap");
            }
            // 상판 위 소품 — 떨어질 것들. 종류를 섞는다(병 · 통 · 받침대 · 기기).
            for (int i = 0; i < 4; i++)
            {
                var at = new Vector3(-2.3f + i * 1.15f + t * 0.3f, 0.955f, rng.RandfRange(-0.22f, 0.22f));
                MeshInstance3D prop;
                if (i % 3 == 0)
                {
                    // 시약병 — 몸통 + 목 + 뚜껑.
                    prop = Cyl(bench, 0.085f, 0.26f, at + new Vector3(0f, 0.13f, 0f),
                        Mat(new Color(0.20f, 0.26f, 0.24f), 0.35f, 0.1f), "Bottle");
                    Cyl(bench, 0.035f, 0.07f, at + new Vector3(0f, 0.29f, 0f), KGlassPane, "BottleNeck");
                    Cyl(bench, 0.045f, 0.035f, at + new Vector3(0f, 0.33f, 0f), KPlasticDark, "Cap");
                    Box(bench, new Vector3(0.1f, 0.09f, 0.004f), at + new Vector3(0f, 0.14f, 0.086f), KPaper, "BottleTag");
                }
                else if (i % 3 == 1)
                {
                    // 금속 통 — 뚜껑 · 밸브 · 밴드를 자식으로 달아 통째로 같이 움직이게 한다.
                    prop = Cyl(bench, 0.115f, 0.30f, at + new Vector3(0f, 0.15f, 0f), KPanel, "Canister");
                    Cyl(prop, 0.125f, 0.03f, new Vector3(0f, 0.08f, 0f), KSteelDark, "CanBand");
                    Cyl(prop, 0.06f, 0.06f, new Vector3(0f, 0.18f, 0f), KBrass, "CanValve");
                    Box(prop, new Vector3(0.1f, 0.07f, 0.004f), new Vector3(0f, -0.02f, 0.116f), KPaper, "CanTag");
                }
                else
                {
                    // 분석 기기 — 케이스 · 통풍구 · 표시등.
                    prop = Box(bench, new Vector3(0.36f, 0.24f, 0.30f), at + new Vector3(0f, 0.12f, 0f),
                        KPlastic, "Analyzer");
                    Louver(prop, new Vector3(0f, 0f, 0.15f), 0.2f, 0.1f, Vector3.Zero, 3);
                    Box(prop, new Vector3(0.05f, 0.03f, 0.01f), new Vector3(0.13f, 0.08f, 0.152f),
                        Glow(new Color(0.26f, 0.66f, 0.38f), 1.8f), "Led");
                }
                _labProps.Add(prop);
            }
            // 모니터 — 케이스 · 베젤 · 받침 · 통풍구.
            var mon = new Node3D { Position = new Vector3(2.0f + t * 0.3f, 0.955f, -0.4f) };
            bench.AddChild(mon);
            mon.RotationDegrees = new Vector3(0f, rng.RandfRange(-22f, 22f), 0f);
            Box(mon, new Vector3(0.26f, 0.04f, 0.22f), new Vector3(0f, 0.02f, 0f), KPlasticDark, "MonBase");
            Box(mon, new Vector3(0.09f, 0.22f, 0.09f), new Vector3(0f, 0.14f, 0f), KPlasticDark, "MonStem");
            var head = Box(mon, new Vector3(0.9f, 0.6f, 0.14f), new Vector3(0f, 0.56f, -0.02f), KPlastic, "MonCase");
            Box(head, new Vector3(0.82f, 0.52f, 0.02f), new Vector3(0f, 0f, 0.075f), KPlasticDark, "Bezel");
            Box(head, new Vector3(0.74f, 0.44f, 0.012f), new Vector3(0f, 0.01f, 0.086f),
                Glow(new Color(0.26f, 0.52f, 0.64f), 1.3f), "Screen");
            Louver(head, new Vector3(0f, 0.32f, -0.03f), 0.5f, 0.1f, new Vector3(90f, 0f, 0f), 3);
            Box(head, new Vector3(0.05f, 0.02f, 0.012f), new Vector3(0.34f, -0.26f, 0.08f), KAlu, "MonBtn");
            _labProps.Add(head);
            CableSag(bench, new Vector3(2.0f + t * 0.3f, 0.92f, -0.5f),
                new Vector3(2.4f + t * 0.3f, 0.30f, -0.62f), 0.1f, 0.01f, KCable, 4);
        }

        // ── 유리 칸막이 — 이 장면의 주인공. 깨지면 숨기고 파편을 뿌린다 ──
        _labGlassMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.40f, 0.54f, 0.60f, 0.20f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.22f, Metallic = 0.1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _labGlass = Box(_pLab, new Vector3(0.06f, 2.6f, 7.0f), new Vector3(3.3f, 1.4f, -0.6f),
            _labGlassMat, "GlassWall");
        Box(_pLab, new Vector3(0.12f, 0.12f, 7.1f), new Vector3(3.3f, 2.72f, -0.6f), steel);
        Box(_pLab, new Vector3(0.12f, 0.12f, 7.1f), new Vector3(3.3f, 0.1f, -0.6f), steel);
        Box(_pLab, new Vector3(0.12f, 2.6f, 0.12f), new Vector3(3.3f, 1.4f, 2.9f), steel);
        // 중간 멀리언 — 통유리 한 장은 '유리' 로 안 읽힌다.
        foreach (float gz in new[] { -2.9f, -1.2f, 0.6f, 2.3f })
            Box(_pLab, new Vector3(0.14f, 2.5f, 0.1f), new Vector3(3.3f, 1.4f, gz), steel, "GlassMullion");
        // 칸막이 위로 올라가는 상부 틀 · 경고 라벨.
        Box(_pLab, new Vector3(0.2f, 1.5f, 7.1f), new Vector3(3.3f, 3.6f, -0.6f), KPanelDark, "GlassHeader");
        Label(_pLab, new Vector3(3.19f, 1.9f, 1.4f), new Vector2(0.34f, 0.24f), new Vector3(0f, 90f, 0f));
        WarnStripe(_pLab, new Vector3(3.18f, 0.3f, -0.6f), 6.6f, 0.14f, 0.012f, new Vector3(0f, 90f, 0f), 20);

        // ── 뒷벽 설비 : 선반 · 시약 · 배관 · 흄후드 ─────────────────────
        foreach (var (y, depth) in new[] { (2.30f, 0.46f), (1.60f, 0.40f) })
        {
            Box(_pLab, new Vector3(9f, 0.07f, depth), new Vector3(-1f, y, -5.6f), steel, "Shelf");
            for (int i = -2; i <= 2; i++)
                Box(_pLab, new Vector3(0.05f, 0.22f, depth - 0.06f), new Vector3(-1f + i * 2.1f, y - 0.14f, -5.6f),
                    KSteelDark, "ShelfBracket");
            Box(_pLab, new Vector3(9f, 0.05f, 0.04f), new Vector3(-1f, y + 0.06f, -5.6f + depth * 0.5f),
                KAlu, "ShelfLip");
        }
        for (int i = 0; i < 9; i++)
        {
            float x = -5.0f + i * 1.1f;
            Cyl(_pLab, 0.09f, 0.3f, new Vector3(x, 2.49f, -5.62f),
                Mat(new Color(rng.RandfRange(0.14f, 0.26f), rng.RandfRange(0.16f, 0.26f),
                    rng.RandfRange(0.14f, 0.24f)), 0.40f, 0.08f), "Reagent");
            Cyl(_pLab, 0.04f, 0.05f, new Vector3(x, 2.67f, -5.62f), KPlasticDark, "ReagentCap");
            if (i % 2 == 0)
                Chamfer(_pLab, new Vector3(0.26f, 0.28f, 0.26f), new Vector3(x + 0.4f, 1.78f, -5.6f),
                    KFoam, 0.008f, "Box");
            else
                Binders(_pLab, new Vector3(x + 0.1f, 1.64f, -5.6f), 0f, 3, (ulong)(900 + i));
        }
        // 흄후드 — 유리 새시 · 배기 덕트 · 내부 등.
        var hood = new Node3D { Position = new Vector3(4.6f, 0f, -5.3f) };
        _pLab.AddChild(hood);
        Chamfer(hood, new Vector3(2.0f, 0.95f, 0.9f), new Vector3(0f, 0.48f, 0f), KPanel, 0.016f, "HoodBase");
        Box(hood, new Vector3(2.0f, 1.5f, 0.1f), new Vector3(0f, 1.75f, -0.4f), KPanelDark, "HoodBack");
        Box(hood, new Vector3(0.1f, 1.5f, 0.9f), new Vector3(-0.95f, 1.75f, 0f), KPanelDark, "HoodSideL");
        Box(hood, new Vector3(0.1f, 1.5f, 0.9f), new Vector3(0.95f, 1.75f, 0f), KPanelDark, "HoodSideR");
        Box(hood, new Vector3(2.0f, 0.12f, 0.95f), new Vector3(0f, 2.55f, 0f), KPanel, "HoodTop");
        Box(hood, new Vector3(1.8f, 0.9f, 0.03f), new Vector3(0f, 1.95f, 0.43f), KGlassPane, "HoodSash");
        Box(hood, new Vector3(1.86f, 0.07f, 0.07f), new Vector3(0f, 1.48f, 0.44f), KAlu, "SashHandle");
        Box(hood, new Vector3(1.7f, 0.05f, 0.05f), new Vector3(0f, 2.44f, 0.1f),
            Glow(new Color(0.70f, 0.74f, 0.70f), 1.4f), "HoodLamp");
        Duct(hood, new Vector3(0f, 2.62f, -0.1f), new Vector3(0f, 4.3f, -0.1f), 0.5f, 0.5f, KPanelDark, KSteelDark, 1.2f);
        Pipe(hood, new Vector3(-0.6f, 1.05f, 0.2f), new Vector3(-0.6f, 1.35f, 0.2f), 0.02f, KAlu, "HoodTap", 6);
        Cyl(hood, 0.05f, 0.04f, new Vector3(-0.6f, 1.37f, 0.2f), KBrass, "HoodValve");

        // 벽 배관 — 굵기가 다른 셋 + 밸브.
        foreach (var (y, r) in new[] { (3.90f, 0.14f), (4.05f, 0.1f), (3.70f, 0.07f) })
            Pipe(_pLab, new Vector3(-5.4f, y, -5.6f), new Vector3(-5.4f, y, 5.6f), r, steel, "WallPipe", 8);
        for (int i = -2; i <= 2; i++)
        {
            Cyl(_pLab, 0.2f, 0.06f, new Vector3(-5.4f, 3.90f, i * 2.2f), KSteelDark, "Flange")
                .RotationDegrees = new Vector3(90f, 0f, 0f);
            Pipe(_pLab, new Vector3(-5.4f, 4.3f, i * 2.2f), new Vector3(-5.4f, 4.05f, i * 2.2f),
                0.025f, KSteelDark, "PipeHanger", 4);
        }
        Cyl(_pLab, 0.1f, 0.22f, new Vector3(-5.4f, 3.62f, 1.1f), KBrass, "Valve")
            .RotationDegrees = new Vector3(0f, 0f, 90f);
        // 세안기 · 소화기 · 경고 표지 — '안전 관리되는 실험실'.
        var eye = new Node3D { Position = new Vector3(-5.5f, 0f, 4.2f) };
        _pLab.AddChild(eye);
        Pipe(eye, new Vector3(0f, 0f, 0f), new Vector3(0f, 1.05f, 0f), 0.035f, Mat(new Color(0.16f, 0.32f, 0.18f), 0.7f), "EyePost");
        Box(eye, new Vector3(0.42f, 0.06f, 0.3f), new Vector3(0.14f, 1.08f, 0f), Mat(new Color(0.18f, 0.36f, 0.20f), 0.7f), "EyeBowl");
        Box(eye, new Vector3(0.24f, 0.3f, 0.015f), new Vector3(0f, 1.6f, 0.2f), Mat(new Color(0.16f, 0.34f, 0.19f), 0.8f), "EyeSign");
        Cyl(_pLab, 0.085f, 0.46f, new Vector3(-5.6f, 0.23f, 5.2f), Mat(new Color(0.40f, 0.09f, 0.06f), 0.70f), "Extinguisher");
        Label(_pLab, new Vector3(-5.81f, 2.6f, 2.6f), new Vector2(0.4f, 0.5f), new Vector3(0f, 90f, 0f));

        // 바퀴 달린 스툴 · 폐기물통 — 사람이 쓰던 흔적.
        var stool = new Node3D { Position = new Vector3(-2.6f, 0f, -1.0f) };
        _pLab.AddChild(stool);
        Cyl(stool, 0.24f, 0.04f, new Vector3(0f, 0.03f, 0f), KSteelDark, "StoolFoot");
        Pipe(stool, new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0.55f, 0f), 0.035f, KAlu, "StoolStem", 6);
        Chamfer(stool, new Vector3(0.38f, 0.07f, 0.38f), new Vector3(0f, 0.58f, 0f), KPlasticDark, 0.012f, "StoolSeat");
        Cyl(_pLab, 0.26f, 0.72f, new Vector3(-4.6f, 0.36f, 2.2f), KPlasticDark, "Bin");
        Cyl(_pLab, 0.28f, 0.05f, new Vector3(-4.6f, 0.74f, 2.2f), Mat(new Color(0.30f, 0.26f, 0.10f), 0.8f), "BinLid");

        // ── 천장 : 조명 · 서비스 레일 · 덕트 ────────────────────────────
        for (int i = 0; i < 3; i++)
        {
            float z = 3f - i * 3.2f;
            Box(_pLab, new Vector3(4.6f, 0.09f, 0.4f), new Vector3(-1.5f, 4.22f, z), KAlu, "LampTroffer");
            Strip(_pLab, new Vector3(-1.5f, 4.1f, z), 4.2f, Cool, 2.4f, _pLampMats, _pLights, 1.6f);
        }
        Box(_pLab, new Vector3(11.6f, 0.2f, 0.22f), new Vector3(0f, 4.26f, -2.0f), KPanelDark, "CeilBeam");
        Box(_pLab, new Vector3(11.6f, 0.2f, 0.22f), new Vector3(0f, 4.26f, 2.0f), KPanelDark, "CeilBeam");
        CableTray(_pLab, new Vector3(1.2f, 4.0f, -5.5f), new Vector3(1.2f, 4.0f, 5.5f), 0.5f, KSteelDark, KCable);

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
    //
    // **재질 언어 : 통행 구역.** 바닥 가운데는 닳은 에폭시, 가장자리는 아직 멀쩡하다.
    // 벽은 허리 아래 콘크리트 · 위는 금속 패널 + 볼트, 그 위로 배관 · 케이블 트레이가 흐른다.
    // 문은 평평한 판이 아니라 **문틀 · 창 · 손잡이 · 표지**를 갖는다.
    //
    // 카메라가 벽에 바짝 붙는 컷이 있다(도주 = 오른쪽 x≈2.05 / 차폐문 = 왼쪽 x≈-1.5).
    // 그래서 **두꺼운 소품은 서로 반대쪽에만** 둔다 — 아니면 렌즈가 소품을 뚫는다.
    private void BuildPrologueCorridor()
    {
        _pCorridor = new Node3D { Name = "PSetCorridor" };
        _world.AddChild(_pCorridor);
        var rng = new RandomNumberGenerator { Seed = 1771 };

        var floor = Mat(new Color(0.244f, 0.258f, 0.276f), 0.44f, 0.08f);
        var wall = Mat(new Color(0.272f, 0.286f, 0.310f), 0.92f);
        var steel = KSteel;
        var dark = KPanelDark;

        Box(_pCorridor, new Vector3(5.2f, 0.3f, 56f), new Vector3(0f, -0.15f, 0f), floor, "Floor");
        Box(_pCorridor, new Vector3(0.3f, 4.2f, 56f), new Vector3(-2.6f, 2.1f, 0f), wall, "WallL");
        Box(_pCorridor, new Vector3(0.3f, 4.2f, 56f), new Vector3(2.6f, 2.1f, 0f), wall, "WallR");
        Box(_pCorridor, new Vector3(5.2f, 0.3f, 56f), new Vector3(0f, 4.2f, 0f), wall, "Ceiling");

        DressCorridorSurfaces(_pCorridor, 2.6f, 4.2f, -28f, 28f);
        DressWallPanels(_pCorridor, new Vector3(-2.45f, 0f, 0f), 56f, 4.2f, false, 1f, 1.05f, 4f);
        DressWallPanels(_pCorridor, new Vector3(2.45f, 0f, 0f), 56f, 4.2f, false, -1f, 1.05f, 4f);

        // 바닥 — 가운데가 닳는다. 양옆 가장자리는 아직 광이 남아 있다.
        Box(_pCorridor, new Vector3(2.5f, 0.004f, 54f), new Vector3(0f, 0.016f, 0f), KEpoxyWorn, "WornLane");
        foreach (float sx in new[] { -1f, 1f })
            Box(_pCorridor, new Vector3(0.1f, 0.006f, 54f), new Vector3(sx * 1.3f, 0.018f, 0f),
                Glow(new Color(0.42f, 0.35f, 0.12f), 0.35f), "LaneLine");
        for (int i = 0; i < 22; i++)
            Box(_pCorridor, new Vector3(rng.RandfRange(0.5f, 1.4f), 0.003f, rng.RandfRange(0.6f, 1.6f)),
                new Vector3(rng.RandfRange(-2.1f, 2.1f), 0.02f, rng.RandfRange(-26f, 26f)), Stain, "FloorStain");

        // ── 문 · 배관 · 천장등 : 달릴 때 흘러가는 것이 있어야 속도가 읽힌다 ──
        for (int i = 0; i < 14; i++)
        {
            float z = 24f - i * 4f;
            Strip(_pCorridor, new Vector3(0f, 4.0f, z), 2.2f, Cool, 2.4f, _pLampMats, _pLights, 1.25f);
            Box(_pCorridor, new Vector3(2.6f, 0.07f, 0.34f), new Vector3(0f, 4.12f, z), KAlu, "LampTrim");
            float side = i % 2 == 0 ? -1f : 1f;

            // 문 — 문틀 · 패널 · 작은 창 · 손잡이 · 번호판.
            var dr = new Node3D { Position = new Vector3(side * 2.42f, 0f, z) };
            _pCorridor.AddChild(dr);
            Box(dr, new Vector3(0.14f, 2.5f, 1.72f), new Vector3(0f, 1.25f, 0f), KSteelDark, "DoorFrame");
            Box(dr, new Vector3(0.1f, 2.26f, 1.44f), new Vector3(-side * 0.02f, 1.13f, 0f), dark, "DoorLeaf");
            Box(dr, new Vector3(0.06f, 0.44f, 0.52f), new Vector3(-side * 0.05f, 1.72f, 0f), KGlassPane, "DoorWindow");
            Box(dr, new Vector3(0.05f, 0.5f, 0.56f), new Vector3(-side * 0.035f, 1.72f, 0f), KSteelDark, "WindowFrame");
            Box(dr, new Vector3(0.07f, 0.05f, 0.18f), new Vector3(-side * 0.06f, 1.02f, -0.52f), KAlu, "Handle");
            Box(dr, new Vector3(0.1f, 0.3f, 0.5f), new Vector3(-side * 0.03f, 0.28f, 0f), KAlu, "KickPlate");
            Label(dr, new Vector3(-side * 0.06f, 2.26f, 0f), new Vector2(0.46f, 0.17f),
                new Vector3(0f, side > 0f ? -90f : 90f, 0f));
            // 문 옆 카드 리더.
            Box(dr, new Vector3(0.07f, 0.14f, 0.1f), new Vector3(-side * 0.04f, 1.18f, side * 1.02f),
                KPlasticDark, "Reader");
            Box(dr, new Vector3(0.02f, 0.03f, 0.03f), new Vector3(-side * 0.07f, 1.22f, side * 1.02f),
                Glow(new Color(0.26f, 0.68f, 0.38f), 1.8f), "ReaderLed");

            // 문 위 인방 · 배관.
            Box(_pCorridor, new Vector3(0.1f, 0.14f, 1.9f), new Vector3(side * 2.4f, 2.6f, z), steel, "Lintel");
            Pipe(_pCorridor, new Vector3(side * 2.3f, 3.7f, z - 2f), new Vector3(side * 2.3f, 3.7f, z + 2f),
                0.1f, steel, "Pipe", 8);
        }
        // 길게 흐르는 배관 · 덕트 · 케이블 트레이 — 굵기를 전부 다르게.
        foreach (var (x, y, r) in new[] { (-2.2f, 3.9f, 0.13f), (-1.95f, 3.76f, 0.08f), (2.2f, 3.86f, 0.16f) })
            Pipe(_pCorridor, new Vector3(x, y, -27f), new Vector3(x, y, 27f), r, steel, "RunPipe", 8);
        Duct(_pCorridor, new Vector3(0.95f, 3.82f, -27f), new Vector3(0.95f, 3.82f, 27f), 0.62f, 0.46f,
            KPanelDark, KSteelDark, 4.4f);
        CableTray(_pCorridor, new Vector3(-1.05f, 3.6f, -27f), new Vector3(-1.05f, 3.6f, 27f), 0.5f, KSteelDark, KCable);
        for (int i = -13; i <= 13; i++)
        {
            Box(_pCorridor, new Vector3(5.0f, 0.16f, 0.18f), new Vector3(0f, 4.02f, i * 2f), KPanelDark, "CeilRib");
            Pipe(_pCorridor, new Vector3(-2.2f, 4.1f, i * 4f), new Vector3(-2.2f, 4.0f, i * 4f), 0.02f,
                KSteelDark, "Hanger", 4);
        }

        // ── 벽 소품 : 두꺼운 것은 **왼쪽 z>0 · 오른쪽 z<0** 에만 ───────
        // 소화전함 · 분전반 · 구급함 — 왼쪽(도주 컷에서 카메라 반대편).
        foreach (var (z, kind) in new[] { (17f, 0), (5f, 1), (-3f, 2), (11f, 1) })
        {
            var at = new Vector3(-2.44f, 0f, z);
            var cab = new Node3D { Position = at, RotationDegrees = new Vector3(0f, 90f, 0f) };
            _pCorridor.AddChild(cab);
            if (kind == 0)
            {
                Chamfer(cab, new Vector3(0.7f, 0.9f, 0.22f), new Vector3(0f, 1.25f, 0f),
                    Mat(new Color(0.34f, 0.08f, 0.06f), 0.80f), 0.012f, "HoseBox");
                Box(cab, new Vector3(0.6f, 0.78f, 0.03f), new Vector3(0f, 1.25f, 0.12f),
                    Mat(new Color(0.24f, 0.06f, 0.05f), 0.86f), "HoseDoor");
                Box(cab, new Vector3(0.05f, 0.1f, 0.04f), new Vector3(0.24f, 1.25f, 0.14f), KAlu, "Latch");
                Label(cab, new Vector3(0f, 1.82f, 0.12f), new Vector2(0.42f, 0.14f), Vector3.Zero);
            }
            else if (kind == 1)
            {
                WallPanel(cab, new Vector3(0f, 1.55f, 0f), Vector3.Zero, 0.62f, 0.78f,
                    new Color(0.30f, 0.68f, 1f), 1.1f, _pLampMats, (ulong)(40 + z));
                Pipe(cab, new Vector3(0f, 1.95f, 0f), new Vector3(0f, 3.5f, 0f), 0.028f, KSteelDark, "Riser", 6);
                CableSag(cab, new Vector3(0.2f, 1.9f, 0f), new Vector3(0.9f, 3.4f, 0f), 0.1f, 0.018f, KCable, 4);
            }
            else
            {
                Chamfer(cab, new Vector3(0.44f, 0.5f, 0.18f), new Vector3(0f, 1.5f, 0f), KFoam, 0.01f, "FirstAid");
                Box(cab, new Vector3(0.3f, 0.07f, 0.02f), new Vector3(0f, 1.5f, 0.1f),
                    Mat(new Color(0.18f, 0.34f, 0.20f), 0.8f), "Cross");
                Box(cab, new Vector3(0.07f, 0.3f, 0.02f), new Vector3(0f, 1.5f, 0.1f),
                    Mat(new Color(0.18f, 0.34f, 0.20f), 0.8f), "Cross");
            }
        }
        // 오른쪽 벽 : 두꺼운 것은 z<0 쪽에만.
        foreach (var z in new[] { -7f, -15f })
        {
            var cab = new Node3D { Position = new Vector3(2.44f, 0f, z), RotationDegrees = new Vector3(0f, -90f, 0f) };
            _pCorridor.AddChild(cab);
            Chamfer(cab, new Vector3(0.8f, 1.1f, 0.24f), new Vector3(0f, 1.3f, 0f), KPanel, 0.014f, "UtilBox");
            Louver(cab, new Vector3(0f, 1.55f, 0.13f), 0.5f, 0.22f, Vector3.Zero, 4);
            Box(cab, new Vector3(0.68f, 0.02f, 0.02f), new Vector3(0f, 1.0f, 0.13f), KSteelDark, "Seam");
            Box(cab, new Vector3(0.05f, 0.12f, 0.035f), new Vector3(0.3f, 1.1f, 0.14f), KAlu, "Handle");
        }
        // 양쪽 벽에 **얇게** 붙는 것들 — 카메라가 스쳐도 걸리지 않는다.
        for (int i = 0; i < 12; i++)
        {
            float z = 22f - i * 4.3f;
            float side = i % 3 == 0 ? 1f : -1f;
            Label(_pCorridor, new Vector3(side * 2.43f, 2.05f, z), new Vector2(0.5f, 0.2f),
                new Vector3(0f, side > 0f ? -90f : 90f, 0f));
            if (i % 2 == 0)
                Box(_pCorridor, new Vector3(0.05f, 0.1f, 0.3f), new Vector3(side * 2.42f, 0.55f, z + 1.4f),
                    KSteelDark, "Socket");
        }
        // 바닥 구역 표시 — 달리는 동안 발밑으로 흘러간다.
        foreach (float z in new[] { 20f, 8f, -4f, -16f })
            WarnStripe(_pCorridor, new Vector3(0f, 0.024f, z), 4.6f, 0.2f, 0.004f, new Vector3(90f, 0f, 0f), 9);

        // ── 차폐문 — 복도 안쪽. 실제로 내려와 닫힌다 ────────────────────
        _pBulkFrame = new Node3D { Position = new Vector3(0f, 0f, -20f) };
        _pCorridor.AddChild(_pBulkFrame);
        Box(_pBulkFrame, new Vector3(5.6f, 0.5f, 0.9f), new Vector3(0f, 4.3f, 0f), steel, "Lintel");
        Box(_pBulkFrame, new Vector3(0.5f, 4.2f, 0.9f), new Vector3(-2.75f, 2.1f, 0f), steel);
        Box(_pBulkFrame, new Vector3(0.5f, 4.2f, 0.9f), new Vector3(2.75f, 2.1f, 0f), steel);
        // 문틀 둘레의 볼트 · 가이드 레일 — '진짜로 움직이는 문' 의 근거.
        foreach (float sx in new[] { -1f, 1f })
        {
            BoltRow(_pBulkFrame, new Vector3(sx * 2.62f, 0.4f, 0.47f), new Vector3(sx * 2.62f, 4.0f, 0.47f),
                7, 0.045f, KAlu);
            Box(_pBulkFrame, new Vector3(0.12f, 4.2f, 0.16f), new Vector3(sx * 2.44f, 2.1f, 0.3f),
                KSteelDark, "Guide");
        }
        WarnStripe(_pBulkFrame, new Vector3(0f, 0.22f, 0.5f), 5.4f, 0.2f, 0.02f, Vector3.Zero, 16);
        _pBulkhead = Box(_pBulkFrame, new Vector3(5.0f, 4.0f, 0.42f), new Vector3(0f, 6.2f, 0f),
            Mat(new Color(0.35f, 0.37f, 0.40f), 0.3f, 0.9f), "Bulkhead");
        for (int i = -2; i <= 2; i++)
            Box(_pBulkhead, new Vector3(0.22f, 3.8f, 0.08f), new Vector3(i * 1.0f, 0f, 0.26f), dark);
        // 문짝에 리브 · 볼트 · 경고 빗금을 더해 두께를 만든다.
        foreach (float y in new[] { -1.5f, 0f, 1.5f })
            Box(_pBulkhead, new Vector3(4.9f, 0.14f, 0.1f), new Vector3(0f, y, 0.27f), KAlu, "Rib");
        BoltRow(_pBulkhead, new Vector3(-2.2f, 1.82f, 0.28f), new Vector3(2.2f, 1.82f, 0.28f), 9, 0.05f, KSteelDark);
        WarnStripe(_pBulkhead, new Vector3(0f, -1.82f, 0.3f), 4.8f, 0.24f, 0.02f, Vector3.Zero, 14);
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
        Box(_pCorridor, new Vector3(3.0f, 0.1f, 0.12f), new Vector3(0f, 2.9f, -30.2f), KPanelDark, "FarSign");
        Pipe(_pCorridor, new Vector3(-2.2f, 3.9f, -30f), new Vector3(-2.2f, 3.9f, -21f), 0.13f, steel, "FarPipe", 8);
        _pCorridor.AddChild(new OmniLight3D
        {
            Name = "FarFill", Position = new Vector3(0f, 3.2f, -24f),
            LightColor = new Color(0.55f, 0.6f, 0.7f), LightEnergy = 1.1f, OmniRange = 12f, ShadowEnabled = false,
        });

        // 비상등 — 재난 전환에서 켜진다. 등 뒤에 금속 갓을 달아 벽에서 띄운다.
        for (int i = 0; i < 7; i++)
        {
            float z = 20f - i * 7f;
            var m = Glow(new Color(1f, 0.16f, 0.1f), 0f);
            _pRedMats.Add(m);
            Box(_pCorridor, new Vector3(0.1f, 0.3f, 0.34f), new Vector3(-2.45f, 3.3f, z), KSteelDark, "RedHood");
            Box(_pCorridor, new Vector3(0.3f, 0.18f, 0.18f), new Vector3(-2.38f, 3.3f, z), m);
            var l = new OmniLight3D
            {
                Position = new Vector3(-2.0f, 3.2f, z), LightColor = new Color(1f, 0.14f, 0.09f),
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
        // 설비 전체가 같이 죽는다 — 바닥 인레이 · 프레임 노드 · 입자 · 헤일로.
        CoreAura(k * k);
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
        CoreAura(0f);
        if (_coreMat != null) _coreMat.EmissionEnergyMultiplier = 0f;
        if (_ringMat != null) _ringMat.EmissionEnergyMultiplier = 0f;
        if (_coreLight != null) _coreLight.LightEnergy = 0f;
        foreach (var m in _floorLampMats) if (m != null) m.EmissionEnergyMultiplier = 0f;
        _coreWobble = 0f;
    }

    // 폭발 — 멀쩡한 코어를 숨기고 손상된 코어를 꺼낸다. 전환은 섬광 · 연기가 가린다(§26).
    public void PrologueCoreBreach()
    {
        CoreAura(0f);
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
