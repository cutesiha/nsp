using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// LOOSE 엔딩 전용 — 왼쪽 CRT 에 뜨는 「근무 인원 명단」.
//
// 타이틀 화면에서 늘 보던 그 신원 카드 그리드다(StaffIdCard 를 그대로 쓴다).
// 한 장씩 "확인" 이 찍히고 전부 정상으로 끝난 뒤, 완전한 무음 속에서 한 장이 깨진다.
// **깨진 카드는 복구되지 않는다** — 타이틀에서 0.1초 뒤 돌아오던 그 글리치와 다른 점이
// 이것 하나뿐이고, 그 차이가 이 엔딩의 전부다.
//
// 순서 · 소리 · 정적은 EndingDirector 가 쥔다. 이 화면은 받은 상태를 그릴 뿐이다.
public partial class EndingStaffIdView : Control
{
    public static EndingStaffIdView Instance { get; private set; }

    private static readonly Vector2 Canvas = new(800f, 600f);

    private sealed class Card
    {
        public StaffIdCard.Data Data = new();
        public bool Revealed;
        public bool Verified;
        public string Stamp = "";
        public bool Dead;
        public bool Broken;
        // 확인 표시가 지워지는 진행도(0 = 그대로, 1 = 완전히 사라짐).
        public float Erase;
    }

    private readonly List<Card> _cards = new();
    private readonly RandomNumberGenerator _rng = new();
    private Font _font;
    private string _foot = "";
    private bool _erasing;
    private float _eraseSeconds = 1.5f;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        _rng.Randomize();
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // --- EndingDirector 가 부르는 것 -------------------------------------------

    // removedId : 지목해 내보낸 직원(없으면 ""). 그 카드만 "격리 · 이송 완료" 로 죽는다.
    public void Present(string removedId)
    {
        _cards.Clear();
        _erasing = false;
        _foot = "";
        foreach (var d in StaffIdCard.Build(FacilitySimulation.Instance))
            _cards.Add(new Card { Data = d, Dead = !string.IsNullOrEmpty(removedId) && d.Id == removedId });
        QueueRedraw();
    }

    public int Count => _cards.Count;
    public string IdAt(int i) => i >= 0 && i < _cards.Count ? _cards[i].Data.Id : "";

    public void RevealAll()
    {
        foreach (var c in _cards) c.Revealed = true;
        QueueRedraw();
    }

    // 한 장에 도장을 찍는다. 내보낸 직원은 "격리 · 이송 완료", 나머지는 "확인".
    public void Verify(int index)
    {
        if (index < 0 || index >= _cards.Count) return;
        var c = _cards[index];
        c.Verified = true;
        c.Stamp = c.Dead ? "격리 · 이송 완료" : "확인";
        QueueRedraw();
    }

    public void SetFooter(string text)
    {
        _foot = text ?? "";
        QueueRedraw();
    }

    // 그 카드가 깨진다. 복구되지 않는다 — 노이즈가 이 카드에만 계속 흐른다.
    public void Break(string employeeId, float eraseSeconds = 1.5f)
    {
        int i = _cards.FindIndex(c => c.Data.Id == employeeId);
        if (i < 0) i = _cards.FindIndex(c => !c.Dead);
        if (i < 0) return;
        _cards[i].Broken = true;
        _eraseSeconds = Mathf.Max(0.05f, eraseSeconds);
        _erasing = true;
        QueueRedraw();
    }

    // 깨진 카드의 화면 위 자리(카메라를 아주 미세하게 그쪽으로 밀 때 쓴다).
    public Vector2 BrokenCenter()
    {
        int i = _cards.FindIndex(c => c.Broken);
        return i < 0 ? Canvas * 0.5f : StaffIdCard.Rect(i).GetCenter();
    }

    public override void _Process(double delta)
    {
        if (_erasing)
        {
            foreach (var c in _cards)
                if (c.Broken && c.Erase < 1f)
                    c.Erase = Mathf.Min(1f, c.Erase + (float)delta / _eraseSeconds);
        }
        // 깨진 카드가 있는 동안에는 계속 지지직거린다.
        if (_erasing || _cards.Exists(c => c.Broken)) QueueRedraw();
    }

    // --- 그리기 ---------------------------------------------------------------

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.018f, 0.028f, 0.030f));
        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), StaffIdCard.Mint with { A = 0.22f }, false, 1.4f);
        DrawString(_font, new Vector2(48f, 76f), "STAFF IDENTIFICATION", HorizontalAlignment.Left,
            520f, ViewFont.S(20), StaffIdCard.Mint);
        DrawString(_font, new Vector2(48f, 102f), "근무 인원 명단 정리", HorizontalAlignment.Left,
            520f, ViewFont.S(13), StaffIdCard.Dim);
        DrawString(_font, new Vector2(Canvas.X - 200f, 80f), $"{_cards.Count} / {_cards.Count}",
            HorizontalAlignment.Right, 160f, ViewFont.S(16), StaffIdCard.Dim);
        DrawRect(new Rect2(48f, 120f, Canvas.X - 96f, 1f), StaffIdCard.Mint with { A = 0.20f });

        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            if (!c.Revealed) continue;
            // 확인 표시는 지워지는 동안 서서히 흐려진다(완전히 지워지면 사라진다).
            string stamp = c.Erase >= 1f ? "" : c.Stamp;
            var col = c.Dead ? StaffIdCard.Dim : StaffIdCard.Mint;
            if (c.Broken) col = col with { A = 1f - c.Erase };
            StaffIdCard.Draw(this, _font, c.Data, StaffIdCard.Rect(i), new StaffIdCard.Style
            {
                Broken = c.Broken,
                Dead = c.Dead,
                Stamp = c.Verified ? stamp : "",
                StampColor = col,
            }, _rng);
        }

        DrawRect(new Rect2(48f, 506f, Canvas.X - 96f, 1f), StaffIdCard.Mint with { A = 0.18f });
        if (!string.IsNullOrEmpty(_foot))
            DrawString(_font, new Vector2(48f, 538f), _foot, HorizontalAlignment.Left,
                Canvas.X - 96f, ViewFont.S(16), StaffIdCard.Ink);

        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));

        // 깨진 카드 위에만 노이즈가 계속 흐른다. 화면 전체는 멀쩡하다 — 그게 더 불편하다.
        int b = _cards.FindIndex(c => c.Broken);
        if (b < 0) return;
        var r = StaffIdCard.Rect(b);
        for (int i = 0; i < 7; i++)
        {
            float y = _rng.RandfRange(r.Position.Y, r.Position.Y + r.Size.Y);
            DrawRect(new Rect2(r.Position.X, y, r.Size.X, _rng.RandfRange(1f, 5f)),
                new Color(0.8f, 0.95f, 0.95f, _rng.RandfRange(0.05f, 0.20f)));
        }
    }
}
