using System.Collections.Generic;
using Godot;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// CCTV 에 비치는 **실제 3D 복도** 한 구간.
//
// 작업실(room_base.tscn)을 빌려 쓰지 않는다. 넓은 네모 방에 '복도' 라고 이름만 붙이면
// 복도로 보이지 않는다 — 길쭉한 원근 · 낮은 천장 · 양쪽 벽이 멀어지며 모이는 소실점이
// 복도를 복도로 만든다. 그래서 여기서 통째로 세운다.
//
// 재질과 소품 어휘는 RoomDressing 과 같은 톤을 쓴다(저채도 지하 산업시설 · 아날로그).
// 네온도, 매끈한 SF 홀로그램도 없다.
//
// 차폐 가능한 구간에는 가운데에 금속 격벽이 선다. 셔터 두 장이 레일을 타고 위아래에서
// 내려오고, 그 열림 정도는 **게임 상태(CorridorSegment.Shut)를 그대로 읽는다** —
// 연출이 판정을 만들지 않는다.
public sealed class CorridorVisual
{
    // 복도 한 칸의 규격(m). 길이는 카메라가 소실점을 볼 수 있을 만큼 길게 잡는다.
    private const float Length = 16f;
    private const float Width = 2.6f;
    private const float Height = 2.9f;
    private const float PanelStep = 2.0f;   // 바닥 패널 · 천장 보 간격

    // 차폐문은 **좌우로** 닫히는 격벽 두 짝이다. 완전히 닫아도 가운데에 세로 관찰
    // 슬릿(SlotHalf×2)이 남는다 — 위아래로 닫는 셔터에 가로 틈을 내면 그 높이에
    // 걸치는 괴물만 보이고, 키 작은 것(아기 괴물 0.78m · 거미 0.62m)은 통째로 가려진다.
    // 세로 슬릿은 키와 상관없이 몸통 한 줄이 그대로 비친다 — 문 앞에 무엇이 붙어
    // 긁고 있는지 **반드시** 눈으로 확인할 수 있어야 한다(지시서 §4 · §6).
    private const float SlotHalf = 0.12f;
    private const float LeafW = Width * 0.5f - SlotHalf;          // 문짝 한 장 폭
    private const float LeafSealX = SlotHalf + LeafW * 0.5f;      // 닫혔을 때 중심 x
    private const float LeafPocketX = Width * 0.5f + LeafW * 0.5f + 0.14f;  // 열려 벽 속에 들어간 자리

    private Node3D _root;
    private Node3D _leafLeft, _leafRight;
    private OmniLight3D _warnLight;
    private StandardMaterial3D _warnMat;
    private Label3D _signLabel;
    private readonly List<OmniLight3D> _lamps = new();

    public Node3D Root => _root;
    public string SegmentId { get; private set; } = "";

    // ── 복도의 방향 ────────────────────────────────────────────────
    //
    // 한쪽 끝은 **중앙제어실**, 반대쪽 끝은 작업실이다. 카메라는 언제나 중앙제어실 쪽
    // 끝에 달려 바깥을 본다 — 그래야 복도를 따라 다가오는 것이 점점 커져 보인다.
    // 카메라가 바깥쪽에 있으면 괴물이 등 뒤에서 생겨 아무것도 보이지 않는다.
    //
    // 차폐문도 같은 이유로 중앙제어실 쪽에 가깝게 선다(지시서 §1 "중앙제어실 바로 앞 복도").
    private float _controlZ = -Length * 0.5f;   // 중앙제어실 쪽 끝
    private float _outerZ = Length * 0.5f;      // 작업실 쪽 끝
    private float _dir = 1f;                    // 중앙제어실 → 바깥 방향(+1 또는 -1)

    // 차폐문이 서는 자리(중앙제어실 끝에서 이만큼 떨어진 곳).
    private const float DoorFromControl = 4.2f;
    public float DoorZ => _controlZ + _dir * DoorFromControl;

    // 카메라 자리 두 개(§4 "카메라 각도 2종 이상").
    //   Far  — 중앙제어실 끝에서 복도 전체를 보는 먼 시야. 접근하는 것이 점점 커진다.
    //   Near — 차폐문 바로 앞의 짧은 시야. 문에 무엇이 붙어 있는지 보인다.
    public Vector3 FarCameraPosition => new(0f, 1.95f, _controlZ + _dir * 0.8f);
    public Vector3 FarCameraLookAt => new(0f, 1.35f, _outerZ);
    // Near 는 슬릿을 비스듬히 보되 너무 비껴서면 틈 속이 가려진다 — 살짝만 옆이다.
    public Vector3 NearCameraPosition => new(Width * 0.17f, 1.76f, DoorZ - _dir * 2.1f);
    public Vector3 NearCameraLookAt => new(0f, 1.30f, DoorZ + _dir * 0.5f);

    // 바깥 끝 ~ 문 사이를 0~1 로 나눈 위치의 z 좌표. 괴물 배치가 이 값을 쓴다.
    //   0 = 복도 바깥 끝(막 들어선 참)   1 = 차폐문 바로 앞
    // 문에 코를 박지 않도록 0.55m 앞에서 멈춘다.
    public float ApproachZ(float t) =>
        Mathf.Lerp(_outerZ, DoorZ + _dir * 0.55f, Mathf.Clamp(t, 0f, 1f));

    // 벽에 바싹 붙어 오는 것(거미)이 쓰는 x. 복도 가운데가 아니라 한쪽 벽 옆이다.
    public float WallHugX => Width * 0.30f;

    // 중앙제어실 쪽을 바라보는 각도(도). 복도 방향에 따라 180도 뒤집힌다.
    public float FacingControlYawDegrees => _dir > 0f ? 180f : 0f;

    // ── 짓기 ────────────────────────────────────────────────────────

    // controlRoomIsA — 이 구간의 RoomA 가 중앙제어실인가. 카메라와 차폐문이 그쪽 끝에 선다.
    public Node3D Build(CorridorSegment seg, string roomAName, string roomBName, bool controlRoomIsA)
    {
        SegmentId = seg.Id;
        _controlZ = controlRoomIsA ? -Length * 0.5f : Length * 0.5f;
        _outerZ = -_controlZ;
        _dir = controlRoomIsA ? 1f : -1f;
        _root = new Node3D { Name = "Corridor_" + seg.Id, Visible = false };

        var concrete = Mat(new Color(0.148f, 0.156f, 0.150f), 0.93f);
        var floorMat = Mat(new Color(0.112f, 0.118f, 0.116f), 0.86f);
        var ceil = Mat(new Color(0.095f, 0.100f, 0.100f), 0.95f);
        var steel = Mat(new Color(0.215f, 0.228f, 0.230f), 0.55f, 0.55f);
        var dark = Mat(new Color(0.072f, 0.078f, 0.080f), 0.85f, 0.2f);
        var trim = Mat(new Color(0.260f, 0.252f, 0.210f), 0.70f, 0.30f);

        float hw = Width * 0.5f, hl = Length * 0.5f;

        // 껍데기 — 바닥 · 천장 · 양쪽 벽.
        Box(_root, new Vector3(Width, 0.12f, Length), new Vector3(0f, -0.06f, 0f), floorMat, "Floor");
        Box(_root, new Vector3(Width, 0.12f, Length), new Vector3(0f, Height + 0.06f, 0f), ceil, "Ceiling");
        Box(_root, new Vector3(0.12f, Height, Length), new Vector3(-hw - 0.06f, Height * 0.5f, 0f), concrete, "WallL");
        Box(_root, new Vector3(0.12f, Height, Length), new Vector3(hw + 0.06f, Height * 0.5f, 0f), concrete, "WallR");
        // 양 끝을 막아 바깥이 비치지 않게 한다(문은 아래에서 따로 단다).
        Box(_root, new Vector3(Width + 0.3f, Height, 0.12f), new Vector3(0f, Height * 0.5f, -hl - 0.06f), concrete, "EndA");
        Box(_root, new Vector3(Width + 0.3f, Height, 0.12f), new Vector3(0f, Height * 0.5f, hl + 0.06f), concrete, "EndB");

        // 바닥 패널 경계 · 걸레받이 — 길이감을 만드는 가장 싼 장치다.
        for (float z = -hl + PanelStep; z < hl; z += PanelStep)
            Box(_root, new Vector3(Width, 0.014f, 0.045f), new Vector3(0f, 0.006f, z), dark);
        Box(_root, new Vector3(0.06f, 0.16f, Length), new Vector3(-hw + 0.03f, 0.08f, 0f), dark, "KickL");
        Box(_root, new Vector3(0.06f, 0.16f, Length), new Vector3(hw - 0.03f, 0.08f, 0f), dark, "KickR");
        // 가운데 안전선 두 줄(노란 도장).
        var line = Mat(new Color(0.42f, 0.36f, 0.13f), 0.9f);
        Box(_root, new Vector3(0.05f, 0.013f, Length - 0.4f), new Vector3(-0.42f, 0.007f, 0f), line);
        Box(_root, new Vector3(0.05f, 0.013f, Length - 0.4f), new Vector3(0.42f, 0.007f, 0f), line);

        // 천장 보 · 케이블 덕트 · 배관.
        for (float z = -hl + PanelStep * 0.5f; z < hl; z += PanelStep)
            Box(_root, new Vector3(Width + 0.1f, 0.10f, 0.09f), new Vector3(0f, Height - 0.06f, z), steel);
        Box(_root, new Vector3(0.34f, 0.24f, Length - 0.3f), new Vector3(-hw + 0.26f, Height - 0.30f, 0f), dark, "Duct");
        Box(_root, new Vector3(0.10f, 0.10f, Length - 0.3f), new Vector3(hw - 0.18f, Height - 0.34f, 0f), steel, "Pipe");
        Box(_root, new Vector3(0.14f, 0.05f, Length - 0.3f), new Vector3(hw - 0.18f, Height - 0.52f, 0f), dark, "Tray");

        // 벽면 허리 레일 — 양쪽 벽이 멀어질수록 모이는 선이 소실점을 만든다.
        Box(_root, new Vector3(0.05f, 0.07f, Length), new Vector3(-hw + 0.04f, 1.12f, 0f), trim, "RailL");
        Box(_root, new Vector3(0.05f, 0.07f, Length), new Vector3(hw - 0.04f, 1.12f, 0f), trim, "RailR");

        BuildLamps();
        BuildEndDoors(seg, roomAName, roomBName, steel, dark);
        BuildZoneSigns(seg, steel);
        if (seg.PermanentSeal) BuildPermanentSeal(seg, steel, dark);
        else if (seg.IsBlockable) BuildShutter(seg, steel, dark, trim);

        return _root;
    }

    // 천장등 — 일정 간격의 형광등. 가운데 하나는 비상등 색으로 둔다(차폐문 쪽).
    private void BuildLamps()
    {
        var lampMat = Glow(new Color(0.78f, 0.82f, 0.74f), 1.1f);
        float hl = Length * 0.5f;
        for (float z = -hl + 1.8f; z < hl; z += 3.4f)
        {
            Box(_root, new Vector3(0.52f, 0.05f, 0.14f), new Vector3(0f, Height - 0.14f, z), lampMat);
            var l = new OmniLight3D
            {
                Position = new Vector3(0f, Height - 0.32f, z),
                LightColor = new Color(0.86f, 0.89f, 0.82f),
                LightEnergy = 1.15f, OmniRange = 6.2f, ShadowEnabled = false,
            };
            _root.AddChild(l);
            _lamps.Add(l);
        }
    }

    // 양 끝 출입문 — 어느 방으로 이어지는지 글자로 알린다.
    private void BuildEndDoors(CorridorSegment seg, string a, string b, Material steel, Material dark)
    {
        float hl = Length * 0.5f;
        for (int i = 0; i < 2; i++)
        {
            float z = i == 0 ? -hl + 0.08f : hl - 0.08f;
            float facing = i == 0 ? 0f : 180f;
            var frame = new Node3D { Position = new Vector3(0f, 0f, z), RotationDegrees = new Vector3(0f, facing, 0f) };
            _root.AddChild(frame);
            Box(frame, new Vector3(1.32f, 2.22f, 0.09f), new Vector3(0f, 1.11f, 0.05f), steel, "DoorFrame");
            Box(frame, new Vector3(1.14f, 2.06f, 0.05f), new Vector3(0f, 1.03f, 0.09f), dark, "DoorLeaf");
            Box(frame, new Vector3(0.10f, 0.10f, 0.05f), new Vector3(0.44f, 1.02f, 0.12f), steel, "Handle");
            // 관찰창 — 어둡게 둔다. 복도 끝이 환하면 깊이가 사라진다.
            Box(frame, new Vector3(0.40f, 0.26f, 0.03f), new Vector3(0f, 1.62f, 0.11f),
                Mat(new Color(0.05f, 0.07f, 0.07f), 0.25f, 0.1f), "DoorWindow");
            frame.AddChild(new Label3D
            {
                Text = i == 0 ? a : b,
                Position = new Vector3(0f, 2.34f, 0.10f),
                PixelSize = 0.0022f, FontSize = 36, OutlineSize = 0,
                Modulate = new Color(0.52f, 0.56f, 0.50f),
                Shaded = false, DoubleSided = false,
            });
        }
    }

    // 벽면 구역번호와 낡은 경고 라벨.
    private void BuildZoneSigns(CorridorSegment seg, Material steel)
    {
        float hw = Width * 0.5f, hl = Length * 0.5f;
        string zone = seg.CctvCameraId.Length > 0 ? seg.CctvCameraId : "H-00";
        for (int i = 0; i < 3; i++)
        {
            float z = -hl + 3.4f + i * 4.6f;
            Box(_root, new Vector3(0.03f, 0.30f, 0.52f), new Vector3(-hw + 0.03f, 1.78f, z), steel);
            _root.AddChild(new Label3D
            {
                Text = $"{zone} · {i + 1:00}",
                Position = new Vector3(-hw + 0.06f, 1.78f, z),
                RotationDegrees = new Vector3(0f, 90f, 0f),
                PixelSize = 0.0019f, FontSize = 34, OutlineSize = 0,
                Modulate = new Color(0.46f, 0.49f, 0.44f),
                Shaded = false, DoubleSided = false,
            });
        }
        // 낡은 경고 라벨 하나.
        _root.AddChild(new Label3D
        {
            Text = "구역 통제\nAUTHORIZED ONLY",
            Position = new Vector3(hw - 0.06f, 1.72f, 1.2f),
            RotationDegrees = new Vector3(0f, -90f, 0f),
            PixelSize = 0.0017f, FontSize = 30, OutlineSize = 0,
            Modulate = new Color(0.52f, 0.44f, 0.26f),
            Shaded = false, DoubleSided = false,
        });
    }

    // 남측 영구 봉쇄 격벽 — **움직이지 않는다.** 레일도 모터도 없이 용접해 붙인 방호판이다.
    // 셔터와 생김새를 일부러 다르게 둔다: 플레이어가 "이건 닫은 문이 아니라 막아 버린 벽"
    // 이라는 것을 한눈에 알아야 조작하려고 시간을 쓰지 않는다.
    private void BuildPermanentSeal(CorridorSegment seg, Material steel, Material dark)
    {
        float hw = Width * 0.5f;
        var wall = new Node3D { Name = "PermanentSeal", Position = new Vector3(0f, 0f, DoorZ) };
        _root.AddChild(wall);

        var plate = Mat(new Color(0.165f, 0.105f, 0.098f), 0.80f, 0.35f);
        var weld = Mat(new Color(0.30f, 0.15f, 0.12f), 0.62f, 0.45f);
        var hazard = Mat(new Color(0.46f, 0.16f, 0.13f), 0.80f);

        // 통로를 통째로 메운 두꺼운 방호판.
        Box(wall, new Vector3(Width + 0.2f, Height, 0.26f), new Vector3(0f, Height * 0.5f, 0f), plate, "Plate");
        // 가로 보강재 넷.
        for (int i = 0; i < 4; i++)
            Box(wall, new Vector3(Width + 0.24f, 0.13f, 0.32f),
                new Vector3(0f, 0.42f + i * 0.72f, -0.03f * _dir), weld);
        // 대각 가새 — ⨯ 자로 가로지른다.
        for (int s = -1; s <= 1; s += 2)
        {
            var brace = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.14f, Height * 1.22f, 0.10f) },
                Position = new Vector3(0f, Height * 0.5f, -0.16f * _dir),
                RotationDegrees = new Vector3(0f, 0f, s * 42f),
                MaterialOverride = weld,
            };
            wall.AddChild(brace);
        }
        // 테두리 경고 띠.
        Box(wall, new Vector3(Width + 0.22f, 0.09f, 0.30f), new Vector3(0f, Height - 0.07f, -0.02f * _dir), hazard);
        Box(wall, new Vector3(Width + 0.22f, 0.09f, 0.30f), new Vector3(0f, 0.09f, -0.02f * _dir), hazard);
        // 옆 기둥 — 벽에 박아 넣은 고정부.
        Box(wall, new Vector3(0.18f, Height, 0.34f), new Vector3(-hw - 0.05f, Height * 0.5f, 0f), steel);
        Box(wall, new Vector3(0.18f, Height, 0.34f), new Vector3(hw + 0.05f, Height * 0.5f, 0f), steel);
        // 쓰다 만 작업등 하나 — 꺼져 있다.
        Box(wall, new Vector3(0.14f, 0.14f, 0.14f), new Vector3(hw - 0.1f, Height - 0.55f, -0.22f * _dir), dark);

        wall.AddChild(new Label3D
        {
            Text = "PERMANENT SEAL\n영구 봉쇄",
            Position = new Vector3(0f, 1.62f, -0.19f * _dir),
            RotationDegrees = new Vector3(0f, _dir > 0f ? 180f : 0f, 0f),
            PixelSize = 0.0026f, FontSize = 40, OutlineSize = 0,
            Modulate = new Color(0.84f, 0.30f, 0.24f),
            Shaded = false, DoubleSided = false,
        });
    }

    // 차폐문 — 레일 · 모터 · 경고등 · 위아래 셔터 두 장.
    private void BuildShutter(CorridorSegment seg, Material steel, Material dark, Material trim)
    {
        float hw = Width * 0.5f;
        // 문은 복도 한가운데가 아니라 **중앙제어실 쪽 끝 가까이** 선다.
        // 작업실 출입문이 아니라 관리자 방 앞의 격벽이기 때문이다(§1).
        var housing = new Node3D { Name = "Barrier", Position = new Vector3(0f, 0f, DoorZ) };
        _root.AddChild(housing);

        // 문틀과 구동 레일.
        Box(housing, new Vector3(Width + 0.26f, 0.30f, 0.34f), new Vector3(0f, Height - 0.15f, 0f), steel, "Head");
        Box(housing, new Vector3(0.22f, Height, 0.34f), new Vector3(-hw - 0.08f, Height * 0.5f, 0f), steel, "RailL");
        Box(housing, new Vector3(0.22f, Height, 0.34f), new Vector3(hw + 0.08f, Height * 0.5f, 0f), steel, "RailR");
        Box(housing, new Vector3(Width + 0.26f, 0.10f, 0.34f), new Vector3(0f, 0.05f, 0f), steel, "Sill");
        // 구동 모터 두 개.
        Box(housing, new Vector3(0.30f, 0.26f, 0.26f), new Vector3(-hw - 0.08f, Height - 0.45f, 0.22f), dark, "MotorL");
        Box(housing, new Vector3(0.30f, 0.26f, 0.26f), new Vector3(hw + 0.08f, Height - 0.45f, 0.22f), dark, "MotorR");

        // 격벽 두 짝. 양쪽 벽 속에서 나와 가운데에서 만나되 세로 슬릿을 남긴다.
        float plateH = Height - 0.20f;
        for (int s = -1; s <= 1; s += 2)
        {
            var leaf = new Node3D
            {
                Name = s < 0 ? "LeafLeft" : "LeafRight",
                Position = new Vector3(s * LeafPocketX, 0f, 0f),
            };
            housing.AddChild(leaf);
            Box(leaf, new Vector3(LeafW, plateH, 0.14f), new Vector3(0f, Height * 0.5f, 0f), steel, "Plate");
            // 세로 보강 리브 — 가까이서 보면 금속판 두 장이 겹쳐 있는 게 보인다.
            for (int i = 0; i < 4; i++)
                Box(leaf, new Vector3(LeafW - 0.1f, 0.05f, 0.17f),
                    new Vector3(0f, 0.42f + i * 0.66f, 0f), dark);
            if (s < 0) _leafLeft = leaf; else _leafRight = leaf;
        }

        // 슬릿 테두리 — 문짝이 만나도 가운데에 세로 틈이 **실제로** 남는다(SlotHalf 참조).
        // 완전히 막아 버리면 "문 앞에서 긁고 있다" 를 확인할 방법이 사라진다.
        for (int s = -1; s <= 1; s += 2)
            Box(housing, new Vector3(0.035f, Height, 0.19f),
                new Vector3(s * (SlotHalf + 0.018f), Height * 0.5f, -0.02f * _dir), trim);

        // 경고등 — 구동 중에 돈다.
        _warnMat = Glow(new Color(0.85f, 0.42f, 0.16f), 0f);
        Box(housing, new Vector3(0.16f, 0.16f, 0.16f), new Vector3(hw - 0.02f, Height - 0.52f, -0.26f), _warnMat, "WarnLamp");
        _warnLight = new OmniLight3D
        {
            Position = new Vector3(hw - 0.02f, Height - 0.52f, -0.26f),
            LightColor = new Color(0.95f, 0.46f, 0.18f),
            LightEnergy = 0f, OmniRange = 3.6f, ShadowEnabled = false,
        };
        housing.AddChild(_warnLight);

        // 문 옆 명판.
        _signLabel = new Label3D
        {
            Text = seg.DisplayName + "\nOPEN",
            Position = new Vector3(-hw + 0.04f, 1.94f, 0.55f),
            RotationDegrees = new Vector3(0f, 90f, 0f),
            PixelSize = 0.0019f, FontSize = 32, OutlineSize = 0,
            Modulate = new Color(0.60f, 0.66f, 0.60f),
            Shaded = false, DoubleSided = false,
        };
        housing.AddChild(_signLabel);
        Box(housing, new Vector3(0.03f, 0.34f, 0.80f), new Vector3(-hw + 0.02f, 1.90f, 0.55f), trim);
    }

    // ── 매 프레임 ───────────────────────────────────────────────────

    // 게임 상태를 그대로 읽어 문짝 위치 · 경고등 · 명판을 맞춘다. 반대 방향은 없다.
    public void Sync(CorridorSegment seg, float time)
    {
        if (seg == null || _leafLeft == null) return;
        float shut = Mathf.Clamp(seg.Shut, 0f, 1f);
        float x = Mathf.Lerp(LeafPocketX, LeafSealX, shut);
        _leafLeft.Position = new Vector3(-x, 0f, 0f);
        _leafRight.Position = new Vector3(x, 0f, 0f);

        // 구동 중에만 경고등이 돈다. 닫힌 뒤에는 약하게 켜진 채로 남는다.
        float energy = seg.Moving ? 1.4f + Mathf.Sin(time * 11f) * 1.0f : seg.Sealed ? 0.55f : 0f;
        if (_warnLight != null) _warnLight.LightEnergy = Mathf.Max(0f, energy);
        if (_warnMat != null) _warnMat.EmissionEnergyMultiplier = Mathf.Max(0f, energy * 1.6f);

        if (_signLabel == null) return;
        _signLabel.Text = seg.DisplayName + "\n" + seg.StatusText;
        _signLabel.Modulate = seg.Sealed ? new Color(0.88f, 0.34f, 0.26f)
            : seg.Moving ? new Color(0.92f, 0.72f, 0.34f)
            : new Color(0.60f, 0.66f, 0.60f);
    }

    // 전력이 나가면 복도도 어두워진다(비상등만 남는 느낌).
    public void SetLit(bool lit)
    {
        foreach (var l in _lamps) l.LightEnergy = lit ? 1.15f : 0.07f;
    }

    // ── 재료 ────────────────────────────────────────────────────────

    private static StandardMaterial3D Mat(Color albedo, float rough, float metal = 0f) => new()
    {
        AlbedoColor = albedo, Roughness = rough, Metallic = metal,
    };

    private static StandardMaterial3D Glow(Color c, float energy) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy,
    };

    private static void Box(Node3D parent, Vector3 size, Vector3 pos, Material mat, string name = "")
    {
        parent.AddChild(new MeshInstance3D
        {
            Name = string.IsNullOrEmpty(name) ? "D" : name,
            Mesh = new BoxMesh { Size = size },
            Position = pos,
            MaterialOverride = mat,
        });
    }
}
