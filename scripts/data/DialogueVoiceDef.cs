using Godot;

namespace NSP.Data;

// 한 직원이 "어떻게 말하는가"를 수치로 옮긴 것. 무엇을 말하는가(사실)는 여기 없다.
//
// EmployeeDef 의 SpeechStyleLine / SpeechExample 은 사람이 읽는 기준 문서로 남기고,
// 런타임이 실제로 보는 값은 이 Resource 하나다. data/dialogue/voices/*.tres 에서
// 숫자만 바꾸면 말투가 바뀌며 코드는 건드리지 않는다.
//
// 값은 전부 0~1. "이 사람이 그렇게 말할 성향이 얼마나 되는가"이지 확률 그 자체는 아니다
// (발화 형태를 고를 때 가중치로 쓰인다).
[GlobalClass]
public partial class DialogueVoiceDef : Resource
{
    [Export] public string EmployeeId = "";

    // 격식체(…습니다)인가. false 면 해요체(…어요).
    [Export] public bool Formal = true;

    // 한 번에 얼마나 많이 말하는가. 높을수록 덧붙이는 문장이 늘어난다.
    [Export(PropertyHint.Range, "0,1")] public float Talkativeness = 0.4f;
    // 답변에 들어갈 수 있는 문장 수 상한(핵심 포함). 늑대 2, 토끼 3.
    [Export(PropertyHint.Range, "1,4")] public int MaxSentences = 2;

    // 질문 주제를 짧게 되받는가. "이상현상이요?" "그때요?"
    [Export(PropertyHint.Range, "0,1")] public float QuestionEchoChance = 0.25f;
    // 묻지 않은 정보를 스스로 덧붙이는가. "있었으면 바로 말씀드렸을 거예요."
    [Export(PropertyHint.Range, "0,1")] public float VolunteerInfoChance = 0.25f;
    // 질문을 되돌리는가. "관리자님은 어떻게 보시는데요?"
    [Export(PropertyHint.Range, "0,1")] public float BackQuestionChance = 0f;
    // 놀람·당황 같은 반응이 핵심 답변보다 먼저 나오는 강도.
    [Export(PropertyHint.Range, "0,1")] public float ReactionIntensity = 0.2f;
    // 확신을 낮추는 표현을 붙이는가. "확실하진 않아요."
    [Export(PropertyHint.Range, "0,1")] public float HedgeChance = 0.15f;
    // 핵심부터 바로 말하는가. 높을수록 군더더기 없이 짧다.
    [Export(PropertyHint.Range, "0,1")] public float Directness = 0.5f;

    // 장식 기호. 캐릭터 구분을 여기에 기대지 않는다 — 어디까지나 보조다.
    [Export(PropertyHint.Range, "0,1")] public float ExclamationChance = 0f;
    [Export(PropertyHint.Range, "0,1")] public float PauseChance = 0f;
    // 한 답변에 허용하는 느낌표 수 상한.
    [Export(PropertyHint.Range, "0,3")] public int MaxExclamations = 1;

    // ── Dialogue V2 캐릭터 축 ─────────────────────────────────────────
    // 지금의 조립기(KoreanDialogueComposer)는 아직 이 값들을 읽지 않는다. Local Dialogue V2 가
    // 캐릭터를 나누는 기준이다. 캐릭터 차이는 문장부호가 아니라 "무엇을 먼저 말하는가 /
    // 얼마나 확신하는가 / 남을 언급하는가 / 행동을 제안하는가"에서 나와야 한다.

    // 사람이 읽는 한 줄 요약. "겁 많음 / 예민 / 관찰 / 확신 낮춤"
    [Export] public string VoiceAxis = "";
    // 답변 첫머리에 무엇을 두는가.
    [Export] public VoiceLead LeadWith = VoiceLead.Fact;
    // 직접 확인한 사실을 얼마나 단정해서 말하는가(HedgeChance 는 "모르는 것"에 붙는 단서).
    [Export(PropertyHint.Range, "0,1")] public float Confidence = 0.5f;
    // 본 것 / 들은 것 / 추측을 나눠서 말하는가.
    [Export(PropertyHint.Range, "0,1")] public float SeparatesFactFromGuess = 0.5f;
    // 묻지 않아도 다른 직원(위치·상태)을 답변에 끼워 넣는가.
    [Export(PropertyHint.Range, "0,1")] public float MentionOthersChance = 0.2f;
    // "제가 가겠습니다" 같은 행동 제안을 하는가.
    [Export(PropertyHint.Range, "0,1")] public float OfferActionChance = 0.2f;

    // 결번자가 이 직원 행세를 할 때 새어 나오는 미세한 어긋남.
    // 플레이어에게 직접 보여주지 않는다 — V2 가 결번자 답변을 만들 때만 참고한다.
    // 너무 노골적이면 범인 표시가 되므로 "정상 성격의 연장선에서 살짝 틀어진 것"만 적는다.
    [Export] public ImpostorTell ImpostorTells = ImpostorTell.None;
    [Export] public string ImpostorTellLine1 = "";
    [Export] public string ImpostorTellLine2 = "";
    [Export] public string ImpostorTellLine3 = "";
}

// 답변 첫머리에 무엇이 오는가.
public enum VoiceLead
{
    Fact,     // 사실부터 바로 (고양이 · 늑대)
    Reaction, // 감정 반응이 먼저 튀어나온다 (토끼)
    Caveat,   // 자기 판단을 먼저 낮추고 조심스럽게 (양)
    People,   // 상황보다 사람의 상태를 먼저 챙긴다 (강아지)
    Deflect,  // 답은 하되 비껴가거나 되묻는다 (여우)
}

// 결번자가 흉내 낼 때 어긋나는 지점. 여러 개를 겹칠 수 있다.
[System.Flags]
public enum ImpostorTell
{
    None = 0,
    CalmWhereShouldFear = 1 << 0,    // 겁먹어야 할 상황에서 예상보다 침착하다
    LingersNearHazard = 1 << 1,      // 위험 설비 근처에 필요 이상으로 오래 있다
    CertainAboutUnseen = 1 << 2,     // 직접 보지 않은 것을 이상하게 확신한다
    OverAssertive = 1 << 3,          // 평소보다 지나치게 단정한다
    ThinOrRepeatedReasons = 1 << 4,  // 행동 이유가 빈약하거나 같은 말을 되풀이한다
    ConcernWithoutDetail = 1 << 5,   // 걱정은 하는데 구체적인 관찰이 없다
    VagueAboutPeople = 1 << 6,       // 누가 어디 있었는지 물으면 지나치게 일반적이다
}
