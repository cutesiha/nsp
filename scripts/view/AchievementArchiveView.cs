using Godot;
using NSP.Core;

namespace NSP.View;

// 도전과제 기록실 — 타이틀의 「기록 열람」과 일시정지 창의 「도전과제」가 **같은 이 화면**을 쓴다.
//
// 왼쪽 CRT 의 관리자 단말기(TitleTerminalView)와 같은 캔버스(800×600) 위에 얹히는 전용
// 화면이다. 단말기의 글꼴 · 색 · 주사선 규약을 그대로 쓴다 — 여기만 다른 UI 로 보이면
// 시설 단말기라는 감각이 깨진다.
//
// 30개를 페이지로 끊지 않고 **한 줄씩 이어서 스크롤**한다. 목록은 ClipContents 가 켜진
// 자식(AchievementListView)이 그리고, 이 Control 은 머리글 · 진행률 · 스크롤바만 맡는다.
// (_Draw 는 자기 사각형 밖도 그려 버리므로, 잘라내려면 자식 Control 이 필요하다.)
//
// 스스로 입력을 받지 않는다. 띄운 쪽이 좌표와 키를 넘겨 준다 —
//   타이틀 : TitleRoomDirector 가 카메라 레이캐스트로 CRT 위 좌표를 계산해서
//   일시정지: AchievementPanel 이 마우스 로컬 좌표를 그대로
// 그래서 기록을 넘기다가 「근무 개시」가 실수로 실행되지 않는다.
public partial class AchievementArchiveView : Control
{
    public static AchievementArchiveView Instance { get; private set; }

    // ── 문구 ────────────────────────────────────────────────────────
    private const string Head = "도전과제 기록";
    private const string Sub = "관리자 활동 기록  /  ACHIEVEMENT ARCHIVE";
    private const string BackLabel = "뒤로";

    // ── 색 (TitleTerminalView 와 같은 팔레트) ─────────────────────────
    public static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    public static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    public static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    public static readonly Color Deep = new(0.26f, 0.32f, 0.33f);

    public static readonly Vector2 Canvas = new(800f, 600f);

    // ── 배치 ────────────────────────────────────────────────────────
    public const float RowStep = 78f;
    private const float ListLeft = 56f;
    private const float ListTop = 132f;
    private const float ListWidth = 688f;
    private const float ListHeight = 398f;
    private const float BarWidth = 5f;
    private const float FootY = 556f;

    private Font _font;
    private float _t;
    private AchievementListView _list;

    // 창 안에 끼워 넣은 상태인가. 바깥 테두리 · 모서리 표제 · [뒤로] 버튼을 생략한다
    // (일시정지 창은 자기 테두리와 ✕ 를 이미 가지고 있다).
    public bool Embedded { get; set; }

    public bool IsOpen { get; private set; }

    // 지금 고른 줄(0~29)과 픽셀 스크롤 위치.
    public int Cursor { get; private set; }
    public float Scroll { get; private set; }

    public static float ContentHeight => Achievements.Total * RowStep;
    public static float MaxScroll => Mathf.Max(0f, ContentHeight - ListHeight);

    public override void _Ready()
    {
        // 타이틀 CRT 의 것 하나만 Instance 로 둔다. 일시정지 창이 띄우는 사본은
        // 전역 참조를 빼앗지 않는다 — 타이틀 입력이 엉뚱한 사본으로 가면 안 된다.
        Instance ??= this;
        _font = ViewFont.Default;
        Size = Canvas;
        Position = Vector2.Zero;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        _list = new AchievementListView
        {
            Owner2 = this,
            Position = new Vector2(ListLeft, ListTop),
            Size = new Vector2(ListWidth, ListHeight),
            ClipContents = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_list);
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // ── 열기 / 닫기 ─────────────────────────────────────────────────

    public void Open()
    {
        IsOpen = true;
        Cursor = 0;
        Scroll = 0f;
        Visible = true;
        SetProcess(true);
        Redraw();
    }

    public void Close()
    {
        IsOpen = false;
        Visible = false;
        SetProcess(false);
    }

    private void Redraw()
    {
        QueueRedraw();
        _list?.QueueRedraw();
    }

    // ── 이동 ────────────────────────────────────────────────────────

    // 한 줄 위/아래. 고른 줄이 화면 밖으로 나가면 그만큼만 따라 스크롤한다.
    public bool MoveCursor(int delta)
    {
        if (!IsOpen) return false;
        int next = Mathf.Clamp(Cursor + delta, 0, Achievements.Total - 1);
        if (next == Cursor) return false;
        Cursor = next;
        ScrollIntoView();
        Redraw();
        return true;
    }

    // 픽셀 단위 스크롤(마우스 휠 · 드래그). 커서는 따라오지 않는다.
    public bool ScrollBy(float pixels)
    {
        if (!IsOpen) return false;
        float next = Mathf.Clamp(Scroll + pixels, 0f, MaxScroll);
        if (Mathf.IsEqualApprox(next, Scroll)) return false;
        Scroll = next;
        Redraw();
        return true;
    }

    // 한 화면씩(PageUp/PageDown). 커서도 같이 옮긴다.
    public bool MovePage(int delta)
    {
        int rows = Mathf.Max(1, Mathf.FloorToInt(ListHeight / RowStep));
        return MoveCursor(delta * rows);
    }

    private void ScrollIntoView()
    {
        float top = Cursor * RowStep;
        float bottom = top + RowStep;
        if (top < Scroll) Scroll = top;
        else if (bottom > Scroll + ListHeight) Scroll = bottom - ListHeight;
        Scroll = Mathf.Clamp(Scroll, 0f, MaxScroll);
    }

    // ── 마우스 ──────────────────────────────────────────────────────

    // 논리 좌표가 어느 조작 위인가. "back" / "" (없음).
    public string ItemAt(Vector2 p)
    {
        if (!IsOpen || Embedded) return "";
        return BackRect().HasPoint(p) ? "back" : "";
    }

    // 목록 위에 마우스를 올리면 그 줄로 커서가 간다. 옮겨졌으면 true(효과음용).
    public bool HoverAt(Vector2 p)
    {
        if (!IsOpen) return false;
        if (!ListRect().HasPoint(p)) return false;
        int row = Mathf.FloorToInt((p.Y - ListTop + Scroll) / RowStep);
        if (row < 0 || row >= Achievements.Total || row == Cursor) return false;
        Cursor = row;
        Redraw();
        return true;
    }

    // 목록 안을 가리키고 있는가(휠을 스크롤로 쓸지 판단).
    public bool OverList(Vector2 p) => ListRect().HasPoint(p);

    private static Rect2 ListRect() => new(ListLeft, ListTop, ListWidth, ListHeight);
    private static Rect2 BackRect() => new(Canvas.X - ListLeft - 128f, FootY - 24f, 128f, 36f);

    // ── 그리기 ──────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // 깜빡이는 것은 없지만 해금이 실시간으로 늘 수 있다 — 초당 네 번만 다시 그린다.
        if (Mathf.PosMod(_t, 0.25f) < delta) Redraw();
    }

    public override void _Draw()
    {
        var mgr = AchievementManager.Instance;
        int done = mgr?.GetUnlockedCount() ?? 0;
        int total = Achievements.Total;

        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.020f, 0.032f, 0.033f));
        if (!Embedded)
        {
            DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Mint with { A = 0.28f }, false, 1.4f);
            DrawString(_font, new Vector2(30f, 38f), "FACILITY CONTROL SYSTEM",
                HorizontalAlignment.Left, 420f, ViewFont.S(12), Dim);
        }

        // 표제 + 보조 문구.
        DrawString(_font, new Vector2(ListLeft, 70f), Head, HorizontalAlignment.Left, 420f, ViewFont.S(26), Ink);
        DrawString(_font, new Vector2(ListLeft, 92f), Sub, HorizontalAlignment.Left, 520f, ViewFont.S(12), Dim);

        // 진행률 — 숫자와 막대. 막대는 실제 비율만큼만 찬다.
        DrawString(_font, new Vector2(Canvas.X - ListLeft - 220f, 76f), $"달성 {done:00} / {total:00}",
            HorizontalAlignment.Right, 220f, ViewFont.S(18), Mint);
        float barY = 110f, barW = Canvas.X - ListLeft * 2f;
        DrawRect(new Rect2(ListLeft, barY, barW, 7f), Deep with { A = 0.55f });
        float ratio = total <= 0 ? 0f : (float)done / total;
        if (ratio > 0f) DrawRect(new Rect2(ListLeft, barY, barW * ratio, 7f), Mint with { A = 0.80f });

        // 목록 테두리(아래위 경계) — 어디까지가 스크롤 영역인지 보이게.
        var lr = ListRect();
        DrawRect(new Rect2(lr.Position.X, lr.Position.Y - 6f, lr.Size.X, 1f), Mint with { A = 0.22f });
        DrawRect(new Rect2(lr.Position.X, lr.Position.Y + lr.Size.Y + 5f, lr.Size.X, 1f), Mint with { A = 0.22f });

        DrawScrollBar(lr);

        // 아래 조작줄.
        if (!Embedded) Button(BackRect(), BackLabel);
        DrawString(_font, new Vector2(0f, FootY + 26f),
            Embedded ? "↑ ↓ 이동   휠 스크롤   ESC 닫기" : "↑ ↓ 이동   휠 스크롤   ESC 뒤로",
            HorizontalAlignment.Center, Canvas.X, ViewFont.S(11), Deep);

        // 주사선.
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
    }

    // 오른쪽 가장자리의 가느다란 막대. 지금 어디쯤인지만 알리면 된다.
    private void DrawScrollBar(Rect2 lr)
    {
        if (MaxScroll <= 0f) return;
        float x = lr.Position.X + lr.Size.X - BarWidth;
        DrawRect(new Rect2(x, lr.Position.Y, BarWidth, lr.Size.Y), Deep with { A = 0.30f });
        float thumb = Mathf.Max(28f, lr.Size.Y * (ListHeight / ContentHeight));
        float y = lr.Position.Y + (lr.Size.Y - thumb) * (Scroll / MaxScroll);
        DrawRect(new Rect2(x, y, BarWidth, thumb), Mint with { A = 0.55f });
    }

    private void Button(Rect2 r, string label)
    {
        DrawRect(r, Mint with { A = 0.38f }, false, 1.1f);
        DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 25f), label,
            HorizontalAlignment.Center, r.Size.X, ViewFont.S(15), Ink);
    }
}

// 스크롤되는 목록만 그리는 자식. ClipContents 가 켜져 있어 사각형 밖으로 넘치지 않는다.
public partial class AchievementListView : Control
{
    public AchievementArchiveView Owner2;

    private const float IconBox = 50f;
    private Font _font;

    public override void _Ready() => _font = ViewFont.Default;

    public override void _Draw()
    {
        if (Owner2 == null) return;
        var mgr = AchievementManager.Instance;
        float step = AchievementArchiveView.RowStep;
        float scroll = Owner2.Scroll;

        int first = Mathf.Max(0, Mathf.FloorToInt(scroll / step));
        int last = Mathf.Min(Achievements.Total - 1, Mathf.CeilToInt((scroll + Size.Y) / step));

        for (int i = first; i <= last; i++)
        {
            var def = Achievements.All[i];
            DrawRow(new Rect2(0f, i * step - scroll, Size.X, step - 10f),
                def, mgr?.IsUnlocked(def.Id) ?? false, i == Owner2.Cursor);
        }
    }

    private void DrawRow(Rect2 r, AchievementDefinition def, bool unlocked, bool on)
    {
        var mint = AchievementArchiveView.Mint;
        var ink = AchievementArchiveView.Ink;
        var dim = AchievementArchiveView.Dim;
        var deep = AchievementArchiveView.Deep;

        if (on) DrawRect(r, mint with { A = 0.09f });
        DrawRect(new Rect2(r.Position.X, r.Position.Y + r.Size.Y, r.Size.X, 1f), mint with { A = 0.12f });

        // 숨김 업적은 달성 전까지 이름도 설명도 내놓지 않는다.
        bool masked = def.Hidden && !unlocked;
        string name = masked ? "???" : def.Name;
        string desc = masked ? "아직 밝혀지지 않은 기록입니다." : def.Condition;

        // 아이콘 칸 — 달성하면 분류 픽토그램, 미달성이면 잠금 표시.
        float bx = r.Position.X + 8f, by = r.Position.Y + (r.Size.Y - IconBox) * 0.5f;
        DrawRect(new Rect2(bx, by, IconBox, IconBox),
            unlocked ? new Color(0.07f, 0.12f, 0.12f) : new Color(0.035f, 0.045f, 0.048f));
        DrawRect(new Rect2(bx, by, IconBox, IconBox),
            (unlocked ? mint : deep) with { A = unlocked ? 0.55f : 0.35f }, false, 1.2f);
        // 그림은 도전과제마다 다르다(AchievementIcons). 미달성은 자물쇠, 숨김 과제는 물음표라
        // 달성 전에 내용이 새지 않는다.
        NSP.Ui.AchievementIcons.Draw(this, def,
            new Rect2(bx + IconBox * 0.16f, by + IconBox * 0.16f, IconBox * 0.68f, IconBox * 0.68f),
            unlocked ? mint : deep, unlocked, _font);

        float tx = bx + IconBox + 16f;
        DrawString(_font, new Vector2(tx, r.Position.Y + 28f), $"{def.No:00}.  {name}",
            HorizontalAlignment.Left, 420f, ViewFont.S(19), unlocked ? ink : dim);
        DrawString(_font, new Vector2(tx, r.Position.Y + 52f), desc,
            HorizontalAlignment.Left, 460f, ViewFont.S(12), unlocked ? dim : deep);

        // 해금 여부 — 오른쪽 끝(스크롤바 자리를 비워 둔다).
        DrawString(_font, new Vector2(r.Position.X + r.Size.X - 138f, r.Position.Y + 40f),
            unlocked ? "✓ 달성" : "미달성", HorizontalAlignment.Right, 126f,
            ViewFont.S(15), unlocked ? mint : deep);
    }
}
