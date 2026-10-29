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
// 제어실 장비(전화기/관리자 패드/전력패널)에 마우스를 올려도 같은 항목이 켜지게 하기 위해서다.
//
// ★ 문구는 전부 이 파일 위쪽 상수 블록에 모여 있다.
public partial class TitleTerminalView : Control
{
    public static TitleTerminalView Instance { get; private set; }

    // ── 문구 ────────────────────────────────────────────────────────────
    private const string Org1 = "국가특수에너지연구원";
    private const string Org2 = "제7지하시설";
    // 표제의 글자 수 · 찍히는 박자는 이 문자열이 정한다(공백도 한 박자). 화면에는 아래 표제 그림이 뜬다.
    private const string TitleKo = "야 간 근 무 지 침";

    // ── 표제 그림 ────────────────────────────────────────────────────────
    // 「야간근무지침」 로고(사용자 제공, 2026-10-29)를 한 글자씩 잘라 둔 것(assets/ui/title/).
    // X = 원본 로고(1283×211)에서 그 조각의 왼쪽 끝 — 글자 사이 간격을 원본 그대로 되살린다.
    // 영문 표제(NIGHT SHIFT PROTOCOL)는 뺐다 — 그 자리까지 표제가 크게 차지한다.
    private static readonly (string File, float X)[] TitleGlyphs =
    {
        ("title_ya", 37f), ("title_gan", 241f), ("title_geun", 437f),
        ("title_mu", 639f), ("title_ji", 841f), ("title_chim", 1043f),
    };
    private const float TitleSrcLeft = 37f, TitleSrcRight = 1249f;   // 로고 전체의 가로 범위(원본 픽셀)
    private const float TitleWidth = 640f;                           // 화면에서 표제가 차지하는 폭
    private const float TitleCenterY = 221f;                         // 위 · 아래 구분선(150 · 292) 사이 가운데
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

    public enum Mode { Standby, Menu, Report, ModeSelect }

    // ── 근무 유형 선택 ──────────────────────────────────────────────────
    // 「근무 개시」를 고르면 뜨는 화면. 세 줄짜리 단말기 선택지이고, 잠긴 항목은
    // 흐리게 + LOCKED 로 표시만 하고 고를 수는 있어도 실행되지 않는다(거부음만 난다).
    private const string ModeHead = "근무 유형을 선택하십시오.";
    private const string ModeSub = "SELECT SHIFT PROTOCOL";
    private const string LockedTag = "LOCKED";

    // 하드 모드 칸은 **뺐다.** 규칙이 없는 채로 LOCKED 만 띄워 두면, 고를 수 없는
    // 줄 하나가 화면에서 자리만 차지하고 "언젠가 열리나?" 하는 기대만 남긴다.
    public static (string Id, GameMode Mode, string Label, string Sub)[] ModeItems =>
        new[]
        {
            ("mode_comp", GameMode.Competition, "단기 근무",
                $"{GameModes.DaysFor(GameMode.Competition)} DAYS  /  SHORT PROTOCOL"),
            ("mode_std", GameMode.Standard, "정규 근무",
                $"{GameModes.DaysFor(GameMode.Standard)} DAYS  /  STANDARD PROTOCOL"),
        };

    private const float ModeTop = 262f, ModeStep = 96f;
    private const float ModeLeft = 150f, ModeWidth = 500f, ModeHeight = 82f;

    // 근무 유형 선택 화면의 [뒤로] — 오른쪽 아래. 세 유형 뒤에 한 칸 더 붙는
    // 항목이라 커서 이동 · 호버 · 클릭이 유형 칸과 똑같이 동작한다.
    public const string ModeBackId = "mode_back";
    private const string ModeBackLabel = "뒤로";
    private static Rect2 ModeBackRect() => new(Canvas.X - 190f, 528f, 140f, 44f);

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
    private const float RevealTailGap = 0.16f;     // 표제가 다 찍힌 뒤 아래 구분선까지
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

    // 표제 조각 그림(TitleGlyphs 순서). 하나라도 없으면 예전처럼 글자로 그린다.
    private Texture2D[] _titleTex;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        _titleTex = new Texture2D[TitleGlyphs.Length];
        for (int i = 0; i < TitleGlyphs.Length; i++)
        {
            string path = $"res://assets/ui/title/{TitleGlyphs[i].File}.png";
            _titleTex[i] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            if (_titleTex[i] == null) { _titleTex = null; break; }
        }
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

    // 근무 유형 선택 화면. 커서는 처음 고를 수 있는 항목에 선다.
    public void ShowModeSelect()
    {
        CurrentMode = Mode.ModeSelect;
        _reveal = -1.0;
        _report.Clear();
        _reportItems = Array.Empty<(string, string)>();
        var items = ModeItems;
        Cursor = 0;
        for (int i = 0; i < items.Length; i++)
            if (GameModes.IsPlayable(items[i].Mode)) { Cursor = i; break; }
        QueueRedraw();
    }

    // 지금 커서가 가리키는 근무 유형.
    public GameMode SelectedMode =>
        CurrentMode == Mode.ModeSelect && Cursor >= 0 && Cursor < ModeItems.Length
            ? ModeItems[Cursor].Mode
            : GameMode.Standard;

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
        Mode.ModeSelect => ModeItems.Length + 1,   // +1 = [뒤로]
        _ => 0,
    };

    public string SelectedId
    {
        get
        {
            if (CurrentMode == Mode.Menu && Cursor >= 0 && Cursor < MenuItems.Length) return MenuItems[Cursor].Id;
            if (CurrentMode == Mode.Report && Cursor >= 0 && Cursor < _reportItems.Length) return _reportItems[Cursor].Id;
            if (CurrentMode == Mode.ModeSelect)
            {
                if (Cursor >= 0 && Cursor < ModeItems.Length) return ModeItems[Cursor].Id;
                if (Cursor == ModeItems.Length) return ModeBackId;
            }
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

    private string IdOf(int i) => CurrentMode switch
    {
        Mode.Menu => MenuItems[i].Id,
        Mode.ModeSelect => i < ModeItems.Length ? ModeItems[i].Id : ModeBackId,
        _ => _reportItems[i].Id,
    };

    private Rect2 ItemRect(int i)
    {
        if (CurrentMode == Mode.ModeSelect)
            return i < ModeItems.Length
                ? new Rect2(ModeLeft, ModeTop + i * ModeStep, ModeWidth, ModeHeight)
                : ModeBackRect();

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
            case Mode.ModeSelect: DrawModeSelect(); break;
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

        // 표제 — 한 글자씩 찍힌다(콘솔에 입력되듯). 박자는 예전 글자 표제와 같다.
        int typed = _reveal < 0 ? TitleKo.Length : TypedTitleChars(t);
        if (_titleTex != null) DrawTitleGlyphs(typed, t);
        else DrawTitleText(typed, t);

        if (t >= RevealSubAt) Rule(292f);

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

    // 표제 그림 — 조각마다 원본 로고의 자리 그대로 놓는다. typed = 찍힌 글자 수(TitleKo 기준, 공백 포함).
    // TitleKo 의 짝수 칸(0 · 2 · 4 …)이 글자 하나씩이다 — 공백 박자는 다음 글자까지의 쉼이다.
    private void DrawTitleGlyphs(int typed, double t)
    {
        float scale = TitleWidth / (TitleSrcRight - TitleSrcLeft);
        float left = (Canvas.X - TitleWidth) * 0.5f;
        float h = _titleTex[0].GetHeight() * scale;
        float top = TitleCenterY - h * 0.5f;
        // 그림 색을 단말기 글자색 쪽으로 아주 조금 눌러 준다(흰 그림이 화면에서 혼자 튀지 않게).
        var tint = new Color(0.93f, 0.98f, 0.97f);
        float cursorX = left;
        for (int i = 0; i < _titleTex.Length; i++)
        {
            if (typed <= i * 2) break;
            var tex = _titleTex[i];
            float x = left + (TitleGlyphs[i].X - TitleSrcLeft) * scale;
            DrawTextureRect(tex, new Rect2(x, top, tex.GetWidth() * scale, h), false, tint);
            cursorX = x + tex.GetWidth() * scale;
        }
        // 찍히는 동안 커서가 따라간다.
        if (typed < TitleKo.Length && t >= RevealTitleAt)
            DrawRect(new Rect2(cursorX + 4f, top + h * 0.14f, 6f, h * 0.72f), Ink with { A = 0.8f });
    }

    // 그림을 못 불러왔을 때만 — 예전 글자 표제.
    private void DrawTitleText(int typed, double t)
    {
        int titlePx = ViewFont.S(52);
        float titleW = _font.GetStringSize(TitleKo, HorizontalAlignment.Left, -1, titlePx).X;
        float titleX = (Canvas.X - titleW) * 0.5f;
        if (typed > 0)
            DrawString(_font, new Vector2(titleX, 236f), TitleKo[..typed],
                HorizontalAlignment.Left, -1, titlePx, Ink);
        if (typed < TitleKo.Length && t >= RevealTitleAt)
        {
            float cx = titleX + _font.GetStringSize(TitleKo[..typed], HorizontalAlignment.Left, -1, titlePx).X;
            DrawRect(new Rect2(cx + 2f, 212f, 4f, 28f), Ink with { A = 0.8f });
        }
    }

    // 근무 유형 선택 — 네모난 단말기 선택지 세 줄. 둥근 버튼도, 네온도 쓰지 않는다.
    private void DrawModeSelect()
    {
        DrawString(_font, new Vector2(0f, 150f), ModeHead, HorizontalAlignment.Center,
            Canvas.X, ViewFont.S(26), Ink);
        DrawString(_font, new Vector2(0f, 178f), ModeSub, HorizontalAlignment.Center,
            Canvas.X, ViewFont.S(13), Mint with { A = 0.8f });
        Rule(206f);

        var items = ModeItems;
        for (int i = 0; i < items.Length; i++)
        {
            var (_, mode, label, sub) = items[i];
            bool open = GameModes.IsPlayable(mode);
            bool on = i == Cursor;
            var r = ItemRect(i);

            if (on) DrawRect(r, (open ? Mint : Err) with { A = 0.10f });
            DrawRect(r, (open ? Mint : Dim) with { A = on ? 0.85f : 0.35f }, false, on ? 1.8f : 1.1f);
            if (on)
                DrawString(_font, new Vector2(r.Position.X - 30f, r.Position.Y + 38f), ">",
                    HorizontalAlignment.Left, 28f, ViewFont.S(26), open ? Mint : Err);

            var nameCol = open ? (on ? Ink : Dim) : Dim with { A = 0.55f };
            DrawString(_font, new Vector2(r.Position.X + 26f, r.Position.Y + 38f), label,
                HorizontalAlignment.Left, r.Size.X - 150f, ViewFont.S(25), nameCol);
            DrawString(_font, new Vector2(r.Position.X + 28f, r.Position.Y + 64f), sub,
                HorizontalAlignment.Left, r.Size.X - 60f, ViewFont.S(12),
                open ? Dim : Dim with { A = 0.45f });

            if (!open)
                DrawString(_font, new Vector2(r.Position.X + r.Size.X - 130f, r.Position.Y + 36f), LockedTag,
                    HorizontalAlignment.Right, 118f, ViewFont.S(15), Err with { A = 0.70f });
        }

        // [뒤로] — 오른쪽 아래. 가리키면 칸이 차고 테두리가 밝아진다(호버 효과).
        bool backOn = Cursor == items.Length;
        var br = ModeBackRect();
        if (backOn) DrawRect(br, Mint with { A = 0.14f });
        DrawRect(br, Mint with { A = backOn ? 0.85f : 0.32f }, false, backOn ? 1.8f : 1.1f);
        DrawString(_font, new Vector2(br.Position.X, br.Position.Y + 30f),
            (backOn ? "< " : "") + ModeBackLabel, HorizontalAlignment.Center, br.Size.X,
            ViewFont.S(19), backOn ? Ink : Dim);
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
