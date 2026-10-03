using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 엔딩 — 책상 위 관리자 패드 화면에 뜨는 신원 조회 콘솔.
//
// 프롤로그는 "큰 모니터 안의 기록 영상을 당신이 **본다**" 였다. 엔딩의 신원 조회는
// 반대로 **손 앞 작은 화면**에서 벌어진다. 큰 화면은 이미 꺼져 있고, 방은 어둡고,
// 빛나는 것은 이 패드 하나다.
//
// 평소 패드 UI(PadView) 위에 덮어쓰는 한 장이다. EndingDirector 가 줄을 밀어 넣고,
// 이 화면은 그 줄을 한 글자씩 찍기만 한다(EndingMonitorView 와 같은 규칙).
public partial class EndingPadConsole : Control
{
    public static EndingPadConsole Instance { get; private set; }

    private static readonly Vector2 Canvas = new(1120f, 700f);
    private static readonly Color Bg = new(0.012f, 0.020f, 0.024f);
    private static readonly Color Ink = new(0.84f, 0.90f, 0.89f);
    private static readonly Color Mint = new(0.46f, 0.92f, 0.80f);
    private static readonly Color Dim = new(0.40f, 0.50f, 0.52f);
    private static readonly Color Err = new(0.98f, 0.30f, 0.24f);
    private const double CharTime = 0.034;
    private const double LineGap = 0.18;

    private sealed class Line
    {
        public string Text = "";
        public EndingMonitorView.Tone Tone;
        public int Size = 30;
        public double At;
        public double Ct = CharTime;
    }

    private readonly List<Line> _lines = new();
    private Font _font;
    private double _t, _queueEnd;
    private string _header = "";

    // 글자가 하나 찍힐 때마다 — 타건음 · GUIDE-0 입모양이 이 신호를 받는다.
    public event System.Action<char> CharTyped;
    private int _pumpLine, _pumpChar;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += delta;
        Pump();
        QueueRedraw();
    }

    private void Pump()
    {
        int guard = 0;
        while (_pumpLine < _lines.Count && guard++ < 256)
        {
            var l = _lines[_pumpLine];
            int shown = Mathf.Clamp((int)((_t - l.At) / l.Ct), 0, l.Text.Length);
            if (_pumpChar < shown) { CharTyped?.Invoke(l.Text[_pumpChar++]); continue; }
            if (shown < l.Text.Length) break;
            _pumpLine++;
            _pumpChar = 0;
        }
    }

    // --- EndingDirector 가 부르는 것 -------------------------------------------

    public void Open(string header)
    {
        _lines.Clear();
        _header = header ?? "";
        _queueEnd = _t;
        _pumpLine = 0;
        _pumpChar = 0;
        Visible = true;
        QueueRedraw();
    }

    public void Close()
    {
        Visible = false;
        _lines.Clear();
    }

    // charTime 을 주면 그 줄만 느리게 찍힌다(마지막 한 줄을 또박또박 찍을 때).
    public void Push(string text, EndingMonitorView.Tone tone = EndingMonitorView.Tone.Normal,
        int size = 30, double charTime = 0)
    {
        text ??= "";
        double ct = charTime > 0 ? charTime : CharTime;
        double at = System.Math.Max(_t, _queueEnd);
        _lines.Add(new Line { Text = text, Tone = tone, Size = size, At = at, Ct = ct });
        _queueEnd = at + text.Length * ct + LineGap;
    }

    public bool Typing => _t < _queueEnd;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), Bg);
        DrawRect(new Rect2(26f, 22f, Canvas.X - 52f, Canvas.Y - 44f), Mint with { A = 0.25f }, false, 1.6f);

        float y = 96f;
        if (!string.IsNullOrEmpty(_header))
        {
            DrawString(_font, new Vector2(64f, y), _header, HorizontalAlignment.Left,
                Canvas.X - 128f, ViewFont.S(34), Mint);
            y += 26f;
            DrawRect(new Rect2(64f, y, Canvas.X - 128f, 1f), Mint with { A = 0.25f });
            y += 54f;
        }

        foreach (var l in _lines)
        {
            int n = Mathf.Clamp((int)((_t - l.At) / l.Ct), 0, l.Text.Length);
            if (n > 0)
            {
                var col = l.Tone switch
                {
                    EndingMonitorView.Tone.Good => Mint,
                    EndingMonitorView.Tone.Bad => Err,
                    EndingMonitorView.Tone.Dim => Dim,
                    EndingMonitorView.Tone.Warn => new Color(1f, 0.78f, 0.38f),
                    EndingMonitorView.Tone.Title => Mint,
                    _ => Ink,
                };
                DrawString(_font, new Vector2(72f, y + l.Size), l.Text[..n], HorizontalAlignment.Left,
                    Canvas.X - 144f, ViewFont.S(l.Size), col);
            }
            y += ViewFont.S(l.Size) + 20f;
        }

        for (float sy = 0; sy < Canvas.Y; sy += 3f)
            DrawRect(new Rect2(0, sy, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.10f));
    }
}
