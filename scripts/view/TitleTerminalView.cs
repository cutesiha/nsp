using System;
using System.Collections.Generic;
using Godot;
using NSP.Core;

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
    private const string TitleKo = "야 간 근 무 지 침";
    private const string TitleEn = "NIGHT SHIFT PROTOCOL";
    private const string WaitingTag = "[ 관리자 인증 대기 중 ]";
    private const string StandbyHead = "FACILITY CONTROL SYSTEM";
    private const string StandbyUser = "USER DETECTED";
    private const string StandbyKey = "[ PRESS ANY KEY ]";
    private const string FailedRecordTag = "복구 실패 기록이 존재합니다.";
    // 진엔딩을 본 뒤에는 "근무 개시" 대신.
    private const string NewShiftLabel = "새 근무 시작";

    // 메뉴 항목 — Id 는 TitleRoomDirector 가 동작을 고를 때 쓰는 키다.
    public static readonly (string Id, string Label)[] MenuItems =
    {
        ("start", "근무 개시"),
        ("archive", "기록 열람"),
        ("config", "환경 설정"),
        ("quit", "시스템 종료"),
    };

    public enum Mode { Standby, Menu, Report }

    // 메뉴 문구(진엔딩을 본 뒤에는 근무 개시 → 새 근무 시작).
    public static string LabelOf(int i) =>
        MenuItems[i].Id == "start" && EndingState.Last == EndingState.Kind.True ? NewShiftLabel : MenuItems[i].Label;

    // ── 색 (무채색 + 포인트 1색. 빨강은 오류에만) ─────────────────────────
    private static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    private static readonly Color Err = new(0.92f, 0.28f, 0.24f);

    private static readonly Vector2 Canvas = new(800f, 600f);
    private const float ItemTop = 398f, ItemStep = 50f;
    private const float ItemLeft = 214f, ItemWidth = 372f;

    // ── 메뉴가 뜨는 연출 ──────────────────────────────────────────────
    // 프로그램이 실행되듯 표제가 한 글자씩 찍히고, 그 아래로 메뉴가 한 줄씩 내려온다.
    private const float RevealOrg2At = 0.14f;      // 시설명 둘째 줄
    private const float RevealRuleAt = 0.28f;      // 구분선
    private const float RevealTitleAt = 0.42f;     // 표제 타이핑 시작
    private const float TitleCharTime = 0.055f;    // 표제 한 글자
    private const float RevealTailGap = 0.16f;     // 표제가 다 찍힌 뒤 영문 표제까지
    private const float RevealItemGap = 0.17f;     // 메뉴 한 줄 간격

    private static float TitleTypedAt => RevealTitleAt + TitleKo.Length * TitleCharTime;
    private static float RevealSubAt => TitleTypedAt + RevealTailGap;
    private static float RevealWaitAt => RevealSubAt + 0.16f;
    private static float RevealItemsAt => RevealSubAt + 0.32f;
    private static float RevealEndAt => RevealItemsAt + (MenuItems.Length - 1) * RevealItemGap + 0.20f;

    // 음수면 연출 없이 처음부터 완성된 상태로 그린다(보고 화면에서 돌아올 때).
    private double _reveal = -1;

    public bool MenuRevealDone => _reveal < 0 || _reveal >= RevealEndAt;

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
    // 한 글자에 걸리는 시간. 부팅 콘솔은 더 빠르게 찍는다.
    private double _typeSpeed = 0.016;
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

    // animate: 전원을 막 넣었을 때만 true — 표제와 메뉴가 순서대로 뜬다.
    public void ShowMenu(bool animate = false)
    {
        CurrentMode = Mode.Menu;
        _reveal = animate ? 0.0 : -1.0;
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
            return new Rect2(ItemLeft, ItemTop + i * ItemStep - 36f, ItemWidth, 46f);

        // Report 모드의 선택지는 아래쪽에 가로로 늘어놓는다.
        float w = 196f;
        float total = _reportItems.Length * w;
        float x = (Canvas.X - total) * 0.5f + i * w;
        return new Rect2(x, 488f, w, 52f);
    }

    // --- tick ---------------------------------------------------------------

    public override void _Process(double delta)
    {
        _t += (float)delta;
        TickReveal(delta);

        if (ReportTyping)
        {
            _typeClock += delta;
            while (_typedChars < _report[^1].Text.Length && _typeClock >= _typeSpeed)
            {
                _typeClock -= _typeSpeed;
                _typedChars++;
                // 오래된 단말기의 타자 소리(승계 콘솔과 같은 규약).
                char ch = _report[^1].Text[_typedChars - 1];
                if (ch != ' ' && _typedChars % 2 == 0)
                    Sfx.Instance?.Play("key_single", -19f, (float)GD.RandRange(0.92, 1.12));
            }
            _dirty = true;
        }

        // 깜빡이는 커서/안내는 초당 몇 번만 다시 그리면 충분하다.
        if (_dirty || Mathf.PosMod(_t, 0.25f) < delta) QueueRedraw();
        _dirty = false;
    }

    // --- 그리기 --------------------------------------------------------------

    // 메뉴가 뜨는 동안 단계마다 한 번씩 소리를 낸다.
    private void TickReveal(double delta)
    {
        if (CurrentMode != Mode.Menu || MenuRevealDone) return;
        double before = _reveal;
        _reveal += delta;
        _dirty = true;

        // 표제가 한 글자씩 찍히는 타자 소리.
        int c0 = TypedTitleChars(before), c1 = TypedTitleChars(_reveal);
        for (int i = c0; i < c1; i++)
            if (!char.IsWhiteSpace(TitleKo[i]))
                Sfx.Instance?.Play("key_single", -17f, (float)GD.RandRange(0.94, 1.10));

        Cue(before, RevealOrg2At, "tick", -21f);
        Cue(before, RevealSubAt, "window_open", -10f);   // 표제가 자리를 잡는 순간
        for (int i = 0; i < MenuItems.Length; i++)
            Cue(before, RevealItemsAt + i * RevealItemGap, "tick", -15f);
    }

    private void Cue(double before, double at, string key, float db)
    {
        if (before < at && _reveal >= at) Sfx.Instance?.Play(key, db);
    }

    private static int TypedTitleChars(double t)
    {
        if (t < RevealTitleAt) return 0;
        return Mathf.Clamp((int)((t - RevealTitleAt) / TitleCharTime), 0, TitleKo.Length);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.020f, 0.032f, 0.033f));
        // 창 테두리 + 위/아래 상태 줄.
        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Mint with { A = 0.28f }, false, 1.4f);
        DrawString(_font, new Vector2(30f, 38f), "FACILITY CONTROL SYSTEM",
            HorizontalAlignment.Left, 420f, ViewFont.S(12), Dim);
        // 결말의 흔적 — 오른쪽 위 작은 상태 표시.
        var mark = EndingState.Last;
        string corner = mark switch
        {
            EndingState.Kind.Bad => "CONTAINMENT FAILED",
            EndingState.Kind.True => "CORE 100% · STABLE",
            _ => "NSP-07",
        };
        DrawString(_font, new Vector2(Canvas.X - 330f, 38f), corner, HorizontalAlignment.Right, 300f, ViewFont.S(12),
            mark == EndingState.Kind.Bad ? Err : mark == EndingState.Kind.True ? Mint : Dim);

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
        // 연출이 끝났거나(음수) 진행 중이거나 — 지난 시간으로 어디까지 그릴지 정한다.
        double t = _reveal < 0 ? 9999.0 : _reveal;

        DrawString(_font, new Vector2(0f, 96f), Org1, HorizontalAlignment.Center, Canvas.X, ViewFont.S(17), Dim);
        if (t >= RevealOrg2At)
            DrawString(_font, new Vector2(0f, 122f), Org2, HorizontalAlignment.Center, Canvas.X, ViewFont.S(17), Dim);
        if (t >= RevealRuleAt) Rule(150f);

        // 표제는 화려한 로고가 아니라 시설 공문서 표제처럼. 한 글자씩 찍힌다.
        int typed = _reveal < 0 ? TitleKo.Length : TypedTitleChars(t);
        int titlePx = ViewFont.S(52);
        float titleW = _font.GetStringSize(TitleKo, HorizontalAlignment.Left, -1, titlePx).X;
        float titleX = (Canvas.X - titleW) * 0.5f;
        if (typed > 0)
            DrawString(_font, new Vector2(titleX, 224f), TitleKo[..typed],
                HorizontalAlignment.Left, -1, titlePx, Ink);
        // 찍히는 동안 커서가 따라간다.
        if (typed < TitleKo.Length && t >= RevealTitleAt)
        {
            float cx = titleX + _font.GetStringSize(TitleKo[..typed], HorizontalAlignment.Left, -1, titlePx).X;
            DrawRect(new Rect2(cx + 2f, 200f, 4f, 28f), Ink with { A = 0.8f });
        }

        if (t >= RevealSubAt)
        {
            DrawString(_font, new Vector2(0f, 262f), TitleEn, HorizontalAlignment.Center, Canvas.X,
                ViewFont.S(15), Mint with { A = 0.85f });
            Rule(292f);
        }

        if (t >= RevealWaitAt)
        {
            float a = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(_t * 2.2f));
            // 실패의 흔적 — 대문짝만한 BAD END 대신, 시설이 실패를 기억하고 있다는 한 줄만.
            if (EndingState.Last == EndingState.Kind.Bad)
                DrawString(_font, new Vector2(0f, 338f), FailedRecordTag, HorizontalAlignment.Center, Canvas.X,
                    ViewFont.S(14), Err with { A = 0.55f + 0.3f * a });
            else
                DrawString(_font, new Vector2(0f, 338f), WaitingTag, HorizontalAlignment.Center, Canvas.X,
                    ViewFont.S(16), Mint with { A = a });
        }

        for (int i = 0; i < MenuItems.Length; i++)
        {
            if (t < RevealItemsAt + i * RevealItemGap) continue;
            bool on = i == Cursor;
            float y = ItemTop + i * ItemStep;
            if (on)
            {
                DrawRect(ItemRect(i), Mint with { A = 0.10f });
                DrawString(_font, new Vector2(ItemLeft - 30f, y), ">", HorizontalAlignment.Left, 28f,
                    ViewFont.S(27), Mint);
            }
            DrawString(_font, new Vector2(ItemLeft + 14f, y), LabelOf(i),
                HorizontalAlignment.Left, ItemWidth - 20f, ViewFont.S(27), on ? Ink : Dim);
        }
    }

    private void DrawReport()
    {
        if (!string.IsNullOrEmpty(_reportTitle))
        {
            DrawString(_font, new Vector2(60f, 110f), _reportTitle, HorizontalAlignment.Left,
                Canvas.X - 120f, ViewFont.S(24), Mint);
            Rule(128f);
        }

        float y = 176f;
        for (int i = 0; i < _report.Count; i++)
        {
            var (text, style) = _report[i];
            if (i == _report.Count - 1 && _typedChars < text.Length) text = text[.._typedChars];
            var col = style switch { 1 => Mint, 2 => Err, 3 => Dim, _ => Ink };
            int size = style == 1 ? ViewFont.S(30) : ViewFont.S(21);
            DrawString(_font, new Vector2(60f, y), text, HorizontalAlignment.Left,
                Canvas.X - 120f, size, col);
            y += style == 1 ? 52f : 38f;
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
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 34f),
                (on ? "> " : "  ") + _reportItems[i].Label, HorizontalAlignment.Center, r.Size.X,
                ViewFont.S(22), on ? Ink : Dim);
        }
    }

    private void Rule(float y) =>
        DrawRect(new Rect2(60f, y, Canvas.X - 120f, 1f), Mint with { A = 0.22f });
}
