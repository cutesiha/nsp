using System.Collections.Generic;

namespace NSP.Dialogue;

// 한 답변을 "어떤 모양으로 말할 것인가".
//
// 예전에는 [반응][핵심][보조][감정][끝맺음] 다섯 조각을 각각 확률로 굴려 붙였다.
// 조각 하나하나는 자연스러워도 이어 붙인 결과가 사람 말이 아니게 되는 원인이 그것이었다.
// 이제는 먼저 발화 형태 하나를 고르고, 그 형태가 요구하는 자리만 채운다.
public enum UtteranceShape
{
    CoreOnly,        // 핵심 한 문장. 늑대의 기본형.
    TopicEchoCore,   // "이상현상이요? …" — 주제를 짧게 되받고 답한다.
    ReactionCore,    // "네?! 저요?" — 질문이 놀랄 만할 때만.
    CoreVolunteer,   // 답 + 묻지 않은 정보 한 마디. 토끼의 기본형.
    CoreBackQuestion,// 답 + 되묻기. 여우 전용에 가깝다.
    RepeatCore,      // "아까 말씀드린 대로…" — 같은 질문을 다시 받았을 때.
}

// 상황의 무게. 같은 캐릭터라도 사람이 죽은 직후에 평소처럼 말하지는 않는다.
public enum SituationTone
{
    Normal,
    Concerned,   // 사고가 났다
    Alarmed,     // 방금 큰 일이 터졌다
    Fearful,     // 사망 · 괴현상
    Defensive,   // 내가 의심받고 있다
}

// 말투 계층이 보는 계획. 사실은 DialogueResponsePlan 이 이미 다 정해 두었고,
// 여기에는 "그 사실을 어떤 모양으로 꺼낼까"만 들어온다.
public sealed class DialogueUtterancePlan
{
    public UtteranceShape Shape = UtteranceShape.CoreOnly;
    public SituationTone Tone = SituationTone.Normal;

    // 채울 자리(비어 있으면 쓰지 않는다).
    public string CoreSlot = "";
    public string EchoText = "";        // 주제 되받기 — 질문에서 직접 만든다
    public string ReactionSlot = "";
    public string ExtraSlot = "";       // 덧붙이는 정보 한 마디
    public string BackQuestionSlot = "";
    public string RepeatSlot = "";
    // 사실 정확성에 필요해 절대 빠지지 않는 보정(간접 목격 · 원인 불명).
    public readonly List<string> CaveatSlots = new();

    public string TimeWord = "";
    public int MaxSentences = 2;
    public int MaxExclamations = 0;
    // 말이 잠깐 막히는 표현을 허용하는가(양).
    public bool AllowPause;
}
