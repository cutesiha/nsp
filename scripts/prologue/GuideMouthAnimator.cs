using Godot;
using NSP.Core;

namespace NSP.Prologue;

public enum GuideMouthFrame { Closed, Small, Open }

// GUIDE-0 의 초상화 리소스를 한 곳에서 찾는다.
//
// 얼굴은 두 벌이 있을 수 있다.
//   guide0_<표정>_base.png : 입이 없는 얼굴  → 입 Overlay 를 그 위에 얹는다
//   guide0_<표정>.png      : 입까지 그려진 얼굴 → Overlay 를 얹지 않는다(옛 그림 호환)
// _base 가 있으면 항상 그쪽을 쓴다.
public static class GuideArt
{
    public const string Dir = "res://assets/ui/guide0/";
    private static readonly string[] Ext = { ".png", ".webp", ".jpg" };

    // 표정 얼굴. mouthless 가 true 면 입 Overlay 를 따로 그려야 한다.
    public static Texture2D Portrait(string expression, out bool mouthless)
    {
        string key = string.IsNullOrEmpty(expression) ? "normal" : expression;
        var baseFace = Find($"guide0_{key}_base");
        if (baseFace != null) { mouthless = true; return baseFace; }
        mouthless = false;
        return Find($"guide0_{key}");
    }

    public static Texture2D Mouth(GuideMouthFrame frame) => frame switch
    {
        GuideMouthFrame.Small => Find("guide0_MouthSmall") ?? Find("guide0_mouthsmall"),
        GuideMouthFrame.Open => Find("guide0_MouthOpen") ?? Find("guide0_mouthopen"),
        _ => Find("guide0_MouthClosed") ?? Find("guide0_mouthclosed"),
    };

    public static Texture2D Horror() => Find("guide0_horrorface") ?? Find("guide0_horror");

    private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _cache = new();

    private static Texture2D Find(string stem)
    {
        if (_cache.TryGetValue(stem, out var hit)) return hit;
        Texture2D tex = null;
        foreach (string ext in Ext)
        {
            string path = Dir + stem + ext;
            if (!ResourceLoader.Exists(path)) continue;
            tex = GD.Load<Texture2D>(path);
            break;
        }
        _cache[stem] = tex;
        return tex;
    }
}

// GUIDE-0 가 말하는 동안 입을 움직인다.
//
// 얼굴 화면(GuideFaceView)과 홀로그램 창의 작은 초상(PortraitBox)이 같은 입을 써야 하므로
// 상태는 여기 한 곳에만 둔다. 두 화면은 CurrentMouth 를 읽어 그리기만 한다.
//
// 규칙
//   · 글자마다 입을 바꾸지 않는다 — 타이핑 중 FrameSeconds 간격으로만 바꾼다.
//     (글자 단위로 바꾸면 타이핑 속도에 따라 입이 덜덜 떨린다.)
//   · 프레임은 무작위가 아니라 손으로 짠 순환 패턴을 돈다. 변화는 있지만 튀지 않는다.
//   · 문장부호가 찍히는 순간에는 잠깐 입을 다문다.
//   · 대사가 끝나거나 스킵되면 즉시 다문 입으로 돌아간다.
public static class GuideMouthAnimator
{
    // 입 모양이 바뀌는 간격. 타이핑 속도와 무관하게 이 값으로만 움직인다.
    public const double FrameSeconds = 0.075;
    // 쉼표·마침표에서 잠깐 다무는 시간.
    public const double PunctuationHold = 0.11;
    // "..." 처럼 점이 이어지면 좀 더 길게 쉰다.
    public const double EllipsisHold = 0.28;

    // 대사를 다 읽고도 넘기지 않을 때, 이만큼 지나면 글리치가 시작될 수 있다.
    public const double IdleBeforeGlitch = 4.0;
    // 글리치와 글리치 사이 간격(무작위).
    public const double GlitchGapMin = 3.5, GlitchGapMax = 7.5;
    // 호러 얼굴이 스치는 시간 — 아주 짧아야 한다.
    public const double GlitchDuration = 0.09;

    // 손으로 짠 말하기 패턴. 닫힘/작게/크게가 섞여 돌지만 순서가 정해져 있어 떨리지 않는다.
    private static readonly GuideMouthFrame[] Pattern =
    {
        GuideMouthFrame.Closed, GuideMouthFrame.Small, GuideMouthFrame.Open, GuideMouthFrame.Small,
        GuideMouthFrame.Closed, GuideMouthFrame.Small, GuideMouthFrame.Open, GuideMouthFrame.Open,
        GuideMouthFrame.Small, GuideMouthFrame.Closed, GuideMouthFrame.Small, GuideMouthFrame.Open,
        GuideMouthFrame.Small, GuideMouthFrame.Small,
    };

    public static bool Talking { get; private set; }
    public static GuideMouthFrame Frame { get; private set; } = GuideMouthFrame.Closed;

    // 호러 얼굴이 스치는 중인가. 두 화면이 이 값을 보고 얼굴을 갈아 끼운다.
    public static bool HorrorActive { get; private set; }
    // 글리치 동안 화면을 찢을 때 쓰는 난수 씨앗(프레임마다 바뀐다).
    public static int GlitchSeed { get; private set; }

    // 말하는 동안 초상화가 아주 약하게 위아래로 움직인다(1px).
    public static float BobOffset => Talking && Frame == GuideMouthFrame.Open ? 1f : 0f;

    public static Texture2D CurrentMouth => GuideArt.Mouth(Frame);

    private static int _step;
    private static double _frameClock;
    private static double _holdUntilMuted;   // 남은 '입 다물기' 시간
    private static char _lastChar;

    private static bool _idleWaiting;
    private static double _idleClock;
    private static double _nextGlitchAt;
    private static double _glitchUntil;
    private static readonly RandomNumberGenerator Rng = new();

    // --- 상태 전환 -------------------------------------------------------

    public static void StartTalking()
    {
        Talking = true;
        _step = 0;
        _frameClock = 0;
        _holdUntilMuted = 0;
        _lastChar = '\0';
        Frame = GuideMouthFrame.Closed;
        SetIdleWaiting(false);
    }

    public static void StopTalking()
    {
        Talking = false;
        _holdUntilMuted = 0;
        Frame = GuideMouthFrame.Closed;
    }

    public static void SetMouthClosed() => Frame = GuideMouthFrame.Closed;

    // 대사를 다 읽고 플레이어의 입력을 기다리는 중인가. 이때만 글리치가 끼어든다.
    public static void SetIdleWaiting(bool waiting)
    {
        if (_idleWaiting == waiting) return;
        _idleWaiting = waiting;
        _idleClock = 0;
        _nextGlitchAt = IdleBeforeGlitch;
        if (!waiting) { HorrorActive = false; _glitchUntil = 0; }
    }

    // 장면이 바뀔 때 전부 초기화한다.
    public static void Reset()
    {
        StopTalking();
        _idleWaiting = false;
        _idleClock = 0;
        _glitchUntil = 0;
        HorrorActive = false;
    }

    // 방금 찍힌 글자. 문장부호면 잠깐 입을 다문다.
    public static void NoticeCharacter(char c)
    {
        bool dot = c is '.' or '…' or '·';
        if (dot)
        {
            // 점이 이어지면(…, ...) 쉬는 시간을 늘린다.
            bool run = _lastChar is '.' or '…' or '·';
            _holdUntilMuted = System.Math.Max(_holdUntilMuted, run ? EllipsisHold : PunctuationHold);
        }
        else if (c is ',' or '?' or '!')
        {
            _holdUntilMuted = System.Math.Max(_holdUntilMuted, PunctuationHold);
        }
        _lastChar = c;
    }

    // --- 매 프레임 -------------------------------------------------------

    // 입 모양이나 글리치 상태가 바뀌면 true — 호출부가 그때만 다시 그리면 된다.
    public static bool Tick(double delta)
    {
        var before = Frame;
        bool beforeHorror = HorrorActive;

        if (Talking)
        {
            if (_holdUntilMuted > 0)
            {
                // 문장부호 구간 — 다문 채로 쉰다.
                _holdUntilMuted -= delta;
                Frame = GuideMouthFrame.Closed;
                if (_holdUntilMuted <= 0) { _frameClock = 0; _holdUntilMuted = 0; }
            }
            else
            {
                _frameClock += delta;
                while (_frameClock >= FrameSeconds)
                {
                    _frameClock -= FrameSeconds;
                    _step = (_step + 1) % Pattern.Length;
                }
                Frame = Pattern[_step];
            }
        }
        else if (Frame != GuideMouthFrame.Closed)
        {
            Frame = GuideMouthFrame.Closed;
        }

        TickGlitch(delta);
        return Frame != before || HorrorActive != beforeHorror || HorrorActive;
    }

    // 대사를 다 읽고도 한참 넘기지 않으면, 가끔 얼굴이 아주 잠깐 일그러진다.
    private static void TickGlitch(double delta)
    {
        if (!_idleWaiting || Talking)
        {
            if (HorrorActive) HorrorActive = false;
            return;
        }

        _idleClock += delta;

        if (HorrorActive)
        {
            GlitchSeed = (int)Rng.Randi();
            if (_idleClock >= _glitchUntil)
            {
                HorrorActive = false;
                _nextGlitchAt = _idleClock + Rng.RandfRange((float)GlitchGapMin, (float)GlitchGapMax);
            }
            return;
        }

        if (_idleClock < _nextGlitchAt) return;
        if (GuideArt.Horror() == null) { _nextGlitchAt = _idleClock + GlitchGapMax; return; }

        HorrorActive = true;
        GlitchSeed = (int)Rng.Randi();
        _glitchUntil = _idleClock + GlitchDuration;
        Sfx.Instance?.Play("noise", -22f, Rng.RandfRange(1.2f, 1.6f));
    }
}

// 얼굴 + 입 Overlay(+ 호러 글리치)를 한 번에 그린다.
// 얼굴 화면(328칸)과 홀로그램 창의 작은 초상(164칸)이 같은 함수를 쓰므로
// 두 화면에서 입 위치가 어긋날 수가 없다 — 입 그림의 캔버스가 얼굴과 같기 때문에
// 얼굴을 맞춘 사각형에 그대로 겹쳐 그리면 끝이다.
public static class GuideFacePaint
{
    public static void Draw(CanvasItem ci, Texture2D face, bool mouthless, Rect2 rect, Color tint)
    {
        if (ci == null || face == null) return;

        // 대사를 넘기지 않고 오래 두면 아주 잠깐 다른 얼굴이 스친다.
        if (GuideMouthAnimator.HorrorActive)
        {
            var horror = GuideArt.Horror();
            if (horror != null)
            {
                DrawTorn(ci, horror, rect, GuideMouthAnimator.GlitchSeed);
                return;
            }
        }

        ci.DrawTextureRect(face, rect, false, tint);
        if (!mouthless) return;

        var mouth = GuideMouthAnimator.CurrentMouth;
        if (mouth != null) ci.DrawTextureRect(mouth, rect, false, tint);
    }

    // 가로로 몇 겹 찢어 어긋나게 그린다. 색도 한 겹 어긋나게 겹쳐 신호가 깨진 느낌을 준다.
    private static void DrawTorn(CanvasItem ci, Texture2D tex, Rect2 rect, int seed)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(uint)seed };
        var src = tex.GetSize();
        if (src.X <= 0f || src.Y <= 0f) return;

        // 색 어긋남(고스트) 한 겹.
        ci.DrawTextureRect(tex, rect with { Position = rect.Position + new Vector2(4f, 0f) },
            false, new Color(0.35f, 0.95f, 1f, 0.35f));

        const int Bands = 7;
        for (int i = 0; i < Bands; i++)
        {
            float t0 = i / (float)Bands;
            float t1 = (i + 1) / (float)Bands;
            var srcBand = new Rect2(0f, src.Y * t0, src.X, src.Y * (t1 - t0));
            float dx = rng.RandfRange(-1f, 1f) * rect.Size.X * 0.04f;
            var dstBand = new Rect2(rect.Position.X + dx, rect.Position.Y + rect.Size.Y * t0,
                rect.Size.X, rect.Size.Y * (t1 - t0));
            ci.DrawTextureRectRegion(tex, dstBand, srcBand);
        }
    }
}
