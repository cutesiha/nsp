using Godot;

namespace NSP.View;

// 3D 중앙제어실 화면(CRT/배치표/통화 등)들의 기본 텍스트 폰트.
// 새 View에 텍스트를 추가할 때는 기본값으로 이 폰트를 쓴다 — 엔진 기본(고딕류) 폰트를
// 그대로 두지 않는다.
public static class ViewFont
{
    private static Font _default;
    public static Font Default => _default ??= GD.Load<Font>("res://assets/fonts/BookkMyungjo_Bold.ttf") ?? ThemeDB.FallbackFont;

    // 화면 위(CanvasLayer)에 직접 뜨는 UI — SubViewport 스케일 프레임을 못 쓰는 곳 — 의
    // 글자 크기를 1920x1080 화면에 맞춰 같은 배율로 키운다. (SubViewport UI 는
    // ControlRoom3DController.AddScaledView 가 통째로 스케일하므로 여기 안 씀.)
    public static int FS(int px) => Mathf.RoundToInt(px * ControlRoom3DController.UiScale);
}
