using Godot;

namespace NSP.View;

// 3D 중앙제어실 화면(CRT/배치표/통화 등)들의 기본 텍스트 폰트와 글자 크기 규약.
// 새 View에 텍스트를 추가할 때는 기본값으로 이 폰트를 쓴다 — 엔진 기본(고딕류) 폰트를
// 그대로 두지 않는다.
public static class ViewFont
{
    private static Font _default;
    public static Font Default => _default ??= GD.Load<Font>("res://assets/fonts/BookkMyungjo_Bold.ttf") ?? ThemeDB.FallbackFont;

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
