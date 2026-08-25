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
