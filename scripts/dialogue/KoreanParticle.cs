using System.Text;

namespace NSP.Dialogue;

// 한국어 조사 처리. 문장 조각을 조립할 때 "{room}은/는" 같은 표기가 그대로 새어 나가거나
// "발전실가" 처럼 문법이 깨지는 것을 막는다.
// 판정은 마지막 한글 음절의 종성(받침) 유무 하나로 끝난다.
public static class KoreanParticle
{
    private const int HangulBase = 0xAC00;
    private const int HangulLast = 0xD7A3;
    private const int JongCount = 28;

    // 마지막 글자에 받침이 있는가. 한글이 아니면(숫자/영문/기호) 받침 없음으로 본다.
    public static bool HasFinal(string word)
    {
        for (int i = (word ?? "").Length - 1; i >= 0; i--)
        {
            char c = word[i];
            if (c == ' ' || c == '"' || c == '\'' || c == ')' || c == ']') continue;
            if (c < HangulBase || c > HangulLast) return false;
            return (c - HangulBase) % JongCount != 0;
        }
        return false;
    }

    // 마지막 글자의 받침이 ㄹ 인가 — "로/으로" 판정에만 쓴다.
    private static bool EndsWithRieul(string word)
    {
        for (int i = (word ?? "").Length - 1; i >= 0; i--)
        {
            char c = word[i];
            if (c == ' ') continue;
            if (c < HangulBase || c > HangulLast) return false;
            return (c - HangulBase) % JongCount == 8;
        }
        return false;
    }

    public static string Topic(string w) => w + (HasFinal(w) ? "은" : "는");
    public static string Subject(string w) => w + (HasFinal(w) ? "이" : "가");
    public static string Object(string w) => w + (HasFinal(w) ? "을" : "를");
    public static string With(string w) => w + (HasFinal(w) ? "과" : "와");
    public static string Direction(string w) => w + (EndsWithRieul(w) || !HasFinal(w) ? "로" : "으로");
    // "발전실이요" / "저장고요" — 되묻듯 짧게 답할 때.
    public static string Yo(string w) => w + (HasFinal(w) ? "이요" : "요");
    // "발전실이었어요" / "저장고였어요"
    public static string WasSoft(string w) => w + (HasFinal(w) ? "이었어요" : "였어요");
    public static string WasFormal(string w) => w + (HasFinal(w) ? "이었습니다" : "였습니다");

    // "{room}은/는" 형태의 슬래시 조사 표기를 실제 조사로 바꾼다.
    // 문장 조각 라이브러리에서 조사를 직접 고르지 않아도 되게 하는 진입점이다.
    public static string Resolve(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('/') < 0) return text;
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int matched = MatchPair(text, i, out string left, out string right);
            if (matched > 0)
            {
                string before = sb.ToString();
                sb.Append(HasFinal(before) ? left : right);
                i += matched;
                continue;
            }
            sb.Append(text[i]);
            i++;
        }
        return sb.ToString();
    }

    private static readonly string[][] Pairs =
    {
        new[] { "은", "는" }, new[] { "이", "가" }, new[] { "을", "를" },
        new[] { "과", "와" }, new[] { "으로", "로" },
        new[] { "이었", "였" }, new[] { "이에요", "예요" }, new[] { "이요", "요" },
    };

    // text[i] 부터 "A/B" 형태의 조사 표기가 시작되면 소비할 글자 수를 돌려준다.
    private static int MatchPair(string text, int i, out string left, out string right)
    {
        foreach (var p in Pairs)
        {
            int len = p[0].Length + 1 + p[1].Length;
            if (i + len > text.Length) continue;
            if (string.CompareOrdinal(text, i, p[0], 0, p[0].Length) != 0) continue;
            if (text[i + p[0].Length] != '/') continue;
            if (string.CompareOrdinal(text, i + p[0].Length + 1, p[1], 0, p[1].Length) != 0) continue;
            left = p[0];
            right = p[1];
            return len;
        }
        left = right = "";
        return 0;
    }
}
