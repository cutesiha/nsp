using Godot;

namespace NSP.Ui;

public partial class CallBubble : Control
{
    public static CallBubble Instance { get; private set; }

    private const double SecondsPerChar = 0.035;
    private const double ResponseHoldSeconds = 1.6;

    private static readonly (string Choice, string Response)[] Script =
    {
        ("작업은 잘 되어가나요?", "네, 별문제 없습니다."),
        ("잘 좀 해보세요.", "...알겠습니다."),
    };
    private const string Greeting = "무슨 일이세요?";

    private PanelContainer _characterBubble;
    private Label _characterLabel;
    private PanelContainer _choiceBubble;
    private VBoxContainer _choiceBox;

    private Control _targetIcon;
    private string _fullText = "";
    private int _typedChars;
    private double _typeTimer;
    private bool _typing;
    private System.Action _onTypeDone;

    public override void _Ready()
    {
        Instance = this;
        Visible = false;

        _characterBubble = GetNode<PanelContainer>("CharacterBubble");
        _characterLabel = GetNode<Label>("CharacterBubble/Label");
        _choiceBubble = GetNode<PanelContainer>("PlayerChoiceBubble");
        _choiceBox = GetNode<VBoxContainer>("PlayerChoiceBubble/VBox");
    }

    public void StartCall(string employeeId, Control targetIcon)
    {
        _targetIcon = targetIcon;
        Visible = true;
        _characterBubble.Visible = true;
        _choiceBubble.Visible = false;

        if (_targetIcon != null)
            _characterBubble.GlobalPosition = _targetIcon.GlobalPosition + new Vector2(-70, -74);

        StartTyping(Greeting, ShowChoices);
    }

    private void ShowChoices()
    {
        foreach (Node child in _choiceBox.GetChildren())
            child.QueueFree();

        foreach (var (choice, response) in Script)
        {
            var button = new Button { Text = choice };
            button.Pressed += () => OnChoicePicked(response);
            _choiceBox.AddChild(button);
        }
        _choiceBubble.Visible = true;
    }

    private void OnChoicePicked(string response)
    {
        _choiceBubble.Visible = false;
        StartTyping(response, EndCallSoon);
    }

    private async void EndCallSoon()
    {
        await ToSignal(GetTree().CreateTimer(ResponseHoldSeconds), SceneTreeTimer.SignalName.Timeout);
        Visible = false;
        _characterBubble.Visible = false;
        _choiceBubble.Visible = false;
        _targetIcon = null;
    }

    private void StartTyping(string text, System.Action onDone)
    {
        _fullText = text;
        _typedChars = 0;
        _typeTimer = 0;
        _typing = true;
        _onTypeDone = onDone;
        _characterLabel.Text = "";
    }

    public override void _Process(double delta)
    {
        if (_typing)
        {
            _typeTimer += delta;
            int shouldShow = Mathf.Min(_fullText.Length, (int)(_typeTimer / SecondsPerChar));
            if (shouldShow != _typedChars)
            {
                _typedChars = shouldShow;
                _characterLabel.Text = _fullText[.._typedChars];
            }
            if (_typedChars >= _fullText.Length)
            {
                _typing = false;
                _onTypeDone?.Invoke();
            }
        }

        if (Visible && _targetIcon != null && IsInstanceValid(_targetIcon))
            _characterBubble.GlobalPosition = _targetIcon.GlobalPosition + new Vector2(-70, -74);
    }
}
