using Godot;

namespace NSP.Prologue;

// 프롤로그 컷씬 자막 · 경고 문구 · GUIDE-0 대사 · 승계 콘솔이 모두 같은 속도로 타이핑되도록
// 한 곳에서 관리한다. 여기 값만 바꾸면 프롤로그/튜토리얼 전체의 글자 속도가 같이 바뀐다.
public static class PrologueTextStyle
{
    // 글자 하나가 드러나는 데 걸리는 시간(초). 작을수록 빠르다.
    public const float SecondsPerChar = 0.045f;
    // 아주 짧은 문장도 최소 이만큼은 타이핑 모션을 보여준다.
    public const float MinTypeSeconds = 0.25f;
    // 타자 소리를 몇 글자마다 한 번 낼지(매 글자마다 내면 지저분하다).
    public const int BlipEveryChars = 2;

    // 승계 콘솔은 '기계가 빠르게 찍는' 느낌이라 대사보다 훨씬 빠르다.
    public const float ConsoleSecondsPerChar = 0.014f;
    // 줄과 줄 사이 간격.
    public const double ConsoleLineGap = 0.05;
    public const double ConsoleOkGap = 0.30;

    public static float TypeSeconds(string text) =>
        Mathf.Max(MinTypeSeconds, (text?.Length ?? 0) * SecondsPerChar);

    // 경과 시간 → 드러난 글자 비율(0~1).
    public static float Ratio(string text, double elapsedSeconds) =>
        Mathf.Clamp((float)elapsedSeconds / TypeSeconds(text), 0f, 1f);
}
