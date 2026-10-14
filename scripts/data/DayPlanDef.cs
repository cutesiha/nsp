using Godot;
using Godot.Collections;

namespace NSP.Data;

// 하루치 "오늘의 업무" 묶음.
//
// 운영 규칙(OpsProfileDef: 작업실 인원 효율·경고 확률 등)과 일부러 파일을 나눴다.
// 업무 목표는 DAY1~5 를 전부 미리 정해 둘 수 있지만, 운영 규칙은 아직 DAY1 것만
// 만들어져 있고 DAY2~5 는 DAY1 규칙을 물려받는 상태이기 때문이다.
// 한 파일에 합치면 DAY2 목표를 넣는 순간 DAY2 운영 규칙까지 비어 버린다.
//
// data/objectives/dayN.tres 로 하나씩 둔다. 수치는 전부 여기서 고친다.
[GlobalClass]
public partial class DayPlanDef : Resource
{
    [Export] public int Day = 1;
    [Export(PropertyHint.MultilineText)] public string Note = "";

    // 이 날의 최대 근무시간(초). 0 이하면 Config.DayLengthSeconds 를 쓴다.
    // 필수 업무를 끝내도 자동으로 끝나지 않고, 이 시간이 다 되면 강제로 종료된다.
    [Export] public float MaxShiftSeconds = 0f;

    [Export] public Array<DayObjectiveDef> Objectives = new();
}
