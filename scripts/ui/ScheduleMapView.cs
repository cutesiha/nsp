using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.View;

namespace NSP.Ui;

// 근무 배치 — 왼쪽 CRT(모니터 1). 시설 지도 위에 직원 칩을 끌어다 놓아 배치한다.
//
// 예전 책상 위 종이 배치표(ScheduleBoardUI)를 콘솔 화면으로 옮긴 것이다. 규칙은 하나도 바꾸지 않았다.
//   · 배치/해제는 FacilitySimulation.AssignToRoom / ClearAssignment 만 쓴다(같은 방이면 그대로).
//   · 배치 대상 작업실 = 제한 구역이 아니고 · 오늘 열려 있고 · 업무가 있는 방(종이 배치표와 같은 조건).
//   · "근무 시작"은 코어실에 1명 이상 배치됐을 때만 눌린다. 누르면 StartPressed 로 알린다.
//   · 금기(DayFeatures.TaboosEnabled 인 날)는 머리말 아래 한 줄로 싣는다.
//
// 방 위치는 RoomDef.MapPosition, 통로는 ConnectedRoomIds(실선) / AdjacentRoomIds(점선)를 그대로 그린다.
// 시뮬레이션의 이동 좌표(SetRoomVisualCenter)는 근무 중 미니맵이 소유하므로 여기서는 건드리지 않는다.
//
// 오른쪽 CRT(ScheduleStaffView)는 이 화면의 선택 상태(FocusEmployeeId / FocusRoomId / HoverRoomId)를 읽어
// 직원·작업실 정보를 띄운다.
public partial class ScheduleMapView : Control
{
    public static ScheduleMapView Instance { get; private set; }

    public event Action StartPressed;
    // 선택·배치가 바뀔 때마다. 오른쪽 모니터가 다시 그린다.
    public event Action Changed;

    // 오른쪽 모니터가 읽는 선택 상태.
    public string FocusEmployeeId { get; private set; } = "";
    public string FocusRoomId { get; private set; } = "";
    // 직원을 고른(또는 끌고 있는) 채 마우스가 올라가 있는 작업실 — 적합도 비교용.
    public string HoverRoomId { get; private set; } = "";
    // 클릭으로 배치 대상으로 고른 직원(다음에 누르는 작업실로 들어간다).
    public string SelectedEmployeeId { get; private set; } = "";
    public string DraggingEmployeeId => _dragging ? _dragEmp : "";

    // ── 화면 배치(모니터 논리 좌표 800 × 600) ────────────────────────────
    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Rect2 MapRect = new(20f, 100f, 548f, 384f);
    private static readonly Rect2 RosterRect = new(580f, 100f, 204f, 384f);
    private static readonly Vector2 CellSize = new(150f, 54f);
    // 방 칸 오른쪽 위 — Phase 1 관계 아이콘(거부/불편/우호)이 들어갈 자리. 지금은 비워 둔다.
    private static readonly Vector2 RelationSlotSize = new(16f, 16f);

    // ── 콘솔 색(시설 모니터와 같은 계열) ─────────────────────────────────
    private static readonly Color Bg = new(0.030f, 0.045f, 0.045f);
    private static readonly Color Ink = new(0.84f, 0.92f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.40f, 0.52f, 0.50f);
    private static readonly Color Amber = new(0.95f, 0.72f, 0.25f);
    private static readonly Color Alert = new(1f, 0.38f, 0.30f);
    private static readonly Color CellFill = new(0.07f, 0.13f, 0.13f);
    private static readonly Color CellLocked = new(0.06f, 0.07f, 0.08f);
    private static readonly Color Corridor = new(0.26f, 0.36f, 0.34f);

    private Font _font;
    private Button _start;
    private float _t;

    // 그릴 때 계산해 두는 판정 영역.
    private readonly Dictionary<string, Rect2> _cells = new();
    private readonly List<(string Emp, Rect2 Rect)> _chipRects = new();
    private readonly List<(string Emp, Rect2 Rect)> _rosterRects = new();

    // 끌어다 놓기 — SubViewport 안에서는 Godot 기본 DnD 가 시작되지 않아 직접 추적한다
    // (종이 배치표 · 근무 중 미니맵과 같은 방식).
    private string _dragEmp = "";
    private Vector2 _pressPos, _lastPos;
    private bool _dragging;

    public override void _Ready()
    {
        Instance = this;
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;
        SetProcess(true);
        SetProcessInput(true);

        _start = MonitorUi.Button("근무 시작  ▶", Mint, _font, () => StartPressed?.Invoke(), ViewFont.S(19));
        _start.Position = new Vector2(Canvas.X - 232f, Canvas.Y - 72f);
        _start.Size = new Vector2(212f, 50f);
        AddChild(_start);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 배치 단계에 들어올 때 — 지난 날의 선택을 비운다.
    public void Rebuild()
    {
        SelectedEmployeeId = "";
        FocusEmployeeId = "";
        FocusRoomId = "";
        HoverRoomId = "";
        EndDrag();
        NotifyChanged();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        var sim = FacilitySimulation.Instance;
        ComputeLayout(sim);
        if (_start != null) _start.Disabled = !CoreStaffed(sim);
        QueueRedraw();
    }

    // 방 칸 · 방 안의 직원 칩 · 대기 인원 카드의 자리. 그리기와 입력 판정이 같은 값을 쓴다.
    public void ComputeLayout(FacilitySimulation sim)
    {
        _chipRects.Clear();
        _rosterRects.Clear();
        if (sim == null || _font == null) { _cells.Clear(); return; }
        LayoutCells(sim);

        foreach (var (roomId, cell) in _cells)
        {
            if (!IsAssignable(sim, roomId)) continue;
            float x = cell.Position.X + 6f;
            float y = cell.Position.Y + cell.Size.Y - 20f;
            foreach (var emp in AssignedTo(sim, roomId))
            {
                string name = sim.GetEmployeeDef(emp)?.Codename ?? emp;
                float w = Mathf.Min(_font.GetStringSize(name, HorizontalAlignment.Left, -1, ViewFont.S(11)).X + 16f, 64f);
                if (x + w > cell.End.X - 4f) break;   // 넘치는 인원은 "…" 로만 표시(대기 인원 카드에서 잡을 수 있다)
                _chipRects.Add((emp, new Rect2(x, y, w, 16f)));
                x += w + 4f;
            }
        }

        var roster = sim.GetActiveEmployeeIds();
        float top = RosterRect.Position.Y + 30f;
        float step = Mathf.Clamp((RosterRect.Size.Y - 40f) / Mathf.Max(1, roster.Count), 40f, 58f);
        foreach (var emp in roster)
        {
            if (sim.GetEmployeeDef(emp) == null || sim.GetEmployeeState(emp) == null) continue;
            _rosterRects.Add((emp, new Rect2(RosterRect.Position.X + 8f, top, RosterRect.Size.X - 16f, step - 6f)));
            top += step;
        }
    }

    // 검증용 — 판정 영역을 밖에서 읽는다.
    public Rect2 CellOf(string roomId) => _cells.TryGetValue(roomId, out var r) ? r : new Rect2();
    public Rect2 RosterCardOf(string emp) => _rosterRects.FirstOrDefault(x => x.Emp == emp).Rect;
    public Rect2 ChipOf(string emp) => _chipRects.FirstOrDefault(x => x.Emp == emp).Rect;
    public Rect2 RosterArea => RosterRect;
    public Rect2 StartButtonRect => _start != null ? new Rect2(_start.Position, _start.Size) : new Rect2();
    public bool StartEnabled => _start != null && !_start.Disabled;

    // ── 규칙(종이 배치표와 동일) ──────────────────────────────────────────

    // 이 방에 직원을 배치할 수 있는가.
    public static bool IsAssignable(FacilitySimulation sim, string roomId)
    {
        var d = sim?.GetRoomDef(roomId);
        return d != null && !d.IsRestricted && sim.IsRoomActive(roomId)
               && sim.GetRoomTasksInPriorityOrder(roomId).Count > 0;
    }

    // 배치 단계에서는 시뮬레이션이 아직 돌지 않으므로 물리적 점유가 아니라 배치 지정으로 센다.
    public static List<string> AssignedTo(FacilitySimulation sim, string roomId) =>
        sim.GetActiveEmployeeIds()
            .Where(id => sim.GetEmployeeState(id)?.AssignedRoomId == roomId)
            .ToList();

    private static bool CoreStaffed(FacilitySimulation sim) =>
        sim != null && sim.GetActiveEmployeeIds().Any(id => sim.GetEmployeeState(id)?.AssignedRoomId == "core_room");

    // 권장 인원 — 종이 배치표와 같은 기준(그 방 업무의 RecommendedHeadcount 최댓값).
    public static int RecommendedHeadcount(FacilitySimulation sim, string roomId) =>
        sim.GetRoomTasksInPriorityOrder(roomId).Select(t => t.RecommendedHeadcount).DefaultIfEmpty(1).Max();

    private bool AssignEmp(string employeeId, string roomId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || !IsAssignable(sim, roomId)) return false;
        var st = sim.GetEmployeeState(employeeId);
        if (st != null && st.AssignedRoomId == roomId) return true; // 이미 그 방
        sim.ClearAssignment(employeeId);
        return sim.AssignToRoom(employeeId, roomId);
    }

    private void Unassign(string employeeId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(sim.GetEmployeeState(employeeId)?.AssignedRoomId)) return;
        sim.ClearAssignment(employeeId);
    }

    // ── 좌표 ─────────────────────────────────────────────────────────────

    // 지도에 올릴 방: MapPosition 이 있는 방 전부(잠긴 방도 어둡게 보여 준다).
    private static List<RoomDef> MapRooms(FacilitySimulation sim) =>
        sim.GetRoomIds().Select(sim.GetRoomDef)
            .Where(d => d != null && d.MapPosition != Vector2.Zero)
            .ToList();

    // MapPosition 전체를 지도 영역에 맞춰 늘린다(칸이 영역 밖으로 나가지 않게 반 칸씩 여백).
    private void LayoutCells(FacilitySimulation sim)
    {
        _cells.Clear();
        var rooms = MapRooms(sim);
        if (rooms.Count == 0) return;

        float minX = rooms.Min(r => r.MapPosition.X), maxX = rooms.Max(r => r.MapPosition.X);
        float minY = rooms.Min(r => r.MapPosition.Y), maxY = rooms.Max(r => r.MapPosition.Y);
        var inner = new Rect2(MapRect.Position + CellSize * 0.5f, MapRect.Size - CellSize);
        float sx = maxX > minX ? inner.Size.X / (maxX - minX) : 0f;
        float sy = maxY > minY ? inner.Size.Y / (maxY - minY) : 0f;

        foreach (var r in rooms)
        {
            var c = new Vector2(
                sx > 0f ? inner.Position.X + (r.MapPosition.X - minX) * sx : MapRect.GetCenter().X,
                sy > 0f ? inner.Position.Y + (r.MapPosition.Y - minY) * sy : MapRect.GetCenter().Y);
            _cells[r.RoomId] = new Rect2(c - CellSize * 0.5f, CellSize);
        }
    }

    // 잠긴 방 표기. 해금일이 근무 기간(config MaxDays) 안이면 "DAY n~" 를 붙인다.
    public static string LockedLabel(RoomDef def)
    {
        int maxDays = Config.Instance?.Data?.MaxDays ?? 5;
        return def != null && def.UnlockDay <= maxDays ? $"LOCKED  ·  DAY {def.UnlockDay}~" : "LOCKED";
    }

    // Phase 1 에서 관계 아이콘을 그릴 자리(방 칸 오른쪽 위 모서리).
    public Rect2 RelationSlotOf(string roomId) =>
        _cells.TryGetValue(roomId, out var r)
            ? new Rect2(r.End.X - RelationSlotSize.X - 3f, r.Position.Y + 3f, RelationSlotSize.X, RelationSlotSize.Y)
            : new Rect2();

    // ── 그리기 ───────────────────────────────────────────────────────────

    public override void _Draw()
    {
        var sim = FacilitySimulation.Instance;
        CrtGlass.Draw(this, new Rect2(Vector2.Zero, Canvas));   // 빛나는 유리 바탕 — UI 는 전부 이 위에 그린다
        if (sim == null || _font == null) return;

        if (_cells.Count == 0) ComputeLayout(sim);

        DrawHeader(sim);
        DrawCorridors(sim);
        foreach (var (roomId, _) in _cells) DrawRoomCell(sim, roomId);
        DrawRoster(sim);
        DrawFooter(sim);
        DrawDragGhost(sim);
        Scanlines();
    }

    private void DrawHeader(FacilitySimulation sim)
    {
        int day = GameState.Instance?.CurrentDay ?? 1;
        DrawRect(new Rect2(10f, 8f, Canvas.X - 20f, Canvas.Y - 16f), Mint with { A = 0.18f }, false, 1.2f);
        DrawString(_font, new Vector2(24f, 38f), "NIGHT SHIFT ASSIGNMENT", HorizontalAlignment.Left, 360f,
            ViewFont.S(16), Mint);
        DrawString(_font, new Vector2(24f, 60f), "근무 배치  ·  직원을 끌어 작업실에 놓으십시오", HorizontalAlignment.Left,
            420f, ViewFont.S(12), Dim);
        DrawString(_font, new Vector2(Canvas.X - 220f, 46f), DayFeatures.DayLabel(day), HorizontalAlignment.Right,
            196f, ViewFont.S(26), Ink);

        // 금기가 해금된 날에만 싣는다(종이 배치표와 같은 조건).
        if (DayFeatures.TaboosEnabled)
        {
            var taboos = TabooRuleSystem.Instance?.GetActiveTaboos().ToList();
            string text = taboos == null || taboos.Count == 0
                ? "오늘의 금기  —  특이사항 없음"
                : "⚠ 오늘의 금기  " + string.Join("   ⚠ ", taboos.Select(t => t.Description));
            DrawString(_font, new Vector2(24f, 86f), text, HorizontalAlignment.Left, Canvas.X - 48f,
                ViewFont.S(13), Amber);
        }
        DrawRect(new Rect2(24f, 93f, Canvas.X - 48f, 1f), Mint with { A = 0.16f });
    }

    private void DrawCorridors(FacilitySimulation sim)
    {
        var seen = new HashSet<string>();
        _cells.TryGetValue(FacilitySimulation.DeployOriginRoomId, out var originCell);
        Vector2 origin = originCell.Size != Vector2.Zero ? originCell.GetCenter() : MapRect.GetCenter();

        foreach (var (roomId, cell) in _cells)
        {
            var def = sim.GetRoomDef(roomId);
            if (def == null) continue;

            // 통로(실선) — 직원이 실제로 다니는 길. 근무 중 미니맵과 같은 직각 꺾임 규칙.
            foreach (var other in def.ConnectedRoomIds)
            {
                if (!_cells.TryGetValue(other, out var oc) || !seen.Add(Key("c", roomId, other))) continue;
                Vector2 a = cell.GetCenter(), b = oc.GetCenter();
                var elbow = CorridorElbow.Compute(a, b, origin);
                if (elbow == null) DrawLine(a, b, Corridor, 2.5f);
                else { DrawLine(a, elbow.Value, Corridor, 2.5f); DrawLine(elbow.Value, b, Corridor, 2.5f); }
            }
            // 벽을 맞댄 방(점선) — 소리가 넘어가는 이웃.
            foreach (var other in def.AdjacentRoomIds)
            {
                if (!_cells.TryGetValue(other, out var oc) || !seen.Add(Key("a", roomId, other))) continue;
                DrawDashedLine(cell.GetCenter(), oc.GetCenter(), Corridor with { A = 0.45f }, 1.2f, 5f);
            }
        }

        static string Key(string kind, string a, string b) =>
            kind + (string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a);
    }

    private void DrawRoomCell(FacilitySimulation sim, string roomId)
    {
        var def = sim.GetRoomDef(roomId);
        var cell = _cells[roomId];
        bool assignable = IsAssignable(sim, roomId);
        bool locked = !sim.IsRoomActive(roomId);
        bool restricted = def.IsRestricted;

        string hoverTarget = HoverRoomId;
        bool isHover = roomId == hoverTarget && (!string.IsNullOrEmpty(SelectedEmployeeId) || _dragging);
        bool focused = roomId == FocusRoomId;

        DrawRect(cell, locked || restricted ? CellLocked : CellFill);
        Color border = isHover ? (assignable ? Mint : Alert)
            : focused ? Mint
            : assignable ? Mint with { A = 0.35f } : Dim with { A = 0.35f };
        DrawRect(cell, border, false, isHover || focused ? 2.2f : 1.1f);
        if (isHover && assignable) DrawRect(cell, Mint with { A = 0.07f });

        var nameCol = assignable ? Ink : Dim;
        DrawString(_font, cell.Position + new Vector2(8f, 17f), def.DisplayName, HorizontalAlignment.Left,
            cell.Size.X - 60f, ViewFont.S(13), nameCol);

        if (locked)
        {
            // UnlockDay 로 잠긴 방 — 비활성 표시만. 이번 근무 기간(MaxDays) 안에 열리지 않는 방은 날짜를 싣지 않는다.
            DrawString(_font, cell.Position + new Vector2(8f, 40f), LockedLabel(def),
                HorizontalAlignment.Left, cell.Size.X - 16f, ViewFont.S(10), Dim);
            return;
        }
        if (restricted)
        {
            string tag = roomId == FacilitySimulation.DeployOriginRoomId ? "관리자" : "제한 구역";
            DrawString(_font, cell.Position + new Vector2(8f, 40f), tag, HorizontalAlignment.Left,
                cell.Size.X - 16f, ViewFont.S(10), Dim);
            return;
        }
        if (!assignable) return;

        // 인원: 배치 n / 권장 k · 수리 최소 m(RoomStaffing 과 같은 값).
        var here = AssignedTo(sim, roomId);
        int rec = RecommendedHeadcount(sim, roomId);
        int repair = RoomStaffing.RepairMinWorkers(roomId, def);
        Color countCol = here.Count == 0 ? Dim : here.Count >= rec ? Mint : Amber;
        DrawString(_font, new Vector2(cell.Position.X, cell.Position.Y + 17f), $"{here.Count}/{rec}",
            HorizontalAlignment.Right, cell.Size.X - 24f, ViewFont.S(12), countCol);
        DrawString(_font, new Vector2(cell.Position.X, cell.Position.Y + 30f), $"수리 {repair}",
            HorizontalAlignment.Right, cell.Size.X - 24f, ViewFont.S(9), Dim);

        // RelationSlotOf(roomId) — Phase 1 관계 아이콘 자리. 지금은 아무것도 그리지 않는다.

        // 배치된 직원 칩(자리는 ComputeLayout 이 잡아 둔다).
        int shown = 0;
        float lastX = cell.Position.X + 6f;
        foreach (var (emp, chip) in _chipRects)
        {
            if (!here.Contains(emp)) continue;
            var edef = sim.GetEmployeeDef(emp);
            DrawChip(chip, edef, edef?.Codename ?? emp, emp, 11);
            lastX = chip.End.X + 4f;
            shown++;
        }
        if (shown < here.Count)
            DrawString(_font, new Vector2(lastX, cell.End.Y - 8f), "…", HorizontalAlignment.Left, 12f, ViewFont.S(11), Dim);
    }

    private void DrawChip(Rect2 chip, EmployeeDef def, string name, string emp, int fontSize)
    {
        bool dragged = _dragging && _dragEmp == emp;
        bool selected = emp == SelectedEmployeeId || emp == FocusEmployeeId;
        Color tint = def?.IconColor ?? Mint;
        DrawRect(chip, new Color(0.03f, 0.07f, 0.07f, dragged ? 0.4f : 0.95f));
        DrawRect(new Rect2(chip.Position, new Vector2(3f, chip.Size.Y)), tint);
        DrawRect(chip, (selected ? Ink : tint with { A = 0.6f }), false, selected ? 1.6f : 1f);
        DrawString(_font, chip.Position + new Vector2(7f, chip.Size.Y * 0.5f + fontSize * 0.42f), name,
            HorizontalAlignment.Left, chip.Size.X - 9f, ViewFont.S(fontSize), dragged ? Dim : Ink);
    }

    // 오른쪽 대기 인원 — 오늘 근무자 전원. 배치된 사람은 어느 방인지 함께 보여 준다.
    private void DrawRoster(FacilitySimulation sim)
    {
        var r = RosterRect;
        bool dropHere = _dragging && r.HasPoint(_lastPos);
        DrawRect(r, new Color(0.04f, 0.08f, 0.08f, 0.9f));
        DrawRect(r, dropHere ? Mint : Mint with { A = 0.22f }, false, dropHere ? 2f : 1f);
        DrawString(_font, r.Position + new Vector2(10f, 20f), "STAFF  ·  대기 인원", HorizontalAlignment.Left,
            r.Size.X - 20f, ViewFont.S(12), Mint);

        foreach (var (emp, card) in _rosterRects)
        {
            var def = sim.GetEmployeeDef(emp);
            var st = sim.GetEmployeeState(emp);
            if (def == null || st == null) continue;
            float h = card.Size.Y;

            bool assigned = !string.IsNullOrEmpty(st.AssignedRoomId);
            bool selected = emp == SelectedEmployeeId || emp == FocusEmployeeId;
            bool dragged = _dragging && _dragEmp == emp;
            DrawRect(card, new Color(0.05f, 0.11f, 0.11f, dragged ? 0.4f : 0.95f));
            DrawRect(new Rect2(card.Position, new Vector2(3f, card.Size.Y)), def.IconColor);
            DrawRect(card, selected ? Ink : Mint with { A = assigned ? 0.18f : 0.45f }, false, selected ? 1.8f : 1f);

            // 얼굴.
            float ps = Mathf.Min(h - 8f, 38f);
            var face = new Rect2(card.Position.X + 8f, card.Position.Y + (h - ps) * 0.5f, ps, ps);
            DrawRect(face, new Color(0.02f, 0.05f, 0.06f));
            if (def.FacePortrait != null) DrawContained(def.FacePortrait, face);
            else DrawCircle(face.GetCenter(), ps * 0.34f, def.IconColor);
            DrawRect(face, Dim with { A = 0.6f }, false, 1f);

            float tx = face.End.X + 8f;
            DrawString(_font, new Vector2(tx, card.Position.Y + h * 0.5f - 2f), def.Codename,
                HorizontalAlignment.Left, card.End.X - tx - 4f, ViewFont.S(14), assigned ? Ink with { A = 0.7f } : Ink);

            string sub = assigned
                ? "→ " + (sim.GetRoomDef(st.AssignedRoomId)?.DisplayName ?? "")
                : DayFeatures.StatsEnabled
                    ? $"기{def.Tech} 담{def.Courage} 관{def.Observation}"
                    : "기분 · " + (string.IsNullOrEmpty(st.DailyMood) ? "—" : st.DailyMood);
            DrawString(_font, new Vector2(tx, card.Position.Y + h * 0.5f + 14f), sub,
                HorizontalAlignment.Left, card.End.X - tx - 4f, ViewFont.S(10), assigned ? Mint : Amber);
        }
    }

    private void DrawFooter(FacilitySimulation sim)
    {
        var roster = sim.GetActiveEmployeeIds();
        int total = roster.Count;
        int placed = roster.Count(id => !string.IsNullOrEmpty(sim.GetEmployeeState(id)?.AssignedRoomId));
        int missing = total - placed;
        bool core = CoreStaffed(sim);

        string status = !core
            ? "⚠ 코어실에 최소 1명의 직원을 배치해야 합니다."
            : missing > 0 ? $"{placed} / {total} 배치  ·  미배치 {missing}명" : $"{placed} / {total} 배치 완료";
        DrawRect(new Rect2(24f, Canvas.Y - 104f, Canvas.X - 48f, 1f), Mint with { A = 0.16f });
        DrawString(_font, new Vector2(24f, Canvas.Y - 52f), status, HorizontalAlignment.Left, 500f,
            ViewFont.S(15), !core || missing > 0 ? Amber : Mint);
        DrawString(_font, new Vector2(24f, Canvas.Y - 28f), "클릭 = 선택 · 끌기 = 배치 · 대기 인원으로 끌기/우클릭 = 해제",
            HorizontalAlignment.Left, 520f, ViewFont.S(10), Dim);
    }

    private void DrawDragGhost(FacilitySimulation sim)
    {
        if (!_dragging || string.IsNullOrEmpty(_dragEmp)) return;
        var def = sim.GetEmployeeDef(_dragEmp);
        string name = def?.Codename ?? _dragEmp;
        var r = new Rect2(_lastPos - new Vector2(46f, 12f), new Vector2(92f, 24f));
        DrawRect(r, new Color(0.05f, 0.14f, 0.13f, 0.95f));
        DrawRect(r, Mint, false, 1.6f);
        DrawRect(new Rect2(r.Position, new Vector2(3f, r.Size.Y)), def?.IconColor ?? Mint);
        DrawString(_font, r.Position + new Vector2(0f, 17f), name, HorizontalAlignment.Center, r.Size.X,
            ViewFont.S(13), Ink);
    }

    private void DrawContained(Texture2D tex, Rect2 box)
    {
        var src = tex.GetSize();
        if (src.X <= 0f || src.Y <= 0f) return;
        float k = Mathf.Min(box.Size.X / src.X, box.Size.Y / src.Y);
        var dst = src * k;
        DrawTextureRect(tex, new Rect2(box.Position + (box.Size - dst) * 0.5f, dst), false);
    }

    private void DrawDashedLine(Vector2 a, Vector2 b, Color col, float width, float dash)
    {
        float len = a.DistanceTo(b);
        if (len < 1f) return;
        Vector2 dir = (b - a) / len;
        for (float d = 0f; d < len; d += dash * 2f)
            DrawLine(a + dir * d, a + dir * Mathf.Min(d + dash, len), col, width);
    }

    private void Scanlines()
    {
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.12f));
        float sweep = Mathf.PosMod(_t * 70f, Canvas.Y);
        DrawRect(new Rect2(0, sweep, Canvas.X, 2f), Mint with { A = 0.05f });
    }

    // ── 판정 ─────────────────────────────────────────────────────────────

    private string EmployeeAt(Vector2 p)
    {
        foreach (var (emp, rect) in _chipRects) if (rect.HasPoint(p)) return emp;
        foreach (var (emp, rect) in _rosterRects) if (rect.HasPoint(p)) return emp;
        return null;
    }

    private string RoomAt(Vector2 p)
    {
        foreach (var (roomId, rect) in _cells) if (rect.HasPoint(p)) return roomId;
        return null;
    }

    // ── 입력 ─────────────────────────────────────────────────────────────

    public override void _GuiInput(InputEvent e)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        if (e is InputEventMouseMotion mm && !_dragging)
        {
            SetHover(RoomAt(mm.Position));
            return;
        }

        if (e is not InputEventMouseButton { Pressed: true } mb) return;
        string emp = EmployeeAt(mb.Position);

        // 우클릭 = 배치 해제.
        if (mb.ButtonIndex == MouseButton.Right)
        {
            if (emp != null) { Unassign(emp); FocusEmp(emp); }
            AcceptEvent();
            return;
        }
        if (mb.ButtonIndex != MouseButton.Left) return;

        if (emp != null)
        {
            // 누른 순간에는 끌기만 준비한다. 손을 뗄 때 끌지 않았으면 클릭(선택)으로 본다.
            _dragEmp = emp;
            _pressPos = mb.Position;
            _lastPos = mb.Position;
            _dragging = false;
            AcceptEvent();
            return;
        }

        string room = RoomAt(mb.Position);
        if (room != null) OnRoomClicked(room);
        AcceptEvent();
    }

    // 끌기 중 이동/뗌은 칩 밖에서도 받아야 한다 — 루트 _Input 에서 추적한다.
    public override void _Input(InputEvent e)
    {
        if (string.IsNullOrEmpty(_dragEmp)) return;
        // 이 뷰는 스케일 프레임(AddScaledView) 안에 있어 _Input 은 확대 좌표로 들어온다 — 로컬로 맞춘다.
        e = MakeInputLocal(e);

        if (e is InputEventMouseMotion mm)
        {
            _lastPos = mm.Position;
            if (!_dragging && mm.Position.DistanceTo(_pressPos) > 6f) _dragging = true;
            if (_dragging) SetHover(RoomAt(mm.Position));
        }
        else if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            string emp = _dragEmp;
            if (_dragging) DropAt(emp, _lastPos);
            else OnEmployeeClicked(emp);
            EndDrag();
            NotifyChanged();
        }
    }

    private void DropAt(string emp, Vector2 pos)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return;
        if (RosterRect.HasPoint(pos)) { Unassign(emp); FocusEmp(emp); return; }

        string room = RoomAt(pos);
        if (room != null && AssignEmp(emp, room))
        {
            SelectedEmployeeId = "";
            FocusRoomId = room;
            FocusEmployeeId = "";
        }
    }

    private void EndDrag()
    {
        _dragEmp = "";
        _dragging = false;
        HoverRoomId = "";
    }

    private void OnEmployeeClicked(string emp)
    {
        SelectedEmployeeId = SelectedEmployeeId == emp ? "" : emp;
        FocusEmp(emp);
    }

    private void FocusEmp(string emp)
    {
        FocusEmployeeId = emp;
        FocusRoomId = "";
        NotifyChanged();
    }

    // 작업실 클릭 — 고른 직원이 있으면 그 방에 배치, 없으면 작업실 정보만.
    private void OnRoomClicked(string room)
    {
        var sim = FacilitySimulation.Instance;
        if (!string.IsNullOrEmpty(SelectedEmployeeId) && IsAssignable(sim, room))
        {
            AssignEmp(SelectedEmployeeId, room);
            SelectedEmployeeId = "";
        }
        FocusRoomId = room;
        FocusEmployeeId = "";
        HoverRoomId = "";
        NotifyChanged();
    }

    private void SetHover(string room)
    {
        room ??= "";
        if (HoverRoomId == room) return;
        HoverRoomId = room;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke();
}
