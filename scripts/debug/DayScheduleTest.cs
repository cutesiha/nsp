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

        // ── 실제 근무: DAY1~5 를 두 대역으로 ───────────────────────────────
        //
        //   수동 : CCTV 를 돌려 보지 않는다(괴물을 방치한다). 격리도 하지 않는다.
        //   대응 : 괴물이 나타나면 그 방을 CCTV 로 지켜봐 없앤다. 격리는 여전히 하지 않는다.
        //
        // 두 대역을 같이 보는 이유는 "CCTV 를 보는 것이 실제로 이득인가" 와
        // "보지 않아도 근무가 굴러는 가는가" 가 서로 다른 질문이기 때문이다.
        // 예전처럼 CoreGain > 0 하나만 보면 목표치의 5% 만 채워도 통과해서
        // 밸런스가 무너진 것을 잡지 못한다.
        var manual = RunBand(spawns, watchGhost: false);
        var reactive = RunBand(spawns, watchGhost: true);

        GD.Print("\n---------------- 코어 복구 합계 ----------------");
        GD.Print("   대역     DAY1   DAY2   DAY3   DAY4   DAY5   합계 / 100");
        PrintBand("수동", manual);
        PrintBand("대응", reactive);

        float manualSum = manual.Values.Sum();
        float reactiveSum = reactive.Values.Sum();
        // 결번자를 격리하지 않고도 100% 에 닿으면 추리할 이유가 사라진다.
        // 수치는 여기서 고치지 않는다 — 넘었다는 사실만 알린다.
        Check(reactiveSum <= 100f,
            $"격리 없이 대응만으로는 코어 100% 에 닿지 않는다 (대응 합계 {reactiveSum:0.0}%)");

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private void PrintBand(string label, Dictionary<int, float> band)
    {
        var sb = new System.Text.StringBuilder($"   {label,-6}");
        // C# 보간 문자열은 {값,자릿수:서식} 순서다 — 뒤집으면 자릿수가 서식의 일부로 먹힌다.
        for (int d = 1; d <= 5; d++) sb.Append($" {band.GetValueOrDefault(d),6:0.0}");
        sb.Append($"   {band.Values.Sum(),6:0.0} / 100");
        GD.Print(sb.ToString());
    }

    // 한 대역을 DAY1~5 로 돌린다. 날마다 세 번의 평균을 낸다.
    // 반환값 = 날짜별 평균 코어 복구량.
    private Dictionary<int, float> RunBand(System.Collections.Generic.List<TaskSpawnDef> spawns, bool watchGhost)
    {
        GD.Print($"\n---------------- {(watchGhost ? "대응" : "수동")} 대역 ----------------");
        var gains = new Dictionary<int, float>();

        GameState.Instance.ResetRun(1);
        GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
        string saboteur = GameState.Instance.SaboteurEmployeeId;

        for (int d = 1; d <= 5; d++)
        {
            if (d > 1)
            {
                GameState.Instance.GoToNextDay();
                Check(GameState.Instance.SaboteurEmployeeId == saboteur, $"DAY{d} 방해자도 DAY1 과 같은 사람");
            }

            var r = Simulate(d, watchGhost);
            float sum = r.CoreGain;
            for (int k = 1; k < 3; k++) sum += Simulate(d, watchGhost).CoreGain;
            float avg = sum / 3f;
            gains[d] = avg;

            GD.Print($"   DAY{d} — 코어 평균 +{avg:0.0}% (목표 {Target(d):0}%) · " +
                     $"경고 {r.Warnings}회 · 고장 {r.Breakdowns}건");

            // 그날 업무 · 경고 발생은 DAY2~5 만 본다(DAY1 은 일정 검사 대상이 아니다).
            if (d >= 2)
            {
                var todays = spawns.Where(s => s.Day == d && !s.Recurring).Select(s => s.TaskId).Distinct().ToList();
                if (!watchGhost)
                {
                    Check(todays.All(r.Spawned.Contains), $"DAY{d} 그날 업무({string.Join(", ", todays)})가 실제로 뜬다");
                    Check(r.Warnings >= 1, $"DAY{d} 경고가 뜬다");
                }
            }

            float target = Target(d);
            if (watchGhost)
                // 대응 대역은 목표의 70% 는 채워야 한다 — CCTV 를 돌려 본 보람이 있어야 한다.
                Check(avg >= target * 0.7f,
                    $"DAY{d} 대응 근무가 목표의 70% 이상 (평균 {avg:0.0}% / 목표 {target:0}% → {target * 0.7f:0.0}%)");
            else
                // 수동 대역은 줄어들지만 않으면 된다. 다만 목표의 40% 는 넘어야
                // "손대지 않아도 어느 정도는 굴러간다" 가 성립한다.
                Check(avg >= 0f && avg >= target * 0.4f,
                    $"DAY{d} 수동 근무가 줄지 않고 목표의 40% 이상 (평균 {avg:0.0}% / 목표 {target:0}% → {target * 0.4f:0.0}%)");
        }
        return gains;
    }

    private static float Target(int day) => OpsProfile.For(day)?.TargetCoreGain ?? 18f;

    private sealed class Result
    {
        public float CoreGain;
        public int Warnings, Breakdowns;
        public readonly HashSet<string> Spawned = new();
    }

    // Day2FlowTest 의 근무 대역과 같은 방식 — 사람이 모자란 곳으로 한 명씩 빌려 보낸다.
    //
    // watchGhost 를 켜면 이상 개체가 나타났을 때 그 방을 CCTV 로 띄워 둔다(= 관리자가
    // 화면을 돌려 찾아내 지켜보는 것). 없애는 판정은 GhostHauntSystem 이 평소대로 한다 —
    // 검사용 뒷문을 따로 만들지 않는다.
    private Result Simulate(int day, bool watchGhost = false)
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
            // 괴물이 있으면 그 방을 본다. 없으면 코어실로 되돌린다(평소 보는 화면).
            if (watchGhost)
                _sim.SetSurveillanceTarget(_sim.Ghost is { Active: true } g ? g.ActiveRoomId : "core_room");

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
