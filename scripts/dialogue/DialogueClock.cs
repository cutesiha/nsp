using Godot;
using NSP.Core;

namespace NSP.Dialogue;

// 근무 시계 변환의 단일 창구.
//
// 근무는 0초에 시작해 Config.DayLengthSeconds 동안 이어지고, 그 사이 게임 안에서는
// 22:00 부터 04:00 까지 6시간(360분)이 흐른다. 이 환산을 여러 곳에 흩어 두면
// 대사에 뜨는 시각과 조사 자료 카드에 뜨는 시각이 서로 달라진다.
//
// 플레이어에게 보이는 시각은 24시간제 숫자("00:45")가 아니라 한글 시간대로 쓴다.
//   21:00~23:59 → "밤 9시 20분" · "밤 11시 15분"
//   00:00 이후  → "새벽 12시" · "새벽 12시 45분" · "새벽 3시 13분"
// 이 규칙은 표시 문자열에만 적용된다. 판정(AnchorTime · 모순 비교 · WindowMinutes)은
// 지금처럼 초/분 숫자로만 한다.
public static class DialogueClock
{
    public const int StartHour = 22;
    public const int ShiftMinutes = 360;   // 22:00 → 04:00

    // 실제 1초가 게임 안에서 몇 분인가.
    public static float MinutesPerSecond =>
        ShiftMinutes / Mathf.Max(1f, Config.Instance?.Data?.DayLengthSeconds ?? 180f);

    public static float SecondsPerMinute => 1f / Mathf.Max(0.0001f, MinutesPerSecond);

    // 게임 안의 분 단위로 환산한 경과 시간.
    public static int MinutesAt(float seconds) => Mathf.FloorToInt(Mathf.Max(0f, seconds) * MinutesPerSecond);

    private static (int Hour, int Minute) HourMinute(float seconds)
    {
        int total = StartHour * 60 + MinutesAt(seconds);
        return ((total / 60) % 24, total % 60);
    }

    // 24시간제 시·분 → 한글 시간대 표기. 근무는 밤~새벽뿐이라 두 구간만 쓴다.
    //   (21, 20) → "밤 9시 20분" / (0, 0) → "새벽 12시" / (1, 10) → "새벽 1시 10분"
    public static string Format(int hour24, int minute)
    {
        int h = ((hour24 % 24) + 24) % 24;
        string band = h >= 12 ? "밤" : "새벽";
        int h12 = h % 12 == 0 ? 12 : h % 12;
        return minute == 0 ? $"{band} {h12}시" : $"{band} {h12}시 {minute}분";
    }

    // 화면 표기(조사 자료 카드 · 로그 · CCTV · 모니터 시계) — "밤 10시 13분".
    public static string Text(float seconds)
    {
        var (h, m) = HourMinute(seconds);
        return Format(h, m);
    }

    // 대사 표기 — 화면 표기와 같은 문자열을 쓴다(자료와 대사의 시각 표현이 어긋나지 않게).
    public static string Spoken(float seconds) => Text(seconds);

    // 시간 범위 — "새벽 12시 42분 ~ 새벽 12시 48분". 같은 분이면 한 번만 쓴다.
    public static string Range(float fromSeconds, float toSeconds)
    {
        string a = Text(fromSeconds), b = Text(toSeconds);
        return a == b ? a : $"{a} ~ {b}";
    }
}
