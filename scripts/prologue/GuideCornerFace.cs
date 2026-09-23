using Godot;
using NSP.View;

namespace NSP.Prologue;

// DAY0 교육에서 오른쪽 CRT(모니터 2) 구석에 뜨는 작은 GUIDE-0 얼굴창.
//
// 교육 중에도 CCTV 는 계속 보여야 한다. 그래서 화면을 통째로 빼앗지 않고,
// CCTV 화면 안 오른쪽 아래에 작은 창으로만 얹는다.
// (대사 자체는 화면 아래 자막 띠 GuideSubtitleHud 가 맡는다.)
//
// CCTV 뷰포트의 자식으로 붙기 때문에 CCTV 가 화면에 떠 있을 때만 보인다.
public partial class GuideCornerFace : Control
{
    // 화면(CCTV / 휴게 심문)마다 하나씩 붙는다 — 어느 화면이 떠 있든 얼굴이 유지되게.
    private static readonly System.Collections.Generic.List<GuideCornerFace> All = new();

    // 떠 있는 모든 얼굴창을 한꺼번에 켜고 끈다.
    public static void ShowAll(bool shown)
    {
        foreach (var f in All) if (GodotObject.IsInstanceValid(f)) f.SetShown(shown);
    }

    public static void SetPortraitAll(string expression, Texture2D texture, bool mouthless)
    {
        foreach (var f in All) if (GodotObject.IsInstanceValid(f)) f.SetPortrait(expression, texture, mouthless);
    }

    public static void NotifyMouthChangedAll()
    {
        foreach (var f in All) if (GodotObject.IsInstanceValid(f)) f.NotifyMouthChanged();
    }

    // 휴게시간에는 심문 창이 화면 아래를 덮는다 — 그럴 땐 얼굴창을 화면 위쪽으로 옮긴다.
    // (자막 띠를 위로 올리는 것과 같은 이유다. 아래에 두면 통째로 가려진다.)
    public static void SetLifted(bool lifted)
    {
        if (_lifted == lifted) return;
        _lifted = lifted;
        foreach (var f in All) if (GodotObject.IsInstanceValid(f)) f.QueueRedraw();
    }

    private static bool _lifted;

    // 엔딩 — 왼쪽 CRT 를 통째로 쓰는 자리. 구석 창이 아니라 화면 가운데에 크게 뜬다.
    public static void SetEndingWindow(bool on)
    {
        if (_ending == on) return;
        _ending = on;
        foreach (var f in All) if (GodotObject.IsInstanceValid(f)) f.QueueRedraw();
    }

    private static bool _ending;

    // 배드엔딩 — 창 전체가 붉은 신호로 뜬다.
    public static void SetAlarmTint(bool on)
    {
        if (_alarmTint == on) return;
        _alarmTint = on;
        foreach (var f in All) if (GodotObject.IsInstanceValid(f)) f.QueueRedraw();
    }

    private static bool _alarmTint;

    // 마지막으로 켜진 상태 — 화면을 바꿔 끼운 뒤에도 같은 얼굴로 되살린다.
    private static bool _shownAll;
    private static Texture2D _lastTexture;
    private static bool _lastMouthless;

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Alarm = new(1f, 0.42f, 0.34f);

    // 지금 이 창의 신호색.
    private static Color Signal => _alarmTint ? Alarm : Cyan;

    // 창 전체 자리 — CCTV 화면 오른쪽 아래 구석. 창의 오른쪽 아래 모서리가 모니터 화면 모서리에 닿는다.
    private const float WinW = 212f, WinH = 236f;
    private const float MarginX = 0f, MarginY = 0f;
    private const float BarH = 22f;
    // 위로 올렸을 때의 자리(화면 위쪽 정보 줄 아래).
    private const float LiftedY = 46f;
    // 엔딩에서 화면 가운데에 크게 뜨는 창.
    private const float EndW = 360f, EndH = 400f;

    private Font _font;
    private Texture2D _texture;
    private bool _mouthless;
    private bool _shown;
    private float _t;

    public override void _Ready()
    {
        All.Add(this);
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        // 나중에 만들어진 창도 지금 상태를 그대로 물려받는다.
        _texture = _lastTexture;
        _mouthless = _lastMouthless;
        _shown = _shownAll;
        Visible = _shownAll;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        All.Remove(this);
    }

    public void SetPortrait(string expression, Texture2D texture, bool mouthless)
    {
        _lastTexture = texture;
        _lastMouthless = mouthless;
        _texture = texture;
        _mouthless = mouthless;
        if (_shown) QueueRedraw();
    }

    public void SetShown(bool shown)
    {
        _shownAll = shown;
        _shown = shown;
        Visible = shown;
        QueueRedraw();
    }

    // 입 모양이 바뀐 프레임에만 다시 그린다.
    public void NotifyMouthChanged()
    {
        if (_shown) QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!_shown) return;
        _t += (float)delta;
        // 홀로그램 특유의 미세한 흔들림. 매 프레임 다시 그릴 필요는 없다.
        if (Mathf.PosMod(_t, 0.2f) < delta) QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_shown) return;

        Rect2 win;
        float barH = BarH;
        int titleSize = 11;
        if (_ending)
        {
            // 화면 한가운데, 구석 창의 약 1.7배. 엔딩에서는 이 창 말고 볼 것이 없다.
            const float w = EndW, h = EndH;
            win = new Rect2((Canvas.X - w) * 0.5f, (Canvas.Y - h) * 0.5f - 12f, w, h);
            barH = 34f;
            titleSize = 15;
        }
        else
        {
            float winY = _lifted ? LiftedY : Canvas.Y - WinH - MarginY;
            win = new Rect2(Canvas.X - WinW - MarginX, winY, WinW, WinH);
        }
        var signal = Signal;
        DrawRect(win, _alarmTint ? new Color(0.20f, 0.03f, 0.03f, 0.94f) : new Color(0.04f, 0.15f, 0.18f, 0.92f));
        DrawRect(win, signal with { A = 0.85f }, false, _alarmTint ? 2.2f : 1.5f);

        var bar = new Rect2(win.Position.X, win.Position.Y, win.Size.X, barH);
        DrawRect(bar, _alarmTint ? new Color(0.42f, 0.07f, 0.06f, 0.95f) : new Color(0.10f, 0.32f, 0.36f, 0.95f));
        DrawString(_font, bar.Position + new Vector2(10f, barH * 0.72f), "GUIDE-0.exe",
            HorizontalAlignment.Left, win.Size.X - 16f, ViewFont.S(titleSize), signal with { A = 0.95f });

        var face = new Rect2(win.Position.X + 8f, win.Position.Y + barH + 8f,
            win.Size.X - 16f, win.Size.Y - barH - 16f);
        DrawRect(face, _alarmTint ? new Color(0.14f, 0.02f, 0.02f, 0.95f) : new Color(0.02f, 0.10f, 0.13f, 0.95f));

        if (_texture != null)
        {
            var src = _texture.GetSize();
            if (src.X > 0f && src.Y > 0f)
            {
                float k = Mathf.Min(face.Size.X / src.X, face.Size.Y / src.Y);
                var dst = src * k;
                var at = face.Position + (face.Size - dst) * 0.5f;
                GuideFacePaint.Draw(this, _texture, _mouthless, new Rect2(at, dst), signal);
            }
        }
        DrawRect(face, signal with { A = 0.55f }, false, 1f);

        // 스캔라인 — 옆의 CCTV 화면과 질감을 맞춘다.
        for (float y = win.Position.Y + barH; y < win.End.Y; y += 4f)
            DrawRect(new Rect2(win.Position.X, y, win.Size.X, 1f), signal with { A = 0.05f });
    }
}
