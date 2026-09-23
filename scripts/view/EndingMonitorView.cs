using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 엔딩 연출 전용 CRT 화면(왼쪽 · 오른쪽 한 대씩). 시설 단말기 출력처럼 줄이 한 줄씩 찍히고,
// 왼쪽 화면에는 FINAL RECOVERY SEQUENCE 의 20칸 진행 막대가 뜬다.
// 내용과 순서는 EndingDirector 가 정한다 — 이 화면은 받은 줄을 그리기만 한다.
public partial class EndingMonitorView : Control
{
    public static EndingMonitorView Left { get; private set; }
    public static EndingMonitorView Right { get; private set; }

    public enum Tone { Normal, Good, Bad, Dim, Title }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Ink = new(0.84f, 0.90f, 0.89f);
    private static readonly Color Mint = new(0.46f, 0.92f, 0.80f);
    private static readonly Color Dim = new(0.40f, 0.50f, 0.52f);
    private static readonly Color Err = new(0.98f, 0.30f, 0.24f);
    private const float CharTime = 0.022f;

    private sealed class Line
    {
        public string Text = "";
        public Tone Tone;
        public int Size = 22;
        public bool Center;
        public double At;   // 찍히기 시작한 시각
        public double Ct = CharTime;   // 이 줄의 글자 간격
    }

    private bool _isLeft;
    private readonly List<Line> _lines = new();
    private Font _font;
    private double _t;

    // 줄은 밀어 넣은 순서대로 "한 줄 다 찍힌 뒤 다음 줄"로 찍힌다. 여러 줄을 한꺼번에
    // 밀어 넣어도 동시에 타이핑되지 않게, 예약된 마지막 시각을 들고 다닌다.
    private const double LineGap = 0.16;
    private double _queueEnd;

    // 방금 찍힌 글자 — GUIDE-0 입모양·보이스가 이 신호에 맞춰 움직인다.
    public event System.Action<char> CharTyped;
    private int _pumpLine, _pumpChar;

    // 경보 모드 — 화면 전체가 붉어진다(배드엔딩).
    private bool _alarm;

    // 줄 묶음을 화면 세로 한가운데에 놓는다(GUIDE-0 이 말하는 화면).
    private bool _blockCenter;

    // 진행 막대(왼쪽 화면). 음수면 그리지 않는다.
    private float _bar = -1f;
    private bool _barFailed;
    private string _header = "";
    private string _sub = "";   // 머리말 둘째 줄(FINAL RECOVERY SEQUENCE 등) — 진행 막대 위에 붙는다
    private Color _accent = Mint;

    public EndingMonitorView() { }
    public EndingMonitorView(bool isLeft) { _isLeft = isLeft; }

    public override void _Ready()
    {
        if (_isLeft) Left = this; else Right = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Left == this) Left = null;
        if (Right == this) Right = null;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        PumpTypedChars();
        QueueRedraw();
    }

    // 새로 드러난 글자를 하나씩 알린다(입모양·보이스·타건음).
    private void PumpTypedChars()
    {
        int guard = 0;
        while (_pumpLine < _lines.Count && guard++ < 256)
        {
            var l = _lines[_pumpLine];
            int shown = Mathf.Clamp((int)((_t - l.At) / l.Ct), 0, l.Text.Length);
            if (_pumpChar < shown)
            {
                char c = l.Text[_pumpChar++];
                CharTyped?.Invoke(c);
                continue;
            }
            if (shown < l.Text.Length) break;   // 아직 이 줄을 찍는 중
            _pumpLine++;
            _pumpChar = 0;
        }
    }

    // --- EndingDirector 가 부르는 것 -----------------------------------------

    public void Clear(string header = "", string sub = "")
    {
        _lines.Clear();
        _bar = -1f;
        _barFailed = false;
        _header = header;
        _sub = sub;
        _accent = _alarm ? Err : Mint;
        _queueEnd = _t;
        _pumpLine = 0;
        _pumpChar = 0;
        _blockCenter = false;
    }

    // 머리말·막대가 없는 화면에서 줄 묶음을 세로 한가운데로.
    public void SetBlockCenter(bool on) => _blockCenter = on;

    public void SetAccent(Color c) => _accent = c;

    // 배드엔딩 — 두 화면이 한꺼번에 붉어진다. 켠 뒤에 Clear 해도 붉은 상태가 유지된다.
    public void SetAlarm(bool on)
    {
        _alarm = on;
        if (on) _accent = Err;
        QueueRedraw();
    }

    // charTime 을 주면 그 줄만 느리게/빠르게 찍힌다(0 = 기본 속도).
    public void Push(string text, Tone tone = Tone.Normal, int size = 22, bool center = false,
        double charTime = 0)
    {
        text ??= "";
        double ct = charTime > 0 ? charTime : CharTime;
        double at = System.Math.Max(_t, _queueEnd);
        _lines.Add(new Line { Text = text, Tone = tone, Size = size, Center = center, At = at, Ct = ct });
        _queueEnd = at + text.Length * ct + LineGap;
    }

    // 밀어 넣은 줄이 전부 찍힐 때까지 남은 시간(초).
    public double TypingSecondsLeft => System.Math.Max(0.0, _queueEnd - _t);

    // 마지막 줄을 바꿔 쓴다(숫자가 올라가는 줄 등).
    public void ReplaceLast(string text, Tone tone)
    {
        if (_lines.Count == 0) { Push(text, tone); return; }
        _lines[^1].Text = text;
        _lines[^1].Tone = tone;
        _lines[^1].At = _t - 99;   // 다시 타이핑하지 않는다
        _queueEnd = _t;
        _pumpLine = _lines.Count;
        _pumpChar = 0;
    }

    public void SetBar(float percent, bool failed = false)
    {
        _bar = Mathf.Clamp(percent, 0f, 100f);
        _barFailed = failed;
    }

    public bool Typing => _t < _queueEnd;

    // --- 그리기 ------------------------------------------------------------

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas),
            _alarm ? new Color(0.20f, 0.018f, 0.018f) : new Color(0.018f, 0.028f, 0.030f));
        if (_alarm)
        {
            // 붉은 경보 — 위아래로 옅은 그라데이션 띠를 얹어 화면 전체가 달아오른 것처럼 보이게.
            DrawRect(new Rect2(0f, 0f, Canvas.X, Canvas.Y), new Color(0.85f, 0.06f, 0.05f, 0.22f));
            DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Err with { A = 0.85f }, false, 3f);
        }
        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), _accent with { A = 0.28f }, false, 1.4f);
        DrawString(_font, new Vector2(30f, 38f), _isLeft ? "CONTAINMENT CONTROL" : "SYSTEM MONITOR",
            HorizontalAlignment.Left, 420f, ViewFont.S(12), _alarm ? Err with { A = 0.8f } : Dim);
        DrawString(_font, new Vector2(Canvas.X - 150f, 38f), "NSP-07",
            HorizontalAlignment.Right, 120f, ViewFont.S(12), Dim);

        float y = 104f;
        if (!string.IsNullOrEmpty(_header))
        {
            DrawString(_font, new Vector2(48f, y), _header, HorizontalAlignment.Left, Canvas.X - 96f, ViewFont.S(22), _accent);
            if (!string.IsNullOrEmpty(_sub))
            {
                y += 36f;
                DrawString(_font, new Vector2(48f, y), _sub, HorizontalAlignment.Left, Canvas.X - 96f, ViewFont.S(20), Ink);
            }
            y += 18f;
            DrawRect(new Rect2(48f, y, Canvas.X - 96f, 1f), _accent with { A = 0.25f });
            y += 36f;
        }

        if (_bar >= 0f)
        {
            // 20칸 블록 막대 + 퍼센트.
            int filled = Mathf.Clamp(Mathf.FloorToInt(_bar / 5f + 0.0001f), 0, 20);
            string bar = new string('█', filled) + new string('░', 20 - filled);
            var col = _barFailed ? Err : _bar >= 100f ? Mint : Ink;
            DrawString(_font, new Vector2(48f, y + 10f), bar, HorizontalAlignment.Left, 520f, ViewFont.S(26), col);
            DrawString(_font, new Vector2(Canvas.X - 250f, y + 10f), $"{_bar:0.0}%", HorizontalAlignment.Right, 200f,
                ViewFont.S(30), col);
            y += 64f;
        }

        if (_blockCenter)
        {
            float total = 0f;
            foreach (var l in _lines) total += ViewFont.S(l.Size) + 14f;
            y = Mathf.Max(y, (Canvas.Y - total) * 0.5f);
        }

        foreach (var l in _lines)
        {
            int n = Mathf.Clamp((int)((_t - l.At) / l.Ct), 0, l.Text.Length);
            if (n <= 0) { y += ViewFont.S(l.Size) + 14f; continue; }   // 아직 안 찍힌 줄도 자리는 차지한다
            var col = l.Tone switch
            {
                Tone.Good => Mint,
                Tone.Bad => Err,
                Tone.Dim => Dim,
                Tone.Title => _accent,
                _ => Ink,
            };
            var align = l.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            float x = l.Center ? 0f : 48f;
            float w = l.Center ? Canvas.X : Canvas.X - 96f;
            DrawString(_font, new Vector2(x, y + l.Size), l.Text[..n], align, w, ViewFont.S(l.Size), col);
            y += ViewFont.S(l.Size) + 14f;
        }

        for (float sy = 0; sy < Canvas.Y; sy += 3f)
            DrawRect(new Rect2(0, sy, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
    }
}
