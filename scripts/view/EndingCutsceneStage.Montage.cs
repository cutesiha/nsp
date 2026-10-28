using Godot;

namespace NSP.View;

// ── ③ 시설 재가동 몽타주 세트 ───────────────────────────────────────────
//
// 코어가 다시 물린 뒤 1~2초짜리 컷 넷이 스친다 — 격벽이 내려와 잠기고, 복도 문 표시등이
// 하나씩 켜지고, 벽 패널이 부팅되고, 멈춰 있던 작업 기계가 돈다(EndingDirector.FacilityMontage).
//
// 예전 세트는 바닥 · 벽 · 문짝 판 · 기계 몸통 상자 몇 개뿐이라 프롤로그 세트 바로 뒤에 붙으면
// "기본 블록 맵" 으로 보였다(2026-10-28 지적). 프롤로그 복도와 **같은 재질 언어와 조립 부품**
// (EndingCutsceneStage.Kit)으로 다시 짓는다. 카메라 네 자리와 연출 API 는 그대로다.
//
// 공간 (z 가 작을수록 안쪽):
//   z -31 ~ -24  격벽 너머 짧은 구간          (컷 A 의 배경)
//   z -24        격벽 문틀 · 내려오는 격벽     (컷 A)
//   z -24 ~ 7    폭 5m 복도 · 왼쪽 벽에 문 넷 (컷 B) · 오른쪽 벽 z=4 에 제어 패널(컷 C)
//   z 7 ~ 19     폭 9m 기계실 · 가운데 작업 기계(컷 D)
public partial class EndingCutsceneStage
{
    private const float CorHalf = 2.5f;   // 복도 안쪽 반폭
    private const float CorH = 4f;        // 복도 천장 높이
    private const float BayHalf = 4.5f;   // 기계실 안쪽 반폭
    private const float BayH = 5.2f;      // 기계실 천장 높이
    private const float BayZ0 = 7f, BayZ1 = 19f;

    private MeshInstance3D _bulkhead, _panelScreen, _panelFace;
    private Node3D _machine, _machineArm, _machineFly;
    private StandardMaterial3D _panelMat, _panelFaceMat, _panelLedMat, _bulkheadLockMat, _machineLampMat;
    private OmniLight3D _panelLight, _machineLight;
    private readonly OmniLight3D[] _doorLights = new OmniLight3D[4];
    private readonly StandardMaterial3D[] _doorLampMats = new StandardMaterial3D[4];

    private const float MachineArmY = 2.62f;   // 프레스 머리의 쉬는 높이

    private void BuildCorridor()
    {
        _setCorridor = new Node3D { Name = "SetCorridor" };
        _world.AddChild(_setCorridor);
        // 그림자 광원은 이 세트에서 하나만 — 몽타주는 짧은 컷이라 비싼 그림자를 여럿 둘 이유가 없다.
        _shadowBudget = 1;

        BuildCorridorShell();
        BuildCorridorDoors();
        BuildCorridorWalls();
        BuildBulkheadGate();
        BuildBootPanel();
        BuildMachineBay();
        BuildWorkMachine();

        _shadowBudget = ShadowBudgetPerSet;
    }

    // ── 껍데기 · 바닥 · 천장 ─────────────────────────────────────────────
    private void BuildCorridorShell()
    {
        var root = _setCorridor;
        var rng = new RandomNumberGenerator { Seed = 4417 };
        var floor = Mat(new Color(0.236f, 0.250f, 0.268f), 0.46f, 0.08f);
        var wall = Mat(new Color(0.262f, 0.274f, 0.298f), 0.92f);
        const float z0 = -31f, z1 = BayZ0, len = z1 - z0, mid = (z0 + z1) * 0.5f;

        Box(root, new Vector3(CorHalf * 2f + 0.6f, 0.3f, len), new Vector3(0f, -0.15f, mid), floor, "Floor");
        Box(root, new Vector3(0.3f, CorH, len), new Vector3(-CorHalf - 0.15f, CorH * 0.5f, mid), wall, "WallL");
        Box(root, new Vector3(0.3f, CorH, len), new Vector3(CorHalf + 0.15f, CorH * 0.5f, mid), wall, "WallR");
        Box(root, new Vector3(CorHalf * 2f + 0.6f, 0.3f, len), new Vector3(0f, CorH + 0.15f, mid), wall, "Ceiling");
        Box(root, new Vector3(CorHalf * 2f, CorH, 0.3f), new Vector3(0f, CorH * 0.5f, z0 - 0.15f), wall, "FarEnd");

        DressCorridorSurfaces(root, CorHalf, CorH, z0, z1);
        DressWallPanels(root, new Vector3(-CorHalf + 0.03f, 0f, mid), len, CorH, false, 1f, 1.05f, 4f);
        DressWallPanels(root, new Vector3(CorHalf - 0.03f, 0f, mid), len, CorH, false, -1f, 1.05f, 4f);

        // 바닥 — 가운데가 닳고, 가장자리에 노란 유도선이 남아 있다.
        Box(root, new Vector3(2.4f, 0.004f, len - 1f), new Vector3(0f, 0.016f, mid), KEpoxyWorn, "WornLane");
        foreach (float sx in new[] { -1f, 1f })
            Box(root, new Vector3(0.1f, 0.006f, len - 1f), new Vector3(sx * 1.3f, 0.018f, mid),
                Glow(new Color(0.42f, 0.35f, 0.12f), 0.3f), "LaneLine");
        for (int i = 0; i < 18; i++)
            Box(root, new Vector3(rng.RandfRange(0.5f, 1.4f), 0.003f, rng.RandfRange(0.6f, 1.6f)),
                new Vector3(rng.RandfRange(-2.0f, 2.0f), 0.02f, rng.RandfRange(z0 + 1f, z1 - 1f)), Stain, "FloorStain");
        foreach (float z in new[] { 3f, -10f, -21.5f })
            WarnStripe(root, new Vector3(0f, 0.024f, z), 4.6f, 0.2f, 0.004f, new Vector3(90f, 0f, 0f), 9);

        // 천장 — 형광등은 아직 비상 전력이라 어둡게만 켜져 있다. 그 사이로 보 · 배관 · 덕트.
        for (int i = 0; i < 9; i++)
        {
            float z = 4.5f - i * 4f;
            Strip(root, new Vector3(0f, CorH - 0.12f, z), 2.2f, Cool, 0.65f, null, null, 0.42f);
            Box(root, new Vector3(2.6f, 0.07f, 0.34f), new Vector3(0f, CorH - 0.04f, z), KAlu, "LampTrim");
        }
        for (int i = 0; i < 19; i++)
        {
            float z = z1 - 1f - i * 2f;
            Box(root, new Vector3(CorHalf * 2f, 0.16f, 0.18f), new Vector3(0f, CorH - 0.02f, z), KPanelDark, "CeilRib");
            Pipe(root, new Vector3(-2.2f, CorH, z), new Vector3(-2.2f, CorH - 0.12f, z), 0.02f, KSteelDark, "Hanger", 4);
        }
        foreach (var (x, y, r) in new[] { (-2.2f, 3.86f, 0.13f), (-1.95f, 3.74f, 0.08f), (2.2f, 3.84f, 0.16f) })
            Pipe(root, new Vector3(x, y, z0 + 0.3f), new Vector3(x, y, z1 - 0.2f), r, KSteel, "RunPipe", 8);
        Duct(root, new Vector3(0.95f, 3.78f, z0 + 0.3f), new Vector3(0.95f, 3.78f, z1 - 0.2f), 0.62f, 0.42f,
            KPanelDark, KSteelDark, 4.2f);
        CableTray(root, new Vector3(-1.05f, 3.58f, z0 + 0.3f), new Vector3(-1.05f, 3.58f, z1 - 0.2f), 0.5f,
            KSteelDark, KCable);
        // 배관 플랜지 — 굵은 관 둘에만.
        for (int i = 0; i < 9; i++)
            foreach (var (x, y, r) in new[] { (-2.2f, 3.86f, 0.13f), (2.2f, 3.84f, 0.16f) })
                Cyl(root, r + 0.05f, 0.06f, new Vector3(x, y, z1 - 2f - i * 4.2f), KSteelDark, "Flange")
                    .RotationDegrees = new Vector3(90f, 0f, 0f);

        // 복도 기본광 — 아직 복구 전이라 약하다. 격벽 쪽은 조금 더 밝게 둔다(닫히는 문이 보여야 한다).
        foreach (var (z, e) in new[] { (2f, 0.5f), (-9f, 0.55f), (-19.5f, 1.25f), (-27.5f, 0.9f) })
            root.AddChild(new OmniLight3D
            {
                Name = $"CorridorFill{z:0}", Position = new Vector3(0f, 3.3f, z),
                LightColor = new Color(0.50f, 0.56f, 0.66f), LightEnergy = e,
                OmniRange = 14f, ShadowEnabled = false,
            });
    }

    // ── 컷 B : 왼쪽 벽의 문 넷 — 문 위 표시등이 하나씩 켜진다 ───────────────
    private void BuildCorridorDoors()
    {
        var root = _setCorridor;
        for (int i = 0; i < 4; i++)
        {
            float z = -3f - i * 5.2f;
            var dr = new Node3D { Name = $"Door{i}", Position = new Vector3(-CorHalf + 0.08f, 0f, z) };
            root.AddChild(dr);
            // 문틀 · 문짝 · 창 · 손잡이 · 발판 · 번호판 · 카드 리더.
            Box(dr, new Vector3(0.14f, 2.5f, 1.72f), new Vector3(0f, 1.25f, 0f), KSteelDark, "DoorFrame");
            Box(dr, new Vector3(0.1f, 2.26f, 1.44f), new Vector3(0.02f, 1.13f, 0f), KPanelDark, "DoorLeaf");
            Box(dr, new Vector3(0.05f, 0.5f, 0.56f), new Vector3(0.05f, 1.72f, 0f), KSteelDark, "WindowFrame");
            Box(dr, new Vector3(0.06f, 0.44f, 0.5f), new Vector3(0.06f, 1.72f, 0f), KGlassPane, "DoorWindow");
            Box(dr, new Vector3(0.07f, 0.05f, 0.18f), new Vector3(0.07f, 1.02f, -0.52f), KAlu, "Handle");
            Box(dr, new Vector3(0.05f, 0.12f, 0.05f), new Vector3(0.06f, 1.02f, -0.6f), KAlu, "HandleBase");
            Box(dr, new Vector3(0.1f, 0.3f, 1.3f), new Vector3(0.04f, 0.18f, 0f), KAlu, "KickPlate");
            BoltRow(dr, new Vector3(0.08f, 0.38f, -0.6f), new Vector3(0.08f, 0.38f, 0.6f), 6, 0.012f, KSteelDark);
            Label(dr, new Vector3(0.08f, 2.3f, -0.42f), new Vector2(0.32f, 0.14f), new Vector3(0f, 90f, 0f));
            WarnStripe(dr, new Vector3(0.09f, 0.06f, 0f), 1.5f, 0.08f, 0.012f, new Vector3(0f, 90f, 0f), 8);
            Box(dr, new Vector3(0.07f, 0.16f, 0.11f), new Vector3(0.05f, 1.2f, -1.04f), KPlasticDark, "Reader");
            Box(dr, new Vector3(0.02f, 0.03f, 0.03f), new Vector3(0.09f, 1.25f, -1.04f),
                Glow(new Color(0.85f, 0.25f, 0.18f), 1.2f), "ReaderLed");
            // 문 위 인방 + 표시등 하우징. 몽타주 B 에서 **하나씩** 초록으로 켜진다(§14).
            Box(root, new Vector3(0.12f, 0.16f, 1.95f), new Vector3(-CorHalf + 0.1f, 2.6f, z), KSteel, "Lintel");
            Box(root, new Vector3(0.12f, 0.24f, 0.62f), new Vector3(-CorHalf + 0.12f, 2.86f, z), KSteelDark, "LampHousing");
            var lamp = Glow(new Color(0.35f, 1f, 0.55f), 0f);
            lamp.AlbedoColor = new Color(0.08f, 0.16f, 0.10f);   // 꺼져 있으면 어두운 초록 유리
            _doorLampMats[i] = lamp;
            Box(root, new Vector3(0.05f, 0.15f, 0.5f), new Vector3(-CorHalf + 0.19f, 2.86f, z), lamp, $"DoorLamp{i}");
            var light = new OmniLight3D
            {
                Name = $"DoorLight{i}",
                Position = new Vector3(-1.9f, 2.6f, z),
                LightColor = new Color(0.45f, 1f, 0.62f),
                LightEnergy = 0f,
                OmniRange = 4.6f,
                ShadowEnabled = false,
            };
            root.AddChild(light);
            _doorLights[i] = light;
        }
    }

    // ── 벽 소품 · 벽기둥 ─────────────────────────────────────────────────
    private void BuildCorridorWalls()
    {
        var root = _setCorridor;
        // 벽기둥 — 문 사이마다 양쪽 벽에서 튀어나온다. 복도의 길이를 마디로 끊어 읽게 한다.
        foreach (float z in new[] { -0.4f, -5.6f, -10.8f, -16f, -21.2f })
            foreach (float sx in new[] { -1f, 1f })
            {
                float x = sx * (CorHalf - 0.12f);
                Box(root, new Vector3(0.26f, CorH, 0.46f), new Vector3(x, CorH * 0.5f, z), KConcreteDark, "Pilaster");
                Box(root, new Vector3(0.3f, 0.42f, 0.52f), new Vector3(x, 0.21f, z), KSteelDark, "PilasterGuard");
                foreach (float y in new[] { 1.2f, 2.4f, 3.5f })
                    Box(root, new Vector3(0.28f, 0.03f, 0.48f), new Vector3(x, y, z), KSteelDark, "PilasterBand");
                WarnStripe(root, new Vector3(x - sx * 0.15f, 0.55f, z), 0.46f, 0.14f, 0.012f,
                    new Vector3(0f, sx > 0f ? -90f : 90f, 0f), 4);
            }

        // 오른쪽 벽 — 소화전함 · 분전함 · 구급함 · 배관 점검구. 왼쪽 벽은 문 사이에 얇은 것만.
        foreach (var (z, kind) in new[] { (-2.6f, 0), (-8.2f, 1), (-13.4f, 2), (-18.6f, 1) })
        {
            var cab = new Node3D { Position = new Vector3(CorHalf - 0.06f, 0f, z), RotationDegrees = new Vector3(0f, -90f, 0f) };
            root.AddChild(cab);
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
                Chamfer(cab, new Vector3(0.8f, 1.1f, 0.24f), new Vector3(0f, 1.35f, 0f), KPanel, 0.014f, "UtilBox");
                Louver(cab, new Vector3(0f, 1.62f, 0.13f), 0.5f, 0.22f, Vector3.Zero, 4);
                Box(cab, new Vector3(0.68f, 0.02f, 0.02f), new Vector3(0f, 1.05f, 0.13f), KSteelDark, "Seam");
                Box(cab, new Vector3(0.05f, 0.12f, 0.035f), new Vector3(0.3f, 1.15f, 0.14f), KAlu, "Handle");
                Pipe(cab, new Vector3(-0.22f, 1.9f, 0f), new Vector3(-0.22f, 3.6f, 0f), 0.03f, KSteelDark, "Conduit", 6);
                Pipe(cab, new Vector3(0.22f, 1.9f, 0f), new Vector3(0.22f, 3.6f, 0f), 0.022f, KSteelDark, "Conduit", 6);
                Label(cab, new Vector3(-0.15f, 1.3f, 0.13f), new Vector2(0.26f, 0.1f), Vector3.Zero);
            }
            else
            {
                Chamfer(cab, new Vector3(0.44f, 0.5f, 0.18f), new Vector3(0f, 1.5f, 0f), KFoam, 0.01f, "FirstAid");
                var green = Mat(new Color(0.18f, 0.34f, 0.20f), 0.8f);
                Box(cab, new Vector3(0.3f, 0.07f, 0.02f), new Vector3(0f, 1.5f, 0.1f), green, "Cross");
                Box(cab, new Vector3(0.07f, 0.3f, 0.02f), new Vector3(0f, 1.5f, 0.1f), green, "Cross");
            }
        }
        // 양쪽 벽에 얇게 붙는 라벨 · 콘센트 · 배관 고정 클램프.
        for (int i = 0; i < 10; i++)
        {
            float z = 1.5f - i * 2.6f;
            float side = i % 3 == 0 ? 1f : -1f;
            float toDoor = Mathf.PosMod(-3f - z + 2.6f, 5.2f) - 2.6f;
            if (side < 0f && Mathf.Abs(toDoor) < 1.1f) continue;   // 문 자리는 비운다
            Label(root, new Vector3(side * (CorHalf - 0.04f), 2.05f, z), new Vector2(0.5f, 0.2f),
                new Vector3(0f, side > 0f ? -90f : 90f, 0f));
            Box(root, new Vector3(0.05f, 0.1f, 0.3f), new Vector3(side * (CorHalf - 0.05f), 0.55f, z + 1.1f),
                KSteelDark, "Socket");
        }
        // 비상등 갓 — 아직 붉게 남아 있는 비상 조명(복구가 끝나 가는 중이라 아주 약하다).
        for (int i = 0; i < 5; i++)
        {
            float z = 2.4f - i * 6.2f;
            Box(root, new Vector3(0.1f, 0.3f, 0.34f), new Vector3(CorHalf - 0.05f, 3.25f, z), KSteelDark, "RedHood");
            Box(root, new Vector3(0.08f, 0.16f, 0.2f), new Vector3(CorHalf - 0.12f, 3.25f, z),
                Glow(new Color(1f, 0.18f, 0.12f), 0.9f), "RedLamp");
        }
    }

    // ── 컷 A : 격벽 — 실제로 내려와 닫히고 잠긴다(§13) ─────────────────────
    private void BuildBulkheadGate()
    {
        var root = _setCorridor;
        var frame = new Node3D { Name = "BulkheadFrame", Position = new Vector3(0f, 0f, -24f) };
        root.AddChild(frame);
        // 문틀 — 천장 쪽 인방 · 양 기둥 · 바닥 문턱. 격벽은 천장 슬롯에서 내려온다.
        Box(frame, new Vector3(CorHalf * 2f, 0.5f, 0.9f), new Vector3(0f, CorH - 0.25f, 0f), KSteel, "Lintel");
        Box(frame, new Vector3(CorHalf * 2f + 0.1f, 0.08f, 1.0f), new Vector3(0f, CorH - 0.52f, 0f), KSteelDark, "LintelLip");
        foreach (float sx in new[] { -1f, 1f })
        {
            Box(frame, new Vector3(0.34f, CorH, 0.9f), new Vector3(sx * (CorHalf - 0.17f), CorH * 0.5f, 0f), KSteel, "Post");
            Box(frame, new Vector3(0.12f, CorH - 0.5f, 0.18f), new Vector3(sx * 2.28f, (CorH - 0.5f) * 0.5f, 0.3f),
                KSteelDark, "Guide");
            Box(frame, new Vector3(0.12f, CorH - 0.5f, 0.18f), new Vector3(sx * 2.28f, (CorH - 0.5f) * 0.5f, -0.3f),
                KSteelDark, "Guide");
            BoltRow(frame, new Vector3(sx * 2.33f, 2.7f, 0.46f), new Vector3(sx * 2.33f, 3.35f, 0.46f), 3, 0.04f, KAlu);
            WarnStripe(frame, new Vector3(sx * 2.33f, 0.85f, 0.46f), 1.3f, 0.2f, 0.02f, new Vector3(0f, 0f, 90f), 6);
        }
        Box(frame, new Vector3(CorHalf * 2f, 0.06f, 0.9f), new Vector3(0f, 0.03f, 0f), KAlu, "Threshold");
        WarnStripe(frame, new Vector3(0f, 0.065f, 0.55f), 4.8f, 0.22f, 0.006f, new Vector3(90f, 0f, 0f), 14);
        // 인방의 경고등 둘 · 구동 모터 하우징.
        foreach (float sx in new[] { -1.75f, 1.75f })
        {
            Box(frame, new Vector3(0.36f, 0.2f, 0.12f), new Vector3(sx, CorH - 0.26f, 0.48f), KSteelDark, "BeaconBase");
            Box(frame, new Vector3(0.3f, 0.14f, 0.08f), new Vector3(sx, CorH - 0.26f, 0.53f),
                Glow(new Color(1f, 0.55f, 0.1f), 0.6f), "Beacon");
        }
        Chamfer(frame, new Vector3(0.9f, 0.34f, 0.5f), new Vector3(-0.75f, CorH - 0.25f, 0.7f), KPanelDark, 0.02f, "Motor");
        Louver(frame, new Vector3(-0.75f, CorH - 0.25f, 0.96f), 0.6f, 0.2f, Vector3.Zero, 4);
        CableSag(frame, new Vector3(-0.3f, CorH - 0.3f, 0.8f), new Vector3(1.4f, CorH - 0.3f, 0.6f), 0.14f, 0.02f, KCable, 5);

        // 격벽 문짝 — 처음에는 천장 위(슬롯 안)에 있다가 내려온다. 리브 · 볼트 · 빗금 · 작은 관측창.
        _bulkhead = Box(root, new Vector3(4.56f, 4.4f, 0.4f), new Vector3(0f, 6.6f, -24f),
            Mat(new Color(0.35f, 0.37f, 0.40f), 0.3f, 0.9f), "Bulkhead");
        foreach (float zf in new[] { 0.24f, -0.24f })
        {
            for (int i = -2; i <= 2; i++)
                Box(_bulkhead, new Vector3(0.2f, 4.2f, 0.08f), new Vector3(i * 0.95f, 0f, zf), KPanelDark, "RibV");
            foreach (float y in new[] { -1.45f, 0f, 1.45f })
                Box(_bulkhead, new Vector3(4.46f, 0.13f, 0.1f), new Vector3(0f, y, zf), KAlu, "RibH");
        }
        BoltRow(_bulkhead, new Vector3(-2.05f, 1.86f, 0.29f), new Vector3(2.05f, 1.86f, 0.29f), 10, 0.045f, KSteelDark);
        BoltRow(_bulkhead, new Vector3(-2.05f, -1.86f, 0.29f), new Vector3(2.05f, -1.86f, 0.29f), 10, 0.045f, KSteelDark);
        WarnStripe(_bulkhead, new Vector3(0f, -2.02f, 0.3f), 4.5f, 0.3f, 0.02f, Vector3.Zero, 14);
        Box(_bulkhead, new Vector3(0.72f, 0.42f, 0.06f), new Vector3(0.95f, 0.7f, 0.29f), KSteelDark, "PortFrame");
        Box(_bulkhead, new Vector3(0.6f, 0.3f, 0.05f), new Vector3(0.95f, 0.7f, 0.31f), KGlassPane, "Port");
        Label(_bulkhead, new Vector3(-1.0f, 0.75f, 0.3f), new Vector2(0.9f, 0.3f), Vector3.Zero);

        // 잠금 표시등 — 문틀 오른쪽 기둥 앞면. 잠기는 순간 초록으로 켜진다.
        Box(frame, new Vector3(0.3f, 0.5f, 0.1f), new Vector3(2.33f, 2.05f, 0.5f), KPanelDark, "LockPanel");
        _bulkheadLockMat = Glow(new Color(0.35f, 1f, 0.55f), 0f);
        _bulkheadLockMat.AlbedoColor = new Color(0.08f, 0.16f, 0.10f);
        Box(frame, new Vector3(0.22f, 0.12f, 0.05f), new Vector3(2.33f, 2.18f, 0.56f), _bulkheadLockMat, "Lock");
        for (int k = 0; k < 3; k++)
            Box(frame, new Vector3(0.05f, 0.05f, 0.03f), new Vector3(2.24f + k * 0.09f, 1.93f, 0.56f), KAlu, "LockKey");
        Label(frame, new Vector3(-2.33f, 2.05f, 0.46f), new Vector2(0.24f, 0.36f), Vector3.Zero);

        // 격벽 너머 — 뒤가 새까마면 닫히는 문이 허공에 떠 보인다.
        Box(root, new Vector3(3.0f, 0.1f, 0.12f), new Vector3(0f, 2.9f, -30.95f), KPanelDark, "FarSign");
        Label(root, new Vector3(0f, 2.9f, -30.88f), new Vector2(1.6f, 0.08f), Vector3.Zero);
        Chamfer(root, new Vector3(1.2f, 1.4f, 0.5f), new Vector3(-1.6f, 0.7f, -30.4f), KPanel, 0.02f, "FarCabinet");
        Louver(root, new Vector3(-1.6f, 1.0f, -30.14f), 0.8f, 0.3f, Vector3.Zero, 5);
    }

    // ── 컷 C : 벽 제어 패널 — 꺼져 있다가 부팅되고, 그 화면에 GUIDE-0 이 뜬다(§15) ──
    private void BuildBootPanel()
    {
        var root = _setCorridor;
        var at = new Vector3(CorHalf - 0.1f, 1.9f, 4f);
        // 벽에서 조금 파인 자리 — 패널이 벽에 그냥 붙은 판이 아니라 설비로 읽히게.
        Box(root, new Vector3(0.08f, 2.3f, 3.0f), new Vector3(CorHalf - 0.04f, 1.75f, 4f), KPanelDark, "Recess");
        Box(root, new Vector3(0.12f, 0.08f, 3.1f), new Vector3(CorHalf - 0.08f, 2.94f, 4f), KAlu, "RecessTop");
        Box(root, new Vector3(0.12f, 0.08f, 3.1f), new Vector3(CorHalf - 0.08f, 0.56f, 4f), KAlu, "RecessSill");

        var panel = new Node3D { Name = "Panel", Position = at };
        root.AddChild(panel);
        Chamfer(panel, new Vector3(0.16f, 1.62f, 2.42f), Vector3.Zero, KPanel, 0.02f, "Case");
        Box(panel, new Vector3(0.05f, 1.42f, 2.08f), new Vector3(-0.08f, 0.02f, 0f), KPlasticDark, "Bezel");
        // 화면 — 꺼져 있으면 검은 유리, 켜지면 청록 인광.
        _panelMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.01f, 0.012f, 0.014f),
            EmissionEnabled = true, Emission = new Color(0.2f, 0.9f, 0.95f), EmissionEnergyMultiplier = 0f,
            Roughness = 0.12f, Metallic = 0.2f,
        };
        _panelScreen = new MeshInstance3D
        {
            Name = "PanelScreen",
            Mesh = new QuadMesh { Size = new Vector2(1.9f, 1.25f) },
            Position = new Vector3(-0.108f, 0.02f, 0f),
            RotationDegrees = new Vector3(0f, -90f, 0f),
            MaterialOverride = _panelMat,
        };
        panel.AddChild(_panelScreen);
        // 그 화면 안에 뜰 GUIDE-0 얼굴. 초상은 흰 실루엣이라 패널 인광색으로 물들인다.
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
            Position = new Vector3(-0.112f, 0.05f, 0f),
            RotationDegrees = new Vector3(0f, -90f, 0f),
            MaterialOverride = _panelFaceMat,
            Visible = false,
        };
        panel.AddChild(_panelFace);
        // 차양 · 옆 통풍구 · 아래 조작부 · 상태 LED.
        Box(panel, new Vector3(0.32f, 0.05f, 2.42f), new Vector3(-0.14f, 0.83f, 0f), KSteelDark, "Visor");
        foreach (float sz in new[] { -1.12f, 1.12f })
            Louver(panel, new Vector3(-0.085f, 0.1f, sz), 0.12f, 0.8f, new Vector3(0f, -90f, 0f), 6);
        var deck = Box(panel, new Vector3(0.26f, 0.08f, 1.6f), new Vector3(-0.16f, -0.86f, 0f), KPanelDark, "Deck");
        deck.RotationDegrees = new Vector3(0f, 0f, -12f);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 6; c++)
                Box(panel, new Vector3(0.06f, 0.025f, 0.11f),
                    new Vector3(-0.2f - r * 0.07f, -0.82f + r * 0.015f, -0.45f + c * 0.18f), r == 0 ? KAlu : KBrass, "Key");
        _panelLedMat = Glow(new Color(0.35f, 1f, 0.55f), 0f);
        for (int k = 0; k < 5; k++)
            Box(panel, new Vector3(0.03f, 0.035f, 0.035f), new Vector3(-0.09f, -0.66f, 0.62f + k * 0.08f),
                k == 0 ? Glow(new Color(1f, 0.6f, 0.2f), 1.2f) : _panelLedMat, "Led");
        BoltRow(panel, new Vector3(-0.085f, 0.76f, -1.12f), new Vector3(-0.085f, 0.76f, 1.12f), 6, 0.015f, KAlu);
        Label(panel, new Vector3(-0.085f, -0.62f, -0.75f), new Vector2(0.5f, 0.1f), new Vector3(0f, -90f, 0f));
        // 위로 올라가는 전선관 · 늘어진 케이블.
        Pipe(root, at + new Vector3(0.0f, 0.82f, -0.7f), new Vector3(CorHalf - 0.12f, CorH, 3.3f), 0.035f, KSteelDark, "Conduit", 6);
        Pipe(root, at + new Vector3(0.0f, 0.82f, 0.7f), new Vector3(CorHalf - 0.12f, CorH, 4.7f), 0.028f, KSteelDark, "Conduit", 6);
        CableSag(root, at + new Vector3(-0.05f, -0.8f, 1.2f), new Vector3(CorHalf - 0.1f, 0.05f, 5.9f), 0.12f, 0.018f, KCable, 5);

        _panelLight = new OmniLight3D
        {
            Name = "PanelLight",
            Position = new Vector3(1.55f, 1.9f, 4f),
            LightColor = new Color(0.4f, 0.9f, 1f),
            LightEnergy = 0f,
            OmniRange = 4.2f,
            ShadowEnabled = false,
        };
        root.AddChild(_panelLight);
    }

    // ── 컷 D : 기계실 ────────────────────────────────────────────────────
    private void BuildMachineBay()
    {
        var root = _setCorridor;
        var rng = new RandomNumberGenerator { Seed = 9127 };
        var floor = Mat(new Color(0.248f, 0.252f, 0.258f), 0.62f, 0.06f);
        var wall = Mat(new Color(0.252f, 0.262f, 0.284f), 0.92f);
        float len = BayZ1 - BayZ0, mid = (BayZ0 + BayZ1) * 0.5f;

        Box(root, new Vector3(BayHalf * 2f + 0.6f, 0.3f, len), new Vector3(0f, -0.15f, mid), floor, "BayFloor");
        Box(root, new Vector3(0.3f, BayH, len), new Vector3(-BayHalf - 0.15f, BayH * 0.5f, mid), wall, "BayWallL");
        Box(root, new Vector3(0.3f, BayH, len), new Vector3(BayHalf + 0.15f, BayH * 0.5f, mid), wall, "BayWallR");
        Box(root, new Vector3(BayHalf * 2f, BayH, 0.3f), new Vector3(0f, BayH * 0.5f, BayZ1 + 0.15f), wall, "BayWallBack");
        Box(root, new Vector3(BayHalf * 2f + 0.6f, 0.3f, len), new Vector3(0f, BayH + 0.15f, mid), wall, "BayCeiling");
        // 복도 쪽 벽 — 가운데에 복도 입구가 뚫려 있다.
        foreach (float sx in new[] { -1f, 1f })
            Box(root, new Vector3(BayHalf - CorHalf, BayH, 0.3f),
                new Vector3(sx * (CorHalf + (BayHalf - CorHalf) * 0.5f), BayH * 0.5f, BayZ0 - 0.15f), wall, "BayWallFront");
        Box(root, new Vector3(CorHalf * 2f, BayH - CorH, 0.3f), new Vector3(0f, CorH + (BayH - CorH) * 0.5f, BayZ0 - 0.15f),
            wall, "BayHeader");
        // 입구 문틀.
        Box(root, new Vector3(CorHalf * 2f + 0.3f, 0.3f, 0.4f), new Vector3(0f, CorH + 0.15f, BayZ0), KSteel, "EntryLintel");
        foreach (float sx in new[] { -1f, 1f })
            Box(root, new Vector3(0.2f, CorH, 0.4f), new Vector3(sx * (CorHalf + 0.1f), CorH * 0.5f, BayZ0), KSteel, "EntryPost");

        DressWallPanels(root, new Vector3(-BayHalf + 0.03f, 0f, mid), len, BayH, false, 1f, 1.2f, 3f);
        DressWallPanels(root, new Vector3(BayHalf - 0.03f, 0f, mid), len, BayH, false, -1f, 1.2f, 3f);
        DressWallPanels(root, new Vector3(0f, 0f, BayZ1 - 0.03f), BayHalf * 2f, BayH, true, -1f, 1.2f, 3f);
        foreach (float sx in new[] { -1f, 1f })
            Box(root, new Vector3(0.1f, 0.22f, len), new Vector3(sx * (BayHalf - 0.05f), 0.11f, mid), Baseboard, "Baseboard");
        Box(root, new Vector3(BayHalf * 2f, 0.22f, 0.1f), new Vector3(0f, 0.11f, BayZ1 - 0.05f), Baseboard, "Baseboard");

        // 바닥 — 기계 둘레 작업 구역 표시 · 배수구 · 얼룩.
        foreach (var (x0, x1, z) in new[] { (-2.2f, 1.8f, 8.9f), (-2.2f, 1.8f, 13.1f) })
            WarnStripe(root, new Vector3((x0 + x1) * 0.5f, 0.024f, z), x1 - x0, 0.2f, 0.004f, new Vector3(90f, 0f, 0f), 10);
        foreach (float x in new[] { -2.2f, 1.8f })
            WarnStripe(root, new Vector3(x, 0.025f, 11f), 4.2f, 0.2f, 0.004f, new Vector3(90f, 90f, 0f), 10);
        Box(root, new Vector3(1.2f, 0.01f, 0.3f), new Vector3(3.2f, 0.012f, 15.5f), KSteelDark, "Drain");
        for (int i = -4; i <= 4; i++)
            Box(root, new Vector3(0.04f, 0.012f, 0.26f), new Vector3(3.2f + i * 0.12f, 0.018f, 15.5f), KDirt, "DrainSlot");
        for (int i = 0; i < 10; i++)
            Box(root, new Vector3(rng.RandfRange(0.6f, 1.6f), 0.003f, rng.RandfRange(0.6f, 1.6f)),
                new Vector3(rng.RandfRange(-4f, 4f), 0.02f, rng.RandfRange(7.6f, 18.4f)), Stain, "FloorStain");

        // 천장 — 크레인 레일(I 빔) · 매달린 등 · 덕트.
        foreach (float x in new[] { -1.8f, 1.8f })
        {
            Box(root, new Vector3(0.12f, 0.36f, len), new Vector3(x, BayH - 0.3f, mid), KSteelDark, "CraneWeb");
            Box(root, new Vector3(0.34f, 0.05f, len), new Vector3(x, BayH - 0.48f, mid), KSteel, "CraneFlange");
            Box(root, new Vector3(0.34f, 0.05f, len), new Vector3(x, BayH - 0.12f, mid), KSteel, "CraneFlange");
        }
        var trolley = new Node3D { Position = new Vector3(0f, BayH - 0.62f, 14.6f) };
        root.AddChild(trolley);
        Box(trolley, new Vector3(3.9f, 0.22f, 0.5f), Vector3.Zero, KWarn, "CraneBeam");
        Chamfer(trolley, new Vector3(0.6f, 0.36f, 0.6f), new Vector3(0.4f, -0.28f, 0f), KPanelDark, 0.02f, "Hoist");
        Pipe(trolley, new Vector3(0.4f, -0.46f, 0f), new Vector3(0.4f, -1.5f, 0f), 0.012f, KSteelDark, "HoistCable", 4);
        Box(trolley, new Vector3(0.16f, 0.2f, 0.06f), new Vector3(0.4f, -1.58f, 0f), KWarn, "Hook");
        for (int i = 0; i < 3; i++)
        {
            float z = 9f + i * 4f;
            foreach (float x in new[] { -3f, 3f })
            {
                Pipe(root, new Vector3(x, BayH, z), new Vector3(x, BayH - 0.9f, z), 0.015f, KSteelDark, "LampWire", 4);
                Strip(root, new Vector3(x, BayH - 0.95f, z), 1.6f, Cool, 0.55f, null, null, 0.35f);
            }
        }
        Duct(root, new Vector3(-3.6f, BayH - 0.55f, BayZ0 + 0.2f), new Vector3(-3.6f, BayH - 0.55f, BayZ1 - 0.2f),
            0.8f, 0.55f, KPanelDark, KSteelDark, 3f);
        foreach (var (x, r) in new[] { (3.9f, 0.15f), (3.55f, 0.09f) })
            Pipe(root, new Vector3(x, BayH - 0.7f, BayZ0 + 0.2f), new Vector3(x, BayH - 0.7f, BayZ1 - 0.2f), r, KSteel, "BayPipe", 8);

        // 벽 — 제어반 · 분전반 · 세로 배관 · 공구판.
        WallPanel(root, new Vector3(BayHalf - 0.08f, 1.6f, 10.2f), new Vector3(0f, -90f, 0f), 1.1f, 0.9f,
            new Color(0.30f, 0.68f, 1f), 0.9f, null, 71);
        WallPanel(root, new Vector3(-BayHalf + 0.08f, 1.6f, 15.8f), new Vector3(0f, 90f, 0f), 0.9f, 0.8f,
            new Color(1f, 0.6f, 0.2f), 0.7f, null, 73);
        foreach (float z in new[] { 8.2f, 17.6f })
        {
            Pipe(root, new Vector3(BayHalf - 0.25f, 0.1f, z), new Vector3(BayHalf - 0.25f, BayH - 0.1f, z), 0.12f, KSteel, "Riser", 8);
            foreach (float y in new[] { 1.2f, 3.0f, 4.4f })
                Cyl(root, 0.17f, 0.06f, new Vector3(BayHalf - 0.25f, y, z), KSteelDark, "RiserFlange");
            Cyl(root, 0.12f, 0.1f, new Vector3(BayHalf - 0.48f, 1.4f, z), KBrass, "Valve")
                .RotationDegrees = new Vector3(0f, 0f, 90f);
        }
        var board = new Node3D { Position = new Vector3(-BayHalf + 0.06f, 1.7f, 11.6f), RotationDegrees = new Vector3(0f, 90f, 0f) };
        root.AddChild(board);
        Box(board, new Vector3(1.6f, 1.0f, 0.03f), Vector3.Zero, KWood, "ToolBoard");
        for (int k = 0; k < 7; k++)
            Box(board, new Vector3(0.05f, rng.RandfRange(0.22f, 0.42f), 0.04f),
                new Vector3(-0.66f + k * 0.22f, rng.RandfRange(-0.1f, 0.15f), 0.04f), k % 2 == 0 ? KSteelDark : KAlu, "Tool");

        // 바닥 소품 — 가스 실린더 랙 · 드럼통 · 팔레트 상자 · 작은 예비 기계.
        var gas = new Node3D { Position = new Vector3(3.5f, 0f, 17.9f), RotationDegrees = new Vector3(0f, 180f, 0f) };
        root.AddChild(gas);
        Box(gas, new Vector3(1.9f, 0.1f, 0.6f), new Vector3(0f, 0.05f, 0f), KSteelDark, "RackFloor");
        foreach (float sx in new[] { -0.9f, 0.9f })
            Pipe(gas, new Vector3(sx, 0.1f, 0f), new Vector3(sx, 1.5f, 0f), 0.035f, KSteelDark, "Post", 6);
        Pipe(gas, new Vector3(-0.9f, 1.1f, 0f), new Vector3(0.9f, 1.1f, 0f), 0.03f, KSteelDark, "Bar", 6);
        for (int k = 0; k < 4; k++)
        {
            Cyl(gas, 0.17f, 1.35f, new Vector3(-0.62f + k * 0.41f, 0.78f, 0f),
                k % 2 == 0 ? Mat(new Color(0.22f, 0.26f, 0.30f), 0.42f, 0.8f) : KAlu, "Cylinder");
            Cyl(gas, 0.07f, 0.16f, new Vector3(-0.62f + k * 0.41f, 1.52f, 0f), KBrass, "Valve");
        }
        foreach (var (x, z) in new[] { (-3.8f, 8.0f), (-3.2f, 8.1f), (-3.85f, 8.85f) })
        {
            Cyl(root, 0.33f, 0.92f, new Vector3(x, 0.46f, z), Mat(new Color(0.20f, 0.19f, 0.17f), 0.78f), "Drum");
            foreach (float y in new[] { 0.28f, 0.64f })
                Cyl(root, 0.35f, 0.06f, new Vector3(x, y, z), KSteelDark, "DrumRib");
        }
        var pal = new Node3D { Position = new Vector3(3.3f, 0f, 8.4f), RotationDegrees = new Vector3(0f, -12f, 0f) };
        root.AddChild(pal);
        Box(pal, new Vector3(1.3f, 0.12f, 1.1f), new Vector3(0f, 0.06f, 0f), KWood, "Pallet");
        for (int k = 0; k < 3; k++)
            Chamfer(pal, new Vector3(0.9f, 0.5f, 0.8f),
                new Vector3(rng.RandfRange(-0.1f, 0.1f), 0.37f + k * 0.52f, rng.RandfRange(-0.08f, 0.08f)),
                k % 2 == 0 ? KFoam : KPlastic, 0.015f, "Crate");
        // 뒤쪽 예비 기계 — 덮개를 씌운 채 서 있다(돌지 않는다). 공간에 '설비가 여럿' 이라는 깊이를 준다.
        var spare = new Node3D { Position = new Vector3(-3.1f, 0f, 17.6f) };
        root.AddChild(spare);
        Box(spare, new Vector3(2.0f, 0.14f, 1.4f), new Vector3(0f, 0.07f, 0f), KSteelDark, "SparePlinth");
        Chamfer(spare, new Vector3(1.8f, 1.5f, 1.2f), new Vector3(0f, 0.9f, 0f), KPanelDark, 0.03f, "SpareBody");
        Box(spare, new Vector3(1.9f, 0.5f, 1.3f), new Vector3(0f, 1.5f, 0.02f), Mat(new Color(0.20f, 0.24f, 0.22f), 0.95f), "Tarp");
        Label(spare, new Vector3(0f, 0.8f, 0.61f), new Vector2(0.5f, 0.2f), Vector3.Zero);

        // 기계 옆 작업등 — 기계가 켜질 때 함께 켜진다. 앞쪽에 두면 기계가 어둠에 남는다.
        _machineLight = new OmniLight3D
        {
            Name = "MachineLight",
            Position = new Vector3(1.6f, 2.6f, 12.4f),
            LightColor = new Color(1f, 0.74f, 0.45f),
            LightEnergy = 0f,
            OmniRange = 7.5f,
            ShadowEnabled = false,
        };
        root.AddChild(_machineLight);
        foreach (var (z, e) in new[] { (9.5f, 0.55f), (16.5f, 0.6f) })
            root.AddChild(new OmniLight3D
            {
                Name = $"BayFill{z:0}", Position = new Vector3(0f, 4.2f, z),
                LightColor = new Color(0.52f, 0.58f, 0.68f), LightEnergy = e,
                OmniRange = 11f, ShadowEnabled = false,
            });
    }

    // 작업 기계 — 받침 · 몸체 · 모터와 냉각핀 · 플라이휠 · 프레스 기둥과 유압 실린더 ·
    // 조작반 · 경광등 · 배관. 멈춰 있다가 덜컹 하고 돌기 시작한다(§16).
    private void BuildWorkMachine()
    {
        _machine = new Node3D { Name = "Machine", Position = new Vector3(-0.2f, 0f, 11f) };
        _setCorridor.AddChild(_machine);
        var m = _machine;
        var body = Mat(new Color(0.300f, 0.336f, 0.322f), 0.5f, 0.55f);   // 산업용 도장(청록 회색)

        // 받침 — 앵커 볼트 · 가장자리 경고 띠.
        Box(m, new Vector3(3.6f, 0.16f, 2.5f), new Vector3(0f, 0.08f, 0f), KSteelDark, "Plinth");
        foreach (float sx in new[] { -1.65f, 1.65f })
            foreach (float sz in new[] { -1.1f, 1.1f })
            {
                Cyl(m, 0.07f, 0.06f, new Vector3(sx, 0.19f, sz), KAlu, "Anchor");
                Cyl(m, 0.03f, 0.12f, new Vector3(sx, 0.24f, sz), KSteel, "AnchorBolt");
            }
        WarnStripe(m, new Vector3(0f, 0.08f, 1.26f), 3.6f, 0.12f, 0.01f, Vector3.Zero, 14);
        Chamfer(m, new Vector3(3.1f, 0.5f, 2.0f), new Vector3(0f, 0.41f, 0f), KPanelDark, 0.03f, "BaseFrame");
        Louver(m, new Vector3(0.6f, 0.42f, 1.01f), 1.2f, 0.28f, Vector3.Zero, 5);

        // 몸체 — 점검문 · 통풍구 · 라벨 · 볼트.
        Chamfer(m, new Vector3(2.0f, 1.3f, 1.5f), new Vector3(-0.35f, 1.31f, 0f), body, 0.04f, "Housing");
        Box(m, new Vector3(0.9f, 0.9f, 0.03f), new Vector3(-0.62f, 1.3f, 0.76f), KPanel, "AccessDoor");
        Box(m, new Vector3(0.05f, 0.18f, 0.05f), new Vector3(-0.24f, 1.3f, 0.79f), KAlu, "DoorLatch");
        Louver(m, new Vector3(0.28f, 1.55f, 0.76f), 0.5f, 0.4f, Vector3.Zero, 5);
        Label(m, new Vector3(0.28f, 1.12f, 0.765f), new Vector2(0.46f, 0.18f), Vector3.Zero);
        Box(m, new Vector3(0.36f, 0.24f, 0.012f), new Vector3(-0.95f, 1.82f, 0.765f), KWarn, "Caution");
        BoltRow(m, new Vector3(-1.3f, 1.95f, 0.76f), new Vector3(0.6f, 1.95f, 0.76f), 6, 0.018f, KAlu);
        BoltRow(m, new Vector3(-1.3f, 0.7f, 0.76f), new Vector3(0.6f, 0.7f, 0.76f), 6, 0.018f, KAlu);

        // 모터 — 냉각핀이 촘촘한 원통. 축은 x 방향.
        var motor = new Node3D { Position = new Vector3(1.05f, 1.06f, 0f), RotationDegrees = new Vector3(0f, 0f, 90f) };
        m.AddChild(motor);
        Cyl(motor, 0.4f, 0.95f, Vector3.Zero, KSteel, "Motor");
        for (int i = 0; i < 9; i++)
            Cyl(motor, 0.45f, 0.025f, new Vector3(0f, -0.4f + i * 0.1f, 0f), KSteelDark, "Fin");
        Cyl(motor, 0.3f, 0.08f, new Vector3(0f, -0.52f, 0f), KAlu, "EndCap");
        Chamfer(m, new Vector3(0.3f, 0.22f, 0.36f), new Vector3(1.05f, 1.56f, 0.12f), KPanelDark, 0.01f, "TermBox");

        // 플라이휠 — 돌기 시작하면 이것이 가장 먼저 눈에 들어온다. 살 넷 · 테 · 허브.
        _machineFly = new Node3D { Name = "Flywheel", Position = new Vector3(1.66f, 1.06f, 0f) };
        m.AddChild(_machineFly);
        var rim = new MeshInstance3D
        {
            Name = "Rim",
            Mesh = new TorusMesh { InnerRadius = 0.46f, OuterRadius = 0.58f, Rings = 24, RingSegments = 8 },
            RotationDegrees = new Vector3(0f, 0f, 90f),
            MaterialOverride = KSteelDark,
        };
        _machineFly.AddChild(rim);
        Cyl(_machineFly, 0.13f, 0.16f, Vector3.Zero, KAlu, "Hub").RotationDegrees = new Vector3(0f, 0f, 90f);
        for (int i = 0; i < 4; i++)
        {
            var spoke = Box(_machineFly, new Vector3(0.06f, 0.92f, 0.08f), Vector3.Zero, KSteel, "Spoke");
            spoke.RotationDegrees = new Vector3(i * 45f, 0f, 0f);
        }
        Box(_machineFly, new Vector3(0.07f, 0.1f, 0.06f), new Vector3(0.02f, 0.52f, 0f), KWarn, "Mark");
        // 휠 가드 — 반만 덮는다(돌아가는 것이 보여야 한다).
        Box(m, new Vector3(0.28f, 0.06f, 1.3f), new Vector3(1.66f, 1.72f, 0f), KWarn, "GuardTop");
        Box(m, new Vector3(0.28f, 0.66f, 0.06f), new Vector3(1.66f, 1.42f, -0.64f), KWarn, "GuardSide");

        // 프레스 — 두 기둥 · 윗보 · 유압 실린더 · 오르내리는 머리 · 받침 금형.
        foreach (float sz in new[] { -0.5f, 0.5f })
            Pipe(m, new Vector3(-0.85f, 1.96f, sz), new Vector3(-0.85f, 3.65f, sz), 0.07f, KSteel, "Column", 10);
        Chamfer(m, new Vector3(0.62f, 0.3f, 1.34f), new Vector3(-0.85f, 3.74f, 0f), body, 0.025f, "Crown");
        Cyl(m, 0.17f, 0.62f, new Vector3(-0.85f, 3.32f, 0f), KAlu, "HydCylinder");
        Box(m, new Vector3(0.7f, 0.12f, 0.9f), new Vector3(-0.85f, 2.02f, 0f), KSteelDark, "Die");
        _machineArm = new Node3D { Name = "PressHead", Position = new Vector3(-0.85f, MachineArmY, 0f) };
        m.AddChild(_machineArm);
        Pipe(_machineArm, new Vector3(0f, 0.15f, 0f), new Vector3(0f, 0.75f, 0f), 0.06f, KAlu, "Ram", 10);
        Chamfer(_machineArm, new Vector3(0.6f, 0.26f, 0.84f), Vector3.Zero, KSteelDark, 0.02f, "Head");
        WarnStripe(_machineArm, new Vector3(0f, -0.06f, 0.43f), 0.6f, 0.1f, 0.01f, Vector3.Zero, 6);
        // 유압 호스 — 실린더에서 몸체로 늘어진다.
        CableSag(m, new Vector3(-0.68f, 3.45f, 0.1f), new Vector3(-0.1f, 1.98f, 0.4f), 0.22f, 0.03f, KRubber, 6);
        CableSag(m, new Vector3(-0.68f, 3.2f, -0.1f), new Vector3(-0.1f, 1.98f, -0.4f), 0.18f, 0.025f, KRubber, 6);

        // 조작반 — 기계 앞 오른쪽 받침대. 비스듬한 판 위에 계기 · 버튼 · 비상 정지.
        var ctl = new Node3D { Position = new Vector3(-1.85f, 0f, 1.2f), RotationDegrees = new Vector3(0f, 28f, 0f) };
        m.AddChild(ctl);
        Pipe(ctl, new Vector3(0f, 0.16f, 0f), new Vector3(0f, 0.98f, 0f), 0.06f, KSteelDark, "Stand", 8);
        Box(ctl, new Vector3(0.5f, 0.06f, 0.4f), new Vector3(0f, 0.18f, 0f), KSteelDark, "StandFoot");
        var face = new Node3D { Position = new Vector3(0f, 1.12f, 0f), RotationDegrees = new Vector3(-32f, 0f, 0f) };
        ctl.AddChild(face);
        Chamfer(face, new Vector3(0.62f, 0.42f, 0.14f), Vector3.Zero, KPanel, 0.015f, "CtlBox");
        foreach (float sx in new[] { -0.16f, 0.06f })
            Cyl(face, 0.075f, 0.02f, new Vector3(sx, 0.08f, 0.075f), KSteelDark, "Gauge").RotationDegrees = new Vector3(90f, 0f, 0f);
        Cyl(face, 0.06f, 0.05f, new Vector3(0.22f, 0.08f, 0.09f), Mat(new Color(0.55f, 0.07f, 0.05f), 0.6f), "EStop")
            .RotationDegrees = new Vector3(90f, 0f, 0f);
        for (int k = 0; k < 4; k++)
            Box(face, new Vector3(0.06f, 0.05f, 0.03f), new Vector3(-0.2f + k * 0.11f, -0.1f, 0.075f), KAlu, "Button");
        CableSag(m, new Vector3(-1.85f, 0.2f, 1.2f), new Vector3(-1.1f, 0.2f, 0.8f), 0.05f, 0.025f, KCable, 4);

        // 경광등 — 몸체 위. 기계가 켜지면 주황으로 돈다.
        Pipe(m, new Vector3(0.45f, 1.96f, 0.45f), new Vector3(0.45f, 2.55f, 0.45f), 0.03f, KSteelDark, "BeaconPole", 6);
        Cyl(m, 0.1f, 0.06f, new Vector3(0.45f, 2.58f, 0.45f), KPlasticDark, "BeaconBase");
        _machineLampMat = Glow(new Color(1f, 0.55f, 0.2f), 0f);
        _machineLampMat.AlbedoColor = new Color(0.22f, 0.12f, 0.05f);
        Cyl(m, 0.085f, 0.2f, new Vector3(0.45f, 2.71f, 0.45f), _machineLampMat, "Lamp");
        // 몸체 앞면 작은 표시등 — 경광등과 같은 재질이라 함께 켜진다.
        Box(m, new Vector3(0.06f, 0.06f, 0.02f), new Vector3(-1.15f, 0.95f, 0.77f), _machineLampMat, "PilotLamp");

        // 바닥으로 들어가는 배관 · 케이블.
        Pipe(m, new Vector3(-1.2f, 1.1f, -0.75f), new Vector3(-1.2f, 0.2f, -1.4f), 0.07f, KSteel, "Feed", 8);
        Pipe(m, new Vector3(-1.2f, 0.2f, -1.4f), new Vector3(-2.6f, 0.2f, -1.4f), 0.07f, KSteel, "Feed", 8);
        CableSag(m, new Vector3(0.9f, 0.5f, -0.9f), new Vector3(2.4f, 0.04f, -1.9f), 0.06f, 0.03f, KCable, 5);

        // 안전 난간 — 카메라 쪽 앞. 사람이 다니는 공장이라는 크기 기준이 된다.
        foreach (float x in new[] { -2.0f, -0.9f, 0.2f })
        {
            Pipe(m, new Vector3(x, 0.0f, 2.25f), new Vector3(x, 1.05f, 2.25f), 0.03f, KWarn, "RailPost", 8);
            Box(m, new Vector3(0.1f, 0.2f, 0.1f), new Vector3(x, 0.1f, 2.25f), KSteelDark, "RailFoot");
        }
        Pipe(m, new Vector3(-2.0f, 1.05f, 2.25f), new Vector3(0.2f, 1.05f, 2.25f), 0.028f, KWarn, "Rail", 8);
        Pipe(m, new Vector3(-2.0f, 0.55f, 2.25f), new Vector3(0.2f, 0.55f, 2.25f), 0.022f, KWarn, "Rail", 8);
    }

    // ── 몽타주 연출 ──────────────────────────────────────────────────────

    public void CorridorReset()
    {
        if (_bulkhead != null) _bulkhead.Position = _bulkhead.Position with { Y = 6.6f };
        if (_bulkheadLockMat != null) _bulkheadLockMat.EmissionEnergyMultiplier = 0f;
        foreach (var l in _doorLights) if (l != null) l.LightEnergy = 0f;
        foreach (var mat in _doorLampMats) if (mat != null) mat.EmissionEnergyMultiplier = 0f;
        if (_panelMat != null) _panelMat.EmissionEnergyMultiplier = 0f;
        if (_panelLedMat != null) _panelLedMat.EmissionEnergyMultiplier = 0f;
        if (_panelFace != null) _panelFace.Visible = false;
        if (_panelLight != null) _panelLight.LightEnergy = 0f;
        if (_machineLampMat != null) _machineLampMat.EmissionEnergyMultiplier = 0f;
        if (_machineLight != null) _machineLight.LightEnergy = 0f;
        if (_machine != null) _machine.Position = _machine.Position with { X = -0.2f };
        if (_machineArm != null) _machineArm.Position = _machineArm.Position with { Y = MachineArmY };
        _machineRunning = false;
        _machineT = 0f;
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
        if (_panelLedMat != null) _panelLedMat.EmissionEnergyMultiplier = energy * 0.8f;
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

    // 기계가 돈다 — 플라이휠이 돌고 프레스 머리가 오르내린다. 몸체는 **아주 미세하게** 떤다.
    // 모델 전체가 미친 듯이 흔들리면 안 된다(§16).
    private void TickMachine(float delta)
    {
        if (!_machineRunning || _machine == null) return;
        _machineT += delta;
        // 처음 1초는 천천히 회전이 붙는다.
        float spin = Mathf.Min(1f, _machineT / 1.0f);
        _machine.Position = _machine.Position with { X = -0.2f + Mathf.Sin(_machineT * 38f) * 0.008f * spin };
        if (_machineFly != null) _machineFly.Rotate(Vector3.Right, delta * 9f * spin);
        if (_machineArm != null)
            _machineArm.Position = _machineArm.Position with
            {
                Y = MachineArmY - (0.5f - 0.5f * Mathf.Cos(_machineT * 3.4f)) * 0.42f * spin,
            };
    }
}
