namespace NSP.Dialogue;

// 오늘의 기분이 어느 쪽 감정인가.
//
// "여유로움" 이라고 적어 낸 사람이 "사고가 신경 쓰여서요" 라고 답하면 말이 안 된다.
// 기분을 사고와 연결해도 되는지는 이 분류 하나로만 판단한다 — 답변을 만드는 쪽에서
// 문자열을 직접 뒤지지 않게 하기 위한 것이다.
//
// 표현은 data/moods/*.tres 에 있고, 분류 기준 단어만 여기 둔다.
public enum MoodTone
{
    Calm,      // 여유로움 · 평온함 · 괜찮음 — 사고 탓으로 돌리면 모순이다
    Uneasy,    // 불안함 · 예민함 · 피곤함 — 사고와 이어 말해도 자연스럽다
    Neutral,   // 그냥 그럼 · 평범함 — 어느 쪽으로도 단정하지 않는다
}

public static class MoodTones
{
    // 부정·불안 계열. 이 말이 들어 있으면 오늘 겪은 사고와 이어 말할 수 있다.
    private static readonly string[] UneasyWords =
    {
        "불안", "긴장", "예민", "피곤", "짜증", "무섭", "무서", "신경", "찝찝", "화가",
        "힘들", "불편", "이상", "의심", "잠을", "걱정",
    };

    // 긍정·안정 계열. 사고를 이유로 대면 앞말과 어긋난다.
    private static readonly string[] CalmWords =
    {
        "여유", "평온", "괜찮", "좋음", "좋아", "나쁘지 않", "가벼", "설렘", "기대", "든든",
        "무난", "평소와 비슷", "차분",
    };

    public static MoodTone Of(string moodText)
    {
        if (string.IsNullOrWhiteSpace(moodText)) return MoodTone.Neutral;
        foreach (string w in UneasyWords)
            if (moodText.Contains(w)) return MoodTone.Uneasy;
        foreach (string w in CalmWords)
            if (moodText.Contains(w)) return MoodTone.Calm;
        return MoodTone.Neutral;
    }

    // 이 기분을 오늘 겪은 사고와 이어 말해도 되는가.
    public static bool CanBlameIncident(string moodText) => Of(moodText) == MoodTone.Uneasy;
}
