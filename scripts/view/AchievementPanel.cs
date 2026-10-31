using Godot;
using NSP.Core;

namespace NSP.View;

// 일시정지(ESC) 창의 「도전과제」 — 환경 설정 창(SettingsPanel)과 같은 홀로그램 창.
//
// 예전에는 타이틀의 기록실 화면(800×600 CRT 캔버스, AchievementArchiveView)을 그대로 창 안에 끼워 넣었다.
// 그러면 모니터 화면을 오려 붙인 것처럼 보였다(2026-10-31 사용자 제보 "창에 시작화면 기록열람 창을 옮겨 놓은 듯").
// 지금은 창 안에서 직접 그린다 — 머리줄 · 달성 수 · 진행 막대 · 굴러가는 목록 · 오른쪽 위 ✕.
// 기록은 그대로다: 해금 상태는 AchievementManager 가 들고 있고, 여기서는 읽기만 한다.
//
// 환경 설정 창과 같은 규약: CanvasLayer 130, 일시정지 중에도 입력을 받고, ESC 로 닫힌다.
public partial class AchievementPanel : CanvasLayer
{
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Ink = new(0.84f, 0.92f, 0.92f);
    private static readonly Color InkDim = new(0.50f, 0.66f, 0.68f);
    private static readonly Color Locked = new(0.30f, 0.38f, 0.40f);
    private static readonly Color WindowBg = new(0.03f, 0.09f, 0.11f, 0.94f);

    private Control _root;
    private Font _body;
    private VBoxContainer _list;
    private ScrollContainer _scroll;
    private Label _count;
    private ColorRect _barFill;
    private const float BarWidth = 768f;

    public override void _Ready()
    {
        // 일시정지 창(120)보다 위 — 환경 설정 창과 같은 층.
        Layer = 130;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;
        _body = ViewFont.Default;
        BuildUi();
    }

    public void Open()
    {
        Refresh();
        Visible = true;
        _root.Visible = true;
        _scroll.ScrollVertical = 0;
    }

    public void Close() => Visible = false;

    public bool IsOpen => Visible;

    // 창이 떠 있는 동안 ESC 를 여기서 먹는다 — 일시정지 창까지 통째로 닫히지 않게.
    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape or Key.Backspace })
        {
            Sfx.Instance?.Play("relay_click", -8f);
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    // ── 화면 ────────────────────────────────────────────────────────

    private void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(scrim);

        var sheet = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -430f, OffsetRight = 430f, OffsetTop = -440f, OffsetBottom = 440f,
        };
        sheet.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = WindowBg,
            BorderColor = Cyan with { A = 0.55f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        _root.AddChild(sheet);
        var frame = new HologramFrame { Accent = Cyan, MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        sheet.AddChild(frame);
        sheet.AddChild(CloseButton());

        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.OffsetLeft = 46; vb.OffsetRight = -46; vb.OffsetTop = 40; vb.OffsetBottom = -36;
        vb.AddThemeConstantOverride("separation", 10);
        sheet.AddChild(vb);

        vb.AddChild(Lbl("ACHIEVEMENTS  /  도전과제", 26, Cyan));
        var rule = new ColorRect { Color = Cyan with { A = 0.22f }, CustomMinimumSize = new Vector2(0, 1f) };
        vb.AddChild(rule);

        // 달성 수 + 진행 막대.
        _count = Lbl("", 18, Ink);
        vb.AddChild(_count);
        var bar = new Control { CustomMinimumSize = new Vector2(BarWidth, 8f) };
        var barBg = new ColorRect { Color = Cyan with { A = 0.12f }, Size = new Vector2(BarWidth, 8f) };
        _barFill = new ColorRect { Color = Cyan with { A = 0.85f }, Size = new Vector2(0f, 8f) };
        bar.AddChild(barBg);
        bar.AddChild(_barFill);
        vb.AddChild(bar);
        vb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4f) });

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        vb.AddChild(_scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 6);
        _scroll.AddChild(_list);

        vb.AddChild(Lbl("휠로 굴려 보기   /   ESC 닫기", 13, InkDim));
    }

    // 열 때마다 다시 그린다 — 근무 중에 새로 딴 과제가 바로 보인다.
    private void Refresh()
    {
        foreach (var c in _list.GetChildren()) c.QueueFree();
        var m = AchievementManager.Instance;
        int got = 0, total = Achievements.All.Count;
        foreach (var def in Achievements.All)
        {
            bool on = m?.IsUnlocked(def.Id) == true;
            if (on) got++;
            _list.AddChild(Row(def, on));
        }
        _count.Text = $"달성 {got} / {total}";
        _barFill.Size = new Vector2(total > 0 ? BarWidth * got / total : 0f, 8f);
    }

    // 한 줄 — 아이콘 칸 · 번호와 이름 · 조건 · 오른쪽에 달성 여부. 숨김 과제는 달성 전까지 이름도 조건도 감춘다.
    private Control Row(AchievementDefinition def, bool unlocked)
    {
        bool masked = def.Hidden && !unlocked;
        var box = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = unlocked ? new Color(0.06f, 0.16f, 0.17f, 0.75f) : new Color(0.02f, 0.05f, 0.06f, 0.6f),
            BorderColor = (unlocked ? Cyan : Locked) with { A = unlocked ? 0.45f : 0.25f },
            BorderWidthLeft = unlocked ? 3 : 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 12, ContentMarginRight = 14, ContentMarginTop = 8, ContentMarginBottom = 8,
        });

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        box.AddChild(row);

        row.AddChild(new IconBox { Def = def, Unlocked = unlocked, Font = _body, CustomMinimumSize = new Vector2(52, 52) });

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 2);
        text.AddChild(Lbl($"{def.No:00}.  {(masked ? "???" : def.Name)}", 19, unlocked ? Ink : InkDim));
        var cond = Lbl(masked ? "아직 밝혀지지 않은 기록입니다." : def.Condition, 14, unlocked ? InkDim : Locked);
        cond.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cond.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        text.AddChild(cond);
        row.AddChild(text);

        var state = Lbl(unlocked ? "✓ 달성" : "미달성", 16, unlocked ? Cyan : Locked);
        state.CustomMinimumSize = new Vector2(90, 0);
        state.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(state);
        return box;
    }

    // 도전과제 그림(AchievementIcons) — 미달성은 자물쇠, 숨김은 물음표.
    private sealed partial class IconBox : Control
    {
        public AchievementDefinition Def;
        public bool Unlocked;
        public Font Font;

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            DrawRect(r, Unlocked ? new Color(0.07f, 0.12f, 0.12f) : new Color(0.035f, 0.045f, 0.048f));
            DrawRect(r, (Unlocked ? Cyan : Locked) with { A = Unlocked ? 0.55f : 0.35f }, false, 1.2f);
            NSP.Ui.AchievementIcons.Draw(this, Def,
                new Rect2(Size * 0.16f, Size * 0.68f), Unlocked ? Cyan : Locked, Unlocked, Font);
        }
    }

    private Label Lbl(string text, int size, Color col)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontOverride("font", _body);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private Button CloseButton()
    {
        var close = new Button
        {
            Text = "✕",
            AnchorLeft = 1f, AnchorRight = 1f,
            OffsetLeft = -62f, OffsetRight = -18f, OffsetTop = 18f, OffsetBottom = 62f,
            TooltipText = "닫기",
        };
        close.AddThemeFontOverride("font", _body);
        close.AddThemeFontSizeOverride("font_size", ViewFont.FS(22));
        close.AddThemeColorOverride("font_color", Cyan);
        close.AddThemeColorOverride("font_hover_color", Colors.White);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.12f),
            BorderColor = Cyan with { A = 0.5f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = Cyan with { A = 0.25f };
        close.AddThemeStyleboxOverride("normal", normal);
        close.AddThemeStyleboxOverride("hover", hover);
        close.AddThemeStyleboxOverride("pressed", hover);
        close.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        close.Pressed += () => { Sfx.Instance?.Play("relay_click", -8f); Close(); };
        MonitorUi.AddFeedback(close);
        return close;
    }
}
