using Godot;

namespace NSP.View;

// 3D 중앙제어실 화면(CRT/배치표/통화 등)들의 기본 텍스트 폰트와 글자 크기 규약.
// 새 View에 텍스트를 추가할 때는 기본값으로 이 폰트를 쓴다 — 엔진 기본(고딕류) 폰트를
// 그대로 두지 않는다.
public static class ViewFont
{
    private static Font _default;
    public static Font Default => _default ??= GD.Load<Font>("res://assets/fonts/BookkMyungjo_Bold.ttf") ?? ThemeDB.FallbackFont;

    // 하루가 시작될 때 뜨는 「DAY N」 표제(ShiftCardView) 전용 — KIMM 볼드.
    private static Font _dayTitle;
    public static Font DayTitle => _dayTitle ??=
        (ResourceLoader.Exists("res://assets/fonts/KIMM_bold.ttf") ? GD.Load<Font>("res://assets/fonts/KIMM_bold.ttf") : null) ?? Default;

    // ── 표정 기호(☺︎ ☹︎) ──────────────────────────────────────────────
    //
    // 대본의 ☺︎ · ☹︎ 는 글자로 그려져야 한다. 그런데 프로젝트 글꼴 넷 다 이 글자가 없어서,
    // 엔진이 윈도의 컬러 이모지 글꼴(Segoe UI Emoji)로 대신 그려 노랗고 둥근 그림이 되었다
    // (글자 뒤의 U+FE0E "글자로 그려라" 표시도 무시된다).
    // 흑백 기호가 든 시스템 글꼴을 각 글꼴의 **첫 대체 글꼴**로 붙인다 — 엔진의 시스템 대체(이모지)보다
    // 먼저 찾으므로 기호가 글자색 그대로 그려진다. 한글 · 영문은 원래 글꼴이 그대로 그린다.
    private static readonly string[] ProjectFonts =
    {
        "res://assets/fonts/BookkMyungjo_Bold.ttf", "res://assets/fonts/BookkMyungjo_Light.ttf",
        "res://assets/fonts/KMU80TTFSungkokSerif.ttf", "res://assets/fonts/NalSun-Regular.ttf",
    };

    // 게임을 켤 때 한 번(Config autoload). 불러 둔 글꼴 리소스는 엔진이 캐시해 같은 것을 돌려주므로
    // 여기서 붙인 대체 글꼴이 테마 · ViewFont · 직접 불러 쓰는 곳 모두에 걸린다.
    public static void InstallSymbolFallback()
    {
        var symbols = new SystemFont
        {
            FontNames = new[] { "Segoe UI Symbol", "Apple Symbols", "DejaVu Sans", "Noto Sans Symbols" },
        };
        foreach (string path in ProjectFonts)
        {
            if (!ResourceLoader.Exists(path) || GD.Load<Font>(path) is not FontFile font) continue;
            // 붙잡아 둔다 — 아무도 쓰지 않는 사이 캐시에서 풀리면 다음에 대체 글꼴 없이 다시 읽힌다.
            Held.Add(font);
            if (font.Fallbacks.Count > 0) continue;
            font.Fallbacks = new Godot.Collections.Array<Font> { symbols };
        }
    }

    private static readonly System.Collections.Generic.List<Font> Held = new();

    // ── 글자 크기 일괄 배율 ────────────────────────────────────────────────
    // 게임 화면의 모든 글자 크기를 여기 한 곳에서 키우고 줄인다.
    // (타이틀 화면 TitleOverlay / TitleScreen 은 자체 크기를 쓰므로 이 배율을 적용하지 않는다.)
    // 레이아웃은 논리 캔버스 좌표로 절대 배치돼 있으므로, 이 값을 크게 올리면 글자가
    // 칸 밖으로 넘칠 수 있다 — 올릴 때는 배치표/모니터 화면을 같이 확인할 것.
    public const float TextScale = 1.22f;

    // SubViewport 안(논리 캔버스) UI 의 글자 크기. 원래 쓰던 px 값을 그대로 넣으면 된다.
    public static int S(int px) => Mathf.Max(1, Mathf.RoundToInt(px * TextScale));

    // 화면 위(CanvasLayer)에 직접 뜨는 UI — SubViewport 스케일 프레임을 못 쓰는 곳 — 의
    // 글자 크기를 1920x1080 화면에 맞춰 같은 배율로 키운다. (SubViewport UI 는
    // ControlRoom3DController.AddScaledView 가 통째로 스케일하므로 여기 안 씀.)
    public static int FS(int px) => Mathf.RoundToInt(px * TextScale * ControlRoom3DController.UiScale);
}
