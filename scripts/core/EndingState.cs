using Godot;

namespace NSP.Core;

// 마지막으로 본 엔딩. 시작 화면(중앙제어실 타이틀)이 "시설이 기억하는 흔적"을 보여 주는 데만 쓴다.
//
//   True : 코어 100% — 복구의 흔적(아침빛 · 정상 표시등 · 조용한 BGM · "새 근무 시작")
//   Bad  : 코어 100% 미달 — 실패의 흔적(붉은 CRT · 느린 비상등 · 기계음 · "복구 실패 기록이 존재합니다.")
//
// 게임을 껐다 켜도 남도록 user:// 에 한 줄로 저장한다. 새 근무를 시작하면 지운다.
// 판정에는 쓰지 않는다 — 타이틀 연출 전용.
public static class EndingState
{
    public enum Kind { None, True, Bad }

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
        if (cfg.Load(Path) == Error.Ok)
            _last = (Kind)(int)cfg.GetValue(Section, "last", 0);
    }
}
