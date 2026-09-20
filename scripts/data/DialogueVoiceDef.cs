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
    // 답변에 들어갈 수 있는 문장 수 상한(핵심 포함). 까마귀 1, 토끼 3.
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
}
