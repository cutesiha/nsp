using Godot;
using NSP.Facility;

namespace NSP.Dialogue;

// CCTV 로 딴짓을 목격한 뒤의 전화 선택지와 대답. **지정된 대사를 원문 그대로 쓴다.**
//
// 기존 대화 시스템(DialogueRepository · LocalDialogueGenerator)을 교체하지 않는다.
// 일반 통화 질문 목록(PhoneCallHud.BuildGeneralQuestions)에 조건부로 한 줄을 더 얹는 것뿐이며,
// 타입라이터 · 말투 색 · 보이스 · 통화 종료 흐름은 전부 그대로다.
//
// 질문이 뜨는 조건은 하나다 — **그 딴짓을 CCTV 로 직접 목격했을 때.**
// 목격하지 않은 딴짓은 질문도 뜨지 않는다(DistractionSystem.LatestUnasked 가 판정한다).
public static class DistractionDialogue
{
    // 선택지 색 — "수상하거나 지적할 행동을 목격했다" 는 표시다. 결번 판정이 아니다.
    // 정상 직원(여우·양·고양이·토끼·늑대)에게도 똑같이 나타난다.
    public static readonly Color Text = new(0.957f, 0.643f, 0.643f);     // #F4A4A4
    public static readonly Color Hover = new(1f, 0.467f, 0.467f);        // #FF7777
    public static readonly Color Border = new(0.60f, 0.26f, 0.26f);
    public static readonly Color Fill = new(0.19f, 0.08f, 0.09f);

    // 일반 딴짓 / 강한 지적이 필요한 딴짓(FOX_02 · WOLF_02)의 문구.
    public const string AskMild = "뭐하고 계셨습니까?";
    public const string AskStrong = "지금 뭐하는 겁니까?";

    public static string Question(DistractionEvent e) =>
        e != null && e.Strong ? AskStrong : AskMild;

    // 목격한 행동에 맞는 대답. {현재 있는 작업실} 은 **행동이 일어난 그 작업실**의 표시 이름이다
    // (전화 시점에 직원이 다른 방으로 옮겨졌어도 과거 행동 장소는 바뀌지 않는다).
    public static string Answer(DistractionEvent e)
    {
        if (e == null) return "…무슨 말씀이신지.";
        string room = string.IsNullOrEmpty(e.RoomName) ? "그" : e.RoomName;
        return e.Kind switch
        {
            DistractionKind.FoxPhone =>
                "저요? 잠깐 폰 좀 봤어요~ 딴 짓 한 줄 아셨나봐요?",
            DistractionKind.FoxFloorVideo =>
                "너무 힘들어서 잠시 누워있었어요~ 뭐, 문제 되나요? 오늘 작업도 열심히 잘 했는데.",
            DistractionKind.SheepDoze =>
                "죄, 죄, 죄송해요! 깜빡 졸아버렸어요... 지, 진짜 죄송합니다...",
            DistractionKind.SheepHide =>
                "저, 저 그냥... 그게... 귀신 나올까봐 조금 무서워서... 죄송해요, 다시 작업 할게요...",
            DistractionKind.CatCall =>
                "왜요? 그냥 잠깐 통화했는데요.",
            DistractionKind.CatWallNap =>
                "너무 졸려서 잠깐 잤어요. 왜요, 불만 있으세요? 작업은 열심히 하니까 신경 끄세요.",
            DistractionKind.RabbitPeek =>
                $"그냥 {room} 기기가 신기해서 봤어요! 어떻게 돌아가는건지 갑자기 궁금해져서... 헤헤.",
            DistractionKind.RabbitSelfie =>
                "앗, 그냥 사진 몇 장 찍었어요. 오늘따라 기분이 좋아서... 에헤헷! 죄송해요!",
            DistractionKind.WolfWallNap =>
                "잠깐 졸았습니다. 죄송합니다. 다시 작업에 착수하겠습니다.",
            DistractionKind.WolfKick =>
                "작업 기기가 평소보다 효율이 안 나와서, 잠시 고치는 중이었습니다. 나중에 확인 부탁드립니다.",
            DistractionKind.DogCall =>
                "아, 들켰네요. 고양이 직원과 잠시 통화했어요. 죄송해요, 다시 일할게요.",
            DistractionKind.DogProbe =>
                "아까 이상한 소리가 나서 좀 살펴보고 있었어요. 작업 다시 할게요. 기분 탓이었나봐요.",
            _ => "…무슨 말씀이신지.",
        };
    }

    // 그 대답만으로 "상대가 누구인지" 까지 말했는가 — 강아지는 먼저 고양이를 말한다.
    public static bool AdmitsPartner(DistractionKind kind) => kind == DistractionKind.DogCall;

    // 고양이는 한 번 더 물어야 상대를 말한다(지시서 §10-1).
    public static bool HasFollowUp(DistractionKind kind) => kind == DistractionKind.CatCall;

    public const string FollowUpAsk = "누구와 통화했습니까?";
    public const string FollowUpReply = "강아지 직원이요. 왜 캐물으시는 거예요?";

    // ── 교차 확인 질문(고양이 ↔ 강아지) ─────────────────────────────────
    //
    // 상대가 관리자에게 통화 사실을 말한 **뒤에만** 뜨고, 그 사건에 대해 한 번만 쓸 수 있다.
    // 실제로 통화하지 않았다면 사건 자체가 없으므로 이 질문도 뜨지 않는다.
    public static string CrossQuestion(string employeeId) => employeeId switch
    {
        "dog" => "고양이 직원과 통화했습니까?",
        "cat" => "강아지 직원과 통화했습니까?",
        _ => "",
    };

    public static string CrossAnswer(string employeeId) => employeeId switch
    {
        "dog" => "어라, 어떻게 아셨어요? 고양이 직원이 말해드렸나..?",
        "cat" => "네, 통화했는데요. 문제 있나요?",
        _ => "",
    };
}
