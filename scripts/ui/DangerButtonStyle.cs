using Godot;

namespace NSP.Ui;

public static class DangerButtonStyle
{
    public static void Apply(Button button)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.72f, 0.12f, 0.12f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            BorderColor = new Color(0.15f, 0.15f, 0.15f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 10, ContentMarginTop = 6, ContentMarginRight = 10, ContentMarginBottom = 6,
        };
        var hover = new StyleBoxFlat
        {
            BgColor = new Color(0.84f, 0.18f, 0.18f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            BorderColor = new Color(0.15f, 0.15f, 0.15f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 10, ContentMarginTop = 6, ContentMarginRight = 10, ContentMarginBottom = 6,
        };
        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("focus", normal);
        button.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        button.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f));
        button.AddThemeColorOverride("font_pressed_color", new Color(1f, 1f, 1f));
    }
}
