using Godot;
using NSP.Core;

namespace NSP.View;

// ESC / 톱니바퀴로 여는 일시정지 메뉴. 환경 설정 · 시설 로그 창과 같은 홀로그램 창이고,
// 카드 다섯 장(설정 / 저장하기 / 불러오기 / 시작화면으로 / 나가기)이 세로로 놓인다.
// 저장·불러오기는 아직 미구현이라 비활성 카드로만 자리를 잡아둔다.
// 시작화면 복귀와 종료는 "예 / 아니오" 확인을 한 번 거친다.
public partial class PauseMenu : CanvasLayer
{
    public static PauseMenu Instance { get; private set; }

    // 환경 설정 창(SettingsPanel) · 기록 창(Day1HistoryOverlay)과 같은 색.
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Ink = new(0.84f, 0.92f, 0.92f);
    private static readonly Color InkDim = new(0.50f, 0.66f, 0.68f);
    private static readonly Color WindowBg = new(0.03f, 0.09f, 0.11f, 0.94f);

    // 시작 화면으로 돌아갈 때 쓰는 씬. 메인 씬 자체를 다시 로드해 처음 상태로 되돌린다.
    [Export] public string TitleScenePath = "res://scenes/main/MainScene3D_Test.tscn";

    private Control _root;
    private Control _confirm;
    private Font _serif, _body;
    private SettingsPanel _settings;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Instance = this;
        Layer = 120;                       // 통화 HUD(90) 위. 설정 창(130)은 이 위에 뜬다.
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;   // 일시정지 중에도 입력을 받는다
        _body = ViewFont.Default;
        _serif = _body;   // 홀로그램 창은 기록 창처럼 본문 글꼴 하나로 통일한다
        BuildUI();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Open()
    {
        if (Visible) return;
        HideConfirm();
        Visible = true;
        GetTree().Paused = true;
    }

    public void Close()
    {
        if (!Visible) return;
        HideConfirm();
        Visible = false;
        GetTree().Paused = false;
    }

    public void Toggle()
    {
        if (Visible) Close();
        else Open();
    }

    // ESC 는 어느 화면에서나 먹어야 하므로 _UnhandledKeyInput 이 아니라 _Input 에서 잡는다.
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;

        // 설정 창이 열려 있으면 그 창이 ESC 를 먼저 처리한다.
        if (_settings != null && IsInstanceValid(_settings) && _settings.Visible) return;

        // 시작 화면(중앙제어실 전체가 타이틀)에서는 ESC 가 그쪽 단말기의 '뒤로'다.
        if (TitleRoomDirector.Instance?.IsRunning == true) return;
        // 엔딩 연출 · 5일간의 근무 기록 동안에는 멈추지 않는다.
        if (EndingDirector.IsPlaying) return;
        // 최종 격리 보고서는 제출 전까지 빠져나갈 수 없다.
        if (FinalReportView.IsOpen) return;

        if (_confirm is { Visible: true }) HideConfirm();
        else if (Visible) Close();
        // 메뉴가 닫혀 있고 기기를 확대해 보는 중이면, ESC 는 먼저 확대만 푼다.
        else if (ControlRoom3DController.Instance?.UnzoomIfFocused() != true) Open();
        GetViewport().SetInputAsHandled();
    }

    // --- UI ---------------------------------------------------------------

    private void BuildUI()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.62f) };
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(scrim);

        var sheet = MakeSheet(-330f, 330f, -282f, 282f);
        _root.AddChild(sheet);

        // 오른쪽 위 닫기(X).
        var close = new Button
        {
            Text = "✕",
            AnchorLeft = 1f, AnchorRight = 1f,
            OffsetLeft = -58f, OffsetRight = -14f, OffsetTop = 14f, OffsetBottom = 58f,
        };
        close.AddThemeFontOverride("font", _body);
        close.AddThemeFontSizeOverride("font_size", ViewFont.FS(24));
        close.AddThemeColorOverride("font_color", Cyan);
        close.AddThemeColorOverride("font_hover_color", Colors.White);
        close.AddThemeColorOverride("font_pressed_color", Colors.White);
        var xNormal = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.12f),
            BorderColor = Cyan with { A = 0.5f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var xHover = (StyleBoxFlat)xNormal.Duplicate();
        xHover.BgColor = Cyan with { A = 0.25f };
        xHover.BorderColor = Cyan;
        close.AddThemeStyleboxOverride("normal", xNormal);
        close.AddThemeStyleboxOverride("hover", xHover);
        close.AddThemeStyleboxOverride("pressed", xHover);
        close.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        close.Pressed += Close;
        sheet.AddChild(close);

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // 오른쪽은 닫기(✕) 자리를 비워 둔다 — 제목이 그 위로 올라타면 둘 다 안 읽힌다.
        vb.OffsetLeft = 36; vb.OffsetRight = -76;
        vb.OffsetTop = 30; vb.OffsetBottom = -30;
        vb.AddThemeConstantOverride("separation", 14);
        sheet.AddChild(vb);

        vb.AddChild(Lbl("SYSTEM PAUSE  /  일시 정지", 23, Cyan, _body));
        vb.AddChild(Rule());
        vb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        vb.AddChild(Card("설  정", true, OpenSettings));
        vb.AddChild(Card("저장하기", false, null));
        vb.AddChild(Card("불러오기", false, null));
        vb.AddChild(Card("시작화면으로", true,
            () => ShowConfirm("시작화면으로 돌아가시겠습니까?", GoToTitle)));
        vb.AddChild(Card("나가기", true,
            () => ShowConfirm("정말로 게임을 종료하시겠습니까?", QuitGame)));

        BuildConfirm();
    }

    private Panel MakeSheet(float l, float r, float t, float b)
    {
        var sheet = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = l, OffsetRight = r, OffsetTop = t, OffsetBottom = b,
        };
        sheet.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = WindowBg,
            BorderColor = Cyan with { A = 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        var frame = new HologramFrame { Accent = Cyan, MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        sheet.AddChild(frame);
        return sheet;
    }

    // 카드 한 장 = 제목만 든 줄.
    private Control Card(string title, bool enabled, System.Action onPressed)
    {
        var b = new Button { Disabled = !enabled, CustomMinimumSize = new Vector2(0, 62) };
        var normal = new StyleBoxFlat
        {
            BgColor = enabled ? new Color(0.06f, 0.14f, 0.17f, 0.9f) : new Color(0.05f, 0.08f, 0.09f, 0.7f),
            BorderColor = Cyan with { A = enabled ? 0.45f : 0.18f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = Cyan with { A = 0.22f };
        hover.BorderColor = Cyan;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (onPressed != null) b.Pressed += () => onPressed();

        // 제목은 버튼 위에 직접 얹는다(Button 은 컨테이너가 아니라 자식 배치를 안 해준다).
        var col = enabled ? Ink : InkDim with { A = 0.55f };
        var t = Lbl(title, 25, col, _serif);
        t.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        t.OffsetLeft = 24;
        t.VerticalAlignment = VerticalAlignment.Center;
        b.AddChild(t);

        // 자식 Label 이라 버튼의 font_hover_color 가 안 먹는다 — 직접 바꿔준다.
        if (enabled)
        {
            b.MouseEntered += () => t.AddThemeColorOverride("font_color", Colors.White);
            b.MouseExited += () => t.AddThemeColorOverride("font_color", col);
        }
        return b;
    }

    // --- 예 / 아니오 확인 ---------------------------------------------------

    private Label _confirmText;
    private System.Action _confirmAction;

    private void BuildConfirm()
    {
        _confirm = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
        _confirm.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_confirm);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.5f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _confirm.AddChild(dim);

        var sheet = MakeSheet(-290f, 290f, -110f, 110f);
        _confirm.AddChild(sheet);

        _confirmText = Lbl("", 22, Ink, _body);
        _confirmText.HorizontalAlignment = HorizontalAlignment.Center;
        _confirmText.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _confirmText.OffsetLeft = 24; _confirmText.OffsetRight = -24; _confirmText.OffsetTop = 32;
        sheet.AddChild(_confirmText);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        row.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        row.OffsetLeft = 24; row.OffsetRight = -24; row.OffsetTop = -66; row.OffsetBottom = -22;
        sheet.AddChild(row);

        var yes = DocButton("예", 150f);
        yes.Pressed += () =>
        {
            var act = _confirmAction;
            HideConfirm();
            act?.Invoke();
        };
        row.AddChild(yes);

        var no = DocButton("아니오", 150f);
        no.Pressed += HideConfirm;
        row.AddChild(no);
    }

    private void ShowConfirm(string text, System.Action onYes)
    {
        _confirmText.Text = text;
        _confirmAction = onYes;
        _confirm.Visible = true;
    }

    private void HideConfirm()
    {
        if (_confirm != null) _confirm.Visible = false;
        _confirmAction = null;
    }

    // --- 동작 --------------------------------------------------------------

    private void OpenSettings()
    {
        if (_settings == null || !IsInstanceValid(_settings))
        {
            _settings = new SettingsPanel();
            _settings.ProcessMode = ProcessModeEnum.Always;
            AddChild(_settings);
        }
        _settings.Open();
    }

    // 처음부터 다시 시작. autoload(GameState / FacilitySimulation / EventLog / 금기)는
    // 씬을 다시 로드해도 살아남으므로 여기서 명시적으로 초기화해야 DAY 1 로 돌아간다.
    private void GoToTitle()
    {
        GetTree().Paused = false;
        Visible = false;
        GameSettings.Save();

        GameState.Instance?.ResetRun();
        NSP.Facility.FacilitySimulation.Instance?.ResetRun();
        EventLog.Instance?.ClearAll();
        // 대화 기록은 한 판 동안 쌓인다 — 타이틀로 나가면 여기서 비운다.
        DialogueHistory.Instance?.ClearAll();
        NSP.Taboo.TabooRuleSystem.Instance?.ActivateDailyTaboos(System.Array.Empty<string>());

        GetTree().ChangeSceneToFile(TitleScenePath);
    }

    private void QuitGame()
    {
        GetTree().Paused = false;
        GameSettings.Save();
        GetTree().Quit();
    }

    // --- 공용 위젯 ----------------------------------------------------------

    private Label Lbl(string text, int size, Color col, Font font)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private Control Rule() =>
        new ColorRect { Color = Cyan with { A = 0.28f }, CustomMinimumSize = new Vector2(0, 1.5f) };

    private Button DocButton(string text, float minWidth)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 42) };
        b.AddThemeFontOverride("font", _body);
        b.AddThemeFontSizeOverride("font_size", ViewFont.FS(20));
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.14f, 0.17f, 0.9f),
            BorderColor = Cyan with { A = 0.5f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = Cyan with { A = 0.25f };
        hover.BorderColor = Cyan;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

}
