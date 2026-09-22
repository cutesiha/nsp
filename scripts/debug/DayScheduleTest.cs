using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.View;

namespace NSP.Debug;

// DAY1~5 일정 검증 — "같은 하루가 반복되지 않는가 · DAY2~5 가 실제로 끝까지 굴러가는가".
//
//   godot --headless --path . res://scenes/debug/DayScheduleTest.tscn
//
// 날마다 다른 것 = 정해진 경고(시각 · 작업실) · 방해공작 기회 구간 · 그날만 뜨는 시간제 업무.
// 각 DAY 를 한 번씩 실제로 돌려(관리자 대역이 사람 모자란 곳으로 한 명씩 옮긴다) 그날 업무가
// 뜨는지, 코어가 오르는지, 방해자가 5일 내내 같은 사람인지 본다. 밸런스 수치 자체는 보지 않는다.
public partial class DayScheduleTest : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ DAY1~5 일정 검증 ################");
        var spawns = ResourceDir.ListFiles("res://data/spawns", ".tres")
            .Select(p => GD.Load<TaskSpawnDef>(p)).Where(s => s != null).ToList();

        // ── 데이터: 날마다 흐름이 다르다 ─────────────────────────────────────
        var sig = new Dictionary<int, string>();
        for (int d = 1; d <= 5; d++)
        {
            var ops = OpsProfile.For(d);
            Check(ops != null && ops.Day == d, $"DAY{d} 전용 운영 설정이 있다 (Day={ops?.Day})");
            if (ops == null) continue;
            var warns = ops.ScheduledWarnings.Where(w => w != null)
                .Select(w => $"{w.RoomId}@{w.AtSeconds:0}").ToList();
            var timed = spawns.Where(s => s.Day == d && !s.Recurring)
                .OrderBy(s => s.SpawnAtSeconds).Select(s => $"{s.TaskId}@{s.SpawnAtSeconds:0}").ToList();
            sig[d] = string.Join(",", warns) + "|" + ops.SabotageWindowStartSeconds + "~" + ops.SabotageWindowEndSeconds
                     + "|" + string.Join(",", timed);
            GD.Print($"   DAY{d}  경고 {string.Join(" → ", warns)}\n" +
                     $"         방해 기회 {ops.SabotageWindowStartSeconds:0}~{ops.SabotageWindowEndSeconds:0}초 · " +
                     $"그날 업무 {(timed.Count == 0 ? "없음" : string.Join(", ", timed))}");
        }
        Check(sig.Values.Distinct().Count() == sig.Count, "DAY1~5 의 일정이 모두 다르다");
        for (int d = 2; d <= 5; d++)
            Check(spawns.Any(s => s.Day == d && !s.Recurring), $"DAY{d} 에만 뜨는 시간제 업무가 있다");
        Check((Config.Instance?.Data?.IncidentLimitLastDay ?? 0) >= 5, "초반 동시 사고 제한이 DAY5 까지 유지된다");

        // ── 실제 근무: DAY2~5 ──────────────────────────────────────────────
        GameState.Instance.ResetRun(1);
        GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
        string saboteur = GameState.Instance.SaboteurEmployeeId;
        for (int d = 2; d <= 5; d++)
        {
            GameState.Instance.GoToNextDay();
            Check(GameState.Instance.SaboteurEmployeeId == saboteur, $"DAY{d} 방해자도 DAY1 과 같은 사람");
            var r = Simulate(d);
            var todays = spawns.Where(s => s.Day == d && !s.Recurring).Select(s => s.TaskId).Distinct().ToList();
            GD.Print($"   DAY{d} 근무 — 코어 +{r.CoreGain:0.0}% · 경고 {r.Warnings}회 · 고장 {r.Breakdowns}건 · " +
                     $"발생 업무 {string.Join(", ", r.Spawned.OrderBy(x => x))}");
            Check(todays.All(r.Spawned.Contains), $"DAY{d} 그날 업무({string.Join(", ", todays)})가 실제로 뜬다");
            Check(r.Warnings >= 1, $"DAY{d} 경고가 뜬다");
            Check(r.CoreGain > 0f, $"DAY{d} 근무가 끝까지 굴러간다(코어가 오른다)");
        }
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private sealed class Result
    {
        public float CoreGain;
        public int Warnings, Breakdowns;
        public readonly HashSet<string> Spawned = new();
    }

    // Day2FlowTest 의 근무 대역과 같은 방식 — 사람이 모자란 곳으로 한 명씩 빌려 보낸다.
    private Result Simulate(int day)
    {
        var r = new Result();
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
        TabooRuleSystem.Instance?.ActivateDailyTaboos(ControlRoom3DController.TodayTabooIds());

        var plan = new[] { ("core_room", 2), ("power_room", 1), ("maintenance_room", 1),
                           ("guard_room", 1), ("storage_room", 1) };
        var roster = _sim.GetActiveEmployeeIds().ToList();
        int i = 0;
        foreach (var (room, n) in plan)
            for (int k = 0; k < n && i < roster.Count; k++, i++)
                _sim.AssignToRoom(roster[i], room);

        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        float start = GameState.Instance.CoreProgress;
        string borrowed = "", origin = "";

        for (float t = 0f; t < DayObjectives.MaxShiftSeconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);

            foreach (var task in _sim.GetTaskDefs())
                if (_sim.GetActiveTasksForRoom(task.RoomId).Any(x => x.TaskId == task.TaskId))
                    r.Spawned.Add(task.TaskId);

            string needRoom = "";
            var w = _sim.Warnings.Active.FirstOrDefault();
            if (w != null && _sim.OnDutyCount(w.RoomId) < w.RequiredStaff) needRoom = w.RoomId;
            else
                foreach (string room in _sim.GetRoomIds())
                {
                    var task = _sim.GetPrimarySpawnedTask(room);
                    if (task is not { Status: SpawnedTaskStatus.Active, Recurring: false }) continue;
                    int min = Mathf.Max(1, _sim.GetTaskDef(task.TaskId)?.MinWorkersToProgress ?? 1);
                    if (_sim.OnDutyCount(room) >= min) continue;
                    needRoom = room;
                    break;
                }

            if (needRoom.Length == 0)
            {
                if (borrowed.Length > 0) { _sim.AssignToRoom(borrowed, origin); borrowed = ""; origin = ""; }
                continue;
            }
            if (borrowed.Length > 0) continue;
            string donor = _sim.GetRoomIds().Where(x => x != needRoom)
                .OrderByDescending(_sim.OnDutyCount).FirstOrDefault(x => _sim.OnDutyCount(x) >= 2);
            if (donor == null) continue;
            borrowed = _sim.GetRoomState(donor).OccupantEmployeeIds.FirstOrDefault() ?? "";
            origin = donor;
            if (borrowed.Length > 0) _sim.AssignToRoom(borrowed, needRoom);
        }

        r.CoreGain = GameState.Instance.CoreProgress - start;
        r.Warnings = _sim.Warnings.Raised;
        r.Breakdowns = IncidentTracker.OpenedCount;
        return r;
    }

    private void Check(bool ok, string label)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }
}
