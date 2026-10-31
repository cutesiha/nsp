using Godot;
using NSP.Core;

namespace NSP.View;

// 환경 설정 창. 게임 안의 기기가 아니라 메타 설정이므로 시스템 오버레이로 띄우되,
// 시설 로그 창(Day1HistoryOverlay)과 같은 홀로그램 창 — 청록 테두리 · HologramFrame · 스캔라인 — 을 쓴다.
// 값은 전부 GameSettings 가 들고 있고, 여기서는 읽고 쓰기만 한다(항목 · 동작은 종이 시절과 동일).
//   · 음량 : MASTER(전체) / BGM(배경음악) / SFX(효과음)
//   · 화면 : 전체화면 켜기·끄기 / 그래픽 품질(3D 렌더 배율)
//   · 조작 : 모니터1 / 모니터2 / 경고 단말기 / 전력 기기 확대 숫자키
public partial class SettingsPanel : CanvasLayer
{
    // 시설 로그 창과 같은 색.
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Ink = new(0.84f, 0.92f, 0.92f);
    private static readonly Color InkDim = new(0.50f, 0.66f, 0.68f);
    private static readonly Color WindowBg = new(0.03f, 0.09f, 0.11f, 0.94f);

    private Control _root;
    private Font _serif, _body;
    // 키 재지정 대기 중인 항목(다음 키 입력을 이 기능에 배정한다).
    private GameSettings.ZoomTarget? _awaitingKey;
    private readonly System.Collections.Generic.Dictionary<GameSettings.ZoomTarget, Button> _keyButtons = new();

    // ── 모니터 안에 띄우기 ─────────────────────────────────────────────
    //
    // 시작 화면에서는 「근무 개시 · 기록 열람 · 시스템 종료」 가 전부 모니터1 단말기 안에서
    // 열리는데, 설정만 화면 위에 따로 창으로 떠서 혼자 튀었다. 시작 화면이 부를 때는 이 창을
    // **모니터1 의 화면(SubViewport) 안에** 그린다 — 기록 열람과 같은 자리다.
    // 마우스는 그 화면 위를 가리킨 좌표로 TitleRoomDirector 가 밀어 넣어 준다.
    // 키 입력(ESC · 키 재지정)은 이 노드가 그대로 받는다(이 노드 자체는 바깥 트리에 있다).
    // 일시정지 메뉴에서 열 때는 예전 그대로 화면 위 창이다(host 없이 만든다).
    private readonly Control _host;
    private bool Embedded => _host != null;
    // 모니터 안 글자는 화면 프레임이 통째로 키우므로 S, 화면 위 창은 FS 를 쓴다.
    private int Fz(int px) => Embedded ? ViewFont.S(px) : ViewFont.FS(px);

    public SettingsPanel() { }

    // host = 모니터 화면 안의 논리 캔버스(800 × 600) 프레임.
    public SettingsPanel(Control host) { _host = host; }

    public event System.Action Closed;

    public bool IsOpen => _root != null && GodotObject.IsInstanceValid(_root) && _root.Visible
                          && (Embedded || Visible);

    public override void _Ready()
    {
        // 일시정지 창(120)보다 위 — 거기서 열었을 때 가려지면 클릭조차 안 된다.
        Layer = 130;
        Visible = false;
        _body = ViewFont.Default;
        _serif = _body;   // 홀로그램 창은 로그 창처럼 본문 글꼴 하나로 통일한다
        if (Embedded) BuildEmbeddedUI();
        else BuildUI();
    }

    public override void _ExitTree()
    {
        // 모니터 안에 붙여 둔 화면은 이 노드의 자식이 아니다 — 같이 치운다.
        if (Embedded && _root != null && GodotObject.IsInstanceValid(_root)) _root.QueueFree();
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
        if (Embedded && _root != null) _root.Visible = false;
        Closed?.Invoke();
    }

    // 키 재지정 대기 중에는 다음 키 입력을 가로챈다.
    public override void _Input(InputEvent e)
    {
        if (!IsOpen) return;

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

        // 홀로그램 창 — 시설 로그 창과 같은 바탕 · 테두리 · 프레임(코너 괄호 + 스캔라인 + 흐르는 선).
        var sheet = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            // 900px 높이 기본 창 안에서 하단 버튼까지 모두 포함한다.
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

        // Panel 은 컨테이너가 아니라 스타일박스의 ContentMargin 을 자식에 적용하지 않는다.
        // 여백은 여기서 앵커 오프셋으로 직접 준다.
        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vb.OffsetLeft = 46; vb.OffsetRight = -46;
        // 하단 버튼은 VBox 밖에 별도로 고정한다. 위 항목의 글이 바뀌어도 움직이지 않는다.
        vb.OffsetTop = 40; vb.OffsetBottom = -84;
        vb.AddThemeConstantOverride("separation", 8);
        sheet.AddChild(vb);

        vb.AddChild(Lbl("SYSTEM CONFIG  /  환경 설정", 26, Cyan, _body));
        vb.AddChild(Rule());

        // ── 음량 ──
        vb.AddChild(Section("음 량"));
        vb.AddChild(Slider("MASTER   전체", () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v));
        vb.AddChild(Slider("BGM        배경음악", () => GameSettings.BgmVolume, v => GameSettings.BgmVolume = v));
        vb.AddChild(Slider("SFX        효과음", () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v));

        vb.AddChild(Rule());

        // ── 화면 ──
        vb.AddChild(Section("화 면"));
        vb.AddChild(FullscreenRow(240f, 140f));
        vb.AddChild(QualityRow(240f, 140f));
        vb.AddChild(FlashRow(240f, 140f));

        vb.AddChild(Rule());

        // ── 조작키 ──
        vb.AddChild(Section("조 작 키"));
        vb.AddChild(Lbl("항목을 누른 뒤 원하는 키를 입력하세요.  (ESC = 취소)", 14, InkDim, _body));
        foreach (var (target, label) in GameSettings.ZoomTargets)
            vb.AddChild(KeyRow(target, label));
        vb.AddChild(FixedKeysBlock());

        // ── 하단 버튼 ──
        var bottom = Row();
        bottom.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        bottom.OffsetLeft = 46; bottom.OffsetRight = -46;
        bottom.OffsetTop = -64; bottom.OffsetBottom = -20;
        var reset = DocButton("조작키 초기화", 200f);
        reset.Pressed += () => { GameSettings.ResetKeysToDefault(); RefreshKeyButtons(); GameSettings.Save(); };
        bottom.AddChild(reset);
        bottom.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = DocButton("닫기", 180f);
        close.Pressed += Close;
        bottom.AddChild(close);
        sheet.AddChild(bottom);
    }

    // 모니터1 안(800 × 600)에 들어가는 판. 세로로 다 쌓으면 넘치므로 두 단으로 나눈다 —
    // 왼쪽 = 음량 · 화면, 오른쪽 = 조작키. 단말기 화면 전체를 덮어 뒤의 메뉴가 비치지 않게 한다.
    private void BuildEmbeddedUI()
    {
        _root = new Control
        {
            Name = "SettingsScreen",
            Position = Vector2.Zero,
            Size = new Vector2(800f, 600f),
            MouseFilter = Control.MouseFilterEnum.Stop,
            Visible = false,
        };
        _host.AddChild(_root);

        _root.AddChild(new ColorRect
        {
            Color = new Color(0.02f, 0.05f, 0.06f, 1f),
            Size = new Vector2(800f, 600f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var sheet = new Panel
        {
            Position = new Vector2(14f, 12f),
            Size = new Vector2(772f, 576f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
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

        // 머리띠(HologramFrame 의 띠)를 제목 글자 높이에서 거꾸로 잡는다. 기본 30px 띠는
        // 제목 한가운데를 가로질러, 띠 아랫선 · 아래 구분선 · 글자가 서로 어긋나 보였다.
        // 위아래 여백을 똑같이 두고 제목 · 힌트를 띠 안 세로 가운데에 앉힌다
        // (PhoneCallHud · StoryCutinHud 의 이름띠와 같은 방식). 띠 아랫선이 곧 구분선이다.
        const float headPad = 10f;
        float titleH = Mathf.Ceil(_body.GetHeight(Fz(24)));
        frame.BandHeight = Mathf.Round(headPad * 2f + titleH);

        var title = Lbl("SYSTEM CONFIG  /  환경 설정", 24, Cyan, _body);
        title.Position = new Vector2(30f, headPad);
        title.Size = new Vector2(560f, titleH);
        sheet.AddChild(title);
        var hint = Lbl("ESC  뒤로", 14, InkDim, _body);
        hint.Position = new Vector2(600f, headPad);
        hint.Size = new Vector2(140f, titleH);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        sheet.AddChild(hint);
        // 본문은 띠 아래에서 시작한다(띠가 글꼴 따라 커져도 겹치지 않게).
        float bodyTop = Mathf.Max(66f, frame.BandHeight + 6f);

        // ── 왼쪽 단 : 음량 · 화면 ──
        var left = new VBoxContainer { Position = new Vector2(30f, bodyTop), Size = new Vector2(372f, 420f) };
        left.AddThemeConstantOverride("separation", 6);
        sheet.AddChild(left);
        left.AddChild(Section("음 량"));
        left.AddChild(Slider("MASTER", () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, 112f, 176f, 58f));
        left.AddChild(Slider("BGM", () => GameSettings.BgmVolume, v => GameSettings.BgmVolume = v, 112f, 176f, 58f));
        left.AddChild(Slider("SFX", () => GameSettings.SfxVolume, v => GameSettings.SfxVolume = v, 112f, 176f, 58f));
        left.AddChild(Section("화 면"));
        left.AddChild(FullscreenRow(150f, 150f));
        left.AddChild(QualityRow(150f, 150f));
        left.AddChild(FlashRow(150f, 150f));

        // ── 오른쪽 단 : 조작키 ──
        var right = new VBoxContainer { Position = new Vector2(424f, bodyTop), Size = new Vector2(318f, 420f) };
        right.AddThemeConstantOverride("separation", 6);
        sheet.AddChild(right);
        right.AddChild(Section("조 작 키"));
        var how = Lbl("항목을 누른 뒤 원하는 키를 입력하세요.", 13, InkDim, _body);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        how.CustomMinimumSize = new Vector2(318f, 0f);
        right.AddChild(how);
        foreach (var (target, label) in GameSettings.ZoomTargets)
            right.AddChild(KeyRow(target, label, 184f, 120f));
        var fixedKeys = new HFlowContainer();
        fixedKeys.AddThemeConstantOverride("h_separation", 14);
        fixedKeys.AddThemeConstantOverride("v_separation", 6);
        fixedKeys.CustomMinimumSize = new Vector2(318f, 0f);
        fixedKeys.AddChild(KeyChip("L", "로그"));
        fixedKeys.AddChild(KeyChip("D", "대화 기록"));
        fixedKeys.AddChild(KeyChip("ESC", "일시정지"));
        right.AddChild(fixedKeys);

        // ── 아래 버튼 ──
        var reset = DocButton("조작키 초기화", 190f);
        reset.Position = new Vector2(30f, 506f);
        reset.Size = new Vector2(190f, 44f);
        reset.Pressed += () => { GameSettings.ResetKeysToDefault(); RefreshKeyButtons(); GameSettings.Save(); };
        sheet.AddChild(reset);
        var close = DocButton("뒤로", 170f);
        close.Position = new Vector2(572f, 506f);
        close.Size = new Vector2(170f, 44f);
        close.Pressed += Close;
        sheet.AddChild(close);
    }

    private Control FullscreenRow(float labelW, float btnW)
    {
        var row = Row();
        row.AddChild(Lbl("전체화면", Embedded ? 18 : 21, Ink, _body, labelW));
        var fsBtn = DocButton(GameSettings.Fullscreen ? "켜짐" : "꺼짐", btnW);
        fsBtn.Pressed += () =>
        {
            GameSettings.Fullscreen = !GameSettings.Fullscreen;
            fsBtn.Text = GameSettings.Fullscreen ? "켜짐" : "꺼짐";
            GameSettings.Save();
        };
        row.AddChild(fsBtn);
        return row;
    }

    // 깜빡임 · 흔들림 줄이기(광과민성 배려). 켜면 붉은 점멸이 느린 맥박으로, 섬광 · 흔들림이 약하게 바뀐다.
    private Control FlashRow(float labelW, float btnW)
    {
        var row = Row();
        row.AddChild(Lbl("깜빡임 줄이기", Embedded ? 18 : 21, Ink, _body, labelW));
        var btn = DocButton(GameSettings.ReduceFlashing ? "켜짐" : "꺼짐", btnW);
        btn.TooltipText = "번쩍임 · 점멸 · 화면 흔들림을 약하게 합니다(광과민성 발작 예방).";
        btn.Pressed += () =>
        {
            GameSettings.ReduceFlashing = !GameSettings.ReduceFlashing;
            btn.Text = GameSettings.ReduceFlashing ? "켜짐" : "꺼짐";
            GameSettings.Save();
        };
        row.AddChild(btn);
        return row;
    }

    private Control QualityRow(float labelW, float btnW)
    {
        var row = Row();
        row.AddChild(Lbl("그래픽 품질", Embedded ? 18 : 21, Ink, _body, labelW));
        var qBtn = DocButton(GameSettings.QualityLabel, btnW);
        qBtn.TooltipText = "낮음에서는 빛 번짐이 꺼집니다.";
        qBtn.Pressed += () =>
        {
            // 그 자리에서 바뀐다 — 재시작하지 않는다.
            int next = ((int)GameSettings.GraphicsQuality + 1) % GameSettings.QualityLevels.Length;
            GameSettings.GraphicsQuality = (GameSettings.Quality)next;
            qBtn.Text = GameSettings.QualityLabel;
            GameSettings.Save();
        };
        row.AddChild(qBtn);
        return row;
    }

    private HBoxContainer Row()
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 14);
        return h;
    }

    private Control Section(string title)
    {
        var l = Lbl(title, 20, Cyan, _serif);
        l.CustomMinimumSize = new Vector2(0, 34);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        return l;
    }

    private Control Rule()
    {
        var r = new ColorRect { Color = Cyan with { A = 0.22f }, CustomMinimumSize = new Vector2(0, 1f) };
        return r;
    }

    private Label Lbl(string text, int size, Color col, Font font, float minWidth = 0f)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", Fz(size));
        l.AddThemeColorOverride("font_color", col);
        if (minWidth > 0f) l.CustomMinimumSize = new Vector2(minWidth, 0);
        return l;
    }

    // 음량 한 줄: 이름 + 슬라이더 + 퍼센트.
    private Control Slider(string label, System.Func<float> get, System.Action<float> set,
        float labelW = 260f, float sliderW = 360f, float pctW = 76f)
    {
        var row = Row();
        row.AddChild(Lbl(label, Embedded ? 18 : 21, Ink, _body, labelW));

        var s = new HSlider
        {
            MinValue = 0, MaxValue = 100, Step = 1, Value = Mathf.Round(get() * 100f),
            CustomMinimumSize = new Vector2(sliderW, 30),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var grabber = new StyleBoxFlat { BgColor = Cyan, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 };
        s.AddThemeStyleboxOverride("slider", new StyleBoxFlat { BgColor = Cyan with { A = 0.14f }, BorderColor = Cyan with { A = 0.35f }, BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginTop = 5, ContentMarginBottom = 5 });
        s.AddThemeStyleboxOverride("grabber_area", new StyleBoxFlat { BgColor = Cyan with { A = 0.55f }, ContentMarginTop = 5, ContentMarginBottom = 5 });
        s.AddThemeStyleboxOverride("grabber_area_highlight", (StyleBoxFlat)grabber.Duplicate());
        row.AddChild(s);

        var pct = Lbl($"{(int)s.Value}%", Embedded ? 17 : 20, InkDim, _body, pctW);
        row.AddChild(pct);

        s.ValueChanged += v =>
        {
            set((float)v / 100f);
            pct.Text = $"{(int)v}%";
            GameSettings.Save();
        };
        return row;
    }

    private Control KeyRow(GameSettings.ZoomTarget target, string label, float labelW = 260f, float btnW = 160f)
    {
        var row = Row();
        row.AddChild(Lbl(label, Embedded ? 17 : 21, Ink, _body, labelW));
        var b = DocButton(GameSettings.KeyName(GameSettings.GetKey(target)), btnW);
        b.Pressed += () =>
        {
            _awaitingKey = target;
            RefreshKeyButtons();
        };
        _keyButtons[target] = b;
        row.AddChild(b);
        return row;
    }

    // 재지정할 수 없는 고정 키. 숫자키 목록 바로 아래에 한 줄로 붙는다
    // (종이 높이가 정해져 있어 세 줄로 쌓으면 하단 버튼과 겹친다).
    private Control FixedKeysBlock()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        row.AddChild(KeyChip("L", "로그 열람"));
        row.AddChild(KeyChip("D", "대화 기록 열람"));
        row.AddChild(KeyChip("ESC", "일시정지 메뉴"));
        box.AddChild(row);
        return box;
    }

    private Control KeyChip(string key, string what)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 7);

        var k = Lbl(key, 18, Ink, _body);
        k.HorizontalAlignment = HorizontalAlignment.Center;
        k.CustomMinimumSize = new Vector2(key.Length > 1 ? 54f : 38f, 30f);
        k.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = Cyan with { A = 0.10f },
            BorderColor = Cyan with { A = 0.6f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        });
        h.AddChild(k);
        h.AddChild(Lbl(what, 17, InkDim, _body));
        return h;
    }

    private void RefreshKeyButtons()
    {
        foreach (var (target, btn) in _keyButtons)
        {
            if (!IsInstanceValid(btn)) continue;
            btn.Text = _awaitingKey == target ? "키 입력…" : GameSettings.KeyName(GameSettings.GetKey(target));
        }
    }

    // 콘솔 버튼 — 모니터 단말기 버튼(MonitorUi)과 같은 청록 발광 테두리.
    // (이름은 호출부를 건드리지 않으려고 그대로 둔다.)
    private Button DocButton(string text, float minWidth)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 40) };
        b.AddThemeFontOverride("font", _body);
        b.AddThemeFontSizeOverride("font_size", Fz(19));
        b.AddThemeColorOverride("font_color", Cyan);
        b.AddThemeColorOverride("font_hover_color", Colors.White);
        b.AddThemeColorOverride("font_pressed_color", Colors.White);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(Cyan.R * 0.16f, Cyan.G * 0.16f, Cyan.B * 0.16f, 0.6f),
            BorderColor = Cyan with { A = 0.5f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(Cyan.R * 0.34f, Cyan.G * 0.34f, Cyan.B * 0.34f, 0.8f);
        hover.BorderColor = Cyan;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MonitorUi.AddFeedback(b);
        return b;
    }

    // 창 오른쪽 위 ✕ — 시설 로그 창의 닫기 버튼과 같은 모양. 하단 "닫기"와 같은 동작(Close).
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
        close.AddThemeFontSizeOverride("font_size", Fz(22));
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
        close.Pressed += Close;
        MonitorUi.AddFeedback(close);
        return close;
    }
}
