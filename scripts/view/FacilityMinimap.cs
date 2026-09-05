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
    private static readonly Dictionary<string, Vector2> Layout = new()
    {
        ["core_room"] = new(0.50f, 0.12f),
        ["guard_room"] = new(0.23f, 0.30f),
        ["storage_room"] = new(0.77f, 0.30f),
        ["power_room"] = new(0.19f, 0.50f),
        ["central_office"] = new(0.50f, 0.50f),
        ["maintenance_room"] = new(0.81f, 0.50f),
        ["vent_room"] = new(0.28f, 0.72f),
        ["medical_room"] = new(0.72f, 0.72f),
        ["isolation_room"] = new(0.50f, 0.90f),
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

                // 직원 이동은 FacilitySimulation.ComputeElbowWaypoint 규칙으로 한 번 직각으로 꺾인다.
                // 회색 통로도 같은 규칙(위쪽 방 X, 아래쪽 방 Y)으로 두 마디로 그려 정확히 겹치게 한다.
                Vector2 pa = CenterOf(roomId), pb = CenterOf(other);
                if (Mathf.Abs(pa.X - pb.X) < 1f || Mathf.Abs(pa.Y - pb.Y) < 1f)
                {
                    DrawLine(pa, pb, col, 3f);
                }
                else
                {
                    Vector2 upper = pa.Y <= pb.Y ? pa : pb;
                    Vector2 lower = pa.Y <= pb.Y ? pb : pa;
                    Vector2 elbow = new(upper.X, lower.Y);
                    DrawLine(upper, elbow, col, 3f);
                    DrawLine(elbow, lower, col, 3f);
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
        var tier = def.IsRestricted ? RoomDangerTier.None : RoomStatusText.GetDangerTier(roomId);

        Color fill = tier switch
        {
            RoomDangerTier.Failure => new Color(0.55f, 0.09f, 0.09f)
                .Lerp(new Color(0.8f, 0.15f, 0.15f), 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 90f)),
            RoomDangerTier.Unstable => new Color(0.5f, 0.32f, 0.08f),
            RoomDangerTier.Delayed => new Color(0.36f, 0.32f, 0.12f),
            _ => def.IsRestricted ? new Color(0.10f, 0.11f, 0.13f) : new Color(0.11f, 0.17f, 0.16f),
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
            box.Size.X, 12, new Color(0.85f, 0.92f, 0.88f));

        // 발생 업무: 남은 시간 + 게이지
        var st = sim.GetPrimarySpawnedTask(roomId);
        if (st is { Status: SpawnedTaskStatus.Active })
        {
            float y = box.Position.Y + box.Size.Y + 4f;
            if (st.IsRepair)
            {
                DrawString(_font, new Vector2(box.Position.X, y + 10f),
                    "🔧 수리 필요", HorizontalAlignment.Center, box.Size.X, 10, new Color(1f, 0.55f, 0.3f));
                y += 13f;
            }
            else if (!st.Recurring)
            {
                DrawString(_font, new Vector2(box.Position.X, y + 10f),
                    $"⏱ {Clock(st.Remaining)}", HorizontalAlignment.Center, box.Size.X, 10,
                    st.Remaining < 8f ? new Color(1f, 0.4f, 0.3f) : new Color(0.9f, 0.8f, 0.4f));
                y += 13f;
            }
            var barBg = new Rect2(box.Position.X + 6f, y, box.Size.X - 12f, 4f);
            DrawRect(barBg, new Color(0.1f, 0.1f, 0.1f));
            DrawRect(new Rect2(barBg.Position, new Vector2(barBg.Size.X * Mathf.Clamp(st.Ratio, 0f, 1f), 4f)),
                new Color(0.4f, 0.75f, 0.92f));
        }

        if (TabooRuleSystemAtRisk(roomId))
            DrawString(_font, box.Position + new Vector2(0f, -4f), "⚠", HorizontalAlignment.Center, box.Size.X, 14,
                new Color(1f, 0.75f, 0.2f));

        if (def.IsCoreRoom)
            DrawString(_font, new Vector2(box.Position.X, box.Position.Y - 14f),
                $"CORE {NSP.Core.GameState.Instance.CoreProgress:0}%", HorizontalAlignment.Center, box.Size.X, 11,
                new Color(0.5f, 0.8f, 1f));
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

        Vector2 p = st.Position;
        Color c = st.Alive ? def.IconColor : new Color(0.35f, 0.35f, 0.35f);

        // 직원 아이콘은 고유색으로 구분한다 — 작게 그리면 색이 안 읽히므로 넉넉한 크기로.
        if (id == SelectedEmployeeId)
            DrawCircle(p, EmpDotRadius + 4f, new Color(1f, 1f, 1f, 0.9f), false, 2.4f);
        DrawCircle(p, EmpDotRadius, c);
        DrawCircle(p, EmpDotRadius, new Color(0f, 0f, 0f, 0.7f), false, 1.6f);

        // 코드네임은 아이콘 아래 — 방 이름(상자 위쪽)과 부딪히지 않는다.
        DrawString(_font, p + new Vector2(-30f, EmpDotRadius + 12f), def.Codename, HorizontalAlignment.Center, 60f, 11,
            new Color(0.95f, 0.95f, 0.8f));
        if (st.Isolated)
            DrawString(_font, p + new Vector2(-30f, EmpDotRadius + 23f), "[격리]", HorizontalAlignment.Center, 60f, 9,
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
            OnEmployeeSelected?.Invoke(empHit);
            AcceptEvent();
            return;
        }

        string roomHit = RoomAt(mb.Position);
        if (roomHit != null)
        {
            OnRoomSelected?.Invoke(roomHit);
            AcceptEvent();
        }
    }

    private string EmployeeAt(FacilitySimulation sim, Vector2 pos)
    {
        foreach (var id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st != null && st.Position.DistanceTo(pos) <= EmpDotRadius + 5f)
                return id;
        }
        return null;
    }

    private string RoomAt(Vector2 pos)
    {
        foreach (var roomId in Layout.Keys)
            if (BoxOf(roomId).HasPoint(pos))
                return roomId;
        return null;
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return default;
        string id = EmployeeAt(sim, atPosition);
        if (id == null) return default;
        var st = sim.GetEmployeeState(id);
        if (st == null || !st.Alive || st.Isolated) return default;

        var prev = new ColorRect { Color = sim.GetEmployeeDef(id).IconColor, Size = new Vector2(16f, 16f) };
        SetDragPreview(prev);
        return id;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.String) return false;
        var sim = FacilitySimulation.Instance;
        string roomId = RoomAt(atPosition);
        if (sim == null || roomId == null) return false;
        var emp = sim.GetEmployeeState(data.AsString());
        return emp != null && emp.Alive && !emp.Isolated && emp.AssignedRoomId != roomId && sim.CanAssignToRoom(roomId);
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var sim = FacilitySimulation.Instance;
        string roomId = RoomAt(atPosition);
        if (sim == null || roomId == null) return;
        sim.AssignToRoom(data.AsString(), roomId);
        OnRoomSelected?.Invoke(roomId);
    }
}
