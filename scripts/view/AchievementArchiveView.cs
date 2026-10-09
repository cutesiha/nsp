using Godot;
using NSP.Core;

namespace NSP.View;

// 타이틀의 「기록 열람」 — 도전과제 기록실.
//
// 왼쪽 CRT 의 관리자 단말기(TitleTerminalView)와 같은 캔버스(800×600) 위에 얹히는
// 전용 화면이다. 단말기의 글꼴 · 색 · 주사선 규약을 그대로 쓴다 — 여기만 다른 UI 로
// 보이면 시설 단말기라는 감각이 깨진다.
//
// 스스로 입력을 받지 않는다. TitleRoomDirector 가 카메라 레이캐스트로 좌표를 넘겨 주고
// (ItemAt / HoverAt) 키보드도 그쪽에서 몰아 준다 — 타이틀 메뉴와 똑같은 규약이다.
// 그래서 기록실을 보는 동안에는 「근무 개시」가 실수로 실행되지 않는다.
//
// 30개를 한 화면에 쑤셔 넣지 않는다. 한 페이지에 다섯 줄씩 여섯 페이지로 넘긴다.
public partial class AchievementArchiveView : Control
{
    public static AchievementArchiveView Instance { get; private set; }

    // ── 문구 ────────────────────────────────────────────────────────
    private const string Head = "도전과제 기록";
    private const string Sub = "관리자 활동 기록  /  ACHIEVEMENT ARCHIVE";
    private const string HiddenName = "???";
    private const string HiddenDesc = "아직 밝혀지지 않은 기록입니다.";
    private const string DoneTag = "달성";
    private const string LockTag = "미달성";
    private const string BackLabel = "뒤로";

    // ── 색 (TitleTerminalView 와 같은 팔레트) ─────────────────────────
    private static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    private static readonly Color Deep = new(0.26f, 0.32f, 0.33f);

    private static readonly Vector2 Canvas = new(800f, 600f);

    // ── 배치 ────────────────────────────────────────────────────────
    public const int PerPage = 5;
    private const float RowTop = 136f;
    private const float RowStep = 78f;
    private const float RowLeft = 56f;
    private const float RowWidth = 688f;
    private const float IconBox = 50f;
    private const float FootY = 554f;

    private Font _font;
    private float _t;

    public bool IsOpen { get; private set; }
    public int Page { get; private set; }
    public int Cursor { get; private set; }     // 페이지 안에서 몇 번째 줄인가(0~4)

    public int PageCount => Mathf.Max(1, (Achievements.Total + PerPage - 1) / PerPage);

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        Size = Canvas;
        Position = Vector2.Zero;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
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
        Page = 0;
        Cursor = 0;
        Visible = true;
        SetProcess(true);
        QueueRedraw();
    }

    public void Close()
    {
        IsOpen = false;
        Visible = false;
        SetProcess(false);
    }

    // ── 이동 ────────────────────────────────────────────────────────

    // 한 줄 위/아래. 페이지 경계를 넘으면 페이지가 따라 넘어간다.
    public bool MoveCursor(int delta)
    {
        if (!IsOpen) return false;
        int flat = Mathf.Clamp(Page * PerPage + Cursor + delta, 0, Achievements.Total - 1);
        int page = flat / PerPage, cursor = flat % PerPage;
        if (page == Page && cursor == Cursor) return false;
        Page = page;
        Cursor = cursor;
        QueueRedraw();
        return true;
    }

    public bool MovePage(int delta)
    {
        if (!IsOpen) return false;
        int page = Mathf.Clamp(Page + delta, 0, PageCount - 1);
        if (page == Page) return false;
        Page = page;
        Cursor = 0;
        QueueRedraw();
        return true;
    }

    // ── 마우스 ──────────────────────────────────────────────────────

    // 논리 좌표가 어느 조작 위인가. "prev" / "next" / "back" / "" (없음).
    public string ItemAt(Vector2 p)
    {
        if (!IsOpen) return "";
        if (PrevRect().HasPoint(p)) return "prev";
        if (NextRect().HasPoint(p)) return "next";
        if (BackRect().HasPoint(p)) return "back";
        return "";
    }

    // 목록 위에 마우스를 올리면 그 줄로 커서가 간다. 옮겨졌으면 true(효과음용).
    public bool HoverAt(Vector2 p)
    {
        if (!IsOpen) return false;
        for (int i = 0; i < PerPage; i++)
        {
            if (Page * PerPage + i >= Achievements.Total) break;
            if (!RowRect(i).HasPoint(p) || Cursor == i) continue;
            Cursor = i;
            QueueRedraw();
            return true;
        }
        return false;
    }

    private static Rect2 RowRect(int i) => new(RowLeft, RowTop + i * RowStep - 6f, RowWidth, RowStep - 10f);
    private static Rect2 PrevRect() => new(RowLeft, FootY - 24f, 128f, 36f);
    private static Rect2 NextRect() => new(RowLeft + 150f, FootY - 24f, 128f, 36f);
    private static Rect2 BackRect() => new(Canvas.X - RowLeft - 128f, FootY - 24f, 128f, 36f);

    // ── 그리기 ──────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // 깜빡이는 커서만 움직인다 — 초당 네 번이면 충분하다.
        if (Mathf.PosMod(_t, 0.25f) < delta) QueueRedraw();
    }

    public override void _Draw()
    {
        var mgr = AchievementManager.Instance;
        int done = mgr?.GetUnlockedCount() ?? 0;
        int total = Achievements.Total;

        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.020f, 0.032f, 0.033f));
        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Mint with { A = 0.28f }, false, 1.4f);
        DrawString(_font, new Vector2(30f, 38f), "FACILITY CONTROL SYSTEM",
            HorizontalAlignment.Left, 420f, ViewFont.S(12), Dim);

        // 표제 + 보조 문구.
        DrawString(_font, new Vector2(RowLeft, 70f), Head, HorizontalAlignment.Left, 420f, ViewFont.S(26), Ink);
        DrawString(_font, new Vector2(RowLeft, 92f), Sub, HorizontalAlignment.Left, 520f, ViewFont.S(12), Dim);

        // 진행률 — 숫자와 막대. 막대는 실제 비율만큼만 찬다.
        string count = $"달성 {done:00} / {total:00}";
        DrawString(_font, new Vector2(Canvas.X - RowLeft - 220f, 76f), count,
            HorizontalAlignment.Right, 220f, ViewFont.S(18), Mint);
        float barY = 110f, barW = Canvas.X - RowLeft * 2f;
        DrawRect(new Rect2(RowLeft, barY, barW, 7f), Deep with { A = 0.55f });
        float ratio = total <= 0 ? 0f : (float)done / total;
        if (ratio > 0f) DrawRect(new Rect2(RowLeft, barY, barW * ratio, 7f), Mint with { A = 0.80f });

        // 목록.
        for (int i = 0; i < PerPage; i++)
        {
            int idx = Page * PerPage + i;
            if (idx >= total) break;
            DrawRow(i, Achievements.All[idx], mgr?.IsUnlocked(Achievements.All[idx].Id) ?? false, i == Cursor);
        }

        // 아래 조작줄.
        Button(PrevRect(), "◀ 이전", Page > 0);
        Button(NextRect(), "다음 ▶", Page < PageCount - 1);
        Button(BackRect(), BackLabel, true);
        DrawString(_font, new Vector2(0f, FootY + 2f), $"{Page + 1} / {PageCount}",
            HorizontalAlignment.Center, Canvas.X, ViewFont.S(15), Dim);
        DrawString(_font, new Vector2(0f, FootY + 26f), "↑ ↓ 이동   ← → 페이지   ESC 뒤로",
            HorizontalAlignment.Center, Canvas.X, ViewFont.S(11), Deep);

        // 주사선.
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
    }

    private void DrawRow(int i, AchievementDefinition def, bool unlocked, bool on)
    {
        var r = RowRect(i);
        if (on) DrawRect(r, Mint with { A = 0.09f });
        DrawRect(new Rect2(r.Position.X, r.Position.Y + r.Size.Y, r.Size.X, 1f), Mint with { A = 0.12f });

        // 숨김 업적은 달성 전까지 이름도 설명도 내놓지 않는다.
        bool masked = def.Hidden && !unlocked;
        string name = masked ? HiddenName : def.Name;
        string desc = masked ? HiddenDesc : def.Condition;
        var nameCol = unlocked ? Ink : Dim;
        var descCol = unlocked ? Dim : Deep;

        // 아이콘 칸 — 달성하면 분류 픽토그램, 미달성이면 잠금 표시.
        float bx = r.Position.X + 8f, by = r.Position.Y + (r.Size.Y - IconBox) * 0.5f;
        DrawRect(new Rect2(bx, by, IconBox, IconBox),
            unlocked ? new Color(0.07f, 0.12f, 0.12f) : new Color(0.035f, 0.045f, 0.048f));
        DrawRect(new Rect2(bx, by, IconBox, IconBox),
            (unlocked ? Mint : Deep) with { A = unlocked ? 0.55f : 0.35f }, false, 1.2f);
        DrawString(_font, new Vector2(bx, by + IconBox * 0.70f), unlocked ? def.Glyph : "□",
            HorizontalAlignment.Center, IconBox, ViewFont.S(22), unlocked ? Mint : Deep);

        float tx = bx + IconBox + 16f;
        DrawString(_font, new Vector2(tx, r.Position.Y + 28f), $"{def.No:00}.  {name}",
            HorizontalAlignment.Left, 430f, ViewFont.S(19), nameCol);
        DrawString(_font, new Vector2(tx, r.Position.Y + 52f), desc,
            HorizontalAlignment.Left, 470f, ViewFont.S(12), descCol);

        // 해금 여부 — 오른쪽 끝.
        DrawString(_font, new Vector2(r.Position.X + r.Size.X - 130f, r.Position.Y + 40f),
            unlocked ? "✓ " + DoneTag : LockTag, HorizontalAlignment.Right, 126f,
            ViewFont.S(15), unlocked ? Mint : Deep);
    }

    private void Button(Rect2 r, string label, bool live)
    {
        DrawRect(r, (live ? Mint : Deep) with { A = live ? 0.38f : 0.18f }, false, 1.1f);
        DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 25f), label,
            HorizontalAlignment.Center, r.Size.X, ViewFont.S(15), live ? Ink : Deep);
    }
}
