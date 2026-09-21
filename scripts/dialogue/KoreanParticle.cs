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
                // "으로/로" 만 예외다 — ㄹ 받침 뒤에는 "로" 가 붙는다(정비실로, 저장고로).
                bool useLeft = left == "으로"
                    ? HasFinal(before) && !EndsWithRieul(before)
                    : HasFinal(before);
                sb.Append(useLeft ? left : right);
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
        new[] { "이었", "였" }, new[] { "이에요", "예요" }, new[] { "이죠", "죠" },
        new[] { "이요", "요" }, new[] { "이라고", "라고" }, new[] { "이랑", "랑" },
        new[] { "이야", "야" }, new[] { "이나", "나" }, new[] { "이라", "라" },
    };

    // ── 문장 틀 검사 ─────────────────────────────────────────────────────
    //
    // 변수 바로 뒤에는 받침에 따라 갈리는 조사를 맨몸으로 쓰면 안 된다.
    // "{room}에요"(저장고에요 ✗), "{room}이 조용했어요"(저장고이 ✗) 같은 실수는
    // 문장을 쓸 때가 아니라 파일을 읽을 때 잡아낸다 — 대사 뱅크 로더와 검증 씬이 쓴다.
    //
    // "{room}에" / "{room}에서" / "{room} 쪽" 은 받침과 무관하므로 괜찮다.
    private static readonly string[] BareParticles =
    {
        "이에요", "이었", "이요", "이라고", "이랑", "이야", "으로", "에요",
        "은", "는", "이", "가", "을", "를", "과", "와", "로", "예요", "였", "요", "랑", "야",
    };

    // 문제가 있으면 설명을, 없으면 빈 문자열을 돌려준다.
    public static string Lint(string template)
    {
        if (string.IsNullOrEmpty(template)) return "";
        int at = 0;
        while ((at = template.IndexOf('}', at)) >= 0)
        {
            at++;
            if (at >= template.Length) break;
            string rest = template[at..];
            // "이었/였" 처럼 슬래시 표기면 통과.
            bool paired = false;
            foreach (var p in Pairs)
                if (rest.StartsWith(p[0] + "/" + p[1], System.StringComparison.Ordinal)) { paired = true; break; }
            if (paired) continue;
            foreach (string bare in BareParticles)
            {
                if (!rest.StartsWith(bare, System.StringComparison.Ordinal)) continue;
                // "{task} 하고" 처럼 띄어 쓴 경우는 조사가 아니다 — StartsWith 라 이미 걸러진다.
                // "{room}이었어요" 는 받침 없는 방 이름에서 "저장고이었어요" 가 된다.
                return $"변수 뒤 조사 '{bare}' 는 받침에 따라 달라집니다 — '{PairHint(bare)}' 로 쓰세요: {template}";
            }
        }
        return "";
    }

    private static string PairHint(string bare) => bare switch
    {
        "은" or "는" => "은/는",
        "이" or "가" => "이/가",
        "을" or "를" => "을/를",
        "과" or "와" => "과/와",
        "으로" or "로" => "으로/로",
        "이에요" or "예요" or "에요" => "이에요/예요 (또는 '에 있었어요')",
        "이었" or "였" => "이었/였",
        "이요" or "요" => "이요/요",
        "이라고" => "이라고/라고",
        "이랑" or "랑" => "이랑/랑",
        "이야" or "야" => "이야/야",
        _ => "A/B",
    };

    // ── 자주 쓰는 표현 ───────────────────────────────────────────────────

    // "났" + 해요체 → "났어요", 격식체 → "났습니다". 줄기는 반드시 과거형 음절(았/었)로 끝난다.
    public static string PastEnding(string stem, bool formal) =>
        string.IsNullOrEmpty(stem) ? "" : stem + (formal ? "습니다" : "어요") + ".";

    // 고유어 수 관형사 — "두 번", "세 번".
    public static string Count(int n) => n switch
    {
        1 => "한", 2 => "두", 3 => "세", 4 => "네", 5 => "다섯",
        6 => "여섯", 7 => "일곱", 8 => "여덟", 9 => "아홉", 10 => "열",
        _ => n.ToString(),
    };

    // 대사 속 시각. 격식체는 "22시 10분경", 해요체는 "22시 10분쯤".
    // 환산은 DialogueClock 한 곳에서만 한다(조사 자료 카드와 같은 시각이 나와야 한다).
    public static string TimePhrase(float seconds, bool formal) =>
        DialogueClock.Spoken(seconds) + (formal ? "경" : "쯤");

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
