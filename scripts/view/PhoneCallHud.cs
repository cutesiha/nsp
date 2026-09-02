using Godot;
using NSP.Core;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 전화 통화 자막 UI. 화면 하단의 작은 홀로그램 '창' 으로만 표시한다 — 중앙 대형 팝업 금지.
//  - 일반 통화(플레이어 발신): 인사 → 질문 → 대답 …
//  - 이벤트 통화(직원 발신: 사고/비명/정전/목격/인터뷰): 첫 대사 → 2지선다 → 대답 → 종료
// 대사 데이터는 DialogueRepository(docs/NSP_DIALOGUE_RUNTIME.md) 하나만 사용한다.
// CanvasLayer 자체는 항상 켜두고 통화창(_panel)만 여닫는다 — 벨이 울리는 동안 아주 작은
// "INCOMING CALL" 보조 표시를 띄우기 위함(직원 이름은 받기 전까지 알려주지 않는다).
public partial class PhoneCallHud : CanvasLayer
{
    [Signal] public delegate void ClosedEventHandler();

    private const double CharDelay = 0.028;
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Amber = new(1f, 0.78f, 0.35f);

    private enum AfterMode { None, GeneralQuestions, EventChoices, EndOnly }

    private Panel _panel;
    private HoloFrame _frame;
    private Label _speaker;
    private Label _message;
    private VBoxContainer _choices;
    private Label _incoming;
    private Font _font;

    private string _employeeId = "";
    private string _dialogueEvent = DialogueRepository.EventGeneralCall;
    private DialogueRepository.EventLine _event;
    private string _fullText = "";
    private double _typeTimer;
    private int _shownChars;
    private bool _typing;
    private AfterMode _after;
    private float _blink;

    public override void _Ready()
    {
        Layer = 90;
        Visible = true;
        _font = ViewFont.Default;

        _panel = new Panel
        {
            AnchorLeft = 0.24f, AnchorRight = 0.76f, AnchorTop = 0.62f, AnchorBottom = 0.95f,
            MouseFilter = Control.MouseFilterEnum.Pass,
            Visible = false,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.09f, 0.11f, 0.82f),
            BorderColor = Cyan with { A = 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 44, ContentMarginBottom = 18,
        });
        AddChild(_panel);

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

        _incoming = new Label
        {
            Text = "● INCOMING CALL",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.9f, AnchorBottom = 0.9f,
            OffsetLeft = -120, OffsetRight = 120, OffsetTop = 0, OffsetBottom = 26,
        };
        _incoming.AddThemeFontOverride("font", _font);
        _incoming.AddThemeFontSizeOverride("font_size", ViewFont.FS(15));
        _incoming.AddThemeColorOverride("font_outline_color", Colors.Black);
        _incoming.AddThemeConstantOverride("outline_size", 4);
        AddChild(_incoming);
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    private SeatedCameraRig Rig => GetViewport()?.GetCamera3D()?.GetParent() as SeatedCameraRig;

    // 벨이 울리는 동안(통화 연결 전) Phone3D 가 호출 — 아주 작은 보조 표시만.
    public void ShowIncoming(Color accent)
    {
        if (_incoming == null) return;
        _incoming.AddThemeColorOverride("font_color", accent.Lerp(Colors.White, 0.2f));
        _incoming.Visible = true;
    }

    public void HideIncoming()
    {
        if (_incoming != null) _incoming.Visible = false;
    }

    public void Open(string employeeId, string dialogueEvent = DialogueRepository.EventGeneralCall)
    {
        _employeeId = employeeId;
        _dialogueEvent = string.IsNullOrEmpty(dialogueEvent) ? DialogueRepository.EventGeneralCall : dialogueEvent;
        HideIncoming();
        _panel.Visible = true;

        var def = FacilitySimulation.Instance?.GetEmployeeDef(employeeId);
        _speaker.Text = "▶ " + (def?.Codename ?? employeeId);
        _frame.Accent = def?.IconColor ?? Cyan;
        ClearChoices();

        if (_dialogueEvent != DialogueRepository.EventGeneralCall)
        {
            _event = DialogueRepository.GetEvent(_dialogueEvent, employeeId);
            if (_event != null && !string.IsNullOrEmpty(_event.Opening))
            {
                StartTyping("\"" + _event.Opening + "\"", AfterMode.EventChoices);
                return;
            }
            _dialogueEvent = DialogueRepository.EventGeneralCall; // 해당 이벤트 대사가 없으면 일반 통화로
        }

        _event = null;
        StartTyping("\"" + DialogueRepository.Greeting(employeeId) + "\"", AfterMode.GeneralQuestions);
    }

    private void StartTyping(string text, AfterMode after)
    {
        Sfx.Instance?.StopVoiceBlip();
        _fullText = text;
        _typeTimer = 0;
        _shownChars = 0;
        _typing = true;
        _after = after;
        _message.Text = text;
        _message.VisibleCharacters = 0;
        // 상대가 말하는 동안 화면이 살짝 끄덕인다.
        Rig?.Speak((float)(text.Length * CharDelay) + 0.2f);
    }

    public override void _Process(double delta)
    {
        _blink += (float)delta;
        if (_incoming != null && _incoming.Visible)
            _incoming.Modulate = new Color(1, 1, 1, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(_blink * 4f)));

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
            switch (_after)
            {
                case AfterMode.GeneralQuestions: BuildGeneralQuestions(); break;
                case AfterMode.EventChoices: BuildEventChoices(); break;
                case AfterMode.EndOnly: BuildEndOnly(); break;
            }
        }
    }

    // --- 일반 통화(플레이어 발신) — 질문/대답 반복 ------------------------
    private void BuildGeneralQuestions()
    {
        ClearChoices();
        var qs = DialogueRepository.GeneralQuestions(_employeeId);
        for (int i = 0; i < qs.Count; i++)
        {
            int idx = i;
            _choices.AddChild(ChoiceButton(qs[i].Question, () => OnGeneralQuestion(idx)));
        }
        _choices.AddChild(ChoiceButton("통화를 종료한다.", CloseCall));
    }

    private void OnGeneralQuestion(int idx)
    {
        ClearChoices();
        StartTyping("\"" + DialogueRepository.GeneralAnswer(_employeeId, idx) + "\"", AfterMode.GeneralQuestions);
    }

    // --- 이벤트 통화(직원 발신) — 첫 대사 → 2지선다 → 대답 → 종료 --------
    private void BuildEventChoices()
    {
        ClearChoices();
        if (_event == null || _event.Choices.Count == 0) { BuildEndOnly(); return; }
        foreach (var c in _event.Choices)
        {
            var choice = c;
            _choices.AddChild(ChoiceButton(choice.Text, () => OnEventChoice(choice)));
        }
    }

    private void OnEventChoice(DialogueRepository.Choice c)
    {
        ClearChoices();
        StartTyping("\"" + c.Reply + "\"", AfterMode.EndOnly);
    }

    private void BuildEndOnly()
    {
        ClearChoices();
        _choices.AddChild(ChoiceButton("통화를 종료한다.", CloseCall));
    }

    private Button ChoiceButton(string text, System.Action onPressed)
    {
        var b = new Button { Text = "  ›  " + text, Alignment = HorizontalAlignment.Left };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", ViewFont.FS(17));
        b.CustomMinimumSize = new Vector2(0, 36);
        b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
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

    private void ClearChoices()
    {
        foreach (var c in _choices.GetChildren()) c.QueueFree();
    }

    private void CloseCall()
    {
        _panel.Visible = false;
        _typing = false;
        Sfx.Instance?.StopVoiceBlip();
        ClearChoices();
        EmitSignal(SignalName.Closed);
    }

    // 전화기(3D)를 직접 클릭해 통화를 끊을 때 — HUD 쪽도 닫는다.
    public void RequestClose()
    {
        if (_panel.Visible) CloseCall();
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
            DrawRect(new Rect2(0, 0, s.X, 30), new Color(a.R, a.G, a.B, 0.18f));
            DrawLine(new Vector2(0, 30), new Vector2(s.X, 30), new Color(a.R, a.G, a.B, 0.6f), 1f);
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
            for (float y = 2; y < s.Y; y += 3f)
                DrawLine(new Vector2(0, y), new Vector2(s.X, y), new Color(0, 0, 0, 0.10f), 1f);
            float ly = Mathf.PosMod(_t * 60f, s.Y);
            DrawLine(new Vector2(0, ly), new Vector2(s.X, ly), new Color(a.R, a.G, a.B, 0.12f), 2f);
        }
    }
}
