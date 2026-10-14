using Godot;

namespace NSP.Core;

// 마지막으로 본 엔딩. 시작 화면(중앙제어실 타이틀)이 "시설이 기억하는 흔적"을 보여 주는 데만 쓴다.
//
// 엔딩은 네 가지다 — 코어 복구(100%) × 최종 보고서의 지목(정답/오답).
//   True  : 복구 O · 지목 O — 복구의 흔적(아침빛 · 정상 표시등 · 조용한 BGM · "새 근무 시작")
//   Loose : 복구 O · 지목 X — 겉으로는 True 와 같다. 신원 카드가 더 자주 깨지는 것만 다르다.
//   Late  : 복구 X · 지목 O — 영구 봉쇄. 어둡고 조용한 방.
//   Bad   : 복구 X · 지목 X — 실패의 흔적(붉은 CRT · 느린 비상등 · 기계음 · "복구 실패 기록이 존재합니다.")
//
// 값은 저장 파일 호환을 위해 True=1 · Bad=2 를 그대로 두고 뒤에 붙인다.
//
// 게임을 껐다 켜도 남도록 user:// 에 한 줄로 저장한다. 새 근무를 시작하면 지운다.
// 판정에는 쓰지 않는다 — 타이틀 연출 전용.
public static class EndingState
{
    public enum Kind { None = 0, True = 1, Bad = 2, Loose = 3, Late = 4 }

    // 코어를 100% 복구하고 끝난 엔딩인가(타이틀의 "복구된 시설" 연출 조건).
    public static bool Recovered(Kind k) => k is Kind.True or Kind.Loose;
    // 결번을 끝내 지목하지 못한 엔딩인가(신원 카드가 계속 깨진다).
    public static bool Missed(Kind k) => k is Kind.Loose or Kind.Bad;

    private const string Path = "user://nsp_progress.cfg";
    private const string Section = "ending";

    private static bool _loaded;
    private static Kind _last = Kind.None;

    // 엔딩 → 결과 화면 → [타이틀로] 직후, 다시 뜬 타이틀이 "눈을 뜨는" 연출을 한 번 할지.
    // 씬을 다시 불러와도 살아 있도록 static 으로 둔다(파일에는 쓰지 않는다).
    public static Kind PendingWake { get; set; } = Kind.None;

    public static Kind Last
    {
        get { Load(); return _last; }
    }

    public static void Record(Kind kind)
    {
        Load();
        _last = kind;
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "last", (int)kind);
        cfg.Save(Path);
    }

    public static void Clear() => Record(Kind.None);

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        // 예전 저장 파일(True=1 / Bad=2)도 그대로 읽힌다. 모르는 값이면 None 으로 둔다.
        int raw = (int)cfg.GetValue(Section, "last", 0);
        _last = System.Enum.IsDefined(typeof(Kind), raw) ? (Kind)raw : Kind.None;
    }
}
