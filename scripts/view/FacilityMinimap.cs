using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 왼쪽 CRT 안의 시설 미니맵. 방 = 네모 노드, 직원 = 점. 게임 상태를 읽기만 하고
// FacilitySimulation 에 배치 명령만 전달한다. 방 좌표를 SetRoomVisualCenter 로 등록해
// 기존 직원 이동 시뮬레이션이 이 미니맵 좌표계로 돌게 한다.
public partial class FacilityMinimap : Control
{
    public Action<string> OnRoomSelected;
    public Action<string> OnEmployeeSelected;

    public string SelectedRoomId = "";
    public string SelectedEmployeeId = "";

    // 직원 아이콘 반지름. 클릭 판정(EmployeeAt)도 이 값을 따라간다.
    private const float EmpDotRadius = 10f;

    // 방 배치 (미니맵 정규화 좌표). 사용자 스케치의 구조.
    // 환기실 / 의무실은 이번 버전에서 쓰지 않으므로 지도에 올리지 않는다
    // (데이터와 기능은 그대로 남아 있다 — 해금되면 여기 한 줄만 다시 넣으면 된다).
    private static readonly Dictionary<string, Vector2> Layout = new()
    {
        ["core_room"] = new(0.50f, 0.13f),
        ["power_room"] = new(0.17f, 0.32f),
        ["storage_room"] = new(0.83f, 0.32f),
        ["central_office"] = new(0.50f, 0.50f),
        ["guard_room"] = new(0.17f, 0.70f),
        ["maintenance_room"] = new(0.83f, 0.70f),
        ["isolation_room"] = new(0.50f, 0.88f),
        // 환기실 · 의무실 — 배치 지도(RoomDef.MapPosition)와 같은 쪽(왼쪽 아래 / 오른쪽 아래)에 둔다.
        ["vent_room"] = new(0.17f, 0.88f),
        ["medical_room"] = new(0.83f, 0.88f),
    };

    // 방 이름(위) / 직원 아이콘(가운데) / 직원 코드네임(아래)이 서로 안 겹치도록 잡은 크기.
    private static readonly Vector2 BoxSize = new(92f, 56f);

    private Font _font;
    private readonly HashSet<string> _seenCorridors = new();

    public override void _Ready()
    {
        _font = ViewFont.Default;
        MouseFilter = MouseFilterEnum.Stop;
        SetProcess(true);
        // 끌어다 놓기는 _GuiInput 밖(루트 _Input)에서 이동/뗌을 받아야 한다.
        SetProcessInput(true);
    }

    public override void _Process(double delta)
    {
        var sim = FacilitySimulation.Instance;
        if (sim != null)
        {
            foreach (var (roomId, _) in Layout)
                sim.SetRoomVisualCenter(roomId, CenterOf(roomId));
        }
        QueueRedraw();
    }

    private Vector2 CenterOf(string roomId) =>
        Layout.TryGetValue(roomId, out var n) ? n * Size : Size * 0.5f;

    private Rect2 BoxOf(string roomId) =>
        new(CenterOf(roomId) - BoxSize * 0.5f, BoxSize);

    // --- draw ---------------------------------------------------------

    public override void _Draw()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || _font == null) return;

        DrawCorridors(sim);

        foreach (var roomId in Layout.Keys)
            DrawRoom(sim, roomId);

        DrawDragHint(sim);

        foreach (var id in sim.GetEmployeeIds())
            DrawEmployee(sim, id);
    }

    private void DrawCorridors(FacilitySimulation sim)
    {
        // 성능: 매 프레임 도는 _Draw 라 HashSet 을 새로 만들지 않고 재사용한다.
        _seenCorridors.Clear();
        var seen = _seenCorridors;
        var col = new Color(0.30f, 0.37f, 0.33f);
        foreach (var roomId in Layout.Keys)
        {
            var def = sim.GetRoomDef(roomId);
            if (def == null) continue;
            foreach (var other in def.ConnectedRoomIds)
            {
                if (!Layout.ContainsKey(other)) continue;
                string key = string.CompareOrdinal(roomId, other) < 0 ? roomId + "|" + other : other + "|" + roomId;
                if (!seen.Add(key)) continue;

                // 직원 이동과 같은 규칙(CorridorElbow)으로 한 번 직각으로 꺾어 그려 정확히 겹치게 한다.
                // 중앙 제어실 ↔ 작업실은 안쪽 살, 작업실 ↔ 작업실은 바깥쪽 고리가 된다.
                Vector2 pa = CenterOf(roomId), pb = CenterOf(other);
                var elbow = CorridorElbow.Compute(pa, pb, CenterOf(FacilitySimulation.DeployOriginRoomId));
                if (elbow == null)
                {
                    DrawLine(pa, pb, col, 3f);
                }
                else
                {
                    DrawLine(pa, elbow.Value, col, 3f);
                    DrawLine(elbow.Value, pb, col, 3f);
                }
            }
        }
    }

    private void DrawRoom(FacilitySimulation sim, string roomId)
    {
        var def = sim.GetRoomDef(roomId);
        var state = sim.GetRoomState(roomId);
        if (def == null || state == null) return;

        Rect2 box = BoxOf(roomId);
        // 오늘 잠긴 작업실(환기실/의무실)은 배치도 업무도 사고도 없다 — 격리실처럼 어둡게만 둔다.
        bool inactive = !sim.IsRoomActive(roomId);
        bool dormant = def.IsRestricted || inactive;
        var tier = dormant ? RoomDangerTier.None : RoomStatusText.GetDangerTier(roomId);

        // 색은 두 단계뿐이다 — 주황(경고) / 빨강(사고 발생).
        // 단계 판정만 경고 단말기와 같은 IncidentBoard 를 쓴다.
        var incident = dormant ? null : NSP.Core.IncidentBoard.ForRoom(roomId);
        Color fill = incident?.State switch
        {
            NSP.Core.IncidentState.Active => new Color(0.55f, 0.09f, 0.09f)
                .Lerp(new Color(0.8f, 0.15f, 0.15f), 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 90f)),
            NSP.Core.IncidentState.Warning or NSP.Core.IncidentState.Caution
                => new Color(0.5f, 0.32f, 0.08f),
            _ => tier switch
            {
                RoomDangerTier.Failure => new Color(0.55f, 0.09f, 0.09f),
                RoomDangerTier.Unstable or RoomDangerTier.Delayed => new Color(0.5f, 0.32f, 0.08f),
                _ => dormant ? new Color(0.10f, 0.11f, 0.13f) : new Color(0.11f, 0.17f, 0.16f),
            },
        };
        DrawRect(box, fill);

        bool selected = roomId == SelectedRoomId;
        Color border = selected ? new Color(0.5f, 1f, 0.85f) : new Color(0.3f, 0.4f, 0.38f);
        DrawRect(box, border, false, selected ? 2.5f : 1.2f);
        if (state.Locked)
            DrawRect(box.Grow(3f), new Color(0.9f, 0.5f, 0.2f), false, 1.5f);

        // 방 이름은 상자 위쪽 — 가운데는 직원 아이콘 자리로 비워둔다.
        // (인원수 "● n" 표기는 아이콘이 곧 인원이라 지웠다. 아이콘과 겹쳐 읽기 힘들었다.)
        string name = def.DisplayName;
        DrawString(_font, box.Position + new Vector2(0f, 14f), name, HorizontalAlignment.Center,
            box.Size.X, ViewFont.S(12), inactive ? new Color(0.45f, 0.48f, 0.47f) : new Color(0.85f, 0.92f, 0.88f));
        if (inactive)
        {
            DrawString(_font, box.Position + new Vector2(0f, 32f), "비활성", HorizontalAlignment.Center,
                box.Size.X, ViewFont.S(11), new Color(0.42f, 0.45f, 0.44f));
            return;
        }

        // 발생 업무: 남은 시간 + 게이지.
        // 평소 작업은 방 '아래' 파란 게이지, 사고 수리는 방 '위' 빨간 게이지로 완전히
        // 갈라 놓는다 — 한 눈에 "지금 고치는 중인 방"을 찾을 수 있어야 한다.
        var st = sim.GetPrimarySpawnedTask(roomId);
        if (st is { Status: SpawnedTaskStatus.Active })
        {
            if (st.IsRepair) DrawRepairBar(box, st);
            else
            {
                float y = box.Position.Y + box.Size.Y + 4f;
                if (!st.Recurring)
                {
                    DrawString(_font, new Vector2(box.Position.X, y + 10f),
                        $"⏱ {Clock(st.Remaining)}", HorizontalAlignment.Center, box.Size.X, ViewFont.S(10),
                        st.Remaining < 8f ? new Color(1f, 0.4f, 0.3f) : new Color(0.9f, 0.8f, 0.4f));
                    y += 13f;
                }
                var barBg = new Rect2(box.Position.X + 6f, y, box.Size.X - 12f, 4f);
                DrawRect(barBg, new Color(0.1f, 0.1f, 0.1f));
                DrawRect(new Rect2(barBg.Position, new Vector2(barBg.Size.X * Mathf.Clamp(st.Ratio, 0f, 1f), 4f)),
                    new Color(0.4f, 0.75f, 0.92f));
            }
        }

        // 아직 사고가 아닌 경고 — 남은 시간과 필요한 인원을 방 위에 띄운다.
        // 수리 막대가 이미 그 자리를 쓰고 있으면 그쪽이 우선이다(이미 고장 난 방이다).
        if (st is not { Status: SpawnedTaskStatus.Active, IsRepair: true })
        {
            var risk = NSP.Core.IncidentBoard.ForRoom(roomId);
            if (risk is { State: NSP.Core.IncidentState.Warning } && risk.WarningRemainingSeconds >= 0f)
                DrawWarningBar(box, sim, roomId, risk);
        }

        if (TabooRuleSystemAtRisk(roomId))
            DrawString(_font, box.Position + new Vector2(0f, -4f), "⚠", HorizontalAlignment.Center, box.Size.X, ViewFont.S(14),
                new Color(1f, 0.75f, 0.2f));

        if (def.IsCoreRoom)
            DrawString(_font, new Vector2(box.Position.X, box.Position.Y - 14f),
                $"CORE {sim.CoreProgressPreview():0.0}%", HorizontalAlignment.Center, box.Size.X, ViewFont.S(11),
                new Color(0.5f, 0.8f, 1f));

        // 지금 이 인원이면 무엇이 달라지는가. 사람을 옮기는 즉시 이 줄이 바뀐다.
        string effect = sim.StaffingEffectLine(roomId);
        if (!string.IsNullOrEmpty(effect))
            DrawString(_font, box.Position + new Vector2(0f, box.Size.Y - 4f), effect,
                HorizontalAlignment.Center, box.Size.X, ViewFont.S(10),
                effect.Contains("불안정") || effect.Contains("정지")
                    ? new Color(1f, 0.72f, 0.35f)
                    : new Color(0.62f, 0.78f, 0.72f));
    }

    // 사고 수리 표시 — 방 바로 위에 붉은 진행 막대 한 줄. 글자는 막대 안에 넣는다.
    // (위아래 방 사이가 12px 뿐이라 막대와 글자를 따로 쌓으면 윗방을 덮는다.)
    // 아무도 붙어 있지 않아 게이지가 멈춰 있으면 "수리 필요"로 바꿔 부른다.
    private void DrawRepairBar(Rect2 box, SpawnedTask st)
    {
        const float BarH = 15f;
        bool working = st.Progressing;
        // 맨 윗줄(코어실)은 위쪽 여백이 13px 뿐이라 그대로 두면 화면 밖으로 나간다.
        float top = Mathf.Max(box.Position.Y - BarH - 2f, 1f);

        var bar = new Rect2(box.Position.X + 4f, top, box.Size.X - 8f, BarH);
        DrawRect(bar, new Color(0.17f, 0.05f, 0.05f, 0.96f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(st.Ratio, 0f, 1f), BarH)),
            new Color(0.84f, 0.17f, 0.14f));
        DrawRect(bar, new Color(1f, 0.48f, 0.40f, 0.8f), false, 1f);

        // 수리 중일 때만 글자가 맥동한다 — 멈춰 있으면 가만히 떠 있다.
        float a = working ? 0.75f + 0.25f * Mathf.Sin(Time.GetTicksMsec() / 170f) : 1f;
        DrawString(_font, new Vector2(bar.Position.X, bar.Position.Y + 12f),
            working ? "수 리 중" : "수리 필요", HorizontalAlignment.Center, bar.Size.X,
            ViewFont.S(10), new Color(1f, 0.94f, 0.92f, a));
    }

    // 경고 표시 — 방 바로 위에 남은 시간 막대와 "몇 초 · 현재/필요 인원".
    // 이 막대가 차 있는 동안에는 아직 사고가 아니다. 인원을 채우면 그대로 사라진다.
    private void DrawWarningBar(Rect2 box, FacilitySimulation sim, string roomId,
        NSP.Core.IncidentDisplayData risk)
    {
        const float BarH = 15f;
        float top = Mathf.Max(box.Position.Y - BarH - 2f, 1f);
        var bar = new Rect2(box.Position.X + 4f, top, box.Size.X - 8f, BarH);

        // 남은 시간이 줄어드는 만큼 막대가 빈다.
        float total = risk.WarningTotalSeconds > 0f ? risk.WarningTotalSeconds : 20f;
        float ratio = Mathf.Clamp(risk.WarningRemainingSeconds / total, 0f, 1f);
        DrawRect(bar, new Color(0.18f, 0.12f, 0.02f, 0.96f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * ratio, BarH)),
            new Color(0.95f, 0.62f, 0.12f));
        DrawRect(bar, new Color(1f, 0.78f, 0.35f, 0.85f), false, 1f);

        int here = sim.OnDutyCount(roomId);
        int need = Mathf.Max(1, risk.RepairWorkers);
        float a = 0.7f + 0.3f * Mathf.Sin(Time.GetTicksMsec() / 150f);
        // 글자가 막대의 채워진 쪽과 빈 쪽에 걸쳐 놓이므로, 어두운 그림자를 먼저 깔고
        // 밝은 글자를 얹어 양쪽 배경에서 모두 읽히게 한다.
        string text = $"⚠ {risk.WarningRemainingSeconds:0}s · {here}/{need}";
        var at = new Vector2(bar.Position.X, bar.Position.Y + 12f);
        DrawString(_font, at + new Vector2(1f, 1f), text, HorizontalAlignment.Center,
            bar.Size.X, ViewFont.S(10), new Color(0.08f, 0.05f, 0f, 0.9f));
        DrawString(_font, at, text, HorizontalAlignment.Center,
            bar.Size.X, ViewFont.S(10), new Color(1f, 0.96f, 0.86f, a));
    }

    // 끌고 있는 동안 대상 작업실을 밝히고, 커서 자리에 직원 색 점을 따라 그린다.
    private void DrawDragHint(FacilitySimulation sim)
    {
        if (!_dragging || string.IsNullOrEmpty(_dragEmp)) return;

        string hover = RoomAt(_lastDragPos);
        if (hover != null)
        {
            bool ok = sim.CanAssignToRoom(hover)
                      && sim.GetEmployeeState(_dragEmp)?.AssignedRoomId != hover;
            DrawRect(BoxOf(hover).Grow(3f),
                ok ? new Color(0.45f, 1f, 0.8f, 0.95f) : new Color(1f, 0.4f, 0.3f, 0.9f), false, 2.4f);
        }

        var def = sim.GetEmployeeDef(_dragEmp);
        Color c = def?.IconColor ?? Colors.White;
        DrawCircle(_lastDragPos, EmpDotRadius, new Color(c.R, c.G, c.B, 0.85f));
        DrawCircle(_lastDragPos, EmpDotRadius, new Color(1f, 1f, 1f, 0.9f), false, 1.6f);
    }

    private static bool TabooRuleSystemAtRisk(string roomId) =>
        NSP.Taboo.TabooRuleSystem.Instance?.IsRoomAtTabooRisk(roomId) ?? false;

    private void DrawEmployee(FacilitySimulation sim, string id)
    {
        var st = sim.GetEmployeeState(id);
        var def = sim.GetEmployeeDef(id);
        if (st == null || def == null) return;

        // 근무 배치에서 빠진 직원은 오늘 근무자가 아니다 — 지도에 띄우지 않는다.
        // (격리된 직원은 배치가 해제되지만 위치는 계속 보여야 하므로 예외.)
        if (string.IsNullOrEmpty(st.AssignedRoomId) && !st.Isolated) return;

        // LIGHTING이 꺼지면(정전 등) 비상 조명이 없는 방의 직원 아이콘은 안 보인다 — 직원을
        // 지우거나 옮기는 게 아니라, 관리자가 위치를 볼 수 없게 되는 것뿐이다(데이터/시뮬레이션은
        // 그대로 유지).
        bool lightingOk = NSP.Core.GameState.Instance?.IsConsumerPowered(NSP.Data.PowerConsumer.Lighting) ?? true;
        if (!lightingOk && !(sim.GetRoomDef(st.CurrentRoomId)?.HasEmergencyLighting ?? false))
            return;

        // 금기 페널티(위치 두절) 중인 직원도 지도에서 사라진다 — 데이터는 그대로다.
        if (NSP.Taboo.TabooRuleSystem.Instance?.IsTrackingLost(id) == true) return;

        Vector2 p = st.Position;
        Color c = st.Alive ? def.IconColor : new Color(0.35f, 0.35f, 0.35f);

        // 직원 아이콘은 고유색으로 구분한다 — 작게 그리면 색이 안 읽히므로 넉넉한 크기로.
        if (id == SelectedEmployeeId)
            DrawCircle(p, EmpDotRadius + 4f, new Color(1f, 1f, 1f, 0.9f), false, 2.4f);
        DrawCircle(p, EmpDotRadius, c);
        DrawCircle(p, EmpDotRadius, new Color(0f, 0f, 0f, 0.7f), false, 1.6f);

        // 코드네임은 아이콘 아래 — 방 이름(상자 위쪽)과 부딪히지 않는다.
        DrawString(_font, p + new Vector2(-30f, EmpDotRadius + 12f), def.Codename, HorizontalAlignment.Center, 60f, ViewFont.S(11),
            new Color(0.95f, 0.95f, 0.8f));
        if (st.Isolated)
            DrawString(_font, p + new Vector2(-30f, EmpDotRadius + 23f), "[격리]", HorizontalAlignment.Center, 60f, ViewFont.S(9),
                new Color(0.9f, 0.5f, 0.9f));
    }

    private static string Clock(float s)
    {
        int t = Mathf.CeilToInt(Mathf.Max(0f, s));
        return $"{t / 60:0}:{t % 60:00}";
    }

    // --- input -------------------------------------------------------

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        string empHit = EmployeeAt(sim, mb.Position);
        if (empHit != null)
        {
            var st = sim.GetEmployeeState(empHit);
            // 살아 있고 격리되지 않았으면 끌어다 놓을 수 있다. 선택은 손을 뗄 때 처리한다.
            if (st is { Alive: true, Isolated: false })
            {
                _dragEmp = empHit;
                _pressPos = mb.Position;
                _lastDragPos = mb.Position;
                _dragging = false;
            }
            else OnEmployeeSelected?.Invoke(empHit);
            AcceptEvent();
            return;
        }

        string roomHit = RoomAt(mb.Position);
        if (roomHit != null)
        {
            // 오늘 잠긴 작업실은 선택해도 볼 것이 없다 — 인스펙터/CCTV를 그쪽으로 돌리지 않는다.
            if (sim.IsRoomActive(roomHit)) OnRoomSelected?.Invoke(roomHit);
            AcceptEvent();
        }
    }

    // 같은 방에 여러 명이 겹쳐 있어도 클릭 지점에 가장 가까운 직원을 집는다.
    private string EmployeeAt(FacilitySimulation sim, Vector2 pos)
    {
        string best = null;
        float bestDist = float.MaxValue;
        foreach (var id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st == null) continue;
            float d = st.Position.DistanceTo(pos);
            if (d <= EmpDotRadius + 10f && d < bestDist) { best = id; bestDist = d; }
        }
        return best;
    }

    private string RoomAt(Vector2 pos)
    {
        foreach (var roomId in Layout.Keys)
            if (BoxOf(roomId).HasPoint(pos))
                return roomId;
        return null;
    }

    // 상자를 살짝 벗어난 곳에 놓아도 가장 가까운 작업실로 들어간다.
    private string NearestRoom(Vector2 pos)
    {
        string best = null;
        float bestDist = 70f;
        foreach (var roomId in Layout.Keys)
        {
            float d = CenterOf(roomId).DistanceTo(pos);
            if (d >= bestDist) continue;
            bestDist = d;
            best = roomId;
        }
        return best;
    }

    // --- 직원 끌어다 놓기 (수동 구현) --------------------------------------
    //
    // Godot 기본 DnD(_GetDragData/_CanDropData)는 이 미니맵처럼 SubViewport 안에서
    // PushInput 으로 입력을 받는 화면에서는 시작조차 되지 않는다. 게다가 _GuiInput 이
    // 눌림을 AcceptEvent 로 먹어버려서 드래그가 아예 걸리지 않았다.
    // 배치표(ScheduleBoardUI)와 같은 방식으로 눌림 → 이동 → 뗌을 직접 추적한다.
    private string _dragEmp = "";
    private Vector2 _pressPos, _lastDragPos;
    private bool _dragging;

    public bool IsDraggingEmployee => _dragging;

    public override void _Input(InputEvent e)
    {
        if (string.IsNullOrEmpty(_dragEmp)) return;

        // 이 뷰는 스케일 프레임 안에 있다 — 입력이 뷰포트(확대) 좌표로 들어오므로 로컬로 바꾼다.
        e = MakeInputLocal(e);

        if (e is InputEventMouseMotion mm)
        {
            _lastDragPos = mm.Position;
            if (!_dragging && mm.Position.DistanceTo(_pressPos) > 6f) _dragging = true;
            if (_dragging) QueueRedraw();
        }
        else if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            if (_dragging) DropEmployee(_lastDragPos);
            else OnEmployeeSelected?.Invoke(_dragEmp);   // 끌지 않았으면 그냥 선택
            _dragEmp = "";
            _dragging = false;
            QueueRedraw();
        }
    }

    private void DropEmployee(Vector2 pos)
    {
        var sim = FacilitySimulation.Instance;
        string roomId = RoomAt(pos) ?? NearestRoom(pos);
        if (sim == null || roomId == null) { OnEmployeeSelected?.Invoke(_dragEmp); return; }

        var emp = sim.GetEmployeeState(_dragEmp);
        if (emp == null || !emp.Alive || emp.Isolated) return;
        if (emp.AssignedRoomId == roomId) return;
        // 근무 중 재배치는 근무표 정원(RoomSlotCapacity)에 묶이지 않는다.
        // 사고가 나면 한 방에 셋을 몰아넣어야 할 때가 있고, 그게 이 게임의 조작이다.
        if (!sim.IsRoomActive(roomId)) return;

        // ClearAssignment 없이 바로 재배치 — AssignToRoom 이 이동까지 처리한다.
        sim.AssignToRoom(_dragEmp, roomId);
        OnEmployeeSelected?.Invoke(_dragEmp);
    }

    private void UnusedDropData(Vector2 atPosition, Variant data)
    {
        var sim = FacilitySimulation.Instance;
        string roomId = RoomAt(atPosition);
        if (sim == null || roomId == null) return;
        sim.AssignToRoom(data.AsString(), roomId);
        OnRoomSelected?.Invoke(roomId);
    }
}
