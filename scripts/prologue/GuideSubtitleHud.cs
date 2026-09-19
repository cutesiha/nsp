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
    private Label _arrow;          // 다음으로 넘길 수 있을 때 오른쪽 끝에서 둥둥 떠다니는 ▶
    private bool _active;
    private float _arrowTime;

    public override void _Ready()
    {
        Instance = this;
        Layer = 85; // 통화 HUD(90) 아래, 시작 화면(80) 위
        BuildUi();
        SetProcess(true);
        Visible = false;
    }

    // ▶ 는 "지금 아무 데나 눌러도 된다"는 표시다 — 넘길 게 있을 때만 뜨고 좌우로 살랑인다.
    public override void _Process(double delta)
    {
        if (!Visible || _arrow == null) return;
        bool canAdvance = GuideHologramView.Instance?.IsWaitingForInput == true;
        _arrow.Visible = canAdvance;
        if (!canAdvance) return;

        _arrowTime += (float)delta;
        _arrow.Position = new Vector2(_arrowBaseX + Mathf.Sin(_arrowTime * 4.2f) * 5f, _arrowBaseY);
        _arrow.Modulate = new Color(1f, 1f, 1f, 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(_arrowTime * 3.4f)));
    }

    private float _arrowBaseX, _arrowBaseY;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void SetActive(bool active)
    {
        _active = active;
        Visible = active && !string.IsNullOrEmpty(_label.Text);
    }

    // 책상 배치표처럼 화면 아래쪽에 눌러야 할 버튼(‘근무 시작’)이 있는 단계에서는
    // 자막 띠를 화면 위로 올린다 — 지시문이 정작 눌러야 할 버튼을 가리지 않게.
    public void SetTopAligned(bool top)
    {
        if (_panel == null) return;
        if (top)
        {
            _panel.AnchorTop = 0f; _panel.AnchorBottom = 0f;
            _panel.OffsetTop = 0f; _panel.OffsetBottom = 70f;
        }
        else
        {
            _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
            _panel.OffsetTop = -132f; _panel.OffsetBottom = -62f;
        }
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
            Size = new Vector2(792f, 38f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", ViewFont.Default);
        _label.AddThemeFontSizeOverride("font_size", ViewFont.FS(17));
        _label.AddThemeColorOverride("font_color", new Color(0.90f, 0.98f, 1f));
        _panel.AddChild(_label);

        _arrowBaseX = 818f;
        _arrowBaseY = 32f;
        _arrow = new Label
        {
            Text = "▶",
            Position = new Vector2(_arrowBaseX, _arrowBaseY),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _arrow.AddThemeFontOverride("font", ViewFont.Default);
        _arrow.AddThemeFontSizeOverride("font_size", ViewFont.FS(18));
        _arrow.AddThemeColorOverride("font_color", new Color(0.62f, 0.98f, 1f));
        _panel.AddChild(_arrow);
    }
}
