using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 근무 중(GamePhase.Live)에만 화면 위에 얹히는 최소 HUD.
//   · 왼쪽 위 구석 : DAY 01 (KMU80 명조)
//   · 위 가운데    : 봉쇄 코어 복구율 게이지 (DAY1~DAY5 누적, 100% 가 최대)
//   · 오른쪽 위 구석: 톱니바퀴 — 누르면 ESC 와 같은 일시정지 메뉴가 열린다.
// 근무 배치 / 시작 화면에서는 숨긴다.
public partial class ShiftHud : CanvasLayer
{
    private const float GaugeWidth = 560f;
    private const float GaugeHeight = 34f;

    private Label _day;
    private Button _gear;
    private Control _root;
    private CoreGauge _gauge;

    public override void _Ready()
    {
        // 화면 가장자리 암전(AmbientOverlay=100) 위에 와야 DAY 표시와 톱니바퀴가 가려지지 않는다.
        // 기록창(115)·일시정지(120)·설정(130)은 이 위에 뜬다.
        Layer = 110;
        ProcessMode = ProcessModeEnum.Always;
        BuildUI();
        Visible = false;
    }

    public override void _Process(double delta)
    {
        // 근무 중에만 보인다(배치·정산·휴게 화면에서는 감춘다).
        bool live = GameState.Instance?.CurrentPhase == GamePhase.Live;
        if (Visible != live) Visible = live;
        if (!live) return;

        int day = GameState.Instance?.CurrentDay ?? 1;
        string text = $"DAY {day:00}";
        if (_day.Text != text) _day.Text = text;

        _gauge?.SetProgress(GameState.Instance?.CoreProgress ?? 0f);
    }

    private void BuildUI()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var serif = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf") ?? ViewFont.Default;

        // ── 왼쪽 위 구석 : 날짜 ──
        _day = new Label
        {
            Text = "DAY 01",
            Position = new Vector2(26, 14),
            Size = new Vector2(240, 48),
            HorizontalAlignment = HorizontalAlignment.Left,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _day.AddThemeFontOverride("font", serif);
        _day.AddThemeFontSizeOverride("font_size", ViewFont.FS(34));
        _day.AddThemeColorOverride("font_color", new Color(0.88f, 0.86f, 0.80f, 0.92f));
        _day.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _day.AddThemeConstantOverride("outline_size", 7);
        _root.AddChild(_day);

        // ── 위 가운데 : 코어 복구율 게이지 ──
        _gauge = new CoreGauge
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            OffsetLeft = -GaugeWidth / 2f, OffsetRight = GaugeWidth / 2f,
            OffsetTop = 18f, OffsetBottom = 18f + GaugeHeight,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _root.AddChild(_gauge);

        // ── 오른쪽 위 구석 : 톱니바퀴(ESC 메뉴) ──
        _gear = new Button
        {
            Flat = true,
            TooltipText = "메뉴 (ESC)",
            AnchorLeft = 1f, AnchorRight = 1f,
            OffsetLeft = -76f, OffsetRight = -24f, OffsetTop = 18f, OffsetBottom = 70f,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _gear.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _gear.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        _gear.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        _gear.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _gear.Pressed += () => PauseMenu.Instance?.Open();
        _root.AddChild(_gear);

        var icon = new GearIcon { MouseFilter = Control.MouseFilterEnum.Ignore };
        icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _gear.AddChild(icon);
        _gear.MouseEntered += () => { icon.Hover = true; icon.QueueRedraw(); };
        _gear.MouseExited += () => { icon.Hover = false; icon.QueueRedraw(); };
    }

    // 봉쇄 코어 복구율 게이지. DAY1~DAY5 내내 이어지는 누적 진행도라 근무마다 초기화하지 않는다.
    // 글자는 두 번 그린다 — 빈 구간(회색) 위에는 흰색, 채워진 구간(하늘색) 위에는 검정색.
    // 채워진 폭만큼만 잘라 보여주는 방식이라 게이지가 차오를수록 글자 색이 자연스럽게 넘어간다.
    private partial class CoreGauge : Control
    {
        private static readonly Color Empty = new(0.24f, 0.26f, 0.28f, 0.88f);
        private static readonly Color Fill = new(0.42f, 0.82f, 0.96f);
        private static readonly Color Border = new(0.72f, 0.78f, 0.82f, 0.65f);

        private ColorRect _fill;
        private Control _fillClip, _darkClip;
        private Label _light, _dark;
        private float _progress = -1f;

        public override void _Ready()
        {
            var bg = new ColorRect { Color = Empty, MouseFilter = MouseFilterEnum.Ignore };
            bg.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(bg);

            _fillClip = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_fillClip);
            _fill = new ColorRect { Color = Fill, MouseFilter = MouseFilterEnum.Ignore };
            _fillClip.AddChild(_fill);

            _light = MakeLabel(new Color(0.96f, 0.98f, 1f));
            AddChild(_light);

            _darkClip = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(_darkClip);
            _dark = MakeLabel(new Color(0.04f, 0.08f, 0.12f));
            _darkClip.AddChild(_dark);

            SetProgress(0f);
        }

        private Label MakeLabel(Color color)
        {
            var l = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            l.AddThemeFontOverride("font", ViewFont.Default);
            l.AddThemeFontSizeOverride("font_size", ViewFont.FS(19));
            l.AddThemeColorOverride("font_color", color);
            return l;
        }

        public void SetProgress(float percent)
        {
            float p = Mathf.Clamp(percent, 0f, 100f);
            if (Mathf.IsEqualApprox(p, _progress)) return;
            _progress = p;

            string text = $"코어 복구율 {p:0}%";
            _light.Text = text;
            _dark.Text = text;
            Layout();
            QueueRedraw();
        }

        public override void _Notification(int what)
        {
            if (what == NotificationResized) Layout();
        }

        private void Layout()
        {
            if (_fill == null) return;
            float w = Size.X, h = Size.Y;
            float filled = w * Mathf.Clamp(_progress, 0f, 100f) / 100f;

            _fillClip.Position = Vector2.Zero;
            _fillClip.Size = new Vector2(filled, h);
            _fill.Position = Vector2.Zero;
            _fill.Size = new Vector2(w, h);

            _light.Position = Vector2.Zero;
            _light.Size = new Vector2(w, h);

            // 어두운 글자는 채워진 폭만큼만 보인다 — 안쪽 라벨은 항상 전체 폭이라 글자 위치가 같다.
            _darkClip.Position = Vector2.Zero;
            _darkClip.Size = new Vector2(filled, h);
            _dark.Position = Vector2.Zero;
            _dark.Size = new Vector2(w, h);
        }

        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), Border, false, 1.6f);
        }
    }

    // 톱니바퀴 아이콘 — 이미지 에셋 없이 직접 그린다(이 게임 톤에 맞는 낡은 금속 느낌).
    private partial class GearIcon : Control
    {
        public bool Hover;

        public override void _Draw()
        {
            // 오른쪽 위 구석에서도 눈에 띄도록 평상시 색을 밝은 회색으로 올린다.
            var c = Hover ? new Color(1f, 0.97f, 0.86f, 1f) : new Color(0.93f, 0.94f, 0.95f, 0.95f);
            var shadow = new Color(0f, 0f, 0f, 0.55f);
            Vector2 mid = Size * 0.5f;
            float r = Mathf.Min(Size.X, Size.Y) * 0.30f;

            void Gear(Vector2 center, Color col, float width)
            {
                // 톱니 8개 — 바깥 링에서 짧은 선을 방사형으로 뻗는다.
                for (int i = 0; i < 8; i++)
                {
                    float a = Mathf.Tau * i / 8f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    DrawLine(center + dir * (r * 0.92f), center + dir * (r * 1.42f), col, width);
                }
                DrawArc(center, r, 0f, Mathf.Tau, 28, col, width);
                DrawArc(center, r * 0.42f, 0f, Mathf.Tau, 18, col, width);
            }

            Gear(mid + new Vector2(1.5f, 1.5f), shadow, 4.2f);
            Gear(mid, c, 3.4f);
        }
    }
}
