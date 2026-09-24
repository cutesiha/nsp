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

        // ── 실제 근무 ─────────────────────────────────────────────────────
        //
        // 대역 셋. 셋 다 **같은 방해자 · 같은 순서**로 돈다(대역마다 따로 뽑으면 비교가 안 된다).
        //   수동 : CCTV 를 돌려 보지 않는다(괴물 방치). 격리도 안 한다.
        //   대응 : 괴물이 나타나면 그 방을 CCTV 로 지켜봐 없앤다. 격리는 안 한다.
        //   탐정 : 대응과 같되 DAY1 이 끝난 뒤(휴게 시점) 결번자를 격리한다.
        //          — 추리로 도달할 수 있는 최선을 재는 대역이라 정답을 알고 시작해도 된다.
        RunMeasurement(spawns);

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 측정 ──────────────────────────────────────────────────────────────

    private enum Band { Manual, Reactive, Detective }
    private static string BandName(Band b) => b switch
    {
        Band.Manual => "수동", Band.Reactive => "대응", _ => "탐정",
    };

    private const int Trials = 12;

    // 그날 기대하는 코어 복구량(data/ops/day*.tres 의 TargetCoreGain).
    private static float Target(int day) => OpsProfile.For(day)?.TargetCoreGain ?? 18f;

    // 하루치 근무 하나를 독립적으로 돌린다.
    //
    // Simulate 가 시작할 때 시뮬레이션을 초기화하므로 날짜끼리 이어지지 않는다 — 덕분에
    // "대응의 DAY3" 과 "탐정의 DAY3" 을 같은 조건에서 따로 잴 수 있다.
    private Result Day(int day, string saboteur, bool watchGhost, bool isolate)
    {
        GameState.Instance.ResetRun(day);
        GameState.Instance.SetSaboteur(saboteur);
        return Simulate(day, watchGhost, isolate, saboteur);
    }

    private void RunMeasurement(List<TaskSpawnDef> spawns)
    {
        // [대역][날짜] → 6회분 기록.
        var runs = new Dictionary<(Band, int), List<Result>>();
        foreach (Band b in System.Enum.GetValues<Band>())
            for (int d = 1; d <= 5; d++) runs[(b, d)] = new List<Result>();

        // 회차별 5일 합계 — 같은 실행 안에서 대역끼리 뺄 수 있어야 비교가 성립한다.
        var totals = new Dictionary<Band, List<float>>();
        foreach (Band b in System.Enum.GetValues<Band>()) totals[b] = new List<float>();

        for (int t = 0; t < Trials; t++)
        {
            // 이번 회차의 방해자를 한 번만 뽑아 세 대역이 같은 사람으로 돈다.
            GameState.Instance.ResetRun(1);
            GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
            string saboteur = GameState.Instance.SaboteurEmployeeId;

            // 수동 대역 — DAY1~5.
            float manualSum = 0f;
            for (int d = 1; d <= 5; d++)
            {
                var r = Day(d, saboteur, watchGhost: false, isolate: false);
                runs[(Band.Manual, d)].Add(r);
                manualSum += r.CoreGain;
            }
            totals[Band.Manual].Add(manualSum);

            // 대응 · 탐정은 **같은 DAY1** 을 공유하고 DAY2 부터 갈라진다.
            // (격리는 DAY1 근무가 끝난 휴게 시점에 이뤄지므로 DAY1 은 같을 수밖에 없다.)
            var day1 = Day(1, saboteur, watchGhost: true, isolate: false);
            runs[(Band.Reactive, 1)].Add(day1);
            runs[(Band.Detective, 1)].Add(day1);

            float reactiveSum = day1.CoreGain, detectiveSum = day1.CoreGain;
            for (int d = 2; d <= 5; d++)
            {
                var rr = Day(d, saboteur, watchGhost: true, isolate: false);
                runs[(Band.Reactive, d)].Add(rr);
                reactiveSum += rr.CoreGain;

                var dr = Day(d, saboteur, watchGhost: true, isolate: true);
                runs[(Band.Detective, d)].Add(dr);
                detectiveSum += dr.CoreGain;
            }
            totals[Band.Reactive].Add(reactiveSum);
            totals[Band.Detective].Add(detectiveSum);
        }

        // ── 표 1 : 코어 복구량 ────────────────────────────────────────
        GD.Print($"\n---------------- 코어 복구량 ({Trials}회 · 평균 [최소~최대]) ----------------");
        GD.Print("   대역    " + string.Join("", Enumerable.Range(1, 5).Select(d => $"{"DAY" + d,-18}")) + "합계");
        foreach (Band band in System.Enum.GetValues<Band>())
        {
            var row = new System.Text.StringBuilder($"   {BandName(band),-6}");
            for (int d = 1; d <= 5; d++)
            {
                var g = runs[(band, d)].Select(x => x.CoreGain).ToList();
                row.Append($"{g.Average(),5:0.0} [{g.Min(),5:0.0}~{g.Max(),5:0.0}]");
            }
            float sum = Enumerable.Range(1, 5).Sum(d => runs[(band, d)].Average(x => x.CoreGain));
            row.Append($"  {sum,6:0.0} / 100");
            GD.Print(row.ToString());
        }

        // ── 표 2 : 코어 장부 ──────────────────────────────────────────
        GD.Print($"\n---------------- 코어 장부 (평균) ----------------");
        GD.Print("   대역  날짜   복구+   방해-   불안정-   정전-   기타-   |  자재정지  코어1명이하  이탈(명·초)");
        foreach (Band band in System.Enum.GetValues<Band>())
        {
            for (int d = 1; d <= 5; d++)
            {
                var rs = runs[(band, d)];
                GD.Print($"   {BandName(band),-4} DAY{d}  " +
                         $"{rs.Average(x => x.CoreUp),6:0.0}  {rs.Average(x => x.LossSabotage),6:0.0}  " +
                         $"{rs.Average(x => x.LossUnstable),8:0.0}  {rs.Average(x => x.LossBlackout),6:0.0}  " +
                         $"{rs.Average(x => x.LossOther),6:0.0}  |  " +
                         $"{rs.Average(x => x.MaterialStallSeconds),7:0.0}초  " +
                         $"{rs.Average(x => x.CoreThinSeconds),9:0.0}초  " +
                         $"{rs.Average(x => x.DownSeconds),10:0.0}초");
            }
        }

        // ── 종료 조건 ────────────────────────────────────────────────
        var det = totals[Band.Detective];
        var rea = totals[Band.Reactive];
        var man = totals[Band.Manual];
        var diff = det.Zip(rea, (a, b) => a - b).ToList();
        int positive = diff.Count(x => x > 0f);

        GD.Print($"\n---------------- 종료 조건 ----------------");
        GD.Print($"   ① 탐정 합계 평균 {det.Average():0.0}  (90 ~ 120)                     " +
                 $"{(det.Average() >= 90f && det.Average() <= 120f ? "OK" : "X")}");
        GD.Print($"   ② 탐정 − 대응 평균 {diff.Average():+0.0;-0.0}  (>= +8) · 양수 {positive}/{Trials} (>= 9)   " +
                 $"{(diff.Average() >= 8f && positive >= 9 ? "OK" : "X")}");
        bool noNegative = true;
        foreach (Band b in System.Enum.GetValues<Band>())
            for (int d = 1; d <= 5; d++)
                if (runs[(b, d)].Average(x => x.CoreGain) < 0f) noNegative = false;
        GD.Print($"   ③ 수동 합계 평균 {man.Average():0.0}  (40 ~ 60) · 마이너스인 날 없음 {(noNegative ? "예" : "아니오")}   " +
                 $"{(man.Average() >= 40f && man.Average() <= 60f && noNegative ? "OK" : "X")}");

        // ── 검사 ─────────────────────────────────────────────────────
        // 결번자를 격리하지 않고도 100% 에 닿으면 추리할 이유가 사라진다.
        float reactiveAvg = rea.Average();
        Check(reactiveAvg <= 100f, $"격리 없이 대응만으로는 코어 100% 에 닿지 않는다 (대응 합계 {reactiveAvg:0.0}%)");

        // 탐정 대역은 DAY2~5 에 방해공작이 한 건도 없어야 한다(격리가 실제로 먹혔다는 증거).
        int detectiveSabotage = Enumerable.Range(2, 4).Sum(d => runs[(Band.Detective, d)].Sum(x => x.Sabotages));
        Check(detectiveSabotage == 0,
            $"탐정 대역은 DAY2~5 방해공작 0건 (실제 {detectiveSabotage}건 / {Trials}회 합)");

        // 날짜별 기준 — 수동은 "줄지 않고 목표의 40%", 대응은 "목표의 70%".
        // CoreGain > 0 하나만 보면 목표치의 5% 만 채워도 통과해 밸런스 회귀를 놓친다.
        for (int d = 1; d <= 5; d++)
        {
            float target = Target(d);
            float manual = runs[(Band.Manual, d)].Average(x => x.CoreGain);
            float reactive = runs[(Band.Reactive, d)].Average(x => x.CoreGain);
            Check(manual >= 0f && manual >= target * 0.4f,
                $"DAY{d} 수동 근무가 줄지 않고 목표의 40% 이상 (평균 {manual:0.0}% / 필요 {target * 0.4f:0.0}%)");
            Check(reactive >= target * 0.7f,
                $"DAY{d} 대응 근무가 목표의 70% 이상 (평균 {reactive:0.0}% / 필요 {target * 0.7f:0.0}%)");
        }

        // 그날 업무 · 경고는 수동 대역 한 번으로 충분히 확인된다.
        for (int d = 2; d <= 5; d++)
        {
            var first = runs[(Band.Manual, d)][0];
            var todays = spawns.Where(x => x.Day == d && !x.Recurring).Select(x => x.TaskId).Distinct().ToList();
            Check(todays.All(first.Spawned.Contains), $"DAY{d} 그날 업무({string.Join(", ", todays)})가 실제로 뜬다");
            Check(runs[(Band.Manual, d)].Any(x => x.Warnings >= 1), $"DAY{d} 경고가 뜬다");
        }

        // 방해자는 5일 내내 같은 사람이어야 한다(대역을 돌리는 동안 바뀌지 않았다).
        Check(true, "방해자는 회차마다 한 번만 뽑아 세 대역이 같은 사람으로 돌았다");
    }

    private sealed class Result
    {
        public float CoreGain;
        public int Warnings, Breakdowns, Sabotages;
        public readonly HashSet<string> Spawned = new();

        // 코어 장부 — 얼마나 늘었고(CoreUp), 무엇 때문에 깎였는가.
        public float CoreUp, LossSabotage, LossUnstable, LossBlackout, LossOther;
        // 멈춰 있던 시간. 코어가 "깎여서" 0 인지 "안 늘어서" 0 인지를 가른다.
        public float MaterialStallSeconds;   // 자재가 없어 코어 복구가 멈춰 있던 초
        public float CoreThinSeconds;        // 코어실 근무자가 0~1 명이던 초
        public float DownSeconds;            // 기절·공황으로 빠진 인원 × 초
    }

    // Day2FlowTest 의 근무 대역과 같은 방식 — 사람이 모자란 곳으로 한 명씩 빌려 보낸다.
    //
    // watchGhost 를 켜면 이상 개체가 나타났을 때 그 방을 CCTV 로 띄워 둔다(= 관리자가
    // 화면을 돌려 찾아내 지켜보는 것). 없애는 판정은 GhostHauntSystem 이 평소대로 한다 —
    // 검사용 뒷문을 따로 만들지 않는다.
    private Result Simulate(int day, bool watchGhost, bool isolateSaboteur, string saboteurId)
    {
        var r = new Result();
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
        TabooRuleSystem.Instance?.ActivateDailyTaboos(ControlRoom3DController.TodayTabooIds());

        // 탐정 대역: 지난 휴게시간에 결번자를 격리해 둔 상태로 근무를 시작한다.
        //
        // **배치보다 먼저** 해야 한다. 순서가 뒤바뀌면 결번자가 자리를 하나 차지한 뒤
        // 격리돼, 그 자리가 빈 채로 근무가 돌아간다(예비가 들어갈 기회를 잃는다).
        if (isolateSaboteur && !string.IsNullOrEmpty(saboteurId)) _sim.IsolateEmployee(saboteurId);

        // 고정 슬롯 다섯 자리. 여섯 번째 사람은 예비다.
        //
        // 자리마다 주인이 정해져 있고, 그 사람이 격리돼 못 나오면 **그 자리에만** 예비를
        // 넣는다. 명단을 한 칸씩 당기면 격리 하나로 모두의 근무지가 바뀌어 대역 간 비교가
        // 성립하지 않는다.
        var slots = new[] { "core_room", "core_room", "power_room", "maintenance_room", "storage_room" };
        var roster = _sim.GetActiveEmployeeIds().ToList();
        var used = new HashSet<string>();
        bool Available(string id)
        {
            var st = _sim.GetEmployeeState(id);
            return st is { Alive: true, Isolated: false } && !used.Contains(id);
        }
        for (int slot = 0; slot < slots.Length; slot++)
        {
            string who = slot < roster.Count && Available(roster[slot]) ? roster[slot] : "";
            if (who.Length == 0) who = roster.FirstOrDefault(Available) ?? "";
            if (who.Length == 0) continue;
            used.Add(who);
            _sim.AssignToRoom(who, slots[slot]);
        }

        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        float start = GameState.Instance.CoreProgress;
        string borrowed = "", origin = "";

        // 코어 증감을 사유별로 받아 적는다. 근무가 끝나면 반드시 뗀다.
        void Ledger(float delta, string reason)
        {
            if (delta >= 0f) { r.CoreUp += delta; return; }
            switch (reason)
            {
                case "복구 작업 방해": r.LossSabotage += -delta; break;
                case "정전 혼란": r.LossBlackout += -delta; break;
                case "코어 출력 불안정": r.LossUnstable += -delta; break;
                default: r.LossOther += -delta; break;
            }
        }
        GameState.CoreLedger += Ledger;

        for (float t = 0f; t < DayObjectives.MaxShiftSeconds; t += Step)
        {
            // 괴물이 있으면 그 방을 본다. 없으면 코어실로 되돌린다(평소 보는 화면).
            if (watchGhost)
                _sim.SetSurveillanceTarget(_sim.Ghost is { Active: true } g ? g.ActiveRoomId : "core_room");

            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);

            // ── 멈춰 있던 시간 ───────────────────────────────────────
            // 코어 복구 업무가 걸려 있는데 자재가 모자라 진행이 막힌 구간.
            var coreTask = _sim.GetPrimarySpawnedTask("core_room");
            if (coreTask != null
                && _sim.GetTaskDef(coreTask.TaskId)?.EffectType == TaskEffectType.AddCoreProgress
                && GameState.Instance.Materials < RoomStaffing.CoreMaterialCost())
                r.MaterialStallSeconds += Step;
            // 코어실에 사람이 한 명 이하인 구간(복구 속도가 거의 나오지 않는다).
            if (_sim.OnDutyCount("core_room") <= 1) r.CoreThinSeconds += Step;
            // 기절·공황으로 근무에서 빠진 인원 × 초.
            foreach (string id in _sim.GetEmployeeIds())
            {
                var st = _sim.GetEmployeeState(id);
                if (st is { Alive: true } && (st.Incapacitated || _sim.IsPanicked(id))) r.DownSeconds += Step;
            }

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

        GameState.CoreLedger -= Ledger;
        r.CoreGain = GameState.Instance.CoreProgress - start;
        r.Warnings = _sim.Warnings.Raised;
        r.Breakdowns = IncidentTracker.OpenedCount;
        // 이 근무에 실제로 일어난 방해공작 수(격리가 먹혔는지 여기서 확인한다).
        r.Sabotages = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Sabotage);
        return r;
    }

    private void Check(bool ok, string label)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }
}
