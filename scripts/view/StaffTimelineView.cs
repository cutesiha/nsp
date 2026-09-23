using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 띠 시간표 — 근무 6시간을 가로축 하나로 펴고, 직원마다 "언제 어느 방에 있었는가"를
// 방 색 띠로 그린다. 사고는 모든 띠를 관통하는 세로선이다.
//
// 이 화면은 **그리기 전용**이다. 격리 판정 · 엔딩 판정 · 추궁 성립 · 결번자 AI 어디에서도
// 이 클래스를 읽지 않는다. 읽는 순간 "플레이어가 본 것"과 "시스템이 아는 것"의 경계가 무너진다.
//
// 그래서 입력은 FacilityLogFormatter.Build() 가 준 DisplayLogEntry 목록뿐이다.
// EventLog 원본을 보지 않는다 — 화면에 뜬 적 없는 이동이 띠에 나타나면
// 플레이어는 보지도 못한 사실로 추리하게 된다.
//
// 노드를 직원×구간만큼 만들지 않고 _Draw() 로 한 번에 그린다(구간이 수십 개다).
public partial class StaffTimelineView : Control
{
    // ── 바깥으로 나가는 클릭 ───────────────────────────────────────────
    // 구간을 눌렀다. time 은 그 구간이 시작한 시각(= 그 이동이 로그에 찍힌 시각)이다.
    public Action<string, float> SegmentPressed;
    // 사고 세로선을 눌렀다. logIndex 는 넘겨받은 DisplayLogEntry 목록에서의 자리다.
    public Action<int> IncidentPressed;
    // 핀(조사 자료 카드)을 눌렀다.
    public Action<string> PinPressed;

    // ── 구간 ───────────────────────────────────────────────────────────
    // [Start, End) 동안 이 직원은 RoomId 에 있었다. RoomId 가 비면 통로다.
    public sealed class Segment
    {
        public float Start;
        public float End;
        public string RoomId = "";
    }

    // 통로 · 모르는 방의 색. RoomDef.MapColor 가 비었을 때도 이 색으로 떨어진다.
    public static readonly Color CorridorColor = new(0.30f, 0.33f, 0.36f);

    // ── 구간 계산 (headless 검사 대상) ─────────────────────────────────
    //
    // 화면 없이도 확인할 수 있게 static 으로 뺐다. 규칙은 하나다 —
    // **방이 적힌 이동 줄만** 본다. 경고 · 안정화 · 자재 · 순찰 줄에는 방 이동이 없으므로
    // 자연히 걸리지 않는다.
    //
    // 첫 줄(최초 배치, 0초)이 첫 구간을 열고, 이동 줄마다 앞 구간을 닫고 새 구간을 연다.
    // 마지막 구간은 근무 끝까지 이어진다.
    public static Dictionary<string, List<Segment>> BuildSegments(
        IEnumerable<string> employeeIds, IEnumerable<DisplayLogEntry> rows, float dayLength)
    {
        float last = Mathf.Max(1f, dayLength);
        var result = new Dictionary<string, List<Segment>>();
        if (employeeIds != null)
            foreach (var id in employeeIds)
                if (!string.IsNullOrEmpty(id)) result[id] = new List<Segment>();
        if (rows == null) return result;

        foreach (var r in rows.Where(IsPlacementRow).OrderBy(r => r.Timestamp))
        {
            if (!result.TryGetValue(r.RelatedEmployeeId, out var list)) continue;
            float t = Mathf.Clamp(r.Timestamp, 0f, last);
            var prev = list.Count > 0 ? list[^1] : null;
            // 같은 방으로 다시 "이동"한 줄은 구간을 쪼개지 않는다.
            if (prev != null && prev.RoomId == r.ToRoomId) continue;
            if (prev != null)
            {
                prev.End = t;
                // 같은 시각에 두 줄이 겹치면 나중 줄이 이긴다(앞 구간은 폭이 0이다).
                if (prev.End <= prev.Start) list.RemoveAt(list.Count - 1);
            }
            list.Add(new Segment { Start = t, End = last, RoomId = r.ToRoomId ?? "" });
        }
        return result;
    }

    // 이 줄이 "이 직원이 지금 이 방에 있다"고 말하는가.
    //
    // 도착한 방(ToRoomId)이 적혀 있느냐만 본다. 시설 로그 화면에서 그 칸을 채우는 줄은
    // 최초 배치와 자리 이동 둘뿐이고, 경고 · 안정화 · 자재 · 순찰 줄에는 방 이동이 없어
    // 자연히 걸리지 않는다. 사건 종류(SourceEventType)로 거르지 않는 이유는 —
    // 최초 배치는 Relocation, 이동은 RoomEnter 로 서로 다른 사건에서 오기 때문이다.
    private static bool IsPlacementRow(DisplayLogEntry r) =>
        r != null && !string.IsNullOrEmpty(r.RelatedEmployeeId) && !string.IsNullOrEmpty(r.ToRoomId);

    // ── 사고 줄 고르기 (headless 검사 대상) ────────────────────────────
    //
    // 세로선이 되는 줄은 사고뿐이다. 경고(Warning) · 안정화(Recovery) 는 띠에 뜨지 않는다 —
    // 화면이 선으로 가득 차면 정작 봐야 할 순간이 묻힌다.
    public static List<int> IncidentRowIndices(IReadOnlyList<DisplayLogEntry> rows)
    {
        var found = new List<int>();
        if (rows == null) return found;
        for (int i = 0; i < rows.Count; i++)
        {
            var s = rows[i]?.Severity;
            if (s is DisplayLogSeverity.Critical or DisplayLogSeverity.Sabotage) found.Add(i);
        }
        return found;
    }

    // ── 입력 ───────────────────────────────────────────────────────────
    private readonly List<string> _employees = new();
    private List<DisplayLogEntry> _rows = new();
    private readonly List<InterviewEvidence> _pins = new();
    private Dictionary<string, List<Segment>> _segments = new();
    private readonly List<int> _incidents = new();

    // 지금 선택된 자료 카드. 같은 핀을 밝게 그려 목록과 띠가 같은 것을 가리키게 한다.
    public string SelectedEvidenceId = "";

    private Font _font;
    private float _dayLength = 120f;
    private (int Kind, int Index, string Key) _hover = (0, -1, "");

    public override void _Ready()
    {
        _font = ViewFont.Default;
        MouseFilter = MouseFilterEnum.Stop;
    }

    // 띠에 그릴 것을 한 번에 넘긴다. 핀은 없어도 된다(L 로그 창은 핀이 없다).
    public void SetData(IEnumerable<string> employeeIds, IEnumerable<DisplayLogEntry> rows,
        IEnumerable<InterviewEvidence> pins = null)
    {
        _employees.Clear();
        if (employeeIds != null)
            foreach (var id in employeeIds)
                if (!string.IsNullOrEmpty(id) && !_employees.Contains(id)) _employees.Add(id);

        _rows = rows?.Where(r => r != null).ToList() ?? new List<DisplayLogEntry>();
        _pins.Clear();
        if (pins != null) _pins.AddRange(pins.Where(p => p != null));

        _dayLength = Mathf.Max(1f, Config.Instance?.Data?.DayLengthSeconds ?? 120f);
        _segments = BuildSegments(_employees, _rows, _dayLength);
        _incidents.Clear();
        _incidents.AddRange(IncidentRowIndices(_rows));
        QueueRedraw();
    }

    // 직원 수 · 사고 수에 맞는 최소 높이. 화면이 이보다 작으면 띠를 눌러서 그린다.
    public static float PreferredHeight(int employeeCount, int incidentCount) =>
        AxisH + HeadH + Mathf.Max(1, employeeCount) * 16f
        + (incidentCount >= 2 ? incidentCount * LegendLineH + 4f : 0f);

    // ── 치수 ───────────────────────────────────────────────────────────
    private const float AxisH = 13f;        // 맨 위 시각 눈금
    private const float HeadH = 14f;        // 사고 라벨(또는 번호) 줄
    private const float LegendLineH = 12f;  // 사고가 여럿일 때 아래 범례 한 줄
    private const float NameW = 46f;        // 왼쪽 코드네임 칸
    private const float PadR = 6f;
    private const float RowGap = 2f;

    private float TrackX => NameW;
    private float TrackW => Mathf.Max(10f, Size.X - NameW - PadR);
    private float LegendH => _incidents.Count >= 2 ? _incidents.Count * LegendLineH + 4f : 0f;
    private float BandTop => AxisH + HeadH;
    private float RowH => Mathf.Max(8f, (Size.Y - BandTop - LegendH) / Mathf.Max(1, _employees.Count));

    private float XOf(float seconds) => TrackX + Mathf.Clamp(seconds / _dayLength, 0f, 1f) * TrackW;
    private float TimeOf(float x) => Mathf.Clamp((x - TrackX) / Mathf.Max(1f, TrackW), 0f, 1f) * _dayLength;
    private float RowTop(int i) => BandTop + i * RowH;

    // ── 그리기 ─────────────────────────────────────────────────────────
    public override void _Draw()
    {
        _font ??= ViewFont.Default;
        if (_employees.Count == 0) return;

        DrawAxis();
        DrawBands();
        DrawIncidentLines();
        DrawPins();
        DrawLegend();
    }

    // 22:00 · 01:00 · 04:00 — 한 시간마다 옅은 세로선, 세 곳에만 숫자.
    private void DrawAxis()
    {
        float bottom = BandTop + _employees.Count * RowH;
        var faint = new Color(1f, 1f, 1f, 0.07f);
        for (int h = 0; h <= 6; h++)
        {
            float x = TrackX + TrackW * (h / 6f);
            DrawLine(new Vector2(x, AxisH - 2f), new Vector2(x, bottom), faint, 1f);
            if (h % 3 != 0) continue;
            string text = $"{(22 + h) % 24:00}:00";
            var w = _font.GetStringSize(text, HorizontalAlignment.Left, -1f, 9);
            float tx = h == 0 ? x : (h == 6 ? x - w.X : x - w.X * 0.5f);
            DrawString(_font, new Vector2(tx, AxisH - 3f), text, HorizontalAlignment.Left, -1f, 9,
                new Color(0.62f, 0.66f, 0.70f, 0.85f));
        }
    }

    private void DrawBands()
    {
        for (int i = 0; i < _employees.Count; i++)
        {
            string id = _employees[i];
            float y = RowTop(i);
            float h = RowH - RowGap;

            // 코드네임. 자리가 좁으면 두 글자까지만.
            var nameCol = new Color(0.70f, 0.74f, 0.78f);
            string name = Codename(id);
            int fs = (int)Mathf.Clamp(h - 3f, 8f, 11f);
            if (_font.GetStringSize(name, HorizontalAlignment.Left, -1f, fs).X > NameW - 4f)
                name = name.Length > 2 ? name[..2] : name;
            DrawString(_font, new Vector2(2f, y + h * 0.5f + fs * 0.36f), name,
                HorizontalAlignment.Left, NameW - 4f, fs, nameCol);

            // 빈 띠(오늘 배치 기록이 없는 직원)도 자리는 남긴다.
            DrawRect(new Rect2(TrackX, y, TrackW, h), new Color(0f, 0f, 0f, 0.25f));

            if (!_segments.TryGetValue(id, out var segs)) continue;
            for (int s = 0; s < segs.Count; s++)
            {
                var seg = segs[s];
                float x0 = XOf(seg.Start), x1 = XOf(seg.End);
                if (x1 - x0 < 1f) continue;
                var col = RoomColor(seg.RoomId);
                bool hot = _hover.Kind == 1 && _hover.Key == id && _hover.Index == s;
                DrawRect(new Rect2(x0, y, x1 - x0, h), hot ? col.Lightened(0.22f) : col);
                // 구간 경계 — 방이 바뀐 순간이 눈에 띄어야 한다.
                if (seg.Start > 0f)
                    DrawLine(new Vector2(x0, y), new Vector2(x0, y + h), new Color(0f, 0f, 0f, 0.45f), 1f);

                // 칸이 넉넉하면 방 이름을 띠 안에 얹는다.
                if (x1 - x0 < 46f || h < 11f) continue;
                var ink = col.Luminance > 0.5f ? new Color(0.08f, 0.09f, 0.10f, 0.9f)
                                               : new Color(1f, 1f, 1f, 0.88f);
                DrawString(_font, new Vector2(x0 + 4f, y + h * 0.5f + 3.4f), RoomName(seg.RoomId),
                    HorizontalAlignment.Left, x1 - x0 - 8f, 9, ink);
            }
        }
    }

    private void DrawIncidentLines()
    {
        float top = BandTop;
        float bottom = BandTop + _employees.Count * RowH;
        bool many = _incidents.Count >= 2;

        for (int n = 0; n < _incidents.Count; n++)
        {
            var row = _rows[_incidents[n]];
            float x = XOf(row.Timestamp);
            var col = IncidentColor(row);
            bool hot = _hover.Kind == 2 && _hover.Index == n;

            DrawLine(new Vector2(x, top - 3f), new Vector2(x, bottom), col, hot ? 3f : 2f);
            // 선이 띠 색에 묻히지 않게 위쪽에 작은 삼각 머리를 단다.
            DrawColoredPolygon(new[]
            {
                new Vector2(x - 4f, top - 8f), new Vector2(x + 4f, top - 8f), new Vector2(x, top - 2f),
            }, col);

            // 하나뿐이면 라벨을 그대로 위에 쓰고, 여럿이면 번호만 쓰고 아래 범례로 보낸다.
            string label = many ? Glyph(n) : Glyph(n) + " " + IncidentLabel(row);
            var size = _font.GetStringSize(label, HorizontalAlignment.Left, -1f, 10);
            float lx = Mathf.Clamp(x - size.X * 0.5f, 10f, Mathf.Max(10f, Size.X - size.X - 1f));
            DrawString(_font, new Vector2(lx, AxisH + 9f), label, HorizontalAlignment.Left, -1f, 10, col);
            // 방 색 네모 — 어느 방에서 난 사고인지 띠 색과 바로 맞춰 보라는 표시다.
            if (many) continue;
            DrawRect(new Rect2(lx - 9f, AxisH + 2f, 7f, 7f), RoomColor(IncidentRoom(row)));
        }
    }

    private void DrawLegend()
    {
        if (_incidents.Count < 2) return;
        float y = BandTop + _employees.Count * RowH + 4f;
        for (int n = 0; n < _incidents.Count; n++)
        {
            var row = _rows[_incidents[n]];
            DrawRect(new Rect2(2f, y + 2f, 7f, 7f), RoomColor(IncidentRoom(row)));
            DrawString(_font, new Vector2(12f, y + 9f), Glyph(n) + " " + IncidentLabel(row),
                HorizontalAlignment.Left, Size.X - 14f, 10, IncidentColor(row));
            y += LegendLineH;
        }
    }

    // 조사 자료 카드를 그 직원 띠 위, 그 시각에 꽂는다.
    // 시각이 없는 자료(기분)는 근무 시작 쪽 끝에 모아 둔다 — 하루 전체에 걸린 말이라
    // 특정 순간에 꽂으면 거짓말이 된다.
    private void DrawPins()
    {
        for (int p = 0; p < _pins.Count; p++)
        {
            var e = _pins[p];
            int row = _employees.IndexOf(e.SubjectEmployeeId);
            if (row < 0) continue;
            float x = e.HasTime ? XOf(e.AnchorTime) : TrackX + 3f;
            float y = RowTop(row);
            var col = PinColor(e.Kind);
            bool on = !string.IsNullOrEmpty(SelectedEvidenceId) && SelectedEvidenceId == e.Id;
            bool hot = _hover.Kind == 3 && _hover.Index == p;

            // 띠 위쪽에 걸쳐 앉는 작은 못. 띠 색을 가리지 않게 절반만 덮는다.
            float r = on || hot ? 5f : 4f;
            DrawColoredPolygon(new[]
            {
                new Vector2(x, y + r * 1.6f), new Vector2(x - r, y - r * 0.4f), new Vector2(x + r, y - r * 0.4f),
            }, on || hot ? col.Lightened(0.35f) : col);
            if (on)
                DrawLine(new Vector2(x, y), new Vector2(x, y + RowH - RowGap), col, 1.5f);
        }
    }

    // ── 클릭 ───────────────────────────────────────────────────────────
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion m)
        {
            var h = Probe(m.Position);
            if (h != _hover)
            {
                _hover = h;
                TooltipText = HoverText(h);
                QueueRedraw();
            }
            return;
        }
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b) return;

        var hit = Probe(b.Position);
        switch (hit.Kind)
        {
            case 3:
                PinPressed?.Invoke(_pins[hit.Index].Id);
                AcceptEvent();
                break;
            case 2:
                IncidentPressed?.Invoke(_incidents[hit.Index]);
                AcceptEvent();
                break;
            case 1:
                SegmentPressed?.Invoke(hit.Key, _segments[hit.Key][hit.Index].Start);
                AcceptEvent();
                break;
        }
    }

    // 이 좌표에 무엇이 있는가. 핀 > 사고 선 > 구간 순으로 본다 —
    // 핀과 선은 좁아서, 겹칠 때 뒤로 밀리면 영영 누를 수 없다.
    private (int Kind, int Index, string Key) Probe(Vector2 pos)
    {
        for (int p = 0; p < _pins.Count; p++)
        {
            var e = _pins[p];
            int row = _employees.IndexOf(e.SubjectEmployeeId);
            if (row < 0) continue;
            float x = e.HasTime ? XOf(e.AnchorTime) : TrackX + 3f;
            float y = RowTop(row);
            if (new Rect2(x - 6f, y - 4f, 12f, 12f).HasPoint(pos)) return (3, p, e.Id);
        }

        float bottom = BandTop + _employees.Count * RowH;
        if (pos.Y >= BandTop - 8f && pos.Y <= bottom)
            for (int n = 0; n < _incidents.Count; n++)
                if (Mathf.Abs(pos.X - XOf(_rows[_incidents[n]].Timestamp)) <= 4f) return (2, n, "");

        if (pos.X >= TrackX && pos.Y >= BandTop && pos.Y < bottom)
        {
            int i = Mathf.Clamp((int)((pos.Y - BandTop) / RowH), 0, _employees.Count - 1);
            string id = _employees[i];
            float t = TimeOf(pos.X);
            if (_segments.TryGetValue(id, out var segs))
                for (int s = 0; s < segs.Count; s++)
                    if (t >= segs[s].Start && t < segs[s].End) return (1, s, id);
        }
        return (0, -1, "");
    }

    private string HoverText((int Kind, int Index, string Key) h) => h.Kind switch
    {
        3 => _pins[h.Index].Header + " · " + _pins[h.Index].Body,
        2 => IncidentLabel(_rows[_incidents[h.Index]]),
        1 => Codename(h.Key) + " · " + RoomName(_segments[h.Key][h.Index].RoomId)
             + " · " + ClockText(_segments[h.Key][h.Index].Start),
        _ => "",
    };

    // ── 이름 · 색 ──────────────────────────────────────────────────────
    private static string Codename(string id) =>
        FacilitySimulation.Instance?.GetEmployeeDef(id)?.Codename ?? id;

    private static string RoomName(string roomId) => string.IsNullOrEmpty(roomId) ? "통로"
        : FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;

    private static Color RoomColor(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return CorridorColor;
        var def = FacilitySimulation.Instance?.GetRoomDef(roomId);
        return def == null ? CorridorColor : def.MapColor;
    }

    private static Color IncidentColor(DisplayLogEntry r) =>
        r.Severity == DisplayLogSeverity.Sabotage ? new Color(0.92f, 0.26f, 0.26f)
                                                  : new Color(0.95f, 0.62f, 0.20f);

    private static Color PinColor(EvidenceKind k) => k switch
    {
        EvidenceKind.Cctv => new Color(0.42f, 0.82f, 0.92f),
        EvidenceKind.Testimony => new Color(0.96f, 0.80f, 0.36f),
        EvidenceKind.OwnStatement => new Color(0.92f, 0.92f, 0.94f),
        EvidenceKind.Mood => new Color(0.66f, 0.62f, 0.78f),
        _ => new Color(0.80f, 0.80f, 0.84f),
    };

    // 사고 줄의 방. 조사 자료 카드가 쓰는 것과 같은 창구를 쓴다 —
    // 띠와 카드가 다른 방을 가리키면 추리가 어긋난다.
    private static string IncidentRoom(DisplayLogEntry r) => InterviewEvidenceBoard.IncidentRoomOf(r);

    // "23:47 · 정비실 · 방해공작"
    private static string IncidentLabel(DisplayLogEntry r) =>
        ClockText(r.Timestamp) + " · " + RoomName(IncidentRoom(r)) + " · "
        + (r.Severity == DisplayLogSeverity.Sabotage ? "방해공작" : "고장");

    // 근무 초 → 22:00~04:00 벽시계 숫자. 환산은 DialogueClock 한 곳에서만 한다
    // (SaboteurPlan.Clock 과 같은 식이다). 띠는 숫자 표기를 쓴다 — 가로축 눈금과 맞춰야 한다.
    private static string ClockText(float seconds)
    {
        int total = DialogueClock.StartHour * 60 + DialogueClock.MinutesAt(seconds);
        return $"{total / 60 % 24:00}:{total % 60:00}";
    }

    private static string Glyph(int n) =>
        n < 10 ? "①②③④⑤⑥⑦⑧⑨⑩"[n].ToString() : $"({n + 1})";
}
