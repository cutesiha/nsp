using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 딴짓 12종이 CCTV 에서 "어떤 클립을 어떤 순서로" 보이는가. **표현 전용이다.**
//
// 발생·진행·목격 판정은 전부 DistractionSystem(시뮬레이션)이 쥐고 있다. 여기 있는 값은
// 화면에서 몇 초 동안 어떤 클립을 틀고, 방 안에서 어디로 걸어가고, 고개를 얼마나 돌리는가뿐이다.
// 방 사이 이동이 아니다 — CurrentRoomId / AssignedRoomId / IsMoving 은 읽지도 쓰지도 않는다.
//
// 흐름: ① 자리에서 준비(Pre) → ② 자리로 걸어감 → ③ 자세 잡기(Enter) → ④ 반복(Loop)
//       → ⑤ 적발(Notice, 개인별) → ⑥ 정리(Recover) → ⑦ 원래 자리로(기존 WorkSpot 처리)
public sealed class DistractionProfile
{
    // 하던 자리에서 먼저 하는 동작(휴대폰 꺼내기 · 주변 살피기 · 귀 기울이기). 없으면 비워 둔다.
    public string PreClip = "";
    public float PreSeconds;

    // 목적지에서 자세를 잡는 동작(눕기 · 웅크리기 · 벽에 기대기 · 몸을 숙이기).
    public string EnterClip = "";
    public float EnterSeconds;

    public string LoopClip = "";
    public string NoticeClip = "";
    public string RecoverClip = "";

    // 휴대폰 소품 — 어느 손에 들고, 언제 나타나고 언제 사라지는가.
    public string PhoneHand = "";          // "" 없음 · "L" · "R"
    public float PhoneShowAt;              // Pre(없으면 Enter) 클립 기준 초
    public float PhoneHideAt;              // Recover 클립 기준 초

    // 고개(Neck)로 CCTV 카메라를 보는 정도. 클립은 Neck 을 쓰지 않으므로 여기서 코드가 돌린다.
    // 딴짓을 하는 동안에는 카메라를 보지 않는다(보는 순간이 곧 적발이다) — 적발 반응에만 쓴다.
    public float LookInNotice = 1f;        // 적발 반응 중
    public float LookRate = 9f;            // 고개가 따라가는 빠르기(rad/s 계수)
    public float NoticeLookDelay;          // 적발 후 이만큼 지난 뒤 고개가 돌아간다(시선이 몸보다 먼저)
    // 적발 뒤 몸까지 카메라 쪽으로 조금 돌리는가(0 = 돌리지 않는다).
    public float BodyTurnToCamera;
    public float BodyTurnRate = 2.5f;

    // 복귀 직후 작업 모션 재생 속도(양만 1.6배로 허둥댄다 — 실제 생산량은 건드리지 않는다).
    public float WorkSpeedAfter = 1f;
    public float WorkSpeedSeconds;

    // 반복 클립 안에서 "실제로 닿는 순간" 에 맞춘 소리. 보고 있는 화면에서만 난다.
    public (float At, string Key, float Db, float Pitch)[] LoopSfx = System.Array.Empty<(float, string, float, float)>();
    // Pre / Enter 클립의 소리(옷 스치는 소리 등).
    public (float At, string Key, float Db, float Pitch)[] EnterSfx = System.Array.Empty<(float, string, float, float)>();

    // 설비를 만지는 딴짓 — 이 클립의 손 거리(DistractionClipReach)로 설 자리를 정한다.
    public string TouchClip = "";

    // 자리에서 하는 딴짓일 때 몸을 CCTV 쪽으로 얼마나 트는가(0 = 설비를 그대로 보고 선다).
    //
    // 작업 자리는 거의 다 벽 쪽 설비를 향한다 — 그대로 두면 카메라에는 **등만** 보여서
    // 손에 든 휴대폰도, 고개를 숙인 것도 읽히지 않는다. 일을 멈추고 딴짓을 시작할 때
    // 몸을 비스듬히 트는 것은 자연스럽기도 하다. 설비를 만져야 하는 딴짓은 0 으로 둔다.
    public float FaceCameraBias;
}

public static class DistractionProfiles
{
    // 검수용 — 프리뷰에서 "지금 보고 있는 중" 으로 강제한다. 게임 플레이에서는 쓰지 않는다.
    public static bool ForceFeedLive;

    private static readonly Dictionary<DistractionKind, DistractionProfile> _table = new()
    {
        // ── 여우 ──
        [DistractionKind.FoxPhone] = new()
        {
            FaceCameraBias = 0.55f,
            EnterClip = "dis_fox_phone_enter", EnterSeconds = 1.75f,
            LoopClip = "dis_fox_phone_loop",
            NoticeClip = "dis_fox_phone_notice", RecoverClip = "dis_fox_phone_recover",
            PhoneHand = "R", PhoneShowAt = 0.72f, PhoneHideAt = 0.78f,
            // 능청스럽게 — 고개만 짧게 돌리고 몸은 그대로.
            LookInNotice = 0.95f, LookRate = 10f, NoticeLookDelay = 0.14f,
            EnterSfx = new[] { (0.62f, "cloth_rustle", -24f, 1.15f) },
        },
        [DistractionKind.FoxFloorVideo] = new()
        {
            PreClip = "dis_fox_phone_enter", PreSeconds = 1.75f,
            EnterClip = "dis_fox_floor_enter", EnterSeconds = 3.0f,
            LoopClip = "dis_fox_floor_loop",
            NoticeClip = "dis_fox_floor_notice", RecoverClip = "dis_fox_floor_recover",
            PhoneHand = "R", PhoneShowAt = 0.72f, PhoneHideAt = 2.95f,
            LookInNotice = 1f, LookRate = 12f, NoticeLookDelay = 0.1f,
            EnterSfx = new[] { (1.05f, "cloth_rustle", -22f, 0.95f), (2.6f, "cloth_rustle", -20f, 0.8f) },
        },

        // ── 양 ──
        [DistractionKind.SheepDoze] = new()
        {
            FaceCameraBias = 0.45f,
            EnterClip = "dis_sheep_doze_enter", EnterSeconds = 2.4f,
            LoopClip = "dis_sheep_doze_loop",
            NoticeClip = "dis_sheep_doze_notice", RecoverClip = "dis_sheep_doze_recover",
            // 크게 화들짝 — 고개가 아주 빠르게 돌아간다.
            LookInNotice = 1f, LookRate = 20f, NoticeLookDelay = 0.05f,
            BodyTurnToCamera = 0.35f, BodyTurnRate = 3.5f,
            // 허둥대며 빠르게 일한다(재생 속도만 — 생산량은 그대로).
            WorkSpeedAfter = 1.65f, WorkSpeedSeconds = 5.5f,
        },
        [DistractionKind.SheepHide] = new()
        {
            PreClip = "dis_sheep_hide_scan", PreSeconds = 2.6f,
            EnterClip = "dis_sheep_hide_crouch", EnterSeconds = 1.5f,
            LoopClip = "dis_sheep_hide_loop",
            NoticeClip = "dis_sheep_hide_notice", RecoverClip = "dis_sheep_hide_recover",
            LookInNotice = 1f, LookRate = 16f, NoticeLookDelay = 0.06f,
            WorkSpeedAfter = 1.2f, WorkSpeedSeconds = 3f,
            EnterSfx = new[] { (0.5f, "cloth_rustle", -22f, 1.05f) },
        },

        // ── 고양이 ──
        [DistractionKind.CatCall] = new()
        {
            FaceCameraBias = 0.5f,
            EnterClip = "dis_cat_call_enter", EnterSeconds = 1.9f,
            LoopClip = "dis_cat_call_loop",
            NoticeClip = "dis_cat_call_notice", RecoverClip = "dis_cat_call_recover",
            PhoneHand = "R", PhoneShowAt = 0.62f, PhoneHideAt = 1.62f,
            // 태연하게 — 천천히 돌아보고 한참 쳐다본다.
            LookInNotice = 0.85f, LookRate = 5.5f, NoticeLookDelay = 0.2f,
            EnterSfx = new[] { (0.95f, "click", -26f, 1.6f) },
        },
        [DistractionKind.CatWallNap] = new()
        {
            EnterClip = "dis_cat_wall_enter", EnterSeconds = 1.8f,
            LoopClip = "dis_cat_wall_loop",
            NoticeClip = "dis_cat_wall_notice", RecoverClip = "dis_cat_wall_recover",
            // 짜증스럽게 천천히.
            LookInNotice = 0.8f, LookRate = 4.5f, NoticeLookDelay = 0.3f,
        },

        // ── 토끼 ──
        [DistractionKind.RabbitPeek] = new()
        {
            EnterClip = "dis_rabbit_peek_enter", EnterSeconds = 1.6f,
            LoopClip = "dis_rabbit_peek_loop",
            NoticeClip = "dis_rabbit_peek_notice", RecoverClip = "dis_rabbit_peek_recover",
            LookInNotice = 1f, LookRate = 14f, NoticeLookDelay = 0.05f,
            TouchClip = "dis_rabbit_peek_loop",
            LoopSfx = new[]
            {
                (0.2f, "pipe_knock", -24f, 1.35f), (0.52f, "pipe_knock", -25f, 1.4f),
                (0.84f, "pipe_knock", -24f, 1.3f),
            },
        },
        [DistractionKind.RabbitSelfie] = new()
        {
            FaceCameraBias = 0.7f,
            EnterClip = "dis_rabbit_selfie_enter", EnterSeconds = 1.7f,
            LoopClip = "dis_rabbit_selfie_loop",
            NoticeClip = "dis_rabbit_selfie_notice", RecoverClip = "dis_rabbit_selfie_recover",
            PhoneHand = "R", PhoneShowAt = 0.48f, PhoneHideAt = 0.95f,
            LookInNotice = 1f, LookRate = 13f, NoticeLookDelay = 0.05f,
            // 셔터 — 촬영 버튼을 누르는 순간에 맞춘다.
            LoopSfx = new[] { (0.87f, "click", -14f, 1.75f), (2.37f, "click", -15f, 1.7f) },
        },

        // ── 늑대 ──
        [DistractionKind.WolfWallNap] = new()
        {
            EnterClip = "dis_wolf_wall_enter", EnterSeconds = 2.1f,
            LoopClip = "dis_wolf_wall_loop",
            NoticeClip = "dis_wolf_wall_notice", RecoverClip = "dis_wolf_wall_recover",
            // 침착하게 — 잠깐 보고 끄덕인다.
            LookInNotice = 0.75f, LookRate = 6f, NoticeLookDelay = 0.12f,
            WorkSpeedAfter = 0.8f, WorkSpeedSeconds = 3.5f,   // 평소보다 조금 느리게 시작한다
        },
        [DistractionKind.WolfKick] = new()
        {
            EnterClip = "dis_wolf_kick_enter", EnterSeconds = 1.7f,
            LoopClip = "dis_wolf_kick_loop",
            NoticeClip = "dis_wolf_kick_notice", RecoverClip = "dis_wolf_kick_recover",
            LookInNotice = 1f, LookRate = 4.5f, NoticeLookDelay = 0.18f,
            TouchClip = "dis_wolf_kick_loop",
            // 발차기와 손 타격 — 박자가 서로 다르다.
            LoopSfx = new[]
            {
                (0.42f, "impact_blunt", -9f, 0.68f),
                (1.45f, "pipe_knock", -8f, 0.92f),
                (2.12f, "pipe_knock", -10f, 1.04f),
            },
        },

        // ── 강아지 ──
        [DistractionKind.DogCall] = new()
        {
            FaceCameraBias = 0.5f,
            EnterClip = "dis_dog_call_enter", EnterSeconds = 1.8f,
            LoopClip = "dis_dog_call_loop",
            NoticeClip = "dis_dog_call_notice", RecoverClip = "dis_dog_call_recover",
            PhoneHand = "R", PhoneShowAt = 0.58f, PhoneHideAt = 1.36f,
            LookInNotice = 0.9f, LookRate = 11f, NoticeLookDelay = 0.08f,
            EnterSfx = new[] { (0.9f, "click", -26f, 1.55f) },
        },
        [DistractionKind.DogProbe] = new()
        {
            PreClip = "dis_dog_probe_listen", PreSeconds = 1.8f,
            EnterClip = "dis_dog_probe_enter", EnterSeconds = 1.5f,
            LoopClip = "dis_dog_probe_loop",
            NoticeClip = "dis_dog_probe_notice", RecoverClip = "dis_dog_probe_recover",
            LookInNotice = 0.7f, LookRate = 10f, NoticeLookDelay = 0.08f,
            TouchClip = "dis_dog_probe_loop",
            LoopSfx = new[]
            {
                (0.18f, "pipe_knock", -19f, 1.1f), (0.52f, "pipe_knock", -20f, 1.16f),
                (3.2f, "pipe_knock", -23f, 1.22f),
            },
        },
    };

    public static DistractionProfile Get(DistractionKind kind) => _table.GetValueOrDefault(kind);

    // CCTV 카메라의 방 기준 위치 — 직원이 "카메라를 쳐다보는" 방향의 기준점.
    // 실제 값은 FacilityCctvWorld 가 들고 있다(방 씬은 모두 원점에 겹쳐 있으므로 방 좌표와 같다).
    public static Vector3 CameraPoint =>
        FacilityCctvWorld.Instance?.CameraPosition ?? new Vector3(3.5f, 3.05f, 3.5f);
}
