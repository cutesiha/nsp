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
    private static readonly Color LogWarning = new(0.95f, 0.80f, 0.25f);
    private static readonly Color LogCritical = new(1f, 0.40f, 0.20f);
    private static readonly Color LogRecovery = new(0.40f, 0.95f, 0.50f);
    private static readonly Color LogTime = new(0.45f, 0.66f, 0.72f);

    private enum WindowMode { None, Log, Dialogue }

    public static Day1HistoryOverlay Instance { get; private set; }
    public bool IsWindowOpen => _mode != WindowMode.None;

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
            OffsetLeft = -260f, OffsetRight = -20f, OffsetTop = -76f, OffsetBottom = -20f,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _icons.AddThemeConstantOverride("separation", 10);
        _icons.Visible = false;
        _root.AddChild(_icons);

        Button logIcon = MonitorUi.Button("L  로그", Cyan, _body, ToggleLog, 17);
        logIcon.Name = "LogHistoryButton";
        logIcon.TooltipText = "DAY1 시설 로그";
        logIcon.CustomMinimumSize = new Vector2(100, 52);
        logIcon.MouseFilter = Control.MouseFilterEnum.Stop;
        _icons.AddChild(logIcon);

        Button dialogueIcon = MonitorUi.Button("D  대화 기록", new Color(0.88f, 0.76f, 0.48f), _body, ToggleDialogue, 17);
        dialogueIcon.Name = "DialogueHistoryButton";
        dialogueIcon.TooltipText = "DAY1 대화 기록";
        dialogueIcon.CustomMinimumSize = new Vector2(110, 52);
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

        var rule = new HSeparator
        {
            AnchorRight = 1f,
            OffsetLeft = 42f, OffsetRight = -42f, OffsetTop = 105f, OffsetBottom = 107f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rule.AddThemeColorOverride("separator", new Color(0.38f, 0.29f, 0.16f, 0.75f));
        _dialoguePanel.AddChild(rule);

        _dialogueScroll = new ScrollContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 42f, OffsetRight = -42f, OffsetTop = 122f, OffsetBottom = -34f,
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
        close.AddThemeFontSizeOverride("font_size", 22);
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
        _displayLog = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(), 1);
        foreach (var row in _displayLog) AppendLogRow(row);
        if (_logRendered == 0) AddEmpty(_logRows, "아직 기록된 시설 로그가 없습니다.", Cyan with { A = 0.65f });
        QueueLogScroll(true, 0);
    }

    private void RebuildDialogue()
    {
        ClearRows(_dialogueRows);
        _dialogueRendered = 0;
        foreach (var entry in DialogueHistory.Instance?.GetAllEntries().Where(e => e.Day == 1)
                     ?? Enumerable.Empty<DialogueHistoryEntry>())
            AppendDialogueRow(entry);
        if (_dialogueRendered == 0) AddEmpty(_dialogueRows, "아직 기록된 대화가 없습니다.", InkDim);
        QueueDialogueScroll(true, 0);
    }

    // 원본 기록 하나가 화면 로그 0줄이 될 수도, 여러 줄이 될 수도 있다.
    // 요약본을 다시 만들어 늘어난 만큼만 덧붙인다.
    private void OnLogAdded()
    {
        if (_mode != WindowMode.Log) return;
        var rebuilt = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(), 1);
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
        if (entry == null || entry.Day != 1) return;
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
        if (!string.IsNullOrEmpty(row.RelatedEmployeeId))
        {
            var def = FacilitySimulation.Instance?.GetEmployeeDef(row.RelatedEmployeeId);
            if (def != null) return Readable(def.IconColor);
        }
        return row.Severity switch
        {
            DisplayLogSeverity.Warning => LogWarning,
            DisplayLogSeverity.Critical => LogCritical,
            DisplayLogSeverity.Recovery => LogRecovery,
            _ => LogNormal,
        };
    }

    // 까마귀처럼 어두운 고유색은 검은 배경에서 안 읽힌다. 색상(hue)은 그대로 두고
    // 최소 밝기까지만 끌어올린다 — PhoneCallHud 와 같은 방식.
    private static Color Readable(Color c)
    {
        float lum = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        const float min = 0.55f;
        return lum >= min ? c : c.Lerp(Colors.White, (min - lum) / Mathf.Max(0.001f, 1f - lum));
    }

    private static string Marker(DisplayLogSeverity severity) => severity switch
    {
        DisplayLogSeverity.Warning => "⚠",
        DisplayLogSeverity.Critical => "■",
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

    private static string ShiftClock(float elapsedSeconds)
    {
        float shiftLength = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        int totalMinutes = 22 * 60 + Mathf.FloorToInt(Mathf.Max(0f, elapsedSeconds) * (360f / Mathf.Max(1f, shiftLength)));
        return $"{(totalMinutes / 60) % 24:00}:{totalMinutes % 60:00}";
    }
}
