using System.Collections.Generic;
using Godot;

namespace NSP.View;

// ── 지상 폐허 : 잔해 키트 ───────────────────────────────────────────────
//
// "단순 도형을 여기저기 놓은" 것과 "무너진 건물" 의 차이는 **종류**다.
// 콘크리트 덩어리 · 철근 · 벽체 조각 · 천장 패널 · 철골 · 깨진 장비 · 부서진 가구 ·
// 유리 파편 · 잿더미가 서로 다르게 생기고 서로 다른 재질이어야 비로소 현장이 된다.
//
// 시신은 **보여 주되 과하지 않게** 간다. 무너진 구조물 아래로 팔다리만 나와 있거나,
// 보호복을 입은 채 엎드려 있거나, 도망치다 넘어진 자세. 잔혹함이 아니라
// 재난의 무게가 남아야 한다.
public partial class EndingCutsceneStage
{
    // ── 폐허 재질 ───────────────────────────────────────────────────────
    //
    // 전부 붉은 하늘 아래 놓이므로 **색이 아니라 명도와 거칠기**로 구분한다.
    private static StandardMaterial3D _rAsphalt, _rConcrete, _rConcreteOld, _rConcretePale, _rRebar,
        _rSteelBent, _rDust, _rGravel, _rScorch, _rPlastic, _rFabric, _rGlassShard, _rSuit, _rSkin;

    private static StandardMaterial3D RAsphalt => _rAsphalt ??= Mat(new Color(0.086f, 0.074f, 0.068f), 0.97f);
    private static StandardMaterial3D RConcrete => _rConcrete ??= Mat(new Color(0.205f, 0.180f, 0.158f), 0.96f);
    private static StandardMaterial3D RConcreteOld => _rConcreteOld ??= Mat(new Color(0.152f, 0.130f, 0.114f), 0.99f);
    private static StandardMaterial3D RConcretePale => _rConcretePale ??= Mat(new Color(0.285f, 0.252f, 0.222f), 0.93f);
    private static StandardMaterial3D RRebar => _rRebar ??= Mat(new Color(0.178f, 0.108f, 0.066f), 0.88f, 0.45f);
    private static StandardMaterial3D RSteelBent => _rSteelBent ??= Mat(new Color(0.124f, 0.108f, 0.100f), 0.52f, 0.85f);
    private static StandardMaterial3D RDust => _rDust ??= Mat(new Color(0.252f, 0.212f, 0.176f), 1.0f);
    private static StandardMaterial3D RGravel => _rGravel ??= Mat(new Color(0.132f, 0.114f, 0.100f), 1.0f);
    private static StandardMaterial3D RScorch => _rScorch ??= Mat(new Color(0.030f, 0.026f, 0.024f), 1.0f);
    private static StandardMaterial3D RPlastic => _rPlastic ??= Mat(new Color(0.108f, 0.100f, 0.094f), 0.70f);
    private static StandardMaterial3D RFabric => _rFabric ??= Mat(new Color(0.212f, 0.180f, 0.150f), 0.99f);
    private static StandardMaterial3D RSuit => _rSuit ??= Mat(new Color(0.430f, 0.392f, 0.272f), 0.93f);
    private static StandardMaterial3D RSkin => _rSkin ??= Mat(new Color(0.074f, 0.062f, 0.056f), 0.97f);

    private static StandardMaterial3D RGlassShard => _rGlassShard ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.50f, 0.42f, 0.36f, 0.42f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.12f, Metallic = 0.3f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // ── 조각 ────────────────────────────────────────────────────────────

    // 깨진 콘크리트 덩어리 — 상자 하나가 아니라 크기가 다른 덩어리 서넛이 붙어 있고,
    // 깨진 면에서 철근이 삐져나온다.
    private Node3D Chunk(Node3D parent, Vector3 at, float yaw, float scale, RandomNumberGenerator rng,
        int rebar = 3)
    {
        var n = new Node3D { Name = "Chunk", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        int lumps = rng.RandiRange(2, 4);
        var mats = new[] { RConcrete, RConcreteOld, RConcretePale };
        for (int i = 0; i < lumps; i++)
        {
            var sz = new Vector3(rng.RandfRange(0.5f, 1.5f), rng.RandfRange(0.3f, 0.9f),
                rng.RandfRange(0.5f, 1.4f)) * scale;
            var b = Box(n, sz, new Vector3(rng.RandfRange(-0.5f, 0.5f) * scale, sz.Y * 0.42f,
                rng.RandfRange(-0.5f, 0.5f) * scale), mats[rng.RandiRange(0, 2)], "Lump");
            b.RotationDegrees = new Vector3(rng.RandfRange(-18f, 18f), rng.RandfRange(0f, 180f),
                rng.RandfRange(-18f, 18f));
        }
        // 철근 — 한 번 꺾여 나온다. 직선 하나면 못처럼 보인다.
        for (int i = 0; i < rebar; i++)
        {
            var a = new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(0.1f, 0.4f),
                rng.RandfRange(-0.6f, 0.6f)) * scale;
            var mid = a + new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(0.35f, 0.8f),
                rng.RandfRange(-0.5f, 0.5f)) * scale;
            var end = mid + new Vector3(rng.RandfRange(-0.9f, 0.9f), rng.RandfRange(-0.1f, 0.45f),
                rng.RandfRange(-0.9f, 0.9f)) * scale;
            Pipe(n, a, mid, 0.022f * scale, RRebar, "Rebar", 5);
            Pipe(n, mid, end, 0.020f * scale, RRebar, "Rebar", 5);
        }
        // 깨진 자리에 쌓인 가루.
        Box(n, new Vector3(1.9f, 0.025f, 1.7f) * scale, new Vector3(0f, 0.012f, 0f), RDust, "Dust");
        return n;
    }

    // 벽체 조각 — 기울어 선 판. 깨진 윗변이 계단처럼 들쭉날쭉하고 철근이 나와 있다.
    private Node3D WallShard(Node3D parent, Vector3 at, float yaw, float w, float h, float tilt,
        RandomNumberGenerator rng)
    {
        var n = new Node3D { Name = "WallShard", Position = at };
        n.RotationDegrees = new Vector3(tilt, yaw, rng.RandfRange(-7f, 7f));
        parent.AddChild(n);
        Box(n, new Vector3(w, h, 0.26f), new Vector3(0f, h * 0.5f, 0f), RConcreteOld, "Slab");
        // 깨진 윗변 — 폭을 나눠 높이를 들쭉날쭉하게.
        int seg = Mathf.Max(3, Mathf.RoundToInt(w / 0.8f));
        for (int i = 0; i < seg; i++)
        {
            float sw = w / seg;
            float extra = rng.RandfRange(-0.35f, 0.55f);
            if (extra <= 0f) continue;
            Box(n, new Vector3(sw * 1.02f, extra, 0.26f),
                new Vector3(-w * 0.5f + sw * (i + 0.5f), h + extra * 0.5f, 0f), RConcreteOld, "Jag");
        }
        // 노출 철근 — 윗변에서 휘어 올라간다.
        for (int i = 0; i < seg; i++)
        {
            if (rng.Randf() > 0.6f) continue;
            float x = -w * 0.5f + w * (i + 0.5f) / seg;
            var a = new Vector3(x, h, rng.RandfRange(-0.08f, 0.08f));
            var mid = a + new Vector3(rng.RandfRange(-0.1f, 0.1f), rng.RandfRange(0.35f, 0.7f), 0f);
            var end = mid + new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(0.05f, 0.35f),
                rng.RandfRange(-0.3f, 0.3f));
            Pipe(n, a, mid, 0.022f, RRebar, "Rebar", 5);
            Pipe(n, mid, end, 0.020f, RRebar, "Rebar", 5);
        }
        // 벽 안쪽 마감 · 타일 자국 — 판 한 장이 아니라 '벽이었던 것' 으로 보이게.
        Box(n, new Vector3(w * 0.8f, h * 0.55f, 0.04f), new Vector3(0f, h * 0.45f, 0.15f),
            RConcretePale, "Plaster");
        for (int i = 1; i < 3; i++)
            Box(n, new Vector3(w * 0.82f, 0.03f, 0.05f), new Vector3(0f, h * 0.3f * i, 0.16f),
                RConcrete, "Course");
        return n;
    }

    // 휘어진 철골 — 한 번 꺾인 H형강 한 토막.
    private void BentGirder(Node3D parent, Vector3 at, float yaw, float len, RandomNumberGenerator rng)
    {
        var n = new Node3D { Name = "Girder", Position = at };
        n.RotationDegrees = new Vector3(rng.RandfRange(-50f, 10f), yaw, rng.RandfRange(-25f, 25f));
        parent.AddChild(n);
        float a = len * 0.55f, b = len * 0.45f;
        foreach (var (l, off, bend) in new[] { (a, 0f, 0f), (b, a, rng.RandfRange(14f, 34f)) })
        {
            var seg = new Node3D { Position = new Vector3(0f, 0f, off), RotationDegrees = new Vector3(bend, 0f, 0f) };
            n.AddChild(seg);
            Box(seg, new Vector3(0.3f, 0.04f, l), new Vector3(0f, 0.13f, l * 0.5f), RSteelBent, "Flange");
            Box(seg, new Vector3(0.3f, 0.04f, l), new Vector3(0f, -0.13f, l * 0.5f), RSteelBent, "Flange");
            Box(seg, new Vector3(0.04f, 0.26f, l), new Vector3(0f, 0f, l * 0.5f), RSteelBent, "Web");
        }
    }

    // 쓰러진 사람. pose 0 = 엎드림 / 1 = 모로 누움 / 2 = 잔해 밑(팔다리만 보인다).
    private void FallenBody(Node3D parent, Vector3 at, float yaw, int pose, RandomNumberGenerator rng,
        bool suit = false)
    {
        var n = new Node3D { Name = "Fallen", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        var body = suit ? RSuit : RFabric;
        if (pose == 2)
        {
            // 무너진 구조물 아래 — 팔 하나와 다리 하나만 삐져나와 있다. 그 이상은 보이지 않는다.
            Box(n, new Vector3(0.14f, 0.12f, 0.62f), new Vector3(0.30f, 0.07f, 0.26f), body, "Arm")
                .RotationDegrees = new Vector3(0f, 28f, 0f);
            Box(n, new Vector3(0.12f, 0.11f, 0.22f), new Vector3(0.44f, 0.06f, 0.58f), RSkin, "Hand")
                .RotationDegrees = new Vector3(0f, 44f, 0f);
            Box(n, new Vector3(0.17f, 0.15f, 0.70f), new Vector3(-0.34f, 0.08f, -0.18f), body, "Leg")
                .RotationDegrees = new Vector3(0f, -16f, 0f);
            Box(n, new Vector3(0.15f, 0.13f, 0.28f), new Vector3(-0.40f, 0.07f, -0.60f), RPlastic, "Boot");
            return;
        }
        float roll = pose == 1 ? 68f : 0f;
        var torso = new Node3D { Position = new Vector3(0f, 0.13f, 0f), RotationDegrees = new Vector3(0f, 0f, roll) };
        n.AddChild(torso);
        Box(torso, new Vector3(0.42f, 0.23f, 0.66f), Vector3.Zero, body, "Torso");
        Box(torso, new Vector3(0.36f, 0.20f, 0.26f), new Vector3(0f, -0.01f, 0.44f), body, "Hips");
        // 머리 — 돌아가 있다. 얼굴은 보이지 않는다.
        var head = Box(torso, new Vector3(0.24f, 0.21f, 0.26f), new Vector3(0.05f, 0.02f, -0.48f), RSkin, "Head");
        head.RotationDegrees = new Vector3(0f, rng.RandfRange(30f, 70f), 0f);
        if (suit) Cyl(head, 0.16f, 0.2f, new Vector3(0f, 0.04f, 0f), RPlastic, "Hood")
            .RotationDegrees = new Vector3(90f, 0f, 0f);
        // 팔 — 좌우가 다르게 뻗어 있다.
        var armL = Box(torso, new Vector3(0.13f, 0.12f, 0.58f), new Vector3(-0.30f, -0.02f, -0.12f), body, "ArmL");
        armL.RotationDegrees = new Vector3(0f, rng.RandfRange(-62f, -30f), 0f);
        var armR = Box(torso, new Vector3(0.13f, 0.12f, 0.54f), new Vector3(0.30f, -0.02f, -0.05f), body, "ArmR");
        armR.RotationDegrees = new Vector3(0f, rng.RandfRange(20f, 58f), 0f);
        Box(torso, new Vector3(0.12f, 0.1f, 0.18f), new Vector3(-0.56f, -0.02f, -0.36f), RSkin, "HandL");
        // 다리 — 하나는 접혀 있다.
        var legL = Box(torso, new Vector3(0.16f, 0.15f, 0.70f), new Vector3(-0.12f, -0.01f, 0.86f), body, "LegL");
        legL.RotationDegrees = new Vector3(0f, rng.RandfRange(-16f, 4f), 0f);
        var legR = Box(torso, new Vector3(0.16f, 0.15f, 0.56f), new Vector3(0.14f, -0.01f, 0.80f), body, "LegR");
        legR.RotationDegrees = new Vector3(0f, rng.RandfRange(26f, 52f), 0f);
        Box(torso, new Vector3(0.14f, 0.12f, 0.26f), new Vector3(-0.16f, -0.02f, 1.22f), RPlastic, "BootL");
        // 위로 덮인 먼지 — 쓰러진 지 시간이 지났다.
        Box(n, new Vector3(1.0f, 0.02f, 1.5f), new Vector3(0f, 0.255f, 0.3f), RDust, "AshOver");
    }

    // 부서진 집기 — 책상 · 캐비닛 · 의자 · 단말. 뒤집히거나 찌그러진 채로.
    private void WreckedFurniture(Node3D parent, Vector3 at, float yaw, int kind, RandomNumberGenerator rng)
    {
        var n = new Node3D { Name = "Wreck", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        switch (kind)
        {
            case 0:   // 뒤집힌 책상 — 상판이 바닥에 닿고 다리가 하늘을 본다
                var top = Box(n, new Vector3(1.5f, 0.07f, 0.8f), new Vector3(0f, 0.05f, 0f), RPlastic, "DeskTop");
                top.RotationDegrees = new Vector3(rng.RandfRange(-12f, 12f), 0f, rng.RandfRange(-14f, 14f));
                foreach (float sx in new[] { -0.6f, 0.6f })
                    foreach (float sz in new[] { -0.3f, 0.3f })
                        Pipe(top, new Vector3(sx, 0.04f, sz), new Vector3(sx * 1.05f, 0.62f, sz * 1.1f),
                            0.03f, RSteelBent, "DeskLeg", 5);
                Box(n, new Vector3(0.6f, 0.4f, 0.45f), new Vector3(0.9f, 0.2f, 0.3f), RPlastic, "Drawer")
                    .RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 80f), 18f);
                break;
            case 1:   // 넘어진 캐비닛 — 문이 열리고 서류가 쏟아져 있다
                var cab = Box(n, new Vector3(0.9f, 0.55f, 1.4f), new Vector3(0f, 0.28f, 0f), RSteelBent, "Cabinet");
                cab.RotationDegrees = new Vector3(0f, 0f, rng.RandfRange(-8f, 8f));
                var door = Box(cab, new Vector3(0.05f, 0.5f, 1.3f), new Vector3(0.52f, 0.1f, 0f), RSteelBent, "Door");
                door.RotationDegrees = new Vector3(0f, 0f, -42f);
                for (int i = 0; i < 9; i++)
                {
                    var sheet = Box(n, new Vector3(0.22f, 0.006f, 0.3f),
                        new Vector3(rng.RandfRange(0.6f, 2.4f), 0.015f, rng.RandfRange(-1.1f, 1.1f)),
                        RConcretePale, "Paper");
                    sheet.RotationDegrees = new Vector3(rng.RandfRange(-6f, 6f), rng.RandfRange(0f, 180f), 0f);
                }
                break;
            case 2:   // 부서진 의자
                var seat = Box(n, new Vector3(0.44f, 0.06f, 0.42f), new Vector3(0f, 0.14f, 0f), RPlastic, "Seat");
                seat.RotationDegrees = new Vector3(rng.RandfRange(-40f, 40f), 0f, rng.RandfRange(-50f, 50f));
                Box(n, new Vector3(0.4f, 0.4f, 0.05f), new Vector3(0.3f, 0.08f, 0.35f), RPlastic, "Back")
                    .RotationDegrees = new Vector3(78f, rng.RandfRange(0f, 90f), 0f);
                for (int i = 0; i < 3; i++)
                    Pipe(n, new Vector3(rng.RandfRange(-0.4f, 0.4f), 0.03f, rng.RandfRange(-0.4f, 0.4f)),
                        new Vector3(rng.RandfRange(-0.7f, 0.7f), rng.RandfRange(0.05f, 0.3f),
                            rng.RandfRange(-0.7f, 0.7f)), 0.016f, RSteelBent, "ChairLeg", 5);
                break;
            default:  // 박살난 단말 · 모니터
                var cse = Box(n, new Vector3(0.6f, 0.45f, 0.5f), new Vector3(0f, 0.2f, 0f), RPlastic, "Case");
                cse.RotationDegrees = new Vector3(rng.RandfRange(-30f, 30f), rng.RandfRange(0f, 180f), 22f);
                Box(cse, new Vector3(0.5f, 0.36f, 0.02f), new Vector3(0f, 0f, 0.26f), RScorch, "DeadScreen");
                for (int i = 0; i < 5; i++)
                {
                    var g = Box(n, new Vector3(rng.RandfRange(0.06f, 0.2f), 0.006f, rng.RandfRange(0.06f, 0.22f)),
                        new Vector3(rng.RandfRange(-0.9f, 0.9f), 0.012f, rng.RandfRange(-0.9f, 0.9f)),
                        RGlassShard, "Shard");
                    g.RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 180f), rng.RandfRange(-8f, 8f));
                }
                CableSag(n, new Vector3(0.2f, 0.25f, 0.1f),
                    new Vector3(rng.RandfRange(-1.4f, 1.4f), 0.02f, rng.RandfRange(-1.4f, 1.4f)),
                    0.06f, 0.014f, RPlastic, 4);
                break;
        }
    }

    // ── 폐허 본편 ───────────────────────────────────────────────────────
    //
    // BuildSurface 끝에서 한 번 부른다. 카메라 셋이 보는 범위
    // (x -25~20 / z -42~8) 안쪽에 밀도를 몰아준다.
    private void BuildSurfaceRuins()
    {
        var rng = new RandomNumberGenerator { Seed = 777013 };
        var ruins = new Node3D { Name = "Ruins" };
        _pSurface.AddChild(ruins);

        // ── ① 땅 : 평면을 깬다 ──────────────────────────────────────────
        // 들린 포장 슬래브 · 꺼진 자리 · 재 더미 · 자갈. 바닥이 '면' 이 아니라
        // '부서진 지면' 으로 보여야 그 위의 잔해가 설득력을 얻는다.
        for (int i = 0; i < 46; i++)
        {
            float x = rng.RandfRange(-28f, 24f), z = rng.RandfRange(-42f, 10f);
            var sz = new Vector3(rng.RandfRange(2.2f, 6.5f), rng.RandfRange(0.12f, 0.4f), rng.RandfRange(2.2f, 6f));
            var sl = Box(ruins, sz, new Vector3(x, sz.Y * 0.18f, z),
                i % 3 == 0 ? RAsphalt : (i % 3 == 1 ? RConcreteOld : RConcrete), "Paving");
            // 살짝 들리거나 기울어 있다 — 각도가 1~2도만 있어도 평면이 깨진다.
            sl.RotationDegrees = new Vector3(rng.RandfRange(-5f, 5f), rng.RandfRange(0f, 90f),
                rng.RandfRange(-5f, 5f));
        }
        // 꺼진 자리(크레이터) 셋 — 둘레에 흙이 밀려 올라와 있다.
        foreach (var (cx, cz, r) in new[] { (-9.5f, -14f, 4.6f), (12f, -26f, 6.2f), (3.5f, -34f, 5.0f) })
        {
            Cyl(ruins, r * 0.72f, 0.5f, new Vector3(cx, -0.3f, cz), RScorch, "CraterFloor");
            int ring = 14;
            for (int i = 0; i < ring; i++)
            {
                float a = Mathf.Tau * i / ring + rng.RandfRange(-0.1f, 0.1f);
                var at = new Vector3(cx + Mathf.Cos(a) * r, 0f, cz + Mathf.Sin(a) * r);
                var b = Box(ruins, new Vector3(rng.RandfRange(1.0f, 2.2f), rng.RandfRange(0.35f, 0.9f),
                    rng.RandfRange(0.8f, 1.8f)), at with { Y = rng.RandfRange(0.1f, 0.35f) },
                    i % 2 == 0 ? RConcrete : RDust, "CraterLip");
                b.RotationDegrees = new Vector3(rng.RandfRange(-26f, 10f), -Mathf.RadToDeg(a),
                    rng.RandfRange(-20f, 20f));
            }
            Box(ruins, new Vector3(r * 2.4f, 0.02f, r * 2.4f), new Vector3(cx, 0.03f, cz), RScorch, "Scorch");
        }
        // 길게 갈라진 균열 — 한쪽이 주저앉아 단차가 생겼다.
        for (int i = 0; i < 16; i++)
        {
            float z = 6f - i * 3.1f;
            float x = -3f + Mathf.Sin(i * 0.7f) * 5.5f;
            Box(ruins, new Vector3(rng.RandfRange(0.5f, 1.4f), 0.5f, 3.2f), new Vector3(x, -0.22f, z),
                RScorch, "Fissure");
            var step = Box(ruins, new Vector3(rng.RandfRange(2.5f, 4.5f), 0.22f, 3.0f),
                new Vector3(x + rng.RandfRange(1.6f, 2.8f), 0.04f, z), RAsphalt, "FissureStep");
            step.RotationDegrees = new Vector3(0f, rng.RandfRange(-8f, 8f), rng.RandfRange(2f, 7f));
        }
        // 재 · 먼지 퇴적 — 바닥 색을 얼룩지게 만든다.
        for (int i = 0; i < 40; i++)
        {
            var d = Box(ruins, new Vector3(rng.RandfRange(1.6f, 5.5f), 0.03f, rng.RandfRange(1.4f, 4.8f)),
                new Vector3(rng.RandfRange(-30f, 26f), 0.045f, rng.RandfRange(-44f, 12f)),
                i % 4 == 0 ? RScorch : RDust, "AshDrift");
            d.RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 180f), 0f);
        }
        // 자갈 — 잘고 많아야 '부서진 것' 이 된다.
        for (int i = 0; i < 220; i++)
        {
            float s = rng.RandfRange(0.07f, 0.32f);
            var g = Box(ruins, new Vector3(s, s * rng.RandfRange(0.4f, 0.9f), s * rng.RandfRange(0.7f, 1.4f)),
                new Vector3(rng.RandfRange(-26f, 22f), s * 0.3f, rng.RandfRange(-40f, 10f)),
                i % 5 == 0 ? RConcretePale : RGravel, "Gravel");
            g.RotationDegrees = new Vector3(rng.RandfRange(0f, 90f), rng.RandfRange(0f, 180f),
                rng.RandfRange(0f, 90f));
        }

        // 카메라 바로 앞(도로 컷이 지나가는 z 2~10 구간)은 따로 한 번 더 깐다.
        // 멀리만 채우면 화면 아래 절반이 통째로 매끈한 면으로 남는다.
        for (int i = 0; i < 70; i++)
        {
            float x = rng.RandfRange(-11f, 13f), z = rng.RandfRange(-4f, 10f);
            if (Mathf.Abs(x) < 1.6f && z > 2f) continue;        // 카메라가 지나는 자리
            if (new Vector2(x + 0.7f, z - 0.9f).Length() < 2.4f) continue;   // 쓰러진 사람 둘레
            float s2 = rng.RandfRange(0.1f, 0.5f);
            var g = Box(ruins, new Vector3(s2 * rng.RandfRange(0.8f, 2.2f), s2 * rng.RandfRange(0.3f, 0.8f),
                s2 * rng.RandfRange(0.8f, 2.0f)), new Vector3(x, s2 * 0.25f, z),
                i % 4 == 0 ? RConcretePale : (i % 4 == 1 ? RGravel : RConcrete), "NearRubble");
            g.RotationDegrees = new Vector3(rng.RandfRange(-25f, 25f), rng.RandfRange(0f, 180f),
                rng.RandfRange(-25f, 25f));
        }
        for (int i = 0; i < 14; i++)
        {
            var d = Box(ruins, new Vector3(rng.RandfRange(1.2f, 3.4f), 0.028f, rng.RandfRange(1.0f, 3.0f)),
                new Vector3(rng.RandfRange(-12f, 14f), 0.04f, rng.RandfRange(-6f, 10f)),
                i % 3 == 0 ? RScorch : RDust, "NearAsh");
            d.RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 180f), 0f);
        }
        // 넘어진 가로등 — 앞쪽을 가로지르는 긴 선 하나가 '길' 이었음을 말해 준다.
        var post = new Node3D { Position = new Vector3(-6.8f, 0.3f, 1.2f) };
        post.RotationDegrees = new Vector3(-4f, 28f, 7f);
        ruins.AddChild(post);
        for (int i = 0; i < 4; i++)
            Pipe(post, new Vector3(0f, 0f, i * 2.3f), new Vector3(0f, rng.RandfRange(-0.08f, 0.12f), (i + 1) * 2.3f),
                0.11f - i * 0.012f, RSteelBent, "PostSeg", 7);
        Pipe(post, new Vector3(0f, 0.05f, 9.2f), new Vector3(0.9f, 0.35f, 10.1f), 0.08f, RSteelBent, "PostArm", 6);
        Box(post, new Vector3(0.6f, 0.18f, 1.0f), new Vector3(1.1f, 0.4f, 10.5f), RScorch, "PostLamp");
        Box(post, new Vector3(0.9f, 0.26f, 0.9f), new Vector3(0f, -0.2f, -0.6f), RConcrete, "PostFoot");

        // ── ② 콘크리트 덩어리 · 벽체 조각 · 철골 ────────────────────────
        for (int i = 0; i < 30; i++)
        {
            float ang = Mathf.DegToRad(rng.RandfRange(-135f, 135f));
            float dist = rng.RandfRange(5f, 34f);
            Chunk(ruins, new Vector3(Mathf.Sin(ang) * dist, 0f, -Mathf.Cos(ang) * dist),
                rng.RandfRange(0f, 180f), rng.RandfRange(0.7f, 1.8f), rng, rng.RandiRange(1, 4));
        }
        foreach (var (x, z, yaw, w, h, tilt) in new[]
                 {
                     (-7.5f, -7.5f, 24f, 4.2f, 2.6f, -12f), (9.5f, -12.5f, -62f, 5.5f, 3.4f, 9f),
                     (-15f, -19f, 70f, 6.0f, 4.2f, -7f), (4.5f, -22f, 14f, 3.4f, 2.0f, 16f),
                     (17f, -31f, -28f, 7.0f, 5.2f, -5f), (-21f, -30f, 48f, 5.0f, 3.6f, 11f),
                     (-2.5f, -17f, -8f, 2.8f, 1.6f, -22f),
                 })
            WallShard(ruins, new Vector3(x, 0f, z), yaw, w, h, tilt, rng);
        for (int i = 0; i < 18; i++)
        {
            float ang = Mathf.DegToRad(rng.RandfRange(-130f, 130f));
            float dist = rng.RandfRange(6f, 32f);
            BentGirder(ruins, new Vector3(Mathf.Sin(ang) * dist, rng.RandfRange(0.1f, 0.9f), -Mathf.Cos(ang) * dist),
                rng.RandfRange(0f, 180f), rng.RandfRange(4f, 11f), rng);
        }
        // 천장 패널 잔해 — 얇은 판이 겹쳐 쓰러져 있고, 조명 기구가 매달려 있다.
        for (int i = 0; i < 22; i++)
        {
            var at = new Vector3(rng.RandfRange(-20f, 18f), 0f, rng.RandfRange(-34f, 6f));
            var p = Box(ruins, new Vector3(rng.RandfRange(0.8f, 1.8f), 0.045f, rng.RandfRange(0.8f, 1.6f)),
                at with { Y = rng.RandfRange(0.03f, 0.5f) }, RConcretePale, "CeilPanel");
            p.RotationDegrees = new Vector3(rng.RandfRange(-45f, 45f), rng.RandfRange(0f, 180f),
                rng.RandfRange(-45f, 45f));
            if (i % 4 == 0)
            {
                var fix = Box(ruins, new Vector3(1.2f, 0.1f, 0.26f), at + new Vector3(0.6f, 0.12f, 0.4f),
                    RSteelBent, "LightFixture");
                fix.RotationDegrees = new Vector3(rng.RandfRange(-30f, 30f), rng.RandfRange(0f, 180f), 24f);
                Box(fix, new Vector3(1.0f, 0.03f, 0.18f), new Vector3(0f, -0.06f, 0f), RScorch, "DeadTube");
            }
        }
        // 유리 파편 밭 — 창이 있던 자리 근처에 몰려 있다.
        foreach (var (cx, cz) in new[] { (6.5f, -11f), (-10f, -22f), (14f, -28f) })
            for (int i = 0; i < 26; i++)
            {
                float s = rng.RandfRange(0.1f, 0.42f);
                var g = Box(ruins, new Vector3(s, 0.008f, s * rng.RandfRange(0.4f, 1.2f)),
                    new Vector3(cx + rng.RandfRange(-3.5f, 3.5f), 0.014f, cz + rng.RandfRange(-3.5f, 3.5f)),
                    RGlassShard, "GlassShard");
                g.RotationDegrees = new Vector3(rng.RandfRange(-5f, 5f), rng.RandfRange(0f, 180f), 0f);
            }

        // ── ③ 무너진 중계시설 — 오른쪽 안쪽 ────────────────────────────
        // "건물이 있었다" 는 것을 가장 분명히 말해 주는 덩어리 하나.
        var relay = new Node3D { Position = new Vector3(16.5f, 0f, -24f), RotationDegrees = new Vector3(0f, -34f, 0f) };
        ruins.AddChild(relay);
        // 남은 전면 — 층 슬래브가 반쯤 꺾여 내려앉았다.
        Box(relay, new Vector3(11f, 0.6f, 8f), new Vector3(0f, 0.3f, 0f), RConcreteOld, "Slab0");
        for (int f = 1; f <= 3; f++)
        {
            var sl = new Node3D { Position = new Vector3(rng.RandfRange(-0.6f, 0.6f), f * 2.9f, 0f) };
            sl.RotationDegrees = new Vector3(rng.RandfRange(-4f, 4f), 0f, -4f - f * 3.5f);
            relay.AddChild(sl);
            Box(sl, new Vector3(11f - f * 1.4f, 0.45f, 7.5f), Vector3.Zero, RConcreteOld, $"Slab{f}");
            // 기둥 — 몇 개는 부러져 있다.
            for (int c = -2; c <= 2; c++)
            {
                if (rng.Randf() < 0.3f) continue;
                float h = rng.Randf() < 0.25f ? rng.RandfRange(0.7f, 1.8f) : 2.45f;
                Box(sl, new Vector3(0.52f, h, 0.52f), new Vector3(c * 2.3f, -h * 0.5f - 0.22f, rng.RandfRange(-2.6f, 2.6f)),
                    RConcrete, "Column");
                if (h < 2f)
                    for (int k = 0; k < 3; k++)
                        Pipe(sl, new Vector3(c * 2.3f + rng.RandfRange(-0.16f, 0.16f), -h - 0.2f, 0f),
                            new Vector3(c * 2.3f + rng.RandfRange(-0.3f, 0.3f), -h - 0.2f + rng.RandfRange(0.4f, 1.1f),
                                rng.RandfRange(-0.3f, 0.3f)), 0.024f, RRebar, "ColRebar", 5);
            }
            // 바닥에 남은 벽 조각.
            Box(sl, new Vector3(rng.RandfRange(2f, 5f), 1.3f, 0.3f),
                new Vector3(rng.RandfRange(-3f, 3f), 0.88f, -3.6f), RConcretePale, "StubWall");
        }
        // 쓰러진 중계 안테나 — '중계시설' 이라는 것을 한 번에 말해 준다.
        var mast = new Node3D { Position = new Vector3(-4f, 1.1f, 5.5f) };
        // 축이 +Z 로 뻗으므로 X 회전은 **거의 0** 이어야 바닥에 눕는다(크게 주면 다시 선다).
        mast.RotationDegrees = new Vector3(-7f, 22f, 8f);
        relay.AddChild(mast);
        for (int i = 0; i < 6; i++)
        {
            Box(mast, new Vector3(0.9f, 0.9f, 2.4f), new Vector3(0f, 0f, 1.2f + i * 2.4f), RSteelBent, "MastSeg")
                .Scale = new Vector3(1f - i * 0.1f, 1f - i * 0.1f, 1f);
            Pipe(mast, new Vector3(-0.42f, -0.42f, i * 2.4f), new Vector3(0.42f, 0.42f, (i + 1) * 2.4f),
                0.035f, RSteelBent, "MastBrace", 5);
            Pipe(mast, new Vector3(0.42f, -0.42f, i * 2.4f), new Vector3(-0.42f, 0.42f, (i + 1) * 2.4f),
                0.035f, RSteelBent, "MastBrace", 5);
        }
        var dish = Cyl(mast, 1.9f, 0.18f, new Vector3(0f, 0.6f, 13.5f), RConcretePale, "Dish");
        dish.RotationDegrees = new Vector3(0f, 0f, 74f);
        Pipe(mast, new Vector3(0f, 0.6f, 13.5f), new Vector3(0f, 1.9f, 12.6f), 0.07f, RSteelBent, "Feed", 5);

        // ── ④ 부서진 집기 · 깨진 장비 ──────────────────────────────────
        foreach (var (x, z, yaw, kind) in new[]
                 {
                     (2.2f, -8.5f, 34f, 0), (-4.8f, -12f, -18f, 1), (7.8f, -16.5f, 66f, 3),
                     (-1.2f, -20f, 12f, 2), (10.5f, -8f, -44f, 0), (-12f, -11f, 80f, 3),
                     (5.0f, -27f, -12f, 1), (-7.5f, -26f, 140f, 2), (13.5f, -18f, 20f, 2),
                     (-16f, -16f, -70f, 0), (1.0f, -30f, 50f, 3),
                 })
            WreckedFurniture(ruins, new Vector3(x, 0f, z), yaw, kind, rng);
        // 장비 랙이 통째로 넘어져 있다.
        foreach (var (x, z, yaw) in new[] { (8.5f, -20.5f, 28f), (-9.5f, -17.5f, -56f) })
        {
            var rack = new Node3D { Position = new Vector3(x, 0f, z) };
            rack.RotationDegrees = new Vector3(0f, yaw, 86f);
            ruins.AddChild(rack);
            Box(rack, new Vector3(0.85f, 2.1f, 0.8f), new Vector3(0f, 1.05f, 0f), RSteelBent, "RackBody");
            for (int u = 0; u < 6; u++)
                Box(rack, new Vector3(0.8f, 0.22f, 0.06f), new Vector3(0f, 0.35f + u * 0.3f, 0.42f),
                    u % 2 == 0 ? RScorch : RPlastic, "RackUnit");
            CableSag(rack, new Vector3(0f, 2.0f, -0.3f), new Vector3(1.6f, 0.05f, -1.4f), 0.2f, 0.03f, RPlastic, 5);
        }
        // 케이블 드럼 · 변압기 상자 — 시설이 있던 자리의 흔적.
        var drum = new Node3D { Position = new Vector3(-6.2f, 0f, -4.2f), RotationDegrees = new Vector3(0f, 18f, 96f) };
        ruins.AddChild(drum);
        Cyl(drum, 1.25f, 1.1f, Vector3.Zero, RFabric, "DrumBody");
        Cyl(drum, 1.45f, 0.12f, new Vector3(0f, 0.55f, 0f), RSteelBent, "DrumFlange");
        Cyl(drum, 1.45f, 0.12f, new Vector3(0f, -0.55f, 0f), RSteelBent, "DrumFlange");
        Chamfer(ruins, new Vector3(2.2f, 1.6f, 1.4f), new Vector3(-13.5f, 0.78f, -7.5f), RSteelBent, 0.04f, "Transformer");
        Box(ruins, new Vector3(0.9f, 0.5f, 0.1f), new Vector3(-13.5f, 1.3f, -6.75f), RScorch, "TxBurn");

        // ── ⑤ 급박한 피난 흔적 ─────────────────────────────────────────
        // 흩어진 서류 · 떨어뜨린 가방 · 넘어진 손수레 · 들것 · 신발 한 짝.
        for (int i = 0; i < 48; i++)
        {
            var sh = Box(ruins, new Vector3(0.21f, 0.005f, 0.29f),
                new Vector3(rng.RandfRange(-14f, 14f), 0.012f, rng.RandfRange(-28f, 6f)),
                RConcretePale, "ScatteredPaper");
            sh.RotationDegrees = new Vector3(rng.RandfRange(-4f, 4f), rng.RandfRange(0f, 180f), 0f);
        }
        foreach (var (x, z, yaw) in new[] { (0.8f, -5.5f, 42f), (4.2f, -13f, -20f), (-3.0f, -9.0f, 110f) })
        {
            var bag = new Node3D { Position = new Vector3(x, 0f, z), RotationDegrees = new Vector3(0f, yaw, 0f) };
            ruins.AddChild(bag);
            Chamfer(bag, new Vector3(0.52f, 0.22f, 0.34f), new Vector3(0f, 0.11f, 0f), RFabric, 0.02f, "Case");
            Pipe(bag, new Vector3(-0.1f, 0.22f, 0f), new Vector3(0.1f, 0.22f, 0f), 0.016f, RPlastic, "Handle", 5);
        }
        var cartW = new Node3D { Position = new Vector3(-2.0f, 0f, -2.6f), RotationDegrees = new Vector3(0f, 54f, 74f) };
        ruins.AddChild(cartW);
        Box(cartW, new Vector3(0.9f, 0.05f, 0.62f), new Vector3(0f, 0.4f, 0f), RSteelBent, "CartTop");
        Box(cartW, new Vector3(0.9f, 0.05f, 0.62f), new Vector3(0f, 0.0f, 0f), RSteelBent, "CartShelf");
        foreach (float sx in new[] { -0.4f, 0.4f })
            foreach (float sz in new[] { -0.26f, 0.26f })
                Pipe(cartW, new Vector3(sx, -0.1f, sz), new Vector3(sx, 0.42f, sz), 0.022f, RSteelBent, "CartLeg", 5);
        // 들것 — 누군가를 옮기려다 두고 간 자리.
        var litter = new Node3D { Position = new Vector3(5.6f, 0f, -6.4f), RotationDegrees = new Vector3(0f, -16f, 0f) };
        ruins.AddChild(litter);
        Box(litter, new Vector3(0.68f, 0.05f, 1.95f), new Vector3(0f, 0.22f, 0f), RFabric, "Canvas");
        foreach (float sx in new[] { -0.36f, 0.36f })
        {
            Pipe(litter, new Vector3(sx, 0.22f, -1.25f), new Vector3(sx, 0.22f, 1.25f), 0.028f, RSteelBent, "Pole", 5);
            foreach (float sz in new[] { -0.8f, 0.8f })
                Pipe(litter, new Vector3(sx, 0.0f, sz), new Vector3(sx, 0.24f, sz), 0.022f, RSteelBent, "LitterLeg", 5);
        }
        // 떨어진 헬멧 둘 · 신발 한 짝.
        foreach (var (x, z) in new[] { (3.1f, -3.4f), (-5.4f, -15.5f) })
        {
            Cyl(ruins, 0.19f, 0.2f, new Vector3(x, 0.09f, z), RPlastic, "Helmet")
                .RotationDegrees = new Vector3(84f, 0f, 0f);
            Box(ruins, new Vector3(0.1f, 0.03f, 0.18f), new Vector3(x + 0.25f, 0.02f, z + 0.1f), RFabric, "Strap");
        }
        Box(ruins, new Vector3(0.12f, 0.1f, 0.28f), new Vector3(-1.4f, 0.05f, -6.8f), RPlastic, "Shoe")
            .RotationDegrees = new Vector3(0f, 38f, 22f);

        // ── ⑥ 쓰러진 사람들 ────────────────────────────────────────────
        // 셋이면 충분하다. 보여 주는 방식이 전부 다르고, 전부 카메라에서 어느 정도 떨어져 있다.
        //   · 무너진 슬래브 아래로 팔다리만 나와 있는 사람
        //   · 보호복을 입은 채 엎드린 사람 (도망치다 쓰러졌다)
        //   · 벽 조각 옆에 모로 누운 사람 — 옆에 떨어뜨린 가방
        var slabDown = Box(ruins, new Vector3(3.8f, 0.5f, 3.0f), new Vector3(-5.0f, 0.32f, -6.5f),
            RConcreteOld, "CollapsedSlab");
        slabDown.RotationDegrees = new Vector3(-9f, 28f, 6f);
        for (int i = 0; i < 4; i++)
            Pipe(ruins, new Vector3(-6.3f + i * 0.5f, 0.55f, -7.6f),
                new Vector3(-6.6f + i * 0.6f, 0.95f + rng.RandfRange(0f, 0.5f), -8.1f),
                0.022f, RRebar, "SlabRebar", 5);
        FallenBody(ruins, new Vector3(-3.5f, 0f, -5.5f), 118f, 2, rng);

        // 보호복을 입은 채 엎드린 사람 — 도로 컷에서 가장 가깝게 보이는 하나.
        // **시선축과 직각으로** 눕힌다. 카메라 쪽으로 머리를 두면 전신이 겹쳐 덩어리가 된다.
        // 카메라가 **바로 옆을 지나간다.** 멀리 두면 잔해와 구분이 안 된다.
        FallenBody(ruins, new Vector3(-0.7f, 0f, 0.9f), 100f, 0, rng, suit: true);
        Box(ruins, new Vector3(0.3f, 0.12f, 0.24f), new Vector3(0.5f, 0.06f, 1.5f), RPlastic, "DroppedMask")
            .RotationDegrees = new Vector3(0f, 52f, 16f);
        // 그 앞에 끌린 자국 — 쓰러지기 전에 기어간 흔적.
        foreach (var dz in new[] { 0.6f, 1.1f, 1.7f })
            Box(ruins, new Vector3(0.5f, 0.004f, 0.3f), new Vector3(-0.7f + dz * 0.5f, 0.016f, 0.9f + dz),
                RScorch, "DragMark").RotationDegrees = new Vector3(0f, 24f, 0f);
        // 조금 안쪽에 하나 더 — 잔해 사이에 반쯤 묻힌 채.
        FallenBody(ruins, new Vector3(-1.4f, 0f, -12.2f), 100f, 0, rng, suit: true);

        // 도망치다 넘어진 사람 — 모로 누운 채. 옆에 떨어뜨린 가방.
        FallenBody(ruins, new Vector3(3.4f, 0f, -9.5f), 152f, 1, rng);
        Chamfer(ruins, new Vector3(0.48f, 0.2f, 0.32f), new Vector3(4.3f, 0.1f, -10.4f), RFabric, 0.02f, "DroppedBag");

        // 조금 더 안쪽에 하나 더 — 멀리 한 사람이 더 있으면 '한 명만의 사고' 로 안 읽힌다.
        FallenBody(ruins, new Vector3(-9.0f, 0f, -20.5f), 62f, 0, rng);

        // ── ⑦ 아직 타는 불 · 연기 ──────────────────────────────────────
        // 잔해 더미 안쪽에서 타오른다 — 땅 위에 그냥 놓인 불은 소품으로 보인다.
        foreach (var (x, z, s) in new[] { (-6.5f, -13.5f, 0.9f), (11f, -21f, 1.3f), (-17f, -24f, 1.1f) })
        {
            Box(ruins, new Vector3(1.0f * s, 0.6f * s, 1.0f * s), new Vector3(x, 0.3f * s, z),
                Glow(new Color(0.78f, 0.30f, 0.07f), 2.4f), "EmberPile");
            var l = new OmniLight3D
            {
                Position = new Vector3(x, 1.1f, z), LightColor = new Color(1f, 0.44f, 0.16f),
                LightEnergy = 2.6f, OmniRange = 12f, ShadowEnabled = false,
            };
            ruins.AddChild(l);
            _fireLights.Add((l, 2.6f, x * 0.13f));
            Ash(ruins, new Vector3(x, 1.6f, z), new Vector3(0.6f, 1.2f, 0.6f),
                new Vector3(0.22f, 1f, 0.06f), 2.4f, 18, 0.14f, new Color(0.22f, 0.17f, 0.15f), 6.5).Emitting = true;
        }
    }
}
