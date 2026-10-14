using Godot;
using NSP.Facility;

namespace NSP.View;

// 관리자 패드 위쪽에 잠깐 떴다 사라지는 큰 글씨 — "고양이 스트레스 주의!".
//
// 미니맵 아이콘 점멸(FacilityMinimap)과 **같은 신호**로 뜬다. 지도를 보고 있지 않을 때도
// 책상 위 패드가 같은 것을 말해 주어야, 누가 한계에 가까워졌는지 놓치지 않는다.
//
// 규칙은 없다 — FacilitySimulation.StressBandRaised 를 받아 그리기만 한다.
public partial class PadStressBanner : Control
{
    // 떠 있는 시간. 뒤쪽 0.6초는 서서히 사라진다.
    private const float ShowSeconds = 3.2f;
    private const float FadeSeconds = 0.6f;
    // 깜빡임 — 이 시간 동안 BlinkCount 번 뛴다.
    private const int BlinkCount = 7;

    private readonly Vector2 _canvas;
    private Font _font;
    private float _age = -1f;
    private string _text = "";
    private Color _color = Colors.White;

    public PadStressBanner(Vector2 canvas)
    {
        _canvas = canvas;
        Size = canvas;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        _font = ViewFont.Default;
        FacilitySimulation.StressBandRaised += OnRaised;
        SetProcess(true);
        Visible = false;
    }

    public override void _ExitTree() => FacilitySimulation.StressBandRaised -= OnRaised;

    private void OnRaised(string employeeId, string band)
    {
        var sim = FacilitySimulation.Instance;
        string name = sim?.GetEmployeeDef(employeeId)?.Codename ?? employeeId;
        _text = $"{name} 스트레스 {band}!";
        _color = FacilitySimulation.StressBandColor(band);
        _age = 0f;
        Visible = true;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_age < 0f) return;
        _age += (float)delta;
        if (_age >= ShowSeconds) { _age = -1f; Visible = false; return; }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_age < 0f) return;

        float fade = _age > ShowSeconds - FadeSeconds
            ? Mathf.Clamp((ShowSeconds - _age) / FadeSeconds, 0f, 1f)
            : 1f;
        // 깜빡임은 0.35~1 사이로만 — 완전히 꺼지면 글자가 "사라졌다"로 읽힌다.
        float beat = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Cos(_age / ShowSeconds * BlinkCount * Mathf.Tau));
        float a = fade * beat;

        var band = new Rect2(0, _canvas.Y * 0.105f, _canvas.X, 86f);
        DrawRect(band, new Color(0.14f, 0.03f, 0.03f, 0.92f * fade));
        DrawRect(band, _color with { A = a }, false, 4f);
        DrawString(_font, new Vector2(0, band.Position.Y + 60f), _text,
            HorizontalAlignment.Center, _canvas.X, ViewFont.S(50), _color with { A = a });
    }
}
