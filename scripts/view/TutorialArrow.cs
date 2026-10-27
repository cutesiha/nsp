using System;
using Godot;

namespace NSP.View;

// 교육 안내 화살표 — "여기를 누르십시오" 를 말 대신 가리킨다.
//
// 밝은 하늘색 화살 하나가 가리키는 쪽으로 둥둥 다가갔다 물러난다. 바깥으로 빛이 번진다
// (같은 모양을 굵게 · 옅게 몇 겹 깔고 그 위에 심을 긋는다).
//
// 화살은 **모니터 밖으로도 나갈 수 있어야 한다** — 배치 화면의 대기 인원 카드는 화면 오른쪽
// 끝에 붙어 있어, 그 옆을 가리키려면 화면 테두리 너머에 화살이 서야 한다. 그래서 모니터 안
// UI 에 그리지 않고 제어실 화면 위(CanvasLayer)에 그린다. 모니터 안의 한 칸은 매 프레임
// "그 UI 의 좌표 → 3D 화면 판 → 카메라" 로 옮겨 잡는다. 화면을 확대하거나 숨을 쉬어
// 카메라가 움직여도 화살이 그 칸을 놓치지 않는다.
//
// 주 화살은 하나다(PointAt). 가리킬 자리는 함수로 받는다 — 상황에 따라 가리킬 곳이 바뀌는
// 교육 단계(토끼 카드 → 집어 들면 코어실)를 그 함수 하나로 표현할 수 있다.
//
// 곁 화살(PointAlso)은 이름을 붙여 몇 개든 같이 띄운다 — "작업실 옆 + 옮길 직원 옆" 처럼
// 한 지시가 두 곳 이상을 가리켜야 할 때 쓴다. 함수가 null 을 돌려주면 그 화살만 숨는다.
public partial class TutorialArrow : CanvasLayer
{
    public static TutorialArrow Instance { get; private set; }

    // 화살촉 끝(화면 좌표)과 화살이 향하는 방향(단위 벡터).
    public readonly record struct Aim(Vector2 Tip, Vector2 Dir);

    public enum Side { Right, Left, Above, Below }

    // 1080 이 아니라 이 프로젝트의 기준 창 높이(project.godot viewport_height).
    private const float BaseHeight = 880f;

    private static readonly Color Core = new(0.80f, 0.98f, 1f);
    private static readonly Color Glow = new(0.42f, 0.90f, 1f);

    // 화살 하나의 상태. 함수를 떼어도 다 사라질 때까지(알파 0) 남아 있다가 지운다.
    private sealed class Slot
    {
        public Func<Aim?> Resolve;
        public Aim? Aim;
        public float Alpha;
    }

    private readonly Slot _main = new();
    private readonly System.Collections.Generic.Dictionary<string, Slot> _extras = new();
    private float _t;
    private ArrowCanvas _canvas;

    public static bool IsShown => Instance?._main.Resolve != null;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        // 대사창(112) · 통화창(114) 위, 아날로그 필터(119) 아래 — 필터를 같이 맞아 장면에 녹는다.
        Layer = 117;
        _canvas = new ArrowCanvas { Owner2 = this, MouseFilter = Control.MouseFilterEnum.Ignore };
        _canvas.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_canvas);
        SetProcess(true);
    }

    // 가리킬 자리를 바꾼다. 함수가 null 을 돌려주는 동안은 숨는다(가리킬 칸이 화면에 없을 때).
    public static void PointAt(Func<Aim?> resolve)
    {
        if (Instance == null) return;
        if (Instance._main.Resolve == null && Instance._extras.Count == 0) Instance._t = 0f;
        Instance._main.Resolve = resolve;
    }

    // 주 화살과 곁 화살을 전부 걷는다.
    public static void Clear()
    {
        if (Instance == null) return;
        Instance._main.Resolve = null;
        foreach (var s in Instance._extras.Values) s.Resolve = null;
    }

    // 이름 붙은 곁 화살을 띄우거나(같은 이름이면 가리킬 자리만 바꾼다) 걷는다.
    public static void PointAlso(string key, Func<Aim?> resolve)
    {
        if (Instance == null || string.IsNullOrEmpty(key)) return;
        if (!Instance._extras.TryGetValue(key, out var slot))
            Instance._extras[key] = slot = new Slot();
        slot.Resolve = resolve;
    }

    public static void ClearAlso(string key)
    {
        if (Instance == null || string.IsNullOrEmpty(key)) return;
        if (Instance._extras.TryGetValue(key, out var slot)) slot.Resolve = null;
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _t += d;
        Step(_main, d);
        System.Collections.Generic.List<string> gone = null;
        foreach (var (key, slot) in _extras)
        {
            Step(slot, d);
            if (slot.Resolve == null && slot.Alpha <= 0f) (gone ??= new()).Add(key);
        }
        if (gone != null) foreach (var key in gone) _extras.Remove(key);
        _canvas.QueueRedraw();
    }

    private static void Step(Slot slot, float d)
    {
        Aim? aim = null;
        try { aim = slot.Resolve?.Invoke(); }
        catch (Exception) { aim = null; }   // 가리키던 노드가 사라진 프레임 — 그냥 숨는다
        if (aim != null) slot.Aim = aim;
        float want = aim != null ? 1f : 0f;
        slot.Alpha = Mathf.MoveToward(slot.Alpha, want, d * (want > slot.Alpha ? 5f : 7f));
    }

    // ── 자리 잡기 ─────────────────────────────────────────────────────

    // 모니터 화면 안의 UI(view) 한 칸(rect · view 로컬 좌표) 옆에서 그 칸을 가리킨다.
    // side = 화살이 서는 쪽(Right = 칸 오른쪽에 서서 왼쪽을 가리킨다).
    public static Aim? BesideOnMonitor(Control view, Rect2 rect, Side side = Side.Right, float gapPx = 10f)
    {
        if (view == null || !GodotObject.IsInstanceValid(view) || !view.IsVisibleInTree()) return null;
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f) return null;
        var screen = ScreenShowing(view);
        if (screen == null) return null;

        Vector2 c = rect.GetCenter();
        (Vector2 edge, Vector2 outward) = side switch
        {
            Side.Left => (new Vector2(rect.Position.X, c.Y), Vector2.Left),
            Side.Above => (new Vector2(c.X, rect.Position.Y), Vector2.Up),
            Side.Below => (new Vector2(c.X, rect.End.Y), Vector2.Down),
            _ => (new Vector2(rect.End.X, c.Y), Vector2.Right),
        };
        var p = MonitorToScreen(screen, view, edge);
        var q = MonitorToScreen(screen, view, edge + outward * 24f);
        if (p == null || q == null) return null;
        Vector2 outScreen = (q.Value - p.Value);
        if (outScreen.LengthSquared() < 0.0001f) return null;
        outScreen = outScreen.Normalized();
        return new Aim(p.Value + outScreen * gapPx * PxScale, -outScreen);
    }

    // 3D 공간의 한 점 바로 위(또는 옆)에서 그 점을 가리킨다 — 책상 위 전화기 등.
    public static Aim? AtWorld(Vector3 world, Side side = Side.Above, float gapPx = 8f)
    {
        var vp = Instance?.GetViewport();
        var cam = vp?.GetCamera3D();
        if (cam == null || cam.IsPositionBehind(world)) return null;
        Vector2 p = cam.UnprojectPosition(world);
        Vector2 outward = side switch
        {
            Side.Below => Vector2.Down,
            Side.Left => Vector2.Left,
            Side.Right => Vector2.Right,
            _ => Vector2.Up,
        };
        return new Aim(p + outward * gapPx * PxScale, -outward);
    }

    // 화면 위에 바로 떠 있는 UI(CanvasLayer — 통화창 선택지 등) 한 칸 옆에서 가리킨다.
    public static Aim? BesideOnScreen(Control view, Rect2 rect, Side side = Side.Right, float gapPx = 8f)
    {
        if (view == null || !GodotObject.IsInstanceValid(view) || !view.IsVisibleInTree()) return null;
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f) return null;
        Vector2 c = rect.GetCenter();
        (Vector2 edge, Vector2 outward) = side switch
        {
            Side.Left => (new Vector2(rect.Position.X, c.Y), Vector2.Left),
            Side.Above => (new Vector2(c.X, rect.Position.Y), Vector2.Up),
            Side.Below => (new Vector2(c.X, rect.End.Y), Vector2.Down),
            _ => (new Vector2(rect.End.X, c.Y), Vector2.Right),
        };
        var xf = view.GetGlobalTransformWithCanvas();
        Vector2 p = xf * edge;
        Vector2 outScreen = xf.BasisXform(outward);
        if (outScreen.LengthSquared() < 0.0001f) return null;
        outScreen = outScreen.Normalized();
        return new Aim(p + outScreen * gapPx * PxScale, -outScreen);
    }

    // 그 UI 를 지금 띄우고 있는 3D 모니터. 어느 모니터에도 걸려 있지 않으면 null.
    private static MonitorScreen3D ScreenShowing(Control view)
    {
        var vp = view.GetViewport() as SubViewport;
        var ctl = ControlRoom3DController.Instance;
        if (vp == null || ctl == null) return null;
        foreach (var s in ctl.Screens)
            if (s.TargetViewport == vp && s.IsVisibleInTree()) return s;
        return null;
    }

    // 모니터 안 UI 좌표 → 제어실 화면 좌표. MonitorScreen3D.TryProjectRay 의 역방향이다
    // (화면 판은 로컬 -0.5~0.5 의 사각형이고, 뷰포트 픽셀이 그 위에 그대로 깔린다).
    private static Vector2? MonitorToScreen(MonitorScreen3D screen, Control view, Vector2 local)
    {
        var vp = view.GetViewport() as SubViewport;
        var cam = Instance?.GetViewport()?.GetCamera3D();
        if (vp == null || cam == null) return null;
        Vector2 size = vp.Size;
        if (size.X < 4f || size.Y < 4f) return null;   // 쓰지 않아 렌더 타깃을 놓아 둔 화면
        Vector2 px = view.GetGlobalTransformWithCanvas() * local;
        float u = px.X / size.X, v = px.Y / size.Y;
        var onQuad = new Vector3(u - 0.5f, screen.FlipV ? v - 0.5f : 0.5f - v, 0f);
        Vector3 world = screen.GlobalTransform * onQuad;
        if (cam.IsPositionBehind(world)) return null;
        return cam.UnprojectPosition(world);
    }

    private static float PxScale
    {
        get
        {
            float h = Instance?.GetViewport()?.GetVisibleRect().Size.Y ?? BaseHeight;
            return Mathf.Max(0.5f, h / BaseHeight);
        }
    }

    // ── 그리기 ─────────────────────────────────────────────────────────

    private void DrawArrows(CanvasItem ci)
    {
        DrawArrow(ci, _main);
        foreach (var slot in _extras.Values) DrawArrow(ci, slot);
    }

    private void DrawArrow(CanvasItem ci, Slot slot)
    {
        float alpha = slot.Alpha;
        if (alpha <= 0.001f || slot.Aim == null) return;
        var aim = slot.Aim.Value;
        float s = PxScale;
        Vector2 dir = aim.Dir.LengthSquared() > 0.0001f ? aim.Dir.Normalized() : Vector2.Left;
        Vector2 n = new(-dir.Y, dir.X);

        // 가리키는 쪽으로 다가갔다 물러난다(둥둥).
        float bob = (0.5f - 0.5f * Mathf.Cos(_t * 4.6f)) * 9f * s;
        Vector2 tip = aim.Tip - dir * bob;
        float len = 54f * s, head = 19f * s, width = 6.5f * s;
        Vector2 tail = tip - dir * len;
        Vector2 h1 = tip - dir * head + n * head * 0.88f;
        Vector2 h2 = tip - dir * head - n * head * 0.88f;

        // 바깥 빛 — 굵고 옅은 것부터 깔고 점점 가늘고 진하게.
        (float W, float A)[] glow = { (20f, 0.07f), (14f, 0.12f), (9f, 0.20f), (4.5f, 0.34f) };
        foreach (var (w, a) in glow)
            Stroke(ci, tail, tip, h1, h2, width + w * s, Glow with { A = a * alpha });
        Stroke(ci, tail, tip, h1, h2, width, Core with { A = alpha });
    }

    // 화살 한 벌(몸통 + 촉 두 획)을 둥근 끝으로 긋는다.
    private static void Stroke(CanvasItem ci, Vector2 tail, Vector2 tip, Vector2 h1, Vector2 h2, float w, Color c)
    {
        ci.DrawLine(tail, tip, c, w, true);
        ci.DrawLine(h1, tip, c, w, true);
        ci.DrawLine(h2, tip, c, w, true);
        float r = w * 0.5f;
        ci.DrawCircle(tail, r, c);
        ci.DrawCircle(h1, r, c);
        ci.DrawCircle(h2, r, c);
        ci.DrawCircle(tip, r, c);
    }

    private partial class ArrowCanvas : Control
    {
        public TutorialArrow Owner2;
        public override void _Draw() => Owner2?.DrawArrows(this);
    }
}
