using System.Collections.Generic;
using Godot;
using NSP.Prologue;

namespace NSP.View;

// 직원 스탠딩 원화(standing_v2) — 표정(smile / bad) × 입(닫은 입 / 작은 입 / 큰 입) 6장.
//
//   res://assets/characters/standing_v2/<id>/<id>_<표정>_<closed|small|big>mouth.png
//
// 폴더가 있는 직원만 말하는 입 모양을 쓴다. 없는 직원은 EmployeeDef.StandingImage 그대로다.
public static class EmployeeArt
{
    private const string Dir = "res://assets/characters/standing_v2/";

    // 평소 표정. 여기 없는 직원은 smile 이 기본이다.
    private static readonly Dictionary<string, string> DefaultExpressions = new()
    {
        ["cat"] = "bad",
        ["rabbit"] = "smile",
        ["sheep"] = "bad",
        ["fox"] = "smile",
        ["wolf"] = "bad",
        ["dog"] = "smile",
    };

    private static readonly Dictionary<string, Texture2D> _cache = new();

    public static string DefaultExpression(string employeeId) =>
        DefaultExpressions.GetValueOrDefault(employeeId ?? "", "smile");

    public static bool HasTalkingArt(string employeeId) =>
        !string.IsNullOrEmpty(employeeId) && Get(employeeId, DefaultExpression(employeeId), GuideMouthFrame.Closed) != null;

    // 요청한 표정을 쓸 수 있는가 — 없는 표정("normal" 처럼 리소스가 없는 이름)을 스토리
    // 스크립트가 지정했을 때 평소 표정으로 조용히 떨어지게 한다.
    // 실제로 있는 표정은 smile · bad 둘뿐이다(standing_v2).
    public static string ResolveExpression(string employeeId, string requested)
    {
        if (string.IsNullOrEmpty(requested)) return DefaultExpression(employeeId);
        return Get(employeeId, requested, GuideMouthFrame.Closed) != null
            ? requested : DefaultExpression(employeeId);
    }

    public static Texture2D Get(string employeeId, string expression, GuideMouthFrame mouth)
    {
        string m = mouth switch
        {
            GuideMouthFrame.Small => "small",
            GuideMouthFrame.Open => "big",
            _ => "closed",
        };
        string path = $"{Dir}{employeeId}/{employeeId}_{expression}_{m}mouth.png";
        if (_cache.TryGetValue(path, out var hit)) return hit;
        var tex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        _cache[path] = tex;
        return tex;
    }
}

// 대사 한 줄의 분위기로 표정을 고른다.
//   · 평소 표정이 bad 인 직원(고양이 · 양 · 늑대)은 기쁘거나 즐겁거나 웃긴 말을 할 때만 smile.
//   · 평소 표정이 smile 인 직원(토끼 · 여우 · 강아지)은 슬프거나 화나거나 당황한 말을 할 때만 bad.
// 판정은 단어 신호의 개수 비교다 — 섞여 있으면 평소 표정을 유지한다.
public static class EmployeeExpression
{
    private static readonly string[] Happy =
    {
        "다행", "좋았", "좋아요", "좋네", "좋겠", "재밌", "재미있", "즐거", "기뻐", "기쁘", "신나", "신기",
        "뿌듯", "고마", "감사", "칭찬", "최고", "ㅎㅎ", "하하", "헤헤", "웃겼", "웃기", "웃었", "잘 풀렸", "순조", "평화",
        "편했", "안심", "든든", "덜 무서", "별일 없었", "별 일 없었", "멋있", "완벽",
    };

    private static readonly string[] Bad =
    {
        // 남의 감정("무서워하시길래", "놀란 것 같더라고요")이 아니라 말하는 사람 자신의 감정만 센다.
        "무서웠", "무서워요", "무섭", "무서운", "놀랐", "깜짝", "억울", "죄송", "힘들었", "힘들어요", "힘드네",
        "지쳤", "피곤해요", "피곤했", "피곤하긴", "슬프", "슬퍼", "화나", "짜증", "당황", "섬뜩", "소름", "오싹",
        "어떡", "으악", "으앗", "에엑", "싫어요", "싫었", "서운", "불안",
        "큰일 났", "큰일이", "아니라니까", "저요?!", "절대 아니", "비명", "쓰러", "죽", "사고가", "망했",
        "손해", "밀렸", "막혔", "떨려", "헷갈", "제가 잘못", "잘못했", "기분 나빴", "좋을 리", "심장",
    };

    public static string For(string employeeId, string line)
    {
        string normal = EmployeeArt.DefaultExpression(employeeId);
        if (string.IsNullOrEmpty(line)) return normal;
        int happy = Count(line, Happy), bad = Count(line, Bad);
        if (normal == "bad") return happy > bad ? "smile" : "bad";
        return bad > happy ? "bad" : "smile";
    }

    private static int Count(string text, string[] words)
    {
        int n = 0;
        foreach (string w in words)
            if (text.Contains(w)) n++;
        return n;
    }
}

// 통화 · 심문에서 직원이 말하는 동안의 입 모양. GUIDE-0 과 같은 규칙으로 움직인다
// (정해진 순환 패턴 · 문장부호에서 잠깐 다묾 · 대사가 끝나면 닫은 입).
// 한 번에 한 사람만 말하므로 상태는 하나다.
public static class EmployeeMouthAnimator
{
    public static string Speaker { get; private set; } = "";
    public static string Expression { get; private set; } = "";
    public static bool Talking { get; private set; }
    public static GuideMouthFrame Frame { get; private set; } = GuideMouthFrame.Closed;

    private static int _step;
    private static double _frameClock;
    private static double _hold;
    private static char _lastChar;

    // 새 대사를 말하기 시작한다. 표정은 이 대사의 분위기로 정해져 다음 대사까지 유지된다.
    //
    // expression 을 주면(스토리 컷인의 `expression:` 지정) 분위기 판정 대신 그 표정을 쓴다.
    // 없는 표정 이름이면 그 직원의 평소 표정으로 떨어진다.
    public static void StartTalking(string employeeId, string line, string expression = null)
    {
        Speaker = employeeId ?? "";
        Expression = string.IsNullOrEmpty(expression)
            ? EmployeeExpression.For(Speaker, line)
            : EmployeeArt.ResolveExpression(Speaker, expression);
        Talking = true;
        // 말이 시작되는 바로 그 프레임부터 입이 벌어져 있어야 한다. 예전에는 닫은 입(패턴 0번)에서
        // 출발해 첫 글자의 여는 따옴표 정지(PunctuationHold)까지 겹쳐, 소리가 난 뒤 0.3초쯤 지나서야
        // 입이 움직였다 — 휴게시간 심문에서 "말과 입이 어긋난다"는 지적이 그것이다.
        _step = OpenStep;
        _frameClock = 0;
        _hold = 0;
        _lastChar = '\0';
        Frame = GuideMouthAnimator.Pattern[OpenStep];
    }

    // 패턴에서 처음으로 입이 크게 열리는 자리.
    private static readonly int OpenStep =
        System.Array.IndexOf(GuideMouthAnimator.Pattern, GuideMouthFrame.Open) is var i && i >= 0 ? i : 0;

    public static void StopTalking()
    {
        Talking = false;
        _hold = 0;
        Frame = GuideMouthFrame.Closed;
    }

    // 통화가 끝나면 평소 표정으로 돌아간다.
    public static void Reset()
    {
        StopTalking();
        Speaker = "";
        Expression = "";
    }

    public static void NoticeCharacter(char c)
    {
        // 대사를 감싸는 여는 따옴표 — 말이 아니므로 입을 멈추지 않는다.
        if (c == '"' && _lastChar == '\0') { _lastChar = c; return; }
        if (c is '.' or '…' or '·')
        {
            bool run = _lastChar is '.' or '…' or '·';
            _hold = System.Math.Max(_hold, run ? GuideMouthAnimator.EllipsisHold : GuideMouthAnimator.PunctuationHold);
        }
        else if (c is ',' or '?' or '!' or '~' or '"')
        {
            _hold = System.Math.Max(_hold, GuideMouthAnimator.PunctuationHold);
        }
        _lastChar = c;
    }

    // 같은 프레임에 두 번 돌지 않는다. 통화창(PhoneCallHud)은 열려 있지 않아도 매 프레임
    // 여기를 돌리므로, 스토리 컷인까지 함께 돌리면 입이 두 배 속도로 움직인다.
    private static ulong _tickedFrame = ulong.MaxValue;

    public static void Tick(double delta)
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame == _tickedFrame) return;
        _tickedFrame = frame;

        if (!Talking) { Frame = GuideMouthFrame.Closed; return; }
        if (_hold > 0)
        {
            _hold -= delta;
            Frame = GuideMouthFrame.Closed;
            if (_hold <= 0) { _hold = 0; _frameClock = 0; }
            return;
        }
        var pattern = GuideMouthAnimator.Pattern;
        _frameClock += delta;
        while (_frameClock >= GuideMouthAnimator.FrameSeconds)
        {
            _frameClock -= GuideMouthAnimator.FrameSeconds;
            _step = (_step + 1) % pattern.Length;
        }
        Frame = pattern[_step];
    }

    // 지금 이 직원을 그릴 원화. 말하는 중이 아니면 마지막 표정(없으면 평소 표정)의 닫은 입.
    public static Texture2D PortraitFor(string employeeId)
    {
        if (!EmployeeArt.HasTalkingArt(employeeId)) return null;
        bool speaking = employeeId == Speaker;
        string expr = speaking && !string.IsNullOrEmpty(Expression) ? Expression : EmployeeArt.DefaultExpression(employeeId);
        var frame = speaking ? Frame : GuideMouthFrame.Closed;
        return EmployeeArt.Get(employeeId, expr, frame) ?? EmployeeArt.Get(employeeId, expr, GuideMouthFrame.Closed);
    }

    // 표정을 밖에서 정해 주는 경로(스토리 컷인). 2인 화면에서 **지금 말하는 사람만** 입이
    // 움직이고, 듣는 쪽은 자기 표정의 닫은 입으로 서 있어야 하기 때문에 따로 둔다.
    public static Texture2D PortraitFor(string employeeId, string expression)
    {
        if (!EmployeeArt.HasTalkingArt(employeeId)) return null;
        string expr = EmployeeArt.ResolveExpression(employeeId, expression);
        var frame = employeeId == Speaker && Talking ? Frame : GuideMouthFrame.Closed;
        return EmployeeArt.Get(employeeId, expr, frame) ?? EmployeeArt.Get(employeeId, expr, GuideMouthFrame.Closed);
    }
}
