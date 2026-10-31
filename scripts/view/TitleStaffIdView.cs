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
        _panel = "";
        _btnHover = "";
        foreach (var c in _cards) { c.Revealed = false; c.Verified = false; }
        QueueRedraw();
    }

    // 근무 개시 — 전원 명단을 한 번 훑고 전부 VERIFIED 로 바꾼다.
    public void RunScan(Action onDone)
    {
        if (!_poweredOn || _cards.Count == 0) { onDone?.Invoke(); return; }
        _panel = "";
        _btnHover = "";
        _scanning = true;
        _scanY = 130f;
        _scanDone = onDone;
        foreach (var c in _cards) c.Verified = false;
        QueueRedraw();
    }

    // --- 마우스 반응 ---------------------------------------------------------

    // ── 외부 리소스 출처 (2026-10-31 사용자 요청) ─────────────────────────────
    //
    // 카드 아래 오른쪽 빈자리에 「출처」 버튼. 누르면 이 화면 위에 출처 창이 덮이고, 오른쪽 위 X 로 닫는다.
    // 내용은 GameCredits 한 곳에서 읽는다(엔딩 크레딧과 같은 목록 — 여기서만 주소까지 적는다).
    public const string SourcesId = "sources";
    public const string SourcesCloseId = "sources_close";

    private static readonly Rect2 SourcesButton = new(Canvas.X - 196f, 548f, 148f, 30f);
    private static readonly Rect2 SourcesPanel = new(28f, 26f, Canvas.X - 56f, Canvas.Y - 52f);
    private static readonly Rect2 SourcesClose = new(Canvas.X - 28f - 50f, 34f, 40f, 40f);

    // 지금 덮여 있는 창 - "" 없음, "sources" 출처, "help" 조작 안내. 어느 쪽이든 X · ESC 로 닫는다.
    private string _panel = "";
    public bool SourcesOpen => _panel.Length > 0;
    public bool HelpOpen => _panel == "help";

    // 「조작 안내」 버튼 - 오른쪽 위(직원 수 옆). 처음 하는 사람 · 심사위원이 조작과 목표를 한 장에서 본다.
    public const string HelpId = "help";
    private static readonly Rect2 HelpButton = new(Canvas.X - 196f, 56f, 148f, 30f);
    private string _btnHover = "";

    // 이 캔버스 좌표에 있는 버튼("sources" · "sources_close" · 없으면 "").
    public string ButtonAt(Vector2 p)
    {
        if (!_poweredOn || _scanning) return "";
        if (SourcesOpen) return SourcesClose.Grow(4f).HasPoint(p) ? SourcesCloseId : "";
        for (int i = 0; i < EndingState.Catalog.Length; i++)
            if (EndingSlot(i).HasPoint(p)) return EndingPrefix + i;
        if (HelpButton.HasPoint(p)) return HelpId;
        return SourcesButton.HasPoint(p) ? SourcesId : "";
    }

    // ── 엔딩 수집 칸 (2026-10-31) ─────────────────────────────────────────
    //
    // 출처 버튼 왼쪽에 엔딩 네 칸. 본 엔딩은 이름표(TRUE · LOOSE …)가, 못 본 엔딩은 「? ? ?」 가 뜬다.
    // 가리키면 아래 안내 줄에 그 엔딩이 갈리는 조건이 뜬다 — 네 엔딩이 두 축(코어 복구 · 지목)으로
    // 갈린다는 구조가 첫 화면에서 보이게.
    public const string EndingPrefix = "ending:";
    private const float EndingLeft = 162f, EndingTop = 548f, EndingW = 100f, EndingH = 30f, EndingGap = 6f;

    private static Rect2 EndingSlot(int i) => new(EndingLeft + i * (EndingW + EndingGap), EndingTop, EndingW, EndingH);

    // 엔딩 칸에 마우스를 올렸을 때 안내 줄에 띄울 문장. 엔딩 칸이 아니면 "".
    public static string EndingHint(string id)
    {
        if (id == null || !id.StartsWith(EndingPrefix) || !int.TryParse(id[EndingPrefix.Length..], out int i)) return "";
        if (i < 0 || i >= EndingState.Catalog.Length) return "";
        var e = EndingState.Catalog[i];
        return EndingState.Seen(e.Kind)
            ? $"{e.Tag} END: {e.Name}   ({e.Rule})"
            : $"아직 보지 못한 엔딩   ({e.Rule})";
    }

    private void DrawEndingSlots()
    {
        int seen = EndingState.SeenCount;
        DrawString(_font, new Vector2(48f, EndingTop + 21f), $"ENDING {seen}/{EndingState.Catalog.Length}",
            HorizontalAlignment.Left, EndingLeft - 50f, ViewFont.S(12), seen > 0 ? Mint : Dim);
        for (int i = 0; i < EndingState.Catalog.Length; i++)
        {
            var e = EndingState.Catalog[i];
            bool got = EndingState.Seen(e.Kind);
            bool on = _btnHover == EndingPrefix + i;
            var r = EndingSlot(i);
            var edge = got ? (e.Kind == EndingState.Kind.True ? Mint : Ink) : Dim;
            if (on) DrawRect(r, edge with { A = 0.14f });
            DrawRect(r, edge with { A = got ? (on ? 0.95f : 0.6f) : (on ? 0.7f : 0.3f) }, false, on ? 1.6f : 1f);
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 21f), got ? e.Tag : "? ? ?",
                HorizontalAlignment.Center, r.Size.X, ViewFont.S(13), got ? (on ? Ink : edge) : Dim with { A = on ? 0.9f : 0.6f });
        }
    }

    // 커서가 올라간 버튼. 바뀌었으면 true(효과음용).
    public bool SetButtonHover(string id)
    {
        id ??= "";
        if (_btnHover == id) return false;
        _btnHover = id;
        QueueRedraw();
        return id.Length > 0;
    }

    public void OpenHelp()
    {
        if (!_poweredOn) return;
        _panel = "help";
        _hover = -1;
        QueueRedraw();
    }

    public void OpenSources()
    {
        if (!_poweredOn) return;
        _panel = "sources";
        _hover = -1;
        QueueRedraw();
    }

    public void CloseSources()
    {
        _panel = "";
        _btnHover = "";
        QueueRedraw();
    }

    public int IndexAt(Vector2 p)
    {
        if (SourcesOpen) return -1;   // 출처 창이 덮고 있는 동안 카드는 가려져 있다
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
        DrawString(_font, new Vector2(48f, 102f), "제7지하시설 / 야간근무 편성", HorizontalAlignment.Left,
            520f, ViewFont.S(13), Dim);
        DrawString(_font, new Vector2(Canvas.X - 330f, 80f), $"{_cards.Count} / {_cards.Count}",
            HorizontalAlignment.Right, 120f, ViewFont.S(16), Dim);
        DrawRect(new Rect2(48f, 120f, Canvas.X - 96f, 1f), Mint with { A = 0.20f });

        for (int i = 0; i < _cards.Count; i++) DrawCard(i);

        if (_scanning)
        {
            DrawRect(new Rect2(40f, _scanY, Canvas.X - 80f, 2f), Mint with { A = 0.9f });
            DrawRect(new Rect2(40f, _scanY - 18f, Canvas.X - 80f, 18f), Mint with { A = 0.06f });
        }

        DrawFooter();
        if (!_scanning) { DrawEndingSlots(); DrawSourcesButton(); DrawHelpButton(); }
        if (SourcesOpen) DrawSourcesPanel();
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

    // 「출처」 버튼 — 단말기 선택지와 같은 네모 칸. 가리키면 칸이 차고 테두리가 밝아진다.
    private void DrawSourcesButton()
    {
        bool on = _btnHover == SourcesId;
        var r = SourcesButton;
        if (on) DrawRect(r, Mint with { A = 0.16f });
        DrawRect(r, Mint with { A = on ? 0.9f : 0.38f }, false, on ? 1.8f : 1.1f);
        DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 21f), (on ? "> " : "") + "출처 / CREDITS",
            HorizontalAlignment.Center, r.Size.X, ViewFont.S(14), on ? Ink : Dim);
    }

    // 「조작 안내」 버튼 — 출처 버튼과 같은 모양.
    private void DrawHelpButton()
    {
        bool on = _btnHover == HelpId;
        var r = HelpButton;
        if (on) DrawRect(r, Mint with { A = 0.16f });
        DrawRect(r, Mint with { A = on ? 0.9f : 0.38f }, false, on ? 1.8f : 1.1f);
        DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 21f), (on ? "> " : "") + "조작 안내 / HELP",
            HorizontalAlignment.Center, r.Size.X, ViewFont.S(14), on ? Ink : Dim);
    }

    private void DrawSourcesPanel()
    {
        var p = SourcesPanel;
        DrawRect(p, new Color(0.012f, 0.022f, 0.024f));
        DrawRect(p, Mint with { A = 0.55f }, false, 1.6f);

        float x = p.Position.X + 26f, y = p.Position.Y + 44f, w = p.Size.X - 52f;
        DrawString(_font, new Vector2(x, y), HelpOpen ? "조작 안내" : "외부 리소스 출처",
            HorizontalAlignment.Left, w - 60f, ViewFont.S(20), Mint);
        DrawString(_font, new Vector2(x, y + 22f), HelpOpen ? "HOW TO PLAY" : "EXTERNAL RESOURCES",
            HorizontalAlignment.Left, w, ViewFont.S(11), Dim);
        DrawRect(new Rect2(x, y + 32f, w, 1f), Mint with { A = 0.25f });
        DrawClose();
        if (HelpOpen) { DrawHelp(x, y + 62f, w); return; }
        DrawSourcesList(x, y + 62f, w);
    }

    // 조작 안내 — 키는 설정에서 바꾼 값을 그대로 읽는다(설정 › 조작 키).
    private void DrawHelp(float x, float y, float w)
    {
        string K(GameSettings.ZoomTarget t) => OS.GetKeycodeString(GameSettings.GetKey(t));
        var rows = new (string Key, string What)[]
        {
            ("마우스", "모니터를 눌러 가까이 보기, 미니맵에서 직원을 끌어 다른 방에 배치"),
            ($"{K(GameSettings.ZoomTarget.Monitor1)} / {K(GameSettings.ZoomTarget.Monitor2)}", "모니터 1 / 모니터 2 확대"),
            ($"{K(GameSettings.ZoomTarget.Sensor)} / Tab", "관리자 패드 꺼내기 (지침, 단서, 직원 정보)"),
            (K(GameSettings.ZoomTarget.PowerPanel), "전력 기기 확대 (조명, CCTV, 차폐문에 전력 배분)"),
            ("T / L / D", "오늘의 업무 / 시설 로그 / 대화 기록"),
            ("ESC", "일시정지, 창 닫기"),
            ("SKIP", "이야기 건너뛰기 (화면 오른쪽 위)"),
        };
        foreach (var (key, what) in rows)
        {
            DrawString(_font, new Vector2(x + 6f, y), key, HorizontalAlignment.Left, 120f, ViewFont.S(13), Mint);
            DrawString(_font, new Vector2(x + 132f, y), what, HorizontalAlignment.Left, w - 132f, ViewFont.S(13), Ink);
            y += 26f;
        }
        y += 14f;
        DrawString(_font, new Vector2(x, y), "목표", HorizontalAlignment.Left, w, ViewFont.S(12), Mint with { A = 0.8f });
        y += 22f;
        string[] goals =
        {
            "직원을 작업실에 배치해 봉쇄 코어를 복구하십시오. 비워 둔 방은 고장 납니다.",
            "경고와 사고는 사람을 보내 고치고, 이상 개체는 CCTV 로 위치를 지켜보십시오.",
            "휴게시간에 조사 자료를 골라 직원을 심문하고, 어긋나는 진술을 찾으십시오.",
            "마지막 보고서에서 결번(방해자)을 지목하십시오. 엔딩은 코어 복구 x 지목 결과로 갈립니다.",
        };
        foreach (string g in goals)
        {
            DrawString(_font, new Vector2(x + 6f, y), "- " + g, HorizontalAlignment.Left, w - 6f, ViewFont.S(12), Ink);
            y += 22f;
        }
    }

    private void DrawClose()
    {

        // X — 가리키면 밝아진다.
        bool xOn = _btnHover == SourcesCloseId;
        var c = SourcesClose;
        if (xOn) DrawRect(c, Err with { A = 0.18f });
        DrawRect(c, (xOn ? Err : Dim) with { A = xOn ? 0.95f : 0.6f }, false, xOn ? 1.8f : 1.2f);
        var a = c.Position + new Vector2(12f, 12f);
        var b = c.End - new Vector2(12f, 12f);
        DrawLine(a, b, xOn ? Err : Ink, 2f);
        DrawLine(new Vector2(b.X, a.Y), new Vector2(a.X, b.Y), xOn ? Err : Ink, 2f);
    }

    private void DrawSourcesList(float x, float y, float w)
    {
        string cat = "";
        foreach (var s in GameCredits.Sources)
        {
            if (s.Category != cat)
            {
                cat = s.Category;
                DrawString(_font, new Vector2(x, y), cat, HorizontalAlignment.Left, w, ViewFont.S(12), Mint with { A = 0.8f });
                y += 18f;
            }
            DrawString(_font, new Vector2(x + 10f, y), $"{s.Use}: {s.Work} - {s.Author}",
                HorizontalAlignment.Left, w - 10f, ViewFont.S(13), Ink);
            y += 17f;
            DrawString(_font, new Vector2(x + 22f, y), s.Url, HorizontalAlignment.Left, w - 22f, ViewFont.S(10), Dim);
            y += 23f;
        }
        y += 4f;
        DrawString(_font, new Vector2(x, y), "폰트", HorizontalAlignment.Left, w, ViewFont.S(12), Mint with { A = 0.8f });
        y += 18f;
        DrawString(_font, new Vector2(x + 10f, y), string.Join(", ", GameCredits.Fonts),
            HorizontalAlignment.Left, w - 10f, ViewFont.S(13), Ink);
        y += 26f;
        DrawString(_font, new Vector2(x, y), $"{GameCredits.Engine}   /   제작 {GameCredits.Maker}",
            HorizontalAlignment.Left, w, ViewFont.S(11), Dim);
    }

    private void Scanlines()
    {
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
    }
}
