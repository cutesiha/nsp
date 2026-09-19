using System;
using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 타이틀 화면 2 — 오른쪽 CRT 의 관리자 단말기.
//
// 기존 TitleOverlay(화면 위에 얹는 2D 메뉴)를 대신한다. 메뉴를 네모 버튼으로 두지 않고
// 시설 단말기의 명령 선택지처럼 '>' 커서로 고른다.
//
// 이 뷰는 스스로 입력을 받지 않는다 — TitleRoomDirector 가 카메라 레이캐스트로 좌표를
// 넘겨 주고(HoverAt/ItemAt), 키보드도 그쪽에서 MoveCursor/Selected 로 몰아 준다.
// 제어실 장비(전화기/센서/전력패널)에 마우스를 올려도 같은 항목이 켜지게 하기 위해서다.
//
// ★ 문구는 전부 이 파일 위쪽 상수 블록에 모여 있다.
public partial class TitleTerminalView : Control
{
    public static TitleTerminalView Instance { get; private set; }

    // ── 문구 ────────────────────────────────────────────────────────────
    private const string Org1 = "국가특수에너지연구원";
    private const string Org2 = "제7지하시설";
    private const string SystemName = "NIGHT SHIFT MANAGEMENT SYSTEM";
    private const string TitleKo = "야 간 근 무 지 침";
    private const string TitleEn = "NIGHT SHIFT PROTOCOL";
    private const string DocLine = "문서번호 NSP-07  ·  관계자 외 열람 금지";
    private const string TerminalTag = "ADMINISTRATOR TERMINAL";
    private const string WaitingTag = "[ 관리자 인증 대기 중 ]";
    private const string StandbyHead = "FACILITY CONTROL SYSTEM";
    private const string StandbyUser = "USER DETECTED";
    private const string StandbyKey = "[ PRESS ANY KEY ]";

    // 메뉴 항목 — Id 는 TitleRoomDirector 가 동작을 고를 때 쓰는 키다.
    public static readonly (string Id, string Label)[] MenuItems =
    {
        ("start", "근무 개시"),
        ("archive", "기록 열람"),
        ("config", "환경 설정"),
        ("quit", "시스템 종료"),
    };

    public enum Mode { Standby, Menu, Report }

    // ── 색 (무채색 + 포인트 1색. 빨강은 오류에만) ─────────────────────────
    private static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    private static readonly Color Err = new(0.92f, 0.28f, 0.24f);

    private static readonly Vector2 Canvas = new(800f, 600f);
    private const float ItemTop = 394f, ItemStep = 46f;
    private const float ItemLeft = 214f, ItemWidth = 372f;

    public Mode CurrentMode { get; private set; } = Mode.Standby;
    public int Cursor { get; private set; }

    private Font _font;
    private float _t;
    private bool _dirty = true;

    // Report 모드 — 한 줄씩 찍히는 출력. style 0=보통 1=성공 2=오류 3=흐림
    private readonly List<(string Text, int Style)> _report = new();
    private string _reportTitle = "";
    private double _typeClock;
    private int _typedChars;
    private (string Id, string Label)[] _reportItems = Array.Empty<(string, string)>();

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // --- 모드 전환 ---------------------------------------------------------

    public void ShowStandby()
    {
        CurrentMode = Mode.Standby;
        _report.Clear();
        QueueRedraw();
    }

    public void ShowMenu()
    {
        CurrentMode = Mode.Menu;
        Cursor = 0;
        _report.Clear();
        _reportItems = Array.Empty<(string, string)>();
        QueueRedraw();
    }

    // 명령을 실행했을 때의 단말기 출력(인증 / 기록 / 종료 확인)을 여는다.
    public void BeginReport(string title, params (string Id, string Label)[] items)
    {
        CurrentMode = Mode.Report;
        _reportTitle = title;
        _report.Clear();
        _reportItems = items ?? Array.Empty<(string, string)>();
        Cursor = 0;
        _typedChars = 0;
        _typeClock = 0;
        QueueRedraw();
    }

    // 한 줄 추가. 마지막 줄은 한 글자씩 찍힌다.
    public void PushLine(string text, int style = 0)
    {
        _report.Add((text ?? "", style));
        _typedChars = 0;
        _typeClock = 0;
        QueueRedraw();
    }

    public bool ReportTyping => _report.Count > 0 && _typedChars < _report[^1].Text.Length;

    // --- 커서 / 선택 -------------------------------------------------------

    private int ItemCount => CurrentMode switch
    {
        Mode.Menu => MenuItems.Length,
        Mode.Report => _reportItems.Length,
        _ => 0,
    };

    public string SelectedId
    {
        get
        {
            if (CurrentMode == Mode.Menu && Cursor >= 0 && Cursor < MenuItems.Length) return MenuItems[Cursor].Id;
            if (CurrentMode == Mode.Report && Cursor >= 0 && Cursor < _reportItems.Length) return _reportItems[Cursor].Id;
            return "";
        }
    }

    public bool MoveCursor(int delta)
    {
        int n = ItemCount;
        if (n <= 1) return false;
        int next = (Cursor + delta % n + n) % n;
        if (next == Cursor) return false;
        Cursor = next;
        QueueRedraw();
        return true;
    }

    // 논리 좌표가 어느 항목 위인가(없으면 "").
    public string ItemAt(Vector2 p)
    {
        int n = ItemCount;
        for (int i = 0; i < n; i++)
            if (ItemRect(i).HasPoint(p)) return IdOf(i);
        return "";
    }

    // 항목 위로 커서를 옮긴다. 옮겨졌으면 true(효과음용).
    public bool HoverId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        int n = ItemCount;
        for (int i = 0; i < n; i++)
        {
            if (IdOf(i) != id) continue;
            if (Cursor == i) return false;
            Cursor = i;
            QueueRedraw();
            return true;
        }
        return false;
    }

    private string IdOf(int i) => CurrentMode == Mode.Menu ? MenuItems[i].Id : _reportItems[i].Id;

    private Rect2 ItemRect(int i)
    {
        if (CurrentMode == Mode.Menu)
            return new Rect2(ItemLeft, ItemTop + i * ItemStep - 32f, ItemWidth, 42f);

        // Report 모드의 선택지는 아래쪽에 가로로 늘어놓는다.
        float w = 170f;
        float total = _reportItems.Length * w;
        float x = (Canvas.X - total) * 0.5f + i * w;
        return new Rect2(x, 494f, w, 44f);
    }

    // --- tick ---------------------------------------------------------------

    public override void _Process(double delta)
    {
        _t += (float)delta;

        if (ReportTyping)
        {
            _typeClock += delta;
            while (_typedChars < _report[^1].Text.Length && _typeClock >= 0.016)
            {
                _typeClock -= 0.016;
                _typedChars++;
            }
            _dirty = true;
        }

        // 깜빡이는 커서/안내는 초당 몇 번만 다시 그리면 충분하다.
        if (_dirty || Mathf.PosMod(_t, 0.25f) < delta) QueueRedraw();
        _dirty = false;
    }

    // --- 그리기 --------------------------------------------------------------

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.020f, 0.032f, 0.033f));
        // 창 테두리 + 위/아래 상태 줄.
        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Mint with { A = 0.28f }, false, 1.4f);
        DrawString(_font, new Vector2(30f, 38f), "FACILITY CONTROL SYSTEM",
            HorizontalAlignment.Left, 420f, ViewFont.S(12), Dim);
        DrawString(_font, new Vector2(Canvas.X - 150f, 38f), "NSP-07",
            HorizontalAlignment.Right, 120f, ViewFont.S(12), Dim);

        switch (CurrentMode)
        {
            case Mode.Standby: DrawStandby(); break;
            case Mode.Menu: DrawMenu(); break;
            default: DrawReport(); break;
        }

        // 주사선.
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
    }

    private void DrawStandby()
    {
        DrawString(_font, new Vector2(0f, 250f), StandbyHead, HorizontalAlignment.Center,
            Canvas.X, ViewFont.S(22), Dim);
        DrawString(_font, new Vector2(0f, 300f), StandbyUser, HorizontalAlignment.Center,
            Canvas.X, ViewFont.S(17), Mint);

        // 느리게 깜빡이는 안내.
        float a = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(_t * 2.6f));
        DrawString(_font, new Vector2(0f, 392f), StandbyKey, HorizontalAlignment.Center,
            Canvas.X, ViewFont.S(24), Ink with { A = a });
    }

    private void DrawMenu()
    {
        DrawString(_font, new Vector2(0f, 84f), Org1, HorizontalAlignment.Center, Canvas.X, ViewFont.S(17), Dim);
        DrawString(_font, new Vector2(0f, 108f), Org2, HorizontalAlignment.Center, Canvas.X, ViewFont.S(17), Dim);
        DrawString(_font, new Vector2(0f, 136f), SystemName, HorizontalAlignment.Center, Canvas.X, ViewFont.S(12), Dim);

        Rule(154f);

        // 제목은 화려한 로고가 아니라 시설 공문서 표제처럼.
        DrawString(_font, new Vector2(0f, 214f), TitleKo, HorizontalAlignment.Center, Canvas.X, ViewFont.S(40), Ink);
        DrawString(_font, new Vector2(0f, 246f), TitleEn, HorizontalAlignment.Center, Canvas.X, ViewFont.S(15), Mint with { A = 0.85f });
        DrawString(_font, new Vector2(0f, 274f), DocLine, HorizontalAlignment.Center, Canvas.X, ViewFont.S(12), Dim);

        Rule(294f);

        DrawString(_font, new Vector2(0f, 324f), TerminalTag, HorizontalAlignment.Center, Canvas.X, ViewFont.S(13), Dim);
        float a = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(_t * 2.2f));
        DrawString(_font, new Vector2(0f, 350f), WaitingTag, HorizontalAlignment.Center, Canvas.X,
            ViewFont.S(13), Mint with { A = a });

        for (int i = 0; i < MenuItems.Length; i++)
        {
            bool on = i == Cursor;
            float y = ItemTop + i * ItemStep;
            if (on)
            {
                DrawRect(ItemRect(i), Mint with { A = 0.10f });
                DrawString(_font, new Vector2(ItemLeft - 26f, y), ">", HorizontalAlignment.Left, 24f,
                    ViewFont.S(22), Mint);
            }
            DrawString(_font, new Vector2(ItemLeft + 14f, y), MenuItems[i].Label,
                HorizontalAlignment.Left, ItemWidth - 20f, ViewFont.S(22), on ? Ink : Dim);
        }
    }

    private void DrawReport()
    {
        if (!string.IsNullOrEmpty(_reportTitle))
        {
            DrawString(_font, new Vector2(60f, 110f), _reportTitle, HorizontalAlignment.Left,
                Canvas.X - 120f, ViewFont.S(20), Mint);
            Rule(128f);
        }

        float y = 176f;
        for (int i = 0; i < _report.Count; i++)
        {
            var (text, style) = _report[i];
            if (i == _report.Count - 1 && _typedChars < text.Length) text = text[.._typedChars];
            var col = style switch { 1 => Mint, 2 => Err, 3 => Dim, _ => Ink };
            int size = style == 1 ? ViewFont.S(24) : ViewFont.S(17);
            DrawString(_font, new Vector2(60f, y), text, HorizontalAlignment.Left,
                Canvas.X - 120f, size, col);
            y += style == 1 ? 44f : 32f;
        }

        // 마지막 줄 끝의 깜빡이는 커서.
        if (_report.Count > 0 && Mathf.PosMod(_t, 0.9f) < 0.45f)
            DrawRect(new Rect2(60f, y - 14f, 12f, 3f), Ink with { A = 0.7f });

        for (int i = 0; i < _reportItems.Length; i++)
        {
            bool on = i == Cursor;
            var r = ItemRect(i);
            if (on) DrawRect(r, Mint with { A = 0.12f });
            DrawRect(r, (on ? Mint : Dim) with { A = on ? 0.8f : 0.35f }, false, 1.2f);
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 29f),
                (on ? "> " : "  ") + _reportItems[i].Label, HorizontalAlignment.Center, r.Size.X,
                ViewFont.S(18), on ? Ink : Dim);
        }
    }

    private void Rule(float y) =>
        DrawRect(new Rect2(60f, y, Canvas.X - 120f, 1f), Mint with { A = 0.22f });
}
