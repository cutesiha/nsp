using Godot;
using NSP.View;

namespace NSP.Prologue;

// GUIDE-0 자막 띠.
//
// 오른쪽 CRT 가 화면에 없거나(근무 배치=책상, 휴게=인터뷰 화면) 대사를 그 창에서 뺀
// 압축 모드일 때, 같은 문장을 화면 아래에 띄운다.
//
// 글자는 홀로그램 창과 '같은 진행도'로 드러난다 — 타이핑 타이밍과 보이스는
// GuideHologramView 가 한 곳에서 굴리고, 여기는 그 VisibleRatio 를 그대로 따라간다.
// (여기서 따로 타이핑하면 소리가 두 번 나고 속도도 어긋난다.)
public partial class GuideSubtitleHud : CanvasLayer
{
    public static GuideSubtitleHud Instance { get; private set; }

    // 자막 블록 크기 — 대사가 길어도 두 줄 이상 편하게 들어가게 넉넉히 잡는다.
    private const float PanelHalfWidth = 560f;
    private const float PanelHeight = 112f;

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

        // 홀로그램 창이 찍고 있는 만큼만 여기도 드러낸다(보이스도 그쪽이 울린다).
        var guide = GuideHologramView.Instance;
        if (guide != null && guide.CurrentLineText == _label.Text)
            _label.VisibleRatio = guide.CurrentLineRatio;
        else
            _label.VisibleRatio = 1f;

        bool canAdvance = guide?.IsWaitingForInput == true && _label.VisibleRatio >= 1f;
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
            _panel.OffsetTop = 0f; _panel.OffsetBottom = PanelHeight;
        }
        else
        {
            _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
            _panel.OffsetTop = -PanelHeight - 44f; _panel.OffsetBottom = -44f;
        }
    }

    public void SetLine(string text)
    {
        _label.Text = text ?? "";
        // 새 문장은 0 에서 시작해 홀로그램 창과 같은 속도로 드러난다.
        _label.VisibleRatio = string.IsNullOrEmpty(_label.Text) ? 1f : 0f;
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
        _panel.OffsetLeft = -PanelHalfWidth; _panel.OffsetRight = PanelHalfWidth;
        _panel.OffsetTop = -PanelHeight - 44f; _panel.OffsetBottom = -44f;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.10f, 0.12f, 0.88f),
            BorderColor = new Color(0.55f, 0.95f, 1f, 0.7f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        root.AddChild(_panel);

        var tag = new Label { Text = "GUIDE-0", Position = new Vector2(24f, 12f), MouseFilter = Control.MouseFilterEnum.Ignore };
        tag.AddThemeFontOverride("font", ViewFont.Default);
        tag.AddThemeFontSizeOverride("font_size", ViewFont.FS(12));
        tag.AddThemeColorOverride("font_color", new Color(0.55f, 0.95f, 1f));
        _panel.AddChild(tag);

        _label = new Label
        {
            Position = new Vector2(24f, 38f),
            Size = new Vector2(PanelHalfWidth * 2f - 82f, PanelHeight - 50f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", ViewFont.Default);
        _label.AddThemeFontSizeOverride("font_size", ViewFont.FS(15));
        _label.AddThemeColorOverride("font_color", new Color(0.90f, 0.98f, 1f));
        _panel.AddChild(_label);

        _arrowBaseX = PanelHalfWidth * 2f - 44f;
        _arrowBaseY = PanelHeight - 44f;
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
