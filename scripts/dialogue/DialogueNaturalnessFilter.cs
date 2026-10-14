using System.Text.RegularExpressions;

namespace NSP.Dialogue;

// 최종 문장이 나가기 직전의 마지막 검사.
//
// 여기서 하는 일은 "사람이 이렇게는 말하지 않는다"를 걷어내는 것뿐이다. 의미를 바꾸지
// 않으며, 사실을 지우지도 않는다. 대부분의 중복은 애초에 발화 계획 단계에서 막지만
// (같은 정보를 두 자리에 넣지 않는다), 조합의 마지막 구멍을 여기서 닫는다.
public static class DialogueNaturalnessFilter
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex SpaceBeforePunct = new(@"\s+([.,!?…])", RegexOptions.Compiled);
    // "네. 네." / "네! 네!" / "아, 네. 네" — 같은 긍정이 두 번.
    private static readonly Regex DoubleYes = new(@"(네|예)([.!,?]\s*)(네|예)([.!?]?)", RegexOptions.Compiled);
    // "아, 그거요? 아, 네." — 같은 반응어가 한 답변에 두 번. 두 번째도 낱말 하나로 서 있어야 한다
    // ("아, 안녕하세요" 같은 더듬기나 "있어." 의 '어' 는 반응어가 아니다).
    private static readonly Regex DoubleAh = new(@"(^|\s)(아|어|음)[,.!…]\s*.*?\s(?<second>\2)[,.!…]", RegexOptions.Compiled);
    // "....." 처럼 길게 늘어진 말줄임.
    private static readonly Regex LongEllipsis = new(@"\.{3,}|…{2,}", RegexOptions.Compiled);

    public static string Clean(string text, int maxExclamations, int maxEllipses = 2)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        // ① 중복 긍정 — 뒤쪽을 지운다.
        for (int i = 0; i < 2 && DoubleYes.IsMatch(text); i++)
            text = DoubleYes.Replace(text, "$1$2", 1);

        // ② 같은 반응어 반복 — 뒤에 나온 쪽을 지운다.
        var ah = DoubleAh.Match(text);
        if (ah.Success)
        {
            var g = ah.Groups["second"];
            int second = g.Index;
            if (second > 0 && second + g.Length < text.Length)
            {
                int cut = second + g.Length;
                while (cut < text.Length && ",.…! ".Contains(text[cut])) cut++;
                text = text[..second] + text[cut..];
            }
        }

        // ③ 말줄임표는 한 답변에 최대 두 번(말끝을 흐리는 게 버릇인 사람은 더), 길이는 "..." 까지.
        text = LongEllipsis.Replace(text, "...");
        text = LimitOccurrences(text, "...", maxEllipses);

        // ④ 느낌표 상한. 넘치는 것은 마침표로 내린다.
        text = LimitExclamations(text, maxExclamations);

        // ⑤ 공백·문장부호 정리.
        text = Spaces.Replace(text, " ").Trim();
        text = SpaceBeforePunct.Replace(text, "$1");
        text = text.Replace(" ?", "?").Replace(" !", "!");
        if (text.Length > 0 && !".!?…~ㅎ".Contains(text[^1])) text += ".";
        return text;
    }

    // 같은 의미를 두 번 말하고 있는가. 계획 단계에서 조각을 붙일지 결정할 때 쓴다.
    public static bool Repeats(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        string x = Core(a), y = Core(b);
        if (x.Length == 0 || y.Length == 0) return false;
        // 알맹이가 한두 글자면("저요?" → "저") 포함 관계가 우연히 성립한다 — 세 글자부터 본다.
        if ((y.Length >= 3 && x.Contains(y)) || (x.Length >= 3 && y.Contains(x))) return true;
        // 앞머리가 길게 겹치면 사람 귀에는 같은 말이다.
        // ("뭔가 들으신 게 있으신가요" / "뭔가 들으신 게 있으시면 말씀해주세요")
        int same = 0;
        while (same < x.Length && same < y.Length && x[same] == y[same]) same++;
        return same >= 6;
    }

    // 어미와 기호를 떼어낸 알맹이. "직접 봤어요." 와 "직접 봤습니다" 를 같게 본다.
    private static string Core(string s)
    {
        // 더듬기는 같은 말의 겉모양일 뿐이다 — "괘, 괜히 …" 와 "괜히 …" 를 같게 본다.
        s = DialogueVoiceTics.StripStutter(s);
        s = Regex.Replace(s, @"[.!?…,~\s]", "");
        foreach (string tail in new[] { "습니다", "했어요", "했는데요", "이에요", "예요", "어요", "아요", "죠", "요" })
            if (s.EndsWith(tail)) { s = s[..^tail.Length]; break; }
        return s;
    }

    private static string LimitOccurrences(string text, string token, int max)
    {
        int found = 0, at = 0;
        while ((at = text.IndexOf(token, at, System.StringComparison.Ordinal)) >= 0)
        {
            found++;
            if (found > max)
            {
                // 지우면 앞뒤 문장이 붙어 버린다("있었어요 저는") — 마침표 하나로 내린다.
                // 뒤에 ?/! 가 붙어 있으면 그 부호가 문장을 끝내므로 그냥 지운다("할까요...?" → "할까요?").
                bool punctAfter = at + token.Length < text.Length && "?!".Contains(text[at + token.Length]);
                text = text.Remove(at, token.Length);
                if (!punctAfter) { text = text.Insert(at, "."); at += 1; }
                continue;
            }
            at += token.Length;
        }
        return text;
    }

    private static string LimitExclamations(string text, int max)
    {
        int kept = 0;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '!')
            {
                kept++;
                sb.Append(kept <= max ? '!' : '.');
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
