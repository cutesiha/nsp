using Godot;

namespace NSP.Core;

// 이번 판을 어떤 규칙으로 도는가. 타이틀의 「근무 개시」에서 한 번 고르고, 그 판이
// 끝날 때까지 바뀌지 않는다.
public enum GameMode
{
    Competition,   // 대회용 — 3일 근무 · 결번 개체 고정
    Standard,      // 기본   — 5일 근무 · 결번 개체 무작위
    Hard,          // 하드   — 아직 없다(자리만)
}

// 모드가 바꾸는 것은 **두 가지뿐**이다: 며칠 도는가, 결번을 누가 맡는가.
// 코어 수율 · 사고 확률 · 직원 능력치 같은 밸런스는 모드와 무관하게 그대로다.
//
// 값을 autoload 가 아니라 static 으로 두는 이유는 EndingState 와 같다 —
// 타이틀로 돌아가며 씬을 다시 불러도 살아남아야 하고, 저장 파일에는 남지 않는다.
public static class GameModes
{
    // ── 잠금 플래그 (대회 빌드에서 여기 한 줄만 바꾼다) ─────────────────
    // 대회 제출본에서 기본 모드를 막으려면 StandardUnlocked = false.
    public const bool StandardUnlocked = true;
    // 하드 모드는 규칙 자체가 아직 없다. 해금 로직이 붙을 때까지 항상 false.
    public const bool HardUnlocked = false;

    // 대회용에서 반드시 결번이 되는 직원. 화면 · 로그 · 대사 어디에도 드러나지 않는다.
    public const string FixedSaboteurId = "dog";

    public static GameMode Current { get; private set; } = GameMode.Standard;

    // 타이틀에서 새 게임을 시작할 때만 부른다. 진행 중에는 바뀌지 않는다.
    public static void Select(GameMode mode)
    {
        if (!IsPlayable(mode)) return;
        Current = mode;
    }

    // 검사 · 개발 도구용 — 잠금을 무시하고 바로 세운다.
    public static void ForceSelect(GameMode mode) => Current = mode;

    public static bool IsPlayable(GameMode mode) => mode switch
    {
        GameMode.Standard => StandardUnlocked,
        GameMode.Hard => HardUnlocked,
        _ => true,
    };

    // 그 모드가 도는 실제 근무 일수. 숫자는 데이터(config.tres)에 있다.
    public static int DaysFor(GameMode mode)
    {
        var cfg = Config.Instance?.Data;
        return mode == GameMode.Competition
            ? Mathf.Max(1, cfg?.CompetitionDays ?? 3)
            : Mathf.Max(1, cfg?.MaxDays ?? 5);
    }

    // 이번 판의 마지막 근무일. 기존 Config.MaxDays 를 읽던 자리가 전부 이걸로 바뀐다.
    public static int MaxDays => DaysFor(Current);

    // 결번을 무작위로 뽑는가, 정해진 직원으로 고정하는가.
    public static bool FixedSaboteur => Current == GameMode.Competition;

    // 마지막 근무일에도 휴게시간(심문)을 거치는가.
    //
    // 기본 5일은 예전 그대로다 — DAY5 는 근무 보고서에서 곧장 최종 격리 보고서로 간다.
    // 대회용 3일만 DAY3 뒤에 휴게시간을 한 번 더 두고, 그 휴게시간이 끝나야 보고서로
    // 넘어간다(사흘치 진술만으로 지목하기에는 물어볼 기회가 너무 적다).
    public static bool FinalDayRest => Current == GameMode.Competition;

    public static string DisplayName(GameMode mode) => mode switch
    {
        GameMode.Competition => "대회용 VER",
        GameMode.Hard => "하드 모드",
        _ => "기본 모드",
    };
}
