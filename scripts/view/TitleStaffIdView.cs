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
// 실제 결번자/방해자와는 아무 관계가 없다 — 같은 직원이 연속으로 깨지지도 않는다.
// (진짜 힌트로 오해되면 안 되므로 시뮬레이션 상태를 아예 읽지 않는다.)
public partial class TitleStaffIdView : Control
{
    public static TitleStaffIdView Instance { get; private set; }

    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    private static readonly Color Err = new(0.92f, 0.28f, 0.24f);

    private const float GridLeft = 82f, GridTop = 150f;
    private const float CardW = 208f, CardH = 152f, GapX = 16f, GapY = 18f;

    // 데이터에 코드네임이 없을 때만 쓰는 폴백. 화면에는 한글 이름만 찍는다.
    private static readonly Dictionary<string, string> Fallback = new()
    {
        { "rabbit", "토끼" }, { "cat", "고양이" }, { "fox", "여우" },
        { "sheep", "양" }, { "wolf", "늑대" }, { "dog", "강아지" },
    };

    // 화면에 놓는 순서(윗줄 3명 / 아랫줄 3명). 목록에 없는 직원은 뒤에 붙는다.
    private static readonly string[] Order = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    private sealed class Card
    {
        public string Id = "";
        public string Codename = "";
        public Texture2D Face;
        public Color Tint = Colors.White;
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
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        // 타이틀에서는 해금 여부와 상관없이 여섯 명을 전부 보여준다.
        var ids = new List<string>(sim.GetEmployeeIds());
        ids.Sort((a, b) =>
        {
            int ia = Array.IndexOf(Order, a), ib = Array.IndexOf(Order, b);
            if (ia < 0) ia = int.MaxValue;
            if (ib < 0) ib = int.MaxValue;
            return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a, b);
        });
        foreach (var id in ids)
        {
            var def = sim.GetEmployeeDef(id);
            if (def == null) continue;
            _cards.Add(new Card
            {
                Id = id,
                Codename = string.IsNullOrEmpty(def.Codename)
                    ? Fallback.GetValueOrDefault(id, id) : def.Codename,
                Face = def.FacePortrait,
                Tint = def.IconColor,
            });
        }
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
            var c = _cards[_hover];
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

        // 진엔딩 뒤의 시작 화면은 시설이 정상화된 상태 — 신원 카드가 더는 깨지지 않는다.
        if (_poweredOn && !_scanning && EndingState.Last != EndingState.Kind.True)
        {
            _nextGlitch -= delta;
            if (_nextGlitch <= 0)
            {
                // 5~8초에 한 번. 직전과 같은 직원은 고르지 않는다.
                _nextGlitch = _rng.RandfRange(5f, 8f);
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

    private static Rect2 CardRect(int i)
    {
        int col = i % 3, row = i / 3;
        return new Rect2(GridLeft + col * (CardW + GapX), GridTop + row * (CardH + GapY), CardW, CardH);
    }

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
        var r = CardRect(i);
        if (!c.Revealed) return;

        bool broken = i == _glitchIndex;
        bool hot = i == _hover;

        DrawRect(r, new Color(0.04f, 0.09f, 0.10f, 0.9f));
        DrawRect(r, (broken ? Err : hot ? Mint : Dim) with { A = broken ? 0.9f : hot ? 0.85f : 0.4f },
            false, hot || broken ? 2f : 1.2f);
        if (hot) DrawRect(r, Mint with { A = 0.07f });

        // 가면 초상.
        var box = new Rect2(r.Position.X + (r.Size.X - 72f) * 0.5f, r.Position.Y + 14f, 72f, 72f);
        DrawRect(box, new Color(0.02f, 0.05f, 0.06f, 0.9f));
        if (broken)
        {
            // 얼굴이 뭉개진다.
            for (int k = 0; k < 8; k++)
                DrawRect(new Rect2(box.Position.X, box.Position.Y + k * 9f, box.Size.X, 7f),
                    new Color(0.55f, 0.58f, 0.58f, _rng.RandfRange(0.25f, 0.85f)));
        }
        else if (c.Face != null)
        {
            var src = c.Face.GetSize();
            if (src.X > 0f && src.Y > 0f)
            {
                float k = Mathf.Min(box.Size.X / src.X, box.Size.Y / src.Y);
                var dst = src * k;
                DrawTextureRect(c.Face, new Rect2(box.Position + (box.Size - dst) * 0.5f, dst), false);
            }
        }
        else
        {
            DrawCircle(box.GetCenter(), 22f, c.Tint);
        }
        DrawRect(box, (broken ? Err : Dim) with { A = 0.6f }, false, 1f);

        // 이름 한 줄만 둔다. 신원 오류는 이름이 깨지고 테두리가 붉어지는 것으로 읽힌다.
        DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 118f),
            broken ? "████" : c.Codename, HorizontalAlignment.Center, r.Size.X,
            ViewFont.S(20), broken ? Err : hot ? Ink : Ink with { A = 0.9f });
    }

    private void DrawFooter()
    {
        // 결말의 흔적 — 실패 뒤에는 CONTAINMENT FAILED, 복구 뒤에는 직원들이 남긴 짧은 메모.
        string idle = EndingState.Last switch
        {
            EndingState.Kind.Bad => "CONTAINMENT FAILED",
            EndingState.Kind.True => "야간 관리 업무 종료.   관리자님, 수고하셨습니다.",
            _ => "ID STATUS : NORMAL",
        };
        var idleCol = EndingState.Last switch
        {
            EndingState.Kind.Bad => Err,
            EndingState.Kind.True => Ink,
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
