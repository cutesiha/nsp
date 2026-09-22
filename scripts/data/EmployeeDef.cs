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

    // 이 직원이 근무에 나오는 날. DAY0(교육)에는 0 인 직원만 나온다.
    // 데이터만 바꾸면 되므로 튜토리얼 인원 구성은 코드를 고치지 않고 조정할 수 있다.
    [Export] public int UnlockDay = 1;

    [Export] public string StartRoomId = "";
    [Export] public Color IconColor = new Color(0.7f, 0.7f, 0.7f);
    [Export] public Texture2D StandingImage;
    [Export] public Texture2D FacePortrait;

    // 휴게시간 심문 스탠딩 일러의 중앙 발광(nsp_crt_glow_standing.gdshader 의 glow_amt / glow_center).
    // 원화마다 밝기가 달라 흰 옷 캐릭터는 같은 세기에서도 빛이 과하게 번진다 — 캐릭터별로 낮춘다.
    // glow_center 는 원화 UV 기준(0=위, 1=아래). y 를 줄이면 빛이 위로, 늘리면 아래로 간다.
    [Export(PropertyHint.Range, "0,3,0.05")] public float StandingGlowAmt = 0.9f;
    [Export] public Vector2 StandingGlowCenter = new(0.5f, 0.46f);
    // 휴게 CCTV 스탠딩을 위로 올리는 양(px). 키 작은 직원의 얼굴이 화면 가운데 쪽에 오게.
    [Export] public float InterviewPortraitLift = 0f;

    public int GetStat(StatType stat) => stat switch
    {
        StatType.Tech => Tech,
        StatType.Courage => Courage,
        StatType.Observation => Observation,
        _ => 0,
    };
}
