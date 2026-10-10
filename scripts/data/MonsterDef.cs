using Godot;

namespace NSP.Data;

// 중앙제어실로 접근하는 괴물 한 종.
//
// **애니메이션 이름을 코드에 박지 않는다.** FBX 마다 클립 이름이 다르고(Rigify 는
// "리그이름|클립이름" 으로 들어온다) 없는 클립도 있다. 그래서 역할 → 실제 클립 이름
// 매핑을 전부 여기 데이터로 뺀다. 비워 두면 그 역할은 코드 연출로 대신한다.
//
// 모델 경로도 데이터다 — 사용자가 FBX 를 교체하면 이 줄만 바꾸면 된다.
[GlobalClass]
public partial class MonsterDef : Resource
{
    [Export] public string MonsterId = "";
    [Export] public string DisplayName = "";

    // ── 모델 ────────────────────────────────────────────────────────
    [Export] public string ModelPath = "";
    // 모델을 이 키(m)에 맞춘다. 0 이하면 원본 크기를 그대로 쓴다.
    // FBX 마다 단위가 달라, 실측 높이로 맞추는 쪽이 배율을 적는 것보다 안전하다.
    [Export] public float TargetHeight = 1.8f;
    // 바닥에서 띄우거나 묻는 보정(m). 발이 바닥을 뚫으면 여기서 올린다.
    [Export] public float FloorOffset = 0f;
    // 모델이 기본적으로 바라보는 방향 보정(도). 괴물은 언제나 중앙제어실 쪽을 본다.
    [Export] public float YawOffsetDegrees = 0f;

    // ── 애니메이션 역할 → 실제 클립 이름 ─────────────────────────────
    // 비워 두면 그 역할은 없다(코드가 대신 흔들거나 밀어 준다).
    [Export] public string AnimIdle = "";
    [Export] public string AnimWalk = "";
    [Export] public string AnimRun = "";
    [Export] public string AnimAttackDoor = "";
    [Export] public string AnimJumpscare = "";
    [Export] public string AnimRetreat = "";

    // ── 행동 ────────────────────────────────────────────────────────
    // 복도 입구에서 차폐문 앞까지 걸리는 시간(초). 이 시간이 곧 플레이어의 대응 시간이다.
    [Export] public float ApproachSeconds = 16f;
    // 문 앞에 닿은 뒤 침입을 확정하기까지 주는 마지막 유예(초).
    // **이 값이 0 이면 경고 없는 즉사가 된다.** 반드시 양수로 둔다.
    [Export] public float BreachDelaySeconds = 3.5f;
    // 문이 닫혀 있을 때 두드리는 시간 → 물러나는 시간 → 다음 시도까지의 쿨다운.
    [Export] public float PoundSeconds = 7f;
    [Export] public float RetreatSeconds = 3f;
    [Export] public float CooldownSeconds = 28f;
    // 접근 중 숨을 고르는 멈춤(초). 0 이면 쉬지 않고 걸어온다.
    [Export] public float PauseEverySeconds = 0f;
    [Export] public float PauseLengthSeconds = 1.2f;

    // ── 소리 ────────────────────────────────────────────────────────
    [Export] public string SfxDistant = "";      // 출현 구역에서 들리는 먼 기척
    [Export] public string SfxStep = "";         // 복도를 걸어오는 소리(간격은 속도에 따라)
    [Export] public string SfxPound = "";        // 문 두드림
    [Export] public string SfxScratch = "";      // 금속 긁힘
    [Export] public string SfxBreach = "";       // 문턱을 넘은 순간

    // ── 등장 조건 ───────────────────────────────────────────────────
    [Export] public int FirstDay = 1;            // 이 날부터 나온다
    [Export] public float Weight = 1f;           // 고를 때의 가중치
}
