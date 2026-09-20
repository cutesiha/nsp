using System.Collections.Generic;

namespace NSP.Dialogue;

// 6명의 EmployeeDef(Personality / SpeechStyle / SpeechExample / Behavior)를 읽고 손으로
// 구조화한 값. 런타임에 문장을 자연어 분석하지 않는다 — 여기 수치가 곧 캐릭터의 말버릇이다.
public static class DialogueVoiceProfiles
{
    private static readonly Dictionary<string, DialogueVoiceProfile> _profiles = Build();

    public static DialogueVoiceProfile Get(string employeeId) =>
        _profiles.GetValueOrDefault(employeeId) ?? _profiles["owl"];

    public static IReadOnlyCollection<string> Ids => _profiles.Keys;

    private static Dictionary<string, DialogueVoiceProfile> Build() => new()
    {
        // 원칙적·차분·절차 중시. 상황 → 판단 순서로 말하고, 확인된 사실만 단정한다.
        ["owl"] = new DialogueVoiceProfile
        {
            EmployeeId = "owl",
            Register = SpeechRegister.Formal,
            EmotionChance = 0.15f,
            ExactTimeChance = 0.15f,
            VagueTimeChance = 0.35f,
            SeparatesGuess = true,
            TaskMentionChance = 0.50f,
            DeceptionOrder = new[] { DeceptionMode.Omit, DeceptionMode.Vague, DeceptionMode.Truth, DeceptionMode.Deny },
        },

        // 효율 중시·까칠. 핵심부터 바로 답하고 설명을 아낀다.
        ["cat"] = new DialogueVoiceProfile
        {
            EmployeeId = "cat",
            Register = SpeechRegister.Soft,
            EmotionChance = 0.35f,
            ExactTimeChance = 0.05f,
            VagueTimeChance = 0.20f,
            SeparatesGuess = false,
            TaskMentionChance = 0.45f,
            DeceptionOrder = new[] { DeceptionMode.Justify, DeceptionMode.Minimize, DeceptionMode.Omit },
        },

        // 소심·과민. 반응이 먼저 나오고, 확신이 약하면 스스로 단서를 붙인다.
        ["jellyfish"] = new DialogueVoiceProfile
        {
            EmployeeId = "jellyfish",
            Register = SpeechRegister.Soft,
            EmotionChance = 0.45f,
            ExactTimeChance = 0.03f,
            VagueTimeChance = 0.30f,
            SeparatesGuess = true,
            TaskMentionChance = 0.20f,
            DeceptionOrder = new[] { DeceptionMode.Vague, DeceptionMode.Omit, DeceptionMode.Deny },
        },

        // 활발·즉흥. 감정이 먼저 튀고, 직접 움직이겠다는 말을 자주 한다.
        ["rabbit"] = new DialogueVoiceProfile
        {
            EmployeeId = "rabbit",
            Register = SpeechRegister.Soft,
            EmotionChance = 0.50f,
            ExactTimeChance = 0.03f,
            VagueTimeChance = 0.25f,
            SeparatesGuess = false,
            TaskMentionChance = 0.35f,
            DeceptionOrder = new[] { DeceptionMode.Justify, DeceptionMode.Minimize, DeceptionMode.Omit },
        },

        // 무뚝뚝·관찰형. 가장 짧고, 본 것과 추측을 나눠 말한다. 정확한 시각을 말할 여지가 가장 크다.
        ["crow"] = new DialogueVoiceProfile
        {
            EmployeeId = "crow",
            Register = SpeechRegister.Formal,
            EmotionChance = 0.05f,
            ExactTimeChance = 0.40f,
            VagueTimeChance = 0.15f,
            SeparatesGuess = true,
            TaskMentionChance = 0.15f,
            DeceptionOrder = new[] { DeceptionMode.Omit, DeceptionMode.Truth, DeceptionMode.Vague },
        },

        // 여유·능글. 답은 하되 핵심을 살짝 비껴가고, 질문을 되돌린다.
        ["fox"] = new DialogueVoiceProfile
        {
            EmployeeId = "fox",
            Register = SpeechRegister.Soft,
            EmotionChance = 0.25f,
            ExactTimeChance = 0.08f,
            VagueTimeChance = 0.30f,
            SeparatesGuess = false,
            TaskMentionChance = 0.30f,
            DeceptionOrder = new[] { DeceptionMode.Omit, DeceptionMode.Redirect, DeceptionMode.Minimize, DeceptionMode.Vague },
        },
    };
}
