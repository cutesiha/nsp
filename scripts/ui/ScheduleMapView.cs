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
//   · 관계(RelationshipSystem): 동실 거부 쌍이 한 방에 있으면 근무 시작이 막히고 두 칩 사이에 붉은 스파크,
//     불편 쌍은 주황 경고(배치는 허용 — 근무 중 소프트 페널티는 RoomStaffing/FacilitySimulation 이 맡는다),
//     밀접/우호는 방 칸 모서리에 하트/고리. 아이콘·스파크 위에 마우스를 올리면 설명 말풍선.
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
    // 방 칸 오른쪽 위 — 관계 아이콘(거부 스파크 / 불편 경고 / 밀접 하트 / 우호 고리) 자리.
    private static readonly Vector2 RelationSlotSize = new(16f, 16f);

    // ── 콘솔 색(시설 모니터와 같은 계열) ─────────────────────────────────
    private static readonly Color Bg = new(0.030f, 0.045f, 0.045f);
    private static readonly Color Ink = new(0.84f, 0.92f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.40f, 0.52f, 0.50f);
    private static readonly Color Amber = new(0.95f, 0.72f, 0.25f);
    private static readonly Color Alert = new(1f, 0.38f, 0.30f);
    // 관계 표시 — 불편(주황) · 밀접(하트).
    private static readonly Color Warn = new(1f, 0.56f, 0.18f);
    private static readonly Color Heart = new(1f, 0.45f, 0.62f);
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
    // 말풍선(관계 설명) 판정용 마우스 위치.
    private Vector2 _mouse = new(-100f, -100f);
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
        // 동실 거부(Refuse) 쌍이 한 방에 있으면 근무를 시작할 수 없다 — 버튼도 붉게 죽인다.
        if (_start != null)
        {
            bool refused = RefusedPairs(sim).Count > 0;
            _start.Disabled = !CoreStaffed(sim) || refused;
            _start.Modulate = refused ? new Color(1f, 0.5f, 0.45f, 0.8f) : Colors.White;
        }
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

    // ── 직원 관계(RelationshipSystem) ───────────────────────────────────
    // 판정은 RelationshipSystem.Band 하나만 쓴다(두 방향 평균 → 밴드, 임계치는 relationships.tres).

    // 한 방에 배치된 사람들의 쌍과 그 밴드. 중립(Neutral) 쌍은 뺀다.
    public static List<(string A, string B, PairBand Band)> RoomPairs(FacilitySimulation sim, string roomId)
    {
        var here = AssignedTo(sim, roomId);
        var list = new List<(string, string, PairBand)>();
        for (int i = 0; i < here.Count; i++)
            for (int j = i + 1; j < here.Count; j++)
            {
                var band = RelationshipSystem.Band(here[i], here[j]);
                if (band != PairBand.Neutral) list.Add((here[i], here[j], band));
            }
        return list;
    }

    // 배치 전체에서 같은 방 근무를 거부하는 쌍(방 id 포함). 하나라도 있으면 근무 시작이 막힌다.
    public static List<(string RoomId, string A, string B)> RefusedPairs(FacilitySimulation sim)
    {
        var list = new List<(string, string, string)>();
        if (sim == null) return list;
        foreach (var roomId in sim.GetRoomIds())
        {
            if (!IsAssignable(sim, roomId)) continue;
            RelationshipSystem.CanCoAssignAll(AssignedTo(sim, roomId), out var refused);
            foreach (var (a, b) in refused) list.Add((roomId, a, b));
        }
        return list;
    }

    // 방 아이콘에 올릴 대표 밴드 — 거부 > 불편 > 밀접 > 우호. 없으면 null.
    public static PairBand? RoomBand(FacilitySimulation sim, string roomId)
    {
        var pairs = RoomPairs(sim, roomId);
        foreach (var b in new[] { PairBand.Refuse, PairBand.Uneasy, PairBand.Close, PairBand.Friendly })
            if (pairs.Any(p => p.Band == b)) return b;
        return null;
    }

    // "고양이와 여우는 같은 방 근무를 거부합니다" 같은 한 줄 설명.
    public static string PairText(FacilitySimulation sim, string a, string b, PairBand band)
    {
        string na = sim?.GetEmployeeDef(a)?.Codename ?? a;
        string nb = sim?.GetEmployeeDef(b)?.Codename ?? b;
        string both = NSP.Dialogue.KoreanParticle.With(na) + " " + NSP.Dialogue.KoreanParticle.Topic(nb);
        return band switch
        {
            PairBand.Refuse => both + " 같은 방 근무를 거부합니다",
            PairBand.Uneasy => both + " 사이가 불편합니다 — 효율 저하 · 긴장 · 언쟁 위험",
            PairBand.Close => both + " 각별한 사이입니다",
            PairBand.Friendly => both + " 사이가 좋습니다",
            _ => "",
        };
    }

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

    // 관계 아이콘 자리(방 칸 오른쪽 위 모서리).
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
        DrawTooltip(sim);
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
        // 관계 경고가 걸린 방은 테두리 색으로도 알린다(거부 = 붉게 맥동, 불편 = 주황).
        PairBand? band = assignable ? RoomBand(sim, roomId) : null;
        float pulse = 0.55f + 0.45f * Mathf.Sin(_t * 6f);
        Color border = isHover ? (assignable ? Mint : Alert)
            : band == PairBand.Refuse ? Alert with { A = 0.5f + 0.5f * pulse }
            : band == PairBand.Uneasy ? Warn
            : focused ? Mint
            : assignable ? Mint with { A = 0.35f } : Dim with { A = 0.35f };
        bool thick = isHover || focused || band is PairBand.Refuse or PairBand.Uneasy;
        DrawRect(cell, border, false, thick ? 2.2f : 1.1f);
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

        // 관계 아이콘(방 칸 오른쪽 위) — 거부 > 불편 > 밀접 > 우호 중 가장 센 것 하나.
        if (band != null) DrawRelationIcon(RelationSlotOf(roomId), band.Value, pulse);

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

        // 동실 거부 쌍 — 두 칩 사이에 붉은 스파크.
        foreach (var (a, b, pb) in RoomPairs(sim, roomId))
        {
            if (pb != PairBand.Refuse) continue;
            var ca = ChipOf(a); var cb = ChipOf(b);
            if (ca.Size == Vector2.Zero || cb.Size == Vector2.Zero) continue;
            DrawSpark(ca, cb, pulse);
        }
    }

    // ── 관계 표시 ────────────────────────────────────────────────────────

    // 두 칩 위를 잇는 붉은 번개 + 가운데 불꽃. 판정 영역은 SparkRect 와 같다.
    private void DrawSpark(Rect2 ca, Rect2 cb, float pulse)
    {
        if (ca.Position.X > cb.Position.X) (ca, cb) = (cb, ca);
        float y = ca.Position.Y - 3f;
        Vector2 a = new(ca.GetCenter().X, y), b = new(cb.GetCenter().X, y);
        // 칩 윗변에서 살짝 뜬 지그재그 — 시간에 따라 꺾임이 흔들린다.
        int seg = Mathf.Max(4, (int)(a.DistanceTo(b) / 7f));
        var pts = new Vector2[seg + 1];
        for (int i = 0; i <= seg; i++)
        {
            float k = (float)i / seg;
            float jag = (i == 0 || i == seg) ? 0f : ((i % 2 == 0 ? 1f : -1f) * (2.5f + 1.5f * Mathf.Sin(_t * 23f + i)));
            pts[i] = a.Lerp(b, k) + new Vector2(0f, jag - 4f * Mathf.Sin(k * Mathf.Pi));
        }
        var glow = Alert with { A = 0.25f * pulse };
        for (int i = 0; i < seg; i++) DrawLine(pts[i], pts[i + 1], glow, 4f);
        for (int i = 0; i < seg; i++) DrawLine(pts[i], pts[i + 1], Alert with { A = 0.6f + 0.4f * pulse }, 1.4f);
        DrawSparkBurst(SparkCenter(ca, cb), 6f, pulse);
    }

    private static Vector2 SparkCenter(Rect2 ca, Rect2 cb)
    {
        float y = Mathf.Min(ca.Position.Y, cb.Position.Y) - 7f;
        return new Vector2((ca.GetCenter().X + cb.GetCenter().X) * 0.5f, y);
    }

    // 작은 불꽃(여덟 갈래 별).
    private void DrawSparkBurst(Vector2 c, float r, float pulse)
    {
        DrawCircle(c, r * 0.9f, Alert with { A = 0.18f * pulse });
        for (int i = 0; i < 8; i++)
        {
            float ang = i * Mathf.Pi / 4f + _t * 1.5f;
            float len = (i % 2 == 0 ? r : r * 0.55f) * (0.8f + 0.2f * pulse);
            var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            DrawLine(c + d * 1.2f, c + d * len, i % 2 == 0 ? Alert : Warn, 1.4f);
        }
        DrawCircle(c, 1.8f, Ink);
    }

    // 방 칸 모서리 아이콘. 글꼴 기호 대신 직접 그린다(콘솔 글꼴에 없는 기호가 있다).
    private void DrawRelationIcon(Rect2 slot, PairBand band, float pulse)
    {
        var c = slot.GetCenter();
        float r = slot.Size.X * 0.5f;
        switch (band)
        {
            case PairBand.Refuse:
                DrawSparkBurst(c, r, pulse);
                break;
            case PairBand.Uneasy:
            {
                // 경고 삼각형 + 느낌표.
                var tri = new[] { c + new Vector2(0f, -r), c + new Vector2(r, r * 0.8f), c + new Vector2(-r, r * 0.8f) };
                DrawColoredPolygon(tri, Warn with { A = 0.25f });
                DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[0] }, Warn, 1.4f);
                DrawLine(c + new Vector2(0f, -r * 0.4f), c + new Vector2(0f, r * 0.25f), Warn, 1.6f);
                DrawCircle(c + new Vector2(0f, r * 0.55f), 1.1f, Warn);
                break;
            }
            case PairBand.Close:
            {
                // 하트 — 두 원 + 역삼각형.
                float s = r * 0.55f;
                DrawCircle(c + new Vector2(-s * 0.72f, -s * 0.35f), s * 0.78f, Heart);
                DrawCircle(c + new Vector2(s * 0.72f, -s * 0.35f), s * 0.78f, Heart);
                DrawColoredPolygon(new[] { c + new Vector2(-s * 1.45f, -s * 0.1f), c + new Vector2(s * 1.45f, -s * 0.1f),
                    c + new Vector2(0f, s * 1.5f) }, Heart);
                break;
            }
            case PairBand.Friendly:
                // 연결 고리 — 겹친 두 고리.
                DrawArc(c + new Vector2(-r * 0.32f, 0f), r * 0.5f, 0f, Mathf.Tau, 16, Mint, 1.4f);
                DrawArc(c + new Vector2(r * 0.32f, 0f), r * 0.5f, 0f, Mathf.Tau, 16, Mint, 1.4f);
                break;
        }
    }

    // 마우스가 관계 아이콘 · 스파크 위에 있으면 그 설명을 말풍선으로 띄운다.
    // 직원을 고르거나 끌고 방 위에 올리면, 놓았을 때 생길 관계를 미리 보여 준다.
    private List<(string Text, PairBand Band)> TooltipLines(FacilitySimulation sim, out Vector2 anchor)
    {
        var lines = new List<(string, PairBand)>();
        anchor = _mouse;

        string mover = _dragging ? _dragEmp : SelectedEmployeeId;
        if (!string.IsNullOrEmpty(mover) && !string.IsNullOrEmpty(HoverRoomId) && IsAssignable(sim, HoverRoomId))
        {
            foreach (var other in AssignedTo(sim, HoverRoomId))
            {
                if (other == mover) continue;
                var band = RelationshipSystem.Band(mover, other);
                if (band != PairBand.Neutral) lines.Add((PairText(sim, mover, other, band), band));
            }
            anchor = _dragging ? _lastPos : _mouse;
            return lines;
        }
        if (_dragging) return lines;

        foreach (var (roomId, _) in _cells)
        {
            if (!IsAssignable(sim, roomId)) continue;
            var pairs = RoomPairs(sim, roomId);
            if (pairs.Count == 0) continue;
            // 말풍선은 방 칸 바로 아래에 띄운다 — 칩과 스파크를 가리지 않게.
            var below = new Vector2(_mouse.X - 24f, _cells[roomId].End.Y - 10f);
            if (RelationSlotOf(roomId).Grow(3f).HasPoint(_mouse))
            {
                foreach (var (a, b, band) in pairs.OrderBy(p => p.Band)) lines.Add((PairText(sim, a, b, band), band));
                anchor = below;
                return lines;
            }
            foreach (var (a, b, band) in pairs)
            {
                if (band != PairBand.Refuse) continue;
                var ca = ChipOf(a); var cb = ChipOf(b);
                if (ca.Size == Vector2.Zero || cb.Size == Vector2.Zero) continue;
                var sc = SparkCenter(ca, cb);
                if (new Rect2(sc - new Vector2(9f, 9f), new Vector2(18f, 18f)).HasPoint(_mouse))
                {
                    lines.Add((PairText(sim, a, b, band), band));
                    anchor = below;
                    return lines;
                }
            }
        }
        return lines;
    }

    // 검증용 — 지금 떠 있는 말풍선 문구.
    public List<string> TooltipTexts() =>
        TooltipLines(FacilitySimulation.Instance, out _).Select(l => l.Text).ToList();

    private void DrawTooltip(FacilitySimulation sim)
    {
        var lines = TooltipLines(sim, out var anchor);
        if (lines.Count == 0) return;
        int fs = ViewFont.S(12);
        float w = lines.Max(l => _font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, fs).X) + 24f;
        float h = lines.Count * 20f + 10f;
        var box = new Rect2(anchor + new Vector2(14f, 16f), new Vector2(w, h));
        // 화면 밖으로 나가지 않게.
        if (box.End.X > Canvas.X - 12f) box.Position = new Vector2(Canvas.X - 12f - w, box.Position.Y);
        if (box.End.Y > Canvas.Y - 12f) box.Position = new Vector2(box.Position.X, anchor.Y - 12f - h);
        DrawRect(box, new Color(0.02f, 0.05f, 0.05f, 0.96f));
        var edge = lines.Any(l => l.Band == PairBand.Refuse) ? Alert : lines.Any(l => l.Band == PairBand.Uneasy) ? Warn : Mint;
        DrawRect(box, edge, false, 1.4f);
        float y = box.Position.Y + 20f;
        foreach (var (text, band) in lines)
        {
            DrawString(_font, new Vector2(box.Position.X + 12f, y), text, HorizontalAlignment.Left, w - 16f, fs, BandColor(band));
            y += 20f;
        }
    }

    private static Color BandColor(PairBand band) => band switch
    {
        PairBand.Refuse => Alert,
        PairBand.Uneasy => Warn,
        PairBand.Close => Heart,
        PairBand.Friendly => Mint,
        _ => Ink,
    };

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
        var refused = RefusedPairs(sim);
        var uneasy = sim.GetRoomIds().Where(r => IsAssignable(sim, r))
            .SelectMany(r => RoomPairs(sim, r)).Where(p => p.Band == PairBand.Uneasy).ToList();

        // 거부 > 코어 미배치 > 인원 순. 불편 쌍은 시작을 막지 않으므로 인원 줄 위에 따로 싣는다.
        string status = refused.Count > 0
            ? "✕ " + PairText(sim, refused[0].A, refused[0].B, PairBand.Refuse) + (refused.Count > 1 ? $" 외 {refused.Count - 1}건" : "")
            : !core
                ? "⚠ 코어실에 최소 1명의 직원을 배치해야 합니다."
                : missing > 0 ? $"{placed} / {total} 배치  ·  미배치 {missing}명" : $"{placed} / {total} 배치 완료";
        Color statusCol = refused.Count > 0 ? Alert : !core || missing > 0 ? Amber : Mint;
        DrawRect(new Rect2(24f, Canvas.Y - 104f, Canvas.X - 48f, 1f), Mint with { A = 0.16f });
        if (uneasy.Count > 0)
        {
            var u = uneasy[0];
            string warn = "⚠ " + PairText(sim, u.A, u.B, PairBand.Uneasy) + (uneasy.Count > 1 ? $" 외 {uneasy.Count - 1}쌍" : "");
            DrawString(_font, new Vector2(24f, Canvas.Y - 80f), warn, HorizontalAlignment.Left, 520f, ViewFont.S(11), Warn);
        }
        DrawString(_font, new Vector2(24f, Canvas.Y - 52f), status, HorizontalAlignment.Left, 520f,
            ViewFont.S(15), statusCol);
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

        // 마우스 이동(호버)은 _Input 이 추적한다 — PushInput 으로 들어오는 CRT 에서는 버튼을 누르지 않은
        // 이동이 _GuiInput 까지 오지 않는 경우가 있다.
        if (e is InputEventMouseMotion) return;

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
        if (!IsVisibleInTree()) return;
        // 이 뷰는 스케일 프레임(AddScaledView) 안에 있어 _Input 은 확대 좌표로 들어온다 — 로컬로 맞춘다.
        if (string.IsNullOrEmpty(_dragEmp))
        {
            // 끌지 않을 때의 이동 = 호버(방 강조 · 오른쪽 모니터 비교 · 관계 말풍선).
            if (MakeInputLocal(e) is InputEventMouseMotion hover)
            {
                _mouse = hover.Position;
                SetHover(RoomAt(hover.Position));
            }
            return;
        }
        e = MakeInputLocal(e);

        if (e is InputEventMouseMotion mm)
        {
            _lastPos = mm.Position;
            _mouse = mm.Position;
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
