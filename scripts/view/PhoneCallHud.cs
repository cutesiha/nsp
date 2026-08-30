using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 전화 통화 자막 UI. 2D 팝업이 아니라 중앙제어실 모니터에 뜨는 홀로그램 '창' 느낌으로 표시한다.
// 대사 데이터는 기존 CallBubble 을 그대로 재사용. 대답이 타이핑되는 동안 화면이 살짝 말하는
// 것처럼 끄덕인다(SeatedCameraRig.Speak).
public partial class PhoneCallHud : CanvasLayer
{
    [Signal] public delegate void ClosedEventHandler();

    private const double CharDelay = 0.028;
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color CyanDim = new(0.4f, 0.7f, 0.78f);
    private static readonly Color Amber = new(1f, 0.78f, 0.35f);

    private Panel _panel;
    private HoloFrame _frame;
    private Label _speaker;
    private Label _message;
    private VBoxContainer _choices;
    private Font _font;

    private string _employeeId = "";
    private string _fullText = "";
    private double _typeTimer;
    private int _shownChars;
    private bool _typing;
    private bool _showChoicesAfter;

    public override void _Ready()
    {
        Layer = 90;
        Visible = false;
        _font = ViewFont.Default;

        _panel = new Panel
        {
            AnchorLeft = 0.22f, AnchorRight = 0.78f, AnchorTop = 0.58f, AnchorBottom = 0.95f,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.09f, 0.11f, 0.82f),
            BorderColor = Cyan with { A = 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 44, ContentMarginBottom = 18,
        });
        AddChild(_panel);

        // 홀로그램 프레임(모서리 브래킷 + 스캔라인 + 헤더바) 를 패널 위에 그린다.
        _frame = new HoloFrame { MouseFilter = Control.MouseFilterEnum.Ignore };
        _frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _panel.AddChild(_frame);

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(vb);

        _speaker = Lbl("", 22, Amber);
        vb.AddChild(_speaker);

        _message = Lbl("", 19, new Color(0.82f, 0.96f, 0.98f));
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _message.CustomMinimumSize = new Vector2(0, 74);
        vb.AddChild(_message);

        _choices = new VBoxContainer();
        _choices.AddThemeConstantOverride("separation", 7);
        vb.AddChild(_choices);
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    private SeatedCameraRig Rig => GetViewport()?.GetCamera3D()?.GetParent() as SeatedCameraRig;

    public void Open(string employeeId)
    {
        _employeeId = employeeId;
        Visible = true;
        var def = FacilitySimulation.Instance?.GetEmployeeDef(employeeId);
        _speaker.Text = "▶ " + (def?.Codename ?? employeeId);
        _frame.Accent = def?.IconColor ?? Cyan;
        ClearChoices();
        StartTyping("\"" + CallBubble.GreetingFor(employeeId) + "\"", showChoicesAfter: true);
    }

    private void StartTyping(string text, bool showChoicesAfter)
    {
        Sfx.Instance?.StopVoiceBlip();
        _fullText = text;
        _typeTimer = 0;
        _shownChars = 0;
        _typing = true;
        _showChoicesAfter = showChoicesAfter;
        _message.Text = "";
        _message.VisibleCharacters = 0;
        _message.Text = text;
        // 상대가 말하는 동안 화면이 살짝 끄덕인다.
        Rig?.Speak((float)(text.Length * CharDelay) + 0.2f);
    }

    public override void _Process(double delta)
    {
        if (!_typing) return;
        _typeTimer += delta;
        int shown = Mathf.Min(_fullText.Length, (int)(_typeTimer / CharDelay));
        if (shown != _shownChars)
        {
            for (int i = _shownChars; i < shown; i++)
                Sfx.Instance?.PlayVoiceBlip(_employeeId, _fullText[i]);
            _shownChars = shown;
        }
        _message.VisibleCharacters = shown;
        if (shown >= _fullText.Length)
        {
            _typing = false;
            if (_showChoicesAfter) BuildChoices();
        }
    }

    private void BuildChoices()
    {
        ClearChoices();
        foreach (var key in CallBubble.PickQuestionKeys(2))
        {
            string k = key;
            _choices.AddChild(ChoiceButton(CallBubble.QuestionLabel(k), () => OnQuestion(k)));
        }
        _choices.AddChild(ChoiceButton("통화를 종료한다.", CloseCall));
    }

    private Button ChoiceButton(string text, System.Action onPressed)
    {
        var b = new Button { Text = "  ›  " + text, Alignment = HorizontalAlignment.Left };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", 17);
        b.CustomMinimumSize = new Vector2(0, 36);
        b.AddThemeColorOverride("font_color", Cyan);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.16f, 0.19f, 0.55f),
            BorderColor = Cyan with { A = 0.4f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.14f, 0.32f, 0.36f, 0.7f);
        hover.BorderColor = Cyan;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += () => onPressed();
        return b;
    }

    private void OnQuestion(string key)
    {
        ClearChoices();
        StartTyping("\"" + CallBubble.AnswerFor(_employeeId, key) + "\"", showChoicesAfter: true);
    }

    private void ClearChoices()
    {
        foreach (var c in _choices.GetChildren()) c.QueueFree();
    }

    private void CloseCall()
    {
        Visible = false;
        _typing = false;
        Sfx.Instance?.StopVoiceBlip();
        ClearChoices();
        EmitSignal(SignalName.Closed);
    }

    // 전화기(3D)를 직접 클릭해 통화를 끊을 때 — HUD 쪽도 닫는다.
    public void RequestClose()
    {
        if (Visible) CloseCall();
    }

    // ── 홀로그램 창 프레임(모서리 브래킷 + 헤더바 + 스캔라인) ──────────────
    private partial class HoloFrame : Control
    {
        public Color Accent = Cyan;
        private float _t;

        public override void _Process(double delta) { _t += (float)delta; QueueRedraw(); }

        public override void _Draw()
        {
            var a = Accent;
            var s = Size;
            // 헤더바
            DrawRect(new Rect2(0, 0, s.X, 30), new Color(a.R, a.G, a.B, 0.18f));
            DrawLine(new Vector2(0, 30), new Vector2(s.X, 30), new Color(a.R, a.G, a.B, 0.6f), 1f);
            // 모서리 브래킷
            float L = 18f;
            var c = new Color(a.R, a.G, a.B, 0.9f);
            void Bracket(Vector2 p, Vector2 dx, Vector2 dy)
            {
                DrawLine(p, p + dx * L, c, 2f);
                DrawLine(p, p + dy * L, c, 2f);
            }
            Bracket(new Vector2(2, 2), Vector2.Right, Vector2.Down);
            Bracket(new Vector2(s.X - 2, 2), Vector2.Left, Vector2.Down);
            Bracket(new Vector2(2, s.Y - 2), Vector2.Right, Vector2.Up);
            Bracket(new Vector2(s.X - 2, s.Y - 2), Vector2.Left, Vector2.Up);
            // 스캔라인
            for (float y = 2; y < s.Y; y += 3f)
                DrawLine(new Vector2(0, y), new Vector2(s.X, y), new Color(0, 0, 0, 0.10f), 1f);
            // 흐르는 밝은 라인
            float ly = Mathf.PosMod(_t * 60f, s.Y);
            DrawLine(new Vector2(0, ly), new Vector2(s.X, ly), new Color(a.R, a.G, a.B, 0.12f), 2f);
        }
    }
}
