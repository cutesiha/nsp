using Godot;

namespace NSP.Data;

[GlobalClass]
public partial class EmployeeDef : Resource
{
    [Export] public string EmployeeId = "";
    [Export] public string Codename = "";

    [Export] public int Tech = 1;
    [Export] public int Courage = 1;
    [Export] public int Observation = 1;

    [Export] public string PersonalityLine1 = "";
    [Export] public string PersonalityLine2 = "";
    [Export] public string PersonalityLine3 = "";

    [Export] public string Trait = "";

    [Export] public string SpeechStyleLine1 = "";
    [Export] public string SpeechStyleLine2 = "";
    [Export] public string SpeechStyleLine3 = "";

    [Export] public string SpeechExample1 = "";
    [Export] public string SpeechExample2 = "";
    [Export] public string SpeechExample3 = "";

    // 사고·비명·금기 경고 등 상황에서 이 직원이 어떻게 행동하는지 (AI 판단 참고용).
    [Export] public string BehaviorLine1 = "";
    [Export] public string BehaviorLine2 = "";
    [Export] public string BehaviorLine3 = "";

    // 관리자가 전화를 받을 때까지 이 직원이 기다려주는 시간(초). 성격에 따라 다르며
    // 코드에 하드코딩하지 않고 캐릭터 데이터에서 관리한다.
    [Export] public float IncomingCallPatienceSeconds = 5f;

    [Export] public string StartRoomId = "";
    [Export] public Color IconColor = new Color(0.7f, 0.7f, 0.7f);
    [Export] public Texture2D StandingImage;
    [Export] public Texture2D FacePortrait;

    public int GetStat(StatType stat) => stat switch
    {
        StatType.Tech => Tech,
        StatType.Courage => Courage,
        StatType.Observation => Observation,
        _ => 0,
    };
}
