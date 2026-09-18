using Godot;
using NSP.View;

namespace NSP.Prologue;

// GUIDE-0 자막 띠.
//
// GUIDE-0 의 본체는 어디까지나 오른쪽 CRT 의 홀로그램 창이다. 다만 근무 배치(책상을 내려다봄)나
// 휴게시간(오른쪽 CRT 가 인터뷰 화면) 처럼 그 모니터가 화면에 없는 단계가 있어서, 그동안에도
// 지시를 놓치지 않도록 같은 문장을 화면 아래에 한 줄로 띄운다.
// 프롤로그처럼 홀로그램이 정면에 보이는 단계에서는 꺼 둔다(SetActive(false)).
public partial class GuideSubtitleHud : CanvasLayer
{
    public static GuideSubtitleHud Instance { get; private set; }

    private Panel _panel;
    private Label _label;
    private bool _active;

    public override void _Ready()
    {
        Instance = this;
        Layer = 85; // 통화 HUD(90) 아래, 시작 화면(80) 위
        BuildUi();
        Visible = false;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void SetActive(bool active)
    {
        _active = active;
        Visible = active && !string.IsNullOrEmpty(_label.Text);
    }

    public void SetLine(string text)
    {
        _label.Text = text ?? "";
        Visible = _active && !string.IsNullOrEmpty(_label.Text);
    }

    public void Clear() => SetLine("");

    private void BuildUi()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _panel = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        _panel.AnchorLeft = 0.5f; _panel.AnchorRight = 0.5f;
        _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
        _panel.OffsetLeft = -430f; _panel.OffsetRight = 430f;
        _panel.OffsetTop = -132f; _panel.OffsetBottom = -62f;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.10f, 0.12f, 0.88f),
            BorderColor = new Color(0.55f, 0.95f, 1f, 0.7f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        root.AddChild(_panel);

        var tag = new Label { Text = "GUIDE-0", Position = new Vector2(16f, 8f), MouseFilter = Control.MouseFilterEnum.Ignore };
        tag.AddThemeFontOverride("font", ViewFont.Default);
        tag.AddThemeFontSizeOverride("font_size", ViewFont.FS(12));
        tag.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 1f));
        _panel.AddChild(tag);

        _label = new Label
        {
            Position = new Vector2(16f, 26f),
            Size = new Vector2(828f, 38f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", ViewFont.Default);
        _label.AddThemeFontSizeOverride("font_size", ViewFont.FS(17));
        _label.AddThemeColorOverride("font_color", new Color(0.90f, 0.98f, 1f));
        _panel.AddChild(_label);
    }
}
