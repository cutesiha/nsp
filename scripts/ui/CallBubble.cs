using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.Ui;

// 통화 연결 말풍선.
//  - 인사말은 캐릭터별로 다르다.
//  - 매 통화마다 선택지 2개를 풀에서 무작위로 뽑는다(항상 같은 2개가 아님).
//  - 각 선택지에 대한 대답은 캐릭터 4명(여우/까마귀/토끼/고양이)의 성격·말투대로 다르게 나온다.
//    대사는 data/employees/*.tres 의 성격/말투/예시를 참고해 임시로 작성한 것.
//  - 플레이어 선택지 말풍선은 전화기 위쪽에 뜨고, 아래로 향하는 꼭지가 달려 말풍선처럼 보인다.
public partial class CallBubble : Control
{
    public static CallBubble Instance { get; private set; }

    private const double SecondsPerChar = 0.032;
    private const double ResponseHoldSeconds = 1.9;
    private const int ChoicesPerCall = 2;

    private static readonly string[] QuestionKeys = { "progress", "hurry", "anomaly", "health", "others", "stay" };

    private static readonly Dictionary<string, string> QuestionText = new()
    {
        ["progress"] = "작업은 잘 되어가나요?",
        ["hurry"] = "지금 좀 더 집중해 주세요.",
        ["anomaly"] = "주변에 이상한 건 없었습니까?",
        ["health"] = "몸 상태는 좀 어때요?",
        ["others"] = "다른 직원 최근에 본 적 있어요?",
        ["stay"] = "지금 자리 지키고 있는 거죠?",
    };

    // employeeId -> (인사말, questionKey -> 대답)
    private static readonly Dictionary<string, (string Greeting, Dictionary<string, string> Answers)> Lines = new()
    {
        ["fox"] = ("네, 여우입니다. 무슨 일이세요?", new()
        {
            ["progress"] = "그럭저럭요. 저한테 맡기셨으면 걱정 안 하셔도 됩니다, 관리자님.",
            ["hurry"] = "재촉 안 하셔도 되는데… 알겠어요, 서두르죠.",
            ["anomaly"] = "이상한 거라면… 관리자님이 자꾸 저만 확인하시는 거? 농담입니다.",
            ["health"] = "저야 늘 멀쩡하죠. 이 정도로 지치면 여기서 못 버팁니다.",
            ["others"] = "아까 고양이 씨가 꽤 바쁘게 어디론가 가던데요.",
            ["stay"] = "네네, 얌전히 있습니다. 딴 데 갈 이유도 없고요.",
        }),
        ["crow"] = ("…까마귀입니다. 말씀하세요.", new()
        {
            ["progress"] = "진행 중입니다. 문제 없습니다.",
            ["hurry"] = "알겠습니다. 속도 올리죠.",
            ["anomaly"] = "확실한 건 없습니다. 다만 아까 복도 쪽 소리가 평소와 달랐습니다.",
            ["health"] = "괜찮습니다.",
            ["others"] = "봤습니다. 토끼가 정비실 쪽으로 갔습니다. 22시쯤.",
            ["stay"] = "예. 이탈하지 않았습니다.",
        }),
        ["rabbit"] = ("네, 네! 토끼예요. 무, 무슨 일이세요?", new()
        {
            ["progress"] = "아, 네… 하고는 있는데, 제, 제가 잘 하고 있는 건지…",
            ["hurry"] = "죄, 죄송해요. 더 빨리… 해볼게요.",
            ["anomaly"] = "그, 그게… 뭔가 지나간 것 같기도 하고… 아, 아닐 수도 있어요.",
            ["health"] = "조, 조금 어지럽긴 한데… 괜찮아요, 아마.",
            ["others"] = "저, 저기… 아까 까마귀 씨가 근처에 있었던 것 같아요.",
            ["stay"] = "네! 여, 여기 계속 있었어요. 안 움직였어요.",
        }),
        ["cat"] = ("…네. 무슨 일인데요?", new()
        {
            ["progress"] = "하고 있어요. 굳이 확인 안 하셔도 되는데요.",
            ["hurry"] = "…듣고 있어요. 하면 되잖아요.",
            ["anomaly"] = "딱히요. 있었으면 먼저 말했겠죠.",
            ["health"] = "멀쩡해요. 그쪽이나 신경 쓰세요.",
            ["others"] = "토끼 씨요. 아까부터 상태 안 좋아 보이던데… 아니, 신경 쓰인다는 건 아니고요.",
            ["stay"] = "네. 안 움직였어요. 못 믿으시겠으면 CCTV 보시든가요.",
        }),
    };

    private const string FallbackGreeting = "네, 무슨 일이세요?";
    private static readonly Dictionary<string, string> FallbackAnswers = new()
    {
        ["progress"] = "진행 중입니다.",
        ["hurry"] = "…알겠습니다.",
        ["anomaly"] = "특별한 건 없었습니다.",
        ["health"] = "괜찮습니다.",
        ["others"] = "글쎄요, 잘 모르겠습니다.",
        ["stay"] = "네, 자리 지키고 있습니다.",
    };

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
        Visible = true;
        _characterBubble.Visible = true;
        _choiceBubble.Visible = false;
        _tail.Visible = false;

        PositionCharacterBubble();
        StartTyping(Greeting(employeeId), ShowChoices);
    }

    private void ShowChoices()
    {
        foreach (Node child in _choiceBox.GetChildren())
            child.QueueFree();

        foreach (var key in PickQuestions())
        {
            string qKey = key;
            var button = new Button { Text = QuestionText[qKey] };
            button.AddThemeFontSizeOverride("font_size", 16);
            button.CustomMinimumSize = new Vector2(0, 44);
            button.Pressed += () => OnChoicePicked(qKey);
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

    private List<string> PickQuestions()
    {
        var pool = QuestionKeys.ToList();
        var picked = new List<string>();
        for (int i = 0; i < ChoicesPerCall && pool.Count > 0; i++)
        {
            int idx = _rng.RandiRange(0, pool.Count - 1);
            picked.Add(pool[idx]);
            pool.RemoveAt(idx);
        }
        return picked;
    }

    private void OnChoicePicked(string questionKey)
    {
        _choiceBubble.Visible = false;
        _tail.Visible = false;
        StartTyping(Answer(_employeeId, questionKey), EndCallSoon);
    }

    private static string Greeting(string employeeId) =>
        Lines.TryGetValue(employeeId, out var l) ? l.Greeting : FallbackGreeting;

    private static string Answer(string employeeId, string questionKey)
    {
        if (Lines.TryGetValue(employeeId, out var l) && l.Answers.TryGetValue(questionKey, out var a))
            return a;
        return FallbackAnswers.GetValueOrDefault(questionKey, "…알겠습니다.");
    }

    private async void EndCallSoon()
    {
        await ToSignal(GetTree().CreateTimer(ResponseHoldSeconds), SceneTreeTimer.SignalName.Timeout);
        Visible = false;
        _characterBubble.Visible = false;
        _choiceBubble.Visible = false;
        _tail.Visible = false;
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
