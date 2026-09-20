using Godot;

namespace NSP.Data;

// 정해진 시각에 반드시 한 번 뜨는 경고. 배치가 어떻든 근무 중 재배치를 한 번은
// 경험하게 만드는 장치다(DAY1 학습용).
[GlobalClass]
public partial class ScheduledWarningDef : Resource
{
    [Export] public float AtSeconds = 30f;
    [Export] public string RoomId = "";
    // 0 이하면 그 방의 RoomOpsDef 값을 쓴다.
    [Export] public int RequiredStaff = 0;
    [Export] public float ResponseSeconds = 0f;
}
