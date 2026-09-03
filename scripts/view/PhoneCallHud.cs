using Godot;
using NSP.Core;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 전화 통화 자막 UI. 화면 하단의 작은 홀로그램 '창' 으로만 표시한다 — 중앙 대형 팝업 금지.
//  - 일반 통화(플레이어 발신): 인사 → 질문 → 대답 …
//  - 이벤트 통화(직원 발신: 사고/비명/정전/목격/인터뷰): 첫 대사 → 2지선다 → 대답 → 종료
// 일반/이벤트 통화는 DialogueRepository를, DAY1 휴게 인터뷰는 LocalInterviewDialogue
// (docs/휴게시간_대사목록.md + 실제 이벤트 로그)를 사용한다.
// CanvasLayer 자체는 항상 켜두고 통화창(_panel)만 여닫는다 — 벨이 울리는 동안 아주 작은
// "INCOMING CALL" 보조 표시를 띄우기 위함(직원 이름은 받기 전까지 알려주지 않는다).
public partial class PhoneCallHud : CanvasLayer
{
    [Signal] public delegate void ClosedEventHandler();
    // 이벤트 통화에서 플레이어가 고른 선택지. index 0 = 원본 대사 목록의 첫 선택지
    // (사고/비명 이벤트에서는 "확인하러 가주세요" 계열). IncomingCallDirector 가 받아
    // 실제 이동/후속 전화를 처리한다 — HUD 는 판정하지 않는다.
    [Signal] public delegate void EventChoiceMadeEventHandler(string employeeId, string dialogueEvent, int choiceIndex);

    public static PhoneCallHud Instance { get; private set; }

    private const double CharDelay = 0.028;
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Amber = new(1f, 0.78f, 0.35f);

    private enum AfterMode { None, GeneralQuestions, EventChoices, LocalInterviewQuestions, EndOnly }

    private Panel _panel;
    private HologramFrame _frame;
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

    // 3D CRT 입력기가 통화창 뒤의 버튼까지 같은 마우스 입력을 전달하지 않도록,
    // 열려 있는 통화창의 상태를 외부에 명시한다.
    public bool IsOpen => _panel?.Visible ?? false;

    public override void _Ready()
    {
        Instance = this;
        Layer = 90;
        Visible = true;
        _font = ViewFont.Default;

        _panel = new Panel
        {
            AnchorLeft = 0.24f, AnchorRight = 0.76f, AnchorTop = 0.62f, AnchorBottom = 0.95f,
            // 통화 선택지 클릭은 이 HUD에서 끝나야 한다. Pass이면 같은 클릭이 뒤쪽
            // 휴게시간 CRT의 "다음 날 근무 배치" 버튼까지 전달될 수 있다.
            MouseFilter = Control.MouseFilterEnum.Stop,
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

        _frame = new HologramFrame { MouseFilter = Control.MouseFilterEnum.Ignore };
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

    // 어두운 고유색(까마귀의 회색 등)은 통화창의 검은 배경에서 안 보이므로 최소 밝기까지만 올린다.
    // 색상(hue)은 건드리지 않아 "그 직원의 색"으로 계속 읽힌다.
    private static Color Readable(Color c)
    {
        float lum = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        const float min = 0.55f;
        return lum >= min ? c : c.Lerp(Colors.White, (min - lum) / Mathf.Max(0.001f, 1f - lum));
    }

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
        SetInterviewLayout(_dialogueEvent == LocalInterviewDialogue.EventDay1Interview);

        var def = FacilitySimulation.Instance?.GetEmployeeDef(employeeId);
        _speaker.Text = "▶ " + (def?.Codename ?? employeeId);
        _frame.Accent = def?.IconColor ?? Cyan;

        // 이름은 그 직원의 고유색 그대로(어두운 색만 살짝 띄워 가독성 확보),
        // 대사는 같은 색에 흰색을 많이 섞어 읽기 편한 밝은 톤으로.
        Color own = def?.IconColor ?? Cyan;
        _speaker.AddThemeColorOverride("font_color", Readable(own));
        _message.AddThemeColorOverride("font_color", Readable(own).Lerp(Colors.White, 0.62f));

        ClearChoices();

        if (_dialogueEvent == LocalInterviewDialogue.EventDay1Interview)
        {
            // 휴게시간 인터뷰는 Claude/API가 아니라 로컬 로그 기반 대사로 완결한다.
            string greeting = LocalInterviewDialogue.InterviewGreeting(employeeId);
            RecordNpc(greeting, DialogueEntryType.NpcLine, DialogueConversationType.Interview);
            StartTyping("\"" + greeting + "\"", AfterMode.LocalInterviewQuestions);
            return;
        }

        if (_dialogueEvent != DialogueRepository.EventGeneralCall)
        {
            _event = DialogueRepository.GetEvent(_dialogueEvent, employeeId);
            if (_event != null && !string.IsNullOrEmpty(_event.Opening))
            {
                RecordNpc(_event.Opening, DialogueEntryType.NpcLine, DialogueConversationType.IncomingCall);
                StartTyping("\"" + _event.Opening + "\"", AfterMode.EventChoices);
                return;
            }
            _dialogueEvent = DialogueRepository.EventGeneralCall; // 해당 이벤트 대사가 없으면 일반 통화로
        }

        _event = null;
        string generalGreeting = DialogueRepository.Greeting(employeeId);
        RecordNpc(generalGreeting, DialogueEntryType.NpcLine, DialogueConversationType.OutgoingCall);
        StartTyping("\"" + generalGreeting + "\"", AfterMode.GeneralQuestions);
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
        // 통화 중 카메라는 전혀 움직이지 않는다(예전의 '말하며 끄덕이는' 흔들림 제거).
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
                case AfterMode.LocalInterviewQuestions: BuildLocalInterviewQuestions(); break;
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
        var questions = DialogueRepository.GeneralQuestions(_employeeId);
        string question = idx >= 0 && idx < questions.Count ? questions[idx].Question : "";
        string answer = DialogueRepository.GeneralAnswer(_employeeId, idx);
        RecordPlayer(question, DialogueConversationType.OutgoingCall);
        RecordNpc(answer, DialogueEntryType.NpcResponse, DialogueConversationType.OutgoingCall);
        StartTyping("\"" + answer + "\"", AfterMode.GeneralQuestions);
    }

    // --- DAY1 휴게시간 인터뷰(플레이어 발신) ----------------------------
    private void BuildLocalInterviewQuestions()
    {
        ClearChoices();
        foreach (var question in LocalInterviewDialogue.Questions)
        {
            string id = question.Id;
            _choices.AddChild(InterviewChoiceButton(LocalInterviewDialogue.GetQuestionText(_employeeId, id),
                () => OnLocalInterviewQuestion(id)));
        }
        _choices.AddChild(InterviewChoiceButton("통화를 종료한다.", CloseCall));
    }

    private void OnLocalInterviewQuestion(string questionId)
    {
        ClearChoices();
        string question = LocalInterviewDialogue.GetQuestionText(_employeeId, questionId);
        string reply = LocalInterviewDialogue.Answer(_employeeId, questionId);
        LocalInterviewDialogue.RecordTurn(_employeeId, question, reply);
        RecordPlayer(question, DialogueConversationType.Interview);
        RecordNpc(reply, DialogueEntryType.NpcResponse, DialogueConversationType.Interview);
        StartTyping("\"" + reply + "\"", AfterMode.LocalInterviewQuestions);
    }

    // --- 이벤트 통화(직원 발신) — 첫 대사 → 2지선다 → 대답 → 종료 --------
    private void BuildEventChoices()
    {
        ClearChoices();
        if (_event == null || _event.Choices.Count == 0) { BuildEndOnly(); return; }
        for (int i = 0; i < _event.Choices.Count; i++)
        {
            var choice = _event.Choices[i];
            int idx = i;
            _choices.AddChild(ChoiceButton(choice.Text, () => OnEventChoice(choice, idx)));
        }
    }

    private void OnEventChoice(DialogueRepository.Choice c, int index)
    {
        ClearChoices();
        RecordPlayer(c.Text, DialogueConversationType.IncomingCall);
        RecordNpc(c.Reply, DialogueEntryType.NpcResponse, DialogueConversationType.IncomingCall);
        EmitSignal(SignalName.EventChoiceMade, _employeeId, _dialogueEvent, index);
        StartTyping("\"" + c.Reply + "\"", AfterMode.EndOnly);
    }

    private void RecordPlayer(string text, DialogueConversationType conversationType) =>
        DialogueHistory.Instance?.AddEntry("manager", "관리자", DialogueEntryType.PlayerChoice, text, conversationType);

    private void RecordNpc(string text, DialogueEntryType entryType, DialogueConversationType conversationType)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(_employeeId);
        DialogueHistory.Instance?.AddEntry(_employeeId, def?.Codename ?? _employeeId, entryType, text, conversationType);
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

    private Button InterviewChoiceButton(string text, System.Action onPressed)
    {
        var b = ChoiceButton(text, onPressed);
        // 휴게시간 질문은 CRT에서 읽기 쉽도록 일반 선택지보다 한 단계 크게 둔다.
        b.AddThemeFontSizeOverride("font_size", ViewFont.FS(16));
        b.CustomMinimumSize = new Vector2(0, 30);
        return b;
    }

    private void SetInterviewLayout(bool interview)
    {
        if (interview)
        {
            // 질문 5개와 종료 버튼이 들어가는 높이만 쓰되, 휴게시간 화면의 하단에서
            // 뜨게 한다. CRT를 가리지 않으며 선택지 아래의 큰 빈칸도 만들지 않는다.
            _panel.AnchorTop = 1f;
            _panel.AnchorBottom = 1f;
            _panel.OffsetTop = -410f;
            _panel.OffsetBottom = -20f;
            return;
        }

        _panel.AnchorTop = 0.62f;
        _panel.AnchorBottom = 0.95f;
        _panel.OffsetTop = 0f;
        _panel.OffsetBottom = 0f;
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

}
