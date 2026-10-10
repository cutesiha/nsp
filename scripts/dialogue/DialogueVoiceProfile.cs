namespace NSP.Dialogue;

public enum SpeechRegister
{
    Formal, // …습니다 / …입니다
    Soft,   // …어요 / …예요
}

// 캐릭터가 "무엇을 말할지" 고를 때 쓰는 성향값.
//
// V2 부터 말투(문장 길이 · 반응 · 되묻기 · 기호)는 전부
// data/dialogue/voices/*.tres(DialogueVoiceDef)로 옮겼다. 여기 남은 것은
// DialogueResponsePlanner 가 계획을 세울 때 보는 값뿐이다.
//   · ExactTimeChance / VagueTimeChance : 시각을 얼마나 정확히 말하는가
//   · SeparatesGuess  : 본 것과 추측을 따로 구분해 말하는가(늑대·양)
//   · TaskMentionChance / EmotionChance : 업무·감정을 언급할 성향
//   · DeceptionOrder  : 방해자일 때 선호하는 전략 순서
public sealed class DialogueVoiceProfile
{
    public string EmployeeId = "";
    public SpeechRegister Register = SpeechRegister.Formal;

    public float EmotionChance;
    public float ExactTimeChance;
    public float VagueTimeChance;
    public bool SeparatesGuess;
    public float TaskMentionChance;

    public DeceptionMode[] DeceptionOrder = { DeceptionMode.Omit, DeceptionMode.Vague, DeceptionMode.Truth };
}
