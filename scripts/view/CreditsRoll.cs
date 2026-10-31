using System.Threading.Tasks;
using Godot;
using NSP.Core;

namespace NSP.View;

// 엔딩 크레딧 — 영화처럼 아래에서 위로 올라간다(2026-10-31 사용자 요청). 네 엔딩 모두 끝나면 뜬다.
//
//   제작 백시하(크게) → 맡은 일 → QA · Special Thanks → 외부 리소스(이름만) → 폰트 → 엔진
//   → 마지막 줄 「플레이해 주셔서 감사합니다.」 는 화면 한가운데에 멈췄다가 사라진다.
//
// 내용은 GameCredits 한 곳에서 읽는다. 아무 키나 누르고 있으면 빨리 감기, ESC 는 건너뛰기.
public partial class CreditsRoll : Control
{
    private const float ScrollPxPerSec = 78f;   // 1920×1080 기준 — 한 줄을 편히 읽는 속도
    private const float FastScale = 5f;         // 누르고 있으면
    private const double HoldLastSeconds = 3.0;

    private static readonly Color Ink = new(0.90f, 0.93f, 0.93f);
    private static readonly Color Dim = new(0.52f, 0.60f, 0.62f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);

    private VBoxContainer _box;
    private Label _last;
    private Label _hint;
    private bool _fast;
    private bool _skip;

    public async Task PlayAsync()
    {
        // 이미 트리에 붙은 뒤라 SetAnchorsPreset 만으로는 크기가 0 으로 남는다 — 여백까지 같이 맞춘다.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ProcessMode = ProcessModeEnum.Always;
        Modulate = new Color(1, 1, 1, 0);

        var bg = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        _box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _box.AddThemeConstantOverride("separation", 10);
        AddChild(_box);
        Build();

        _hint = Text("아무 키나 누르고 있으면 빨리 넘어갑니다   /   ESC 건너뛰기", 13, Dim with { A = 0.7f });
        _hint.HorizontalAlignment = HorizontalAlignment.Right;
        AddChild(_hint);

        // 배치가 잡힐 때까지 한 프레임 — 전체 높이를 알아야 마지막 줄을 가운데 세울 수 있다.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        float h = Size.Y, w = Size.X;
        _box.Size = new Vector2(w, _box.GetCombinedMinimumSize().Y);
        _box.Position = new Vector2(0f, h);
        _hint.Size = new Vector2(800f, 30f);
        _hint.Position = new Vector2(w - 840f, h - 56f);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        Sfx.Instance?.CrossfadeMusic("nsp_mainbgm2", 2.0f, loop: true);
        var fadeIn = CreateTween();
        fadeIn.TweenProperty(this, "modulate:a", 1f, 1.2).SetTrans(Tween.TransitionType.Sine);

        // 마지막 줄의 가운데가 화면 가운데에 오는 자리까지 올린다.
        float stopY = h * 0.5f - (_last.Position.Y + _last.Size.Y * 0.5f);
        while (_box.Position.Y > stopY && !_skip && IsInstanceValid(this))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            float k = ControlRoom3DController.UiScale;
            float step = ScrollPxPerSec * k * (float)GetProcessDeltaTime() * (_fast ? FastScale : 1f);
            _box.Position = new Vector2(0f, Mathf.Max(stopY, _box.Position.Y - step));
        }
        if (!IsInstanceValid(this)) return;
        if (!_skip)
        {
            _box.Position = new Vector2(0f, stopY);
            double left = HoldLastSeconds;
            while (left > 0 && !_skip && IsInstanceValid(this))
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                left -= GetProcessDeltaTime();
            }
        }
        Sfx.Instance?.FadeOutMusic(1.6f);
        var fadeOut = CreateTween();
        fadeOut.TweenProperty(this, "modulate:a", 0f, 1.0).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(fadeOut, Tween.SignalName.Finished);
        QueueFree();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey k)
        {
            if (k.Pressed && !k.Echo && k.Keycode == Key.Escape) _skip = true;
            else _fast = k.Pressed;
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _fast = mb.Pressed;
            GetViewport().SetInputAsHandled();
        }
    }

    // ── 내용 ─────────────────────────────────────────────────────────

    private void Build()
    {
        Gap(80f);
        _box.AddChild(Text(GameCredits.Title, 64, Mint, ViewFont.DayTitle));
        _box.AddChild(Text(GameCredits.TitleEn, 18, Dim));
        Gap(160f);

        _box.AddChild(Text("제작", 22, Dim));
        _box.AddChild(Text(GameCredits.Maker, 72, Ink, ViewFont.DayTitle));
        Gap(110f);

        foreach (var (role, name) in GameCredits.Roles) Pair(role, name);
        Gap(130f);

        _box.AddChild(Text(GameCredits.ThanksHead, 24, Mint));
        Gap(8f);
        foreach (string n in GameCredits.Thanks) _box.AddChild(Text(n, 30, Ink));
        Gap(130f);

        _box.AddChild(Text("외부 리소스", 24, Mint));
        Gap(8f);
        string cat = "";
        foreach (var s in GameCredits.Sources)
        {
            if (s.Category != cat)
            {
                if (cat.Length > 0) Gap(22f);
                cat = s.Category;
                _box.AddChild(Text(cat, 17, Dim));
            }
            // 이름만 — 링크는 타이틀의 출처 창에 있다.
            _box.AddChild(Text($"{s.Use}: {s.Work} - {s.Author}", 21, Ink));
        }
        Gap(70f);
        _box.AddChild(Text("폰트", 17, Dim));
        foreach (string f in GameCredits.Fonts) _box.AddChild(Text(f, 21, Ink));
        Gap(90f);
        _box.AddChild(Text(GameCredits.Engine, 18, Dim));
        Gap(240f);

        _last = Text(GameCredits.ThankYou, 34, Ink);
        _box.AddChild(_last);
    }

    // 영화 크레딧처럼 — 왼쪽 칸은 맡은 일(오른쪽 정렬), 오른쪽 칸은 이름(왼쪽 정렬). 가운데 줄에서 만난다.
    private void Pair(string role, string name)
    {
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 48);
        var l = Text(role, 24, Dim);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        l.CustomMinimumSize = new Vector2(420f, 0f);
        var r = Text(name, 28, Ink);
        r.HorizontalAlignment = HorizontalAlignment.Left;
        r.CustomMinimumSize = new Vector2(420f, 0f);
        row.AddChild(l);
        row.AddChild(r);
        _box.AddChild(row);
    }

    private void Gap(float px) =>
        _box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, px), MouseFilter = MouseFilterEnum.Ignore });

    private static Label Text(string text, int size, Color color, Font font = null)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontOverride("font", font ?? ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
