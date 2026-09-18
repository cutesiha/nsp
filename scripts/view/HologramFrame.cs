using Godot;

namespace NSP.View;

// PhoneCallHud가 사용하던 홀로그램 프레임을 기록 창에서도 그대로 쓸 수 있게 공용화했다.
public partial class HologramFrame : Control
{
    public Color Accent = new(0.55f, 0.95f, 1f);
    private float _time;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Color a = Accent;
        Vector2 s = Size;
        DrawRect(new Rect2(0, 0, s.X, 30), new Color(a.R, a.G, a.B, 0.18f));
        DrawLine(new Vector2(0, 30), new Vector2(s.X, 30), new Color(a.R, a.G, a.B, 0.6f), 1f);
        const float length = 18f;
        Color c = new(a.R, a.G, a.B, 0.9f);

        void Bracket(Vector2 p, Vector2 dx, Vector2 dy)
        {
            DrawLine(p, p + dx * length, c, 2f);
            DrawLine(p, p + dy * length, c, 2f);
        }

        Bracket(new Vector2(2, 2), Vector2.Right, Vector2.Down);
        Bracket(new Vector2(s.X - 2, 2), Vector2.Left, Vector2.Down);
        Bracket(new Vector2(2, s.Y - 2), Vector2.Right, Vector2.Up);
        Bracket(new Vector2(s.X - 2, s.Y - 2), Vector2.Left, Vector2.Up);
        for (float y = 2; y < s.Y; y += 3f)
            DrawLine(new Vector2(0, y), new Vector2(s.X, y), new Color(0, 0, 0, 0.10f), 1f);

        float lineY = Mathf.PosMod(_time * 60f, s.Y);
        DrawLine(new Vector2(0, lineY), new Vector2(s.X, lineY), new Color(a.R, a.G, a.B, 0.12f), 2f);
    }
}
