using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 전화 통화 자막 UI. 3D 공간이 아니라 읽기 좋은 2D HUD (CanvasLayer). 두 모니터를
// 가리지 않도록 화면 하단에만 뜬다. 대사 데이터는 기존 CallBubble 을 그대로 재사용.
public partial class PhoneCallHud : CanvasLayer
{
    [Signal] public delegate void ClosedEventHandler();

    private const double CharDelay = 0.028;

    private Panel _panel;
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
            AnchorLeft = 0.16f, AnchorRight = 0.84f, AnchorTop = 0.62f, AnchorBottom = 0.97f,
        };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.05f, 0.96f),
            BorderColor = new Color(0.45f, 0.7f, 0.6f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            ContentMarginLeft = 22, ContentMarginRight = 22, ContentMarginTop = 16, ContentMarginBottom = 16,
        });
        AddChild(_panel);

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(vb);

        _speaker = Lbl("", 24, new Color(0.95f, 0.75f, 0.3f));
        vb.AddChild(_speaker);

        _message = Lbl("", 18, new Color(0.85f, 0.92f, 0.85f));
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _message.CustomMinimumSize = new Vector2(0, 70);
        vb.AddChild(_message);

        _choices = new VBoxContainer();
        _choices.AddThemeConstantOverride("separation", 6);
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

    public void Open(string employeeId)
    {
        _employeeId = employeeId;
        Visible = true;
        var def = FacilitySimulation.Instance?.GetEmployeeDef(employeeId);
        _speaker.Text = def?.Codename ?? employeeId;
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
        var b = new Button { Text = "> " + text, Alignment = HorizontalAlignment.Left };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", 17);
        b.CustomMinimumSize = new Vector2(0, 34);
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
}
