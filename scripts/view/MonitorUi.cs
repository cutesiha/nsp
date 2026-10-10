using Godot;

namespace NSP.View;

// 3D 중앙제어실 모니터(SubViewport) 위에 뜨는 UI 공통 스타일.
// 2D 시절의 기본 Button 대신, 화면에 뜬 '단말기 버튼'처럼 보이게 한다 — 얇은 발광 테두리 +
// 코너 마크 + hover 시 밝아짐.
public static class MonitorUi
{
    // 모니터 버튼. accent 색으로 테두리/글자, 배경은 그 색의 어두운 버전.
    public static Button Button(string text, Color accent, Font font, System.Action onPressed, int fontSize = 18)
    {
        var b = new Button { Text = text, Alignment = HorizontalAlignment.Center };
        if (font != null) b.AddThemeFontOverride("font", font);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.AddThemeColorOverride("font_color", accent);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        b.AddThemeColorOverride("font_focus_color", accent);
        // 고를 수 없는 버튼(이미 확인한 질문 등)의 글자색. 살짝만 죽인다 —
        // 여기서 많이 어둡게 하면 그대로 안 보이는 글자가 된다.
        b.AddThemeColorOverride("font_disabled_color", accent.Lerp(new Color(0.62f, 0.72f, 0.75f), 0.35f));

        var normal = new StyleBoxFlat
        {
            BgColor = new Color(accent.R * 0.16f, accent.G * 0.16f, accent.B * 0.16f, 0.6f),
            BorderColor = accent with { A = 0.5f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(accent.R * 0.34f, accent.G * 0.34f, accent.B * 0.34f, 0.8f);
        hover.BorderColor = accent;
        var pressed = (StyleBoxFlat)hover.Duplicate();
        pressed.BgColor = new Color(accent.R * 0.5f, accent.G * 0.5f, accent.B * 0.5f, 0.9f);

        // 비활성 배경. 이걸 안 주면 엔진 기본 '밝은 회색 판'이 깔려서
        // 어두운 화면 위에 흰 덩어리가 생기고 글자가 통째로 묻힌다.
        var disabled = (StyleBoxFlat)normal.Duplicate();
        disabled.BgColor = new Color(accent.R * 0.10f, accent.G * 0.10f, accent.B * 0.10f, 0.45f);
        disabled.BorderColor = accent with { A = 0.22f };

        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", pressed);
        b.AddThemeStyleboxOverride("disabled", disabled);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += () => onPressed?.Invoke();
        return b;
    }
}
