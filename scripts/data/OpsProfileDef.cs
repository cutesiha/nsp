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

    // ── 방해공작 기회 조건(SaboteurPlan) ───────────────────────────────
    // 결번자가 노릴 수 있는 중요 시설. 비워 두면 예전 방식(현재 있는 방에서 바로 실행).
    // 결번자가 실제로 그 방까지 걸어가야만 그 방에서 사고가 난다.
    [Export] public Godot.Collections.Array<string> SabotageTargetRooms = new();
    // SaboteurStartSeconds 보다 이만큼 먼저 자리를 뜨기 시작한다(걸어갈 시간).
    [Export] public float SabotageApproachLeadSeconds = 14f;
    // 대상 작업실에서 이만큼 머문 뒤에야 손을 댄다 — 체류 자체가 전조가 된다.
    [Export] public float SabotageDwellSeconds = 10f;
    // 같은 방에 이 인원보다 많으면 시도하지 않는다.
    [Export] public int SabotageMaxOthersInRoom = 1;
    // 저지른 뒤 원래 자리로 돌아가기까지의 시간(초).
    [Export] public float SaboteurReturnSeconds = 8f;

    // ── 정상 직원의 반응 이동(EmployeeBehaviorSystem) ──────────────────
    // 사고가 났을 때 성격에 따라 자리를 뜨는 최대 인원. 로그가 복잡해지지 않게 제한한다.
    [Export] public int MaxReactionMovesPerDay = 2;
    [Export] public float ReactionMoveGapSeconds = 24f;
    [Export] public float ReactionStaySeconds = 18f;
}
