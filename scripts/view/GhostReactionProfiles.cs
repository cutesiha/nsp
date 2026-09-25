using System.Collections.Generic;
using NSP.Facility;

namespace NSP.View;

// 괴물을 마주친 직원의 CCTV 반응 — 캐릭터마다 "무엇을 어떤 순서로 하는가". **표현 전용이다.**
//
// 스트레스·사고·소멸 판정(GhostHauntSystem)과는 아무 관계가 없다. 여기 있는 값은 화면에서
// 몇 초 동안 어떤 클립을 틀고, 같은 방 안에서 몇 미터를 움직이는가뿐이다.
// 방 사이 이동이 아니다 — CurrentRoomId / AssignedRoomId / IsMoving 은 절대 바뀌지 않는다.
//
// 흐름: ① 첫 반응(Intro) → ② 캐릭터별 행동(Move) → ③ 괴물이 있는 동안 반복(Loop)
//       → ④ 괴물이 사라지면 회복(Recover) → ⑤ 원래 업무로(기존 WorkSpot 처리)
public enum GhostMove
{
    InPlace,     // 제자리(양·여우)
    StepBack,    // 괴물 반대쪽으로 한두 걸음(토끼)
    StepToward,  // 괴물 쪽으로 반 걸음(늑대) — 공격이 아니다, 가까이 붙지도 않는다
    Hide,        // 같은 방 안의 먼 구석으로(고양이·강아지)
}

public sealed class GhostReactionProfile
{
    public string IntroClip = "";
    public float IntroSeconds;
    public double IntroBlend = 0.05;     // 놀람은 짧게 — 기본 0.15초로 부드럽게 섞으면 순간이 죽는다
    public string LoopClip = "";
    public string MoveClip = "";         // 숨으러 가는 동안의 클립(Hide 만)
    public string RecoverClip = "";
    public string SeatedRecoverClip = "";   // 앉은 채로 하는 회복(여우)
    public string FloorClip = "";           // 회복 전에 바닥에 주저앉은 채 떠는 구간(양)
    public float FloorSeconds;
    public float RecoverSeconds;            // FloorSeconds 포함 전체 회복 길이

    public GhostMove Move = GhostMove.InPlace;
    public float MoveAt;                 // 반응 시작 후 이 시점부터 움직인다
    public float MoveSpeed = 1f;         // m/s (Hide)
    public float StepMeters;             // StepBack / StepToward 거리
    public float StepSeconds = 0.4f;     // 한 걸음에 걸리는 시간
    public float KeepFromGhost = 1.3f;   // StepToward 가 괴물에게 이 거리보다 가까이 가지 않는다
    public float BackOffBelow;           // InPlace 인데 괴물이 이보다 가까우면 그만큼 물러난 뒤 반응한다

    public float FaceRate = 6f;          // 괴물 쪽으로 몸을 돌리는 빠르기(0 = 돌리지 않음)
    public float LookWeight;             // 반복 구간에서 고개(Neck)로 괴물을 좇는 정도
    public bool KeepsWorking;            // 여우 — 업무 자리·업무 클립을 그대로 둔다
}

public static class GhostReactionProfiles
{
    // 공포 강도(애니메이션 기준): 양 >>>> 강아지 ≈ 토끼 >> 고양이 >> 늑대 >> 여우
    // 반응의 **종류**가 다르다 — 무너짐 · 비명 · 도망 · 회피 · 대치 · 무시.
    private static readonly Dictionary<CctvEmployeeAction, GhostReactionProfile> _table = new()
    {
        [CctvEmployeeAction.GhostSheepCower] = new()
        {
            IntroClip = "ghost_sheep_drop", IntroSeconds = 0.9f, IntroBlend = 0.05,
            LoopClip = "ghost_sheep_cower_loop",
            // 사라진 뒤에도 바닥에서 떨다가(FloorClip) 힘겹게 일어난다(RecoverClip).
            FloorClip = "ghost_sheep_floor_loop", FloorSeconds = PostGhostRecovery.SheepFloorSeconds,
            RecoverClip = "ghost_sheep_recover", RecoverSeconds = PostGhostRecovery.AnimSeconds("sheep"),
            Move = GhostMove.InPlace, MoveAt = 0.1f, StepSeconds = 0.4f, BackOffBelow = 1.1f, FaceRate = 3f,
        },
        [CctvEmployeeAction.GhostRabbitScream] = new()
        {
            IntroClip = "ghost_rabbit_shock", IntroSeconds = 0.95f, IntroBlend = 0.03,
            LoopClip = "ghost_rabbit_scream_loop",
            RecoverClip = "ghost_rabbit_relief", RecoverSeconds = PostGhostRecovery.AnimSeconds("rabbit"),
            Move = GhostMove.StepBack, MoveAt = 0.3f, StepMeters = 0.35f, StepSeconds = 0.45f,
            FaceRate = 7f, LookWeight = 0.7f,
        },
        [CctvEmployeeAction.GhostCatHide] = new()
        {
            IntroClip = "ghost_cat_startle", IntroSeconds = 0.28f, IntroBlend = 0.06,
            MoveClip = "ghost_cat_retreat", LoopClip = "ghost_cat_hide_loop",
            RecoverClip = "ghost_cat_wipe_recover", RecoverSeconds = PostGhostRecovery.AnimSeconds("cat"),
            Move = GhostMove.Hide, MoveAt = 0.28f, MoveSpeed = 2.7f,
            FaceRate = 9f, LookWeight = 0.8f,
        },
        [CctvEmployeeAction.GhostDogHide] = new()
        {
            IntroClip = "ghost_dog_startle", IntroSeconds = 0.46f, IntroBlend = 0.04,
            MoveClip = "ghost_dog_flee", LoopClip = "ghost_dog_hide_loop",
            RecoverClip = "ghost_dog_breathe_recover", RecoverSeconds = PostGhostRecovery.AnimSeconds("dog"),
            Move = GhostMove.Hide, MoveAt = 0.46f, MoveSpeed = 2.9f,
            FaceRate = 6f, LookWeight = 0.6f,
        },
        [CctvEmployeeAction.GhostWolfConfront] = new()
        {
            IntroClip = "ghost_wolf_startle", IntroSeconds = 0.46f, IntroBlend = 0.08,
            LoopClip = "ghost_wolf_guard_loop",
            RecoverClip = "ghost_wolf_check_recover", RecoverSeconds = PostGhostRecovery.AnimSeconds("wolf"),
            Move = GhostMove.StepToward, MoveAt = 0.18f, StepMeters = 0.45f, StepSeconds = 0.3f,
            KeepFromGhost = 1.3f,
            FaceRate = 10f, LookWeight = 1f,
        },
        [CctvEmployeeAction.GhostFoxWorking] = new()
        {
            // 괴물이 있는 동안은 클립을 바꾸지 않는다 — 하던 업무 클립 위에서 손을 잠깐 멈추고 고개만 돌린다.
            // 사라진 뒤에는 "엥...? 뭐였냐 저건." (앉아 있었으면 앉은 채로).
            RecoverClip = "ghost_fox_shrug_recover", SeatedRecoverClip = "ghost_fox_shrug_recover_seated",
            RecoverSeconds = PostGhostRecovery.AnimSeconds("fox"), KeepsWorking = true,
        },
        [CctvEmployeeAction.GhostStartled] = new()
        {
            LoopClip = "ghost_startle", RecoverSeconds = 0f, FaceRate = 4f,
        },
    };

    // 여우가 처음 알아챌 때: 손이 멈추는 구간과 고개를 돌리고 있는 구간(반응 시작 기준, 초).
    public const float FoxFreezeFrom = 0.03f, FoxFreezeUntil = 0.33f;
    public const float FoxNoticeLookUntil = 0.6f;
    // 그 뒤로는 불규칙하게 가끔 쳐다본다.
    public const float FoxGlanceGapMin = 1.5f, FoxGlanceGapMax = 2.5f;
    public const float FoxGlanceMin = 0.3f, FoxGlanceMax = 0.5f;
    public const float FoxGlanceWorkSpeed = 0.6f;   // 쳐다보는 동안 손이 조금 느려진다

    public static GhostReactionProfile Get(CctvEmployeeAction a) => _table.GetValueOrDefault(a);

    public static bool IsGhostReaction(CctvEmployeeAction a) => _table.ContainsKey(a);

    // 직원 ID → 그 사람의 반응. 성격 수치(AvoidsDanger)로 고르지 않는다 —
    // 위험을 피하는 성향과 "놀라는 몸짓의 크기"는 다른 것이다(토끼는 위험을 안 피하지만 제일 크게 놀란다).
    public static CctvEmployeeAction ActionFor(string employeeId) => employeeId switch
    {
        "sheep" => CctvEmployeeAction.GhostSheepCower,
        "rabbit" => CctvEmployeeAction.GhostRabbitScream,
        "cat" => CctvEmployeeAction.GhostCatHide,
        "dog" => CctvEmployeeAction.GhostDogHide,
        "wolf" => CctvEmployeeAction.GhostWolfConfront,
        "fox" => CctvEmployeeAction.GhostFoxWorking,
        _ => CctvEmployeeAction.GhostStartled,
    };
}
