using System;
using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 타이틀 화면 2 — 왼쪽 CRT 의 직원 신원 확인 화면.
//
// 장식이 아니라 "이 게임이 뭘 하는 게임인지"를 타이틀에서 미리 보여주는 화면이다.
//   직원 여섯 명  ·  신원  ·  그중 뭔가 이상함
//
// 몇 초에 한 번 무작위로 한 명의 얼굴이 0.1초쯤 깨진다. 이건 순수한 연출이고
// 실제 결번/방해자와는 아무 관계가 없다 — 같은 직원이 연속으로 깨지지도 않는다.
// (진짜 힌트로 오해되면 안 되므로 시뮬레이션 상태를 아예 읽지 않는다.)
public partial class TitleStaffIdView : Control
{
    public static TitleStaffIdView Instance { get; private set; }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    private static readonly Color Err = new(0.92f, 0.28f, 0.24f);

    // 카드 자리 · 색 · 그림은 StaffIdCard 가 가진다 — 엔딩(EndingStaffIdView)이 같은 그림을 쓴다.
    private sealed class Card
    {
        public StaffIdCard.Data Data = new();
        public bool Revealed;
        public bool Verified;
    }

    private readonly List<Card> _cards = new();
    private readonly RandomNumberGenerator _rng = new();
    private Font _font;

    private bool _poweredOn;
    private float _t;
    private int _hover = -1;

    // 무작위 신원 오류 연출.
    private double _nextGlitch = 6.0;
    private double _glitchUntil;
    private int _glitchIndex = -1;
    private int _lastGlitchIndex = -1;

    // 근무 개시 시 위에서 아래로 훑는 인증 스캔.
    private bool _scanning;
    private float _scanY;
    private Action _scanDone;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        _rng.Randomize();
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
        BuildCards();
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildCards()
    {
        _cards.Clear();
        // 타이틀에서는 해금 여부와 상관없이 여섯 명을 전부 보여준다.
        foreach (var d in StaffIdCard.Build(FacilitySimulation.Instance))
            _cards.Add(new Card { Data = d });
    }

    // --- 전원 / 스캔 ---------------------------------------------------------

    public bool PoweredOn => _poweredOn;

    // 장비가 하나씩 켜지는 연출 — 카드도 한 장씩 뜬다.
    public void PowerOn()
    {
        if (_cards.Count == 0) BuildCards();
        _poweredOn = true;
        foreach (var c in _cards) { c.Revealed = false; c.Verified = false; }
        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            GetTree().CreateTimer(0.10 + i * 0.09).Timeout += () =>
            {
                card.Revealed = true;
                QueueRedraw();
            };
        }
        _nextGlitch = 5.0;
        QueueRedraw();
    }

    public void PowerOff()
    {
        _poweredOn = false;
        _hover = -1;
        _scanning = false;
        foreach (var c in _cards) { c.Revealed = false; c.Verified = false; }
        QueueRedraw();
    }

    // 근무 개시 — 전원 명단을 한 번 훑고 전부 VERIFIED 로 바꾼다.
    public void RunScan(Action onDone)
    {
        if (!_poweredOn || _cards.Count == 0) { onDone?.Invoke(); return; }
        _scanning = true;
        _scanY = 130f;
        _scanDone = onDone;
        foreach (var c in _cards) c.Verified = false;
        QueueRedraw();
    }

    // --- 마우스 반응 ---------------------------------------------------------

    public int IndexAt(Vector2 p)
    {
        for (int i = 0; i < _cards.Count; i++)
            if (CardRect(i).HasPoint(p)) return i;
        return -1;
    }

    // 커서가 올라간 카드. 바뀌었으면 true(효과음용).
    public bool SetHover(int index)
    {
        if (!_poweredOn) index = -1;
        if (_hover == index) return false;
        _hover = index;
        QueueRedraw();
        return index >= 0;
    }

    public string HoverLine
    {
        get
        {
            if (_hover < 0 || _hover >= _cards.Count) return "";
            var c = _cards[_hover].Data;
            // 아주 낮은 확률로 이 줄까지 깨진다(마우스를 올린 직원과 무관한 연출).
            bool garbled = _glitchIndex == _hover && _glitchUntil > 0;
            return garbled
                ? $"{c.Codename}   시설 직원 신ㅇ■▓▒▒"
                : $"{c.Codename}   시설 직원 신원 확인됨.";
        }
    }

    // --- tick ----------------------------------------------------------------

    public override void _Process(double delta)
    {
        _t += (float)delta;
        bool redraw = false;

        // 결번을 찾아낸 뒤의 시작 화면은 시설이 정리된 상태 — 신원 카드가 더는 깨지지 않는다.
        // 찾지 못한 채 끝났으면(Loose · Bad) 그대로 깨진다. 아직 그 자리에 있기 때문이다.
        if (_poweredOn && !_scanning && EndingState.Last is not (EndingState.Kind.True or EndingState.Kind.Late))
        {
            _nextGlitch -= delta;
            if (_nextGlitch <= 0)
            {
                // 5~8초에 한 번. 직전과 같은 직원은 고르지 않는다.
                // LOOSE(복구했지만 결번을 놓침) 뒤에는 그 간격이 1.5배 잦아진다.
                float rate = EndingState.Last == EndingState.Kind.Loose ? 1f / 1.5f : 1f;
                _nextGlitch = _rng.RandfRange(5f, 8f) * rate;
                if (_cards.Count > 1)
                {
                    int pick;
                    do { pick = _rng.RandiRange(0, _cards.Count - 1); } while (pick == _lastGlitchIndex);
                    _glitchIndex = pick;
                    _lastGlitchIndex = pick;
                    _glitchUntil = 0.12;
                    Sfx.Instance?.Play("noise", -24f);
                }
            }
            if (_glitchUntil > 0)
            {
                _glitchUntil -= delta;
                if (_glitchUntil <= 0) _glitchIndex = -1;
                redraw = true;
            }
        }

        if (_scanning)
        {
            _scanY += (float)delta * 340f;
            for (int i = 0; i < _cards.Count; i++)
            {
                var r = CardRect(i);
                if (!_cards[i].Verified && _scanY > r.Position.Y + r.Size.Y * 0.5f)
                {
                    _cards[i].Verified = true;
                    Sfx.Instance?.Play("tick", -18f);
                }
            }
            redraw = true;
            if (_scanY > 500f)
            {
                _scanning = false;
                var cb = _scanDone;
                _scanDone = null;
                cb?.Invoke();
            }
        }

        if (redraw || Mathf.PosMod(_t, 0.2f) < delta) QueueRedraw();
    }

    // --- 그리기 ---------------------------------------------------------------

    private static Rect2 CardRect(int i) => StaffIdCard.Rect(i);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.018f, 0.028f, 0.030f));

        if (!_poweredOn)
        {
            // 전원이 들어오기 전 — 약한 노이즈만.
            for (int i = 0; i < 40; i++)
            {
                float y = _rng.RandfRange(0f, Canvas.Y);
                DrawRect(new Rect2(0f, y, Canvas.X, _rng.RandfRange(1f, 4f)),
                    new Color(0.6f, 0.7f, 0.7f, _rng.RandfRange(0.01f, 0.05f)));
            }
            DrawString(_font, new Vector2(0f, 310f), "NO SIGNAL", HorizontalAlignment.Center,
                Canvas.X, ViewFont.S(18), Dim with { A = 0.35f });
            Scanlines();
            return;
        }

        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Mint with { A = 0.22f }, false, 1.4f);
        DrawString(_font, new Vector2(48f, 76f), "STAFF IDENTIFICATION", HorizontalAlignment.Left,
            520f, ViewFont.S(20), Mint);
        DrawString(_font, new Vector2(48f, 102f), "제7지하시설 · 야간근무 편성", HorizontalAlignment.Left,
            520f, ViewFont.S(13), Dim);
        DrawString(_font, new Vector2(Canvas.X - 200f, 80f), $"{_cards.Count} / {_cards.Count}",
            HorizontalAlignment.Right, 160f, ViewFont.S(16), Dim);
        DrawRect(new Rect2(48f, 120f, Canvas.X - 96f, 1f), Mint with { A = 0.20f });

        for (int i = 0; i < _cards.Count; i++) DrawCard(i);

        if (_scanning)
        {
            DrawRect(new Rect2(40f, _scanY, Canvas.X - 80f, 2f), Mint with { A = 0.9f });
            DrawRect(new Rect2(40f, _scanY - 18f, Canvas.X - 80f, 18f), Mint with { A = 0.06f });
        }

        DrawFooter();
        Scanlines();

        // 신원 오류가 뜨는 순간에는 화면 전체가 한 번 찢어진다.
        if (_glitchIndex >= 0)
        {
            for (int i = 0; i < 6; i++)
            {
                float y = _rng.RandfRange(0f, Canvas.Y);
                DrawRect(new Rect2(0f, y, Canvas.X, _rng.RandfRange(2f, 12f)),
                    new Color(0.8f, 0.95f, 0.95f, _rng.RandfRange(0.05f, 0.18f)));
            }
        }
    }

    private void DrawCard(int i)
    {
        var c = _cards[i];
        if (!c.Revealed) return;
        StaffIdCard.Draw(this, _font, c.Data, CardRect(i),
            new StaffIdCard.Style { Hot = i == _hover, Broken = i == _glitchIndex }, _rng);
    }

    private void DrawFooter()
    {
        // 결말의 흔적 — 실패 뒤에는 CONTAINMENT FAILED, 복구 뒤에는 직원들이 남긴 짧은 메모.
        string idle = EndingState.Last switch
        {
            EndingState.Kind.Bad => "CONTAINMENT FAILED",
            EndingState.Kind.Late => "PERMANENT SEAL ENGAGED",
            EndingState.Kind.True => "야간 관리 업무 종료.   관리자님, 수고하셨습니다.",
            // 명단은 여섯 명 전부 정상이다. 그 줄이 그대로 남아 있는 것이 이 엔딩의 뒷맛이다.
            EndingState.Kind.Loose => "근무 인원 명단 정리 완료.   이상 없음.",
            _ => "ID STATUS : NORMAL",
        };
        var idleCol = EndingState.Last switch
        {
            EndingState.Kind.Bad => Err,
            EndingState.Kind.Late => new Color(0.95f, 0.78f, 0.55f),
            EndingState.Kind.True or EndingState.Kind.Loose => Ink,
            _ => Dim,
        };
        string line = _glitchIndex >= 0
            ? "ID STATUS : ▒▒▒▒▒▒"
            : _hover >= 0 ? HoverLine : idle;
        var col = _glitchIndex >= 0 ? Err : _hover >= 0 ? Ink : idleCol;
        DrawRect(new Rect2(48f, 506f, Canvas.X - 96f, 1f), Mint with { A = 0.18f });
        DrawString(_font, new Vector2(48f, 538f), line, HorizontalAlignment.Left,
            Canvas.X - 96f, ViewFont.S(16), col);
    }

    private void Scanlines()
    {
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
    }
}
