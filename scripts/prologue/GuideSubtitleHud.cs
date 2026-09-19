using System;
using System.Collections.Generic;
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
    // 선택지 한 줄의 높이와 간격. 선택지가 뜨면 블록이 그만큼 아래로 자란다.
    private const float ChoiceHeight = 40f;
    private const float ChoiceGap = 6f;
    private const float ChoiceTop = 104f;

    // 화면 아래 자막 블록에 띄울 선택지 하나.
    public sealed class Choice
    {
        public string Text = "";
        public bool Disabled;
        public Action OnPick;
    }

    private Panel _panel;
    private Label _label;
    private Label _arrow;          // 다음으로 넘길 수 있을 때 오른쪽 끝에서 둥둥 떠다니는 ▶
    private VBoxContainer _choices;
    private bool _active;
    private bool _topAligned;
    private float _arrowTime;

    public override void _Ready()
    {
        Instance = this;
        // 화면 UI 는 분위기 오버레이(AmbientOverlay = 100)보다 항상 위다.
        // 아래에 두면 비네트·노이즈가 대사창까지 덮어 글자가 어두워진다.
        Layer = 112;
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

        bool canAdvance = guide?.IsWaitingForInput == true && _label.VisibleRatio >= 1f
                          && _choices.GetChildCount() == 0;
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
        _topAligned = top;
        Layout();
    }

    // 선택지 수에 맞춰 블록 높이를 다시 잡는다.
    private void Layout()
    {
        if (_panel == null) return;
        int n = _choices?.GetChildCount() ?? 0;
        float h = n == 0 ? PanelHeight : ChoiceTop + n * ChoiceHeight + (n - 1) * ChoiceGap + 14f;

        if (_topAligned)
        {
            _panel.AnchorTop = 0f; _panel.AnchorBottom = 0f;
            _panel.OffsetTop = 0f; _panel.OffsetBottom = h;
        }
        else
        {
            _panel.AnchorTop = 1f; _panel.AnchorBottom = 1f;
            _panel.OffsetTop = -h - 44f; _panel.OffsetBottom = -44f;
        }

        if (_choices != null)
            _choices.Size = new Vector2(PanelHalfWidth * 2f - 48f, h - ChoiceTop - 14f);
        _arrowBaseY = h - 44f;
        if (_arrow != null) _arrow.Position = new Vector2(_arrowBaseX, _arrowBaseY);
    }

    // --- 선택지 ---------------------------------------------------------

    // GUIDE-0 의 질문 선택지를 대사 바로 아래에 띄운다.
    // 홀로그램 창(오른쪽 CRT)이 아니라 여기에 뜨는 이유는, 플레이어가 읽고 있는 자리가
    // 이 자막 블록이기 때문이다 — 대사와 선택지가 떨어져 있으면 시선이 두 번 움직인다.
    public void ShowChoices(IReadOnlyList<Choice> choices)
    {
        ClearChoices();
        if (choices == null || choices.Count == 0) { Layout(); return; }

        foreach (var c in choices)
        {
            var captured = c;
            // 이미 고른 선택지도 같은 색으로 둔다 — 어둡게 하면 글씨를 읽을 수 없다.
            var accent = new Color(0.55f, 0.95f, 1f);
            var b = MonitorUi.Button(c.Text, accent, ViewFont.Default,
                () => captured.OnPick?.Invoke(), ViewFont.FS(15));
            b.AddThemeColorOverride("font_disabled_color", accent);
            b.Alignment = HorizontalAlignment.Left;
            b.Disabled = c.Disabled;
            b.CustomMinimumSize = new Vector2(0f, ChoiceHeight);
            b.MouseFilter = Control.MouseFilterEnum.Stop;
            _choices.AddChild(b);
        }
        _choices.Visible = true;
        Visible = _active;
        Layout();
    }

    public void ClearChoices()
    {
        if (_choices == null) return;
        foreach (Node c in _choices.GetChildren()) { _choices.RemoveChild(c); c.QueueFree(); }
        _choices.Visible = false;
        Layout();
    }

    public bool HasChoices => _choices != null && _choices.GetChildCount() > 0;
    public bool IsActive => _active;

    public void SetLine(string text)
    {
        _label.Text = text ?? "";
        // 새 문장은 0 에서 시작해 홀로그램 창과 같은 속도로 드러난다.
        _label.VisibleRatio = string.IsNullOrEmpty(_label.Text) ? 1f : 0f;
        Visible = _active && (!string.IsNullOrEmpty(_label.Text) || HasChoices);
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
        _label.AddThemeFontSizeOverride("font_size", ViewFont.FS(16));
        _label.AddThemeColorOverride("font_color", new Color(0.90f, 0.98f, 1f));
        _panel.AddChild(_label);

        _choices = new VBoxContainer
        {
            Position = new Vector2(24f, ChoiceTop),
            Size = new Vector2(PanelHalfWidth * 2f - 48f, 0f),
            MouseFilter = Control.MouseFilterEnum.Pass,
            Visible = false,
        };
        _choices.AddThemeConstantOverride("separation", (int)ChoiceGap);
        _panel.AddChild(_choices);

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
