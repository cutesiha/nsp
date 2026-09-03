using Godot;
using NSP.Core;

namespace NSP.View;

// 시작 화면의 설정 창. 근무 배치표와 같은 낡은 서류 톤(누런 아이보리 종이 + 붉은 도장 잉크).
// 값은 전부 GameSettings 가 들고 있고, 여기서는 읽고 쓰기만 한다.
//   · 음량 : MASTER(전체) / BGM(배경음악) / SFX(효과음)
//   · 화면 : 전체화면 켜기·끄기
//   · 조작 : 모니터1 / 모니터2 / 센서 기기 / 전력 기기 확대 숫자키
public partial class SettingsPanel : CanvasLayer
{
    private static readonly Color Ink = new(0.18f, 0.14f, 0.09f);
    private static readonly Color InkDim = new(0.42f, 0.35f, 0.24f);
    private static readonly Color InkRed = new(0.55f, 0.14f, 0.10f);
    private static readonly Color Paper = new(0.855f, 0.80f, 0.645f);

    private Control _root;
    private Font _serif, _body;
    // 키 재지정 대기 중인 항목(다음 키 입력을 이 기능에 배정한다).
    private GameSettings.ZoomTarget? _awaitingKey;
    private readonly System.Collections.Generic.Dictionary<GameSettings.ZoomTarget, Button> _keyButtons = new();

    public override void _Ready()
    {
        // 일시정지 창(120)보다 위 — 거기서 열었을 때 가려지면 클릭조차 안 된다.
        Layer = 130;
        Visible = false;
        _serif = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf") ?? ViewFont.Default;
        _body = ViewFont.Default;
        BuildUI();
    }

    public void Open()
    {
        GameSettings.Load();
        RefreshKeyButtons();
        Visible = true;
        _root.Visible = true;
    }

    public void Close()
    {
        _awaitingKey = null;
        GameSettings.Save();
        Visible = false;
    }

    // 키 재지정 대기 중에는 다음 키 입력을 가로챈다.
    public override void _Input(InputEvent e)
    {
        if (!Visible) return;

        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (_awaitingKey.HasValue)
            {
                if (k.Keycode != Key.Escape)
                    GameSettings.SetKey(_awaitingKey.Value, k.Keycode);
                _awaitingKey = null;
                RefreshKeyButtons();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (k.Keycode == Key.Escape)
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // --- UI ---------------------------------------------------------------

    private void BuildUI()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(scrim);

        // 종이 — 배치표와 같은 누런 서류 질감.
        var sheet = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -430f, OffsetRight = 430f, OffsetTop = -404f, OffsetBottom = 404f,
        };
        sheet.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Paper,
            BorderColor = new Color(0.32f, 0.24f, 0.13f),
            BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
        });
        _root.AddChild(sheet);
        sheet.AddChild(new PaperGrain { MouseFilter = Control.MouseFilterEnum.Ignore });

        // Panel 은 컨테이너가 아니라 스타일박스의 ContentMargin 을 자식에 적용하지 않는다.
        // 여백은 여기서 앵커 오프셋으로 직접 준다.
        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.OffsetLeft = 46; vb.OffsetRight = -46;
        vb.OffsetTop = 34; vb.OffsetBottom = -30;
        vb.AddThemeConstantOverride("separation", 10);
        sheet.AddChild(vb);

        vb.AddChild(Lbl("DOC NO. NSP-00   FACILITY CONTROL DEPT.", 13, InkDim, _body));
        vb.AddChild(Lbl("설정", 40, Ink, _serif));
        vb.AddChild(Rule());

        // ── 음량 ──
        vb.AddChild(Section("음 량"));
        vb.AddChild(Slider("MASTER   전체", () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v));
        vb.AddChild(Slider("BGM        배경음악", () => GameSettings.BgmVolume, v => GameSettings.BgmVolume = v));
        vb.AddChild(Slider("SFX        효과음", () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v));

        vb.AddChild(Rule());

        // ── 화면 ──
        vb.AddChild(Section("화 면"));
        var fsRow = Row();
        fsRow.AddChild(Lbl("전체화면", 21, Ink, _body, 240f));
        var fsBtn = DocButton(GameSettings.Fullscreen ? "켜짐" : "꺼짐", 140f);
        fsBtn.Pressed += () =>
        {
            GameSettings.Fullscreen = !GameSettings.Fullscreen;
            fsBtn.Text = GameSettings.Fullscreen ? "켜짐" : "꺼짐";
            GameSettings.Save();
        };
        fsRow.AddChild(fsBtn);
        fsRow.AddChild(Lbl("화면 비율은 그대로 유지된 채 모니터 크기에 맞춰 확대됩니다.", 14, InkDim, _body));
        vb.AddChild(fsRow);

        vb.AddChild(Rule());

        // ── 조작키 ──
        vb.AddChild(Section("조 작 키"));
        vb.AddChild(Lbl("항목을 누른 뒤 원하는 키를 입력하세요.  (ESC = 취소)", 14, InkDim, _body));
        foreach (var (target, label) in GameSettings.ZoomTargets)
            vb.AddChild(KeyRow(target, label));

        vb.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // ── 하단 버튼 ──
        var bottom = Row();
        var reset = DocButton("조작키 초기화", 200f);
        reset.Pressed += () => { GameSettings.ResetKeysToDefault(); RefreshKeyButtons(); GameSettings.Save(); };
        bottom.AddChild(reset);
        bottom.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = DocButton("닫기", 180f);
        close.Pressed += Close;
        bottom.AddChild(close);
        vb.AddChild(bottom);
    }

    private HBoxContainer Row()
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 14);
        return h;
    }

    private Control Section(string title)
    {
        var l = Lbl(title, 22, InkRed, _serif);
        l.CustomMinimumSize = new Vector2(0, 34);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }

    private Control Rule()
    {
        var r = new ColorRect { Color = new Color(0.3f, 0.24f, 0.14f, 0.45f), CustomMinimumSize = new Vector2(0, 1.5f) };
        return r;
    }

    private Label Lbl(string text, int size, Color col, Font font, float minWidth = 0f)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", col);
        if (minWidth > 0f) l.CustomMinimumSize = new Vector2(minWidth, 0);
        return l;
    }

    // 음량 한 줄: 이름 + 슬라이더 + 퍼센트.
    private Control Slider(string label, System.Func<float> get, System.Action<float> set)
    {
        var row = Row();
        row.AddChild(Lbl(label, 21, Ink, _body, 260f));

        var s = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 1, Value = Mathf.Round(get() * 100f),
            CustomMinimumSize = new Vector2(360, 30),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var grabber = new StyleBoxFlat { BgColor = new Color(0.30f, 0.22f, 0.12f), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 };
        s.AddThemeStyleboxOverride("slider", new StyleBoxFlat { BgColor = new Color(0.55f, 0.48f, 0.34f, 0.55f), ContentMarginTop = 5, ContentMarginBottom = 5 });
        s.AddThemeStyleboxOverride("grabber_area", new StyleBoxFlat { BgColor = new Color(0.52f, 0.30f, 0.14f), ContentMarginTop = 5, ContentMarginBottom = 5 });
        s.AddThemeStyleboxOverride("grabber_area_highlight", (StyleBoxFlat)grabber.Duplicate());
        row.AddChild(s);

        var pct = Lbl($"{(int)s.Value}%", 20, InkDim, _body, 76f);
        row.AddChild(pct);

        s.ValueChanged += v =>
        {
            set((float)v / 100f);
            pct.Text = $"{(int)v}%";
            GameSettings.Save();
        };
        return row;
    }

    private Control KeyRow(GameSettings.ZoomTarget target, string label)
    {
        var row = Row();
        row.AddChild(Lbl(label, 21, Ink, _body, 260f));
        var b = DocButton(GameSettings.KeyName(GameSettings.GetKey(target)), 160f);
        b.Pressed += () =>
        {
            _awaitingKey = target;
            RefreshKeyButtons();
        };
        _keyButtons[target] = b;
        row.AddChild(b);
        return row;
    }

    private void RefreshKeyButtons()
    {
        foreach (var (target, btn) in _keyButtons)
        {
            if (!IsInstanceValid(btn)) continue;
            btn.Text = _awaitingKey == target ? "키 입력…" : GameSettings.KeyName(GameSettings.GetKey(target));
        }
    }

    // 배치표 버튼과 같은 서류 톤.
    private Button DocButton(string text, float minWidth)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 40) };
        b.AddThemeFontOverride("font", _body);
        b.AddThemeFontSizeOverride("font_size", 19);
        b.AddThemeColorOverride("font_color", new Color(0.15f, 0.11f, 0.07f));
        b.AddThemeColorOverride("font_hover_color", new Color(0.99f, 0.96f, 0.88f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.99f, 0.96f, 0.88f));
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.90f, 0.86f, 0.73f),
            BorderColor = new Color(0.4f, 0.32f, 0.2f, 0.7f),
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.72f, 0.60f, 0.30f);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

    // 종이 얼룩 — 배치표(ScheduleBoardUI.PaperTexture)와 같은 결.
    private partial class PaperGrain : Control
    {
        public override void _Ready() => SetAnchorsPreset(LayoutPreset.FullRect);

        public override void _Draw()
        {
            var rng = new RandomNumberGenerator { Seed = 771144 };
            for (int i = 0; i < 22; i++)
            {
                var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
                DrawCircle(p, rng.RandfRange(24f, 84f), new Color(0.42f, 0.33f, 0.18f, rng.RandfRange(0.03f, 0.075f)));
            }
            for (int i = 0; i < 420; i++)
            {
                var p = new Vector2(rng.RandfRange(0, Size.X), rng.RandfRange(0, Size.Y));
                bool light = rng.Randf() > 0.5f;
                DrawRect(new Rect2(p, new Vector2(1.6f, 1.6f)),
                    light ? new Color(0.96f, 0.92f, 0.79f, 0.07f) : new Color(0.32f, 0.25f, 0.14f, 0.07f));
            }
            var edge = new Color(0.28f, 0.21f, 0.11f, 0.20f);
            const float b = 18f;
            DrawRect(new Rect2(0, 0, Size.X, b), edge);
            DrawRect(new Rect2(0, Size.Y - b, Size.X, b), edge);
            DrawRect(new Rect2(0, 0, b, Size.Y), edge);
            DrawRect(new Rect2(Size.X - b, 0, b, Size.Y), edge);
        }
    }
}
