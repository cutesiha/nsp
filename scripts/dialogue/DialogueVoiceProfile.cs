namespace NSP.Dialogue;

public enum SpeechRegister
{
    Formal, // …습니다 / …입니다
    Soft,   // …어요 / …예요
}

// 캐릭터가 "어떻게 말하는가"를 수치로 옮긴 것.
// 어미만 바꾸는 것이 아니라 정보량·순서·생략 여부를 결정한다.
//   · MaxParts    : 한 대답에 들어갈 문장 조각 수 상한(까마귀 2, 올빼미 3 …)
//   · *Chance     : 각 보조 조각을 붙일 확률
//   · ReactionFirst: 감정 반응이 핵심 답변보다 먼저 나오는가(토끼·해파리)
//   · ExactTimeChance : 정확한 시각을 말할 확률(까마귀만 높다)
//   · SeparatesGuess  : 본 것과 추측을 따로 구분해 말하는가(까마귀·올빼미)
//   · DeceptionOrder  : 방해자일 때 선호하는 전략 순서
public sealed class DialogueVoiceProfile
{
    public string EmployeeId = "";
    public SpeechRegister Register = SpeechRegister.Formal;

    public int MaxParts = 3;
    public float OpenerChance;
    public float SupportChance;
    public float EmotionChance;
    public float CloserChance;

    public bool ReactionFirst;
    public float ExactTimeChance;
    public float VagueTimeChance;
    public float HedgeChance;
    public bool SeparatesGuess;
    public bool AsksBack;
    public float TaskMentionChance;

    public DeceptionMode[] DeceptionOrder = { DeceptionMode.Omit, DeceptionMode.Vague, DeceptionMode.Truth };

    // 문장 끝맺음. 과거형 어간("멈췄", "들렸")에 붙여 완성한다.
    public string PastEnding => Register == SpeechRegister.Formal ? "습니다" : "어요";
    public string PresentEnding => Register == SpeechRegister.Formal ? "습니다" : "어요";
}
