using Godot;
using NSP.Core;

namespace NSP.View;

// ESC / 톱니바퀴로 여는 일시정지 메뉴. 설정 창과 같은 낡은 서류 톤이고,
// 카드 다섯 장(설정 / 저장하기 / 불러오기 / 시작화면으로 / 나가기)이 세로로 놓인다.
// 저장·불러오기는 아직 미구현이라 비활성 카드로만 자리를 잡아둔다.
// 시작화면 복귀와 종료는 "예 / 아니오" 확인을 한 번 거친다.
public partial class PauseMenu : CanvasLayer
{
    public static PauseMenu Instance { get; private set; }

    private static readonly Color Ink = new(0.18f, 0.14f, 0.09f);
    private static readonly Color InkDim = new(0.42f, 0.35f, 0.24f);
    private static readonly Color InkRed = new(0.55f, 0.14f, 0.10f);
    private static readonly Color Paper = new(0.855f, 0.80f, 0.645f);

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
        _serif = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf") ?? ViewFont.Default;
        _body = ViewFont.Default;
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

        var sheet = MakeSheet(-300f, 300f, -282f, 282f);
        _root.AddChild(sheet);

        // 오른쪽 위 닫기(X).
        var close = new Button
        {
            Text = "✕",
            AnchorLeft = 1f, AnchorRight = 1f,
            OffsetLeft = -58f, OffsetRight = -14f, OffsetTop = 14f, OffsetBottom = 58f,
        };
        close.AddThemeFontOverride("font", _body);
        close.AddThemeFontSizeOverride("font_size", 24);
        close.AddThemeColorOverride("font_color", InkDim);
        close.AddThemeColorOverride("font_hover_color", new Color(0.98f, 0.93f, 0.82f));
        close.AddThemeColorOverride("font_pressed_color", new Color(0.98f, 0.93f, 0.82f));
        var xNormal = new StyleBoxFlat
        {
            BgColor = new Color(0.88f, 0.84f, 0.71f, 0f),
            BorderColor = new Color(0.4f, 0.32f, 0.2f, 0.45f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var xHover = (StyleBoxFlat)xNormal.Duplicate();
        xHover.BgColor = InkRed with { A = 0.85f };
        xHover.BorderColor = InkRed;
        close.AddThemeStyleboxOverride("normal", xNormal);
        close.AddThemeStyleboxOverride("hover", xHover);
        close.AddThemeStyleboxOverride("pressed", xHover);
        close.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        close.Pressed += Close;
        sheet.AddChild(close);

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.OffsetLeft = 40; vb.OffsetRight = -40;
        vb.OffsetTop = 30; vb.OffsetBottom = -30;
        vb.AddThemeConstantOverride("separation", 14);
        sheet.AddChild(vb);

        vb.AddChild(Lbl("DOC NO. NSP-00   FACILITY CONTROL DEPT.", 13, InkDim, _body));
        vb.AddChild(Lbl("일시 정지", 34, Ink, _serif));
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
            BgColor = Paper,
            BorderColor = new Color(0.32f, 0.24f, 0.13f),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
        });
        sheet.AddChild(new PaperGrain { MouseFilter = Control.MouseFilterEnum.Ignore });
        return sheet;
    }

    // 카드 한 장 = 제목만 든 서류 블록.
    private Control Card(string title, bool enabled, System.Action onPressed)
    {
        var b = new Button { Disabled = !enabled, CustomMinimumSize = new Vector2(0, 62) };
        var normal = new StyleBoxFlat
        {
            BgColor = enabled ? new Color(0.90f, 0.86f, 0.73f) : new Color(0.80f, 0.77f, 0.68f, 0.6f),
            BorderColor = new Color(0.4f, 0.32f, 0.2f, enabled ? 0.75f : 0.35f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.72f, 0.60f, 0.30f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        if (onPressed != null) b.Pressed += () => onPressed();

        // 제목은 버튼 위에 직접 얹는다(Button 은 컨테이너가 아니라 자식 배치를 안 해준다).
        var col = enabled ? Ink : new Color(0.5f, 0.46f, 0.38f);
        var t = Lbl(title, 25, col, _serif);
        t.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        t.OffsetLeft = 24;
        t.VerticalAlignment = VerticalAlignment.Center;
        b.AddChild(t);

        // 자식 Label 이라 버튼의 font_hover_color 가 안 먹는다 — 직접 바꿔준다.
        if (enabled)
        {
            var hot = new Color(0.99f, 0.96f, 0.88f);
            b.MouseEntered += () => t.AddThemeColorOverride("font_color", hot);
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
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private Control Rule() =>
        new ColorRect { Color = new Color(0.3f, 0.24f, 0.14f, 0.45f), CustomMinimumSize = new Vector2(0, 1.5f) };

    private Button DocButton(string text, float minWidth)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 42) };
        b.AddThemeFontOverride("font", _body);
        b.AddThemeFontSizeOverride("font_size", 20);
        b.AddThemeColorOverride("font_color", Ink);
        b.AddThemeColorOverride("font_hover_color", new Color(0.99f, 0.96f, 0.88f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.99f, 0.96f, 0.88f));
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.90f, 0.86f, 0.73f),
            BorderColor = new Color(0.4f, 0.32f, 0.2f, 0.7f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.72f, 0.60f, 0.30f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

    // 설정 창과 같은 종이 결.
    private partial class PaperGrain : Control
    {
        public override void _Ready() => SetAnchorsPreset(LayoutPreset.FullRect);

        public override void _Draw()
        {
            var rng = new RandomNumberGenerator { Seed = 445512 };
            for (int i = 0; i < 18; i++)
            {
                var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
                DrawCircle(p, rng.RandfRange(20f, 72f), new Color(0.42f, 0.33f, 0.18f, rng.RandfRange(0.03f, 0.07f)));
            }
            for (int i = 0; i < 300; i++)
            {
                var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
                bool light = rng.Randf() > 0.5f;
                DrawRect(new Rect2(p, new Vector2(1.6f, 1.6f)),
                    light ? new Color(0.96f, 0.92f, 0.79f, 0.07f) : new Color(0.32f, 0.25f, 0.14f, 0.07f));
            }
            var edge = new Color(0.28f, 0.21f, 0.11f, 0.2f);
            const float b = 16f;
            DrawRect(new Rect2(0, 0, Size.X, b), edge);
            DrawRect(new Rect2(0, Size.Y - b, Size.X, b), edge);
            DrawRect(new Rect2(0, 0, b, Size.Y), edge);
            DrawRect(new Rect2(Size.X - b, 0, b, Size.Y), edge);
        }
    }
}
