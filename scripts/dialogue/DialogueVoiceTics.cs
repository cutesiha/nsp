using System.Text.RegularExpressions;
using Godot;
using NSP.Data;

namespace NSP.Dialogue;

// 완성된 답변에 캐릭터 말버릇을 한 번 입힌다 — 양의 더듬기 · 말끝 흐리기, 여우의 "~".
//
// 대사 뱅크 문장에 이미 그 표식이 있으면 아무것도 하지 않는다. 표식은 한 답변에 한 번만
// 덧대며 의미(사실)는 건드리지 않는다. 수치는 data/dialogue/voices/*.tres 에 있다.
public static class DialogueVoiceTics
{
    // "저, 저는" / "자, 잘" / "벼, 별일" — 한 음절(받침 뺀 것)이 쉼표를 사이에 두고 되풀이된다.
    private static readonly Regex StutterCandidate = new(@"(?:^|\s)([가-힣]), ([가-힣])", RegexOptions.Compiled);
    private static readonly Regex FirstWord = new(@"^([가-힣])([가-힣]+)", RegexOptions.Compiled);
    // 문장 끝의 "요." — 뒤에 공백이나 끝이 온다.
    private static readonly Regex YoEnd = new(@"요\.(?=\s|$)", RegexOptions.Compiled);

    // 더듬으면 어색한 첫마디 — 반응어나 대답 한 음절은 건너뛴다.
    private static readonly string[] NoStutterStarts = { "네", "아", "어", "음", "예" };

    public static string Apply(string text, DialogueVoiceDef voice)
    {
        if (string.IsNullOrWhiteSpace(text) || voice == null) return text;

        if (voice.StutterChance > 0f && !HasStutter(text) && GD.Randf() < voice.StutterChance)
            text = StutterFirst(text);

        if (voice.TrailOffChance > 0f && !text.Contains("...") && GD.Randf() < voice.TrailOffChance)
            text = TrailOff(text);

        if (voice.TildeChance > 0f && !text.Contains('~') && GD.Randf() < voice.TildeChance)
            text = Tilde(text);

        return text;
    }

    // 첫 낱말(두 음절 이상)의 첫 음절을 한 번 더듬는다. "발전실에 …" → "바, 발전실에 …"
    private static string StutterFirst(string text)
    {
        foreach (string skip in NoStutterStarts)
            if (text.StartsWith(skip) && (text.Length == skip.Length || !IsHangul(text[skip.Length])))
            {
                // "네, 저는 …" 이면 반응어 뒤 낱말을 더듬는다.
                int next = text.IndexOf(' ');
                if (next < 0) return text;
                return text[..(next + 1)] + StutterWord(text[(next + 1)..]);
            }
        return StutterWord(text);
    }

    // 더듬는 음절은 받침을 뺀다 — "발전실" → "바, 발전실", "관리자님" → "과, 관리자님".
    private static string StutterWord(string s)
    {
        var m = FirstWord.Match(s);
        return m.Success ? Open(m.Groups[1].Value[0]) + ", " + s : s;
    }

    public static bool HasStutter(string text)
    {
        foreach (Match m in StutterCandidate.Matches(text))
            if (Open(m.Groups[1].Value[0]) == Open(m.Groups[2].Value[0])) return true;
        return false;
    }

    // 더듬기를 걷어낸 문장 — 같은 말인지 비교할 때 쓴다.
    public static string StripStutter(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return StutterCandidate.Replace(text, m =>
            Open(m.Groups[1].Value[0]) == Open(m.Groups[2].Value[0])
                ? m.Value[..(m.Value.Length - m.Groups[2].Length - m.Groups[1].Length - 2)] + m.Groups[2].Value
                : m.Value);
    }

    // 받침을 뗀 음절.
    private static char Open(char c) => IsHangul(c) ? (char)(0xAC00 + (c - 0xAC00) / 28 * 28) : c;

    // 마지막 문장 끝을 흐린다. "?" 는 "...?", "." 는 "...". 느낌표 문장은 흐리지 않는다.
    private static string TrailOff(string text)
    {
        char last = text[^1];
        if (last == '.') return text[..^1] + "...";
        if (last == '?') return text[..^1] + "...?";
        return text;
    }

    // "요." 가운데 하나를 "요~" 로. 여러 개면 무작위로 하나.
    private static string Tilde(string text)
    {
        var ms = YoEnd.Matches(text);
        if (ms.Count == 0) return text;
        var m = ms[(int)(GD.Randi() % (uint)ms.Count)];
        return text[..m.Index] + "요~" + text[(m.Index + m.Length)..];
    }

    private static bool IsHangul(char c) => c >= '가' && c <= '힣';
}
