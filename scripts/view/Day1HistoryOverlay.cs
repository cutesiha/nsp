using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// DAY1 시설 로그/대화 기록 열람 오버레이. 표시 중에도 SceneTree를 멈추지 않으며,
// 투명 입력 차단막만으로 뒤쪽 CRT 클릭을 막는다.
public partial class Day1HistoryOverlay : CanvasLayer
{
    private const string ToggleLogAction = "toggle_log_history";
    private const string ToggleDialogueAction = "toggle_dialogue_history";
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Paper = new(0.855f, 0.80f, 0.645f);
    private static readonly Color Ink = new(0.18f, 0.14f, 0.09f);
    private static readonly Color InkDim = new(0.42f, 0.35f, 0.24f);
    private static readonly Color InkRed = new(0.55f, 0.14f, 0.10f);
    // 시설 로그의 중요도 색. 경고 단말기(AlertTerminalView)와 같은 팔레트를 쓴다.
    private static readonly Color LogNormal = new(0.82f, 0.96f, 0.98f);
    private static readonly Color LogMove = new(0.45f, 0.92f, 0.88f);
    private static readonly Color LogWarning = new(0.98f, 0.70f, 0.20f);
    private static readonly Color LogCritical = new(1f, 0.44f, 0.26f);
    private static readonly Color LogSabotage = new(1f, 0.26f, 0.24f);
    private static readonly Color LogRecovery = new(0.40f, 0.95f, 0.50f);
    private static readonly Color LogTime = new(0.45f, 0.66f, 0.72f);

    private enum WindowMode { None, Log, Dialogue, Objectives }

    public static Day1HistoryOverlay Instance { get; private set; }
    public bool IsWindowOpen => _mode != WindowMode.None;
    // 튜토리얼(TutorialDirector)이 "플레이어가 실제로 이 창을 열었는가"를 확인한다.
    public bool IsLogOpen => _mode == WindowMode.Log;
    public bool IsDialogueOpen => _mode == WindowMode.Dialogue;
    public bool IsObjectivesOpen => _mode == WindowMode.Objectives;

    // 오늘의 업무 창에서 "근무 종료" 를 눌렀을 때. ShiftFlowController 가 받는다.
    public event System.Action EndShiftRequested;

    private WindowMode _mode;
    private Control _root;
    private Control _icons;
    private ColorRect _scrim;
    private Panel _logPanel;
    private Panel _dialoguePanel;
    private ScrollContainer _logScroll;
    private ScrollContainer _dialogueScroll;
    private VBoxContainer _logRows;
    private VBoxContainer _dialogueRows;
    private Font _body;
    private Font _serif;
    private int _logRendered;
    // 화면용으로 해석된 로그. EventLog 원본은 그대로 두고 여기에만 요약본을 만든다.
    private List<DisplayLogEntry> _displayLog = new();
    private int _dialogueRendered;
    // 대화 기록에서 지금 골라 둔 직원들. 비어 있으면 전부 보여준다.
    private readonly HashSet<string> _dialogueFilter = new();
    private HBoxContainer _dialogueTabs;

    // 오늘의 업무 창.
    private Panel _objPanel;
    private VBoxContainer _objRows;
    private Label _objTitle, _objTime, _objHint;
    private Button _objButton, _objEndBtn;
    private string _objSignature = "";
    private bool _objAutoShown;
    private float _objTick;
    private bool _logStick;
    private bool _dialogueStick;
    private double _logOldScroll;
    private double _dialogueOldScroll;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        SetProcessInput(true);
        Layer = 115; // 통화(90) 위, ESC 메뉴(120) 아래
        _body = ViewFont.Default;
        _serif = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf") ?? _body;
        BuildUi();

        if (EventLog.Instance != null)
        {
            EventLog.Instance.EntryLogged += OnLogAdded;
            EventLog.Instance.Cleared += OnLogCleared;
        }
        if (DialogueHistory.Instance != null)
        {
            DialogueHistory.Instance.EntryAdded += OnDialogueAdded;
            DialogueHistory.Instance.Cleared += OnDialogueCleared;
        }
    }

    public override void _ExitTree()
    {
        if (GetViewport() != null)
            GetViewport().SizeChanged -= RefreshRootSize;
        if (EventLog.Instance != null)
        {
            EventLog.Instance.EntryLogged -= OnLogAdded;
            EventLog.Instance.Cleared -= OnLogCleared;
        }
        if (DialogueHistory.Instance != null)
        {
            DialogueHistory.Instance.EntryAdded -= OnDialogueAdded;
            DialogueHistory.Instance.Cleared -= OnDialogueCleared;
        }
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        bool showIcons = GameState.Instance?.CurrentPhase is GamePhase.Live or GamePhase.Rest;
        if (_icons.Visible != showIcons) _icons.Visible = showIcons;

        // 기록은 DAY1의 근무/정산/휴게시간에만 열람한다. 새 판 타이틀이나 배치표로
        // 돌아가면 남아 있던 오버레이만 닫고 데이터 초기화는 새 게임 시작 지점이 맡는다.
        if (IsWindowOpen && !CanOpen()) CloseWindow();
        TickObjectives((float)delta);
    }

    public override void _Input(InputEvent e)
    {
        if (PauseMenu.Instance?.IsOpen == true) return;

        if (e.IsActionPressed(ToggleLogAction, allowEcho: false))
        {
            ToggleLog();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e.IsActionPressed(ToggleDialogueAction, allowEcho: false))
        {
            ToggleDialogue();
            GetViewport().SetInputAsHandled();
            return;
        }
        // 오늘의 업무는 별도 입력 액션을 만들지 않고 T 키로 연다(프로젝트 설정 불변).
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.T })
        {
            ToggleObjectives();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (IsWindowOpen && e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            CloseWindow();
            GetViewport().SetInputAsHandled();
        }
    }

    private bool CanOpen()
    {
        return GameState.Instance?.CurrentPhase is GamePhase.Live or GamePhase.Settlement or GamePhase.Rest;
    }

    private void ToggleLog()
    {
        if (_mode == WindowMode.Log) CloseWindow();
        else if (CanOpen()) OpenLog();
    }

    private void ToggleDialogue()
    {
        if (_mode == WindowMode.Dialogue) CloseWindow();
        else if (CanOpen()) OpenDialogue();
    }

    private void ToggleObjectives()
    {
        if (_mode == WindowMode.Objectives) CloseWindow();
        // 오늘의 업무는 근무 중에만 의미가 있다.
        else if (GameState.Instance?.CurrentPhase == GamePhase.Live) OpenObjectives();
    }

    private void OpenObjectives()
    {
        _mode = WindowMode.Objectives;
        _scrim.Color = new Color(0f, 0f, 0f, 0.30f);
        _scrim.Visible = true;
        _logPanel.Visible = false;
        _dialoguePanel.Visible = false;
        _objPanel.Visible = true;
        _objSignature = "";
        RefreshObjectives();
    }

    private void OpenLog()
    {
        _mode = WindowMode.Log;
        _scrim.Color = new Color(0f, 0f, 0f, 0.38f);
        _scrim.Visible = true;
        _logPanel.Visible = true;
        _dialoguePanel.Visible = false;
        RebuildLog();
    }

    private void OpenDialogue()
    {
        _mode = WindowMode.Dialogue;
        _scrim.Color = new Color(0f, 0f, 0f, 0.55f);
        _scrim.Visible = true;
        _logPanel.Visible = false;
        _dialoguePanel.Visible = true;
        RebuildDialogue();
    }

    public void CloseWindow()
    {
        _mode = WindowMode.None;
        _scrim.Visible = false;
        _logPanel.Visible = false;
        _dialoguePanel.Visible = false;
        if (_objPanel != null) _objPanel.Visible = false;
    }

    private void BuildUi()
    {
        _root = new Control
        {
            Name = "HistoryRoot",
            Position = Vector2.Zero,
            Size = GetViewport().GetVisibleRect().Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_root);
        GetViewport().SizeChanged += RefreshRootSize;

        _icons = new HBoxContainer
        {
            Name = "HistoryIcons",
            AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
            OffsetLeft = -598f, OffsetRight = -20f, OffsetTop = -80f, OffsetBottom = -20f,
            MouseFilter = Control.MouseFilterEnum.Pass,
            // 오른쪽에 붙인다 — 휴게시간에 '업무' 버튼이 빠져도 로그/대화 기록이 화면 오른쪽 끝에 남는다.
            Alignment = BoxContainer.AlignmentMode.End,
        };
        _icons.AddThemeConstantOverride("separation", 10);
        _icons.Visible = false;
        _root.AddChild(_icons);

        // 오늘의 업무 — 평소에는 진행도만 작게 보여주고, 누르면 창이 열린다.
        // 맨 왼쪽에 둔다 — 휴게시간에 이 버튼이 사라져도 나머지가 오른쪽에 그대로 붙어 있다.
        _objButton = MonitorUi.Button("T  업무 0/0", new Color(0.62f, 0.92f, 0.70f), _body,
            ToggleObjectives, ViewFont.FS(17));
        _objButton.Name = "ObjectivesButton";
        _objButton.TooltipText = "오늘의 업무 (T)";
        _objButton.CustomMinimumSize = new Vector2(186, 56);
        _objButton.MouseFilter = Control.MouseFilterEnum.Stop;
        _icons.AddChild(_objButton);

        Button logIcon = MonitorUi.Button("L  로그", Cyan, _body, ToggleLog, ViewFont.FS(17));
        logIcon.Name = "LogHistoryButton";
        logIcon.TooltipText = "시설 로그";
        logIcon.CustomMinimumSize = new Vector2(134, 56);
        logIcon.MouseFilter = Control.MouseFilterEnum.Stop;
        _icons.AddChild(logIcon);

        Button dialogueIcon = MonitorUi.Button("D  대화 기록", new Color(0.88f, 0.76f, 0.48f), _body, ToggleDialogue, ViewFont.FS(17));
        dialogueIcon.Name = "DialogueHistoryButton";
        dialogueIcon.TooltipText = "DAY1 대화 기록";
        dialogueIcon.CustomMinimumSize = new Vector2(216, 56);
        dialogueIcon.MouseFilter = Control.MouseFilterEnum.Stop;
        _icons.AddChild(dialogueIcon);

        _scrim = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.38f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_scrim);

        BuildLogPanel(_root);
        BuildDialoguePanel(_root);
        BuildObjectivePanel(_root);
    }

    private void RefreshRootSize()
    {
        if (_root != null) _root.Size = GetViewport().GetVisibleRect().Size;
    }

    private void BuildLogPanel(Control root)
    {
        _logPanel = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -470f, OffsetRight = 470f, OffsetTop = -340f, OffsetBottom = 340f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _logPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.09f, 0.11f, 0.91f),
            BorderColor = Cyan with { A = 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        root.AddChild(_logPanel);

        var frame = new HologramFrame { Accent = Cyan, MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _logPanel.AddChild(frame);

        Label title = LabelFor("DAY1 FACILITY LOG  /  시설 로그", 24, Cyan, _body);
        title.Position = new Vector2(28, 38);
        title.Size = new Vector2(810, 38);
        _logPanel.AddChild(title);
        _logPanel.AddChild(CloseButton(false));

        _logScroll = new ScrollContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 28f, OffsetRight = -28f, OffsetTop = 88f, OffsetBottom = -28f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _logPanel.AddChild(_logScroll);

        _logRows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _logRows.AddThemeConstantOverride("separation", 8);
        _logScroll.AddChild(_logRows);
    }

    private void BuildDialoguePanel(Control root)
    {
        _dialoguePanel = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -430f, OffsetRight = 430f, OffsetTop = -360f, OffsetBottom = 360f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _dialoguePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Paper,
            BorderColor = new Color(0.32f, 0.24f, 0.13f),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
        });
        root.AddChild(_dialoguePanel);
        _dialoguePanel.AddChild(new DocumentPaperTexture { MouseFilter = Control.MouseFilterEnum.Ignore });

        Label doc = LabelFor("DOC NO. NSP-D1-TRANSCRIPT   FACILITY CONTROL DEPT.", 13, InkDim, _body);
        doc.Position = new Vector2(42, 26);
        doc.Size = new Vector2(700, 24);
        _dialoguePanel.AddChild(doc);

        Label title = LabelFor("DAY1 대화 기록", 37, Ink, _serif);
        title.Position = new Vector2(42, 48);
        title.Size = new Vector2(700, 52);
        _dialoguePanel.AddChild(title);
        _dialoguePanel.AddChild(CloseButton(true));

        // 직원별로 골라 보는 줄. 아무것도 고르지 않으면 전체 기록이 그대로 뜬다.
        _dialogueTabs = new HBoxContainer
        {
            AnchorRight = 1f,
            OffsetLeft = 42f, OffsetRight = -42f, OffsetTop = 106f, OffsetBottom = 150f,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _dialogueTabs.AddThemeConstantOverride("separation", 6);
        _dialoguePanel.AddChild(_dialogueTabs);

        var rule = new HSeparator
        {
            AnchorRight = 1f,
            OffsetLeft = 42f, OffsetRight = -42f, OffsetTop = 158f, OffsetBottom = 160f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rule.AddThemeColorOverride("separator", new Color(0.38f, 0.29f, 0.16f, 0.75f));
        _dialoguePanel.AddChild(rule);

        _dialogueScroll = new ScrollContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 42f, OffsetRight = -42f, OffsetTop = 172f, OffsetBottom = -34f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _dialoguePanel.AddChild(_dialogueScroll);

        _dialogueRows = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _dialogueRows.AddThemeConstantOverride("separation", 12);
        _dialogueScroll.AddChild(_dialogueRows);
    }

    // --- 오늘의 업무 -----------------------------------------------------

    private void BuildObjectivePanel(Control root)
    {
        _objPanel = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -382f, OffsetRight = 382f, OffsetTop = -250f, OffsetBottom = 250f,
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _objPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.09f, 0.11f, 0.94f),
            BorderColor = Cyan with { A = 0.6f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        root.AddChild(_objPanel);

        var frame = new HologramFrame { Accent = Cyan, MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _objPanel.AddChild(frame);

        _objTitle = LabelFor("DAY 1 - 오늘의 업무", 24, Cyan, _body);
        _objTitle.Position = new Vector2(28, 30);
        _objTitle.Size = new Vector2(500, 34);
        _objPanel.AddChild(_objTitle);

        _objTime = LabelFor("남은 근무시간  --:--", 20, LogNormal, _body);
        _objTime.Position = new Vector2(28, 70);
        _objTime.Size = new Vector2(600, 28);
        _objPanel.AddChild(_objTime);
        _objPanel.AddChild(CloseButton(false));

        _objRows = new VBoxContainer
        {
            AnchorRight = 1f,
            OffsetLeft = 28f, OffsetRight = -28f, OffsetTop = 112f, OffsetBottom = -110f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _objRows.AddThemeConstantOverride("separation", 7);
        _objPanel.AddChild(_objRows);

        _objEndBtn = MonitorUi.Button("근무 종료", new Color(1f, 0.78f, 0.35f), _body,
            () => { CloseWindow(); EndShiftRequested?.Invoke(); }, ViewFont.FS(19));
        _objEndBtn.AnchorLeft = 0.5f; _objEndBtn.AnchorRight = 0.5f;
        _objEndBtn.AnchorTop = 1f; _objEndBtn.AnchorBottom = 1f;
        _objEndBtn.OffsetLeft = -110f; _objEndBtn.OffsetRight = 110f;
        _objEndBtn.OffsetTop = -92f; _objEndBtn.OffsetBottom = -44f;
        _objEndBtn.MouseFilter = Control.MouseFilterEnum.Stop;
        _objPanel.AddChild(_objEndBtn);

        _objHint = LabelFor("", 14, LogTime, _body);
        _objHint.HorizontalAlignment = HorizontalAlignment.Center;
        _objHint.AnchorRight = 1f; _objHint.AnchorTop = 1f; _objHint.AnchorBottom = 1f;
        _objHint.OffsetLeft = 20f; _objHint.OffsetRight = -20f;
        _objHint.OffsetTop = -38f; _objHint.OffsetBottom = -14f;
        _objPanel.AddChild(_objHint);
    }

    private void TickObjectives(float delta)
    {
        var gs = GameState.Instance;
        bool live = gs?.CurrentPhase == GamePhase.Live;

        // 버튼은 근무 중 + 오늘 업무가 등록된 날에만. 닫혀 있어도 진행도(1/2)는 읽힌다.
        // (가상 시뮬레이션 교육일에는 업무 데이터가 없으므로 버튼도 뜨지 않는다.)
        bool show = live && DayObjectives.Today != null;
        if (_objButton != null && _objButton.Visible != show) _objButton.Visible = show;

        if (!live)
        {
            _objAutoShown = false;
            if (_mode == WindowMode.Objectives) CloseWindow();
            return;
        }

        _objTick += delta;
        if (_objTick >= 0.25f)
        {
            _objTick = 0f;
            if (_objButton != null)
            {
                string label = "T  " + DayObjectives.ShortStatus();
                if (_objButton.Text != label) _objButton.Text = label;
            }
        }

        // DAY 가 시작되면 한 번만 저절로 열어 "오늘 뭘 해야 하는지"를 먼저 보여준다.
        // 교육(가상 시뮬레이션)은 GUIDE-0 가 직접 안내하므로 건드리지 않는다.
        if (!_objAutoShown && !DayFeatures.IsTutorialDay && DayObjectives.Today != null)
        {
            _objAutoShown = true;
            if (_mode == WindowMode.None) OpenObjectives();
        }

        if (_mode == WindowMode.Objectives) RefreshObjectives();
    }

    private void RefreshObjectives()
    {
        if (_objPanel == null || !_objPanel.Visible) return;

        int day = GameState.Instance?.CurrentDay ?? 1;
        _objTitle.Text = $"{DayFeatures.DayLabel(day)} - 오늘의 업무";

        // 남은 시간은 매 프레임 갱신한다. 30초 아래로 내려가면 붉게만 바꾼다
        // (화면 전체를 깜빡이게 하지 않는다).
        float left = DayObjectives.RemainingSeconds;
        _objTime.Text = $"남은 근무시간   {(int)left / 60:00}:{(int)left % 60:00}";
        _objTime.AddThemeColorOverride("font_color", left <= 30f ? LogCritical : LogNormal);

        var lines = DayObjectives.Lines();
        // 내용이 바뀐 경우에만 줄을 다시 만든다(매 프레임 새로 만들 이유가 없다).
        string sig = string.Join("|", lines.Select(l => $"{l.Def.ObjectiveId}:{l.Done}:{l.ProgressText}"));
        if (sig != _objSignature)
        {
            _objSignature = sig;
            RebuildObjectiveRows(lines);
        }

        bool can = DayObjectives.CanEndShift;
        if (_objEndBtn.Disabled == can) _objEndBtn.Disabled = !can;
        _objHint.Text = can
            ? "필수 업무 완료 — 더 일하거나, 지금 근무를 마칠 수 있습니다."
            : "필수 업무를 완료해야 근무를 종료할 수 있습니다.";
    }

    private void RebuildObjectiveRows(System.Collections.Generic.List<DayObjectives.Line> lines)
    {
        ClearRows(_objRows);
        if (lines.Count == 0)
        {
            AddEmpty(_objRows, "등록된 업무가 없습니다.", Cyan with { A = 0.65f });
            return;
        }

        bool headedRequired = false, headedOptional = false;
        foreach (var l in lines.Where(x => x.Required).Concat(lines.Where(x => !x.Required)))
        {
            if (l.Required && !headedRequired)
            {
                headedRequired = true;
                _objRows.AddChild(SectionHead("필수 업무"));
            }
            else if (!l.Required && !headedOptional)
            {
                headedOptional = true;
                _objRows.AddChild(SectionHead("선택 업무"));
            }
            _objRows.AddChild(ObjectiveRow(l));
        }
    }

    private Label SectionHead(string text)
    {
        var l = LabelFor(text, 15, LogTime, _body);
        l.CustomMinimumSize = new Vector2(0, 26);
        return l;
    }

    // "☑ 봉쇄 코어 복구율 18% 달성        18.4 / 18%"
    private RichTextLabel ObjectiveRow(DayObjectives.Line l)
    {
        var line = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 30),
        };
        line.AddThemeFontOverride("normal_font", _body);
        line.AddThemeFontSizeOverride("normal_font_size", ViewFont.FS(18));

        // 근무가 끝나야 확정되는 업무는 근무 중에 '달성'으로 보여주지 않는다 —
        // 시작하자마자 초록 별이 켜져 있으면 이미 끝낸 줄 안다.
        bool settled = l.Done && !l.Provisional;
        string mark = l.Required ? (settled ? "☑" : "□") : (settled ? "★" : "☆");
        Color col = settled ? LogRecovery : (l.Required ? LogNormal : LogTime);
        string tail = l.ProgressText;
        if (l.Provisional) tail += l.Done ? "  유지 중" : "  실패";
        line.Text = $"[color=#{col.ToHtml(false)}]  {mark}  {Escape(l.Def.DisplayText)}[/color]" +
                    (string.IsNullOrEmpty(tail)
                        ? ""
                        : $"   [color=#{LogTime.ToHtml(false)}]{Escape(tail)}[/color]");
        return line;
    }

    private Button CloseButton(bool paper)
    {
        Color color = paper ? InkDim : Cyan;
        var close = new Button
        {
            Text = "✕",
            AnchorLeft = 1f, AnchorRight = 1f,
            OffsetLeft = -62f, OffsetRight = -18f, OffsetTop = 18f, OffsetBottom = 62f,
            TooltipText = "닫기",
        };
        close.AddThemeFontOverride("font", _body);
        close.AddThemeFontSizeOverride("font_size", ViewFont.FS(22));
        close.AddThemeColorOverride("font_color", color);
        close.AddThemeColorOverride("font_hover_color", Colors.White);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, paper ? 0f : 0.12f),
            BorderColor = color with { A = 0.5f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = paper ? InkRed with { A = 0.82f } : Cyan with { A = 0.25f };
        close.AddThemeStyleboxOverride("normal", normal);
        close.AddThemeStyleboxOverride("hover", hover);
        close.AddThemeStyleboxOverride("pressed", hover);
        close.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        close.Pressed += CloseWindow;
        return close;
    }

    private void RebuildLog()
    {
        ClearRows(_logRows);
        _logRendered = 0;
        // 교육일(DAY0)에도 그날의 기록이 그대로 뜬다 — 1 로 못 박으면 튜토리얼이 통째로 빈다.
        _displayLog = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(),
            GameState.Instance?.CurrentDay ?? 1);
        foreach (var row in _displayLog) AppendLogRow(row);
        if (_logRendered == 0) AddEmpty(_logRows, "아직 기록된 시설 로그가 없습니다.", Cyan with { A = 0.65f });
        QueueLogScroll(true, 0);
    }

    private void RebuildDialogue()
    {
        BuildDialogueTabs();
        ClearRows(_dialogueRows);
        _dialogueRendered = 0;
        // 기록된 차례 그대로 내려 쓴다 — 다시 정렬하지 않는다.
        foreach (var entry in DialogueHistory.Instance?.GetAllEntries() ?? Enumerable.Empty<DialogueHistoryEntry>())
        {
            // 교육일(DAY0)의 대화도 그대로 뜬다 — 튜토리얼에서 이 화면을 쓰는 법을 배운다.
            if (entry.Day != (GameState.Instance?.CurrentDay ?? 1) || !PassesFilter(entry)) continue;
            AppendDialogueRow(entry);
        }
        if (_dialogueRendered == 0)
            AddEmpty(_dialogueRows, _dialogueFilter.Count > 0
                ? "고른 직원과의 대화 기록이 없습니다."
                : "아직 기록된 대화가 없습니다.", InkDim);
        QueueDialogueScroll(true, 0);
    }

    // 고른 직원이 없으면 전부, 있으면 그 직원들과 오간 대화만.
    private bool PassesFilter(DialogueHistoryEntry e)
    {
        if (_dialogueFilter.Count == 0) return true;
        foreach (string id in _dialogueFilter)
            if (DialogueHistory.Involves(e, id)) return true;
        return false;
    }

    // 직원 이름 버튼 줄. 각자의 고유색으로 칠하고, 누르면 켜지고 다시 누르면 꺼진다.
    private void BuildDialogueTabs()
    {
        if (_dialogueTabs == null) return;
        foreach (Node c in _dialogueTabs.GetChildren()) { _dialogueTabs.RemoveChild(c); c.QueueFree(); }

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        foreach (string id in sim.GetEmployeeIds())
        {
            var def = sim.GetEmployeeDef(id);
            if (def == null) continue;
            string captured = id;
            bool on = _dialogueFilter.Contains(id);
            _dialogueTabs.AddChild(SpeakerTab(def.Codename, def.IconColor, on, () =>
            {
                if (!_dialogueFilter.Remove(captured)) _dialogueFilter.Add(captured);
                RebuildDialogue();
            }));
        }
    }

    // 종이 위에 찍힌 이름표처럼 보이게 한다. 켜지면 그 직원 색으로 칠해진다.
    private Button SpeakerTab(string label, Color own, bool on, System.Action onPressed)
    {
        // 밝은 고유색은 종이 위에서 흐려진다 — 잉크 쪽으로 섞어 글자가 읽히게 한다.
        Color ink = own.Lerp(Ink, 0.45f);
        var b = new Button
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 38),
            MouseFilter = Control.MouseFilterEnum.Stop,
            ToggleMode = false,
        };
        b.AddThemeFontOverride("font", _body);
        b.AddThemeFontSizeOverride("font_size", ViewFont.FS(17));
        b.AddThemeColorOverride("font_color", on ? Paper : ink);
        b.AddThemeColorOverride("font_hover_color", on ? Paper : InkRed);
        b.AddThemeColorOverride("font_pressed_color", on ? Paper : InkRed);

        var normal = new StyleBoxFlat
        {
            BgColor = on ? ink : new Color(ink.R, ink.G, ink.B, 0.10f),
            BorderColor = ink with { A = on ? 1f : 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = on ? 3 : 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = on ? ink : new Color(ink.R, ink.G, ink.B, 0.26f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.Pressed += () => onPressed();
        return b;
    }

    // 원본 기록 하나가 화면 로그 0줄이 될 수도, 여러 줄이 될 수도 있다.
    // 요약본을 다시 만들어 늘어난 만큼만 덧붙인다.
    private void OnLogAdded()
    {
        if (_mode != WindowMode.Log) return;
        var rebuilt = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(),
            GameState.Instance?.CurrentDay ?? 1);
        if (rebuilt.Count == _displayLog.Count) { _displayLog = rebuilt; return; }
        if (rebuilt.Count < _logRendered) { _displayLog = rebuilt; RebuildLog(); return; }

        bool stick = IsAtBottom(_logScroll);
        double old = _logScroll.GetVScrollBar().Value;
        if (_logRendered == 0) ClearRows(_logRows);
        for (int i = _logRendered; i < rebuilt.Count; i++) AppendLogRow(rebuilt[i]);
        _displayLog = rebuilt;
        QueueLogScroll(stick, old);
    }

    private void OnDialogueAdded()
    {
        if (_mode != WindowMode.Dialogue) return;
        var entry = DialogueHistory.Instance?.GetAllEntries().LastOrDefault();
        if (entry == null || entry.Day != (GameState.Instance?.CurrentDay ?? 1)) return;
        if (!PassesFilter(entry)) return;
        bool stick = IsAtBottom(_dialogueScroll);
        double old = _dialogueScroll.GetVScrollBar().Value;
        if (_dialogueRendered == 0) ClearRows(_dialogueRows);
        AppendDialogueRow(entry);
        QueueDialogueScroll(stick, old);
    }

    private void OnLogCleared()
    {
        if (_mode == WindowMode.Log) RebuildLog();
    }

    private void OnDialogueCleared()
    {
        if (_mode == WindowMode.Dialogue) RebuildDialogue();
    }

    // 시각은 기본색, 본문은 "직원 고유색" 또는 "중요도 색". 두 색을 한 줄에 쓰기 위해
    // RichTextLabel 을 사용한다.
    private void AppendLogRow(DisplayLogEntry row)
    {
        var line = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 29),
        };
        line.AddThemeFontOverride("normal_font", _body);
        line.AddThemeFontSizeOverride("normal_font_size", ViewFont.FS(18));
        line.Text = $"[color=#{LogTime.ToHtml(false)}]{ShiftClock(row.Timestamp)}[/color]  " +
                    $"[color=#{BodyColor(row).ToHtml(false)}]{Marker(row.Severity)} {Escape(row.Text)}[/color]";
        _logRows.AddChild(line);
        _logRendered++;
    }

    // 직원 개인의 행동이면 그 직원의 고유색(IconColor), 시설 사건이면 중요도 색.
    private static Color BodyColor(DisplayLogEntry row)
    {
        // 중요한 사건은 직원 고유색에 묻히면 안 된다 — 중요도 색이 항상 이긴다.
        switch (row.Severity)
        {
            case DisplayLogSeverity.Warning: return LogWarning;
            case DisplayLogSeverity.Critical: return LogCritical;
            case DisplayLogSeverity.Sabotage: return LogSabotage;
            case DisplayLogSeverity.Recovery: return LogRecovery;
        }
        // 배치·이동은 누구의 줄인지가 먼저 읽혀야 하므로 그 직원의 고유색으로 쓴다.
        if (!string.IsNullOrEmpty(row.RelatedEmployeeId))
        {
            var def = FacilitySimulation.Instance?.GetEmployeeDef(row.RelatedEmployeeId);
            if (def != null) return Readable(def.IconColor);
        }
        return row.Severity == DisplayLogSeverity.Move ? LogMove : LogNormal;
    }

    // 늑대처럼 어두운 고유색은 검은 배경에서 안 읽힌다. 색상(hue)은 그대로 두고
    // 최소 밝기까지만 끌어올린다. 로그는 글자가 작고 줄이 빽빽해 통화창(0.55)보다
    // 더 밝게 잡는다 — 이 값은 시설 로그에서만 쓴다.
    private static Color Readable(Color c)
    {
        float lum = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        const float min = 0.74f;
        return lum >= min ? c : c.Lerp(Colors.White, (min - lum) / Mathf.Max(0.001f, 1f - lum));
    }

    private static string Marker(DisplayLogSeverity severity) => severity switch
    {
        DisplayLogSeverity.Move => "→",
        DisplayLogSeverity.Warning => "⚠",
        DisplayLogSeverity.Critical => "⚠",
        DisplayLogSeverity.Sabotage => "■",
        DisplayLogSeverity.Recovery => "✓",
        _ => "·",
    };

    // 작업실/업무 이름에 대괄호가 들어가도 BBCode 태그로 해석되지 않게 한다.
    private static string Escape(string text) => (text ?? "").Replace("[", "[lb]");

    private void AppendDialogueRow(DialogueHistoryEntry entry)
    {
        var block = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        block.AddThemeConstantOverride("separation", 3);
        _dialogueRows.AddChild(block);

        string when = entry.ConversationType == DialogueConversationType.Interview
            ? "휴게시간"
            : ShiftClock(entry.Timestamp);
        Color speakerColor = SpeakerInk(entry);

        Label header = LabelFor($"[{when} / {entry.SpeakerDisplayName}]", 15, InkDim, _body);
        block.AddChild(header);
        Label text = LabelFor($"{entry.SpeakerDisplayName}:\n\"{entry.Text}\"", 19, speakerColor, _body);
        text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        block.AddChild(text);

        var separator = new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore };
        separator.AddThemeColorOverride("separator", new Color(0.36f, 0.28f, 0.16f, 0.35f));
        block.AddChild(separator);
        _dialogueRendered++;
    }

    private Color SpeakerInk(DialogueHistoryEntry entry)
    {
        if (entry.SpeakerId == "manager") return InkRed;
        Color own = FacilitySimulation.Instance?.GetEmployeeDef(entry.SpeakerId)?.IconColor ?? Ink;
        // 밝은 고유색은 종이 위에서 흐려지므로 잉크 쪽으로 섞되 색 구분은 유지한다.
        return own.Lerp(Ink, 0.58f);
    }

    private static void ClearRows(Node parent)
    {
        foreach (Node child in parent.GetChildren()) child.QueueFree();
    }

    private void AddEmpty(VBoxContainer parent, string text, Color color)
    {
        Label empty = LabelFor(text, 18, color, _body);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.CustomMinimumSize = new Vector2(0, 80);
        parent.AddChild(empty);
    }

    private static bool IsAtBottom(ScrollContainer scroll)
    {
        VScrollBar bar = scroll.GetVScrollBar();
        return bar.MaxValue <= bar.Page + 8.0 || bar.Value >= bar.MaxValue - bar.Page - 8.0;
    }

    private void QueueLogScroll(bool stick, double old)
    {
        _logStick = stick;
        _logOldScroll = old;
        CallDeferred(nameof(ApplyLogScroll));
    }

    private void QueueDialogueScroll(bool stick, double old)
    {
        _dialogueStick = stick;
        _dialogueOldScroll = old;
        CallDeferred(nameof(ApplyDialogueScroll));
    }

    private void ApplyLogScroll()
    {
        VScrollBar bar = _logScroll.GetVScrollBar();
        bar.Value = _logStick ? bar.MaxValue : Mathf.Min(_logOldScroll, bar.MaxValue);
    }

    private void ApplyDialogueScroll()
    {
        VScrollBar bar = _dialogueScroll.GetVScrollBar();
        bar.Value = _dialogueStick ? bar.MaxValue : Mathf.Min(_dialogueOldScroll, bar.MaxValue);
    }

    private static Label LabelFor(string text, int size, Color color, Font font)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    // 플레이어에게 보이는 시각 — 한글 시간대 표기(밤/새벽). 환산 · 표기는 DialogueClock 한 곳에서만.
    private static string ShiftClock(float elapsedSeconds) => NSP.Dialogue.DialogueClock.Text(elapsedSeconds);
}
