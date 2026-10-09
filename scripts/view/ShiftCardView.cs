using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 스토리가 끝난 직후 짧게 뜨는 카드(지시서 §9).
//
//   DAY 3
//   봉쇄 코어 44%
//   오늘 목표 58%
//   비상 차폐 종료까지 48시간
//
// 목적 하나다 — 휴게실 대화를 보고 나온 플레이어에게 **"지금은 시설을 복구해야 한다"**
// 를 다시 인식시킨다. 긴 안내는 넣지 않는다(§9). 지침은 패드에 이미 있다.
//
// 수치는 전부 지금 돌고 있는 값에서 읽는다. 하드코딩 금지(§9) — 그러면 DAY2 에도
// "DAY 3 / 44%" 가 뜬다.
public partial class ShiftCardView : CanvasLayer
{
    public static ShiftCardView Instance { get; private set; }

    // 화면에 떠 있는 시간. 입력이 들어오면 그보다 먼저 닫힌다(§9 — 짧게).
    private const double HoldSeconds = 2.6;
    private const double FadeSeconds = 0.45;

    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Ink = new(0.88f, 0.94f, 0.96f);
    private static readonly Color Dim = new(0.56f, 0.68f, 0.72f);

    public bool IsShown { get; private set; }

    private Control _root;
    private Label _day;
    private Label _core;
    private Label _goal;
    private Label _left;
    private Tween _tween;
    private bool _dismissed;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        // 눈 감기 오버레이(160)보다 아래, 공포 오버레이(128)보다 위.
        Layer = 150;
        Build();
        SetProcessInput(false);
    }

    private void Build()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.Modulate = new Color(1f, 1f, 1f, 0f);
        _root.Visible = false;
        AddChild(_root);

        // 글자가 배경에 묻히지 않게 아주 얕은 어둠만 깐다. 완전히 덮으면 복귀한
        // 중앙제어실이 안 보여서 "또 컷씬" 으로 읽힌다.
        var dim = new ColorRect
        {
            Color = new Color(0.01f, 0.02f, 0.03f, 0.62f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsPreset(Control.LayoutPreset.Center);
        box.AddThemeConstantOverride("separation", 14);
        box.Position = new Vector2(-300f, -120f);
        box.CustomMinimumSize = new Vector2(600f, 0f);
        _root.AddChild(box);

        _day = Line(box, 74, Cyan);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });
        _core = Line(box, 34, Ink);
        _goal = Line(box, 34, Ink);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });
        _left = Line(box, 27, Dim);
    }

    private static Label Line(Node parent, int size, Color color)
    {
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        l.AddThemeConstantOverride("outline_size", 6);
        parent.AddChild(l);
        return l;
    }

    // ── 표시 내용 ─────────────────────────────────────────────────────

    // 오늘 코어 복구 목표(%). 목표가 적혀 있지 않은 날이면 0.
    public static float GoalPercent(int day)
    {
        var plan = DayObjectives.For(day);
        if (plan == null) return 0f;
        float target = 0f;
        foreach (var obj in plan.Objectives)
            if (obj != null && obj.Type == DayObjectiveType.CoreProgress)
                target = Mathf.Max(target, obj.TargetValue);
        return target;
    }

    // 비상 차폐가 끝나기까지 남은 시간(시). 오늘을 뺀 남은 근무일 × 24.
    // DAY3 · 전체 5일이면 (5-3)*24 = 48시간 — 지시서 §9 의 예와 같다.
    public static int ShieldHoursLeft(int day)
    {
        int max = GameModes.MaxDays;
        return Mathf.Max(0, max - day) * 24;
    }

    // 카드에 적히는 네 줄. 화면을 만들지 않고 문장만 뽑는다 — 검사가 이것을 본다.
    public static string[] Lines(int day)
    {
        float core = GameState.Instance?.CoreProgress ?? 0f;
        float goal = GoalPercent(day);
        int hours = ShieldHoursLeft(day);
        return new[]
        {
            $"DAY {day}",
            $"봉쇄 코어 {Mathf.FloorToInt(core)}%",
            goal > 0f ? $"오늘 목표 {goal:0}%" : "오늘 목표 —",
            hours > 0 ? $"비상 차폐 종료까지 {hours}시간" : "비상 차폐 종료 — 오늘이 마지막 근무",
        };
    }

    // ── 표시 ──────────────────────────────────────────────────────────

    // 카드를 띄우고 닫힐 때까지 기다린다. 어떤 경우에도 반드시 돌아온다.
    public async Task Present()
    {
        int day = GameState.Instance?.CurrentDay ?? 1;
        var text = Lines(day);
        _day.Text = text[0];
        _core.Text = text[1];
        _goal.Text = text[2];
        _left.Text = text[3];

        IsShown = true;
        _dismissed = false;
        _root.Visible = true;
        SetProcessInput(true);
        Sfx.Instance?.Play("sensor_beep", -14f);

        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_root, "modulate:a", 1f, FadeSeconds).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(_tween, Tween.SignalName.Finished);

        // 떠 있는 동안 기다린다. 클릭 · 키 입력이 들어오면 그 자리에서 넘어간다.
        double left = HoldSeconds;
        while (left > 0.0 && !_dismissed && IsInstanceValid(this))
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            left -= GetProcessDeltaTime();
        }
        await Hide();
    }

    public async Task Hide()
    {
        SetProcessInput(false);
        if (!IsInstanceValid(_root)) { IsShown = false; return; }
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_root, "modulate:a", 0f, FadeSeconds).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(_tween, Tween.SignalName.Finished);
        if (IsInstanceValid(_root)) _root.Visible = false;
        IsShown = false;
    }

    // 트윈을 기다리지 않고 즉시 치운다. fail-safe 전용.
    public void HideNow()
    {
        SetProcessInput(false);
        _tween?.Kill();
        _tween = null;
        IsShown = false;
        _dismissed = true;
        if (_root != null && IsInstanceValid(_root))
        {
            _root.Modulate = new Color(1f, 1f, 1f, 0f);
            _root.Visible = false;
        }
    }

    public override void _Input(InputEvent e)
    {
        if (!IsShown) return;
        if (e is InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true, Echo: false })
        {
            _dismissed = true;
            GetViewport().SetInputAsHandled();
        }
    }
}
