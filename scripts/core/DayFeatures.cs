using NSP.Data;

namespace NSP.Core;

// V3 초반 단순화 — 첫날에는 "시설 로그와 증언으로 방해자를 찾는다"는 핵심만 남기고
// 능력치 / 스트레스 / 금기 / 일부 작업실을 잠가 둔다.
//
// 중요: 해당 시스템의 코드와 데이터는 그대로 남아 있다. 여기서 하는 일은 "오늘 이 시스템이
// 동작하는가"를 한 곳에서 판정하는 것뿐이고, 해금 날짜는 전부 ConfigData 의 *UnlockDay
// 값(작업실은 RoomDef.UnlockDay)에서만 바꾼다. 값을 1 로 내리면 DAY1 부터 예전처럼 켜진다.
public static class DayFeatures
{
    // 능력치가 잠긴 동안 모든 직원에게 적용되는 값 = "보통". Config 의 배율표(index 2)를
    // 그대로 쓰므로 새 밸런스 상수를 만들지 않는다.
    public const int NeutralStatValue = 2;

    private static int Day => GameState.Instance?.CurrentDay ?? 1;
    private static ConfigData Cfg => Config.Instance?.Data;

    public static bool StatsEnabled => Day >= (Cfg?.StatsUnlockDay ?? 2);
    public static bool StressEnabled => Day >= (Cfg?.StressUnlockDay ?? 2);
    public static bool TaboosEnabled => Day >= (Cfg?.TabooUnlockDay ?? 2);

    // 능력치가 잠겨 있으면 실제 수치 대신 "보통"으로 읽는다.
    public static int EffectiveStat(int rawValue) => StatsEnabled ? rawValue : NeutralStatValue;

    // 오늘 이 작업실을 쓰는가(배치 / 업무 발생 / 무인 사고 / 지도 표시 판정의 단일 창구).
    public static bool IsRoomActive(RoomDef def) => def == null || Day >= def.UnlockDay;
}
