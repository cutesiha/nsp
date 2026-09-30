using Godot;

namespace NSP.View;

// '존재' 를 화면에 그리는 층. 판단은 PresenceDirector 가 하고 여기는 그리기만 한다.
//
// 3D 로 실루엣을 세우지 않는다 — 어디에 서 있느냐가 아니라 "뭔가 비쳤다" 가 전부이고,
// 낮은 알파의 어두운 형체를 잠깐 얹는 것으로 충분하다(설계 문서 16절 B).
// 다만 자리는 화면에 고정하지 않고 실제 3D 물체(모니터 · 문창)를 투영해서 잡는다.
// 시점이 조금 움직여도 형체가 엉뚱한 허공에 뜨지 않게.
public partial class PresenceOverlay : CanvasLayer
{
    private enum Mode { None, Reflection, DoorWindow, LightReturn }

    private Painter _paint;
    private Mode _mode;
    private float _t, _dur;

    public override void _Ready()
    {
        // 분위기 오버레이(100)보다 위, 대사창(112)보다 아래 — UI 를 가리지 않는다.
        Layer = 105;
        _paint = new Painter();
        _paint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _paint.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_paint);
        Visible = false;
        SetProcess(true);
    }

    public void ShowReflection() => Begin(Mode.Reflection, 0.35f);
    public void ShowDoorWindow() => Begin(Mode.DoorWindow, 0.60f);
    // 조명이 돌아오는 한두 프레임 — 아주 짧다.
    public void ShowLightReturn() => Begin(Mode.LightReturn, 0.05f);

    private void Begin(Mode m, float seconds)
    {
        _mode = m;
        _dur = seconds;
        _t = 0f;
        Visible = true;
        _paint.QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += (float)delta;
        if (_t >= _dur) { Visible = false; _mode = Mode.None; return; }
        _paint.Mode = (int)_mode;
        _paint.Progress = _t / _dur;
        _paint.Rect = RegionFor(_mode);
        _paint.QueueRedraw();
    }

    // 연출마다 그려질 화면 영역. 실제 3D 물체를 투영해서 잡고, 못 잡으면 화면 비율로 떨어진다.
    private Rect2 RegionFor(Mode m)
    {
        var vp = GetViewport();
        Vector2 size = vp?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        var cam = vp?.GetCamera3D();

        switch (m)
        {
            // 오른쪽 모니터 표면 — 그 위에 뒤에 선 무언가가 비친다.
            case Mode.Reflection:
                return Project(cam, FindNode3D("ControlRoom/Monitor02/M02_Screen"),
                    new Vector2(0.66f, 0.30f), new Vector2(0.14f, 0.30f), size);

            // 뒤쪽 출입문의 작은 창. 앉은 자리에서 문창이 모니터에 가려 보이지 않으면
            // 모니터 사이로 보이는 뒷벽을 쓴다 — 가려진 곳에 그림자를 그리면 허공에 뜬 것처럼 보인다.
            case Mode.DoorWindow:
                return Project(cam, FindNode3D("ControlRoom/Door/Door_Window"),
                    new Vector2(0.45f, 0.24f), new Vector2(0.09f, 0.22f), size);

            // 모니터 사이의 빈 공간.
            default:
                return new Rect2(size.X * 0.45f, size.Y * 0.26f, size.X * 0.12f, size.Y * 0.34f);
        }
    }

    // 3D 노드를 화면 사각형으로. 못 찾거나 뒤에 있으면 화면 비율로 잡은 자리를 쓴다.
    private Rect2 Project(Camera3D cam, Node3D at, Vector2 fallbackPos, Vector2 fallbackSize, Vector2 screen)
    {
        var fb = new Rect2(screen * fallbackPos, screen * fallbackSize);
        if (cam == null || at == null) return fb;
        var p = at.GlobalPosition;
        if (cam.IsPositionBehind(p)) return fb;
        Vector2 c = cam.UnprojectPosition(p);
        // 물체의 크기를 화면 크기로 환산한다(가로 0.4m 기준).
        Vector2 edge = cam.UnprojectPosition(p + cam.GlobalTransform.Basis.X * 0.2f);
        float half = Mathf.Max(18f, Mathf.Abs(edge.X - c.X));
        return new Rect2(c.X - half, c.Y - half * 1.5f, half * 2f, half * 3f);
    }

    // 현재 씬에서 찾고, 없으면 트리 전체에서 이름으로 찾는다
    // (검사 씬이 메인 씬을 자식으로 안고 도는 경우가 있다).
    private Node3D FindNode3D(string path)
    {
        var tree = GetTree();
        var hit = tree?.CurrentScene?.GetNodeOrNull<Node3D>(path);
        if (hit != null) return hit;
        foreach (var n in tree?.Root?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            hit = n.GetNodeOrNull<Node3D>(path);
            if (hit != null) return hit;
        }
        return null;
    }

    // 어두운 인간형 하나. 머리 + 어깨 + 몸통뿐이다 — 자세히 그리면 정체가 보여 무섭지 않다.
    private partial class Painter : Control
    {
        public int Mode;
        public float Progress;
        public Rect2 Rect;

        public override void _Draw()
        {
            if (Rect.Size.X <= 1f || Rect.Size.Y <= 1f) return;

            // 들어왔다 나가는 밝기. 문창은 좌→우로 지나가므로 자리도 함께 민다.
            float fade = Mathf.Sin(Mathf.Pi * Mathf.Clamp(Progress, 0f, 1f));
            var box = Rect;
            if (Mode == 2)   // DoorWindow
            {
                float slide = Mathf.Lerp(-Rect.Size.X, Rect.Size.X, Progress);
                box = new Rect2(Rect.Position + new Vector2(slide, 0f), Rect.Size);
                // 창 밖으로 나간 부분은 보이지 않는다 — 창 크기로 잘라 그린다.
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }

            float alpha = Mode switch
            {
                1 => 0.30f,   // 반사 — 옅게, 그러나 어두운 화면에서도 읽히게
                2 => 0.50f,   // 문창 — 그림자라 조금 진하게
                _ => 0.38f,   // 조명 복귀 — 한두 프레임뿐이라 조금 진하게
            };
            var ink = new Color(0.02f, 0.02f, 0.03f, alpha * fade);

            float w = box.Size.X, h = box.Size.Y;
            var center = box.Position + new Vector2(w * 0.5f, 0f);
            float headR = w * 0.22f;
            // 머리
            DrawCircle(center + new Vector2(0f, h * 0.16f), headR, ink);
            // 어깨~몸통 — 아래로 갈수록 살짝 넓어지는 사다리꼴
            var body = new Vector2[]
            {
                center + new Vector2(-w * 0.28f, h * 0.34f),
                center + new Vector2(w * 0.28f, h * 0.34f),
                center + new Vector2(w * 0.36f, h * 1.0f),
                center + new Vector2(-w * 0.36f, h * 1.0f),
            };
            DrawColoredPolygon(body, ink);
            // 목
            DrawRect(new Rect2(center.X - w * 0.07f, box.Position.Y + h * 0.26f, w * 0.14f, h * 0.10f), ink);
        }
    }
}
