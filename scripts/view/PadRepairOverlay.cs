using System.Linq;
using Godot;
using NSP.Core;

namespace NSP.View;

// 패드 화면 위의 수리 승인 절차(G-2) — 승인 요청과 미로를 그린다.
//
// 규칙은 RepairApprovalSystem 이 전부 쥐고 있다. 여기서는 그 상태를 읽어 그리고,
// 누른 것만 돌려준다. 방향키는 미로가 떠 있는 동안 이 창이 독점한다.
public partial class PadRepairOverlay : Control
{
    private static readonly Color Ink = new(0.86f, 0.92f, 0.94f);
    private static readonly Color Dim = new(0.50f, 0.62f, 0.66f);
    private static readonly Color Cyan = new(0.55f, 0.95f, 1f);
    private static readonly Color Amber = new(1f, 0.80f, 0.36f);
    private static readonly Color Red = new(1f, 0.42f, 0.34f);
    private static readonly Color Good = new(0.45f, 0.95f, 0.55f);

    private readonly Vector2 _canvas;
    private Font _font;

    public PadRepairOverlay(Vector2 canvas)
    {
        _canvas = canvas;
        Size = canvas;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        _font = ViewFont.Default;
        SetProcess(true);
    }

    // 지금 마우스가 올라와 있는 버튼(0 = 없음, 1 = 예, 2 = 아니오).
    // 패드 화면은 SubViewport 안이라 제어실이 마우스를 밀어 넣어 준다 — 그 자리를 그대로 읽는다.
    private int _hover;

    public override void _Process(double delta)
    {
        bool want = RepairApprovalSystem.Busy;
        if (Visible != want) Visible = want;
        if (!want) { _hover = 0; return; }

        int hover = 0;
        if (RepairApprovalSystem.Current == RepairApprovalSystem.Phase.Asking)
        {
            var at = GetLocalMousePosition();
            hover = YesRect.HasPoint(at) ? 1 : NoRect.HasPoint(at) ? 2 : 0;
        }
        if (hover != _hover)
        {
            // 올라탄 순간에만 한 번 — 머무는 동안 계속 울리면 귀가 아프다.
            if (hover != 0) Sfx.Instance?.Play("tick", -16f);
            _hover = hover;
        }
        QueueRedraw();
    }

    // 방향키 처리는 AdminPad3D 가 맡는다 — 패드 화면은 SubViewport 안이라
    // 창의 키 입력이 여기까지 내려오지 않는다. 그리는 것만 이 클래스의 일이다.
    public static RepairMaze.Dir? DirOf(Key key) => key switch
    {
        Key.Up or Key.W => RepairMaze.Dir.Up,
        Key.Down or Key.S => RepairMaze.Dir.Down,
        Key.Left or Key.A => RepairMaze.Dir.Left,
        Key.Right or Key.D => RepairMaze.Dir.Right,
        _ => null,
    };

    // 승인 요청 화면에서 [예] / [아니오] 를 눌렀다(패드 화면 좌표).
    public bool TryPress(Vector2 at)
    {
        if (!Visible || RepairApprovalSystem.Current != RepairApprovalSystem.Phase.Asking) return false;
        if (YesRect.HasPoint(at)) { RepairApprovalSystem.Approve(); Sfx.Instance?.Play("relay_click", -10f); return true; }
        if (NoRect.HasPoint(at)) { RepairApprovalSystem.Decline(); Sfx.Instance?.Play("switch_fail", -12f); return true; }
        return false;
    }

    private Rect2 YesRect => new(_canvas.X * 0.5f - 320f, _canvas.Y * 0.655f, 300f, 110f);
    private Rect2 NoRect => new(_canvas.X * 0.5f + 20f, _canvas.Y * 0.655f, 300f, 110f);

    public override void _Draw()
    {
        if (!RepairApprovalSystem.Busy) return;
        // 완전 불투명 — 3% 만 비쳐도 뒤의 홈 아이콘이 읽힌다.
        DrawRect(new Rect2(Vector2.Zero, _canvas), new Color(0.01f, 0.03f, 0.04f));

        switch (RepairApprovalSystem.Current)
        {
            case RepairApprovalSystem.Phase.Asking: DrawAsking(); break;
            case RepairApprovalSystem.Phase.Maze: DrawMaze(); break;
            case RepairApprovalSystem.Phase.Result: DrawResult(); break;
        }
    }

    private void DrawAsking()
    {
        var r = RepairApprovalSystem.Active;
        float w = _canvas.X;

        // 거치대 위 패드는 화면에서 손바닥만 하다 — 이 창의 글자는 전부 패드의 다른 화면보다 크다.
        // 깜빡이는 테두리까지 둬서 "지금 답해야 한다"가 곁눈으로도 보이게 한다.
        float beat = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 150f);
        DrawRect(new Rect2(Vector2.Zero, _canvas), Amber with { A = 0.35f + 0.45f * beat }, false, 10f);

        Title("수리 승인 요청", Amber);
        DrawString(_font, new Vector2(0, _canvas.Y * 0.31f), $"{r?.RoomName} — 설비 수리",
            HorizontalAlignment.Center, w, ViewFont.S(40), Ink);
        DrawString(_font, new Vector2(0, _canvas.Y * 0.43f), "수리를 승인하시겠습니까?",
            HorizontalAlignment.Center, w, ViewFont.S(48), Ink);

        // 남은 응답 시간 — 막대와 숫자.
        float ratio = RepairApprovalSystem.SecondsTotal <= 0f ? 0f
            : Mathf.Clamp(RepairApprovalSystem.SecondsLeft / RepairApprovalSystem.SecondsTotal, 0f, 1f);
        var bar = new Rect2(w * 0.5f - 330f, _canvas.Y * 0.495f, 660f, 20f);
        DrawRect(bar, new Color(0.10f, 0.14f, 0.16f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * ratio, bar.Size.Y)),
            ratio < 0.34f ? Red : Amber);
        DrawString(_font, new Vector2(0, _canvas.Y * 0.575f),
            $"{Mathf.CeilToInt(Mathf.Max(0f, RepairApprovalSystem.SecondsLeft))}초 — 답하지 않으면 수리가 느려집니다",
            HorizontalAlignment.Center, w, ViewFont.S(26), Dim);

        Button(YesRect, "예", Good, _hover == 1);
        Button(NoRect, "아니오", Red, _hover == 2);
    }

    private void Button(Rect2 box, string label, Color col, bool hot)
    {
        // 올라타면 테두리 색으로 안이 차고 글자가 검게 뒤집힌다 — 패드 화면이 작아서
        // 테두리만 밝히는 정도로는 "눌리는 것" 이 읽히지 않는다.
        DrawRect(box, hot ? col with { A = 0.85f } : new Color(0.05f, 0.11f, 0.13f));
        DrawRect(box.Grow(hot ? 2f : 0f), col, false, hot ? 5f : 3f);
        DrawString(_font, new Vector2(box.Position.X, box.Position.Y + box.Size.Y * 0.68f), label,
            HorizontalAlignment.Center, box.Size.X, ViewFont.S(46),
            hot ? new Color(0.03f, 0.08f, 0.09f) : col);
    }

    private void DrawMaze()
    {
        var m = RepairApprovalSystem.Maze;
        if (m == null) return;
        float w = _canvas.X;
        Title("수리 절차 인증", Cyan);

        // 상단 타이머 — 첫 입력 전에는 가득 찬 채로 멈춰 있다.
        float ratio = RepairApprovalSystem.SecondsTotal <= 0f ? 1f
            : Mathf.Clamp(RepairApprovalSystem.SecondsLeft / RepairApprovalSystem.SecondsTotal, 0f, 1f);
        var bar = new Rect2(w * 0.5f - 330f, _canvas.Y * 0.155f, 660f, 20f);
        DrawRect(bar, new Color(0.10f, 0.14f, 0.16f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * ratio, bar.Size.Y)),
            ratio < 0.3f ? Red : Cyan);
        // 남은 시간은 막대 **아래** 에 — 제목과 겹치지 않는다.
        DrawString(_font, new Vector2(0, bar.Position.Y + 48f),
            RepairApprovalSystem.MazeStarted
                ? $"{Mathf.Max(0f, RepairApprovalSystem.SecondsLeft):0.0}초"
                : "방향키를 누르면 시작합니다",
            HorizontalAlignment.Center, w, ViewFont.S(26), Dim);

        // 미로 — 화면 가운데 정사각형.
        float side = Mathf.Min(_canvas.X * 0.60f, _canvas.Y * 0.60f);
        float cell = side / m.Size;
        var origin = new Vector2((w - side) * 0.5f, _canvas.Y * 0.265f);

        for (int y = 0; y < m.Size; y++)
        for (int x = 0; x < m.Size; x++)
        {
            var at = new Vector2I(x, y);
            var box = new Rect2(origin + new Vector2(x * cell, y * cell), new Vector2(cell, cell));
            bool trail = m.Trail.Contains(at);
            var fill = at == m.Goal ? new Color(0.14f, 0.34f, 0.20f)
                : at == m.Cursor ? new Color(0.18f, 0.40f, 0.46f)
                : trail ? new Color(0.10f, 0.22f, 0.26f)
                : new Color(0.04f, 0.07f, 0.08f);
            // 실패한 칸은 붉게 — 어디서 왜 끝났는지 보인다.
            if (m.Failed && at == m.FailedAt) fill = new Color(0.42f, 0.10f, 0.10f);
            DrawRect(box, fill);

            // 벽 — 열려 있지 않은 면만 선을 긋는다.
            var wall = new Color(0.45f, 0.62f, 0.68f);
            if (!m.IsOpen(at, RepairMaze.Dir.Up))
                DrawLine(box.Position, box.Position + new Vector2(cell, 0), wall, 2f);
            if (!m.IsOpen(at, RepairMaze.Dir.Left))
                DrawLine(box.Position, box.Position + new Vector2(0, cell), wall, 2f);
            if (x == m.Size - 1 && !m.IsOpen(at, RepairMaze.Dir.Right))
                DrawLine(box.Position + new Vector2(cell, 0), box.Position + new Vector2(cell, cell), wall, 2f);
            if (y == m.Size - 1 && !m.IsOpen(at, RepairMaze.Dir.Down))
                DrawLine(box.Position + new Vector2(0, cell), box.Position + new Vector2(cell, cell), wall, 2f);
        }

        Label(origin, cell, m.Start, "S", Amber);
        Label(origin, cell, m.Goal, "G", Good);

        DrawString(_font, new Vector2(0, origin.Y + side + 40f),
            "방향키로 한 칸씩 · 벽 · 되돌아가기 · 시간 초과 = 실패",
            HorizontalAlignment.Center, w, ViewFont.S(24), Dim);
    }

    private void Label(Vector2 origin, float cell, Vector2I at, string text, Color col)
    {
        var p = origin + new Vector2(at.X * cell, at.Y * cell);
        DrawString(_font, new Vector2(p.X, p.Y + cell * 0.7f), text,
            HorizontalAlignment.Center, cell, ViewFont.S(Mathf.RoundToInt(cell * 0.5f)), col);
    }

    // 결과를 0.5초 보여 준다 — 실패면 어느 칸에서 왜 끝났는지 미로를 그대로 둔 채로.
    private void DrawResult()
    {
        if (RepairApprovalSystem.Maze != null) DrawMaze();
        else Title("수리 승인 요청", Amber);

        bool ok = RepairApprovalSystem.LastSucceeded;
        string why = ok ? "승인 완료 — 수리를 진행합니다"
            : !RepairApprovalSystem.LastApproved ? "미승인 — 수리가 느려집니다"
            : RepairApprovalSystem.Maze?.TimedOut == true ? "시간 초과 — 수리가 느려집니다"
            : RepairApprovalSystem.Maze?.FailedAs == RepairMaze.StepResult.Backtracked
                ? "왔던 칸으로 되돌아갔습니다 — 수리가 느려집니다"
                : "벽에 막혔습니다 — 수리가 느려집니다";

        var band = new Rect2(0, _canvas.Y * 0.84f, _canvas.X, 80f);
        DrawRect(band, new Color(0.02f, 0.05f, 0.06f, 0.95f));
        DrawString(_font, new Vector2(0, band.Position.Y + 54f), why,
            HorizontalAlignment.Center, _canvas.X, ViewFont.S(32), ok ? Good : Red);
    }

    private void Title(string text, Color col) =>
        DrawString(_font, new Vector2(0, _canvas.Y * 0.115f), text,
            HorizontalAlignment.Center, _canvas.X, ViewFont.S(50), col);
}
