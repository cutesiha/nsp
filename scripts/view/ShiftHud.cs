using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 근무 중(GamePhase.Live)에만 화면 위에 얹히는 최소 HUD.
//   · 위 가운데 : DAY 01 (KMU80 명조)
//   · 왼쪽 위   : 톱니바퀴 — 누르면 ESC 와 같은 일시정지 메뉴가 열린다.
// 근무 배치 / 시작 화면에서는 숨긴다.
public partial class ShiftHud : CanvasLayer
{
    private Label _day;
    private Button _gear;
    private Control _root;

    public override void _Ready()
    {
        Layer = 70;                       // 통화 HUD(90)·일시정지(120)보다 아래
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
    }

    private void BuildUI()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var serif = GD.Load<Font>("res://assets/fonts/KMU80TTFSungkokSerif.ttf") ?? ViewFont.Default;

        _day = new Label
        {
            Text = "DAY 01",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _day.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _day.OffsetTop = 22;
        _day.AddThemeFontOverride("font", serif);
        _day.AddThemeFontSizeOverride("font_size", ViewFont.FS(38));
        _day.AddThemeColorOverride("font_color", new Color(0.88f, 0.86f, 0.80f, 0.92f));
        _day.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _day.AddThemeConstantOverride("outline_size", 7);
        _root.AddChild(_day);

        // 톱니바퀴 — ESC 메뉴를 여는 버튼.
        _gear = new Button
        {
            Flat = true,
            TooltipText = "메뉴 (ESC)",
            Position = new Vector2(24, 20),
            Size = new Vector2(52, 52),
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

    // 톱니바퀴 아이콘 — 이미지 에셋 없이 직접 그린다(이 게임 톤에 맞는 낡은 금속 느낌).
    private partial class GearIcon : Control
    {
        public bool Hover;

        public override void _Draw()
        {
            var c = Hover ? new Color(1f, 0.94f, 0.78f, 0.98f) : new Color(0.82f, 0.83f, 0.85f, 0.78f);
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
