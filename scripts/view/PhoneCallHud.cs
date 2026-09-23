using Godot;
using NSP.Core;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 전화 통화 자막 UI. 화면 하단의 작은 홀로그램 '창' 으로만 표시한다 — 중앙 대형 팝업 금지.
//  - 일반 통화(플레이어 발신): 인사 → 질문 → 대답 …
//  - 이벤트 통화(직원 발신: 사고/비명/정전/목격): 첫 대사 → 2지선다 → 대답 → 종료
//  - 휴게시간 심문: 이 창이 대화 전부를 맡는다 — 지금 질문 · 지금 답변 · 선택지.
//    자리와 크기는 일반 통화와 같다(1.5차에서 자막 띠 방식을 버리고 되돌렸다).
//    조사 자료는 MONITOR 01 의 RestInterviewConsole 에 있고, 여기서는 그 카드를 읽어
//    "무엇을 물을 수 있는가"만 선택지로 만든다(추리 규칙은 InterviewSession 이 쥔다).
//
//    이 창은 **스스로 말을 이어 가지 않는다.** 답변은 플레이어가 선택지를 누를 때만 나온다.
//    세션을 끝내는 길도 둘뿐이다 — 선택지 맨 아래 「통화를 종료한다.」 와 3D 전화기 클릭.
//    가리는 것이 문제라면 끊지 말고 손잡이 오른쪽의 「▼ 접기」로 이름 한 줄만 남긴다.
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
    private RichTextLabel _message;
    private VBoxContainer _choices;
    private Label _incoming;
    private Font _font;

    // 휴게시간 심문 전용 — 왼쪽 대화(진술 · 질문), 오른쪽 「조사 노트」, 아래 「자료 A / B」.
    private VBoxContainer _root;
    private HBoxContainer _body;
    private VBoxContainer _leftCol;
    private VBoxContainer _leftInner;
    // 심문 UI 는 MONITOR 01 의 심문 콘솔(RestInterviewConsole) 안에 있다. 아래는 그 콘솔의 자리를 가리킨다.
    private RestInterviewConsole _console;
    // 창 오른쪽 위의 통화 종료 버튼(심문 콘솔에서는 숨긴다 — 그쪽은 콘솔이 종료를 맡는다).
    private Button _hangUp;
    private bool _consoleWired;
    private VBoxContainer _evidenceList;
    private HFlowContainer _noteTabs;
    private Button _slotA;
    private Button _slotB;
    private VBoxContainer _tail;
    private InterviewSession _session;
    private System.Collections.Generic.List<InterviewQuestion> _intents = new();
    // 조사 노트 화면 상태(보기 방식일 뿐 — 판정에 쓰지 않는다).
    private readonly System.Collections.Generic.HashSet<string> _expandedCards = new();
    private readonly System.Collections.Generic.HashSet<string> _collapsedGroups = new();

    // 창 윗부분을 잡고 끌어 옮기는 손잡이(심문 창에서만 쓴다).
    private Control _dragBar;
    private bool _dragging;
    // 손잡이 위의 이름(접었을 때 남는 한 줄)과 접기 버튼.
    private Label _barName;
    private Button _collapseBtn;
    // 접힘 — 창을 치우되 통화는 유지한다. MONITOR 01 을 조작하는 동안 쓰는 상태다.
    private bool _collapsed;
    private float _openAnchorTop, _openAnchorBottom, _openOffsetTop, _openOffsetBottom;

    // 이미 들어 본 근무 진술(✓ 표시용). 목록에서 빼지는 않는다.
    private readonly System.Collections.Generic.HashSet<int> _heardOpenings = new();
    // 마지막으로 그린 자료 A/B. 바뀔 때만 콘솔에 알린다(매 프레임 알리지 않게).
    private string _lastSlots = "";

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

        // 통화는 언제든 끊을 수 있어야 한다 — 선택지가 없는 구간(상대가 말하는 중, 사건 전화 등)
        // 에서도 창 오른쪽 위의 이 버튼으로 바로 끊는다. ESC 도 같은 동작이다.
        _hangUp = MonitorUi.Button("통화 종료 ✕", new Color(1f, 0.55f, 0.45f), _font, CloseCall, ViewFont.FS(13));
        _hangUp.AnchorLeft = 1f; _hangUp.AnchorRight = 1f;
        _hangUp.OffsetLeft = -128f; _hangUp.OffsetRight = -14f;
        _hangUp.OffsetTop = 10f; _hangUp.OffsetBottom = 36f;
        _hangUp.MouseFilter = Control.MouseFilterEnum.Stop;
        _panel.AddChild(_hangUp);

        // 위 = [왼쪽 대화 | 오른쪽 조사 노트], 아래 = 자료 A/B · 질문 · 비교 · 통화 종료(심문에서만).
        // 일반 통화에서는 오른쪽 열과 아래 줄을 숨긴다.
        _root = new VBoxContainer();
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // 창 테두리(홀로그램 모서리)와 글자가 붙지 않게 안쪽 여백.
        _root.OffsetLeft = 24; _root.OffsetTop = 10; _root.OffsetRight = -24; _root.OffsetBottom = -10;
        _root.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(_root);

        _body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 18);
        _root.AddChild(_body);

        _leftCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.15f };
        _leftCol.AddThemeConstantOverride("separation", 10);
        _body.AddChild(_leftCol);

        _speaker = Lbl("", 19, Amber);
        _leftCol.AddChild(_speaker);

        // 플레이어가 방금 던진 질문. 심문에서 "무엇을 물었는지"가 남아야 흐름이 읽힌다.
        _playerLine = Lbl("", 15, new Color(0.62f, 0.72f, 0.76f));
        _playerLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _playerLine.Visible = false;
        _leftCol.AddChild(_playerLine);

        // 직원 이름 · 작업실 이름에 강조색을 입히려고 RichTextLabel 을 쓴다(글자 수는 원문과 같다).
        _message = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _message.AddThemeFontOverride("normal_font", _font);
        _message.AddThemeFontSizeOverride("normal_font_size", ViewFont.FS(17));
        _message.AddThemeColorOverride("default_color", new Color(0.82f, 0.96f, 0.98f));
        // 답변 두 줄이 들어갈 만큼만. 넉넉히 잡으면 창 아래가 통째로 빈다.
        _message.CustomMinimumSize = new Vector2(0, 54);
        _leftCol.AddChild(_message);

        // 진술 블록 · 질문은 길어질 수 있으므로 남는 자리 안에서 스크롤한다(버튼이 창 밖으로 밀리지 않게).
        var leftHost = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true };
        _leftCol.AddChild(leftHost);
        var leftScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        leftScroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        leftHost.AddChild(leftScroll);
        _leftInner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _leftInner.AddThemeConstantOverride("separation", 7);
        leftScroll.AddChild(_leftInner);

        _choices = new VBoxContainer();
        _choices.AddThemeConstantOverride("separation", 7);
        _leftInner.AddChild(_choices);

        // 맨 아래 한 줄(통화를 종료한다 / 다른 질문을 한다)은 **스크롤 밖**에 둔다.
        // 안에 두면 선택지가 길어질 때 끊는 버튼이 화면 밖으로 밀린다 — 1차에서 나온 불만이다.
        _tail = new VBoxContainer();
        _leftCol.AddChild(_tail);

        BuildDragBar();

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

    // --- 창 옮기기 -------------------------------------------------------

    // 실제 프로그램 창처럼, 위쪽 띠를 잡고 끌면 창이 따라온다.
    // 심문 창은 화면을 크게 차지하므로 가리는 곳을 플레이어가 직접 치울 수 있어야 한다.
    private void BuildDragBar()
    {
        _dragBar = new Control
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetTop = 0f, OffsetBottom = DragBarHeight,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Move,
            Visible = false,
        };
        _dragBar.GuiInput += OnDragBarInput;
        _panel.AddChild(_dragBar);

        // 접었을 때 남는 한 줄. 누구와 통화 중인지는 항상 보여야 한다.
        _barName = Lbl("", 15, Amber);
        _barName.AnchorLeft = 0f; _barName.AnchorRight = 0f;
        _barName.OffsetLeft = 26f; _barName.OffsetRight = 320f;
        _barName.OffsetTop = 6f; _barName.OffsetBottom = 30f;
        _barName.MouseFilter = Control.MouseFilterEnum.Ignore;
        _barName.Visible = false;
        _dragBar.AddChild(_barName);

        // 창을 치우는 방법은 끊는 것 말고 하나 더 있어야 한다 — 접기.
        _collapseBtn = MonitorUi.Button("▼ 접기", Cyan, _font, () => SetCollapsed(!_collapsed), ViewFont.FS(13));
        _collapseBtn.AnchorLeft = 1f; _collapseBtn.AnchorRight = 1f;
        _collapseBtn.OffsetLeft = -112f; _collapseBtn.OffsetRight = -14f;
        _collapseBtn.OffsetTop = 4f; _collapseBtn.OffsetBottom = 32f;
        _collapseBtn.MouseFilter = Control.MouseFilterEnum.Stop;
        _dragBar.AddChild(_collapseBtn);
    }

    // --- 접기 -------------------------------------------------------------

    // 접으면 이름 한 줄만 남는다. **세션도 선택지도 타이핑 상태도 그대로다** —
    // 통화를 끊는 것과는 완전히 다른 동작이다(끊는 길은 선택지와 전화기 둘뿐).
    public bool Collapsed => _collapsed;

    private const float CollapsedHeight = DragBarHeight;

    public void SetCollapsed(bool on)
    {
        if (_session == null) on = false;          // 일반 통화는 접지 않는다
        if (_collapsed == on || _panel == null) return;
        _collapsed = on;

        if (on)
        {
            _openAnchorTop = _panel.AnchorTop; _openAnchorBottom = _panel.AnchorBottom;
            _openOffsetTop = _panel.OffsetTop; _openOffsetBottom = _panel.OffsetBottom;
            _panel.AnchorBottom = _panel.AnchorTop;
            _panel.OffsetBottom = _panel.OffsetTop + CollapsedHeight;
        }
        else
        {
            _panel.AnchorTop = _openAnchorTop; _panel.AnchorBottom = _openAnchorBottom;
            _panel.OffsetTop = _openOffsetTop; _panel.OffsetBottom = _openOffsetBottom;
        }
        if (_root != null) _root.Visible = !on;
        if (_barName != null) _barName.Visible = on;
        if (_collapseBtn != null) _collapseBtn.Text = on ? "▲ 펼치기" : "▼ 접기";
    }

    // MONITOR 01 이 "대화창 펼치기" 를 눌렀거나 슬롯이 바뀌었을 때 — 선택지가 달라졌으니 편다.
    private void ExpandForConsole()
    {
        SetCollapsed(false);
    }

    private const float DragBarHeight = 36f;

    private void OnDragBarInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                _dragBar.AcceptEvent();
                break;
            case InputEventMouseMotion mm when _dragging:
                MovePanelBy(mm.Relative);
                _dragBar.AcceptEvent();
                break;
        }
    }

    private void MovePanelBy(Vector2 delta)
    {
        _panel.OffsetLeft += delta.X;
        _panel.OffsetRight += delta.X;
        _panel.OffsetTop += delta.Y;
        _panel.OffsetBottom += delta.Y;
        ClampPanel();
    }

    // 창을 화면 밖으로 완전히 내보내지 못하게 한다 — 손잡이는 항상 잡을 수 있어야 한다.
    private void ClampPanel()
    {
        var screen = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        if (screen == Vector2.Zero) return;
        var rect = _panel.GetRect();
        const float Keep = 120f;
        var want = new Vector2(
            Mathf.Clamp(rect.Position.X, Keep - rect.Size.X, screen.X - Keep),
            Mathf.Clamp(rect.Position.Y, 0f, screen.Y - DragBarHeight));
        var fix = want - rect.Position;
        if (fix.IsZeroApprox()) return;
        _panel.OffsetLeft += fix.X;
        _panel.OffsetRight += fix.X;
        _panel.OffsetTop += fix.Y;
        _panel.OffsetBottom += fix.Y;
    }

    // --- MONITOR 01 심문 콘솔 --------------------------------------------

    // 심문 화면은 통화창이 아니라 MONITOR 01(휴게실 CRT) 안에 있다. 콘솔은 자리와 화면 전환만 갖고,
    // 내용(진술 · 질문 · 카드 · 자료 A/B · 버튼)은 여기서 채운다.
    // 3D 씬이 없는 캡처/테스트에서는 콘솔을 이 CanvasLayer 에 직접 띄운다.
    private void AttachConsole()
    {
        var con = RestInterviewConsole.Instance;
        if (con == null)
        {
            con = new RestInterviewConsole();
            AddChild(con);
        }
        if (_console != con) { _console = con; _consoleWired = false; }
        if (_consoleWired) return;
        _consoleWired = true;

        _noteTabs = con.NoteTabs;
        _evidenceList = con.EvidenceList;
        // 콘솔에는 통화 종료가 없다 — 이 신호는 "접어 둔 대화창을 다시 펴 달라" 다.
        con.ExpandRequested += ExpandForConsole;
        // 자료 A/B 가 바뀌면 선택지도 바뀐다 — 접혀 있으면 펴서 보여 준다.
        con.SlotsChanged += ExpandForConsole;
        // 띠에 꽂힌 핀 · 사고 세로선을 눌렀다 = 목록에서 그 카드를 누른 것.
        // 카드 버튼과 **같은 두 줄**을 탄다 — 고르는 길이 둘로 갈라지면 규칙도 둘이 된다.
        con.EvidencePinPressed += id =>
        {
            if (_session == null) return;
            _session.Toggle(id);
            OnEvidencePicked();
        };

        // 보기 — 기본은 지금 심문 중인 직원의 자료다. 다른 직원 자료는 아예 뜨지 않는다
        // (회색으로 깔아 두면 읽을 수 없는 카드가 화면의 절반을 먹는다).
        // [전원] 을 켰을 때만 오늘 확보한 자료 전부를 사건별로 묶어 보여 준다.
        foreach (var (label, tab) in new (string, NoteTab)[]
                 {
                     ("현재 직원", NoteTab.CurrentEmployee), ("★ 중요", NoteTab.Starred),
                     ("전원", NoteTab.ByIncident),
                 })
        {
            var t = tab;
            var chip = FilterChip(label, () =>
            {
                if (_session == null) return;
                _session.SetTab(t);
                RefreshEvidence();
            });
            chip.SetMeta("tab", (int)t);
            _noteTabs.AddChild(chip);
        }

        // [자료 A] [자료 B] — 질문/비교 버튼은 없다. 자막 띠의 선택지가 그 역할을 한다.
        _slotA = SlotButton(0);
        _slotB = SlotButton(1);
        con.SlotRow.AddChild(_slotA);
        con.SlotRow.AddChild(_slotB);
    }

    // 심문 콘솔(MONITOR 01) 안의 글자는 SubViewport 논리 크기, 통화 자막은 화면 크기.
    private bool OnMonitor => _session != null;
    private int Fz(int px) => OnMonitor ? ViewFont.S(px) : ViewFont.FS(px);

    // 선택한 자료 한 칸. 누르면 그 자료의 선택이 풀린다.
    private Button SlotButton(int index)
    {
        var b = new Button
        {
            Alignment = HorizontalAlignment.Left,
            VerticalIconAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
            CustomMinimumSize = new Vector2(0, 88),
            ClipText = true,
        };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", ViewFont.S(13));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += () =>
        {
            var ev = _session?.EvidenceAt(index);
            if (ev == null) return;
            _session.Toggle(ev.Id);
            OnEvidencePicked();
        };
        return b;
    }

    // 작은 분류 단추 하나.
    private Button FilterChip(string label, System.Action onPressed)
    {
        var b = new Button { Text = label, CustomMinimumSize = new Vector2(0, 30) };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", ViewFont.S(14));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += onPressed;
        return b;
    }

    private void PaintChip(Button b, bool on)
    {
        b.AddThemeColorOverride("font_color", on ? Colors.White : Cyan with { A = 0.75f });
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        var box = new StyleBoxFlat
        {
            BgColor = on ? new Color(0.16f, 0.34f, 0.38f, 0.85f) : new Color(0.05f, 0.11f, 0.14f, 0.6f),
            BorderColor = on ? Cyan : Cyan with { A = 0.28f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        b.AddThemeStyleboxOverride("normal", box);
        b.AddThemeStyleboxOverride("hover", box);
        b.AddThemeStyleboxOverride("pressed", box);
    }

    // 조사 노트를 다시 그린다. 답변으로 새 진술이 남으면 카드가 늘어난다.
    private void RefreshEvidence()
    {
        if (_evidenceList == null || _session == null) return;
        foreach (var c in _evidenceList.GetChildren()) c.QueueFree();

        foreach (Node c in _noteTabs.GetChildren())
            if (c is Button chip)
                PaintChip(chip, (int)chip.GetMeta("tab", 0) == (int)_session.Tab);

        // 마지막으로 고른 자료와 같은 시간대의 자료. 밝기만 달라진다 —
        // 무엇이 단서인지는 화면이 판단하지 않는다.
        var near = _session.SameWindowIds();
        int shown = 0;

        switch (_session.Tab)
        {
            case NoteTab.ByIncident:
                foreach (var g in InterviewEvidenceDisplay.ByIncident(_session.Board))
                {
                    AddGroupHeader(g);
                    if (_collapsedGroups.Contains(g.Key)) continue;
                    foreach (var card in g.Cards) { AddCard(card, near); shown++; }
                }
                break;
            case NoteTab.CurrentEmployee:
                foreach (var card in InterviewEvidenceDisplay.Compress(_session.Board)) { AddCard(card, near); shown++; }
                break;
            case NoteTab.Starred:
                foreach (var ev in _session.Board)
                {
                    if (!_session.IsStarred(ev.Id)) continue;
                    var single = new EvidenceCard();
                    single.Items.Add(ev);
                    AddCard(single, near);
                    shown++;
                }
                break;
        }

        if (shown == 0)
            _evidenceList.AddChild(Lbl(_session.Tab == NoteTab.Starred
                ? "★ 로 표시한 자료가 없습니다."
                : "확보한 자료가 없습니다.", 13, new Color(0.55f, 0.62f, 0.66f)));

        RefreshTimeline();
        UpdateSlots();
    }

    // 조사 노트 위의 띠 시간표를 다시 채운다. 카드가 늘어나면 핀도 늘어난다.
    //
    // 띠에 넘기는 로그는 조사 자료를 만들 때와 **같은 요약본**이다 — 자료판에는 없는
    // 이동이 띠에 뜨면 플레이어가 보지 못한 사실로 추리하게 된다.
    private void RefreshTimeline()
    {
        if (_console == null || _session == null) return;
        var rows = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(),
            GameState.Instance?.CurrentDay ?? 1);
        _console.SetTimeline(FacilitySimulation.Instance?.GetActiveEmployeeIds(), rows,
            _session.Board, _session.EmployeeId, _session.Selected);
    }

    // 사건 묶음 머리 — 누르면 접고 편다.
    private void AddGroupHeader(EvidenceGroup g)
    {
        bool closed = _collapsedGroups.Contains(g.Key);
        var b = new Button
        {
            Text = (closed ? "▶  " : "▼  ") + g.Title,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            CustomMinimumSize = new Vector2(0, 30),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", ViewFont.S(15));
        b.AddThemeColorOverride("font_color", g.Incident != null ? Amber : Cyan with { A = 0.7f });
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.07f, 0.09f, 0.55f),
            BorderColor = (g.Incident != null ? Amber : Cyan) with { A = 0.35f },
            BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2,
        };
        foreach (string st in new[] { "normal", "hover", "pressed" }) b.AddThemeStyleboxOverride(st, box);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        string key = g.Key;
        b.Pressed += () =>
        {
            if (!_collapsedGroups.Remove(key)) _collapsedGroups.Add(key);
            RefreshEvidence();
        };
        _evidenceList.AddChild(b);
    }

    // 카드 한 장. 묶음 카드는 눌러서 펼치고, 펼친 상세의 원본 자료를 골라 질문 · 비교에 쓴다.
    private void AddCard(EvidenceCard card, System.Collections.Generic.HashSet<string> near)
    {
        if (card == null || card.Items.Count == 0) return;
        if (!card.IsCluster)
        {
            AddEvidenceRow(card.First, InterviewEvidenceDisplay.Title(card), InterviewEvidenceDisplay.Body(card), 0, near);
            return;
        }

        bool open = _expandedCards.Contains(card.Key);
        bool anySelected = card.Items.Exists(e => _session.IsSelected(e.Id));
        bool anyNear = card.Items.Exists(e => near.Contains(e.Id));
        bool usable = card.Items.Exists(e => _session.CanUse(e));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(30, CardHeight) });   // ★ 칸 자리 맞춤
        row.AddChild(TagStrip(card.First));

        string label = (open ? "▾ " : "▸ ") + Short(InterviewEvidenceDisplay.Body(card), CardBodyChars)
                       + $"  ·{card.Items.Count}건";
        var b = CardButton(label, anySelected, anyNear, usable);
        string key = card.Key;
        b.Pressed += () =>
        {
            if (!_expandedCards.Remove(key)) _expandedCards.Add(key);
            RefreshEvidence();
        };
        row.AddChild(b);
        row.AddChild(TimeLabel(DialogueClock.Range(card.First.AnchorTime, card.Last.AnchorTime), usable));
        _evidenceList.AddChild(row);

        if (!open) return;
        var detailHead = Lbl("      ▼ 상세 기록", 12, Cyan with { A = 0.6f });
        _evidenceList.AddChild(detailHead);
        foreach (var ev in card.Items)
            AddEvidenceRow(ev, InterviewEvidenceDisplay.DetailLine(ev), null, 1, near);
    }

    // 원본 자료 한 장 — ★ 와 선택 버튼.
    private void AddEvidenceRow(InterviewEvidence ev, string line1, string line2, int indent,
        System.Collections.Generic.HashSet<string> near)
    {
        var captured = ev;
        bool on = _session.IsSelected(ev.Id);
        bool usable = _session.CanUse(ev);
        bool star = _session.IsStarred(ev.Id);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        _evidenceList.AddChild(row);
        if (indent > 0) row.AddChild(new Control { CustomMinimumSize = new Vector2(22 * indent, 0) });

        // ★ — 플레이어가 직접 찍는 메모. 게임이 중요도를 정하지 않는다.
        var mark = new Button
        {
            Text = star ? "★" : "☆",
            CustomMinimumSize = new Vector2(30, CardHeight),
            TooltipText = "중요 표시",
        };
        mark.AddThemeFontOverride("font", _font);
        mark.AddThemeFontSizeOverride("font_size", ViewFont.S(14));
        mark.AddThemeColorOverride("font_color", star ? Amber : Cyan with { A = 0.45f });
        mark.AddThemeColorOverride("font_hover_color", Amber);
        foreach (string st in new[] { "normal", "hover", "pressed", "focus" })
            mark.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
        mark.Pressed += () => { _session.ToggleStar(captured.Id); RefreshEvidence(); };
        row.AddChild(mark);

        // 태그는 아이콘 대신 색 띠 + 두 글자. 종류가 색으로 먼저 읽히고, 글자는 확인용이다.
        row.AddChild(TagStrip(ev));

        // 카드 본문은 "누가 · 어디서" 만. 태그는 왼쪽 색 띠가, 시각은 오른쪽 라벨이 맡는다.
        // 한 줄에 다 싣지 않는다 — 전문은 카드를 누르면 아래 상세칸에 뜬다.
        string body = string.IsNullOrEmpty(line2) ? StripHead(line1) : line2;
        var b = CardButton(SlotBadge(ev) + Short(body, CardBodyChars), on, near.Contains(ev.Id), usable);
        b.Pressed += () =>
        {
            _session.Toggle(captured.Id);
            OnEvidencePicked();
        };
        row.AddChild(b);

        row.AddChild(TimeLabel(ev.TimeText, usable));
    }

    // 시각은 오른쪽 끝에 붙인다 — 카드들이 세로로 줄을 맞춰 훑기 쉬워진다.
    private Label TimeLabel(string text, bool usable)
    {
        var l = Lbl(text ?? "", 14, Cyan with { A = usable ? 0.75f : 0.4f });
        l.HorizontalAlignment = HorizontalAlignment.Right;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.CustomMinimumSize = new Vector2(150, CardHeight);
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        return l;
    }

    // 카드 본문에 들어가는 글자 수. 넘치면 … 로 자르고 전문은 상세칸이 맡는다.
    private const int CardBodyChars = 14;

    // 카드 한 장의 높이. 기울어진 CRT 에서 읽히려면 이 정도는 되어야 한다.
    private const float CardHeight = 44f;

    // 자료 종류 = 색 + 두 글자. 색은 시설 로그 화면의 팔레트와 같은 계열로 맞춘다.
    private static Color TagColor(InterviewEvidence ev) => ev.Kind switch
    {
        EvidenceKind.Movement => new Color(0.36f, 0.86f, 0.82f),   // 기록  청록
        EvidenceKind.Cctv => new Color(0.92f, 0.94f, 0.96f),       // CCTV  흰색
        EvidenceKind.Testimony => new Color(1f, 0.82f, 0.36f),     // 증언  노랑
        EvidenceKind.Incident => new Color(1f, 0.42f, 0.34f),      // 사고  빨강
        EvidenceKind.Mood => new Color(0.60f, 0.66f, 0.70f),       // 기분  회색
        // 진술은 그 직원의 고유색 — 누구의 말인지가 색으로 먼저 읽힌다.
        _ => Readable(FacilitySimulation.Instance?.GetEmployeeDef(ev.SubjectEmployeeId)?.IconColor ?? Cyan),
    };

    private Control TagStrip(InterviewEvidence ev)
    {
        var c = TagColor(ev);
        var host = new PanelContainer { CustomMinimumSize = new Vector2(52, CardHeight), MouseFilter = Control.MouseFilterEnum.Ignore };
        host.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = c with { A = 0.22f },
            BorderColor = c,
            BorderWidthLeft = 4,
            ContentMarginLeft = 4, ContentMarginRight = 2, ContentMarginTop = 2, ContentMarginBottom = 2,
        });
        var l = Lbl(ev.Tag, 14, c);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.VerticalAlignment = VerticalAlignment.Center;
        l.MouseFilter = Control.MouseFilterEnum.Ignore;
        host.AddChild(l);
        return host;
    }

    // 슬롯에 담긴 카드에는 A / B 가 붙는다 — 아래 슬롯 칸을 보지 않아도 무엇을 골랐는지 안다.
    private string SlotBadge(InterviewEvidence ev)
    {
        if (_session == null) return "";
        var sel = _session.Selected;
        for (int i = 0; i < sel.Count && i < 2; i++)
            if (sel[i] == ev.Id) return i == 0 ? "[A] " : "[B] ";
        return "";
    }

    // "[태그]  시각" 앞머리는 이제 색 띠와 오른쪽 라벨이 맡는다 — 본문에서 뗀다.
    private static string StripHead(string text)
    {
        text = (text ?? "").Trim();
        int close = text.StartsWith('[') ? text.IndexOf(']') : -1;
        return close < 0 ? text : text[(close + 1)..].Trim();
    }

    private Button CardButton(string text, bool on, bool near, bool usable)
    {
        var b = new Button
        {
            Text = text,
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            CustomMinimumSize = new Vector2(0, CardHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", ViewFont.S(16));
        b.AddThemeColorOverride("font_color", on ? Colors.White : usable ? Cyan : Cyan with { A = 0.5f });
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        var box = new StyleBoxFlat
        {
            BgColor = on ? new Color(0.16f, 0.34f, 0.38f, 0.8f)
                : near ? new Color(0.09f, 0.19f, 0.23f, 0.7f)
                : new Color(0.06f, 0.13f, 0.16f, 0.6f),
            BorderColor = on ? Cyan : near ? Cyan with { A = 0.55f } : Cyan with { A = 0.32f },
            BorderWidthLeft = on || near ? 3 : 1,
            BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 9, ContentMarginRight = 9, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        foreach (string st in new[] { "normal", "hover", "pressed" }) b.AddThemeStyleboxOverride(st, box);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

    // 아래 「자료 A / 자료 B」 칸과 두 버튼의 활성 상태.
    private void UpdateSlots()
    {
        if (_session == null || _slotA == null) return;
        for (int i = 0; i < 2; i++)
        {
            var slot = i == 0 ? _slotA : _slotB;
            var ev = _session.EvidenceAt(i);
            string head = i == 0 ? "[자료 A]" : "[자료 B]";
            slot.Text = head + "\n" + (ev == null ? "— 비어 있음" : InterviewEvidenceDisplay.SlotLine(ev));
            slot.TooltipText = ev == null ? "" : "누르면 선택을 해제합니다.";
            slot.AddThemeColorOverride("font_color", ev == null ? Cyan with { A = 0.45f } : Colors.White);
            slot.AddThemeColorOverride("font_hover_color", Colors.White);
            var box = new StyleBoxFlat
            {
                BgColor = ev == null ? new Color(0.03f, 0.07f, 0.09f, 0.5f) : new Color(0.10f, 0.22f, 0.26f, 0.8f),
                BorderColor = Cyan with { A = ev == null ? 0.25f : 0.7f },
                BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
            };
            foreach (string st in new[] { "normal", "hover", "pressed" }) slot.AddThemeStyleboxOverride(st, box);
        }

        // 자료 A/B 가 바뀌었다 — 접어 둔 대화창이 있으면 펴진다(선택지가 달라졌으므로).
        string picked = string.Join("|", _session.Selected);
        if (picked != _lastSlots) { _lastSlots = picked; _console?.NotifySlotsChanged(); }

        // 고른 자료의 전문을 MON01 상세칸에 펼친다 — 카드 한 줄로는 다 실리지 않는다.
        var focus = _session.EvidenceAt(_session.Selected.Count - 1);
        _console?.SetDetail(focus == null
            ? ""
            : $"[{focus.Header}]  {focus.OneLine}"
              + (string.IsNullOrEmpty(focus.BehaviorDetail) ? "" : "\n· " + focus.BehaviorDetail));
    }

    private Label Lbl(string t, int size, Color c)
    {
        var l = new Label { Text = t };
        l.AddThemeFontOverride("font", _font);
        l.AddThemeFontSizeOverride("font_size", Fz(size));
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    // 어두운 고유색(늑대의 짙은 적색 등)은 통화창의 검은 배경에서 안 보이므로 최소 밝기까지만 올린다.
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

    // 이 화면 좌표가 통화창 위인가. 통화창 밖을 클릭했다면 그건 통화가 아니라
    // 가이드 대사 넘기기다(PrologueAdvanceInput 이 묻는다).
    public bool ContainsPoint(Vector2 screenPosition) =>
        IsOpen && _panel != null && _panel.GetGlobalRect().HasPoint(screenPosition);

    // 이 입력을 3D 모니터로 넘기면 안 되는가. 일반 · 수신 통화 중에는 전부 막고,
    // 휴게시간 심문 중에는 자막 띠 위를 누를 때만 막는다(심문 조작은 MONITOR 01 에서 한다).
    public bool BlocksCrtInput(InputEvent e)
    {
        if (!IsOpen) return false;
        if (_session == null) return true;
        return e is InputEventMouse m && ContainsPoint(m.Position);
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
        if (_barName != null) _barName.Text = "▶ " + (def?.Codename ?? employeeId);
        _frame.Accent = def?.IconColor ?? Cyan;

        // 이름은 그 직원의 고유색 그대로(어두운 색만 살짝 띄워 가독성 확보),
        // 대사는 같은 색에 흰색을 많이 섞어 읽기 편한 밝은 톤으로.
        Color own = def?.IconColor ?? Cyan;
        _speaker.AddThemeColorOverride("font_color", Readable(own));
        _message.AddThemeColorOverride("default_color", Readable(own).Lerp(Colors.White, 0.62f));
        // 「이 자료로 질문 / 두 자료 비교」 도 그 직원의 고유색으로(토끼 = 분홍 …).

        ClearChoices();

        if (_dialogueEvent == LocalInterviewDialogue.EventDay1Interview)
        {
            // 휴게시간 심문 — 직원이 먼저 근무 진술을 몇 마디 하고(세션이 한 번만 만든다),
            // 플레이어는 진술을 골라 캐묻거나 조사 노트의 자료로 묻는다.
            _session = new InterviewSession(employeeId);
            // MON01 은 조사 노트 전용이다 — 시작 탭은 이 직원의 자료(세션 기본값).
            AttachConsole();
            _console.Open(def?.Codename ?? employeeId, Readable(own));
            _expandedCards.Clear();
            _collapsedGroups.Clear();
            _followUps.Clear();
            _intents.Clear();
            _playerLine.Visible = false;
            _console.SetGoal(InvestigationGoal(_session));
            RefreshEvidence();
            _heardOpenings.Clear();
            string greeting = _session.Greeting();
            RecordNpc(greeting, DialogueEntryType.NpcLine, DialogueConversationType.Interview);
            // 인사만 하고 멈춘다. 근무 진술은 플레이어가 기본 질문을 골라야 나온다 —
            // 자동으로 흘려보내면 읽을 새가 없다(1차 플레이테스트에서 확인).
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
                // 근무 기억용 구조화 기록 — "그래서 바로 관리자님께 전화드렸잖아요".
                CallMemoryLog.Record(employeeId, CallRecordKind.Reported, _incidentRoomId, _dialogueEvent);
                RecordNpc(_event.Opening, DialogueEntryType.NpcLine, DialogueConversationType.IncomingCall);
                StartTyping("\"" + _event.Opening + "\"", AfterMode.EventChoices);
                return;
            }
            _dialogueEvent = DialogueRepository.EventGeneralCall; // 해당 이벤트 대사가 없으면 일반 통화로
        }

        _event = null;
        // 관리자가 먼저 건 통화일 때만. (직원이 건 전화가 대사 부족으로 일반 통화로 넘어온 경우는 아니다.)
        if (string.IsNullOrEmpty(dialogueEvent) || dialogueEvent == DialogueRepository.EventGeneralCall)
            CallMemoryLog.Record(employeeId, CallRecordKind.ManagerCalled,
                FacilitySimulation.Instance?.GetEmployeeState(employeeId)?.CurrentRoomId ?? "");
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
        _message.Text = DialogueHighlight.Colorize(text);
        _message.VisibleCharacters = 0;
        // 스탠딩 원화의 입 모양 · 표정(휴게 심문 화면이 읽는다).
        EmployeeMouthAnimator.StartTalking(_employeeId, text);
        // 통화 중 카메라는 전혀 움직이지 않는다(예전의 '말하며 끄덕이는' 흔들림 제거).
    }

    public override void _Process(double delta)
    {
        _blink += (float)delta;
        if (_incoming != null && _incoming.Visible)
            _incoming.Modulate = new Color(1, 1, 1, 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(_blink * 4f)));

        EmployeeMouthAnimator.Tick(delta);
        if (!_typing) return;
        _typeTimer += delta;
        int shown = Mathf.Min(_fullText.Length, (int)(_typeTimer / CharDelay));
        if (shown != _shownChars)
        {
            for (int i = _shownChars; i < shown; i++)
            {
                Sfx.Instance?.PlayVoiceBlip(_employeeId, _fullText[i]);
                EmployeeMouthAnimator.NoticeCharacter(_fullText[i]);
            }
            _shownChars = shown;
        }
        _message.VisibleCharacters = shown;
        if (shown >= _fullText.Length)
        {
            _typing = false;
            EmployeeMouthAnimator.StopTalking();
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
        AddTail(ChoiceButton("통화를 종료한다.", CloseCall));
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

    // 심문 기본 화면 — 직원의 근무 진술 블록과, 고른 진술에서 이어지는 꼬리질문만.
    // (예전의 기본 질문 세 개는 목록으로 띄우지 않는다 — 그 답이 곧 진술 블록이다.)
    // 심문 기본 선택지 — 전부 자막 띠 안에 있다.
    //   ① 방금 진술에서 이어지는 꼬리질문(0~2)
    //   ② 조사 노트에서 고른 자료로 묻기 / 두 자료를 함께 제시하기
    //   ③ 통화 종료(항상 맨 아래)
    private void BuildInterviewMenu()
    {
        ClearChoices();
        _followUps.Clear();
        _intents.Clear();
        RefreshEvidence();
        if (_session == null) return;

        // ① 기본 질문 세 개. 이미 들은 것은 ✓ — 목록에서 빠지지 않는다(다시 들을 수 있다).
        foreach (var st in _session.Openings)
        {
            var captured = st;
            string label = (_heardOpenings.Contains(st.Index) ? "✓  " : "") + OpeningLabel(st.QuestionId);
            _choices.AddChild(InterviewChoiceButton(label, () => PlayOpening(captured)));
        }

        // ② 방금 들은 진술에서 이어지는 꼬리질문(최대 2).
        int room = 2;
        foreach (var fq in _session.OpeningFollowUps())
        {
            if (room-- <= 0) break;
            var captured = fq;
            string text = (_session.WasAskedFollowUp(fq) ? "✓  " : "") + fq.Text;
            _choices.AddChild(InterviewChoiceButton(text, () => AskOpeningFollowUp(captured)));
        }

        // ③ MONITOR 01 에서 고른 자료로 묻기 / 두 자료 제시.
        AddEvidenceChoice();
        // ④ 세션을 끝내는 유일한 버튼.
        AddTail(InterviewChoiceButton("통화를 종료한다.", CloseCall));
    }

    // 카드 본문을 선택지 라벨에 넣을 만큼만 자른다.
    private static string Short(string text, int max)
    {
        text = (text ?? "").Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }

    // 조사 노트에서 자료를 고르거나 뺐다 — 목록과 함께 자막 띠의 선택지도 다시 그린다.
    // (무엇을 물을 수 있는지가 곧 고른 자료에 달려 있으므로 둘은 항상 같이 움직인다.)
    private void OnEvidencePicked()
    {
        RefreshEvidence();
        if (!_typing && _after == AfterMode.InterviewMenu) BuildInterviewMenu();
    }

    // 조사 노트에서 고른 자료로 물을 수 있는 것. 자료를 고르지 않았으면 안내만 남긴다.
    //
    // 두 장을 골랐을 때 추궁이 성립하면 선택지 글이 곧 추궁 문장이다 — 무엇을 들이밀게
    // 되는지 누르기 전에 읽을 수 있어야 한다. 성립하지 않아도 누를 수 있다(§3-1).
    private void AddEvidenceChoice()
    {
        int picked = _session.Selected.Count;
        if (picked == 0)
        {
            // 물어볼 꼬리질문이 하나도 없을 때만 안내를 남긴다 — 선택지 네 줄을 넘기지 않게.
            if (_choices.GetChildCount() == 0)
                _choices.AddChild(Wrap(Lbl("MONITOR 01 의 조사 노트에서 자료를 고르면 그 자료로 물어볼 수 있습니다.",
                    13, new Color(0.55f, 0.66f, 0.70f))));
            return;
        }
        if (picked == 1)
        {
            var one = _session.EvidenceAt(0);
            _choices.AddChild(InterviewChoiceButton($"[{Short(one?.Body, 10)}]로 묻는다.", OnAskWithEvidence));
            return;
        }

        var result = _session.CheckContradiction();
        bool holds = result.Kind != ConfrontKind.None;
        string label = holds ? result.QuestionText : "두 자료를 함께 제시한다.";
        var b = InterviewChoiceButton(label, OnConfront);
        // 성립한 추궁만 붉게 — 성립하지 않은 조합에는 아무 표시도 붙지 않는다.
        if (holds) TintAction(b, new Color(1f, 0.45f, 0.38f));
        _choices.AddChild(b);
    }

    // MON01 맨 위 한 줄 — 오늘 무엇을 밝혀야 하는가.
    //
    // 조사 자료(= 시설 로그 화면에 실제로 뜬 줄)에서만 만든다. 방해공작 기록이 있으면
    // 그것을, 없으면 가장 이른 사고를 쓴다. 범인 이름은 어디에도 없다 — 그건 여전히
    // 플레이어가 좁혀야 할 몫이다.
    private static string InvestigationGoal(InterviewSession session)
    {
        InterviewEvidence pick = null;
        foreach (var e in InterviewEvidenceBoard.BuildAll(session.EmployeeId))
        {
            if (e.Kind != EvidenceKind.Incident) continue;
            bool sabotage = e.IncidentType == NSP.Data.LogEventType.Sabotage;
            if (pick == null || (sabotage && pick.IncidentType != NSP.Data.LogEventType.Sabotage)) pick = e;
            if (sabotage) break;
        }
        if (pick == null) return "오늘 시설 기록에 남은 사건이 없습니다.";

        string room = InterviewEvidenceBoard.RoomName(pick.SubjectRoomId);
        string when = DialogueClock.Text(pick.AnchorTime);
        return pick.IncidentType == NSP.Data.LogEventType.Sabotage
            ? $"{when} {room} 방해공작 — 그 시각 {room}에 있던 사람은?"
            : $"{when} {room} 사고 — 그 시각 {room}에 있던 사람은?";
    }

    // 기본 질문 하나를 고르면 그 답(세션이 만들어 둔 최초 진술)을 그대로 말한다.
    //
    // 새로 생성하지 않는 이유 — 같은 질문을 다시 골랐을 때 문장이 바뀌면 "아까 뭐라고 했더라"
    // 를 확인할 수가 없다. 진술은 근무 한 번에 하나로 고정이고, 몇 번이든 다시 들을 수 있다.
    private void PlayOpening(InterviewSession.OpeningStatement st)
    {
        if (_session == null || st == null) return;
        ClearChoices();
        // 이 진술의 꼬리질문이 목록에 뜨도록 고른 상태로 만든다(같은 것을 다시 눌러도 유지).
        if (_session.SelectedOpening != st.Index) _session.SelectOpening(st.Index);
        _heardOpenings.Add(st.Index);

        string question = OpeningLabel(st.QuestionId);
        LocalInterviewDialogue.RecordTurn(_employeeId, question, st.Text);
        RecordPlayer(question, DialogueConversationType.Interview);
        RecordNpc(st.Text, DialogueEntryType.NpcResponse, DialogueConversationType.Interview);
        ShowPlayerLine(question);
        RefreshEvidence();
        _console?.FlashNotes();
        StartTyping("\"" + st.Text + "\"", AfterMode.InterviewMenu);
    }

    // 기본 질문의 문구. InterviewQuestionFactory.BasicQuestions 와 같은 문장을 쓴다.
    private static string OpeningLabel(string questionId) => questionId switch
    {
        DialogueQuestions.ShiftReview => "오늘 근무는 어땠습니까?",
        DialogueQuestions.Suspicious => "수상한 행동을 한 사람을 봤습니까?",
        _ => "오늘 이상한 점을 느꼈습니까?",
    };

    private static Label Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    // 진술에서 나온 꼬리질문 → 기존 꼬리질문 답변(FollowUpAnswer).
    private void AskOpeningFollowUp(FollowUpQuestion q)
    {
        if (_session == null || q == null) return;
        ClearChoices();
        var turn = _session.AskFollowUp(q);
        LocalInterviewDialogue.RecordTurn(_employeeId, turn.QuestionText, turn.Answer);
        RecordPlayer(turn.QuestionText, DialogueConversationType.Interview);
        RecordNpc(turn.Answer, DialogueEntryType.NpcResponse, DialogueConversationType.Interview);
        ShowPlayerLine(turn.QuestionText);
        RefreshEvidence();
        _console?.FlashNotes();
        StartTyping("\"" + turn.Answer + "\"", AfterMode.InterviewMenu);
    }

    // 「선택한 자료로 질문」 — 고른 자료로 물을 수 있는 것들을 왼쪽에 펼친다.
    private void OnAskWithEvidence()
    {
        if (_session == null || _session.Selected.Count != 1) return;
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
            _choices.AddChild(InterviewChoiceButton(Mark(q), () => AskInterview(captured)));
        }
        AddTail(InterviewChoiceButton("다른 질문을 한다.", BuildInterviewMenu));
    }

    // 이미 물어본 질문에는 체크 표시만 붙인다 — 목록에서 사라지지는 않는다.
    private string Mark(InterviewQuestion q) =>
        _session != null && _session.WasAsked(q) ? "✓  " + q.Text : q.Text;

    private void AskInterview(InterviewQuestion q)
    {
        ClearChoices();
        var turn = _session.Ask(q);
        _followUps = turn.FollowUps;
        LocalInterviewDialogue.RecordTurn(_employeeId, turn.QuestionText, turn.Answer);
        RecordPlayer(turn.QuestionText, DialogueConversationType.Interview);
        RecordNpc(turn.Answer, DialogueEntryType.NpcResponse, DialogueConversationType.Interview);
        // 방금 내가 고른 질문을 다시 보여 주지 않는다 — 답변만 뜬다.
        ShowPlayerLine("");
        RefreshEvidence();
        _console?.FlashNotes();
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
        AddTail(InterviewChoiceButton("다른 질문을 한다.", BuildInterviewMenu));
    }

    // 두 자료를 함께 제시한다. 성립하든 아니든 직원은 대답한다 —
    // 화면이 "그건 아니다" 라고 막으면 틀린 조합을 대 볼 자유가 사라진다(§3-1).
    private void OnConfront()
    {
        if (_session == null || !_session.CanTryConfront) return;
        var result = _session.CheckContradiction();

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
        EmployeeMouthAnimator.StopTalking();
        ShowPlayerLine("");
        _message.Text = DialogueHighlight.Colorize("— " + text);
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
        // 사고·비명 통화의 첫 선택지는 "확인하러 가라", 둘째는 "대기하라"(IncomingCallDirector 와 같은 규약).
        if (_dialogueEvent is DialogueRepository.EventAccidentNearby or DialogueRepository.EventScreamNextRoom)
            CallMemoryLog.Record(_employeeId, index == 0 ? CallRecordKind.OrderedGo : CallRecordKind.OrderedStay,
                _incidentRoomId, _dialogueEvent);
        RecordPlayer(c.Text, DialogueConversationType.IncomingCall);
        RecordNpc(c.Reply, DialogueEntryType.NpcResponse, DialogueConversationType.IncomingCall);
        EmitSignal(SignalName.EventChoiceMade, _employeeId, _dialogueEvent, index);
        StartTyping("\"" + c.Reply + "\"", AfterMode.EndOnly);
    }

    // 관리자의 말도 "누구와의 통화였는가"를 함께 남긴다 — 대화 기록에서 직원별로
    // 골라 볼 때 질문과 대답이 갈라지지 않게 하기 위해서다.
    private void RecordPlayer(string text, DialogueConversationType conversationType) =>
        DialogueHistory.Instance?.AddEntry("manager", "관리자", DialogueEntryType.PlayerChoice,
            text, conversationType, _employeeId);

    private void RecordNpc(string text, DialogueEntryType entryType, DialogueConversationType conversationType)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(_employeeId);
        DialogueHistory.Instance?.AddEntry(_employeeId, def?.Codename ?? _employeeId, entryType,
            text, conversationType, _employeeId);
    }

    private void BuildEndOnly()
    {
        ClearChoices();
        AddTail(ChoiceButton("통화를 종료한다.", CloseCall));
    }

    private static void TintAction(Button b, Color c)
    {
        if (b == null) return;
        b.AddThemeColorOverride("font_color", c);
        b.AddThemeColorOverride("font_disabled_color", c with { A = 0.55f });
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            if (b.GetThemeStylebox(state) is StyleBoxFlat box)
            {
                var tinted = (StyleBoxFlat)box.Duplicate();
                tinted.BorderColor = c with { A = state == "disabled" ? 0.25f : state == "normal" ? 0.55f : 1f };
                b.AddThemeStyleboxOverride(state, tinted);
            }
    }

    private Button ChoiceButton(string text, System.Action onPressed)
    {
        var b = new Button { Text = "  ›  " + text, Alignment = HorizontalAlignment.Left };
        b.AddThemeFontOverride("font", _font);
        b.AddThemeFontSizeOverride("font_size", Fz(15));
        b.CustomMinimumSize = new Vector2(0, 40);
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
        // 아직 고를 수 없는 버튼(자료 미선택 등)도 글씨는 읽혀야 한다.
        // 엔진 기본 비활성색은 바탕과 거의 같은 회색이라 글자가 사라져 버렸다.
        var off = (StyleBoxFlat)normal.Duplicate();
        off.BgColor = new Color(0.05f, 0.09f, 0.11f, 0.45f);
        off.BorderColor = Cyan with { A = 0.18f };
        b.AddThemeColorOverride("font_disabled_color", new Color(0.46f, 0.62f, 0.68f));
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("disabled", off);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += () => onPressed();
        return b;
    }

    private Button InterviewChoiceButton(string text, System.Action onPressed)
    {
        var b = ChoiceButton(text, onPressed);
        b.AddThemeFontSizeOverride("font_size", Fz(15));
        b.CustomMinimumSize = new Vector2(0, 36);
        return b;
    }

    // 심문 창도 일반 통화와 **같은 자리 · 같은 크기**다. 1차의 자막 띠 레이아웃은 버렸다 —
    // 화면 아래 띠로는 답변이 한 문장씩만 남아 대화가 되지 않았고, MONITOR 01 을 가렸다.
    // 가리는 것이 문제라면 끊지 말고 접는다(SetCollapsed).
    private void SetInterviewLayout(bool interview)
    {
        // 손잡이(와 접기 버튼)는 심문 창에서만 쓴다.
        if (_dragBar != null) _dragBar.Visible = interview;
        _dragging = false;
        _collapsed = false;
        if (_root != null) _root.Visible = true;
        if (_barName != null) _barName.Visible = false;
        if (_collapseBtn != null) _collapseBtn.Text = "▼ 접기";

        _panel.AnchorLeft = 0.24f;
        _panel.AnchorRight = 0.76f;
        _panel.AnchorTop = 0.62f;
        _panel.AnchorBottom = 0.95f;
        _panel.OffsetTop = 0f;
        _panel.OffsetBottom = 0f;
        // 심문 중에는 위쪽 ✕ 를 숨긴다 — 끊는 길은 선택지 맨 아래 한 곳뿐이어야 한다.
        if (_hangUp != null) _hangUp.Visible = !interview;
    }

    // ESC — 통화 중이면 끊는다(심문 콘솔은 자기 종료 흐름을 쓴다).
    public override void _UnhandledInput(InputEvent e)
    {
        if (_panel is not { Visible: true } || _hangUp is not { Visible: true }) return;
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        CloseCall();
        GetViewport().SetInputAsHandled();
    }

    private void ClearChoices()
    {
        foreach (var box in new Control[] { _choices, _tail })
        {
            if (box == null) continue;
            foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
        }
    }

    // 목록의 맨 아래 한 줄. 조사 자료 버튼 두 칸보다 항상 아래에 놓인다.
    private void AddTail(Button b)
    {
        _tail.AddChild(b);
    }

    private void CloseCall()
    {
        _panel.Visible = false;
        _typing = false;
        Sfx.Instance?.StopVoiceBlip();
        EmployeeMouthAnimator.Reset();
        ClearChoices();
        _session = null;
        _console?.Close();
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
