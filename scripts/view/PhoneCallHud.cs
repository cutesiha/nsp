using Godot;
using NSP.Core;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 전화 통화 자막 UI. 화면 하단의 작은 홀로그램 '창' 으로만 표시한다 — 중앙 대형 팝업 금지.
//  - 일반 통화(플레이어 발신): 인사 → 질문 → 대답 …
//  - 이벤트 통화(직원 발신: 사고/비명/정전/목격): 첫 대사 → 2지선다 → 대답 → 종료
//  - 휴게시간 심문: 왼쪽에 대화, 오른쪽에 「조사 자료」. 플레이어가 자료를 골라 질문을
//    만들고, 자료 두 장을 직접 맞대어 모순을 제시한다(InterviewSession 이 규칙을 쥔다).
// 모든 대사는 LocalDialogueGenerator(로컬 규칙 기반 생성기)가 실제 게임 로그/상태에서 만든다.
// DialogueRepository 는 질문 목록과, 생성이 불가능할 때의 폴백 대사로만 남는다.
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

    private enum AfterMode
    {
        None, GeneralQuestions, EventChoices, EndOnly,
        // 휴게시간 심문: 기본 질문 / 고른 자료의 질문 / 중립 꼬리질문
        InterviewMenu, InterviewIntents, InterviewFollowUps,
    }

    private Panel _panel;
    private HologramFrame _frame;
    private Label _speaker;
    private Label _playerLine;
    private Label _message;
    private VBoxContainer _choices;
    private Label _incoming;
    private Font _font;

    // 휴게시간 심문 전용 — 오른쪽 「조사 자료」 열.
    private HBoxContainer _body;
    private VBoxContainer _leftCol;
    private VBoxContainer _evidenceCol;
    private VBoxContainer _evidenceList;
    private Label _evidenceHint;
    private Button _askBtn;
    private Button _confrontBtn;
    private InterviewSession _session;
    private System.Collections.Generic.List<InterviewQuestion> _intents = new();

    private string _employeeId = "";
    private string _dialogueEvent = DialogueRepository.EventGeneralCall;
    private string _incidentRoomId = "";
    private LocalDialogueGenerator.CallLine _event;
    // 방금 답변에서 자연스럽게 이어지는 중립 질문(0~2개). 추궁은 여기 들어오지 않는다.
    private System.Collections.Generic.List<InterviewQuestion> _followUps = new();
    private string _fullText = "";
    private double _typeTimer;
    private int _shownChars;
    private bool _typing;
    private AfterMode _after;
    private float _blink;

    // 3D CRT 입력기가 통화창 뒤의 버튼까지 같은 마우스 입력을 전달하지 않도록,
    // 열려 있는 통화창의 상태를 외부에 명시한다.
    public bool IsOpen => _panel?.Visible ?? false;
    // 지금 통화 중인 상대. 튜토리얼이 "토끼와 통화 중인가"를 확인하는 데 쓴다.
    public string CurrentEmployeeId => _employeeId;

    public override void _Ready()
    {
        Instance = this;
        Layer = 114;   // 분위기 오버레이(100) 위 — 통화창이 어두워지지 않게
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

        // 왼쪽 = 대화, 오른쪽 = 조사 자료. 일반 통화에서는 오른쪽 열을 숨긴다.
        _body = new HBoxContainer();
        _body.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _body.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(_body);

        _leftCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _leftCol.AddThemeConstantOverride("separation", 10);
        _body.AddChild(_leftCol);

        _speaker = Lbl("", 22, Amber);
        _leftCol.AddChild(_speaker);

        // 플레이어가 방금 던진 질문. 심문에서 "무엇을 물었는지"가 남아야 흐름이 읽힌다.
        _playerLine = Lbl("", 15, new Color(0.62f, 0.72f, 0.76f));
        _playerLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _playerLine.Visible = false;
        _leftCol.AddChild(_playerLine);

        _message = Lbl("", 19, new Color(0.82f, 0.96f, 0.98f));
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _message.CustomMinimumSize = new Vector2(0, 88);
        _leftCol.AddChild(_message);

        _choices = new VBoxContainer();
        _choices.AddThemeConstantOverride("separation", 7);
        _leftCol.AddChild(_choices);

        BuildEvidenceColumn();

        _incoming = new Label
        {
            Text = "● INCOMING CALL",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.9f, AnchorBottom = 0.9f,
            OffsetLeft = -150, OffsetRight = 150, OffsetTop = 0, OffsetBottom = 34,
        };
        _incoming.AddThemeFontOverride("font", _font);
        _incoming.AddThemeFontSizeOverride("font_size", ViewFont.FS(15));
        _incoming.AddThemeColorOverride("font_outline_color", Colors.Black);
        _incoming.AddThemeConstantOverride("outline_size", 4);
        AddChild(_incoming);
    }

    // --- 조사 자료 열 ---------------------------------------------------

    private void BuildEvidenceColumn()
    {
        _evidenceCol = new VBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(430, 0),
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        _evidenceCol.AddThemeConstantOverride("separation", 8);
        _body.AddChild(_evidenceCol);

        var head = Lbl("조 사 자 료", 17, Cyan);
        head.HorizontalAlignment = HorizontalAlignment.Center;
        _evidenceCol.AddChild(head);

        // ScrollContainer 는 내용물의 최소 높이를 그대로 물려받는다. 그대로 두면 자료가
        // 늘어날수록 세로로 밀려 아래의 버튼이 창 밖으로 나간다. 최소 높이가 없는 Control
        // 안에 넣어 "남는 자리만 차지"하게 만든다.
        var scrollHost = new Control
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipContents = true,
        };
        _evidenceCol.AddChild(scrollHost);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scrollHost.AddChild(scroll);

        _evidenceList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _evidenceList.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(_evidenceList);

        _evidenceHint = Lbl("", 13, new Color(0.62f, 0.72f, 0.76f));
        _evidenceHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _evidenceCol.AddChild(_evidenceHint);

        _askBtn = ChoiceButton("선택한 자료로 질문", OnAskWithEvidence);
        _askBtn.CustomMinimumSize = new Vector2(0, 38);
        _evidenceCol.AddChild(_askBtn);

        _confrontBtn = ChoiceButton("두 자료를 비교 / 모순 추궁", OnConfront);
        _confrontBtn.CustomMinimumSize = new Vector2(0, 38);
        _evidenceCol.AddChild(_confrontBtn);
    }

    // 자료 카드를 다시 그린다. 답변으로 새 진술이 남으면 카드가 늘어난다.
    private void RefreshEvidence()
    {
        if (_evidenceList == null || _session == null) return;
        foreach (var c in _evidenceList.GetChildren()) c.QueueFree();

        foreach (var ev in _session.Board)
        {
            var captured = ev;
            bool on = _session.IsSelected(ev.Id);
            var b = new Button
            {
                Text = (on ? "▣ " : "□ ") + ev.Header + "\n"
                       + (string.IsNullOrEmpty(ev.TimeText) ? "" : ev.TimeText + "  ") + ev.Body,
                Alignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(0, 52),
            };
            b.AddThemeFontOverride("font", _font);
            b.AddThemeFontSizeOverride("font_size", ViewFont.FS(13));
            b.AddThemeColorOverride("font_color", on ? Colors.White : Cyan);
            b.AddThemeColorOverride("font_hover_color", Colors.White);
            var box = new StyleBoxFlat
            {
                BgColor = on ? new Color(0.16f, 0.34f, 0.38f, 0.8f) : new Color(0.06f, 0.13f, 0.16f, 0.6f),
                BorderColor = on ? Cyan : Cyan with { A = 0.32f },
                BorderWidthLeft = on ? 2 : 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                ContentMarginLeft = 9, ContentMarginRight = 9, ContentMarginTop = 4, ContentMarginBottom = 4,
            };
            b.AddThemeStyleboxOverride("normal", box);
            b.AddThemeStyleboxOverride("hover", box);
            b.AddThemeStyleboxOverride("pressed", box);
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            b.Pressed += () =>
            {
                _session.Toggle(captured.Id);
                RefreshEvidence();
            };
            _evidenceList.AddChild(b);
        }

        if (_session.Board.Count == 0)
            _evidenceList.AddChild(Lbl("확보한 자료가 없습니다.", 13, new Color(0.55f, 0.62f, 0.66f)));

        int picked = _session.Selected.Count;
        _askBtn.Disabled = picked == 0;
        _confrontBtn.Disabled = picked != 2;
        _evidenceHint.Text = picked switch
        {
            0 => "자료를 고르면 그 기록을 물을 수 있습니다.",
            1 => "한 장 더 고르면 맞대어 볼 수 있습니다.",
            _ => "두 자료를 맞대어 모순을 제시합니다.",
        };
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

    public void Open(string employeeId, string dialogueEvent = DialogueRepository.EventGeneralCall,
        string incidentRoomId = "")
    {
        _employeeId = employeeId;
        _dialogueEvent = string.IsNullOrEmpty(dialogueEvent) ? DialogueRepository.EventGeneralCall : dialogueEvent;
        _incidentRoomId = incidentRoomId ?? "";
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
            // 휴게시간 심문 — 질문은 플레이어가 조사 자료에서 직접 만든다.
            _session = new InterviewSession(employeeId);
            _followUps.Clear();
            _intents.Clear();
            _playerLine.Visible = false;
            RefreshEvidence();
            string greeting = _session.Greeting();
            RecordNpc(greeting, DialogueEntryType.NpcLine, DialogueConversationType.Interview);
            StartTyping("\"" + greeting + "\"", AfterMode.InterviewMenu);
            return;
        }
        _session = null;

        if (_dialogueEvent != DialogueRepository.EventGeneralCall)
        {
            // 사고 신고는 실제 RoomId/EventType 으로 매번 새로 만든다. 만들 수 없을 때만
            // 원본 대사(DialogueRepository)를 폴백으로 쓴다.
            _event = LocalDialogueGenerator.BuildIncomingCall(employeeId, _dialogueEvent, _incidentRoomId)
                     ?? FallbackEvent(_dialogueEvent, employeeId);
            if (_event != null && !string.IsNullOrEmpty(_event.Opening))
            {
                RecordNpc(_event.Opening, DialogueEntryType.NpcLine, DialogueConversationType.IncomingCall);
                StartTyping("\"" + _event.Opening + "\"", AfterMode.EventChoices);
                return;
            }
            _dialogueEvent = DialogueRepository.EventGeneralCall; // 해당 이벤트 대사가 없으면 일반 통화로
        }

        _event = null;
        string generalGreeting = LocalDialogueGenerator.GeneralGreeting(employeeId);
        RecordNpc(generalGreeting, DialogueEntryType.NpcLine, DialogueConversationType.OutgoingCall);
        StartTyping("\"" + generalGreeting + "\"", AfterMode.GeneralQuestions);
    }

    // 동적 생성이 불가능한 경우에만 쓰는 폴백 — 원본 문서의 고정 대사를 같은 형태로 감싼다.
    private static LocalDialogueGenerator.CallLine FallbackEvent(string dialogueEvent, string employeeId)
    {
        // "수상한 행동 목격" 원문은 특정 직원(여우)과 작업실을 문장에 박아 두었다.
        // 실제로 목격한 사실이 없으면 그 문장이 없는 사실을 말하게 되므로 폴백하지 않는다.
        if (dialogueEvent == DialogueRepository.EventWitnessSuspicious) return null;
        var src = DialogueRepository.GetEvent(dialogueEvent, employeeId);
        if (src == null) return null;
        var line = new LocalDialogueGenerator.CallLine { Opening = src.Opening };
        foreach (var c in src.Choices)
            line.Choices.Add(new LocalDialogueGenerator.CallChoice { Text = c.Text, Reply = c.Reply });
        return line;
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
                case AfterMode.InterviewMenu: BuildInterviewMenu(); break;
                case AfterMode.InterviewIntents: BuildIntentChoices(); break;
                case AfterMode.InterviewFollowUps: BuildInterviewFollowUps(); break;
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
        // 질문 목록은 기존 구조 그대로, 대답만 현재 근무 상태에서 생성한다.
        string answer = LocalDialogueGenerator.GeneralAnswer(_employeeId, idx);
        RecordPlayer(question, DialogueConversationType.OutgoingCall);
        RecordNpc(answer, DialogueEntryType.NpcResponse, DialogueConversationType.OutgoingCall);
        StartTyping("\"" + answer + "\"", AfterMode.GeneralQuestions);
    }

    // --- 휴게시간 심문 --------------------------------------------------

    // 기본 질문 — 증거 없이도 물을 수 있는 도입부. 인터뷰의 중심이 아니다.
    private void BuildInterviewMenu()
    {
        ClearChoices();
        _followUps.Clear();
        _intents.Clear();
        RefreshEvidence();
        foreach (var q in _session.BasicQuestions())
        {
            var captured = q;
            _choices.AddChild(InterviewChoiceButton(q.Text, () => AskInterview(captured)));
        }
        _choices.AddChild(InterviewChoiceButton("통화를 종료한다.", CloseCall));
    }

    // 「선택한 자료로 질문」 — 고른 자료로 물을 수 있는 것들을 왼쪽에 펼친다.
    private void OnAskWithEvidence()
    {
        if (_session == null) return;
        _intents = _session.QuestionsForSelection();
        if (_intents.Count == 0)
        {
            ShowSystemLine("그 자료로 더 물어볼 것이 없습니다.");
            return;
        }
        BuildIntentChoices();
    }

    private void BuildIntentChoices()
    {
        ClearChoices();
        foreach (var q in _intents)
        {
            var captured = q;
            _choices.AddChild(InterviewChoiceButton(q.Text, () => AskInterview(captured)));
        }
        _choices.AddChild(InterviewChoiceButton("다른 질문을 한다.", BuildInterviewMenu));
    }

    private void AskInterview(InterviewQuestion q)
    {
        ClearChoices();
        var turn = _session.Ask(q);
        _followUps = turn.FollowUps;
        LocalInterviewDialogue.RecordTurn(_employeeId, turn.QuestionText, turn.Answer);
        RecordPlayer(turn.QuestionText, DialogueConversationType.Interview);
        RecordNpc(turn.Answer, DialogueEntryType.NpcResponse, DialogueConversationType.Interview);
        ShowPlayerLine(turn.QuestionText);
        RefreshEvidence();
        StartTyping("\"" + turn.Answer + "\"",
            _followUps.Count > 0 ? AfterMode.InterviewFollowUps : AfterMode.InterviewMenu);
    }

    // 답변에서 이어지는 중립 질문만. "기록과 다른데요?" 는 여기 없다.
    private void BuildInterviewFollowUps()
    {
        ClearChoices();
        if (_followUps.Count == 0) { BuildInterviewMenu(); return; }
        foreach (var q in _followUps)
        {
            var captured = q;
            _choices.AddChild(InterviewChoiceButton(q.Text, () => AskInterview(captured)));
        }
        _choices.AddChild(InterviewChoiceButton("다른 질문을 한다.", BuildInterviewMenu));
    }

    // 「두 자료를 비교」 — 모순인지 아닌지는 여기서 처음 밝혀진다.
    private void OnConfront()
    {
        if (_session == null || !_session.CanTryConfront) return;
        var result = _session.CheckContradiction();
        if (!result.IsContradiction)
        {
            ShowSystemLine(result.Notice);
            return;
        }

        ClearChoices();
        var turn = _session.Confront(result);
        _followUps.Clear();
        LocalInterviewDialogue.RecordTurn(_employeeId, turn.QuestionText, turn.Answer);
        RecordPlayer(turn.QuestionText, DialogueConversationType.Interview);
        RecordNpc(turn.Answer, DialogueEntryType.NpcResponse, DialogueConversationType.Interview);
        ShowPlayerLine(turn.QuestionText);
        _session.ClearSelection();
        RefreshEvidence();
        StartTyping("\"" + turn.Answer + "\"", AfterMode.InterviewMenu);
    }

    private void ShowPlayerLine(string text)
    {
        if (_playerLine == null) return;
        _playerLine.Text = string.IsNullOrEmpty(text) ? "" : "관리자 ▸ " + text;
        _playerLine.Visible = !string.IsNullOrEmpty(text);
    }

    // 직원이 하는 말이 아니라 시스템 안내(모순 없음 등). 목소리도 타이핑도 없다.
    private void ShowSystemLine(string text)
    {
        _typing = false;
        Sfx.Instance?.StopVoiceBlip();
        ShowPlayerLine("");
        _message.Text = "— " + text;
        _message.VisibleCharacters = -1;
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

    private void OnEventChoice(LocalDialogueGenerator.CallChoice c, int index)
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
        b.CustomMinimumSize = new Vector2(0, 44);
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
        b.CustomMinimumSize = new Vector2(0, 38);
        return b;
    }

    private void SetInterviewLayout(bool interview)
    {
        if (_evidenceCol != null) _evidenceCol.Visible = interview;

        if (interview)
        {
            // 심문은 왼쪽 대화 + 오른쪽 조사 자료의 두 열이라 넓은 판이 필요하다.
            _panel.AnchorLeft = 0.06f;
            _panel.AnchorRight = 0.94f;
            _panel.AnchorTop = 1f;
            _panel.AnchorBottom = 1f;
            _panel.OffsetTop = -600f;
            _panel.OffsetBottom = -16f;
            return;
        }

        _panel.AnchorLeft = 0.24f;
        _panel.AnchorRight = 0.76f;
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
        _session = null;
        _followUps.Clear();
        _intents.Clear();
        ShowPlayerLine("");
        EmitSignal(SignalName.Closed);
    }

    // 전화기(3D)를 직접 클릭해 통화를 끊을 때 — HUD 쪽도 닫는다.
    public void RequestClose()
    {
        if (_panel.Visible) CloseCall();
    }

}
