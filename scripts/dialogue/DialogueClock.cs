using Godot;
using NSP.Core;

namespace NSP.Dialogue;

// 근무 시계 변환의 단일 창구.
//
// 근무는 0초에 시작해 Config.DayLengthSeconds 동안 이어지고, 그 사이 게임 안에서는
// 22:00 부터 04:00 까지 6시간(360분)이 흐른다. 이 환산을 여러 곳에 흩어 두면
// 대사에 뜨는 시각과 조사 자료 카드에 뜨는 시각이 서로 달라진다.
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

    // 화면 표기 — "22:13".
    public static string Text(float seconds)
    {
        var (h, m) = HourMinute(seconds);
        return $"{h:00}:{m:00}";
    }

    // 대사 표기 — "22시 13분" / 정각이면 "22시".
    public static string Spoken(float seconds)
    {
        var (h, m) = HourMinute(seconds);
        return m == 0 ? $"{h}시" : $"{h}시 {m}분";
    }
}
