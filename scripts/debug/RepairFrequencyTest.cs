using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 수리 승인 요청(G-2)이 하루에 몇 번 뜨는가 — 실제 근무를 돌려서 센다.
//
//   godot --headless --path . res://scenes/debug/RepairFrequencyTest.tscn --quit-after 120000
//   (끝에 -- --sweep 을 붙이면 Config 조정 후보끼리 비교한다)
//
// 승인 요청은 "고장이 났을 때"만 뜬다. 그래서 묻는 것은 하나다 —
// 하루에 고장이 몇 번 나는가, 그리고 그게 어디서 오는가.
//   ① 경고 대응 실패   ② 무인 방치   ③ 이상 개체   ④ 업무 제한시간 초과
//
// 배치를 세 가지로 두고 DAY1~5 를 각각 끝까지 돌린다.
//   상주 — 6명을 요구 인원대로 깔아 두고 근무 중에는 손대지 않는다(잘 짠 배치).
//   대응 — 상주 + 경고가 뜨면 남는 인원을 그 방으로 보낸다(실제 플레이에 가장 가깝다).
//   방치 — 3명만 넣고 아무 대응도 하지 않는다(최악).
public partial class RepairFrequencyTest : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;

    private sealed class Row
    {
        public int Day;
        public string Style = "";
        public int Raised, Prevented, Failed;
        public int Approvals;
        public readonly List<string> When = new();
        public readonly Dictionary<string, int> Cause = new();
    }

    private readonly List<Row> _rows = new();

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("\n\n################ 수리 승인 요청 빈도 측정 ################");
        GD.Print($"   하루 길이 {Config.Instance.Data.DayLengthSeconds:0}초 · "
                 + $"동시 사고 상한 {Config.Instance.Data.Day1MaxActiveIncidents}건(DAY{Config.Instance.Data.IncidentLimitLastDay}까지) · "
                 + $"사고 간격 {Config.Instance.Data.IncidentGapSeconds:0}초");

        for (int day = 1; day <= 5; day++)
        {
            _rows.Add(RunDay(day, "상주", respond: false, headcount: 6));
            _rows.Add(RunDay(day, "대응", respond: true, headcount: 6));
            _rows.Add(RunDay(day, "방치", respond: false, headcount: 3));
        }

        Report();
        // 조정 후보 비교는 측정을 세 번 더 돌린다(3~4분) — 밸런스를 만질 때만 켠다.
        if (OS.GetCmdlineUserArgs().Contains("--sweep")) Sweep();
        GetTree().Quit();
    }

    // 요구 인원대로 깔아 두는 기본 배치. 남는 인원은 저장고로 보낸다.
    private static readonly (string Room, int Want)[] Plan =
    {
        ("core_room", 2), ("power_room", 2), ("maintenance_room", 1), ("guard_room", 1), ("storage_room", 1),
    };

    private Row RunDay(int day, string style, bool respond, int headcount)
    {
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();
        GameState.Instance.SetPhase(GamePhase.Live);

        var ids = _sim.GetActiveEmployeeIds().Take(headcount).ToList();
        int k = 0;
        foreach (var (room, want) in Plan)
            for (int i = 0; i < want && k < ids.Count; i++) _sim.AssignToRoom(ids[k++], room);
        for (; k < ids.Count; k++) _sim.AssignToRoom(ids[k], "storage_room");

        var row = new Row { Day = day, Style = style };
        RepairApprovalSystem.Request seen = null;

        float dayLength = Config.Instance.Data.DayLengthSeconds;
        for (float t = 0f; t < dayLength; t += Step)
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);

            // 승인 요청이 새로 뜬 순간만 센다.
            var active = RepairApprovalSystem.Active;
            if (active != null && !ReferenceEquals(active, seen))
            {
                seen = active;
                row.Approvals++;
                row.When.Add($"{t:0}초 {active.RoomName}");
            }

            // 대응 배치만 요청에 답한다 — [예] 를 누르고 미로를 끝까지 푼다.
            // 나머지는 무응답으로 흘려보낸다(수리 +50%).
            if (respond)
            {
                AnswerApproval();
                Dispatch();
            }
        }

        var w = _sim.Warnings;
        row.Raised = w.Raised; row.Prevented = w.Prevented; row.Failed = w.Failed;

        foreach (var e in EventLog.Instance?.GetAllEntries() ?? new List<LogEntry>())
        {
            string c = Classify(e.Description);
            if (c == null) continue;
            row.Cause[c] = row.Cause.GetValueOrDefault(c) + 1;
        }
        return row;
    }

    // 고장이 난 원인 — 로그 문구가 원인별로 다르다(FacilitySimulation 참조).
    private static string Classify(string d)
    {
        if (string.IsNullOrEmpty(d) || !d.StartsWith("🚨")) return null;
        if (d.Contains("무인 방치")) return "무인 방치";
        if (d.Contains("이상 개체 접촉")) return "이상 개체";
        if (d.Contains("대응 실패")) return "경고 실패";
        if (d.Contains("제한시간 초과, 고장")) return "업무 초과";
        return null;
    }

    // 요청에 [예] 로 답하고 미로를 정답대로 한 칸 민다(한 틱에 한 칸 — 사람이 푸는 속도보다 빠르다).
    private static void AnswerApproval()
    {
        switch (RepairApprovalSystem.Current)
        {
            case RepairApprovalSystem.Phase.Asking:
                RepairApprovalSystem.Approve();
                break;
            case RepairApprovalSystem.Phase.Maze:
                var m = RepairApprovalSystem.Maze;
                if (m == null || m.Failed) break;
                var path = m.ShortestPath(m.Cursor, m.Goal);
                if (path.Count < 2) break;
                var d = path[1] - m.Cursor;
                RepairApprovalSystem.MazeInput(
                    d.Y < 0 ? RepairMaze.Dir.Up : d.Y > 0 ? RepairMaze.Dir.Down
                    : d.X < 0 ? RepairMaze.Dir.Left : RepairMaze.Dir.Right);
                break;
        }
    }

    // 관리자가 할 법한 최소 대응 — 고장 난 방부터 고치고, 남으면 경고가 뜬 방을 채운다.
    // 수리를 끝내야 그 방이 다시 고장 날 수 있으므로, 고치지 않는 측정은 실제보다 적게 나온다.
    private void Dispatch()
    {
        foreach (string room in _sim.GetRoomIds().Where(r => _sim.HasRepairPending(r)).ToList())
            Fill(room, RoomStaffing.RepairMinWorkers(room, _sim.GetRoomDef(room)));
        foreach (var w in _sim.Warnings.Active.ToList())
            Fill(w.RoomId, w.RequiredStaff);
    }

    private void Fill(string room, int want)
    {
        int need = want - _sim.OnDutyCount(room);
        if (need <= 0) return;
        foreach (string id in _sim.GetActiveEmployeeIds())
        {
            if (need <= 0) return;
            var st = _sim.GetEmployeeState(id);
            if (st == null || st.AssignedRoomId == room) continue;
            // 더 급한 방에 붙어 있는 인원은 빼지 않는다.
            if (_sim.HasRepairPending(st.AssignedRoomId)) continue;
            if (_sim.Warnings.HasActive(st.AssignedRoomId)) continue;
            if (_sim.AssignToRoom(id, room)) need--;
        }
    }

    // 조정 후보 — "동시 사고 한 건" 제한을 며칠째까지 유지하느냐가 가장 크게 움직인다.
    // 간격(IncidentGapSeconds)과 동시 상한(Day1MaxActiveIncidents)은 거의 효과가 없었다.
    private void Sweep()
    {
        var cfg = Config.Instance.Data;
        int keepLast = cfg.IncidentLimitLastDay;
        int keepMax = cfg.Day1MaxActiveIncidents;
        float keepGap = cfg.IncidentGapSeconds;

        GD.Print("\n===== 조정 후보 : DAY 별 승인 요청 (상주 / 대응 / 방치) =====");
        foreach (var (name, last, max, gap) in new[]
                 {
                     ("제한 DAY5 까지(예전)", 5, 1, 18f),
                     ("제한 DAY3 까지", 3, 1, 18f),
                     ("제한 DAY2 까지(지금)", 2, 1, 18f),
                 })
        {
            cfg.IncidentLimitLastDay = last;
            cfg.Day1MaxActiveIncidents = max;
            cfg.IncidentGapSeconds = gap;
            GD.Print($"   [{name}]");
            float sa = 0f, sb = 0f, sc = 0f;
            for (int day = 1; day <= 5; day++)
            {
                int a = RunDay(day, "상주", false, 6).Approvals;
                int b = RunDay(day, "대응", true, 6).Approvals;
                int c = RunDay(day, "방치", false, 3).Approvals;
                sa += a; sb += b; sc += c;
                GD.Print($"      DAY{day}   {a,2} / {b,2} / {c,2}");
            }
            GD.Print($"      평균   {sa / 5f,4:0.0} / {sb / 5f,4:0.0} / {sc / 5f,4:0.0}");
        }

        cfg.IncidentLimitLastDay = keepLast;
        cfg.Day1MaxActiveIncidents = keepMax;
        cfg.IncidentGapSeconds = keepGap;
    }

    private void Report()
    {
        GD.Print("\n===== 하루당 수리 승인 요청 횟수 =====");
        GD.Print("   DAY  배치   경고(뜸/막음/놓침)   승인요청   원인");
        foreach (var r in _rows)
        {
            string cause = r.Cause.Count == 0 ? "-"
                : string.Join(" · ", r.Cause.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"));
            GD.Print($"   {r.Day,3}  {r.Style,-4}   {r.Raised,2} / {r.Prevented,2} / {r.Failed,2}          "
                     + $"{r.Approvals,2}회      {cause}");
        }

        GD.Print("\n===== 요청이 뜬 시각 =====");
        foreach (var r in _rows)
            GD.Print($"   DAY{r.Day} {r.Style,-4} : {(r.When.Count == 0 ? "없음" : string.Join(" , ", r.When))}");

        foreach (string style in new[] { "상주", "대응", "방치" })
        {
            var rs = _rows.Where(x => x.Style == style).ToList();
            GD.Print($"\n   [{style}] 5일 합계 {rs.Sum(x => x.Approvals)}회 · 하루 평균 {rs.Average(x => x.Approvals):0.00}회");
        }
    }
}
