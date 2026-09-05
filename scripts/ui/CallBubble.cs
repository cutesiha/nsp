using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Dialogue;

namespace NSP.Ui;

// 통화 연결 말풍선 (2D 백업 화면 전용 — 메인은 3D PhoneCallHud).
//  - 대사는 DialogueRepository(docs/NSP_DIALOGUE_RUNTIME.md) 의 일반 통화(⑦) 데이터만 사용한다.
//  - 매 통화마다 질문 2개를 무작위로 뽑는다(항상 같은 2개가 아님).
//  - 플레이어 선택지 말풍선은 전화기 위쪽에 뜨고, 아래로 향하는 꼭지가 달려 말풍선처럼 보인다.
public partial class CallBubble : Control
{
    public static CallBubble Instance { get; private set; }

    private const double SecondsPerChar = 0.032;
    private const double ResponseHoldSeconds = 1.9;
    private const int ChoicesPerCall = 2;

    private PanelContainer _characterBubble;
    private Label _characterLabel;
    private PanelContainer _choiceBubble;
    private VBoxContainer _choiceBox;
    private Polygon2D _tail;

    private Control _targetIcon;
    private string _employeeId = "";
    private string _fullText = "";
    private int _typedChars;
    private double _typeTimer;
    private bool _typing;
    private System.Action _onTypeDone;
    private readonly RandomNumberGenerator _rng = new();
    private IReadOnlyList<DialogueRepository.QA> _qa = System.Array.Empty<DialogueRepository.QA>();

    public override void _Ready()
    {
        Instance = this;
        Visible = false;

        _characterBubble = GetNode<PanelContainer>("CharacterBubble");
        _characterLabel = GetNode<Label>("CharacterBubble/Label");
        _choiceBubble = GetNode<PanelContainer>("PlayerChoiceBubble");
        _choiceBox = GetNode<VBoxContainer>("PlayerChoiceBubble/VBox");

        // 아래로 향하는 말풍선 꼭지 — 선택지 박스 바닥 가운데(전화기 쪽). 색은 카드 배경색(크림).
        _tail = new Polygon2D
        {
            Color = new Color(0.99f, 0.98f, 0.94f, 1f),
            Polygon = new[] { new Vector2(-20f, -2f), new Vector2(20f, -2f), new Vector2(0f, 24f) },
            Visible = false,
        };
        var tailBorder = new Line2D
        {
            Points = new[] { new Vector2(-20f, -2f), new Vector2(0f, 24f), new Vector2(20f, -2f) },
            Width = 2f,
            DefaultColor = new Color(0.15f, 0.15f, 0.15f, 1f),
        };
        _tail.AddChild(tailBorder);
        AddChild(_tail);
    }

    public void StartCall(string employeeId, Control targetIcon)
    {
        _employeeId = employeeId;
        _targetIcon = targetIcon;
        _qa = DialogueRepository.GeneralQuestions(employeeId);
        Visible = true;
        _characterBubble.Visible = true;
        _choiceBubble.Visible = false;
        _tail.Visible = false;

        PositionCharacterBubble();
        StartTyping(DialogueRepository.Greeting(employeeId), ShowChoices);
    }

    private void ShowChoices()
    {
        foreach (Node child in _choiceBox.GetChildren())
            child.QueueFree();

        var picked = PickQuestionIndices();
        if (picked.Count == 0) { EndCallSoon(); return; }
        foreach (int idx in picked)
        {
            int qi = idx;
            var button = new Button { Text = _qa[qi].Question };
            button.AddThemeFontSizeOverride("font_size", 16);
            button.CustomMinimumSize = new Vector2(0, 44);
            button.Pressed += () => OnChoicePicked(qi);
            _choiceBox.AddChild(button);
        }

        _choiceBubble.Visible = true;
        _tail.Visible = true;
        CallDeferred(nameof(PlaceTail));
    }

    private void PlaceTail()
    {
        // 꼭지를 선택지 박스 바닥 가운데(전화기 쪽)로. _tail과 _choiceBubble 모두 CallLayer 자식이라 좌표계 동일.
        _tail.Position = _choiceBubble.Position + new Vector2(_choiceBubble.Size.X / 2f, _choiceBubble.Size.Y);
    }

    private List<int> PickQuestionIndices()
    {
        var pool = new List<int>();
        for (int i = 0; i < _qa.Count; i++) pool.Add(i);
        var picked = new List<int>();
        for (int i = 0; i < ChoicesPerCall && pool.Count > 0; i++)
        {
            int p = _rng.RandiRange(0, pool.Count - 1);
            picked.Add(pool[p]);
            pool.RemoveAt(p);
        }
        return picked;
    }

    private void OnChoicePicked(int questionIndex)
    {
        _choiceBubble.Visible = false;
        _tail.Visible = false;
        // 대답은 현재 근무 상태에서 생성한다(3D 전화기와 같은 파이프라인).
        StartTyping(LocalDialogueGenerator.GeneralAnswer(_employeeId, questionIndex), EndCallSoon);
    }

    private async void EndCallSoon()
    {
        await ToSignal(GetTree().CreateTimer(ResponseHoldSeconds), SceneTreeTimer.SignalName.Timeout);
        Sfx.Instance?.StopVoiceBlip();
        Visible = false;
        _characterBubble.Visible = false;
        _choiceBubble.Visible = false;
        _tail.Visible = false;
        _targetIcon = null;
    }

    private void StartTyping(string text, System.Action onDone)
    {
        Sfx.Instance?.StopVoiceBlip();
        _fullText = text;
        _typedChars = 0;
        _typeTimer = 0;
        _typing = true;
        _onTypeDone = onDone;
        _characterLabel.Text = "";
    }

    private void PositionCharacterBubble()
    {
        if (_targetIcon != null && IsInstanceValid(_targetIcon))
            _characterBubble.GlobalPosition = _targetIcon.GlobalPosition + new Vector2(-150f, -120f);
    }

    public override void _Process(double delta)
    {
        if (_typing)
        {
            _typeTimer += delta;
            int shouldShow = Mathf.Min(_fullText.Length, (int)(_typeTimer / SecondsPerChar));
            if (shouldShow != _typedChars)
            {
                for (int i = _typedChars; i < shouldShow; i++)
                    Sfx.Instance?.PlayVoiceBlip(_employeeId, _fullText[i]);
                _typedChars = shouldShow;
                _characterLabel.Text = _fullText[.._typedChars];
            }
            if (_typedChars >= _fullText.Length)
            {
                _typing = false;
                _onTypeDone?.Invoke();
            }
        }

        if (Visible && _characterBubble.Visible)
            PositionCharacterBubble();
    }
}
