using Godot;

namespace NSP.View;

// 작업실 3D 를 "같은 시설의 서로 다른 방"으로 보이게 하는 마감 레이어.
//
// 방 씬(scenes/rooms/*.tscn)은 전부 room_base.tscn 을 깔고 핵심 장비만 얹는다. 그래서
// 벽 · 바닥 · 천장이 아홉 방 모두 **같은 재질 한 벌**이었고, 조명도 천장등 하나뿐이라
// 어느 방이든 "푸른 회색 상자"로 보였다. 방을 구분해 주던 것은 가운데 놓인 물건뿐이었다.
//
// 여기서 하는 일은 셋이다.
//   ① 껍데기 다시 칠하기 — 방마다 벽 · 바닥 · 천장 재질을 따로 준다(콘크리트 · 금속 패널 ·
//      에폭시 · 논슬립 등). 같은 시설처럼 보이되 방마다 다르게.
//   ② 공통 구조 얹기   — 걸레받이, 벽 패널 이음선, 천장 덕트 · 보조 조명, 바닥 구역선.
//      '기본 큐브에 조명 하나' 느낌을 깨는 것은 결국 모서리와 이음선이다.
//   ③ 방별 소품       — 그 방에서 실제로 무슨 일을 하는지 읽히는 물건들.
//
// **씬 파일은 고치지 않는다.** 방 씬에는 WorkSpot · InteractionTarget · GhostHideSpot 처럼
// 게임 로직이 이름으로 찾아 쓰는 노드가 들어 있어서, 텍스트로 손대면 그쪽이 조용히 깨진다.
// 이 레이어는 씬을 띄운 **뒤에** 붙으므로 그 노드들을 건드리지 않는다.
//
// 붙이는 곳은 FacilityCctvWorld(실제 게임 화면)와 작업실 미리보기 두 군데다.
public static class RoomDressing
{
    // 이미 꾸민 방에 두 번 붙지 않게 하는 표식.
    private const string Marker = "RoomDressing";

    public static void Apply(string roomId, Node3D room)
    {
        if (room == null || room.GetNodeOrNull(Marker) != null) return;
        var host = new Node3D { Name = Marker };
        room.AddChild(host);

        var p = Palette(roomId);
        Repaint(room, p);
        CommonShell(host, p);
        Props(roomId, host, p);
        Lighting(room, host, p);
    }

    // ── 방별 색 · 재질 ───────────────────────────────────────────────────
    //
    // 같은 기관의 지하시설이라 톤은 한 계열로 묶되, 방마다 벽과 바닥의 재질감이 다르다.
    private sealed class Pal
    {
        public Color Wall, Floor, Ceiling, Trim, Accent;
        public float WallRough = 0.92f, WallMetal = 0f;
        public float FloorRough = 0.8f, FloorMetal = 0f;
        // 바닥 구역선 색. 방마다 다른 이 선 하나가 멀리서도 방을 구분해 준다.
        public Color Line;
        public float LightEnergy = 2.2f;
        public Color LightColor = new(0.78f, 0.84f, 0.98f);
    }

    private static Pal Palette(string id) => id switch
    {
        // 격리실 — 도장 금속 + 차가운 콘크리트. 붉은 경고등은 조명이 담당한다.
        "isolation_room" => new Pal
        {
            Wall = new(0.20f, 0.185f, 0.19f), Floor = new(0.145f, 0.135f, 0.14f),
            Ceiling = new(0.145f, 0.14f, 0.15f), Trim = new(0.30f, 0.26f, 0.26f),
            Accent = new(0.62f, 0.74f, 0.92f), Line = new(0.62f, 0.16f, 0.14f),
            WallRough = 0.72f, WallMetal = 0.25f, FloorRough = 0.62f,
            LightEnergy = 1.5f, LightColor = new(0.95f, 0.72f, 0.70f),
        },
        // 코어복구실 — 중장비실. 금속 비중이 높고 바닥은 작업 흔적이 있는 에폭시.
        "core_room" => new Pal
        {
            Wall = new(0.17f, 0.195f, 0.225f), Floor = new(0.135f, 0.155f, 0.175f),
            Ceiling = new(0.13f, 0.15f, 0.175f), Trim = new(0.34f, 0.38f, 0.44f),
            Accent = new(0.35f, 0.78f, 0.95f), Line = new(0.25f, 0.55f, 0.72f),
            WallRough = 0.55f, WallMetal = 0.45f, FloorRough = 0.52f, FloorMetal = 0.15f,
            LightEnergy = 2.3f, LightColor = new(0.74f, 0.86f, 1f),
        },
        // 경비실 — 무광 벽 + 카펫 타일 느낌. 빛은 모니터가 만든다.
        "guard_room" => new Pal
        {
            Wall = new(0.155f, 0.165f, 0.185f), Floor = new(0.11f, 0.115f, 0.13f),
            Ceiling = new(0.125f, 0.13f, 0.15f), Trim = new(0.24f, 0.26f, 0.30f),
            Accent = new(0.45f, 0.85f, 0.75f), Line = new(0.30f, 0.42f, 0.46f),
            WallRough = 0.95f, FloorRough = 0.95f,
            LightEnergy = 1.9f, LightColor = new(0.72f, 0.80f, 0.96f),
        },
        // 의무실 — 밝은 위생 패널 + 비닐 바닥. 시설에서 가장 밝은 방이다.
        "medical_room" => new Pal
        {
            Wall = new(0.42f, 0.45f, 0.47f), Floor = new(0.30f, 0.33f, 0.35f),
            Ceiling = new(0.40f, 0.43f, 0.46f), Trim = new(0.52f, 0.56f, 0.58f),
            Accent = new(0.62f, 0.78f, 0.88f), Line = new(0.52f, 0.62f, 0.70f),
            WallRough = 0.58f, FloorRough = 0.45f,
            LightEnergy = 2.9f, LightColor = new(0.92f, 0.95f, 1f),
        },
        // 환기실 — 도장 철판. 가장 산업적이고 가장 지저분하다.
        "vent_room" => new Pal
        {
            Wall = new(0.175f, 0.175f, 0.165f), Floor = new(0.125f, 0.125f, 0.12f),
            Ceiling = new(0.14f, 0.14f, 0.135f), Trim = new(0.32f, 0.31f, 0.27f),
            Accent = new(0.55f, 0.72f, 0.80f), Line = new(0.58f, 0.52f, 0.22f),
            WallRough = 0.62f, WallMetal = 0.40f, FloorRough = 0.88f,
            LightEnergy = 2.2f, LightColor = new(0.82f, 0.84f, 0.90f),
        },
        // 정비실 — 기름때 낀 작업장. 벽은 거칠고 바닥은 가장 어둡다.
        "maintenance_room" => new Pal
        {
            Wall = new(0.185f, 0.175f, 0.155f), Floor = new(0.115f, 0.11f, 0.10f),
            Ceiling = new(0.145f, 0.14f, 0.13f), Trim = new(0.38f, 0.33f, 0.22f),
            Accent = new(0.95f, 0.62f, 0.22f), Line = new(0.62f, 0.45f, 0.14f),
            WallRough = 0.95f, WallMetal = 0.12f, FloorRough = 0.92f,
            LightEnergy = 2.6f, LightColor = new(0.98f, 0.90f, 0.78f),
        },
        // 저장고 — 민무늬 콘크리트. 조명이 가장 성기고 구석이 어둡다.
        "storage_room" => new Pal
        {
            Wall = new(0.195f, 0.19f, 0.175f), Floor = new(0.145f, 0.14f, 0.13f),
            Ceiling = new(0.15f, 0.147f, 0.138f), Trim = new(0.30f, 0.28f, 0.24f),
            Accent = new(0.80f, 0.74f, 0.52f), Line = new(0.66f, 0.58f, 0.30f),
            WallRough = 0.98f, FloorRough = 0.90f,
            LightEnergy = 3.1f, LightColor = new(0.86f, 0.84f, 0.78f),
        },
        // 휴게실 — 유일하게 따뜻한 방. 벽에 도장 패널, 바닥에 생활 마모.
        "rest_room" => new Pal
        {
            Wall = new(0.26f, 0.235f, 0.205f), Floor = new(0.185f, 0.165f, 0.145f),
            Ceiling = new(0.22f, 0.205f, 0.185f), Trim = new(0.36f, 0.31f, 0.25f),
            Accent = new(0.95f, 0.78f, 0.50f), Line = new(0.45f, 0.38f, 0.28f),
            WallRough = 0.85f, FloorRough = 0.78f,
            LightEnergy = 2.0f, LightColor = new(1f, 0.90f, 0.76f),
        },
        _ => new Pal
        {
            Wall = new(0.17f, 0.19f, 0.23f), Floor = new(0.12f, 0.13f, 0.16f),
            Ceiling = new(0.15f, 0.17f, 0.20f), Trim = new(0.28f, 0.30f, 0.34f),
            Accent = new(0.55f, 0.70f, 0.85f), Line = new(0.35f, 0.42f, 0.52f),
        },
    };

    // ── ① 껍데기 다시 칠하기 ─────────────────────────────────────────────
    private static void Repaint(Node3D room, Pal p)
    {
        Paint(room, "RoomBase/Shell/Floor", Mat(p.Floor, p.FloorRough, p.FloorMetal));
        var wall = Mat(p.Wall, p.WallRough, p.WallMetal);
        Paint(room, "RoomBase/Shell/WallBack", wall);
        Paint(room, "RoomBase/Shell/WallLeft", wall);
        Paint(room, "RoomBase/Shell/Ceiling", Mat(p.Ceiling, 0.95f));

        // 의무실 벽의 형광 초록 십자 — 공간 맥락에 맞지 않는 네온이라 떼어 낸다.
        // 의료 표식 자체는 아래 Props 에서 차분한 표지판으로 다시 세운다.
        foreach (string n in new[] { "CrossSign", "MedCrossV", "MedCrossH" })
            if (room.GetNodeOrNull<Node3D>(n) is { } cross) cross.Visible = false;
        // 약병 · 모니터의 형광 초록도 의료기기다운 차분한 색으로 바꾼다.
        foreach (string n in new[] { "MedBottle0", "MedBottle1", "MedBottle2", "MedBottle3" })
            Paint(room, n, Mat(new Color(0.78f, 0.82f, 0.80f), 0.35f));
        Paint(room, "MedMonitorScreen", Glow(new Color(0.42f, 0.68f, 0.82f), 0.9f));
    }

    private static void Paint(Node3D room, string path, Material m)
    {
        if (room.GetNodeOrNull<MeshInstance3D>(path) is { } mi) mi.MaterialOverride = m;
    }

    // ── ② 모든 방에 공통으로 얹는 구조 ───────────────────────────────────
    //
    // 방이 '상자'로 보이는 가장 큰 이유는 벽과 바닥이 **한 장의 평면**이기 때문이다.
    // 걸레받이 · 패널 이음선 · 천장 덕트가 들어가면 같은 형상이라도 두께가 생긴다.
    private static void CommonShell(Node3D host, Pal p)
    {
        var trim = Mat(p.Trim, 0.5f, 0.35f);
        var dark = Mat(p.Ceiling * 0.7f, 0.9f);

        // 걸레받이 — 벽과 바닥이 만나는 선. 이것 하나로 바닥이 '깔린' 것처럼 보인다.
        Box(host, new Vector3(6.2f, 0.14f, 0.06f), new Vector3(0f, 0.07f, -2.9f), trim, "KickBack");
        Box(host, new Vector3(0.06f, 0.14f, 6.2f), new Vector3(-2.9f, 0.07f, 0f), trim, "KickLeft");

        // 벽 패널 이음선 — 세로로 일정 간격. 벽이 한 장이 아니라 여러 장으로 읽힌다.
        for (int i = -2; i <= 2; i++)
        {
            Box(host, new Vector3(0.05f, 3.0f, 0.04f), new Vector3(i * 1.2f, 1.6f, -2.92f), trim);
            Box(host, new Vector3(0.04f, 3.0f, 0.05f), new Vector3(-2.92f, 1.6f, i * 1.2f), trim);
        }
        // 허리 높이 가로 몰딩 — 세로선만 있으면 격자무늬처럼 보인다.
        Box(host, new Vector3(6.2f, 0.07f, 0.05f), new Vector3(0f, 1.15f, -2.92f), trim, "RailBack");
        Box(host, new Vector3(0.05f, 0.07f, 6.2f), new Vector3(-2.92f, 1.15f, 0f), trim, "RailLeft");

        // 천장 덕트와 배선 트레이 — 지하시설의 천장은 비어 있지 않다.
        //
        // **왼쪽 벽 가까이(x = -2.1)에 붙인다.** CCTV 는 오른쪽 앞 코너 위에서 내려다보므로
        // 오른쪽 천장에 무엇을 달면 그게 화면 위를 통째로 가린다(처음에 x = 2.1 에 뒀다가
        // 방이 반쯤 안 보였다).
        Box(host, new Vector3(0.46f, 0.30f, 5.6f), new Vector3(-2.15f, 3.02f, 0f), dark, "Duct");
        for (int i = -2; i <= 2; i++)
            Box(host, new Vector3(0.52f, 0.05f, 0.07f), new Vector3(-2.15f, 3.19f, i * 1.1f), trim);
        Box(host, new Vector3(4.6f, 0.08f, 0.12f), new Vector3(-0.4f, 3.17f, -2.72f), trim, "CableTray");

        // 천장 환기 그릴 — 덕트가 어디로 빠지는지 보이게.
        var grille = Mat(p.Trim * 0.8f, 0.6f, 0.5f);
        Box(host, new Vector3(0.56f, 0.05f, 0.56f), new Vector3(-2.15f, 2.88f, -1.9f), grille, "Grille");
        for (int i = 0; i < 4; i++)
            Box(host, new Vector3(0.50f, 0.03f, 0.06f), new Vector3(-2.15f, 2.85f, -2.08f + i * 0.13f), dark);

        // 바닥 구역선 — 방마다 색이 다르다. 멀리서도 방이 구분되는 가장 싼 장치다.
        var line = Unshaded(p.Line * 0.75f);
        Box(host, new Vector3(3.0f, 0.012f, 0.06f), new Vector3(-0.6f, 0.005f, 1.35f), line, "FloorLineA");
        Box(host, new Vector3(0.06f, 0.012f, 2.4f), new Vector3(-1.95f, 0.005f, 0.1f), line, "FloorLineB");
    }

    // ── ③ 방별 소품 ─────────────────────────────────────────────────────
    private static void Props(string id, Node3D h, Pal p)
    {
        switch (id)
        {
            case "isolation_room": Isolation(h, p); break;
            case "core_room": Core(h, p); break;
            case "guard_room": Guard(h, p); break;
            case "medical_room": Medical(h, p); break;
            case "vent_room": Vent(h, p); break;
            case "maintenance_room": Maintenance(h, p); break;
            case "storage_room": Storage(h, p); break;
            case "rest_room": Rest(h, p); break;
        }
    }

    // 격리실 — 병실이 아니라 **통제 공간**. 관찰창과 잠금 표시가 그 차이를 만든다.
    private static void Isolation(Node3D h, Pal p)
    {
        var steel = Mat(new Color(0.30f, 0.28f, 0.29f), 0.45f, 0.6f);
        var pad = Mat(new Color(0.16f, 0.15f, 0.16f), 0.98f);

        // 방음 · 격리 패널 — 뒷벽 한 면을 흡음 패널로 덮는다.
        for (int x = -2; x <= 2; x++)
        for (int y = 0; y < 3; y++)
        {
            // 관찰창과 감시 패널이 앉을 칸은 비워 둔다 — 안 그러면 패널 뒤로 묻힌다.
            if (y == 2 && (x == -1 || x == 1)) continue;
            Box(h, new Vector3(1.05f, 0.80f, 0.05f), new Vector3(x * 1.12f, 0.55f + y * 0.88f, -2.88f), pad);
        }

        // 관찰창 — 밖에서 안을 들여다보는 창. 유리는 아주 약하게만 빛난다.
        Box(h, new Vector3(1.20f, 0.70f, 0.12f), new Vector3(-1.12f, 2.31f, -2.84f), steel, "WindowFrame");
        Box(h, new Vector3(1.04f, 0.54f, 0.04f), new Vector3(-1.12f, 2.31f, -2.79f),
            Glow(new Color(0.58f, 0.68f, 0.80f), 0.55f), "WindowGlass");
        Box(h, new Vector3(0.05f, 0.54f, 0.05f), new Vector3(-1.12f, 2.31f, -2.77f), steel);

        // 감시 패널 · 상태등 · 잠금 표시.
        Box(h, new Vector3(0.52f, 0.66f, 0.11f), new Vector3(1.12f, 2.31f, -2.84f), steel, "MonitorPanel");
        Box(h, new Vector3(0.40f, 0.28f, 0.03f), new Vector3(1.12f, 2.44f, -2.79f),
            Glow(new Color(0.80f, 0.30f, 0.24f), 1.3f));
        Box(h, new Vector3(0.11f, 0.11f, 0.04f), new Vector3(1.12f, 2.10f, -2.79f),
            Unshaded(new Color(0.95f, 0.78f, 0.25f)), "LockLamp");

        // 경고 라벨 — 글자는 넣지 않는다(해상도에서 뭉갠다). 사선 경고띠로 읽힌다.
        for (int i = 0; i < 5; i++)
            Box(h, new Vector3(0.14f, 0.26f, 0.02f), new Vector3(-2.93f, 1.70f, -1.3f + i * 0.22f),
                Unshaded(i % 2 == 0 ? new Color(0.72f, 0.60f, 0.12f) : new Color(0.12f, 0.12f, 0.12f)));

        // 침대별 구역 표시 — 바닥에 사각 테두리.
        foreach (float x in new[] { -1.3f, 0.6f })
        {
            Box(h, new Vector3(1.5f, 0.012f, 0.05f), new Vector3(x, 0.006f, -1.75f), Unshaded(p.Line));
            Box(h, new Vector3(1.5f, 0.012f, 0.05f), new Vector3(x, 0.006f, -0.35f), Unshaded(p.Line));
        }

        // 보조 집기 — 폐기함 · 보관함 · 스툴. 휑한 바닥을 메운다.
        Box(h, new Vector3(0.36f, 0.52f, 0.36f), new Vector3(2.25f, 0.26f, -2.3f), steel, "WasteBin");
        Box(h, new Vector3(0.34f, 0.06f, 0.34f), new Vector3(2.25f, 0.55f, -2.3f), pad);
        Box(h, new Vector3(0.50f, 1.25f, 0.34f), new Vector3(-2.5f, 0.62f, 1.6f), steel, "Locker");
        Cyl(h, 0.17f, 0.42f, new Vector3(1.9f, 0.21f, 0.4f), steel, "Stool");
    }

    // 코어복구실 — 빛나는 통 하나가 아니라 **그것을 둘러싼 설비 전체**가 보여야 한다.
    private static void Core(Node3D h, Pal p)
    {
        var steel = Mat(new Color(0.33f, 0.37f, 0.43f), 0.4f, 0.75f);
        var dark = Mat(new Color(0.12f, 0.14f, 0.17f), 0.6f, 0.4f);

        // 전력 분배 패널 — 뒷벽 한 줄. 계량기와 차단기처럼 보이게 칸을 나눈다.
        Box(h, new Vector3(2.1f, 1.30f, 0.22f), new Vector3(-1.3f, 1.75f, -2.82f), steel, "PowerPanel");
        for (int i = 0; i < 5; i++)
        {
            Box(h, new Vector3(0.34f, 0.46f, 0.05f), new Vector3(-2.1f + i * 0.40f, 2.05f, -2.70f), dark);
            Box(h, new Vector3(0.10f, 0.10f, 0.04f), new Vector3(-2.1f + i * 0.40f, 1.52f, -2.70f),
                Unshaded(i % 2 == 0 ? p.Accent : new Color(0.35f, 0.75f, 0.45f)));
        }

        // 냉각 배관 — 벽을 타고 천장으로 올라간다.
        foreach (float x in new[] { 0.7f, 1.1f })
        {
            Cyl(h, 0.09f, 2.4f, new Vector3(x, 1.2f, -2.78f), steel);
            Cyl(h, 0.12f, 0.1f, new Vector3(x, 0.55f, -2.78f), dark);
            Cyl(h, 0.12f, 0.1f, new Vector3(x, 1.85f, -2.78f), dark);
        }

        // 작업 콘솔 — 코어 옆에서 수치를 보는 자리.
        Box(h, new Vector3(1.0f, 0.78f, 0.52f), new Vector3(-2.15f, 0.39f, 0.7f), steel, "Console");
        Box(h, new Vector3(0.94f, 0.06f, 0.46f), new Vector3(-2.15f, 0.80f, 0.7f), dark);
        var screen = Box(h, new Vector3(0.80f, 0.44f, 0.04f), new Vector3(-2.12f, 1.12f, 0.62f),
            Glow(p.Accent, 0.8f), "ConsoleScreen");
        screen.RotationDegrees = new Vector3(-14f, 0f, 0f);

        // 안전 반경 — 코어 둘레의 경고 라인. 방 한가운데를 비워 두는 이유가 보인다.
        for (int i = 0; i < 16; i++)
        {
            double a = Mathf.Tau * i / 16.0;
            var at = new Vector3(Mathf.Cos((float)a) * 1.55f, 0.006f, -0.45f + Mathf.Sin((float)a) * 1.55f);
            var seg = Box(h, new Vector3(0.30f, 0.012f, 0.07f), at,
                Unshaded(i % 2 == 0 ? new Color(0.78f, 0.62f, 0.14f) : new Color(0.14f, 0.14f, 0.14f)));
            seg.RotationDegrees = new Vector3(0f, -(float)(a * 180.0 / Mathf.Pi), 0f);
        }

        // 공구 카트 · 예비 부품함 — 사람이 드나드는 작업실이라는 증거.
        Box(h, new Vector3(0.62f, 0.07f, 0.44f), new Vector3(1.85f, 0.70f, 1.5f), steel, "CartTop");
        Box(h, new Vector3(0.58f, 0.06f, 0.40f), new Vector3(1.85f, 0.34f, 1.5f), steel);
        foreach (var (dx, dz) in new[] { (-0.26f, -0.18f), (0.26f, -0.18f), (-0.26f, 0.18f), (0.26f, 0.18f) })
            Cyl(h, 0.035f, 0.30f, new Vector3(1.85f + dx, 0.15f, 1.5f + dz), dark);
        Box(h, new Vector3(0.46f, 0.30f, 0.34f), new Vector3(1.85f, 0.88f, 1.5f), dark, "PartsBox");
    }

    // 경비실 — 사무실이 아니라 **관제실**. 벽이 정보로 덮여 있어야 한다.
    private static void Guard(Node3D h, Pal p)
    {
        var steel = Mat(new Color(0.22f, 0.24f, 0.28f), 0.55f, 0.4f);
        var dark = Mat(new Color(0.08f, 0.09f, 0.11f), 0.5f);

        // 벽면 모니터 월 — 작은 화면 여섯 개. 각자 조금씩 다른 밝기로 켜 둔다.
        for (int i = 0; i < 6; i++)
        {
            float x = -2.2f + (i % 3) * 0.78f;
            float y = 2.30f - (i / 3) * 0.62f;
            Box(h, new Vector3(0.70f, 0.52f, 0.07f), new Vector3(x, y, -2.88f), dark);
            Box(h, new Vector3(0.62f, 0.44f, 0.03f), new Vector3(x, y, -2.93f),
                Glow(new Color(0.30f, 0.55f, 0.62f), 0.35f + (i % 3) * 0.18f));
        }

        // 시설 구역도 — 왼쪽 벽. 선 몇 개로 '지도'로 읽힌다.
        Box(h, new Vector3(0.08f, 1.15f, 1.75f), new Vector3(-2.86f, 1.85f, 0.4f), steel, "MapBoard");
        for (int i = 0; i < 4; i++)
            Box(h, new Vector3(0.03f, 0.05f, 1.45f), new Vector3(-2.90f, 1.45f + i * 0.26f, 0.4f),
                Unshaded(p.Accent * 0.8f));

        // 서버 랙 — 관제실 옆에 늘 서 있는 물건.
        Box(h, new Vector3(0.62f, 1.85f, 0.58f), new Vector3(2.25f, 0.93f, -2.1f), dark, "Rack");
        for (int i = 0; i < 7; i++)
        {
            Box(h, new Vector3(0.56f, 0.16f, 0.04f), new Vector3(2.25f, 0.28f + i * 0.24f, -1.83f), steel);
            Box(h, new Vector3(0.06f, 0.04f, 0.03f), new Vector3(2.45f, 0.28f + i * 0.24f, -1.80f),
                Unshaded(i % 3 == 0 ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.75f, 0.55f, 0.20f)));
        }

        // 통신 장비 · 서류함.
        Box(h, new Vector3(0.34f, 0.12f, 0.26f), new Vector3(1.1f, 0.84f, -1.0f), dark, "Radio");
        Cyl(h, 0.012f, 0.42f, new Vector3(1.22f, 1.10f, -1.0f), steel, "Antenna");
        Box(h, new Vector3(0.52f, 0.68f, 0.42f), new Vector3(-2.35f, 0.34f, -1.5f), steel, "Cabinet");
        Box(h, new Vector3(0.46f, 0.03f, 0.36f), new Vector3(-2.33f, 0.70f, -1.5f),
            Mat(new Color(0.72f, 0.70f, 0.64f), 0.95f), "Papers");

        // 바닥 케이블 트레이 — 관제실 바닥은 늘 선이 지나간다.
        Box(h, new Vector3(0.22f, 0.05f, 2.6f), new Vector3(1.85f, 0.025f, -0.6f), dark, "CableRun");
    }

    // 의무실 — 네온 초록을 걷어 내고, 의료 가구와 기기로 기능을 보여 준다.
    private static void Medical(Node3D h, Pal p)
    {
        var white = Mat(new Color(0.78f, 0.80f, 0.82f), 0.45f);
        var steel = Mat(new Color(0.55f, 0.58f, 0.60f), 0.35f, 0.6f);
        var dark = Mat(new Color(0.18f, 0.20f, 0.22f), 0.6f);

        // 의료 표식 — 네온이 아니라 **벽에 붙은 표지판**. 흰 바탕에 차분한 청색 십자.
        Box(h, new Vector3(0.46f, 0.46f, 0.03f), new Vector3(-1.5f, 2.05f, -2.93f), white, "SignPlate");
        Box(h, new Vector3(0.32f, 0.10f, 0.02f), new Vector3(-1.5f, 2.05f, -2.95f), Unshaded(new Color(0.30f, 0.45f, 0.62f)));
        Box(h, new Vector3(0.10f, 0.32f, 0.02f), new Vector3(-1.5f, 2.05f, -2.95f), Unshaded(new Color(0.30f, 0.45f, 0.62f)));

        // 약품 캐비닛 — 유리문 두 짝.
        Box(h, new Vector3(1.05f, 1.35f, 0.36f), new Vector3(0.9f, 1.75f, -2.78f), white, "MedCabinet");
        foreach (float dx in new[] { -0.26f, 0.26f })
            Box(h, new Vector3(0.46f, 1.15f, 0.03f), new Vector3(0.9f + dx, 1.75f, -2.59f),
                Glow(new Color(0.60f, 0.70f, 0.78f), 0.18f));
        for (int i = 0; i < 3; i++)
            Box(h, new Vector3(0.96f, 0.03f, 0.30f), new Vector3(0.9f, 1.28f + i * 0.40f, -2.78f), steel);

        // 세면대 · 정리대.
        Box(h, new Vector3(0.80f, 0.86f, 0.50f), new Vector3(-2.35f, 0.43f, -1.2f), white, "Sink");
        Box(h, new Vector3(0.44f, 0.08f, 0.32f), new Vector3(-2.30f, 0.88f, -1.2f), steel);
        Cyl(h, 0.022f, 0.26f, new Vector3(-2.52f, 1.02f, -1.2f), steel, "Faucet");

        // 의료 카트 + 모니터링 장비.
        Box(h, new Vector3(0.50f, 0.06f, 0.38f), new Vector3(1.9f, 0.82f, 0.9f), white, "CartTop");
        Box(h, new Vector3(0.46f, 0.05f, 0.34f), new Vector3(1.9f, 0.44f, 0.9f), white);
        foreach (var (dx, dz) in new[] { (-0.20f, -0.15f), (0.20f, -0.15f), (-0.20f, 0.15f), (0.20f, 0.15f) })
            Cyl(h, 0.025f, 0.38f, new Vector3(1.9f + dx, 0.19f, 0.9f + dz), dark);
        Box(h, new Vector3(0.30f, 0.24f, 0.20f), new Vector3(1.9f, 0.97f, 0.9f), dark, "Vitals");
        Box(h, new Vector3(0.24f, 0.16f, 0.02f), new Vector3(1.9f, 0.99f, 0.79f),
            Glow(new Color(0.45f, 0.70f, 0.80f), 0.7f));

        // 주사대(IV) — 가는 기둥 하나로 의무실 실루엣이 완성된다.
        Cyl(h, 0.018f, 1.70f, new Vector3(-0.2f, 0.85f, 0.6f), steel, "IVPole");
        Box(h, new Vector3(0.22f, 0.03f, 0.22f), new Vector3(-0.2f, 0.02f, 0.6f), dark);
        Box(h, new Vector3(0.10f, 0.20f, 0.08f), new Vector3(-0.31f, 1.58f, 0.6f),
            Mat(new Color(0.82f, 0.86f, 0.84f), 0.3f), "IVBag");

        // 응급함 · 의료 폐기물통 · 스툴.
        Box(h, new Vector3(0.40f, 0.30f, 0.16f), new Vector3(2.4f, 1.55f, -1.6f),
            Mat(new Color(0.62f, 0.26f, 0.22f), 0.6f), "FirstAid");
        Cyl(h, 0.19f, 0.54f, new Vector3(2.35f, 0.27f, 0.0f), Mat(new Color(0.55f, 0.50f, 0.22f), 0.7f), "SharpsBin");
        Cyl(h, 0.16f, 0.44f, new Vector3(0.6f, 0.22f, 1.4f), steel, "Stool");

        // 커튼 레일 — 침상을 가리는 구조. 천장에서 내려온 선 하나면 충분하다.
        Box(h, new Vector3(2.4f, 0.04f, 0.04f), new Vector3(-0.6f, 2.55f, -0.3f), steel, "CurtainRail");
        for (int i = 0; i < 6; i++)
            Box(h, new Vector3(0.36f, 1.05f, 0.02f), new Vector3(-1.7f + i * 0.40f, 2.0f, -0.3f),
                Mat(new Color(0.68f, 0.72f, 0.74f), 0.95f));
    }

    // 환기실 — 공기를 거르고 밀어내는 **설비실**.
    private static void Vent(Node3D h, Pal p)
    {
        var steel = Mat(new Color(0.34f, 0.34f, 0.31f), 0.52f, 0.65f);
        var dark = Mat(new Color(0.14f, 0.14f, 0.13f), 0.75f);

        // 굵은 덕트 — 벽을 따라 들어와 천장으로 꺾인다.
        Cyl(h, 0.34f, 3.2f, new Vector3(-1.6f, 2.45f, -2.55f), steel, "DuctMain");
        var elbow = Cyl(h, 0.34f, 1.6f, new Vector3(-1.6f, 2.45f, -1.8f), steel, "DuctElbow");
        elbow.RotationDegrees = new Vector3(90f, 0f, 0f);
        foreach (float z in new[] { -2.6f, -1.6f })
            Cyl(h, 0.40f, 0.09f, new Vector3(-1.6f, 2.45f, z), dark, "Flange");

        // 필터 교체함 — 슬롯 세 칸. 한 칸은 비어 있다.
        Box(h, new Vector3(1.15f, 1.05f, 0.42f), new Vector3(1.5f, 0.95f, -2.74f), steel, "FilterBox");
        for (int i = 0; i < 3; i++)
            Box(h, new Vector3(0.32f, 0.78f, 0.06f), new Vector3(1.1f + i * 0.40f, 0.95f, -2.52f),
                i == 1 ? dark : Mat(new Color(0.46f, 0.44f, 0.36f), 0.95f));
        Box(h, new Vector3(1.05f, 0.08f, 0.05f), new Vector3(1.5f, 1.52f, -2.52f), dark, "FilterHandleBar");

        // 계기판 — 압력계 셋.
        Box(h, new Vector3(0.80f, 0.52f, 0.10f), new Vector3(-0.3f, 1.95f, -2.90f), steel, "GaugePanel");
        for (int i = 0; i < 3; i++)
        {
            Cyl(h, 0.09f, 0.04f, new Vector3(-0.56f + i * 0.26f, 1.95f, -2.96f), dark, "Gauge");
            Box(h, new Vector3(0.11f, 0.11f, 0.02f), new Vector3(-0.56f + i * 0.26f, 1.95f, -2.98f),
                Unshaded(new Color(0.62f, 0.66f, 0.62f)));
        }

        // 밸브 · 배관 — 바닥에서 올라오는 작은 관들.
        foreach (float x in new[] { -0.55f, -0.2f })
        {
            Cyl(h, 0.06f, 1.5f, new Vector3(x, 0.75f, -2.72f), steel);
            Cyl(h, 0.11f, 0.06f, new Vector3(x, 1.45f, -2.72f), dark);
        }
        Box(h, new Vector3(0.30f, 0.05f, 0.05f), new Vector3(-0.55f, 1.52f, -2.72f), Unshaded(p.Accent), "ValveHandle");

        // 점검 발판 · 공구함 · 예비 필터 박스.
        Box(h, new Vector3(0.90f, 0.08f, 0.60f), new Vector3(-1.6f, 0.36f, -1.5f), steel, "StepPlatform");
        foreach (var (dx, dz) in new[] { (-0.38f, -0.24f), (0.38f, -0.24f), (-0.38f, 0.24f), (0.38f, 0.24f) })
            Box(h, new Vector3(0.06f, 0.36f, 0.06f), new Vector3(-1.6f + dx, 0.18f, -1.5f + dz), dark);
        Box(h, new Vector3(0.46f, 0.26f, 0.30f), new Vector3(0.9f, 0.13f, 1.7f), dark, "ToolBox");
        Box(h, new Vector3(0.52f, 0.46f, 0.26f), new Vector3(1.75f, 0.23f, 1.9f),
            Mat(new Color(0.44f, 0.42f, 0.34f), 0.95f), "SpareFilters");

        // 정비구역 표시 — 설비 앞 바닥.
        for (int i = 0; i < 6; i++)
            Box(h, new Vector3(0.22f, 0.012f, 0.09f), new Vector3(0.5f + i * 0.34f, 0.006f, -1.9f),
                Unshaded(i % 2 == 0 ? p.Line : new Color(0.12f, 0.12f, 0.12f)));
    }

    // 정비실 — 매일 손을 대는 **작업장**. 다른 방보다 거칠고 어수선하다.
    private static void Maintenance(Node3D h, Pal p)
    {
        var steel = Mat(new Color(0.30f, 0.28f, 0.24f), 0.7f, 0.45f);
        var dark = Mat(new Color(0.13f, 0.125f, 0.115f), 0.85f);
        var wood = Mat(new Color(0.34f, 0.27f, 0.19f), 0.95f);

        // 벽걸이 공구 보드 — 구멍판에 공구가 걸려 있다.
        Box(h, new Vector3(1.9f, 1.15f, 0.05f), new Vector3(-1.1f, 1.95f, -2.90f), dark, "ToolBoard");
        for (int i = 0; i < 7; i++)
        {
            float x = -1.9f + i * 0.27f;
            float len = 0.18f + (i % 3) * 0.12f;
            Box(h, new Vector3(0.045f, len, 0.04f), new Vector3(x, 2.15f - len * 0.5f, -2.94f), steel);
            Box(h, new Vector3(0.09f, 0.06f, 0.05f), new Vector3(x, 2.15f - len, -2.94f), dark);
        }

        // 벽 선반 — 부품 상자들이 크기별로 올라가 있다.
        foreach (float y in new[] { 1.05f, 1.55f })
        {
            Box(h, new Vector3(0.40f, 0.05f, 2.0f), new Vector3(-2.72f, y, 0.4f), steel, "Shelf");
            for (int i = 0; i < 4; i++)
                Box(h, new Vector3(0.30f, 0.20f, 0.34f), new Vector3(-2.72f, y + 0.12f, -0.4f + i * 0.46f),
                    i % 2 == 0 ? wood : dark);
        }

        // 바이스가 달린 작업대 보강 + 정비용 조명.
        Box(h, new Vector3(0.22f, 0.20f, 0.18f), new Vector3(0.55f, 0.92f, -1.35f), steel, "Vise");
        Box(h, new Vector3(0.16f, 0.08f, 0.14f), new Vector3(0.55f, 1.05f, -1.35f), dark);
        Cyl(h, 0.02f, 0.75f, new Vector3(1.25f, 1.20f, -1.6f), dark, "LampArm");
        var head = Cyl(h, 0.12f, 0.12f, new Vector3(1.25f, 1.56f, -1.45f),
            Glow(new Color(1f, 0.92f, 0.72f), 1.6f), "LampHead");
        head.RotationDegrees = new Vector3(28f, 0f, 0f);

        // 분해 중인 설비 — 부품이 바닥에 흩어져 있다.
        Box(h, new Vector3(0.52f, 0.30f, 0.40f), new Vector3(1.9f, 0.15f, 0.6f), steel, "OpenedUnit");
        Box(h, new Vector3(0.50f, 0.03f, 0.38f), new Vector3(2.05f, 0.32f, 1.05f), dark, "LooseCover");
        foreach (var (x, z, r) in new[] { (1.45f, 1.25f, 0.07f), (2.3f, 1.35f, 0.05f), (1.7f, 0.1f, 0.06f) })
            Cyl(h, r, 0.05f, new Vector3(x, 0.025f, z), steel);
        Cyl(h, 0.055f, 0.70f, new Vector3(2.45f, 0.03f, -0.4f), steel, "PipeScrap");

        // 공구 카트 · 바닥 안전 표식.
        Box(h, new Vector3(0.56f, 0.50f, 0.40f), new Vector3(-1.0f, 0.25f, 1.65f), Mat(p.Accent * 0.6f, 0.8f), "ToolCart");
        Box(h, new Vector3(0.58f, 0.05f, 0.42f), new Vector3(-1.0f, 0.52f, 1.65f), dark);
        for (int i = 0; i < 7; i++)
            Box(h, new Vector3(0.20f, 0.012f, 0.09f), new Vector3(-2.0f + i * 0.32f, 0.006f, -1.05f),
                Unshaded(i % 2 == 0 ? new Color(0.72f, 0.56f, 0.14f) : new Color(0.12f, 0.11f, 0.10f)));
    }

    // 저장고 — 물자가 **분류되어 쌓여 있는** 공간. 상자가 전부 같으면 창고로 안 보인다.
    private static void Storage(Node3D h, Pal p)
    {
        var steel = Mat(new Color(0.30f, 0.29f, 0.26f), 0.6f, 0.5f);
        var dark = Mat(new Color(0.14f, 0.135f, 0.125f), 0.85f);
        Material[] boxes =
        {
            Mat(new Color(0.46f, 0.37f, 0.25f), 0.95f),   // 판지
            Mat(new Color(0.30f, 0.34f, 0.32f), 0.65f),   // 플라스틱 컨테이너
            Mat(new Color(0.38f, 0.36f, 0.30f), 0.5f, 0.4f), // 금속 보관함
            Mat(new Color(0.52f, 0.44f, 0.30f), 0.95f),   // 밝은 판지
        };

        // 벽면 선반 — 3층 철제 랙. 층마다 다른 물건이 올라간다.
        for (int lv = 0; lv < 3; lv++)
        {
            float y = 0.55f + lv * 0.80f;
            Box(h, new Vector3(0.56f, 0.06f, 4.2f), new Vector3(-2.60f, y, 0.2f), steel, $"ShelfL{lv}");
            for (int i = 0; i < 5; i++)
            {
                if ((i + lv) % 5 == 3) continue;             // 군데군데 비워 둔다
                float d = 0.42f + ((i + lv) % 3) * 0.14f;
                float hh = 0.26f + ((i * 2 + lv) % 3) * 0.12f;
                Box(h, new Vector3(0.40f, hh, d), new Vector3(-2.60f, y + 0.03f + hh * 0.5f, -1.6f + i * 0.92f),
                    boxes[(i + lv) % boxes.Length]);
            }
        }
        for (int i = 0; i < 4; i++)
            Box(h, new Vector3(0.60f, 2.4f, 0.08f), new Vector3(-2.60f, 1.2f, -1.8f + i * 1.3f), dark, "RackPost");

        // 구역 번호 — 선반 기둥에 붙은 작은 라벨.
        for (int i = 0; i < 4; i++)
            Box(h, new Vector3(0.02f, 0.12f, 0.18f), new Vector3(-2.90f, 2.30f, -1.8f + i * 1.3f),
                Unshaded(p.Accent));

        // 바닥 적재 — 일부는 반듯하게, 일부는 임시로 놓인 것처럼 비스듬히.
        var stackA = Box(h, new Vector3(0.70f, 0.52f, 0.60f), new Vector3(1.6f, 0.26f, 0.5f), boxes[0], "StackA");
        Box(h, new Vector3(0.64f, 0.42f, 0.56f), new Vector3(1.6f, 0.73f, 0.5f), boxes[2]);
        var tilt = Box(h, new Vector3(0.62f, 0.46f, 0.54f), new Vector3(0.6f, 0.23f, 1.6f), boxes[1], "StackB");
        tilt.RotationDegrees = new Vector3(0f, 17f, 0f);
        Box(h, new Vector3(0.54f, 0.34f, 0.48f), new Vector3(-1.5f, 0.17f, 1.4f), boxes[3], "Loose");
        _ = stackA;

        // 운반대차 — 창고에 한 대는 반드시 있다.
        Box(h, new Vector3(0.86f, 0.07f, 0.56f), new Vector3(-1.9f, 0.26f, -0.4f), steel, "DollyDeck");
        Box(h, new Vector3(0.06f, 0.70f, 0.52f), new Vector3(-2.32f, 0.62f, -0.4f), steel, "DollyHandle");
        foreach (var (dx, dz) in new[] { (-0.34f, -0.22f), (0.34f, -0.22f), (-0.34f, 0.22f), (0.34f, 0.22f) })
            Cyl(h, 0.08f, 0.06f, new Vector3(-1.9f + dx, 0.09f, -0.4f + dz), dark);

        // 적재 라인 · 이동 동선 — 바닥이 '쓰이는' 것처럼 보이게.
        Box(h, new Vector3(0.08f, 0.012f, 3.4f), new Vector3(0.1f, 0.005f, 0.2f), Unshaded(p.Line), "AisleLine");
        Box(h, new Vector3(1.7f, 0.012f, 0.08f), new Vector3(1.6f, 0.005f, -0.35f), Unshaded(p.Line));
        Box(h, new Vector3(1.7f, 0.012f, 0.08f), new Vector3(1.6f, 0.005f, 1.35f), Unshaded(p.Line));
    }

    // 휴게실 — 사람이 있었던 흔적. 가정집이 아니라 **시설 안의 직원 휴게공간**.
    private static void Rest(Node3D h, Pal p)
    {
        var steel = Mat(new Color(0.34f, 0.32f, 0.29f), 0.55f, 0.4f);
        var board = Mat(new Color(0.42f, 0.36f, 0.28f), 0.92f);
        var plastic = Mat(new Color(0.60f, 0.58f, 0.54f), 0.6f);

        // 공용 보관함 — 칸마다 문이 달린 사물함.
        Box(h, new Vector3(1.6f, 1.55f, 0.42f), new Vector3(-1.8f, 0.78f, -2.72f), steel, "Lockers");
        for (int i = 0; i < 4; i++)
        {
            Box(h, new Vector3(0.34f, 1.42f, 0.04f), new Vector3(-2.4f + i * 0.40f, 0.78f, -2.50f), plastic);
            Box(h, new Vector3(0.05f, 0.05f, 0.04f), new Vector3(-2.28f + i * 0.40f, 0.95f, -2.47f), steel);
        }

        // 조리대 — 정수기와 전자레인지 자리.
        Box(h, new Vector3(1.3f, 0.88f, 0.48f), new Vector3(0.9f, 0.44f, -2.70f), board, "Counter");
        Box(h, new Vector3(1.26f, 0.05f, 0.46f), new Vector3(0.9f, 0.90f, -2.70f), plastic);
        Box(h, new Vector3(0.40f, 0.24f, 0.30f), new Vector3(0.52f, 1.05f, -2.66f),
            Mat(new Color(0.46f, 0.44f, 0.41f), 0.5f, 0.3f), "Microwave");
        Box(h, new Vector3(0.24f, 0.14f, 0.02f), new Vector3(0.46f, 1.05f, -2.51f),
            Glow(new Color(0.30f, 0.28f, 0.25f), 0.15f));
        Box(h, new Vector3(0.28f, 0.52f, 0.28f), new Vector3(1.38f, 1.19f, -2.70f), plastic, "WaterUrn");
        Cyl(h, 0.10f, 0.26f, new Vector3(1.38f, 1.56f, -2.70f), Glow(new Color(0.55f, 0.70f, 0.78f), 0.25f), "WaterTank");

        // 벽 선반 + 컵 몇 개.
        Box(h, new Vector3(1.0f, 0.05f, 0.26f), new Vector3(0.9f, 1.62f, -2.80f), board, "WallShelf");
        for (int i = 0; i < 4; i++)
            Cyl(h, 0.045f, 0.11f, new Vector3(0.55f + i * 0.23f, 1.70f, -2.80f),
                Mat(i % 2 == 0 ? new Color(0.72f, 0.70f, 0.66f) : new Color(0.55f, 0.42f, 0.34f), 0.7f));

        // 공지판 + 시계 — 생활 흔적.
        Box(h, new Vector3(0.06f, 0.70f, 1.0f), new Vector3(-2.88f, 1.95f, 0.6f), board, "Notice");
        for (int i = 0; i < 5; i++)
            Box(h, new Vector3(0.02f, 0.18f, 0.14f), new Vector3(-2.92f, 1.72f + (i % 3) * 0.24f, 0.25f + i * 0.21f),
                Mat(new Color(0.78f, 0.76f, 0.70f), 0.95f));
        Cyl(h, 0.14f, 0.05f, new Vector3(-2.90f, 2.55f, -1.2f), plastic, "Clock");

        // 테이블 위 소품 — 컵과 종이컵, 그리고 놓고 간 물건 하나.
        foreach (var (x, z, r, hh) in new[] { (-0.35f, -0.15f, 0.045f, 0.10f), (0.25f, 0.2f, 0.035f, 0.09f),
                                              (0.1f, -0.35f, 0.04f, 0.11f) })
            Cyl(h, r, hh, new Vector3(x, 0.80f + hh * 0.5f, z), plastic);
        Box(h, new Vector3(0.20f, 0.03f, 0.14f), new Vector3(-0.1f, 0.81f, 0.35f),
            Mat(new Color(0.70f, 0.68f, 0.62f), 0.95f), "LeftPaper");

        // 휴지통 · 간이 수납.
        Cyl(h, 0.17f, 0.46f, new Vector3(2.35f, 0.23f, -1.3f), plastic, "Bin");
        Box(h, new Vector3(0.50f, 0.60f, 0.38f), new Vector3(2.3f, 0.30f, 1.3f), board, "SideCabinet");
        Box(h, new Vector3(0.44f, 0.24f, 0.30f), new Vector3(2.3f, 0.72f, 1.3f),
            Mat(new Color(0.48f, 0.40f, 0.30f), 0.95f), "FoodBox");
    }

    // ── 조명 ────────────────────────────────────────────────────────────
    //
    // 기본 방은 천장등 하나뿐이라 전부 평평했다. 여기서 둘을 바꾼다 —
    // 기본등의 색 · 세기를 방마다 다르게, 그리고 방의 성격을 만드는 보조광을 하나 더.
    private static void Lighting(Node3D room, Node3D host, Pal p)
    {
        if (room.GetNodeOrNull<OmniLight3D>("RoomBase/Lights/RoomLight") is { } key)
        {
            key.LightColor = p.LightColor;
            key.LightEnergy = p.LightEnergy;
            key.ShadowEnabled = true;
        }
        // 채움광은 낮춘다 — 이게 세서 그림자가 전부 씻겨 나갔다.
        if (room.GetNodeOrNull<OmniLight3D>("RoomBase/Lights/FillLight") is { } fill)
        {
            fill.LightColor = p.LightColor.Lerp(p.Accent, 0.3f);
            fill.LightEnergy = 0.28f;
        }
        // 천장등 메시도 방 색을 따라간다.
        if (room.GetNodeOrNull<MeshInstance3D>("RoomBase/Shell/CeilingLamp") is { } lamp)
            lamp.MaterialOverride = Glow(p.LightColor, 1.5f);

        // 반대쪽 구석의 보조 조명 — 그림자 방향을 둘로 만들어 입체감을 낸다.
        host.AddChild(new OmniLight3D
        {
            Name = "RoomAccentLight",
            Position = new Vector3(2.0f, 2.25f, 1.9f),
            LightColor = p.Accent,
            LightEnergy = 0.55f,
            OmniRange = 5.5f,
            ShadowEnabled = false,
        });
    }

    // ── 도우미 ──────────────────────────────────────────────────────────

    private static StandardMaterial3D Mat(Color albedo, float rough, float metal = 0f) => new()
    {
        AlbedoColor = albedo, Roughness = rough, Metallic = metal,
    };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
    };

    private static StandardMaterial3D Unshaded(Color c) => new()
    {
        AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    // ── 메시 공유 ────────────────────────────────────────────────────────
    //
    // 걸레받이 · 이음선 · 바닥 구역선처럼 **치수가 똑같은** 조각이 방마다 수십 개씩 붙는다.
    // 조각마다 메시를 새로 만들면 엔진이 하나로 묶어 그릴 수 없어 드로우 콜이 그 수만큼
    // 나간다. 치수를 열쇠로 캐시해 같은 치수는 같은 메시를 쓴다 — 그림은 그대로다.
    private static readonly System.Collections.Generic.Dictionary<(int, int, int), BoxMesh> _boxCache = new();
    private static readonly System.Collections.Generic.Dictionary<(int, int), CylinderMesh> _cylCache = new();

    private static int Q(float v) => Mathf.RoundToInt(v * 10000f);

    private static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, string name = "")
    {
        var key = (Q(size.X), Q(size.Y), Q(size.Z));
        if (!_boxCache.TryGetValue(key, out var mesh)) _boxCache[key] = mesh = new BoxMesh { Size = size };
        var m = new MeshInstance3D
        {
            Name = string.IsNullOrEmpty(name) ? "D" : name,
            Mesh = mesh,
            Position = pos,
            MaterialOverride = mat,
        };
        parent.AddChild(m);
        return m;
    }

    private static MeshInstance3D Cyl(Node3D parent, float radius, float height, Vector3 pos,
        Material mat, string name = "")
    {
        var key = (Q(radius), Q(height));
        if (!_cylCache.TryGetValue(key, out var mesh))
            _cylCache[key] = mesh = new CylinderMesh
            {
                TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 14,
            };
        var m = new MeshInstance3D
        {
            Name = string.IsNullOrEmpty(name) ? "C" : name,
            Mesh = mesh,
            Position = pos,
            MaterialOverride = mat,
        };
        parent.AddChild(m);
        return m;
    }
}
