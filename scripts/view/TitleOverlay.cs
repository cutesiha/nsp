using Godot;

namespace NSP.View;

// 시작 화면. 별도 2D 씬이 아니라 3D 중앙제어실 위에 얹히는 최소 UI.
// 어두운 제어실을 배경으로 제목과 메뉴만 보여주고, "근무 시작" 시 페이드 아웃한다.
// (제어실을 어둡게 두는 연출은 NSP_3D_REWORK_PLAN §3 의 의도. 배치표/보고서 등
//  실제 업무 화면은 크림색을 유지한다.)
public partial class TitleOverlay : CanvasLayer
{
    [Signal] public delegate void StartRequestedEventHandler();
    [Signal] public delegate void QuitRequestedEventHandler();

    private Control _root;
    private VBoxContainer _menu;
    private Label _banner;
    private ColorRect _fadeRect;

    public override void _Ready()
    {
        Layer = 80;
        BuildUI();
        Visible = false;
    }

    public void ShowTitle()
    {
        Visible = true;
        _root.Visible = true;
        _root.Modulate = Colors.White;
    }

    public void FadeOut()
    {
        _root.MouseFilter = Control.MouseFilterEnum.Ignore;
        if (_menu != null) { _menu.Visible = false; _menu.MouseFilter = Control.MouseFilterEnum.Ignore; }
        var t = CreateTween();
        t.TweenProperty(_root, "modulate:a", 0f, 0.5);
        t.TweenCallback(Callable.From(() => _root.Visible = false));
    }

    // 근무 부팅 순간 잠깐 뜨는 "NIGHT SHIFT START" 배너.
    public void FlashBanner(string text)
    {
        _banner.Text = text;
        _banner.Visible = true;
        _banner.Modulate = new Color(1f, 1f, 1f, 0f);
        var t = CreateTween();
        t.TweenProperty(_banner, "modulate:a", 1f, 0.18);
        t.TweenInterval(0.7);
        t.TweenProperty(_banner, "modulate:a", 0f, 0.35);
        t.TweenCallback(Callable.From(() => _banner.Visible = false));
    }

    // 근무/휴게 사이의 짧은 암전(다음 날로 넘어갈 때 등) — 제목 UI와는 무관한 별도 레이어.
    public void FadeToBlack(float duration = 0.5f)
    {
        _fadeRect.MouseFilter = Control.MouseFilterEnum.Stop;
        var t = CreateTween();
        t.TweenProperty(_fadeRect, "color:a", 1f, duration);
    }

    public void FadeFromBlack(float duration = 0.5f)
    {
        var t = CreateTween();
        t.TweenProperty(_fadeRect, "color:a", 0f, duration);
        t.TweenCallback(Callable.From(() => _fadeRect.MouseFilter = Control.MouseFilterEnum.Ignore));
    }

    // --- UI ---------------------------------------------------------

    private void BuildUI()
    {
        var serif = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf") ?? ThemeDB.FallbackFont;
        var body = GD.Load<Font>("res://assets/fonts/BookkMyungjo_Bold.ttf") ?? ThemeDB.FallbackFont;

        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(0.015f, 0.017f, 0.022f, 0.62f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(dim);

        var title = new Label
        {
            Text = "야간근무지침",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        title.AnchorLeft = 0f; title.AnchorRight = 1f;
        title.OffsetTop = 190f; title.OffsetLeft = 0f; title.OffsetRight = 0f;
        title.AddThemeFontOverride("font", serif);
        title.AddThemeFontSizeOverride("font_size", ViewFont.FS(68));
        title.AddThemeColorOverride("font_color", new Color(0.86f, 0.84f, 0.78f));
        title.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));
        title.AddThemeConstantOverride("outline_size", 6);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(title);

        var sub = new Label
        {
            Text = "N I G H T   S H I F T   P R O T O C O L",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        sub.AnchorLeft = 0f; sub.AnchorRight = 1f;
        sub.OffsetTop = 276f;
        sub.AddThemeFontOverride("font", body);
        sub.AddThemeFontSizeOverride("font_size", ViewFont.FS(18));
        sub.AddThemeColorOverride("font_color", new Color(0.55f, 0.58f, 0.62f));
        sub.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(sub);

        _menu = new VBoxContainer();
        _menu.AnchorLeft = 0.5f; _menu.AnchorRight = 0.5f;
        _menu.AnchorTop = 0.5f; _menu.AnchorBottom = 0.5f;
        _menu.OffsetLeft = -130f; _menu.OffsetRight = 130f; _menu.OffsetTop = 20f;
        _menu.AddThemeConstantOverride("separation", 10);
        _root.AddChild(_menu);

        _menu.AddChild(MenuButton("근무 시작", body, true, () => EmitSignal(SignalName.StartRequested)));
        _menu.AddChild(MenuButton("이어하기", body, false, null));
        _menu.AddChild(MenuButton("설정", body, false, null));
        _menu.AddChild(MenuButton("종료", body, true, () => EmitSignal(SignalName.QuitRequested)));

        _banner = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _banner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _banner.AddThemeFontOverride("font", body);
        _banner.AddThemeFontSizeOverride("font_size", ViewFont.FS(40));
        _banner.AddThemeColorOverride("font_color", new Color(0.9f, 0.92f, 0.95f));
        _banner.AddThemeColorOverride("font_outline_color", Colors.Black);
        _banner.AddThemeConstantOverride("outline_size", 8);
        AddChild(_banner);

        _fadeRect = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_fadeRect);
    }

    private Button MenuButton(string text, Font font, bool enabled, System.Action onPressed)
    {
        var b = new Button
        {
            Text = text,
            Disabled = !enabled,
            CustomMinimumSize = new Vector2(260f, 44f),
            Flat = true,
        };
        b.AddThemeFontOverride("font", font);
        b.AddThemeFontSizeOverride("font_size", ViewFont.FS(20));
        b.AddThemeColorOverride("font_color", new Color(0.82f, 0.83f, 0.85f));
        b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.8f));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.4f, 0.4f, 0.44f));
        var hover = new StyleBoxFlat { BgColor = new Color(0.9f, 0.85f, 0.6f, 0.14f) };
        b.AddThemeStyleboxOverride("hover", hover);
        if (onPressed != null) b.Pressed += () => onPressed();
        return b;
    }
}
