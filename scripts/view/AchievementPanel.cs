using Godot;
using NSP.Core;

namespace NSP.View;

// 일시정지(ESC) 창의 「도전과제」 — 타이틀의 기록실과 **같은 화면**을 홀로그램 창에 끼워 띄운다.
//
// 목록을 여기서 다시 그리지 않는다. AchievementArchiveView 사본 하나를 창 안에 넣고
// Embedded 로 두면 바깥 테두리와 [뒤로] 버튼만 빠진 똑같은 화면이 나온다 — 타이틀에서
// 본 것과 글자 하나 다르지 않아야 "같은 기록"이라는 느낌이 유지된다.
//
// 환경 설정 창(SettingsPanel)과 같은 규약: CanvasLayer 130, 일시정지 중에도 입력을 받고,
// ESC 로 닫힌다.
public partial class AchievementPanel : CanvasLayer
{
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color WindowBg = new(0.03f, 0.09f, 0.11f, 0.96f);

    // 휠 한 칸에 굴러가는 거리(논리 px).
    private const float WheelStep = 62f;

    private Control _root;
    private AchievementArchiveView _view;
    private Font _body;

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
        Visible = true;
        _root.Visible = true;
        _view.Open();
    }

    public void Close()
    {
        _view?.Close();
        Visible = false;
    }

    public bool IsOpen => Visible;

    // ── 입력 ────────────────────────────────────────────────────────
    //
    // 창이 떠 있는 동안 키와 휠을 전부 여기서 먹는다. 일시정지 창이 ESC 를 받아
    // 통째로 닫아 버리지 않도록 반드시 SetInputAsHandled 한다.
    public override void _Input(InputEvent e)
    {
        if (!Visible || _view == null) return;

        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            switch (k.Keycode)
            {
                case Key.Up or Key.W:
                    if (_view.MoveCursor(-1)) Sfx.Instance?.Play("tick", -16f);
                    break;
                case Key.Down or Key.S:
                    if (_view.MoveCursor(1)) Sfx.Instance?.Play("tick", -16f);
                    break;
                case Key.Pageup or Key.Home:
                    if (_view.MovePage(-1)) Sfx.Instance?.Play("tick", -13f);
                    break;
                case Key.Pagedown or Key.End:
                    if (_view.MovePage(1)) Sfx.Instance?.Play("tick", -13f);
                    break;
                case Key.Escape or Key.Backspace:
                    Sfx.Instance?.Play("relay_click", -8f);
                    Close();
                    break;
                default:
                    return;   // 모르는 키는 아래로 흘려 보낸다
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        if (e is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                float step = mb.ButtonIndex == MouseButton.WheelUp ? -WheelStep : WheelStep;
                if (_view.ScrollBy(step)) Sfx.Instance?.Play("tick", -22f);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        // 목록 위에 마우스를 올리면 그 줄로 커서가 간다(타이틀 기록실과 같은 동작).
        if (e is InputEventMouseMotion && _view.HoverAt(_view.GetLocalMousePosition()))
            Sfx.Instance?.Play("tick", -22f);
    }

    // ── 화면 ────────────────────────────────────────────────────────

    private void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var scrim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.62f) };
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(scrim);

        // 800×600 짜리 기록실 화면이 그대로 들어갈 크기 + 테두리 여백.
        const float w = 800f, h = 600f, pad = 22f;
        var sheet = new Panel
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -(w * 0.5f + pad), OffsetRight = w * 0.5f + pad,
            OffsetTop = -(h * 0.5f + pad), OffsetBottom = h * 0.5f + pad,
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
        _root.AddChild(sheet);

        _view = new AchievementArchiveView { Embedded = true, Position = new Vector2(pad, pad) };
        sheet.AddChild(_view);

        // 오른쪽 위 닫기(✕) — 환경 설정 창과 같은 모양.
        var close = new Button
        {
            Text = "✕",
            AnchorLeft = 1f, AnchorRight = 1f,
            OffsetLeft = -56f, OffsetRight = -14f, OffsetTop = 12f, OffsetBottom = 54f,
        };
        close.AddThemeFontOverride("font", _body);
        close.AddThemeFontSizeOverride("font_size", ViewFont.FS(22));
        close.AddThemeColorOverride("font_color", Cyan);
        close.AddThemeColorOverride("font_hover_color", Colors.White);
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.20f),
            BorderColor = Cyan with { A = 0.5f },
            BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = Cyan with { A = 0.25f };
        hover.BorderColor = Cyan;
        close.AddThemeStyleboxOverride("normal", normal);
        close.AddThemeStyleboxOverride("hover", hover);
        close.AddThemeStyleboxOverride("pressed", hover);
        close.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        close.Pressed += Close;
        sheet.AddChild(close);
    }
}
