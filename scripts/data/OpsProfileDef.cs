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
}
