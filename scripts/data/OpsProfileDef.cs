using Godot;
using Godot.Collections;

namespace NSP.Data;

// 하루치 운영 규칙 묶음.
//
// "이 날 각 작업실은 인원수에 따라 어떻게 동작하는가 / 경고는 어떤 간격으로 뜨는가 /
//  방해자는 언제부터 몇 번 움직이는가 / 목표 복구량은 얼마인가"를 한 파일에 모은다.
// DAY2~5 를 만들 때는 Day 값만 다른 .tres 를 data/ops/ 에 추가하면 된다 — 코드 수정 없음.
[GlobalClass]
public partial class OpsProfileDef : Resource
{
    [Export] public int Day = 1;
    [Export(PropertyHint.MultilineText)] public string Note = "";

    // 작업실별 인원 효과표.
    [Export] public Array<RoomOpsDef> Rooms = new();

    // ── 목표 ───────────────────────────────────────────────────────────
    // 이 날 근무에서 기대하는 코어 복구량(%). 정산 화면이 달성 여부를 보여준다.
    [Export] public float TargetCoreGain = 18f;

    // ── 경고 발생 제어 ─────────────────────────────────────────────────
    // 동시에 떠 있을 수 있는 경고 수.
    [Export] public int MaxConcurrentWarnings = 1;
    // 하루에 뜰 수 있는 경고의 총 상한(정해진 시각의 경고 포함). 0 이면 제한 없음.
    // 빈도를 올려도 한 근무에 경고가 우르르 쏟아지지 않게 막는 안전장치다.
    [Export] public int MaxWarningsPerDay = 0;
    // 경고 하나가 끝난 뒤 다음 경고까지의 최소 간격(초).
    [Export] public float WarningGapSeconds = 14f;
    // 근무 시작 후 이 시간까지는 경고가 뜨지 않는다(초기 배치를 마칠 여유).
    [Export] public float WarningGraceSeconds = 18f;
    // 근무 종료 직전 이 시간부터는 새 경고를 띄우지 않는다(대응할 시간이 없다).
    [Export] public float WarningCutoffSeconds = 16f;

    [Export] public Array<ScheduledWarningDef> ScheduledWarnings = new();

    // ── 방해자 ────────────────────────────────────────────────────────
    // 근무 시작 후 이 시간이 지나야 방해공작을 시도한다.
    [Export] public float SaboteurStartSeconds = 0f;
    // 하루에 성공시킬 수 있는 방해공작 횟수. 0 이하면 제한 없음.
    [Export] public int MaxSabotageActionsPerDay = 0;
    // 이 날 조건부 살인이 일어날 수 있는가. DAY1 은 운영을 배우는 날이라 꺼 둔다.
    [Export] public bool AllowMurder = true;

    // ── 이상 개체(괴물) ───────────────────────────────────────────────
    // 하루에 나타날 수 있는 횟수. 음수면 정하지 않은 것이고, 그때만 Config.GhostMaxPerDay 를 쓴다.
    //
    // 날짜별로 두는 이유: 전역 한도 하나로 두면 방해공작이 많은 날(DAY5 = 4회)에 괴물 3회가
    // 그대로 겹쳐, 배치를 아무리 잘해도 코어가 깎이는 날이 된다. 사고의 총량은 그날의
    // 방해공작 한도와 함께 정해져야 한다.
    [Export] public int GhostMaxPerDay = -1;

    // 첫 등장을 반드시 이 구간(초) 안에 띄운다. 음수면 쓰지 않는다(확률에만 맡긴다).
    // DAY1 은 괴물을 처음 보는 날이라, 운이 나빠 한 번도 안 나오면 그 계통을 통째로 못 배운다.
    [Export] public float GhostFirstAppearFromSeconds = -1f;
    [Export] public float GhostFirstAppearBySeconds = -1f;

    // ── 방해공작 기회 조건(SaboteurPlan) ───────────────────────────────
    // 결번 개체가 노릴 수 있는 중요 시설. 비워 두면 예전 방식(현재 있는 방에서 바로 실행).
    // 결번 개체가 실제로 그 방까지 걸어가야만 그 방에서 사고가 난다.
    [Export] public Godot.Collections.Array<string> SabotageTargetRooms = new();
    // 배치된 자리에 이만큼 자리를 잡아야 기회가 열린다.
    [Export] public float SabotageSettleSeconds = 6f;
    // 준비(전조가 새어 나오는 구간)에 걸리는 시간. 인원·경비에 따라 늘어난다.
    [Export] public float SabotagePrepareMinSeconds = 10f;
    [Export] public float SabotagePrepareMaxSeconds = 16f;
    // 실제 방해공작이 일어나길 바라는 구간(초). 시작 전에는 준비만 한다.
    [Export] public float SabotageWindowStartSeconds = 78f;
    [Export] public float SabotageWindowEndSeconds = 96f;

    // 방 안에 자기 말고 아무도 없으면 손대지 않는다.
    //
    // 혼자 있는 방에서 코어 복구율이 깎이면 로그만 보고도 범인이 확정된다. 그건 추리가
    // 아니라 통보다. 결번 개체는 다른 사람이 같은 방에 있을 때만 움직인다 — 그래서
    // "혼자 두는 배치" 가 관리자의 실제 방어 수단이 된다(대신 방마다 효율이 떨어진다).
    [Export] public bool SabotageNeedsCompany = true;

    // 목표 구간이 끝나 가면 준비가 덜 됐어도 강행하는가.
    // 켜면 그날 방해공작이 사실상 보장된다. DAY1 처럼 "거의 일어나지 않는" 날에는 끈다.
    [Export] public bool SabotageDeadlineRush = true;

    // 그날 결번 개체가 **아예 손댈 생각이 있는가** — 근무 시작 때 한 번만 굴린다.
    //
    // 준비 시간이나 목표 구간을 늘려 빈도를 낮추면, 그 날의 전조가 나오는 시점까지
    // 같이 밀려 추리 재료가 망가진다. 그래서 "할지 말지" 는 여기 숫자 하나로만 정하고,
    // 하기로 한 날의 진행은 지금까지와 똑같이 둔다.
    // 실패한 날에도 준비와 전조는 그대로 흐른다 — 관리자는 무엇이 일어날 뻔했는지 모른다.
    [Export] public float SabotageChancePerDay = 1f;

    // ── 작은 교란(MinorTamper) ─────────────────────────────────────────
    //
    // 실제 방해공작과 다른 행동이다. 설비를 부수지 않고 **신호만 흔든다** —
    // 출력이 잠깐 떨어졌다 돌아오거나, 한 화면에 잡음이 끼거나, 센서가 한 번 튄다.
    // 목적은 피해가 아니라 의심이다: 결번 개체는 하루에 한 번은 반드시 무언가를 한다.
    //
    // 방해공작(SabotageChancePerDay)은 "할지 말지" 를 굴리지만, 교란은 굴리지 않는다.
    // 그래야 휴게시간에 "오늘 아무 일도 없었는데 누구를 의심하지" 가 되지 않는다.
    [Export] public int TamperAttemptsPerDay = 0;
    // 교란이 터질 수 있는 구간(초). 그 전에는 기회가 열려도 기다린다.
    [Export] public float TamperWindowStartSeconds = 55f;
    [Export] public float TamperWindowEndSeconds = 80f;
    // 결번 개체가 손댄 방이 **아닌** 다른 방에 증상이 나타날 확률.
    // 이 값이 0 이면 "이상이 난 방 = 범인이 있던 방" 공식이 성립해 추리가 끝난다.
    [Export(PropertyHint.Range, "0,1,0.05")] public float TamperCrossRoomChance = 0.7f;

    // ── 오늘의 금기 ────────────────────────────────────────────────────
    // 이 날 적용할 금기 id 목록(data/taboos/*.tres). 비어 있으면 그 날은 금기가 없다.
    // DAY 별 차이는 전부 이 데이터에서 나온다 — 코드에 날짜 분기를 넣지 않는다.
    [Export] public Godot.Collections.Array<string> DailyTabooIds = new();

    // ── 정상 직원의 반응 이동(EmployeeBehaviorSystem) ──────────────────
    // 사고가 났을 때 성격에 따라 자리를 뜨는 최대 인원. 로그가 복잡해지지 않게 제한한다.
    [Export] public int MaxReactionMovesPerDay = 2;
    // 첫 번째 행동까지의 시간. 0 이하면 ReactionMoveGapSeconds 를 그대로 쓴다.
    // 따로 두는 이유: 첫 건은 근무 초반에, 두 번째는 중후반에 떨어져야 하루가 고르게 채워진다.
    [Export] public float ReactionMoveFirstSeconds = 0f;
    [Export] public float ReactionMoveGapSeconds = 24f;
    [Export] public float ReactionStaySeconds = 18f;
}
