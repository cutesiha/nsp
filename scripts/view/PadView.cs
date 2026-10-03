using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 관리자 패드 화면(NSP-07 관리자 단말) — 실제 태블릿처럼 홈 화면에서 앱을 연다.
//
//   상태바   NSP-07 · DAY n + 게임 시각 · 전력 채널 · 배터리
//   홈       앱 아이콘 [지침] [단서] [직원] + 사고 예고 배너(AlertSystem — 패드가 이어받았다)
//   지침     챕터 카드(2×3) → 그림책 페이지(그림 + 제목 + 최대 4줄, ◀ ▶ · 점 표시)
//   단서     카드(2×3, 썸네일이 카드의 2/3) → 오른쪽 절반 상세
//   직원     초상 카드(2×3, 얼굴 중심 크롭) → 인사 기록
//   잠금     거치대에 놓여 있을 때 — 시각 · 사고 예고 점멸 · 새 단서 수
//   전원     패드 채널이 꺼지면 글리치 → 검은 화면, 켜지면 부팅 로고 → 홈
//
// 이 화면은 보여 주기만 한다. 자료를 만들지 않는다 — 단서는 ClueBoard, 자료 내용은
// InterviewEvidenceBoard, 진술은 DialogueHistory, 지침은 data/pad/manual.tres 에서 읽는다.
public partial class PadView : Control
{
    public enum Tab { Home, Manual, Clues, Staff }

    public static readonly Vector2 Layout = new(1120, 700);

    public const float BootSeconds = 0.8f;
    public const int CardsPerPage = 6;

    // ── 글자 크기 규약(논리 캔버스 1120×700 기준) ──────────────────────────
    // 손에 들고 코앞에서 읽는 화면이다. 16 미만은 쓰지 않는다.
    private const int FsStatus = 18;    // 상태바
    private const int FsAppLabel = 22;  // 홈 앱 아이콘 라벨
    private const int FsScreen = 26;    // 화면 제목(앱 바)
    private const int FsSection = 22;   // 섹션 제목 · 카드 제목
    private const int FsBody = 19;      // 본문
    private const int FsCaption = 16;   // 보조 · 캡션(최소값)

    private const float StatusH = 44f;
    private const float AppBarH = 62f;
    private const float Top = StatusH + AppBarH;     // 앱 본문이 시작하는 높이
    private static readonly Vector2 Card = new(346, 268);
    private const float CardGap = 16f;
    // 단서 상세(오른쪽에서 밀려 나오는 판)의 너비. 본문 19 로도 스크롤 없이 담기는 크기다.
    private const float DetailWidth = 668f;

    private static readonly Color Bg = new(0.025f, 0.045f, 0.055f);
    private static readonly Color PanelBg = new(0.04f, 0.075f, 0.09f, 0.97f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Dim = new(0.50f, 0.62f, 0.66f);
    private static readonly Color Ink = new(0.86f, 0.92f, 0.94f);
    private static readonly Color Amber = new(1f, 0.80f, 0.36f);
    private static readonly Color Red = new(1f, 0.42f, 0.34f);
    private static readonly Color Green = new(0.45f, 0.95f, 0.55f);

    public Tab Current { get; private set; } = Tab.Home;

    // ── 단서 ──
    public bool GroupByEmployee { get; private set; }
    public string EmployeeFilter { get; private set; } = "";
    public int Page { get; private set; }
    public ClueBoard.Entry DetailEntry { get; private set; }
    private readonly HashSet<string> _openStatements = new();

    // ── 직원 ──
    public string StaffDetailId { get; private set; } = "";

    // ── 지침 ──
    public int ManualChapter { get; private set; } = -1;   // -1 = 챕터 목록
    public int ManualPage { get; private set; }

    // ── 전원 · 거치 ──
    public bool ScreenOn { get; private set; } = true;
    public bool IsGlitching => _powerNoise is { Visible: true } && _powerLayer is { Visible: true };
    public bool IsStowed { get; private set; } = true;
    // 거치 중 잠금 화면인가. 근무 · 휴게시간에는 거치대 위에서도 홈(앱 세 개)이 켜져 있다(= false).
    public bool IsLocked { get; private set; } = true;

    private Font _font;
    private PadManualDef _manual;
    private Label _statusTime;
    private StatusIcons _statusIcons;
    private Control _home, _app, _appBody, _appTools;
    private Label _appTitle;
    private Button _backBtn;
    private PanelContainer _banner;
    private Label _bannerText;
    private Control _detail;
    private Control _lock;
    private Label _lockTime, _lockAlert, _lockAlertSub, _lockClues;
    private Control _powerLayer, _powerLogo;
    private PadRepairOverlay _repair;

    // 수리 승인 절차가 화면을 잡고 있는가. 잡고 있으면 다른 앱 조작을 받지 않는다.
    public bool RepairOverlayActive => _repair is { Visible: true };

    // 화면을 눌렀다 — 승인 요청의 [예]/[아니오] 였으면 true.
    public bool TryRepairPress(Vector2 at) => _repair != null && _repair.TryPress(at);
    private ColorRect _powerBar;
    private TextureRect _powerNoise;
    private Tween _appTween, _slide, _powerTween;
    private float _tick, _noiseTick, _blink;
    private int _noiseFrame;
    private readonly Dictionary<string, Texture2D> _images = new();

    public override void _Ready()
    {
        _font = ViewFont.Default;
        Size = Layout;
        MouseFilter = MouseFilterEnum.Stop;
        _manual = GD.Load<PadManualDef>(PadManualDef.DefaultPath);

        AddChild(new ColorRect { Color = Bg, Size = Layout, MouseFilter = MouseFilterEnum.Ignore });
        BuildStatusBar();
        BuildHome();
        BuildApp();
        BuildLock();
        // 수리 승인 절차 — 잠금 화면 위, 전원 연출 아래. 거치 중에도 떠야 관리자가 보고 집어 든다.
        _repair = new PadRepairOverlay(Layout) { Visible = false };
        AddChild(_repair);
        BuildPowerLayer();

        ClueBoard.Changed += OnClueChanged;
        GoHome(fade: false);
        SetStowed(true);
    }

    public override void _ExitTree() => ClueBoard.Changed -= OnClueChanged;

    // ═════════════════════════════════════════════════════════════════════
    //  AdminPad3D 가 부르는 곳
    // ═════════════════════════════════════════════════════════════════════

    // 패드를 들었다 — 열면 언제나 홈 화면부터.
    public void OnOpened()
    {
        _tick = 0f;
        SetStowed(false);
        GoHome(fade: false);
    }

    // 거치대에 내려놓았다 — 어느 앱에 있었든 홈으로 돌아가 놓인다.
    public void OnClosed(bool locked = true)
    {
        GoHome(fade: false);
        SetStowed(true, locked);
    }

    // 화면을 눌렀다 — 누른 자리에 짧게 물결 하나(논리 좌표). 클릭 피드백은 전부 이 UI 층에서만 준다.
    // 패드 · 손 · 카메라는 클릭에 절대 움직이지 않는다(AdminPad3D.OnSurfacePressed 참고).
    public void FlashTouch(Vector2 at)
    {
        if (!IsInsideTree() || !ScreenOn || IsStowed) return;
        var dot = new TouchDot { Position = at };
        AddChild(dot);
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(dot.SetProgress), 0f, 1f, 0.26)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        t.TweenCallback(Callable.From(dot.QueueFree));
    }

    // 누른 자리에서 번지는 고리 하나.
    private partial class TouchDot : Control
    {
        private float _t;

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public void SetProgress(float t) { _t = t; QueueRedraw(); }

        public override void _Draw()
        {
            float r = Mathf.Lerp(9f, 48f, _t);
            float a = 1f - _t;
            DrawCircle(Vector2.Zero, r * 0.55f, Cyan with { A = a * 0.22f });
            DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 28, Cyan with { A = a * 0.9f }, 2.5f);
        }
    }

    // 거치대에 놓여 있다. locked = 잠금 화면(시각 · 사고 예고 · 새 단서) — 근무 · 휴게시간 밖.
    // 근무 · 휴게시간에는 잠그지 않는다 — 책상 위에서도 홈(지침 · 단서 · 직원)이 켜져 있고,
    // 사고 예고는 홈의 배너가 점멸하며 알린다.
    public void SetStowed(bool stowed, bool locked = true)
    {
        IsStowed = stowed;
        IsLocked = stowed && locked;
        if (stowed && !locked && Current != Tab.Home) GoHome(fade: false);
        if (_lock != null) _lock.Visible = IsLocked;
        if (_homeHint != null) _homeHint.Text = stowed ? StowedHint : HeldHint;
        RefreshLock();
        RefreshBanner();
    }

    // 거치대 위 화면에서 누른 자리의 앱 아이콘(논리 좌표). 없으면 null.
    public Tab? AppAt(Vector2 at)
    {
        if (IsLocked || Current != Tab.Home) return null;
        foreach (var (tab, btn) in _appButtons)
            if (btn.GetRect().HasPoint(at)) return tab;
        return null;
    }

    // 지금 알릴 사고 예고(가장 급한 것). 패드 전원이 없으면 알림도 없다.
    public static Alert CurrentAlert()
    {
        if (!AdminPad3D.PadPowered) return null;
        return AlertSystem.Instance?.GetActiveAlerts().FirstOrDefault();
    }

    public bool HasAlert => ScreenOn && CurrentAlert() != null;

    private void OnClueChanged(ClueBoard.Entry entry, bool pinned)
    {
        if (!IsInsideTree()) return;
        if (!pinned && DetailEntry == entry) CloseDetail(instant: true);
        RefreshHome();
        RefreshLock();
        if (Current is Tab.Clues or Tab.Staff) RebuildCurrent();
    }

    public override void _Process(double delta)
    {
        if (_powerNoise is { Visible: true })
        {
            _noiseTick += (float)delta;
            if (_noiseTick > 0.03f) { _noiseTick = 0f; _powerNoise.Texture = NoiseTexture(++_noiseFrame); }
        }

        _blink += (float)delta;
        _tick -= (float)delta;
        if (_tick > 0f) return;
        _tick = 0.25f;

        var gs = GameState.Instance;
        int day = gs?.CurrentDay ?? 1;
        string when = gs?.CurrentPhase == GamePhase.Rest ? "휴게시간" : DialogueClock.Text(gs?.DayTimeSeconds ?? 0f);
        if (_statusTime != null) _statusTime.Text = $"DAY {day} · {when}";
        _statusIcons?.QueueRedraw();
        RefreshBanner();
        RefreshLock();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  상태바
    // ═════════════════════════════════════════════════════════════════════

    private void BuildStatusBar()
    {
        AddChild(new ColorRect { Color = new Color(0.02f, 0.06f, 0.075f), Size = new Vector2(Layout.X, StatusH),
                                 MouseFilter = MouseFilterEnum.Ignore });
        AddChild(new ColorRect { Color = Cyan with { A = 0.25f }, Position = new Vector2(0, StatusH - 1),
                                 Size = new Vector2(Layout.X, 1), MouseFilter = MouseFilterEnum.Ignore });
        var brand = Lbl("NSP-07", FsStatus, Cyan);
        brand.Position = new Vector2(18, 8);
        AddChild(brand);

        _statusTime = Lbl("", FsStatus, Ink);
        _statusTime.Position = new Vector2(0, 8);
        _statusTime.Size = new Vector2(Layout.X, 28);
        _statusTime.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_statusTime);

        _statusIcons = new StatusIcons { Position = new Vector2(Layout.X - 392, 4), Size = new Vector2(378, 36), Font = _font };
        AddChild(_statusIcons);
    }

    // 전력 채널(조명 · CCTV · 패드) 불빛 + 배터리(장식 — 87% 고정).
    private partial class StatusIcons : Control
    {
        public Font Font;

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Draw()
        {
            var gs = GameState.Instance;
            var chans = new (string Label, PowerConsumer C)[]
                { ("조명", PowerConsumer.Lighting), ("CCTV", PowerConsumer.CctvWatch), ("패드", PowerConsumer.Sensor) };
            float x = 0f;
            int fs = ViewFont.S(FsCaption);
            foreach (var (label, c) in chans)
            {
                bool on = gs == null || gs.IsConsumerPowered(c);
                DrawCircle(new Vector2(x + 8, 18), 6f, on ? Green : new Color(0.3f, 0.35f, 0.36f));
                DrawString(Font, new Vector2(x + 19, 25), label, HorizontalAlignment.Left, -1, fs, on ? Ink : Dim);
                x += label == "CCTV" ? 82f : 68f;
            }
            // 배터리
            var b = new Rect2(x + 22, 9, 40, 18);
            DrawRect(b, Ink, false, 1.5f);
            DrawRect(new Rect2(b.End.X, 14, 4, 8), Ink);
            DrawRect(new Rect2(b.Position + new Vector2(2, 2), new Vector2((b.Size.X - 4) * 0.87f, b.Size.Y - 4)), Green);
            DrawString(Font, new Vector2(x + 72, 25), "87%", HorizontalAlignment.Left, -1, fs, Ink);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  홈
    // ═════════════════════════════════════════════════════════════════════

    private readonly Dictionary<Tab, Label> _homeBadges = new();

    private void BuildHome()
    {
        _home = new Control { Size = Layout, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_home);

        // 사고 예고 배너 — 상태바 바로 아래.
        _banner = new PanelContainer { Position = new Vector2(24, StatusH + 12), Size = new Vector2(Layout.X - 48, 54),
                                       MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _banner.AddThemeStyleboxOverride("panel", Box(new Color(0.22f, 0.07f, 0.04f, 0.95f), Red, 14, 8));
        _bannerText = Lbl("", FsBody, new Color(1f, 0.88f, 0.8f));
        _bannerText.VerticalAlignment = VerticalAlignment.Center;
        _banner.AddChild(_bannerText);
        _home.AddChild(_banner);

        var apps = new (Tab Tab, string Label, AppGlyph.Kind Kind)[]
        {
            (Tab.Manual, "지침", AppGlyph.Kind.Book),
            (Tab.Clues, "단서", AppGlyph.Kind.Star),
            (Tab.Staff, "직원", AppGlyph.Kind.Person),
        };
        const float tile = 190f, gap = 88f;
        float total = apps.Length * tile + (apps.Length - 1) * gap;
        float x0 = (Layout.X - total) * 0.5f, y0 = 250f;
        for (int i = 0; i < apps.Length; i++)
        {
            var (tab, label, kind) = apps[i];
            var t = tab;
            var btn = new Button { Position = new Vector2(x0 + i * (tile + gap), y0), Size = new Vector2(tile, tile),
                                   FocusMode = FocusModeEnum.None };
            btn.AddThemeStyleboxOverride("normal", Box(new Color(0.05f, 0.11f, 0.13f), Cyan with { A = 0.45f }, 0, 0, 28));
            btn.AddThemeStyleboxOverride("hover", Box(new Color(0.08f, 0.17f, 0.2f), Cyan, 0, 0, 28));
            btn.AddThemeStyleboxOverride("pressed", Box(new Color(0.08f, 0.17f, 0.2f), Cyan, 0, 0, 28));
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            btn.Pressed += () => OpenApp(t);
            _appButtons[tab] = btn;
            var glyph = new AppGlyph { Shape = kind, Position = new Vector2(38, 38), Size = new Vector2(tile - 76, tile - 76) };
            btn.AddChild(glyph);
            _home.AddChild(btn);

            var name = Lbl(label, FsAppLabel, Ink);
            name.Position = new Vector2(btn.Position.X, y0 + tile + 12);
            name.Size = new Vector2(tile, 38);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            _home.AddChild(name);

            if (tab == Tab.Clues)
            {
                var badge = Lbl("", FsCaption, Colors.Black);
                badge.HorizontalAlignment = HorizontalAlignment.Center;
                badge.VerticalAlignment = VerticalAlignment.Center;
                badge.Position = new Vector2(btn.Position.X + tile - 46, y0 - 16);
                badge.Size = new Vector2(64, 34);
                var bb = new StyleBoxFlat { BgColor = Amber };
                bb.SetCornerRadiusAll(17);
                badge.AddThemeStyleboxOverride("normal", bb);
                _home.AddChild(badge);
                _homeBadges[tab] = badge;
            }
        }

        _homeHint = Lbl(IsStowed ? StowedHint : HeldHint, FsCaption, Dim);
        _homeHint.Position = new Vector2(0, Layout.Y - 46);
        _homeHint.Size = new Vector2(Layout.X, 28);
        _homeHint.HorizontalAlignment = HorizontalAlignment.Center;
        _home.AddChild(_homeHint);
    }

    private const string HeldHint = "앱 안에서 우클릭 = 뒤로     ·     홈에서 우클릭 · Tab = 패드 내려놓기";
    private const string StowedHint = "앱을 누르거나 Tab — 패드 들기";
    private readonly Dictionary<Tab, Button> _appButtons = new();
    private Label _homeHint;

    private void RefreshHome()
    {
        if (_homeBadges.TryGetValue(Tab.Clues, out var badge))
        {
            int n = ClueBoard.Count, unseen = ClueBoard.UnseenCount;
            badge.Visible = n > 0;
            badge.Text = unseen > 0 ? $"+{unseen}" : n.ToString();
        }
        RefreshBanner();
    }

    private void RefreshBanner()
    {
        if (_banner == null) return;
        var a = CurrentAlert();
        _banner.Visible = a != null && Current == Tab.Home;
        if (a == null) return;
        string when = string.IsNullOrEmpty(a.Countdown) ? "" : $"   ·   {a.Countdown}";
        _bannerText.Text = $"⚠  {a.SubLabel}  ·  {a.Headline}{when}";
        // 거치대 위에서는 크게 점멸한다(관리자가 패드를 집어 들 이유). 든 동안은 급한 예고만 은은하게.
        float a01 = IsStowed ? (Mathf.PosMod(_blink, 1f) < 0.62f ? 1f : 0.3f)
                  : a.Severity == AlertSeverity.Critical ? 0.75f + 0.25f * Mathf.Sin(_blink * 8f) : 1f;
        _banner.Modulate = Colors.White with { A = a01 };
    }

    // 앱 아이콘 그림 — 에셋 없이 선으로 그린다.
    private partial class AppGlyph : Control
    {
        public enum Kind { Book, Star, Person }
        public Kind Shape;

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Draw()
        {
            var c = Cyan;
            var s = Size;
            var m = s * 0.5f;
            switch (Shape)
            {
                case Kind.Book:
                {
                    var l = new Rect2(s.X * 0.08f, s.Y * 0.14f, s.X * 0.4f, s.Y * 0.72f);
                    var r = new Rect2(s.X * 0.52f, s.Y * 0.14f, s.X * 0.4f, s.Y * 0.72f);
                    DrawRect(l, c, false, 4f);
                    DrawRect(r, c, false, 4f);
                    for (int i = 0; i < 4; i++)
                    {
                        float y = s.Y * (0.3f + 0.13f * i);
                        DrawLine(new Vector2(l.Position.X + 10, y), new Vector2(l.End.X - 10, y), c with { A = 0.7f }, 3f);
                        DrawLine(new Vector2(r.Position.X + 10, y), new Vector2(r.End.X - 10, y), c with { A = 0.7f }, 3f);
                    }
                    break;
                }
                case Kind.Star:
                {
                    var pts = new Vector2[11];
                    for (int i = 0; i < 10; i++)
                    {
                        float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
                        float rr = (i % 2 == 0 ? 0.46f : 0.2f) * s.X;
                        pts[i] = m + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    }
                    pts[10] = pts[0];
                    DrawPolyline(pts, Amber, 4f, true);
                    break;
                }
                default:
                {
                    DrawArc(new Vector2(m.X, s.Y * 0.34f), s.X * 0.17f, 0, Mathf.Tau, 32, c, 4f);
                    DrawArc(new Vector2(m.X, s.Y * 0.98f), s.X * 0.38f, Mathf.Pi * 1.08f, Mathf.Pi * 1.92f, 32, c, 4f);
                    break;
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  앱 틀(◀ 홈 · 제목 · 도구줄) + 전환
    // ═════════════════════════════════════════════════════════════════════

    private void BuildApp()
    {
        _app = new Control { Size = Layout, MouseFilter = MouseFilterEnum.Ignore, Visible = false, PivotOffset = Layout * 0.5f };
        AddChild(_app);

        _app.AddChild(new ColorRect { Color = new Color(0.03f, 0.07f, 0.085f), Position = new Vector2(0, StatusH),
                                      Size = new Vector2(Layout.X, AppBarH), MouseFilter = MouseFilterEnum.Ignore });
        // 뒤로 — 모든 앱 화면 좌상단에 늘 같은 자리 · 같은 문구. 한 단계씩 올라간다(상세 → 목록 → 홈).
        _backBtn = Btn(BackLabel, Cyan, OnBack, FsBody);
        _backBtn.Position = new Vector2(14, StatusH + 8);
        _backBtn.Size = BackButtonSize;
        _app.AddChild(_backBtn);

        _appTitle = Lbl("", FsScreen, Ink);
        _appTitle.Position = new Vector2(14 + BackButtonSize.X + 22, StatusH + 12);
        _appTitle.Size = new Vector2(420, 38);
        _app.AddChild(_appTitle);

        _appTools = new Control { Position = new Vector2(0, StatusH), Size = new Vector2(Layout.X, AppBarH),
                                  MouseFilter = MouseFilterEnum.Ignore };
        _app.AddChild(_appTools);

        _appBody = new Control { Position = new Vector2(0, Top), Size = new Vector2(Layout.X, Layout.Y - Top),
                                 MouseFilter = MouseFilterEnum.Ignore };
        _app.AddChild(_appBody);

        _detail = new Panel
        {
            Position = new Vector2(Layout.X, Top), Size = new Vector2(DetailWidth, Layout.Y - Top),
            MouseFilter = MouseFilterEnum.Stop, Visible = false,
        };
        // 완전 불투명 — 화면이 거의 검어서 3% 만 비쳐도 뒤 카드 글자가 드러난다.
        _detail.AddThemeStyleboxOverride("panel", Box(PanelBg with { A = 1f }, Cyan with { A = 0.5f }, 0, 0));
        _app.AddChild(_detail);
    }

    // 앱을 연다 — 아이콘에서 커지듯 짧게 확대 · 페이드.
    public void OpenApp(Tab tab, bool fade = true)
    {
        if (tab == Tab.Home) { GoHome(fade); return; }
        Current = tab;
        CloseDetail(instant: true);
        _home.Visible = false;
        _app.Visible = true;
        if (tab == Tab.Clues) ClueBoard.MarkSeen();
        RebuildCurrent();
        RefreshHome();

        _appTween?.Kill();
        if (!fade || !IsInsideTree()) { _app.Scale = Vector2.One; _app.Modulate = Colors.White; return; }
        _app.Scale = new Vector2(0.93f, 0.93f);
        _app.Modulate = Colors.White with { A = 0f };
        _appTween = CreateTween().SetParallel(true);
        _appTween.TweenProperty(_app, "scale", Vector2.One, 0.18).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _appTween.TweenProperty(_app, "modulate:a", 1f, 0.16);
    }

    public void GoHome(bool fade = true)
    {
        Current = Tab.Home;
        CloseDetail(instant: true);
        _app.Visible = false;
        _home.Visible = true;
        RefreshHome();
        _appTween?.Kill();
        if (!fade || !IsInsideTree()) { _home.Modulate = Colors.White; return; }
        _home.Modulate = Colors.White with { A = 0f };
        _appTween = CreateTween();
        _appTween.TweenProperty(_home, "modulate:a", 1f, 0.14);
    }

    // 예전 탭 방식과 같은 이름 — 검사 · 캡처 코드가 쓴다.
    public void SwitchTab(Tab tab, bool fade = true) => OpenApp(tab, fade);

    // 뒤로 버튼 — 어느 화면에서나 같은 문구 · 같은 자리 · 같은 크기(터치 타깃 최소 56×44).
    private const string BackLabel = "◀ 뒤로";
    private static readonly Vector2 BackButtonSize = new(142, 46);

    // 한 단계 위로. 단서 상세 → 단서 목록 → 홈, 지침 페이지 → 챕터 목록 → 홈, 직원 상세 → 직원 목록 → 홈.
    private void OnBack() => TryGoBack();

    // 올라갈 곳이 있었으면 true. 홈이면 false(= 패드를 내려놓을 차례다 — AdminPad3D 가 판단한다).
    public bool TryGoBack()
    {
        if (_detail is { Visible: true }) { CloseDetail(); return true; }
        if (Current == Tab.Manual && ManualChapter >= 0) { OpenChapter(-1); return true; }
        if (Current == Tab.Staff && !string.IsNullOrEmpty(StaffDetailId)) { OpenStaffDetail(""); return true; }
        if (Current == Tab.Home) return false;
        GoHome();
        return true;
    }

    private void RebuildCurrent()
    {
        Clear(_appTools);
        Clear(_appBody);
        switch (Current)
        {
            case Tab.Manual: BuildManual(); break;
            case Tab.Clues: BuildClues(); break;
            case Tab.Staff: BuildStaff(); break;
        }
    }

    // 직원 앱 → 단서 앱(그 직원 필터).
    public void ShowCluesFor(string employeeId)
    {
        EmployeeFilter = employeeId ?? "";
        Page = 0;
        OpenApp(Tab.Clues);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  지침 — 챕터 카드 → 그림책 페이지
    // ═════════════════════════════════════════════════════════════════════

    private List<PadManualChapterDef> Chapters() => _manual?.ChapterList() ?? new List<PadManualChapterDef>();

    public void OpenChapter(int index)
    {
        ManualChapter = index;
        ManualPage = 0;
        if (Current != Tab.Manual) { OpenApp(Tab.Manual); return; }
        RebuildCurrent();
    }

    public void SetManualPage(int page)
    {
        var ch = Chapters().ElementAtOrDefault(ManualChapter);
        if (ch == null) return;
        ManualPage = Math.Clamp(page, 0, Math.Max(0, ch.PageList().Count - 1));
        RebuildCurrent();
    }

    private void BuildManual()
    {
        var chapters = Chapters();
        _appTitle.Text = ManualChapter >= 0 && ManualChapter < chapters.Count
            ? $"{ManualChapter + 1}장  {chapters[ManualChapter].Title}" : _manual?.Title ?? "관리자 지침";

        if (ManualChapter < 0 || ManualChapter >= chapters.Count)
        {
            ManualChapter = -1;
            if (chapters.Count == 0) { EmptyNote("지침 문서를 불러오지 못했습니다."); return; }
            for (int i = 0; i < chapters.Count; i++)
                _appBody.AddChild(ChapterCard(i, chapters[i], CardPos(i)));
            return;
        }

        var ch = chapters[ManualChapter];
        var pages = ch.PageList();
        if (pages.Count == 0) { EmptyNote("이 장에는 아직 페이지가 없습니다."); return; }
        var page = pages[Math.Clamp(ManualPage, 0, pages.Count - 1)];

        // 그림 → 제목 → 본문 4줄 → 쪽 넘김. 글자가 커진 만큼 그림을 조금 낮추고 줄 간격을 넓혔다.
        float bodyH = Layout.Y - Top;
        float imgH = Mathf.Round(bodyH * 0.48f), imgW = Mathf.Round(imgH * 16f / 9f);
        var img = ImageBox(page.ImagePath, new Vector2(imgW, imgH));
        img.Position = new Vector2((Layout.X - imgW) * 0.5f, 10);
        _appBody.AddChild(img);

        var title = Lbl(page.Title, FsSection, Amber);
        title.Position = new Vector2(60, imgH + 22);
        title.Size = new Vector2(Layout.X - 120, 36);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _appBody.AddChild(title);

        var lines = page.Lines ?? Array.Empty<string>();
        for (int i = 0; i < Math.Min(lines.Length, PadManualDef.MaxLinesPerPage); i++)
        {
            var l = Lbl(lines[i], FsBody, Ink);
            l.Position = new Vector2(60, imgH + 66 + i * 34);
            l.Size = new Vector2(Layout.X - 120, 32);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            _appBody.AddChild(l);
        }

        // ◀ ● ○ ○ ▶
        float navY = bodyH - 52;
        var prev = Btn("◀", Cyan, () => SetManualPage(ManualPage - 1), FsSection);
        prev.Position = new Vector2(Layout.X * 0.5f - 196, navY);
        prev.Size = new Vector2(62, 44);
        prev.Disabled = ManualPage <= 0;
        _appBody.AddChild(prev);
        var next = Btn("▶", Cyan, () => SetManualPage(ManualPage + 1), FsSection);
        next.Position = new Vector2(Layout.X * 0.5f + 134, navY);
        next.Size = new Vector2(62, 44);
        next.Disabled = ManualPage >= pages.Count - 1;
        _appBody.AddChild(next);
        var dots = new PageDots { Count = pages.Count, Index = ManualPage,
                                  Position = new Vector2(Layout.X * 0.5f - 120, navY), Size = new Vector2(240, 44) };
        _appBody.AddChild(dots);
    }

    private Control ChapterCard(int index, PadManualChapterDef ch, Vector2 pos)
    {
        var card = CardButton(Cyan);
        card.Position = pos;
        int captured = index;
        card.Pressed += () => OpenChapter(captured);
        var thumb = ImageBox(ch.ThumbnailPath(), new Vector2(Card.X - 16, 180));
        thumb.Position = new Vector2(8, 8);
        card.AddChild(thumb);
        var t = Lbl($"{index + 1}장  {ch.Title}", FsBody, Ink);
        t.Position = new Vector2(12, 196);
        t.Size = new Vector2(Card.X - 24, 30);
        card.AddChild(t);
        var n = Lbl($"{ch.PageList().Count}쪽", FsCaption, Dim);
        n.Position = new Vector2(12, 230);
        n.Size = new Vector2(Card.X - 24, 26);
        card.AddChild(n);
        return card;
    }

    private partial class PageDots : Control
    {
        public int Count, Index;

        public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

        public override void _Draw()
        {
            float step = 22f, x0 = Size.X * 0.5f - (Count - 1) * step * 0.5f;
            for (int i = 0; i < Count; i++)
            {
                var p = new Vector2(x0 + i * step, Size.Y * 0.5f);
                if (i == Index) DrawCircle(p, 6f, Cyan);
                else DrawArc(p, 5f, 0, Mathf.Tau, 16, Cyan with { A = 0.6f }, 1.5f);
            }
        }
    }

    // 그림 한 장 — res:// 그림을 읽고, 없으면 파일 이름이 적힌 자리표시로 둔다(직접 캡처해 교체할 수 있게).
    private Control ImageBox(string path, Vector2 size)
    {
        var box = new Control { Size = size, CustomMinimumSize = size, ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(new ColorRect { Color = new Color(0.01f, 0.02f, 0.025f), Size = size, MouseFilter = MouseFilterEnum.Ignore });
        var tex = LoadImage(path);
        if (tex != null)
        {
            box.AddChild(new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Texture = tex, Size = size, MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        else
        {
            var l = Lbl($"그림 준비 중\n{System.IO.Path.GetFileName(path ?? "")}", FsCaption, Dim);
            l.Size = size;
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.VerticalAlignment = VerticalAlignment.Center;
            box.AddChild(l);
        }
        var frame = new Panel { Size = size, MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", Box(Colors.Transparent, Cyan with { A = 0.3f }, 0, 0));
        box.AddChild(frame);
        return box;
    }

    // 에디터가 가져온(import) 그림이면 리소스로, 아직 가져오기 전의 새 PNG 면 파일에서 바로 읽는다.
    private Texture2D LoadImage(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_images.TryGetValue(path, out var cached)) return cached;
        Texture2D tex = null;
        if (ResourceLoader.Exists(path)) tex = GD.Load<Texture2D>(path);
        else if (FileAccess.FileExists(path))
        {
            var img = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            if (img != null && !img.IsEmpty()) tex = ImageTexture.CreateFromImage(img);
        }
        _images[path] = tex;
        return tex;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  단서
    // ═════════════════════════════════════════════════════════════════════

    // 지금 보이는 단서(필터 · 정렬 적용, 페이지 나누기 전).
    public List<ClueBoard.Entry> VisibleClues()
    {
        IEnumerable<ClueBoard.Entry> list = ClueBoard.Entries;
        if (!string.IsNullOrEmpty(EmployeeFilter)) list = list.Where(e => ClueBoard.Involves(e, EmployeeFilter));

        var byTime = list
            .OrderBy(e => e.Day)
            .ThenBy(e => e.Evidence.HasTime ? 1 : 0)
            .ThenBy(e => e.Evidence.AnchorTime)
            .ThenBy(e => e.Seq);
        if (!GroupByEmployee) return byTime.ToList();

        var order = EmployeeOrder();
        return byTime.OrderBy(e => order.IndexOf(PrimaryEmployee(e.Evidence)) is var i && i >= 0 ? i : 99).ToList();
    }

    public int PageCount => Math.Max(1, (VisibleClues().Count + CardsPerPage - 1) / CardsPerPage);

    public void SetPage(int page)
    {
        Page = Math.Clamp(page, 0, PageCount - 1);
        if (Current == Tab.Clues) RebuildCurrent();
    }

    public void SetGroupByEmployee(bool on)
    {
        GroupByEmployee = on;
        Page = 0;
        if (Current == Tab.Clues) RebuildCurrent();
    }

    public void ClearEmployeeFilter()
    {
        EmployeeFilter = "";
        Page = 0;
        if (Current == Tab.Clues) RebuildCurrent();
    }

    private void BuildClues()
    {
        _appTitle.Text = $"단서  {ClueBoard.Count}";
        var all = VisibleClues();
        int pages = Math.Max(1, (all.Count + CardsPerPage - 1) / CardsPerPage);
        Page = Math.Clamp(Page, 0, pages - 1);

        // 도구줄 — 정렬 · 직원 필터 · 쪽.
        var sort = Btn(GroupByEmployee ? "정렬: 직원별" : "정렬: 시간순", Cyan, () => SetGroupByEmployee(!GroupByEmployee), FsCaption);
        sort.Position = new Vector2(Layout.X - 596, 9);
        sort.Size = new Vector2(164, 44);
        _appTools.AddChild(sort);
        if (!string.IsNullOrEmpty(EmployeeFilter))
        {
            var def = FacilitySimulation.Instance?.GetEmployeeDef(EmployeeFilter);
            var chip = Btn($"{def?.Codename ?? EmployeeFilter}  ✕", Readable(def?.IconColor ?? Cyan), ClearEmployeeFilter, FsCaption);
            chip.Position = new Vector2(Layout.X - 420, 9);
            chip.Size = new Vector2(136, 44);
            _appTools.AddChild(chip);
        }
        var prev = Btn("◀", Cyan, () => SetPage(Page - 1), FsBody);
        prev.Position = new Vector2(Layout.X - 272, 9);
        prev.Size = new Vector2(56, 44);
        prev.Disabled = Page <= 0;
        _appTools.AddChild(prev);
        var pl = Lbl($"{Page + 1} / {pages}", FsCaption, Dim);
        pl.Position = new Vector2(Layout.X - 212, 17);
        pl.Size = new Vector2(96, 28);
        pl.HorizontalAlignment = HorizontalAlignment.Center;
        _appTools.AddChild(pl);
        var next = Btn("▶", Cyan, () => SetPage(Page + 1), FsBody);
        next.Position = new Vector2(Layout.X - 112, 9);
        next.Size = new Vector2(56, 44);
        next.Disabled = Page >= pages - 1;
        _appTools.AddChild(next);

        if (all.Count == 0)
        {
            EmptyNote(string.IsNullOrEmpty(EmployeeFilter)
                ? "핀으로 기록한 단서가 없습니다.\n시설 로그의 ☆를 눌러 수상한 기록을 보관하세요."
                : $"{Codename(EmployeeFilter)} 직원이 등장하는 단서가 없습니다.");
            return;
        }
        int k = 0;
        foreach (var entry in all.Skip(Page * CardsPerPage).Take(CardsPerPage))
            _appBody.AddChild(ClueCard(entry, CardPos(k++)));
    }

    // 카드 — 썸네일이 카드의 2/3 이상, 그 아래 제목(18자) · 시각 · 관련 직원 색 점. 설명 문장은 넣지 않는다.
    private Control ClueCard(ClueBoard.Entry entry, Vector2 pos)
    {
        var ev = entry.Evidence;
        var tag = PhoneCallHud.TagColor(ev);
        var card = CardButton(tag);
        card.Position = pos;
        var captured = entry;
        card.Pressed += () => OpenDetail(captured);

        var thumb = Thumbnail(entry, new Vector2(Card.X - 16, 180));
        thumb.Position = new Vector2(8, 8);
        card.AddChild(thumb);

        var tagBg = new PanelContainer { Position = new Vector2(14, 14), MouseFilter = MouseFilterEnum.Ignore };
        tagBg.AddThemeStyleboxOverride("panel", Box(tag, tag, 8, 2));
        tagBg.AddChild(Lbl(InterviewEvidenceDisplay.Tag(ev), FsCaption, Colors.Black));
        card.AddChild(tagBg);

        var title = Lbl(Short(ClueTitle(ev), 17), FsBody, Ink);
        title.Position = new Vector2(12, 194);
        title.Size = new Vector2(Card.X - 24, 30);
        card.AddChild(title);

        var when = Lbl(WhenText(entry), FsCaption, Dim);
        when.Position = new Vector2(12, 228);
        when.Size = new Vector2(Card.X - 110, 26);
        card.AddChild(when);

        float dx = Card.X - 20;
        foreach (string id in StaffOf(ev).AsEnumerable().Reverse())
        {
            var def = FacilitySimulation.Instance?.GetEmployeeDef(id);
            if (def == null) continue;
            card.AddChild(new ColorRect { Color = def.IconColor, Position = new Vector2(dx - 14, 234), Size = new Vector2(14, 14),
                                          MouseFilter = MouseFilterEnum.Ignore });
            dx -= 20;
        }
        return card;
    }

    // 카드 한 줄 제목 — 무슨 일이었는가.
    private static string ClueTitle(InterviewEvidence ev) => ev.Kind switch
    {
        EvidenceKind.Incident => $"{InterviewEvidenceBoard.RoomName(ev.SubjectRoomId)} {IncidentWord(ev)}",
        EvidenceKind.Movement => $"{Codename(ev.SubjectEmployeeId)} → {InterviewEvidenceBoard.RoomName(ev.ToRoomId)}",
        EvidenceKind.Overheard => $"{Codename(ev.SubjectEmployeeId)} · {string.Join(" · ", ev.RelatedEmployeeIds.Select(Codename))}의 대화",
        EvidenceKind.Cctv => $"CCTV · {InterviewEvidenceBoard.RoomName(ev.SubjectRoomId)}",
        EvidenceKind.Testimony => $"{Codename(ev.SpeakerEmployeeId)}의 증언",
        EvidenceKind.OwnStatement => $"{Codename(ev.SubjectEmployeeId)}의 진술",
        _ => $"{Codename(ev.SubjectEmployeeId)}의 기분",
    };

    // 업무 실패(TaskFailed)는 한 순간에 "고장" · "파손" 두 줄이 함께 찍히기도 해서 본문 낱말로 가른다.
    private static string IncidentWord(InterviewEvidence ev) => ev.IncidentType switch
    {
        LogEventType.Sabotage => "방해공작",
        LogEventType.PowerOutage => "정전",
        LogEventType.TabooViolation => "금기 위반",
        LogEventType.CctvDisconnect => "CCTV 단절",
        LogEventType.Death => "사망",
        LogEventType.AnomalyIncident => "이상 개체",
        LogEventType.TaskFailed when ev.Body.Contains("파손") => "설비 파손",
        LogEventType.TaskFailed when ev.Body.Contains("고장") => "설비 고장",
        _ => "사고",
    };

    // 썸네일 — 사건 순간의 CCTV 한 컷. 없으면 사고는 NO SIGNAL(정적 노이즈 + 스탬프), 그 밖은 종류 글자.
    private Control Thumbnail(ClueBoard.Entry entry, Vector2 size)
    {
        var box = new Control { Size = size, CustomMinimumSize = size, MouseFilter = MouseFilterEnum.Ignore, ClipContents = true };
        box.AddChild(new ColorRect { Color = new Color(0.01f, 0.02f, 0.025f), Size = size, MouseFilter = MouseFilterEnum.Ignore });

        var tex = entry.Snapshot ?? ClueBoard.SnapshotOf(entry.Day, entry.EvidenceId);
        if (tex != null)
        {
            box.AddChild(new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                Texture = tex, Size = size, MouseFilter = MouseFilterEnum.Ignore,
            });
            return box;
        }

        if (entry.Evidence.Kind == EvidenceKind.Incident)
        {
            box.AddChild(new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                Texture = NoiseTexture(entry.Seq), Size = size, MouseFilter = MouseFilterEnum.Ignore,
                Modulate = new Color(0.55f, 0.6f, 0.62f),
            });
            var stamp = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            stamp.AddThemeStyleboxOverride("panel", Box(new Color(0f, 0f, 0f, 0.7f), Red with { A = 0.9f }, 12, 4));
            stamp.AddChild(Lbl("NO SIGNAL", FsSection, Red));
            stamp.Position = size * 0.5f - new Vector2(88, 22);
            stamp.RotationDegrees = -6f;
            box.AddChild(stamp);
            return box;
        }

        var ev = entry.Evidence;
        var tag = PhoneCallHud.TagColor(ev);
        var faces = StaffOf(ev).Take(3).Select(id => FacilitySimulation.Instance?.GetEmployeeDef(id)).Where(d => d != null).ToList();
        if (faces.Count == 0)
        {
            var l = Lbl(InterviewEvidenceDisplay.Tag(ev), 30, tag with { A = 0.45f });
            l.Size = size;
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.VerticalAlignment = VerticalAlignment.Center;
            box.AddChild(l);
            return box;
        }

        // 스냅샷이 없는 기록 — CCTV 화면 표시처럼: 관련 직원 얼굴 + 아래 한 줄(방 · 이동 · REC).
        box.AddChild(new ColorRect { Color = (tag * 0.10f + Bg * 0.90f) with { A = 1f }, Size = size, MouseFilter = MouseFilterEnum.Ignore });
        box.AddChild(new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = NoiseTexture(entry.Seq), Size = size, MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1f, 1f, 1f, 0.06f),
        });
        float fs = size.Y - 74f, gap = 12f;
        float x0 = (size.X - (faces.Count * fs + (faces.Count - 1) * gap)) * 0.5f;
        for (int i = 0; i < faces.Count; i++)
        {
            var def = faces[i];
            var frame = new Panel { Position = new Vector2(x0 + i * (fs + gap), 34f), Size = new Vector2(fs, fs), ClipContents = true,
                                    MouseFilter = MouseFilterEnum.Ignore };
            frame.AddThemeStyleboxOverride("panel", Box((def.IconColor * 0.25f + Bg * 0.75f) with { A = 1f }, Colors.Transparent, 0, 0, 6));
            frame.AddChild(FacePortrait(def, frame.Size));
            var rim = new Panel { Size = frame.Size, MouseFilter = MouseFilterEnum.Ignore };
            rim.AddThemeStyleboxOverride("panel", Box(Colors.Transparent, def.IconColor with { A = 0.8f }, 0, 0, 6));
            frame.AddChild(rim);
            box.AddChild(frame);
        }

        string osd = ev.Kind switch
        {
            EvidenceKind.Cctv => $"CAM · {InterviewEvidenceBoard.RoomName(ev.SubjectRoomId)}",
            EvidenceKind.Movement when !string.IsNullOrEmpty(ev.FromRoomId)
                => $"{InterviewEvidenceBoard.RoomName(ev.FromRoomId)} → {InterviewEvidenceBoard.RoomName(ev.ToRoomId)}",
            EvidenceKind.Movement => $"→ {InterviewEvidenceBoard.RoomName(ev.ToRoomId)}",
            EvidenceKind.Overheard when !string.IsNullOrEmpty(ev.SubjectRoomId)
                => $"❝ {InterviewEvidenceBoard.RoomName(ev.SubjectRoomId)}에서 엿들음",
            _ => $"❝ {InterviewEvidenceDisplay.Tag(ev)}",
        };
        var line = Lbl(osd, FsCaption, Ink with { A = 0.85f });
        line.Position = new Vector2(10, size.Y - 34f);
        line.Size = new Vector2(size.X - 20, 28);
        line.ClipText = true;
        box.AddChild(line);
        if (ev.Kind == EvidenceKind.Cctv)
        {
            var rec = Lbl("● REC", FsCaption, Red);
            rec.Position = new Vector2(size.X - 84, 10);
            rec.Size = new Vector2(74, 24);
            rec.HorizontalAlignment = HorizontalAlignment.Right;
            box.AddChild(rec);
        }
        return box;
    }

    // ── 상세(오른쪽 절반) ─────────────────────────────────────────────

    public void OpenDetail(ClueBoard.Entry entry)
    {
        if (entry == null) return;
        bool same = DetailEntry == entry;
        DetailEntry = entry;
        if (!same) _openStatements.Clear();
        BuildDetail();

        _detail.Visible = true;
        _slide?.Kill();
        float to = Layout.X - DetailWidth;
        if (!IsInsideTree()) { _detail.Position = _detail.Position with { X = to }; return; }
        if (!same) _detail.Position = _detail.Position with { X = Layout.X };
        _slide = CreateTween();
        _slide.TweenProperty(_detail, "position:x", to, 0.18).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    public void CloseDetail(bool instant = false)
    {
        if (_detail == null) return;
        DetailEntry = null;
        _slide?.Kill();
        if (instant || !IsInsideTree())
        {
            _detail.Visible = false;
            _detail.Position = _detail.Position with { X = Layout.X };
            return;
        }
        _slide = CreateTween();
        _slide.TweenProperty(_detail, "position:x", Layout.X, 0.14);
        _slide.TweenCallback(Callable.From(() => _detail.Visible = false));
    }

    private void BuildDetail()
    {
        Clear(_detail);
        var entry = DetailEntry;
        if (entry == null) return;
        var ev = entry.Evidence;
        var tag = PhoneCallHud.TagColor(ev);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(14, 10), Size = _detail.Size - new Vector2(28, 20),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _detail.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(col);

        var head = new HBoxContainer();
        var title = Lbl($"[{InterviewEvidenceDisplay.Tag(ev)}]  {ClueTitle(ev)}", FsSection, tag);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.ClipText = true;
        head.AddChild(title);
        var x = Btn("✕", Dim, () => CloseDetail(), FsBody);
        x.CustomMinimumSize = new Vector2(46, 40);
        head.AddChild(x);
        col.AddChild(head);

        // 스냅샷 + 시각 · 사건을 한 줄에 나란히 — 본문 19 를 지키면서 스크롤 없이 담는다.
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 12);
        float w = Mathf.Round(_detail.Size.X * 0.46f);
        top.AddChild(Thumbnail(entry, new Vector2(w, Mathf.Round(w * 9f / 16f))));
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", 4);
        top.AddChild(side);
        col.AddChild(top);

        side.AddChild(IconRow("◷", "시각 · 장소", Cyan));
        string room = string.IsNullOrEmpty(ev.SubjectRoomId) ? "" : $" · {InterviewEvidenceBoard.RoomName(ev.SubjectRoomId)}";
        side.AddChild(Wrap(Lbl($"{WhenText(entry)}{room}", FsBody, Ink)));
        side.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        side.AddChild(IconRow("▤", "사건", Cyan));
        side.AddChild(Wrap(Lbl(FirstSentences(ev.Body, 2), FsBody, Ink)));

        col.AddChild(Separator());
        col.AddChild(IconRow("●", "관련 직원", Cyan));
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 6);
        chips.AddThemeConstantOverride("v_separation", 4);
        var staff = RelatedStaff(entry);
        foreach (string id in staff) chips.AddChild(StaffChip(id));
        if (staff.Count == 0) chips.AddChild(Lbl("—", FsBody, Dim));
        col.AddChild(chips);

        col.AddChild(Separator());
        col.AddChild(IconRow("❝", "관련 진술", Cyan));
        var talks = RelatedStatements(entry);
        if (talks.Count == 0)
            col.AddChild(Wrap(Lbl("진술 없음 — 휴게시간에 물어볼 수 있습니다.", FsBody, Dim)));
        foreach (var (who, lines) in talks)
        {
            string key = who;
            bool open = _openStatements.Contains(key);
            int answers = lines.Count(l => l.EntryType != DialogueEntryType.PlayerChoice);
            var toggle = Btn($"{(open ? "▾" : "▸")} {Codename(who)}의 진술 ({answers})",
                Readable(FacilitySimulation.Instance?.GetEmployeeDef(who)?.IconColor ?? Cyan), () =>
                {
                    if (!_openStatements.Remove(key)) _openStatements.Add(key);
                    BuildDetail();
                }, FsBody);
            toggle.Alignment = HorizontalAlignment.Left;
            toggle.CustomMinimumSize = new Vector2(0, 44);
            col.AddChild(toggle);
            if (!open) continue;
            foreach (var l in lines)
            {
                bool mine = l.EntryType == DialogueEntryType.PlayerChoice;
                col.AddChild(Wrap(Lbl((mine ? "   관리자: " : $"   {l.SpeakerDisplayName}: ") + l.Text, FsBody, mine ? Dim : Ink)));
            }
        }

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var unpin = Btn("★ 핀 해제", Amber, () => ClueBoard.Unpin(entry.Day, entry.EvidenceId), FsBody);
        unpin.CustomMinimumSize = new Vector2(0, 46);
        col.AddChild(unpin);
    }

    private Control IconRow(string icon, string label, Color c)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Lbl(icon, FsSection, c));
        row.AddChild(Lbl(label, FsSection, c with { A = 0.8f }));
        return row;
    }

    private static Control Separator() =>
        new ColorRect { Color = Cyan with { A = 0.18f }, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore };

    // 설명은 두 문장까지만 — 나머지는 카드가 아니라 심문에서 확인한다.
    public static string FirstSentences(string text, int n)
    {
        if (string.IsNullOrEmpty(text)) return "";
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or '。')) continue;
            if (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1])) continue;
            if (++count >= n) return text[..(i + 1)];
        }
        return text;
    }

    // 이 단서의 관련 직원. 오늘 단서면 조사 자료판의 "같은 시간대" 창(SameWindow)을 그대로 써서
    // 같은 방 기록에 잡힌 사람까지 더한다. 지난 날 단서는 원천이 비워졌으므로 단서 자체만 본다.
    public static List<string> RelatedStaff(ClueBoard.Entry entry)
    {
        var ev = entry?.Evidence;
        var ids = new List<string>();
        if (ev == null) return ids;
        foreach (string id in StaffOf(ev)) if (!ids.Contains(id)) ids.Add(id);

        int today = GameState.Instance?.CurrentDay ?? 1;
        if (entry.Day != today || !ev.HasTime || string.IsNullOrEmpty(ev.SubjectRoomId)) return ids;

        var board = InterviewEvidenceBoard.BuildAll("");
        foreach (string otherId in InterviewEvidenceBoard.SameWindow(board, ev))
        {
            var other = InterviewEvidenceBoard.Find(board, otherId);
            if (other == null || other.SubjectRoomId != ev.SubjectRoomId || other.Position == PositionClaim.None) continue;
            foreach (string id in StaffOf(other)) if (!ids.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    // 이 단서를 들고 한 질문 · 대답(그 단서의 날). 심문한 직원별로 묶는다.
    public static List<(string Employee, List<DialogueHistoryEntry> Lines)> RelatedStatements(ClueBoard.Entry entry)
    {
        var result = new List<(string, List<DialogueHistoryEntry>)>();
        var all = DialogueHistory.Instance?.GetAllEntries();
        if (entry == null || all == null) return result;
        foreach (var g in all.Where(e => e.Day == entry.Day && e.Mentions(entry.EvidenceId)
                                         && !string.IsNullOrEmpty(e.CounterpartId))
                     .GroupBy(e => e.CounterpartId))
            result.Add((g.Key, g.OrderBy(e => e.Seq).ToList()));
        return result;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  직원
    // ═════════════════════════════════════════════════════════════════════

    public void OpenStaffDetail(string employeeId)
    {
        StaffDetailId = employeeId ?? "";
        if (Current != Tab.Staff) { OpenApp(Tab.Staff); return; }
        RebuildCurrent();
    }

    private void BuildStaff()
    {
        if (!string.IsNullOrEmpty(StaffDetailId)) { BuildStaffDetail(StaffDetailId); return; }
        _appTitle.Text = "직원";
        int k = 0;
        foreach (string id in EmployeeOrder()) _appBody.AddChild(StaffCard(id, CardPos(k++)));
    }

    private Control StaffCard(string id, Vector2 pos)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(id);
        var color = def?.IconColor ?? Cyan;
        var card = CardButton(color);
        card.Position = pos;
        string captured = id;
        card.Pressed += () => OpenStaffDetail(captured);

        var portrait = new Control { Position = new Vector2(8, 8), Size = new Vector2(Card.X - 16, 208), ClipContents = true,
                                     MouseFilter = MouseFilterEnum.Ignore };
        portrait.AddChild(new ColorRect { Color = color * 0.25f + Bg * 0.75f, Size = portrait.Size, MouseFilter = MouseFilterEnum.Ignore });
        portrait.AddChild(FacePortrait(def, portrait.Size));
        card.AddChild(portrait);

        var name = Lbl(def?.Codename ?? id, FsSection, Readable(color));
        name.Position = new Vector2(12, 222);
        name.Size = new Vector2(Card.X - 24, 36);
        name.VerticalAlignment = VerticalAlignment.Center;
        card.AddChild(name);

        int clues = ClueBoard.CountFor(id);
        var count = Lbl(clues > 0 ? $"단서 {clues}건" : "단서 없음", FsCaption, clues > 0 ? Amber : Dim);
        count.Position = new Vector2(12, 222);
        count.Size = new Vector2(Card.X - 24, 36);
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.VerticalAlignment = VerticalAlignment.Center;
        card.AddChild(count);

        var (badge, badgeColor) = StatusOf(id);
        if (!string.IsNullOrEmpty(badge))
        {
            var b = new PanelContainer { Position = new Vector2(Card.X - 94, 16), MouseFilter = MouseFilterEnum.Ignore };
            b.AddThemeStyleboxOverride("panel", Box(badgeColor, badgeColor, 10, 3));
            b.AddChild(Lbl(badge, FsCaption, Colors.Black));
            card.AddChild(b);
        }
        return card;
    }

    // 초상을 머리 크기(EmployeeDef.PortraitHeadSpan)가 칸 높이에 꼭 맞게 키우고, PortraitFocus 를 칸 가운데에 둔다.
    // 초상 배경이 투명이라 좌우 여백은 카드 바탕색과 이어진다. 위아래는 빈 띠가 생기지 않게 막는다.
    public static TextureRect FacePortrait(EmployeeDef def, Vector2 box)
    {
        var rect = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var src = def?.FacePortrait ?? def?.StandingImage;
        if (src == null) return rect;
        Vector2 img = src.GetSize();
        if (img.X <= 0 || img.Y <= 0 || box.X <= 0 || box.Y <= 0) return rect;
        float scale = box.Y / (Mathf.Clamp(def.PortraitHeadSpan, 0.3f, 1f) * img.Y);
        Vector2 drawn = img * scale;
        Vector2 at = box * 0.5f - def.PortraitFocus * drawn;
        at.Y = Mathf.Clamp(at.Y, box.Y - drawn.Y, 0f);
        rect.Texture = src;
        rect.Size = drawn;
        rect.Position = at;
        return rect;
    }

    // 사망 / 격리 / 아직 합류 전.
    public static (string Text, Color Color) StatusOf(string id)
    {
        var sim = FacilitySimulation.Instance;
        var st = sim?.GetEmployeeState(id);
        var def = sim?.GetEmployeeDef(id);
        if (st != null && !st.Alive) return ("사망", Red);
        if (st is { Isolated: true }) return ("격리", Amber);
        if (def != null && def.UnlockDay > (GameState.Instance?.CurrentDay ?? 1)) return ("미합류", Dim);
        return ("", Dim);
    }

    private void BuildStaffDetail(string id)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(id);
        var color = Readable(def?.IconColor ?? Cyan);
        _appTitle.Text = $"직원 · {def?.Codename ?? id}";
        float bodyH = Layout.Y - Top;

        // 전신 스탠딩 — 잘리지 않게 세로 맞춤. 글자가 커진 만큼 그림 쪽 여백을 줄여 글 칸을 넓혔다.
        var art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = def?.StandingImage ?? def?.FacePortrait, Position = new Vector2(10, 8), Size = new Vector2(324, bodyH - 16),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _appBody.AddChild(art);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(346, 8), Size = new Vector2(Layout.X - 360, bodyH - 16),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _appBody.AddChild(scroll);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 5);
        scroll.AddChild(col);

        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 14);
        nameRow.AddChild(Lbl(def?.Codename ?? id, 28, color));
        if (!string.IsNullOrEmpty(def?.Gender)) nameRow.AddChild(BottomAligned(Lbl(def.Gender, FsCaption, Dim)));
        var (badge, badgeColor) = StatusOf(id);
        if (!string.IsNullOrEmpty(badge)) nameRow.AddChild(BottomAligned(Lbl($"[{badge}]", FsCaption, badgeColor)));
        col.AddChild(nameRow);

        if (!string.IsNullOrEmpty(def?.ShortProfileLine)) col.AddChild(Wrap(Lbl(def.ShortProfileLine, FsBody, Ink)));

        col.AddChild(Separator());
        col.AddChild(IconRow("❝", "자기소개", Cyan));
        col.AddChild(Wrap(Lbl(string.IsNullOrEmpty(def?.SelfIntroLine) ? "—" : $"“{def.SelfIntroLine}”", FsBody, color)));

        var (good, bad) = Relations(id);
        col.AddChild(Separator());
        col.AddChild(IconRow("●", "사이 좋은 직원", Green));
        col.AddChild(ChipRow(good));
        col.AddChild(IconRow("●", "사이 안 좋은 직원", Red));
        col.AddChild(ChipRow(bad));

        col.AddChild(Separator());
        col.AddChild(IconRow("◆", "TMI", Cyan));
        col.AddChild(Wrap(Lbl($"좋아하는 음식 · {Or(def?.FavoriteFood)}", FsBody, Ink)));
        col.AddChild(Wrap(Lbl($"싫어하는 음식 · {Or(def?.DislikedFood)}", FsBody, Ink)));

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        int clues = ClueBoard.CountFor(id);
        var link = Btn($"이 직원의 단서 {clues}건 보기  ▶", Amber, () => ShowCluesFor(id), FsBody);
        link.CustomMinimumSize = new Vector2(0, 46);
        col.AddChild(link);
    }

    // 큰 이름 옆에 붙는 작은 글씨 — 밑줄을 맞춘다.
    private static Control BottomAligned(Label l)
    {
        l.VerticalAlignment = VerticalAlignment.Bottom;
        l.SizeFlagsVertical = SizeFlags.ExpandFill;
        return l;
    }

    // 관계는 따로 적어 두지 않는다 — RelationshipSystem 의 Band 로 그때그때 판정한다.
    public static (List<string> Good, List<string> Bad) Relations(string id)
    {
        var good = new List<string>();
        var bad = new List<string>();
        foreach (string other in EmployeeOrder())
        {
            if (other == id) continue;
            var band = RelationshipSystem.Band(id, other);
            if (band is PairBand.Close or PairBand.Friendly) good.Add(other);
            else if (band is PairBand.Uneasy or PairBand.Refuse) bad.Add(other);
        }
        return (good, bad);
    }

    private Control ChipRow(List<string> ids)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        if (ids.Count == 0) row.AddChild(Lbl("—", FsBody, Dim));
        foreach (string id in ids) row.AddChild(StaffChip(id));
        return row;
    }

    private Control StaffChip(string id)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(id);
        var c = def?.IconColor ?? Cyan;
        var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", Box(c with { A = 0.22f }, c, 10, 3, 12));
        chip.AddChild(Lbl(def?.Codename ?? id, FsCaption + 2, Readable(c)));
        return chip;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  잠금 화면(거치 중) · 전원
    // ═════════════════════════════════════════════════════════════════════

    private void BuildLock()
    {
        _lock = new Control { Size = Layout, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_lock);
        _lock.AddChild(new ColorRect { Color = new Color(0.01f, 0.025f, 0.03f), Size = Layout, MouseFilter = MouseFilterEnum.Ignore });

        var brand = Lbl("NSP-07  관리자 단말", 18, Cyan with { A = 0.7f });
        brand.Position = new Vector2(0, 90);
        brand.Size = new Vector2(Layout.X, 30);
        brand.HorizontalAlignment = HorizontalAlignment.Center;
        _lock.AddChild(brand);

        _lockTime = Lbl("", 44, Ink);
        _lockTime.Position = new Vector2(0, 150);
        _lockTime.Size = new Vector2(Layout.X, 70);
        _lockTime.HorizontalAlignment = HorizontalAlignment.Center;
        _lock.AddChild(_lockTime);

        // 사고 예고 — 거치 중에도 크게 점멸한다(관리자가 패드를 집어 들 이유).
        _lockAlert = Lbl("", 64, Red);
        _lockAlert.Position = new Vector2(0, 300);
        _lockAlert.Size = new Vector2(Layout.X, 90);
        _lockAlert.HorizontalAlignment = HorizontalAlignment.Center;
        _lock.AddChild(_lockAlert);
        _lockAlertSub = Lbl("", 26, new Color(1f, 0.85f, 0.78f));
        _lockAlertSub.Position = new Vector2(0, 400);
        _lockAlertSub.Size = new Vector2(Layout.X, 44);
        _lockAlertSub.HorizontalAlignment = HorizontalAlignment.Center;
        _lock.AddChild(_lockAlertSub);

        _lockClues = Lbl("", 22, Amber);
        _lockClues.Position = new Vector2(0, Layout.Y - 120);
        _lockClues.Size = new Vector2(Layout.X, 36);
        _lockClues.HorizontalAlignment = HorizontalAlignment.Center;
        _lock.AddChild(_lockClues);
    }

    private void RefreshLock()
    {
        if (_lock == null || !_lock.Visible) return;
        var gs = GameState.Instance;
        _lockTime.Text = gs?.CurrentPhase == GamePhase.Rest ? "휴게시간" : DialogueClock.Text(gs?.DayTimeSeconds ?? 0f);
        var a = CurrentAlert();
        bool on = a != null && Mathf.PosMod(_blink, 1f) < 0.62f;
        _lockAlert.Text = a != null ? "⚠" : "";
        _lockAlert.Visible = on;
        _lockAlertSub.Text = a == null ? "" : $"{a.SubLabel} · {a.Headline}  {a.Countdown}";
        _lockClues.Text = ClueBoard.UnseenCount > 0 ? $"새 단서 {ClueBoard.UnseenCount}건" : "";
    }

    // 정적 노이즈 몇 장(글리치 · NO SIGNAL 썸네일이 함께 쓴다).
    private static ImageTexture[] _noise;
    public static ImageTexture NoiseTexture(int i)
    {
        if (_noise == null)
        {
            var rng = new RandomNumberGenerator { Seed = 0x5EED };
            _noise = new ImageTexture[3];
            for (int n = 0; n < _noise.Length; n++)
            {
                var img = Image.CreateEmpty(160, 100, false, Image.Format.Rgb8);
                for (int y = 0; y < 100; y++)
                for (int x = 0; x < 160; x++)
                {
                    float v = rng.Randf();
                    v = v * v * 0.85f + (y % 3 == 0 ? 0.05f : 0f);
                    img.SetPixel(x, y, new Color(v * 0.8f, v, v));
                }
                _noise[n] = ImageTexture.CreateFromImage(img);
            }
        }
        return _noise[((i % _noise.Length) + _noise.Length) % _noise.Length];
    }

    private void BuildPowerLayer()
    {
        _powerLayer = new Control { Size = Layout, MouseFilter = MouseFilterEnum.Stop, Visible = false };
        AddChild(_powerLayer);
        _powerLayer.AddChild(new ColorRect { Color = Colors.Black, Size = Layout, MouseFilter = MouseFilterEnum.Ignore });
        _powerNoise = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            Texture = NoiseTexture(0), Size = Layout, MouseFilter = MouseFilterEnum.Ignore, Visible = false,
        };
        _powerLayer.AddChild(_powerNoise);

        _powerLogo = new Control { Size = Layout, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        var logo = Lbl("NSP-07", 44, Cyan);
        logo.Size = new Vector2(Layout.X, 64);
        logo.Position = new Vector2(0, Layout.Y * 0.5f - 62);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        _powerLogo.AddChild(logo);
        var sub = Lbl("관리자 단말", FsSection, Dim);
        sub.Size = new Vector2(Layout.X, 30);
        sub.Position = new Vector2(0, Layout.Y * 0.5f + 12);
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        _powerLogo.AddChild(sub);
        _powerBar = new ColorRect { Color = Cyan with { A = 0.6f }, Position = new Vector2(Layout.X * 0.5f - 120, Layout.Y * 0.5f + 64),
                                    Size = new Vector2(0, 4), MouseFilter = MouseFilterEnum.Ignore };
        _powerLogo.AddChild(_powerBar);
        _powerLayer.AddChild(_powerLogo);
    }

    // 전원이 끊겼다 — 글리치 노이즈(0.2초) 뒤 검은 화면. glitch=false 면 바로 검은 화면.
    public void PowerOff(bool glitch)
    {
        ScreenOn = false;
        CloseDetail(instant: true);
        _powerTween?.Kill();
        _powerLayer.Visible = true;
        _powerLayer.Modulate = Colors.White;
        _powerLogo.Visible = false;
        _powerNoise.Visible = glitch;
        if (!glitch || !IsInsideTree()) { _powerNoise.Visible = false; return; }
        _powerTween = CreateTween();
        _powerTween.TweenInterval(0.2);
        _powerTween.TweenCallback(Callable.From(() => _powerNoise.Visible = false));
    }

    // 전원이 돌아왔다 — 검은 화면 → 「NSP-07 관리자 단말」 로고 + 진행 막대(BootSeconds) → 홈(거치 중이면 잠금 화면).
    public void PowerOn()
    {
        ScreenOn = true;
        _powerTween?.Kill();
        _powerLayer.Visible = true;
        _powerLayer.Modulate = Colors.White;
        _powerNoise.Visible = false;
        _powerLogo.Visible = true;
        _powerLogo.Modulate = Colors.White with { A = 0f };
        _powerBar.Size = new Vector2(0, 4);
        if (!IsInsideTree()) { _powerLayer.Visible = false; GoHome(fade: false); return; }
        _powerTween = CreateTween();
        _powerTween.TweenProperty(_powerLogo, "modulate:a", 1f, 0.15);
        _powerTween.Parallel().TweenProperty(_powerBar, "size:x", 240f, BootSeconds - 0.1f);
        _powerTween.TweenInterval(Mathf.Max(0.05f, BootSeconds - 0.3f));
        _powerTween.TweenProperty(_powerLayer, "modulate:a", 0f, 0.15);
        _powerTween.TweenCallback(Callable.From(() =>
        {
            _powerLayer.Visible = false;
            _powerLayer.Modulate = Colors.White;
            GoHome(fade: false);
        }));
    }

    // ═════════════════════════════════════════════════════════════════════
    //  공통
    // ═════════════════════════════════════════════════════════════════════

    // 2행 × 3열 카드 자리.
    private static Vector2 CardPos(int i)
    {
        float total = 3 * Card.X + 2 * CardGap;
        float x0 = (Layout.X - total) * 0.5f;
        return new Vector2(x0 + (i % 3) * (Card.X + CardGap), 16 + (i / 3 % 2) * (Card.Y + 14));
    }

    private Button CardButton(Color accent)
    {
        var card = new Button { Size = Card, CustomMinimumSize = Card, FocusMode = FocusModeEnum.None, ClipContents = true };
        card.AddThemeStyleboxOverride("normal", Box(PanelBg, accent with { A = 0.35f }, 0, 0, 10));
        card.AddThemeStyleboxOverride("hover", Box(PanelBg, accent, 0, 0, 10));
        card.AddThemeStyleboxOverride("pressed", Box(PanelBg, accent, 0, 0, 10));
        card.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return card;
    }

    private void EmptyNote(string text)
    {
        var l = Wrap(Lbl(text, FsBody, Dim));
        l.Position = new Vector2(80, 200);
        l.Size = new Vector2(Layout.X - 160, 140);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        _appBody.AddChild(l);
    }

    // 자료에 나오는 직원(그 사람 · 말한 사람 · 함께 있던 사람).
    private static List<string> StaffOf(InterviewEvidence ev)
    {
        var ids = new List<string>();
        void Add(string id) { if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id); }
        Add(ev?.SubjectEmployeeId);
        Add(ev?.SpeakerEmployeeId);
        if (ev != null) foreach (string id in ev.RelatedEmployeeIds) Add(id);
        return ids;
    }

    private static string PrimaryEmployee(InterviewEvidence ev) => StaffOf(ev).FirstOrDefault() ?? "";

    private static List<string> EmployeeOrder() =>
        FacilitySimulation.Instance?.GetEmployeeIds().ToList() ?? new List<string>();

    private static string WhenText(ClueBoard.Entry entry)
    {
        string t = entry.Evidence.TimeText;
        return string.IsNullOrEmpty(t) ? $"DAY {entry.Day} · 근무 전" : $"DAY {entry.Day} · {t}";
    }

    private static string Codename(string id) => InterviewEvidenceBoard.Codename(id);

    private static string Or(string s) => string.IsNullOrEmpty(s) ? "—" : s;

    private static string Short(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s[..max] + "…";

    // 어두운 고유색(늑대 등)이 어두운 화면에서 안 읽히지 않게 최소 밝기만 끌어올린다.
    private static Color Readable(Color c)
    {
        float lum = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
        const float min = 0.6f;
        return lum >= min ? c : c.Lerp(Colors.White, (min - lum) / Mathf.Max(0.001f, 1f - lum));
    }

    private static void Clear(Node n)
    {
        foreach (Node c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    private static Label Wrap(Label l)
    {
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return l;
    }

    private Label Lbl(string text, int size, Color color)
    {
        var l = new Label { Text = text ?? "", MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", _font ?? ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.S(size));
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private Button Btn(string text, Color accent, Action onPressed, int size)
    {
        var b = MonitorUi.Button(text, accent, _font ?? ViewFont.Default, onPressed, ViewFont.S(size));
        b.FocusMode = FocusModeEnum.None;
        var normal = Box(new Color(0.04f, 0.08f, 0.10f, 0.7f), accent with { A = 0.4f }, 8, 2, 6);
        var hover = Box(accent with { A = 0.18f }, accent, 8, 2, 6);
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.03f, 0.05f, 0.06f, 0.5f), accent with { A = 0.12f }, 8, 2, 6));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        return b;
    }

    private static StyleBoxFlat Box(Color bg, Color border, int padX, int padY, int radius = 0)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg, BorderColor = border,
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY,
        };
        s.SetCornerRadiusAll(radius);
        return s;
    }
}
