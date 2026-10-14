using System.Collections.Generic;
using Godot;

namespace NSP.View;

// ── 봉쇄 코어실 : 설비 ──────────────────────────────────────────────────
//
// 설정상 시설의 심장인데 화면에서는 "큰 흰 구체" 하나였다. 구(球)만 키우면 더 커 보이지
// 않는다 — 구를 **붙잡고 있는 기계**가 보여야 그 구가 무엇인지 읽힌다.
//
// 그래서 여기서 짓는 것은 빛이 아니라 설비다.
//   · 받침 : 단마다 테두리 · 볼트 · 점검구 · 단 사이로 새어 나오는 빛
//   · 구속 프레임 : 받침 네 귀에서 올라와 코어 위에서 모이는 아치 + 천장 고정 칼라
//   · 보조 링 : 본 링과 반대로 도는 얇은 자기장 링 둘
//   · 배선 : 칼라에서 내려오는 굵은 케이블, 바닥으로 들어가는 트렁크
//   · 벽 : 냉각 핀 스택 · 덕트 · 점검 통로 · 벽면 제어 콘솔
//   · 공기 : 느리게 떠오르는 에너지 입자, 코어를 감싼 옅은 헤일로
//
// **SF 판타지로 가지 않는다.** 국가 연구시설이 산업 설비로 지어 올린 초과학 장치 —
// 강재 · 볼트 · 냉각 핀 · 경고 띠가 먼저 보이고, 빛은 그 틈으로 샌다.
public partial class EndingCutsceneStage
{
    // 출력에 따라 같이 오르내리는 발광 재질들(바닥 인레이 · 단 이음매 · 프레임 노드 …).
    private readonly List<(StandardMaterial3D Mat, float Base)> _coreGlow = new();
    private readonly List<(OmniLight3D Light, float Base)> _coreGlowLights = new();
    private Node3D _coreAuraRoot, _coreRingA, _coreRingB;
    private GpuParticles3D _coreMotes, _coreHaze;
    private readonly List<MeshInstance3D> _coreHalo = new();
    private readonly List<StandardMaterial3D> _coreHaloMats = new();
    private float _coreAura, _coreAuraT;

    private StandardMaterial3D CoreGlowMat(Color c, float energy)
    {
        var m = Glow(c, 0f);
        m.AlbedoColor = c * 0.22f;          // 꺼져 있을 때는 그냥 어두운 띠로 보여야 한다
        _coreGlow.Add((m, energy));
        return m;
    }

    // 코어실 설비. BuildCoreHall 끝에서 한 번 부른다.
    private void BuildCoreRig()
    {
        var rng = new RandomNumberGenerator { Seed = 20931 };
        Vector3 c = CoreCenter;                       // (0, 8.2, -3)
        var rig = new Node3D { Name = "CoreRig" };
        _setCore.AddChild(rig);

        // ── ① 바닥 : 인레이 · 경고 띠 · 바닥판 ──────────────────────────
        var inlay = CoreGlowMat(new Color(0.40f, 0.68f, 0.92f), 1.6f);
        // 받침을 둘러싼 발광 원 — 36조각으로 끊어 그린다.
        for (int i = 0; i < 36; i++)
        {
            float a = Mathf.Tau * i / 36f;
            var seg = Box(rig, new Vector3(0.10f, 0.035f, 1.62f),
                new Vector3(Mathf.Cos(a) * 9.1f, 0.04f, -3f + Mathf.Sin(a) * 9.1f), inlay, "InlayArc");
            seg.RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(a), 0f);
        }
        // 받침에서 뻗어 나가는 에너지 흔적 — 여덟 줄.
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8f + 0.39f;
            var line = Box(rig, new Vector3(0.14f, 0.03f, 8.2f),
                new Vector3(Mathf.Cos(a) * 11.6f, 0.035f, -3f + Mathf.Sin(a) * 11.6f), inlay, "InlayRay");
            line.RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(a) + 90f, 0f);
        }
        // 바닥판 — 커다란 색면 하나로 두면 '바닥' 이 아니라 배경이 된다.
        var plate = Mat(new Color(0.158f, 0.162f, 0.172f), 0.62f, 0.18f);
        var plateDark = Mat(new Color(0.118f, 0.122f, 0.132f), 0.72f, 0.14f);
        for (int gx = -3; gx <= 3; gx++)
            for (int gz = -3; gz <= 3; gz++)
            {
                if (Mathf.Abs(gx) <= 1 && Mathf.Abs(gz) <= 1) continue;   // 받침 자리
                Box(rig, new Vector3(4.3f, 0.05f, 4.3f), new Vector3(gx * 4.5f, 0.03f, -3f + gz * 4.5f),
                    (gx + gz) % 2 == 0 ? plate : plateDark, "FloorPlate");
            }
        // 점검 해치 · 배수 그레이팅 몇.
        foreach (var at in new[] { new Vector3(-9f, 0f, 3.5f), new Vector3(8.4f, 0f, -8.5f), new Vector3(5.2f, 0f, 6.2f) })
        {
            Box(rig, new Vector3(1.5f, 0.07f, 1.5f), at + new Vector3(0f, 0.045f, 0f), KSteelDark, "Hatch");
            for (int i = 0; i < 6; i++)
                Box(rig, new Vector3(1.32f, 0.03f, 0.08f), at + new Vector3(0f, 0.085f, -0.55f + i * 0.22f),
                    KAlu, "Grate");
        }
        // 받침 둘레 경고 띠.
        foreach (var (p, r) in new[]
                 {
                     (new Vector3(0f, 0.62f, 3.1f), 0f), (new Vector3(0f, 0.62f, -9.1f), 180f),
                     (new Vector3(-6.05f, 0.62f, -3f), -90f), (new Vector3(6.05f, 0.62f, -3f), 90f),
                 })
            WarnStripe(rig, p, 11.4f, 0.26f, 0.03f, new Vector3(0f, r, 0f), 18);

        // ── ② 받침 : 단 테두리 · 볼트 · 이음매 빛 ───────────────────────
        var seamGlow = CoreGlowMat(new Color(0.46f, 0.74f, 1f), 2.0f);
        foreach (var (half, y) in new[] { (6.0f, 0.5f), (4.0f, 1.0f), (2.5f, 1.5f) })
        {
            // 단 윗면 테두리 — 금속 띠 한 줄이 '깎인 단' 을 만든다.
            foreach (var (sx, sz) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
            {
                var size = sx != 0f ? new Vector3(0.14f, 0.12f, half * 2f) : new Vector3(half * 2f, 0.12f, 0.14f);
                Box(rig, size, new Vector3(sx * half, y - 0.06f, -3f + sz * half), KAlu, "TierRim");
                var seam = sx != 0f ? new Vector3(0.06f, 0.05f, half * 2f - 0.3f)
                                    : new Vector3(half * 2f - 0.3f, 0.05f, 0.06f);
                Box(rig, seam, new Vector3(sx * (half + 0.04f), y - 0.22f, -3f + sz * (half + 0.04f)),
                    seamGlow, "TierSeam");
            }
            // 모서리 볼트.
            foreach (float sx in new[] { -1f, 1f })
                BoltRow(rig, new Vector3(sx * (half - 0.25f), y + 0.01f, -3f - half + 0.3f),
                    new Vector3(sx * (half - 0.25f), y + 0.01f, -3f + half - 0.3f),
                    Mathf.Max(3, Mathf.RoundToInt(half)), 0.055f, KSteelDark);
        }
        // 1단 옆면 점검구 여덟.
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8f;
            var at = new Vector3(Mathf.Cos(a) * 6.02f, 0.28f, -3f + Mathf.Sin(a) * 6.02f);
            var d = Box(rig, new Vector3(0.9f, 0.5f, 0.06f), at, KPanelDark, "AccessDoor");
            d.RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(a) + 90f, 0f);
            Box(d, new Vector3(0.18f, 0.03f, 0.03f), new Vector3(0f, -0.1f, 0.04f), KAlu, "DoorHandle");
        }

        // ── ③ 구속 프레임 : 네 아치 + 천장 칼라 ─────────────────────────
        float apexY = 15.4f;
        var nodeGlow = CoreGlowMat(new Color(0.55f, 0.80f, 1f), 2.4f);
        for (int q = 0; q < 4; q++)
        {
            float baseAng = Mathf.Tau * q / 4f + Mathf.Pi * 0.25f;
            var foot = new Vector3(Mathf.Cos(baseAng) * 6.6f, 1.5f, -3f + Mathf.Sin(baseAng) * 6.6f);
            // 발 — 바닥에 볼트로 박힌 베이스 플레이트.
            var plateN = Box(rig, new Vector3(1.5f, 0.28f, 1.5f), foot with { Y = 1.62f }, KSteel, "ArchFoot");
            plateN.RotationDegrees = new Vector3(0f, -Mathf.RadToDeg(baseAng), 0f);
            BoltRow(rig, foot + new Vector3(-0.5f, 0.17f, -0.5f), foot + new Vector3(0.5f, 0.17f, 0.5f),
                3, 0.07f, KSteelDark);

            // 아치 — 발에서 코어 위 정점까지 7토막.
            Vector3 prev = foot with { Y = 1.76f };
            var apex = new Vector3(0f, apexY, -3f);
            for (int i = 1; i <= 7; i++)
            {
                float t = i / 7f;
                // 바깥으로 한 번 부풀었다가 모인다 — 직선으로 이으면 그냥 지지대가 된다.
                float bulge = Mathf.Sin(t * Mathf.Pi) * 1.9f;
                var p = prev.Lerp(apex, 0f);
                p = (foot with { Y = 1.76f }).Lerp(apex, t);
                var outward = new Vector3(Mathf.Cos(baseAng), 0f, Mathf.Sin(baseAng)) * bulge;
                p += outward;
                Pipe(rig, prev, p, i >= 6 ? 0.26f : 0.34f, KSteel, "Arch", 8);
                // 토막 사이 조인트 — 이음매가 있어야 '조립된 구조물' 이다.
                Cyl(rig, 0.40f, 0.20f, p, KSteelDark, "ArchJoint");
                if (i == 4) Box(rig, new Vector3(0.42f, 0.14f, 0.14f), p + new Vector3(0f, 0.3f, 0f), nodeGlow, "ArchNode");
                prev = p;
            }
            // 아치 중간을 서로 묶는 가로대.
            float ringY = 9.6f;
            var a0 = new Vector3(Mathf.Cos(baseAng) * 6.1f, ringY, -3f + Mathf.Sin(baseAng) * 6.1f);
            float nextAng = Mathf.Tau * ((q + 1) % 4) / 4f + Mathf.Pi * 0.25f;
            var a1 = new Vector3(Mathf.Cos(nextAng) * 6.1f, ringY, -3f + Mathf.Sin(nextAng) * 6.1f);
            Pipe(rig, a0, a1, 0.1f, KSteelDark, "ArchTie", 6);
        }
        // 천장 칼라 — 아치가 모이는 자리. 여기서 코어를 위에서 물고 있다.
        Cyl(rig, 2.3f, 0.7f, new Vector3(0f, apexY + 0.3f, -3f), KSteel, "Collar");
        Cyl(rig, 2.62f, 0.2f, new Vector3(0f, apexY + 0.72f, -3f), KAlu, "CollarRim");
        Cyl(rig, 1.55f, 0.45f, new Vector3(0f, apexY + 0.9f, -3f), KPanelDark, "CollarHub");
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8f;
            Box(rig, new Vector3(0.22f, 0.5f, 0.22f),
                new Vector3(Mathf.Cos(a) * 2.42f, apexY + 0.3f, -3f + Mathf.Sin(a) * 2.42f), KSteelDark, "CollarBolt");
            // 칼라에서 천장으로 올라가는 행거.
            Pipe(rig, new Vector3(Mathf.Cos(a) * 2.0f, apexY + 1.1f, -3f + Mathf.Sin(a) * 2.0f),
                new Vector3(Mathf.Cos(a) * 2.6f, 21.6f, -3f + Mathf.Sin(a) * 2.6f), 0.11f, KSteelDark, "Hanger", 6);
        }
        // 칼라에서 코어 쪽으로 내려오는 굵은 케이블 다발.
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f + 0.3f;
            CableSag(rig, new Vector3(Mathf.Cos(a) * 1.9f, apexY - 0.1f, -3f + Mathf.Sin(a) * 1.9f),
                new Vector3(Mathf.Cos(a) * 3.1f, c.Y + 3.3f, -3f + Mathf.Sin(a) * 3.1f), 0.5f, 0.1f, KCable, 4);
        }
        // 받침에서 바닥으로 들어가는 트렁크 — 설비는 어딘가로 연결되어 있어야 한다.
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f + 0.52f;
            var from = new Vector3(Mathf.Cos(a) * 4.2f, 1.1f, -3f + Mathf.Sin(a) * 4.2f);
            var to = new Vector3(Mathf.Cos(a) * 10.5f, 0.35f, -3f + Mathf.Sin(a) * 10.5f);
            Duct(rig, from, to, 0.62f, 0.52f, KPanelDark, KSteelDark, 2.2f);
            CableSag(rig, from + new Vector3(0f, 0.5f, 0f), to + new Vector3(0f, 0.45f, 0f), 0.25f, 0.075f, KCable, 4);
        }

        // ── ④ 보조 링 : 본 링과 반대로 도는 자기장 링 둘 ────────────────
        var ringSteel = Mat(new Color(0.265f, 0.278f, 0.305f), 0.36f, 0.88f);
        _coreRingA = new Node3D { Name = "AuxRingA", Position = c };
        _coreRingB = new Node3D { Name = "AuxRingB", Position = c };
        rig.AddChild(_coreRingA);
        rig.AddChild(_coreRingB);
        foreach (var (root, rad, nodes) in new[] { (_coreRingA, 5.75f, 10), (_coreRingB, 6.6f, 14) })
        {
            root.AddChild(new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = rad - 0.11f, OuterRadius = rad, Rings = 40, RingSegments = 8 },
                MaterialOverride = ringSteel,
            });
            var nm = CoreGlowMat(new Color(0.50f, 0.78f, 1f), 2.2f);
            for (int i = 0; i < nodes; i++)
            {
                float a = Mathf.Tau * i / nodes;
                var seg = Box(root, new Vector3(0.30f, 0.30f, 0.16f),
                    new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, 0f), i % 3 == 0 ? nm : (Material)KSteelDark,
                    "RingNode");
                seg.RotationDegrees = new Vector3(0f, 0f, Mathf.RadToDeg(a));
            }
        }
        _coreRingA.RotationDegrees = new Vector3(74f, 0f, 12f);
        _coreRingB.RotationDegrees = new Vector3(-26f, 38f, 0f);

        // ── ⑤ 벽 : 냉각 핀 스택 · 덕트 · 점검 통로 · 제어 콘솔 ──────────
        var finMat = Mat(new Color(0.232f, 0.242f, 0.262f), 0.48f, 0.70f);
        for (int i = -2; i <= 2; i++)
        {
            float x = i * 6.1f;
            var stack = new Node3D { Position = new Vector3(x, 0f, -15.1f) };
            rig.AddChild(stack);
            Box(stack, new Vector3(4.0f, 11.5f, 1.1f), new Vector3(0f, 5.75f, 0f), KPanelDark, "CoolerBody");
            for (int f = 0; f < 17; f++)
                Box(stack, new Vector3(4.3f, 0.12f, 1.5f), new Vector3(0f, 1.1f + f * 0.62f, 0.14f), finMat, "Fin");
            Box(stack, new Vector3(4.4f, 0.4f, 1.3f), new Vector3(0f, 11.7f, 0f), KSteel, "CoolerCap");
            // 위로 빠지는 덕트.
            Duct(stack, new Vector3(0f, 11.9f, 0f), new Vector3(0f, 20.8f, 0f), 1.5f, 1.5f, KPanelDark, KSteelDark, 3.0f);
            Box(stack, new Vector3(1.5f, 0.9f, 0.1f), new Vector3(0f, 0.6f, 0.6f), KSteelDark, "CoolerDoor");
            var pilot = CoreGlowMat(new Color(0.35f, 1f, 0.62f), 2.0f);
            Box(stack, new Vector3(0.16f, 0.16f, 0.1f), new Vector3(1.5f, 1.0f, 0.62f), pilot, "CoolerPilot");
        }
        // 양 옆 벽 : 점검 통로(캣워크) + 난간 + 아래쪽 덕트.
        foreach (float sx in new[] { -1f, 1f })
        {
            float wx = sx * 14.6f;
            Box(rig, new Vector3(2.6f, 0.18f, 36f), new Vector3(wx, 6.4f, -1f), KSteelDark, "Catwalk");
            for (int i = 0; i < 25; i++)
                Box(rig, new Vector3(2.4f, 0.05f, 0.1f), new Vector3(wx, 6.52f, -18f + i * 1.45f), KAlu, "CatwalkGrate");
            for (int i = 0; i <= 12; i++)
            {
                Pipe(rig, new Vector3(wx - sx * 1.3f, 6.5f, -17f + i * 2.9f),
                    new Vector3(wx - sx * 1.3f, 7.6f, -17f + i * 2.9f), 0.045f, KSteelDark, "RailPost", 6);
                // 캣워크를 벽에 매단 브래킷.
                Pipe(rig, new Vector3(wx + sx * 1.2f, 6.3f, -17f + i * 2.9f),
                    new Vector3(wx + sx * 2.3f, 7.8f, -17f + i * 2.9f), 0.06f, KSteelDark, "Bracket", 6);
            }
            Pipe(rig, new Vector3(wx - sx * 1.3f, 7.6f, -18.5f), new Vector3(wx - sx * 1.3f, 7.6f, 16.5f),
                0.055f, KAlu, "Rail", 6);
            // 벽을 따라 흐르는 덕트 · 케이블 트레이.
            Duct(rig, new Vector3(sx * 16.0f, 12.6f, -15f), new Vector3(sx * 16.0f, 12.6f, 15f),
                1.0f, 0.8f, KPanelDark, KSteelDark, 4.0f);
            CableTray(rig, new Vector3(sx * 15.9f, 10.6f, -15f), new Vector3(sx * 15.9f, 10.6f, 15f),
                0.7f, KSteelDark, KCable);
            // 벽면 제어 콘솔 둘.
            foreach (float z in new[] { -8.5f, 1.5f })
            {
                var con = new Node3D { Position = new Vector3(sx * 14.6f, 0f, z), RotationDegrees = new Vector3(0f, sx > 0f ? -90f : 90f, 0f) };
                rig.AddChild(con);
                Chamfer(con, new Vector3(3.4f, 1.0f, 1.3f), new Vector3(0f, 0.5f, 0f), KPanel, 0.04f, "ConsoleBody");
                var top = Box(con, new Vector3(3.3f, 0.12f, 1.0f), new Vector3(0f, 1.12f, 0.1f), KPanelDark, "ConsoleTop");
                top.RotationDegrees = new Vector3(-22f, 0f, 0f);
                var read = CoreGlowMat(new Color(0.40f, 0.82f, 1f), 1.8f);
                for (int i = 0; i < 5; i++)
                    Box(top, new Vector3(0.5f, 0.02f, 0.3f), new Vector3(-1.2f + i * 0.6f, 0.08f, 0f), read, "Readout");
                WarnStripe(con, new Vector3(0f, 0.16f, 0.67f), 3.3f, 0.2f, 0.02f, Vector3.Zero, 10);
                Louver(con, new Vector3(0f, 0.6f, 0.67f), 1.2f, 0.34f, Vector3.Zero, 5);
            }
        }
        // 캣워크 아래 작업등 — 벽 쪽 바닥이 완전히 죽으면 홀의 크기가 안 읽힌다.
        foreach (float sx in new[] { -1f, 1f })
            for (int i = 0; i < 5; i++)
            {
                float z = -13f + i * 6.5f;
                var lm = CoreGlowMat(new Color(0.78f, 0.86f, 1f), 2.2f);
                Box(rig, new Vector3(0.9f, 0.08f, 0.22f), new Vector3(sx * 14.1f, 6.24f, z), lm, "WorkLamp");
                var wl = new OmniLight3D
                {
                    Position = new Vector3(sx * 13.2f, 5.9f, z),
                    LightColor = new Color(0.62f, 0.70f, 0.86f), LightEnergy = 0f,
                    OmniRange = 13f, ShadowEnabled = false,
                };
                rig.AddChild(wl);
                _coreGlowLights.Add((wl, 1.5f));
            }

        // 뒷벽 위쪽 대형 패널 — 벽이 통째로 비면 홀이 상자처럼 보인다.
        for (int i = -4; i <= 4; i++)
        {
            Box(rig, new Vector3(3.3f, 8.0f, 0.22f), new Vector3(i * 3.6f, 16.5f, -15.5f), KPanel, "BackPanel");
            Box(rig, new Vector3(0.18f, 8.2f, 0.34f), new Vector3(i * 3.6f + 1.8f, 16.5f, -15.45f), KSteelDark, "PanelSeam");
            if (i % 2 == 0)
                BoltRow(rig, new Vector3(i * 3.6f, 13.0f, -15.33f), new Vector3(i * 3.6f, 20.0f, -15.33f),
                    6, 0.07f, KAlu);
        }

        // ── ⑤-b 코어 구속 밴드 · 고정 장치 ──────────────────────────────
        // 구가 통째로 하얗게 보이는 가장 큰 이유는 표면에 **아무 선도 없기** 때문이다.
        // 얇은 띠 몇 줄만 둘러도 구의 크기와 둥근 형태가 그 자리에서 읽힌다.
        var bandMat = Mat(new Color(0.105f, 0.112f, 0.126f), 0.42f, 0.70f);
        foreach (var rot in new[] { new Vector3(90f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0f, 0f, 0f),
                                    new Vector3(62f, 0f, 34f) })
            rig.AddChild(new MeshInstance3D
            {
                Name = "CoreBand",
                Mesh = new TorusMesh { InnerRadius = 3.20f, OuterRadius = 3.33f, Rings = 36, RingSegments = 6 },
                Position = c, RotationDegrees = rot, MaterialOverride = bandMat,
            });
        // 위아래 고정 장치에 집게발을 붙인다 — 맨 상자는 구 앞에 떠 있는 판으로 보인다.
        foreach (var (clamp, dir) in new[] { (_clampTop, -1f), (_clampBottom, 1f) })
        {
            if (clamp == null) continue;
            Cyl(clamp, 1.35f, 0.3f, new Vector3(0f, dir * 0.9f, 0f), KAlu, "ClampRim");
            for (int i = 0; i < 4; i++)
            {
                float a = Mathf.Tau * i / 4f + 0.78f;
                var arm = Box(clamp, new Vector3(0.34f, 1.5f, 0.34f),
                    new Vector3(Mathf.Cos(a) * 1.5f, dir * 1.25f, Mathf.Sin(a) * 1.5f), KSteel, "ClampArm");
                arm.RotationDegrees = new Vector3(Mathf.Sin(a) * 19f * dir, 0f, -Mathf.Cos(a) * 19f * dir);
                Box(clamp, new Vector3(0.52f, 0.26f, 0.52f),
                    new Vector3(Mathf.Cos(a) * 1.78f, dir * 1.95f, Mathf.Sin(a) * 1.78f), KSteelDark, "ClampPad");
            }
            var cg = CoreGlowMat(new Color(0.48f, 0.76f, 1f), 1.8f);
            Box(clamp, new Vector3(1.9f, 0.09f, 0.09f), new Vector3(0f, dir * 0.78f, 0f), cg, "ClampSeam");
            Box(clamp, new Vector3(0.09f, 0.09f, 1.9f), new Vector3(0f, dir * 0.78f, 0f), cg, "ClampSeam");
        }

        // ── ⑥ 공기 : 에너지 입자 · 헤일로 ───────────────────────────────
        _coreAuraRoot = new Node3D { Name = "CoreAura", Position = c };
        rig.AddChild(_coreAuraRoot);
        // 코어를 감싼 옅은 껍질 셋 — 안쪽에서 바깥으로 갈수록 묽어진다.
        foreach (var (rad, alpha) in new[] { (3.62f, 0.115f), (4.25f, 0.050f), (5.05f, 0.022f) })
        {
            var hm = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.52f, 0.76f, 1f, alpha),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Front,     // 안쪽 면만 — 구 뒤로 빛이 번진다
                DisableReceiveShadows = true,
                NoDepthTest = false,
            };
            var h = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = rad, Height = rad * 2f, RadialSegments = 20, Rings = 12 },
                MaterialOverride = hm,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            _coreAuraRoot.AddChild(h);
            _coreHalo.Add(h);
            _coreHaloMats.Add(hm);
        }
        // 느리게 떠오르는 에너지 입자. **잘고 많아야** 먼지가 된다 —
        // 크게 키우면 렌즈 앞에 붙은 흰 구슬이 되어 코어를 가려 버린다.
        _coreMotes = Motes(_coreAuraRoot, Vector3.Zero, 5.6f, 220, 0.085f, new Color(0.62f, 0.84f, 1f), 0.42f);
        // 받침 둘레의 낮은 연무 — 빛이 공기 중에 있다는 느낌.
        _coreHaze = Ash(rig, new Vector3(0f, 1.7f, -3f), new Vector3(7.5f, 1.0f, 7.5f),
            new Vector3(0.1f, 0.4f, 0f), 4.5f, 20, 0.030f, new Color(0.40f, 0.56f, 0.74f), 10.0);

        CoreAura(0f);
    }

    // 떠다니는 발광 입자. Ash() 는 연기용이라 가산 합성이 아니다 — 에너지 입자는
    // 겹칠수록 밝아져야 한다.
    private GpuParticles3D Motes(Node3D parent, Vector3 pos, float radius, int amount, float size,
        Color tint, float alpha)
    {
        var mat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 180f,
            InitialVelocityMin = 0.08f,
            InitialVelocityMax = 0.42f,
            Gravity = new Vector3(0f, 0.12f, 0f),
            ScaleMin = size * 0.4f,
            ScaleMax = size * 1.6f,
            Damping = new Vector2(0.1f, 0.4f),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = radius,
        };
        var p = new GpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = 7.0,
            Preprocess = 6.0f,
            Emitting = false,
            ProcessMaterial = mat,
            DrawPass1 = new QuadMesh { Size = new Vector2(1f, 1f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = tint with { A = alpha },
                AlbedoTexture = SoftDot(),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                BillboardKeepScale = true,          // 없으면 scale_min/max 가 무시된다
                DisableReceiveShadows = true,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(p);
        return p;
    }

    // 코어 설비의 '살아 있음' 정도. 0 = 완전 정지(검은 띠만 남는다), 1 = 정상 가동.
    public void CoreAura(float k)
    {
        k = Mathf.Max(0f, k);
        _coreAura = k;
        foreach (var (m, b) in _coreGlow) if (m != null) m.EmissionEnergyMultiplier = b * k;
        foreach (var (l, b) in _coreGlowLights) if (l != null) l.LightEnergy = b * k;
        for (int i = 0; i < _coreHalo.Count; i++)
        {
            if (_coreHalo[i] == null) continue;
            _coreHalo[i].Visible = k > 0.02f;
        }
        if (_coreMotes != null) _coreMotes.Emitting = k > 0.05f;
        if (_coreHaze != null) _coreHaze.Emitting = k > 0.05f;
    }

    public float CoreAuraLevel => _coreAura;

    // 설비는 멈춰 있지 않다 — 보조 링이 천천히 돌고, 헤일로가 숨 쉬듯 뛴다.
    // 폭발 뒤에는 CoreAura(0) 이 걸려 전부 꺼진다.
    private void TickCoreRig(float delta)
    {
        if (_setCore == null || !_setCore.Visible) return;
        _coreAuraT += delta;
        if (_coreRingA != null)
            _coreRingA.RotationDegrees = new Vector3(74f, _coreAuraT * (6f + 10f * _coreAura), 12f);
        if (_coreRingB != null)
            _coreRingB.RotationDegrees = new Vector3(-26f, 38f - _coreAuraT * (4f + 7f * _coreAura), 0f);
        if (_coreAura <= 0.02f) return;
        // 맥동 — 세 껍질이 조금씩 다른 박자로 뛴다. 같이 뛰면 기계적으로 보인다.
        for (int i = 0; i < _coreHaloMats.Count; i++)
        {
            float f = 0.55f + i * 0.21f;
            float pulse = 0.78f + 0.22f * Mathf.Sin(_coreAuraT * Mathf.Tau * f + i * 1.7f);
            var baseA = new[] { 0.115f, 0.050f, 0.022f }[Mathf.Min(i, 2)];
            _coreHaloMats[i].AlbedoColor = _coreHaloMats[i].AlbedoColor with
            {
                A = baseA * Mathf.Min(1.4f, _coreAura) * pulse,
            };
            if (_coreHalo[i] != null)
                _coreHalo[i].Scale = Vector3.One * (1f + 0.035f * Mathf.Sin(_coreAuraT * Mathf.Tau * f * 0.7f + i));
        }
    }
}
