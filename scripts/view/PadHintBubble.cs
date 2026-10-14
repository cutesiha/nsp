using Godot;

namespace NSP.View;

// 손에 든 패드 바로 위에 잠깐 떴다 사라지는 한 줄 안내.
//
// 대사창(GuideSubtitleHud)과 같은 생김새를 쓰되, 화면 아래 자막 띠가 아니라 패드를 가리키는
// 말풍선이다 — 아래에서 위로 살짝 튀어오르고, 잠시 머문 뒤 다시 아래로 내려가며 사라진다.
//
// 자리는 매 프레임 패드 화면의 위쪽 가운데를 다시 잡는다. 패드는 들어 올려지는 중에도 움직이므로
// 한 번 잡아 두면 안내만 허공에 남는다. 패드가 없으면 화면 가운데 위쪽으로 떨어진다.
public partial class PadHintBubble : CanvasLayer
{
    public static PadHintBubble Instance { get; private set; }

    // 패드 위 모서리에서 이만큼 띄운다(px, 1080 기준).
    private const float Gap = 26f;
    // 튀어오르기 전 아래로 내려가 있는 거리(px).
    private const float DropPx = 46f;
    private const float PanelWidth = 700f;
    private const float PanelHeight = 66f;

    private Panel _panel;
    private Label _label;
    private float _slide = DropPx;   // 0 = 제자리, 양수 = 아래
    private Tween _tween;

    public override void _Ready()
    {
        Instance = this;
        // 대사창(112) · 단서 토스트(116) 보다 위 — 패드를 가리키는 안내가 가장 앞이다.
        Layer = 117;
        Build();
        Visible = false;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void Build()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        _panel = new Panel
        {
            Size = new Vector2(PanelWidth, PanelHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.10f, 0.12f, 0.92f),
            BorderColor = new Color(0.55f, 0.95f, 1f, 0.75f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        root.AddChild(_panel);

        _label = new Label
        {
            Position = new Vector2(22f, 0f),
            Size = new Vector2(PanelWidth - 44f, PanelHeight),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", ViewFont.Default);
        _label.AddThemeFontSizeOverride("font_size", ViewFont.FS(19));
        _label.AddThemeColorOverride("font_color", new Color(0.90f, 0.98f, 1f));
        _panel.AddChild(_label);
    }

    // 패드 위에 한 줄 띄운다 — 뽀잉 올라와 holdSeconds 만큼 머물고 다시 내려간다.
    public static void Show(string text, float holdSeconds = 2f) =>
        Instance?.Play(text, holdSeconds);

    public void Play(string text, float holdSeconds = 2f)
    {
        if (string.IsNullOrEmpty(text)) return;
        _label.Text = text;
        Visible = true;
        _slide = DropPx;
        _panel.Modulate = Colors.White with { A = 0f };
        Place();

        // 단계 사이는 반드시 Chain() 으로 끊는다 — 병렬 모드에서 그냥 이어 붙이면 머무는 동안
        // 사라지는 연출이 같이 돌아 버려서, 뜨자마자 사라진다.
        _tween?.Kill();
        _tween = CreateTween();
        // ① 아래에서 위로 — 살짝 지나쳤다 제자리로(뽀잉).
        _tween.SetParallel(true);
        _tween.TweenMethod(Callable.From<float>(v => _slide = v), DropPx, 0f, 0.34)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _tween.TweenProperty(_panel, "modulate:a", 1f, 0.18);
        // ② 머문다.
        _tween.Chain().TweenInterval(Mathf.Max(0.1f, holdSeconds));
        // ③ 다시 아래로 내려가며 사라진다.
        _tween.Chain().TweenMethod(Callable.From<float>(v => _slide = v), 0f, DropPx, 0.28)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        _tween.TweenProperty(_panel, "modulate:a", 0f, 0.24);
        _tween.Chain().TweenCallback(Callable.From(() => Visible = false));
    }

    public void HideNow()
    {
        _tween?.Kill();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (Visible) Place();
    }

    // 패드 화면의 위쪽 가운데 바로 위에 말풍선을 놓는다.
    private void Place()
    {
        var vp = GetViewport();
        if (vp == null) return;
        Vector2 screen = vp.GetVisibleRect().Size;
        Vector2 anchor = new(screen.X * 0.5f, screen.Y * 0.42f);

        var pad = AdminPad3D.Instance;
        var cam = vp.GetCamera3D();
        if (pad?.TargetViewport != null && cam != null && pad.IsOpen)
        {
            var top = pad.ScreenPointWorld(new Vector2(pad.TargetViewport.Size.X * 0.5f, 0f));
            if (!cam.IsPositionBehind(top)) anchor = cam.UnprojectPosition(top);
        }

        float w = _panel.Size.X, h = _panel.Size.Y;
        float x = Mathf.Clamp(anchor.X - w * 0.5f, 12f, Mathf.Max(12f, screen.X - w - 12f));
        float y = Mathf.Clamp(anchor.Y - h - Gap + _slide, 12f, Mathf.Max(12f, screen.Y - h - 12f));
        _panel.Position = new Vector2(x, y);
    }
}
