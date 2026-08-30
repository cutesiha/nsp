using Godot;

namespace NSP.View;

// 3D 중앙제어실 화면(CRT/배치표/통화 등)들의 기본 텍스트 폰트.
// 새 View에 텍스트를 추가할 때는 기본값으로 이 폰트를 쓴다 — 엔진 기본(고딕류) 폰트를
// 그대로 두지 않는다.
public static class ViewFont
{
    private static Font _default;
    public static Font Default => _default ??= GD.Load<Font>("res://assets/fonts/BookkMyungjo_Bold.ttf") ?? ThemeDB.FallbackFont;
}
