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
    private const string UnlockSection = "unlock";

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
        // 단기 근무를 끝까지 마쳤다(엔딩 넷 중 무엇이든) — 정규(장기) 근무가 열린다.
        if (kind != Kind.None && GameModes.Current == GameMode.Competition) _shortCleared = true;
        Save();
    }

    // ── 정규 근무 해금 ───────────────────────────────────────────────
    //
    // 단기 근무를 한 번 끝까지 마치면(엔딩을 보면) 타이틀의 「정규 근무」가 열린다(2026-10-31 사용자 요청).
    // 엔딩 기록과 같은 파일에 남지만 **새 근무를 시작해도 지우지 않는다**(Clear 는 엔딩만 비운다).
    // 에디터에서는 엔딩 기록과 똑같이 F5 마다 처음 상태(잠김)로 돌아간다 — 아래 Load 참고.
    private static bool _shortCleared;

    public static bool ShortShiftCleared
    {
        get { Load(); return _shortCleared; }
    }

    private static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "last", (int)_last);
        cfg.SetValue(UnlockSection, "short_cleared", _shortCleared);
        cfg.Save(Path);
    }

    public static void Clear() => Record(Kind.None);

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        // **에디터에서 돌릴 때는 지난 판의 엔딩을 들고 오지 않는다.**
        //
        // F5 는 "처음부터 해 보는" 버튼이다. 그런데 한 번 배드엔딩을 보고 나면 그 뒤로
        // 켤 때마다 타이틀이 붉은 CRT · "복구 실패 기록이 존재합니다" 로 떴다 — 매번
        // 그 판의 뒤끝에서 시작하는 셈이라, 처음 켠 사람이 보는 화면을 확인할 수가 없다.
        // 도전과제를 F5 마다 비우는 것과 같은 이유다(AchievementManager).
        //
        // **내보낸 빌드에는 해당하지 않는다** — 플레이어에게는 시설이 지난 밤을
        // 기억하는 것이 이 타이틀의 전부다. 지우는 것은 에디터에서 돌릴 때뿐이다.
        //
        // 이번 판에서 엔딩을 본 뒤 타이틀로 돌아가는 길은 그대로다 — 아래에서 파일만
        // 비우고 _loaded 를 세워 두므로, 그 뒤의 Record() 는 평소처럼 기록된다.
        if (OS.HasFeature("editor"))
        {
            _last = Kind.None;
            PendingWake = Kind.None;
            if (FileAccess.FileExists(Path)) DirAccess.RemoveAbsolute(Path);
            return;
        }

        var cfg = new ConfigFile();
        if (cfg.Load(Path) != Error.Ok) return;
        // 예전 저장 파일(True=1 / Bad=2)도 그대로 읽힌다. 모르는 값이면 None 으로 둔다.
        int raw = (int)cfg.GetValue(Section, "last", 0);
        _last = System.Enum.IsDefined(typeof(Kind), raw) ? (Kind)raw : Kind.None;
        _shortCleared = (bool)cfg.GetValue(UnlockSection, "short_cleared", false);
    }
}
