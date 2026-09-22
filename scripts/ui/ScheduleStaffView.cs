using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Ui;

// 근무 배치 — 오른쪽 CRT(모니터 2). 타이틀의 STAFF IDENTIFICATION 화면과 같은 톤으로
// 왼쪽 지도(ScheduleMapView)에서 고른 직원 · 작업실의 정보를 보여 준다. 조작은 받지 않는다.
//
// 보여 주는 값은 종이 배치표의 정보 패널과 같다 — 새 수치를 만들지 않는다.
//   직원     : 얼굴 · 코드네임 · 특성(EmployeeDef.Trait) · 능력치(해금된 날) 또는 오늘의 기분 · 현재 배치
//   작업실   : 요구 능력(해금된 날) · 인원별 효과(OpsProfile RoleNote) 또는 권장 인원 · 수리 최소 인원 · 설명
//   비교     : 직원을 고른/끄는 채 작업실 위에 있으면 그 방 요구 능력에 대한 적합도(해금된 날)
//   아무것도 : 오늘 근무자 신원 카드 그리드(누가 어디 배치됐는지 한눈에)
public partial class ScheduleStaffView : Control
{
    private static readonly Vector2 Canvas = new(800f, 600f);
    private static readonly Color Bg = new(0.018f, 0.028f, 0.030f);
    private static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    private static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    private static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    private static readonly Color Amber = new(0.95f, 0.72f, 0.25f);
    private static readonly Color Err = new(0.92f, 0.28f, 0.24f);
    private static readonly Color Good = new(0.45f, 0.95f, 0.55f);

    private Font _font;
    private float _t;

    public override void _Ready()
    {
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        CrtGlass.Draw(this, new Rect2(Vector2.Zero, Canvas));   // 빛나는 유리 바탕 — UI 는 전부 이 위에 그린다
        var sim = FacilitySimulation.Instance;
        var map = ScheduleMapView.Instance;
        if (sim == null || _font == null) { Scanlines(); return; }

        DrawRect(new Rect2(16f, 14f, Canvas.X - 32f, Canvas.Y - 28f), Mint with { A = 0.22f }, false, 1.4f);

        string emp = map?.DraggingEmployeeId ?? "";
        if (string.IsNullOrEmpty(emp)) emp = map?.SelectedEmployeeId ?? "";
        string hover = map?.HoverRoomId ?? "";

        if (DayFeatures.StatsEnabled && !string.IsNullOrEmpty(emp) && !string.IsNullOrEmpty(hover)
            && ScheduleMapView.IsAssignable(sim, hover))
            DrawCompare(sim, emp, hover);
        else if (!string.IsNullOrEmpty(map?.FocusEmployeeId))
            DrawEmployee(sim, map.FocusEmployeeId);
        else if (!string.IsNullOrEmpty(map?.FocusRoomId))
            DrawRoom(sim, map.FocusRoomId);
        else
            DrawRosterGrid(sim);

        Scanlines();
    }

    // ── 머리말 ───────────────────────────────────────────────────────────

    private void Header(string title, string sub)
    {
        DrawString(_font, new Vector2(48f, 66f), title, HorizontalAlignment.Left, 560f, ViewFont.S(20), Mint);
        DrawString(_font, new Vector2(48f, 92f), sub, HorizontalAlignment.Left, 640f, ViewFont.S(13), Dim);
        DrawRect(new Rect2(48f, 108f, Canvas.X - 96f, 1f), Mint with { A = 0.20f });
    }

    private void Footer(string line, Color col)
    {
        DrawRect(new Rect2(48f, 506f, Canvas.X - 96f, 1f), Mint with { A = 0.18f });
        DrawString(_font, new Vector2(48f, 538f), line, HorizontalAlignment.Left, Canvas.X - 96f, ViewFont.S(15), col);
    }

    // ── 기본: 신원 카드 그리드 ────────────────────────────────────────────

    private void DrawRosterGrid(FacilitySimulation sim)
    {
        var roster = sim.GetActiveEmployeeIds();
        Header("STAFF IDENTIFICATION", "제7지하시설 · 야간근무 편성");
        DrawString(_font, new Vector2(Canvas.X - 200f, 70f), $"{roster.Count} / {roster.Count}",
            HorizontalAlignment.Right, 152f, ViewFont.S(16), Dim);

        const float left = 82f, top = 132f, w = 208f, h = 168f, gx = 16f, gy = 14f;
        for (int i = 0; i < roster.Count && i < 6; i++)
        {
            var def = sim.GetEmployeeDef(roster[i]);
            var st = sim.GetEmployeeState(roster[i]);
            if (def == null || st == null) continue;
            var r = new Rect2(left + (i % 3) * (w + gx), top + (i / 3) * (h + gy), w, h);
            bool assigned = !string.IsNullOrEmpty(st.AssignedRoomId);

            DrawRect(r, new Color(0.04f, 0.09f, 0.10f, 0.9f));
            DrawRect(r, (assigned ? Mint : Dim) with { A = assigned ? 0.6f : 0.4f }, false, 1.2f);
            var face = new Rect2(r.Position.X + (w - 72f) * 0.5f, r.Position.Y + 14f, 72f, 72f);
            Portrait(def, face);
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 112f), def.Codename,
                HorizontalAlignment.Center, w, ViewFont.S(19), Ink);
            string where = assigned ? "→ " + RoomName(sim, st.AssignedRoomId) : "미배치";
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 140f), where,
                HorizontalAlignment.Center, w, ViewFont.S(13), assigned ? Mint : Amber);
        }
        Footer("직원 또는 작업실을 선택하면 상세 정보가 표시됩니다.", Dim);
    }

    // ── 직원 ─────────────────────────────────────────────────────────────

    private void DrawEmployee(FacilitySimulation sim, string id)
    {
        var def = sim.GetEmployeeDef(id);
        var st = sim.GetEmployeeState(id);
        if (def == null || st == null) { DrawRosterGrid(sim); return; }

        Header("STAFF IDENTIFICATION  ·  " + id.ToUpperInvariant(), "시설 직원 신원 확인됨.");

        // 초상(스탠딩 원화가 있으면 크게, 없으면 얼굴).
        var box = new Rect2(56f, 128f, 230f, 360f);
        DrawRect(box, new Color(0.03f, 0.07f, 0.08f, 0.9f));
        DrawRect(box, Dim with { A = 0.55f }, false, 1.2f);
        var tex = def.StandingImage ?? def.FacePortrait;
        if (tex != null) Contain(tex, box.Grow(-8f));
        else DrawCircle(box.GetCenter(), 50f, def.IconColor);

        float x = 316f, y = 150f;
        DrawRect(new Rect2(x, y - 22f, 4f, 30f), def.IconColor);
        DrawString(_font, new Vector2(x + 14f, y), def.Codename, HorizontalAlignment.Left, 420f, ViewFont.S(30), Ink);
        y += 34f;
        if (!string.IsNullOrEmpty(def.Trait))
        {
            DrawString(_font, new Vector2(x, y), "특성   " + def.Trait, HorizontalAlignment.Left, 440f, ViewFont.S(16), Mint);
            y += 32f;
        }

        if (DayFeatures.StatsEnabled)
        {
            Stat("기술", def.Tech, x, y); y += 30f;
            Stat("담력", def.Courage, x, y); y += 30f;
            Stat("관찰", def.Observation, x, y); y += 40f;
        }
        else
        {
            // 능력치가 잠긴 날 — 오늘의 기분이 주 정보다(직원 본인의 자기보고).
            DrawString(_font, new Vector2(x, y), "오늘의 기분", HorizontalAlignment.Left, 440f, ViewFont.S(14), Dim);
            y += 30f;
            string mood = sim.GetDailyMood(id);
            DrawString(_font, new Vector2(x, y), string.IsNullOrEmpty(mood) ? "—" : mood,
                HorizontalAlignment.Left, 440f, ViewFont.S(24), Amber);
            y += 26f;
            DrawString(_font, new Vector2(x, y), "※ 직원 본인이 근무 전에 적어 낸 자기보고입니다.",
                HorizontalAlignment.Left, 440f, ViewFont.S(11), Dim);
            y += 34f;
        }

        string room = st.AssignedRoomId;
        DrawString(_font, new Vector2(x, y), "현재 배치", HorizontalAlignment.Left, 440f, ViewFont.S(14), Dim);
        y += 30f;
        DrawString(_font, new Vector2(x, y), string.IsNullOrEmpty(room) ? "미배치" : RoomName(sim, room),
            HorizontalAlignment.Left, 440f, ViewFont.S(22), string.IsNullOrEmpty(room) ? Amber : Mint);

        bool selected = ScheduleMapView.Instance?.SelectedEmployeeId == id;
        Footer(selected ? $"{def.Codename} 선택됨 — 배치할 작업실을 누르십시오." : $"{def.Codename}   시설 직원 신원 확인됨.",
            selected ? Mint : Ink);
    }

    private void Stat(string label, int v, float x, float y)
    {
        DrawString(_font, new Vector2(x, y), label, HorizontalAlignment.Left, 60f, ViewFont.S(16), Ink);
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect2(x + 64f + i * 26f, y - 15f, 20f, 16f);
            DrawRect(r, i < v ? Mint : Mint with { A = 0.14f });
        }
        DrawString(_font, new Vector2(x + 150f, y), v.ToString(), HorizontalAlignment.Left, 40f, ViewFont.S(16), Dim);
    }

    // ── 작업실 ───────────────────────────────────────────────────────────

    private void DrawRoom(FacilitySimulation sim, string roomId)
    {
        var def = sim.GetRoomDef(roomId);
        if (def == null) { DrawRosterGrid(sim); return; }
        bool assignable = ScheduleMapView.IsAssignable(sim, roomId);

        Header("ROOM  ·  " + def.DisplayName, assignable ? "배치 가능 작업실" : !sim.IsRoomActive(roomId)
            ? "비활성 — " + ScheduleMapView.LockedLabel(def) : "제한 구역 — 배치할 수 없음");

        float x = 56f, y = 150f;
        if (!assignable)
        {
            DrawDescription(roomId, x, y);
            Footer("이 작업실에는 직원을 배치할 수 없습니다.", Dim);
            return;
        }

        if (DayFeatures.StatsEnabled)
        {
            var stats = sim.GetRoomTasksInPriorityOrder(roomId).Select(t => t.RequiredStat).Distinct();
            DrawString(_font, new Vector2(x, y), "요구 능력   " + string.Join("  ·  ", stats.Select(StatLabel)),
                HorizontalAlignment.Left, 680f, ViewFont.S(16), Amber);
            y += 34f;
        }

        var here = ScheduleMapView.AssignedTo(sim, roomId);
        var ops = OpsProfile.Room(roomId);
        if (ops != null && !string.IsNullOrWhiteSpace(ops.RoleNote))
        {
            DrawString(_font, new Vector2(x, y), $"인원별 효과   (현재 {here.Count}명)", HorizontalAlignment.Left,
                680f, ViewFont.S(16), Mint);
            y += 28f;
            foreach (string line in ops.RoleNote.Split(" / ").Take(4))
            {
                DrawString(_font, new Vector2(x + 8f, y), "· " + line.Trim(), HorizontalAlignment.Left, 680f,
                    ViewFont.S(14), Ink);
                y += 24f;
            }
        }
        else
        {
            DrawString(_font, new Vector2(x, y),
                $"권장 인원   {ScheduleMapView.RecommendedHeadcount(sim, roomId)}명   (현재 {here.Count}명)",
                HorizontalAlignment.Left, 680f, ViewFont.S(16), Mint);
            y += 28f;
        }
        y += 6f;
        DrawString(_font, new Vector2(x, y), $"사고 수리 최소 인원   {RoomStaffing.RepairMinWorkers(roomId, def)}명",
            HorizontalAlignment.Left, 680f, ViewFont.S(15), Dim);
        y += 34f;

        DrawDescription(roomId, x, y);

        string who = here.Count == 0 ? "배치된 직원 없음"
            : "배치 : " + string.Join(" · ", here.Select(e => sim.GetEmployeeDef(e)?.Codename ?? e));
        Footer(who, here.Count == 0 ? Amber : Ink);
    }

    // 작업실 설명 — 2D 방 카드(RoomDetailCard)와 같은 문장을 그대로 쓴다.
    private void DrawDescription(string roomId, float x, float y)
    {
        string desc = RoomDetailCard.Descriptions.GetValueOrDefault(roomId, "");
        if (string.IsNullOrEmpty(desc)) return;
        // 줄바꿈 — DrawMultilineString 으로 폭 안에서 접는다.
        DrawMultilineString(_font, new Vector2(x, y), desc, HorizontalAlignment.Left, 688f, ViewFont.S(14),
            4, Ink with { A = 0.85f }, TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Mandatory);
    }

    // ── 적합도 비교 ──────────────────────────────────────────────────────

    private void DrawCompare(FacilitySimulation sim, string emp, string roomId)
    {
        var edef = sim.GetEmployeeDef(emp);
        var rdef = sim.GetRoomDef(roomId);
        if (edef == null || rdef == null) { DrawRosterGrid(sim); return; }

        Header("ASSIGNMENT CHECK", $"{edef.Codename}  →  {rdef.DisplayName}");
        var stats = sim.GetRoomTasksInPriorityOrder(roomId).Select(t => t.RequiredStat).Distinct().ToList();
        var primary = stats.Count > 0 ? stats[0] : StatType.Tech;
        int value = edef.GetStat(primary);

        var face = new Rect2(56f, 140f, 120f, 120f);
        DrawRect(face, new Color(0.03f, 0.07f, 0.08f, 0.9f));
        Portrait(edef, face);

        float x = 204f, y = 170f;
        DrawString(_font, new Vector2(x, y), $"{rdef.DisplayName} 요구 능력 : {StatLabel(primary)}",
            HorizontalAlignment.Left, 540f, ViewFont.S(17), Ink);
        y += 34f;
        DrawString(_font, new Vector2(x, y), $"{edef.Codename} 의 {StatLabel(primary)}", HorizontalAlignment.Left,
            200f, ViewFont.S(16), Dim);
        Stat("", value, x + 150f, y);

        // 업무 적합도 3단계 — 종이 배치표 · FacilitySimulation.StatWorkRate 와 같은 구간.
        var (text, col, note) = value switch
        {
            >= 3 => ("✓ 적합", Good, "업무 속도 조금 빠름"),
            2 => ("○ 보통", Amber, "기준 속도"),
            _ => ("△ 비효율", Err, "업무 속도 크게 느림"),
        };
        DrawString(_font, new Vector2(x, y + 64f), text, HorizontalAlignment.Left, 300f, ViewFont.S(28), col);
        DrawString(_font, new Vector2(x, y + 94f), note, HorizontalAlignment.Left, 400f, ViewFont.S(14), Dim);
        Footer("놓으면 이 작업실에 배치됩니다.", Mint);
    }

    // ── 공통 ─────────────────────────────────────────────────────────────

    private void Portrait(EmployeeDef def, Rect2 box)
    {
        DrawRect(box, new Color(0.02f, 0.05f, 0.06f, 0.9f));
        if (def.FacePortrait != null) Contain(def.FacePortrait, box);
        else DrawCircle(box.GetCenter(), box.Size.X * 0.3f, def.IconColor);
        DrawRect(box, Dim with { A = 0.6f }, false, 1f);
    }

    private void Contain(Texture2D tex, Rect2 box)
    {
        var src = tex.GetSize();
        if (src.X <= 0f || src.Y <= 0f) return;
        float k = Mathf.Min(box.Size.X / src.X, box.Size.Y / src.Y);
        var dst = src * k;
        DrawTextureRect(tex, new Rect2(box.Position + (box.Size - dst) * 0.5f, dst), false);
    }

    private static string RoomName(FacilitySimulation sim, string roomId) =>
        sim.GetRoomDef(roomId)?.DisplayName ?? roomId;

    private static string StatLabel(StatType s) => s switch
    {
        StatType.Tech => "기술",
        StatType.Courage => "담력",
        StatType.Observation => "관찰",
        _ => s.ToString(),
    };

    private void Scanlines()
    {
        for (float y = 0; y < Canvas.Y; y += 3f)
            DrawRect(new Rect2(0, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.13f));
        float sweep = Mathf.PosMod(_t * 60f, Canvas.Y);
        DrawRect(new Rect2(0, sweep, Canvas.X, 2f), Mint with { A = 0.05f });
    }
}
