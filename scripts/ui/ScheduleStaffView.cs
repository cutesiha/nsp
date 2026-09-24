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
    // 누를 수 있는 블록의 호버 테두리 — 시작 화면 신원 카드와 같은 하늘색.
    private static readonly Color Cyan = NSP.View.GuideTextMarkup.ChoiceCyan;

    private Font _font;
    private float _t;

    // 이 화면(모니터 2)에서 직접 고른 직원. 왼쪽 지도에서 직원을 눌러도 여기는 바뀌지 않는다.
    private string _detailEmp = "";
    private readonly List<(Rect2 Rect, string Id)> _cards = new();
    // ⑬ 마우스가 올라간 직원 블록. CRT 안이라 이동 이벤트는 _Input 에서 받는다
    //    (ScheduleMapView 와 같은 방식).
    private string _hoverCard = "";
    // ⑮ 오늘의 한마디 타이핑 진행도 — 블록을 새로 열 때마다 처음부터 찍는다.
    private float _remarkTime;
    private int _remarkSpoken;
    private string _remarkFor = "";
    private static readonly Rect2 CloseRect = new(Canvas.X - 104f, 28f, 64f, 56f);
    private bool _closeVisible;

    // 이 화면 글자 배율(읽기 쉽게 키움).
    private static int Fs(int n) => ViewFont.S(Mathf.RoundToInt(n * 1.2f));

    public override void _Ready()
    {
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Stop;
        SetProcess(true);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
        var p = mb.Position;
        if (_closeVisible && CloseRect.Grow(6f).HasPoint(p))
        {
            _detailEmp = "";
            ScheduleMapView.Instance?.ClearFocus();
            AcceptEvent();
            return;
        }
        if (!_closeVisible)
            foreach (var (r, id) in _cards)
                if (r.HasPoint(p))
                {
                    _detailEmp = id;
                    Sfx.Instance?.Play("relay_click", -8f);
                    AcceptEvent();
                    return;
                }
    }

    // 호버는 여기서 받는다 — CRT 로 밀려 들어오는 이동 이벤트는 _GuiInput 까지 오지 않는다.
    public override void _Input(InputEvent e)
    {
        if (!IsVisibleInTree() || _cards.Count == 0) return;
        if (MakeInputLocal(e) is not InputEventMouseMotion mm) return;
        string hit = "";
        foreach (var (r, id) in _cards)
            if (r.HasPoint(mm.Position)) { hit = id; break; }
        if (_hoverCard == hit) return;
        _hoverCard = hit;
        if (!string.IsNullOrEmpty(hit)) Sfx.Instance?.Play("tick", -22f);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // 상세로 연 직원이 바뀌면 한마디를 처음부터 다시 찍는다.
        if (_remarkFor != _detailEmp)
        {
            _remarkFor = _detailEmp;
            _remarkTime = 0f;
            _remarkSpoken = 0;
        }
        else if (!string.IsNullOrEmpty(_detailEmp))
        {
            _remarkTime += (float)delta;
        }
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

        // 직원 상세는 이 화면의 직원 블록을 눌렀을 때만 연다(왼쪽 지도 선택과 무관).
        // 작업실 정보는 왼쪽 지도에서 작업실을 눌렀을 때 뜬다. 둘 다 오른쪽 위 X 로 닫는다.
        _cards.Clear();
        _closeVisible = false;
        if (!string.IsNullOrEmpty(_detailEmp))
        {
            DrawEmployee(sim, _detailEmp);
            DrawClose();
        }
        else if (!string.IsNullOrEmpty(map?.FocusRoomId))
        {
            DrawRoom(sim, map.FocusRoomId);
            DrawClose();
        }
        else
            DrawRosterGrid(sim);

        Scanlines();
    }

    private void DrawClose()
    {
        _closeVisible = true;
        // 기울어진 CRT 위에서는 옅은 민트 테두리가 배경에 묻혀 닫는 버튼이 잘 안 보였다.
        // 바탕을 민트로 채우고 X 를 어둡게 뒤집어 화면에서 가장 밝은 자리로 만든다.
        DrawRect(CloseRect.Grow(3f), Mint with { A = 0.22f });
        DrawRect(CloseRect, Mint);
        DrawRect(CloseRect, Colors.White with { A = 0.85f }, false, 2f);
        var c = CloseRect.GetCenter();
        float k = 13f;
        var ink = new Color(0.03f, 0.10f, 0.11f);
        DrawLine(c + new Vector2(-k, -k), c + new Vector2(k, k), ink, 4f);
        DrawLine(c + new Vector2(-k, k), c + new Vector2(k, -k), ink, 4f);
    }

    // ── 머리말 ───────────────────────────────────────────────────────────

    private void Header(string title, string sub)
    {
        DrawString(_font, new Vector2(48f, 66f), title, HorizontalAlignment.Left, 560f, Fs(20), Mint);
        DrawString(_font, new Vector2(48f, 92f), sub, HorizontalAlignment.Left, 640f, Fs(13), Dim);
        DrawRect(new Rect2(48f, 108f, Canvas.X - 96f, 1f), Mint with { A = 0.20f });
    }

    private void Footer(string line, Color col)
    {
        DrawRect(new Rect2(48f, 506f, Canvas.X - 96f, 1f), Mint with { A = 0.18f });
        DrawString(_font, new Vector2(48f, 538f), line, HorizontalAlignment.Left, Canvas.X - 96f, Fs(15), col);
    }

    // ── 기본: 신원 카드 그리드 ────────────────────────────────────────────

    private void DrawRosterGrid(FacilitySimulation sim)
    {
        var roster = sim.GetActiveEmployeeIds();
        Header("STAFF IDENTIFICATION", "제7지하시설 · 야간근무 편성");
        DrawString(_font, new Vector2(Canvas.X - 200f, 70f), $"{roster.Count} / {roster.Count}",
            HorizontalAlignment.Right, 152f, Fs(16), Dim);

        const float left = 82f, top = 132f, w = 208f, h = 168f, gx = 16f, gy = 14f;
        for (int i = 0; i < roster.Count && i < 6; i++)
        {
            var def = sim.GetEmployeeDef(roster[i]);
            var st = sim.GetEmployeeState(roster[i]);
            if (def == null || st == null) continue;
            var r = new Rect2(left + (i % 3) * (w + gx), top + (i / 3) * (h + gy), w, h);
            bool assigned = !string.IsNullOrEmpty(st.AssignedRoomId);
            _cards.Add((r, roster[i]));

            bool hot = _hoverCard == roster[i];
            DrawRect(r, hot ? new Color(0.06f, 0.14f, 0.16f, 0.95f) : new Color(0.04f, 0.09f, 0.10f, 0.9f));
            // 마우스를 올리면 테두리가 하늘색으로 — 누를 수 있다는 표시(타이틀 화면과 같은 규칙).
            DrawRect(r, hot ? Cyan : (assigned ? Mint : Dim) with { A = assigned ? 0.6f : 0.4f },
                false, hot ? 2.4f : 1.2f);
            var face = new Rect2(r.Position.X + (w - 72f) * 0.5f, r.Position.Y + 14f, 72f, 72f);
            Portrait(def, face);
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 112f), def.Codename,
                HorizontalAlignment.Center, w, Fs(19), Ink);
            // ⑭ 이 화면은 "기분"을 맡는다 — 배치 상태는 왼쪽 지도의 대기 인원 카드가 보여 준다.
            string mood = sim.GetDailyMood(roster[i]);
            string where = st.Isolated ? "격리실 · 근무 불가"
                : "기분: " + (string.IsNullOrEmpty(mood) ? "—" : mood);
            DrawString(_font, new Vector2(r.Position.X, r.Position.Y + 140f), where,
                HorizontalAlignment.Center, w, Fs(13), st.Isolated ? Err : Amber);
        }
        Footer("직원 블록을 누르면 상세 정보가 표시됩니다.", Dim);
    }

    // ── 직원 ─────────────────────────────────────────────────────────────

    private void DrawEmployee(FacilitySimulation sim, string id)
    {
        var def = sim.GetEmployeeDef(id);
        var st = sim.GetEmployeeState(id);
        if (def == null || st == null) { DrawRosterGrid(sim); return; }

        Header("STAFF IDENTIFICATION  ·  " + id.ToUpperInvariant(), "시설 직원 신원 확인됨.");

        // 초상(스탠딩 원화가 있으면 크게, 없으면 얼굴).
        var box = new Rect2(40f, 120f, 270f, 380f);
        DrawRect(box, new Color(0.03f, 0.07f, 0.08f, 0.9f));
        DrawRect(box, Dim with { A = 0.55f }, false, 1.2f);
        var tex = def.StandingImage ?? def.FacePortrait;
        if (tex != null && def.StandingImage != null) FillUpper(tex, box.Grow(-6f));
        else if (tex != null) Contain(tex, box.Grow(-8f));
        else DrawCircle(box.GetCenter(), 50f, def.IconColor);

        float x = 334f, y = 156f;
        DrawRect(new Rect2(x, y - 22f, 4f, 30f), def.IconColor);
        DrawString(_font, new Vector2(x + 14f, y), def.Codename, HorizontalAlignment.Left, 420f, Fs(30), Ink);
        y += 34f;
        // 경영 리워크: 특성 · 능력치는 싣지 않는다(EmployeeDef 필드는 남아 있음).
        // 항목 이름이 너무 흐려 읽히지 않았다 — 본문보다 한 단계만 어둡게 한다.
        var label = Ink with { A = 0.75f };
        DrawString(_font, new Vector2(x, y), "오늘의 기분", HorizontalAlignment.Left, 440f, Fs(14), label);
        y += 30f;
        string mood = sim.GetDailyMood(id);
        DrawString(_font, new Vector2(x, y), string.IsNullOrEmpty(mood) ? "—" : mood,
            HorizontalAlignment.Left, 440f, Fs(24), Amber);
        y += 38f;

        string room = st.AssignedRoomId;
        DrawString(_font, new Vector2(x, y), "현재 배치", HorizontalAlignment.Left, 440f, Fs(14), label);
        y += 30f;
        DrawString(_font, new Vector2(x, y),
            st.Isolated ? "격리실 · 근무 불가" : string.IsNullOrEmpty(room) ? "미배치" : RoomName(sim, room),
            HorizontalAlignment.Left, 440f, Fs(22),
            st.Isolated ? Err : string.IsNullOrEmpty(room) ? Amber : Mint);
        y += 52f;

        DrawRemark(sim, id, x, y);

        bool selected = ScheduleMapView.Instance?.SelectedEmployeeId == id;
        Footer(selected ? $"{def.Codename} 선택됨 — 배치할 작업실을 누르십시오." : $"{def.Codename}   시설 직원 신원 확인됨.",
            selected ? Mint : Ink);
    }

    // 오늘의 한마디 — 하루에 한 줄(FacilitySimulation.GetDailyRemark). 한 글자씩 찍히고,
    // 찍히는 동안 그 직원의 보이스가 울린다(대사 시스템과 같은 PlayVoiceBlip 경로).
    private void DrawRemark(FacilitySimulation sim, string id, float x, float y)
    {
        string remark = sim.GetDailyRemark(id);
        if (string.IsNullOrEmpty(remark)) return;

        string full = "\u201c" + remark + "\u201d";
        int shown = Mathf.Clamp(Mathf.FloorToInt(_remarkTime / RemarkCharSeconds), 0, full.Length);
        // 새로 드러난 글자만큼 보이스를 울린다.
        while (_remarkSpoken < shown)
        {
            char c = full[_remarkSpoken];
            _remarkSpoken++;
            Sfx.Instance?.PlayVoiceBlip(id, c);
        }
        DrawString(_font, new Vector2(x, y), full[..shown], HorizontalAlignment.Left, 440f,
            Fs(17), Ink with { A = 0.92f });
    }

    private const float RemarkCharSeconds = 0.055f;

    private void Stat(string label, int v, float x, float y)
    {
        DrawString(_font, new Vector2(x, y), label, HorizontalAlignment.Left, 60f, Fs(16), Ink);
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect2(x + 64f + i * 26f, y - 15f, 20f, 16f);
            DrawRect(r, i < v ? Mint : Mint with { A = 0.14f });
        }
        DrawString(_font, new Vector2(x + 150f, y), v.ToString(), HorizontalAlignment.Left, 40f, Fs(16), Dim);
    }

    // ── 작업실 ───────────────────────────────────────────────────────────

    private void DrawRoom(FacilitySimulation sim, string roomId)
    {
        var def = sim.GetRoomDef(roomId);
        if (def == null) { DrawRosterGrid(sim); return; }
        bool assignable = ScheduleMapView.IsAssignable(sim, roomId);

        Header("ROOM  -  " + def.DisplayName, assignable ? "배치 가능 작업실" : !sim.IsRoomActive(roomId)
            ? "비활성 — " + ScheduleMapView.LockedLabel(def) : "제한 구역 — 배치할 수 없음");

        float x = 56f, y = 150f;
        if (!assignable)
        {
            DrawDescription(roomId, x, y);
            Footer("이 작업실에는 직원을 배치할 수 없습니다.", Dim);
            return;
        }

        var here = ScheduleMapView.AssignedTo(sim, roomId);
        var ops = OpsProfile.Room(roomId);
        if (ops != null && !string.IsNullOrWhiteSpace(ops.RoleNote))
        {
            DrawString(_font, new Vector2(x, y), $"인원별 효과   (현재 {here.Count}명)", HorizontalAlignment.Left,
                680f, Fs(18), Mint);
            y += 32f;
            foreach (string line in ops.RoleNote.Split(" / ").Take(4))
            {
                DrawString(_font, new Vector2(x + 8f, y), "· " + line.Trim(), HorizontalAlignment.Left, 680f,
                    Fs(17), Ink);
                y += 28f;
            }
        }
        else
        {
            DrawString(_font, new Vector2(x, y),
                $"권장 인원   {ScheduleMapView.RecommendedHeadcount(sim, roomId)}명   (현재 {here.Count}명)",
                HorizontalAlignment.Left, 680f, Fs(18), Mint);
            y += 32f;
        }
        y += 6f;
        DrawString(_font, new Vector2(x, y), $"사고 수리 최소 인원   {RoomStaffing.RepairMinWorkers(roomId, def)}명",
            HorizontalAlignment.Left, 680f, Fs(18), Amber);
        y += 38f;

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
        DrawMultilineString(_font, new Vector2(x, y), desc, HorizontalAlignment.Left, 688f, Fs(17),
            4, Ink with { A = 0.92f }, TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Mandatory);
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
            HorizontalAlignment.Left, 540f, Fs(17), Ink);
        y += 34f;
        DrawString(_font, new Vector2(x, y), $"{edef.Codename} 의 {StatLabel(primary)}", HorizontalAlignment.Left,
            200f, Fs(16), Dim);
        Stat("", value, x + 150f, y);

        // 업무 적합도 3단계 — 종이 배치표 · FacilitySimulation.StatWorkRate 와 같은 구간.
        var (text, col, note) = value switch
        {
            >= 3 => ("✓ 적합", Good, "업무 속도 조금 빠름"),
            2 => ("○ 보통", Amber, "기준 속도"),
            _ => ("△ 비효율", Err, "업무 속도 크게 느림"),
        };
        DrawString(_font, new Vector2(x, y + 64f), text, HorizontalAlignment.Left, 300f, Fs(28), col);
        DrawString(_font, new Vector2(x, y + 94f), note, HorizontalAlignment.Left, 400f, Fs(14), Dim);
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

    // 스탠딩 원화의 투명 여백을 잘라 그림 폭을 칸 폭에 맞추고, 머리부터 칸 높이만큼만 그린다.
    // 원화에서 상반신을 잘라 신원 칸에 채운다.
    //
    // 예전에는 **캐릭터마다 자기 그림의 가로폭**에 맞춰 배율을 정했다. 그래서 팔을 벌리거나
    // 귀가 넓은 캐릭터일수록 배율이 작아져, 여섯 명의 얼굴 크기가 제각각이었다
    // (고양이만 크게 나오고 나머지는 작게). 지금은 인터뷰 화면(InterviewCCTVView.PortraitUnit)과
    // 같은 방식으로 **여섯 명이 하나의 공통 배율**을 쓴다 — 가장 큰 원화를 기준으로 잡는다.
    private void FillUpper(Texture2D tex, Rect2 box)
    {
        var c = NSP.View.InterviewCCTVView.ContentBox(tex);
        if (c.Size.X <= 0 || c.Size.Y <= 0 || box.Size.Y <= 0f) return;

        float unit = IdentUnit(box.Size.Y);
        if (unit <= 0f) return;
        // 표시 칸을 원본 좌표로 되돌린 크기 = 잘라 올 영역.
        float srcW = box.Size.X / unit;
        float srcH = box.Size.Y / unit;
        // 가로는 그림의 중심, 세로는 정수리부터.
        float left = c.Position.X + c.Size.X * 0.5f - srcW * 0.5f;
        float top = c.Position.Y;
        // 원본 밖으로 나가지 않게 민다(배율은 그대로 둔다 — 밀기만 한다).
        left = Mathf.Clamp(left, 0f, Mathf.Max(0f, tex.GetWidth() - srcW));
        top = Mathf.Clamp(top, 0f, Mathf.Max(0f, tex.GetHeight() - srcH));
        DrawTextureRectRegion(tex, box, new Rect2(left, top, srcW, srcH));
    }

    // 원화에서 위에서부터 이만큼만 보여준다(0.5 = 상반신). 얼굴 크기는 이 값 하나로 조절한다.
    private const float UpperBodyFraction = 0.52f;
    private static float _identUnit;

    // 여섯 명이 함께 쓰는 배율. 가장 큰 원화의 상반신이 칸 높이에 딱 맞도록 한 번만 구한다.
    private static float IdentUnit(float boxHeight)
    {
        if (_identUnit > 0f) return _identUnit;
        var sim = FacilitySimulation.Instance;
        if (sim == null) return 0f;

        float tallest = 0f;
        foreach (string id in sim.GetEmployeeIds())
        {
            var t = sim.GetEmployeeDef(id)?.StandingImage;
            if (t != null) tallest = Mathf.Max(tallest, NSP.View.InterviewCCTVView.ContentBox(t).Size.Y);
        }
        if (tallest <= 0f) return 0f;   // 아직 원화가 준비되지 않았다 — 다음 프레임에 다시 본다
        _identUnit = boxHeight / (tallest * UpperBodyFraction);
        return _identUnit;
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
