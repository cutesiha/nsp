using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.Taboo;
using NSP.View;

namespace NSP.Debug;

// DAY2 플레이 가능 여부 검증.
//
//   godot --headless --path . scenes/debug/Day2FlowTest.tscn --quit-after 2000
//
// DAY2 전용 코드를 만들지 않는 것이 이 작업의 핵심이므로, 여기서 보는 것도 그것이다 —
// "같은 시스템에 DAY 데이터만 갈아 끼웠을 때 2일차가 제대로 굴러가는가".
public partial class Day2FlowTest : Node
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
        GD.Print("################ DAY2 진입 / 운영 검증 ################");
        CheckData();
        CheckUnlocks();
        CheckObjectives();
        var day1 = Average(1);
        var day2 = Average(2);
        Compare(day1, day2);
        CheckDayChain();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // --- 데이터 ------------------------------------------------------------

    private void CheckData()
    {
        var d1 = OpsProfile.For(1);
        var d2 = OpsProfile.For(2);
        var d3 = OpsProfile.For(3);
        GD.Print($"\n[데이터] 운영 프로필 — DAY1 Day={d1?.Day} / DAY2 Day={d2?.Day} / DAY3 Day={d3?.Day}");
        Check(d2 != null && d2.Day == 2, "DAY2 전용 운영 프로필이 있다");
        // DAY3~5 는 날마다 다른 일정의 자기 프로필을 쓴다(상세 검증은 DayScheduleTest).
        Check(d3 != null && d3.Day == 3, "DAY3 은 자기 운영 프로필을 쓴다");
        if (d1 == null || d2 == null) return;

        GD.Print($"        경고 간격 {d1.WarningGapSeconds:0.#}→{d2.WarningGapSeconds:0.#}초 · " +
                 $"유예 {d1.WarningGraceSeconds:0.#}→{d2.WarningGraceSeconds:0.#}초 · " +
                 $"동시 경고 {d1.MaxConcurrentWarnings}→{d2.MaxConcurrentWarnings}개");
        GD.Print($"        방해자 시작 {d1.SaboteurStartSeconds:0}→{d2.SaboteurStartSeconds:0}초 · " +
                 $"준비 {d1.SabotagePrepareMinSeconds:0}~{d1.SabotagePrepareMaxSeconds:0}→" +
                 $"{d2.SabotagePrepareMinSeconds:0}~{d2.SabotagePrepareMaxSeconds:0}초 · " +
                 $"하루 {d2.MaxSabotageActionsPerDay}회");

        Check(d2.MaxConcurrentWarnings == 1, "동시에 뜨는 경고는 DAY2 에서도 1개");
        Check(!d2.AllowMurder, "DAY2 에도 살인은 없다");

        // 아래 둘은 **DAY1 과의 관계**만 본다. 예전에는 "하루 1회" · "6초 이내" 처럼 그때의
        // day2.tres 값을 그대로 적어 두었는데, 밸런스를 조정할 때마다 검사가 데이터를
        // 따라오지 못해 실패했다. 검사가 지켜야 할 것은 특정 숫자가 아니라 "DAY2 는 DAY1
        // 보다 빡세지되 한 번에 뛰어오르지는 않는다" 는 설계 의도다.
        Check(d2.MaxSabotageActionsPerDay >= d1.MaxSabotageActionsPerDay,
            $"방해공작 횟수가 DAY1 보다 줄지 않았다 ({d1.MaxSabotageActionsPerDay} → {d2.MaxSabotageActionsPerDay}회)");
        float shift = Config.Instance?.Data?.DayLengthSeconds ?? 120f;
        float earlier = d1.SaboteurStartSeconds - d2.SaboteurStartSeconds;
        Check(earlier > 0f && earlier <= shift * 0.5f,
            $"방해공작 기회가 더 일찍 열리되 근무 절반을 넘게 당기지는 않는다 " +
            $"({d1.SaboteurStartSeconds:0} → {d2.SaboteurStartSeconds:0}초, {earlier:0}초 당김 / 상한 {shift * 0.5f:0}초)");
        // 2026-09-22 설계 변경: DAY1 의 시스템이 DAY5 까지 그대로 이어진다(금기 없음).
        Check(d2.DailyTabooIds.Count == 0, "DAY2 에도 금기는 없다");
        Check(d2.Rooms.Count == d1.Rooms.Count && d2.Rooms.All(r => d1.Rooms.Any(o => o.RoomId == r.RoomId)),
            "DAY2 작업실 구성이 DAY1 과 같다");
    }

    // --- 해금 --------------------------------------------------------------

    private void CheckUnlocks()
    {
        GameState.Instance.ResetRun(1);
        bool d1Stats = DayFeatures.StatsEnabled, d1Stress = DayFeatures.StressEnabled,
             d1Taboo = DayFeatures.TaboosEnabled;
        bool d1Vent = _sim.IsRoomActive("vent_room"), d1Med = _sim.IsRoomActive("medical_room");

        GameState.Instance.ResetRun(2);
        GD.Print($"\n[해금] DAY1 능력치 {d1Stats} 스트레스 {d1Stress} 금기 {d1Taboo} 환기실 {d1Vent} 의무실 {d1Med}");
        GD.Print($"       DAY2 능력치 {DayFeatures.StatsEnabled} 스트레스 {DayFeatures.StressEnabled} " +
                 $"금기 {DayFeatures.TaboosEnabled} 환기실 {_sim.IsRoomActive("vent_room")} " +
                 $"의무실 {_sim.IsRoomActive("medical_room")}");

        // 경영 리워크: 능력치 · 금기는 걷어냈고(잠금 유지), 스트레스와 환기실 · 의무실은 DAY1 부터 켜진다.
        Check(!d1Stats && d1Stress && !d1Taboo, "DAY1 에는 능력치·금기가 꺼져 있고 스트레스는 켜져 있다");
        Check(!DayFeatures.StatsEnabled && DayFeatures.StressEnabled && !DayFeatures.TaboosEnabled,
            "DAY2 도 같은 규칙(능력치·금기 없음, 스트레스 있음)");
        Check(d1Vent && d1Med && _sim.IsRoomActive("vent_room") && _sim.IsRoomActive("medical_room"),
            "환기실·의무실은 DAY1 부터 열려 있다(작업실 7개)");
        GameState.Instance.ResetRun(5);
        Check(!DayFeatures.StatsEnabled && !DayFeatures.TaboosEnabled && _sim.IsRoomActive("vent_room"),
            "DAY5 까지 같은 규칙이 유지된다");
        GameState.Instance.ResetRun(2);

        // 능력치가 실제 작업 효율에 들어가는가 — 같은 직원의 배율이 날마다 다르다.
        GameState.Instance.ResetRun(1);
        float flat = _sim.TechWorkMultiplier("cat");
        GameState.Instance.ResetRun(2);
        float real = _sim.TechWorkMultiplier("cat");
        GD.Print($"       고양이 기술 배율 DAY1 {flat:0.00} → DAY2 {real:0.00}");
        Check(Mathf.IsEqualApprox(flat, real), "DAY2 에도 능력치는 작업 효율에 반영되지 않는다(모두 보통)");

        // 금기 목록이 데이터에서 나온다.
        var taboos = ControlRoom3DController.TodayTabooIds();
        GD.Print($"       오늘의 금기 {taboos.Length}개 : {string.Join(", ", taboos)}");
        Check(taboos.Length == 0, "DAY2 오늘의 금기는 없다");
    }

    // --- 오늘의 업무 --------------------------------------------------------

    private void CheckObjectives()
    {
        GameState.Instance.ResetRun(2);
        var lines = DayObjectives.Lines();
        GD.Print($"\n[업무] DAY2 {lines.Count}개 (최대 근무 {DayObjectives.MaxShiftSeconds:0}초)");
        foreach (var l in lines)
            GD.Print($"       {(l.Required ? "필수" : "선택")}  {l.Def.DisplayText}   {l.ProgressText}");

        Check(lines.Count == 3 && lines.Count(l => l.Required) == 2, "필수 2 + 선택 1");
        Check(Mathf.IsEqualApprox(DayObjectives.MaxShiftSeconds, 120f), "근무 시간은 DAY1 과 같은 120초");
        Check(lines.Any(l => l.Def.Type == DayObjectiveType.CoreProgress
                             && Mathf.IsEqualApprox(l.Def.TargetValue, 38f)), "필수① 코어 누적 38%");
        Check(lines.Any(l => l.Def.Type == DayObjectiveType.IncidentsResolved
                             && Mathf.IsEqualApprox(l.Def.TargetValue, 2f)), "필수② 경고/고장 2회 해결");
        var opt = lines.FirstOrDefault(l => !l.Required);
        Check(opt != null && !opt.Def.TargetRooms.Contains("vent_room") && !opt.Def.TargetRooms.Contains("medical_room"),
            "선택 업무가 잠긴 작업실(환기실·의무실)을 요구하지 않는다");
        Check(!lines.Any(l => l.Def.DisplayText.Contains("방해") || l.Def.DisplayText.Contains("결번")),
            "방해공작 관련 업무는 노출되지 않는다");
    }

    // --- 실제 근무 ----------------------------------------------------------

    private sealed class Result
    {
        public float CoreGain;
        public int Warnings, Prevented, Breakdowns;
        public float SabotageAt = -1f;
        public bool CulpritNamed;
        public int NamedCount;
        public int UnorderedMoves;
        public readonly HashSet<string> SpawnedTasks = new();
    }

    // 난수가 섞이므로 하루를 여러 번 돌려 평균을 본다.
    private Result Average(int day)
    {
        var all = new List<Result>();
        for (int i = 0; i < 3; i++) all.Add(Simulate(day));
        var avg = new Result
        {
            CoreGain = all.Average(x => x.CoreGain),
            Warnings = Mathf.RoundToInt((float)all.Average(x => x.Warnings)),
            Prevented = Mathf.RoundToInt((float)all.Average(x => x.Prevented)),
            Breakdowns = Mathf.RoundToInt((float)all.Average(x => x.Breakdowns)),
            SabotageAt = all.Where(x => x.SabotageAt >= 0f).Select(x => x.SabotageAt).DefaultIfEmpty(-1f).Average(),
            NamedCount = all.Count(x => x.CulpritNamed),
            UnorderedMoves = all.Sum(x => x.UnorderedMoves),
        };
        foreach (var r in all) avg.SpawnedTasks.UnionWith(r.SpawnedTasks);
        return avg;
    }

    private Result Simulate(int day)
    {
        var r = new Result();
        var ordered = new Dictionary<string, string>();

        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
        TabooRuleSystem.Instance?.ActivateDailyTaboos(ControlRoom3DController.TodayTabooIds());

        // 배치는 DAY1 과 같다. DAY2 의 새 방(환기실·의무실)은 상주가 아니라
        // "업무가 뜨면 사람을 보내는 곳" 이므로, 같은 인원으로 더 많은 방을 돌리게 된다.
        var plan = new[] { ("core_room", 2), ("power_room", 1), ("maintenance_room", 1),
                           ("guard_room", 1), ("storage_room", 1) };
        var roster = _sim.GetActiveEmployeeIds().ToList();
        int i = 0;
        foreach (var (room, n) in plan)
            for (int k = 0; k < n && i < roster.Count; k++, i++)
            {
                ordered[roster[i]] = room;
                _sim.AssignToRoom(roster[i], room);
            }

        GameState.Instance.AssignRandomSaboteur(roster);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);

        float start = GameState.Instance.CoreProgress;
        var where = roster.ToDictionary(id => id, id => _sim.GetEmployeeState(id).CurrentRoomId);
        string borrowed = "", origin = "";

        for (float t = 0f; t < DayObjectives.MaxShiftSeconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);

            // 자율 이동 감시 — 판정은 단순하다.
            // "배치받지 않은 방에 가서 멈춰 있다" 면 누군가 제 발로 간 것이다.
            // 통로를 지나는 중(IsMoving)과 기절·격리 이송은 제외한다.
            foreach (string id in roster)
            {
                var st = _sim.GetEmployeeState(id);
                if (st == null || st.CurrentRoomId == where[id]) continue;
                where[id] = st.CurrentRoomId;
                if (st.IsMoving || st.Incapacitated || st.Isolated) continue;
                if (string.IsNullOrEmpty(st.AssignedRoomId) || st.CurrentRoomId == st.AssignedRoomId) continue;
                r.UnorderedMoves++;
                GD.Print($"       [자율 이동] {id} → {st.CurrentRoomId} " +
                         $"(배치는 {st.AssignedRoomId}) @{GameState.Instance.DayTimeSeconds:0}초");
            }

            foreach (var task in _sim.GetTaskDefs())
                if (_sim.GetActiveTasksForRoom(task.RoomId).Any(x => x.TaskId == task.TaskId))
                    r.SpawnedTasks.Add(task.TaskId);

            // 관리자 대역 — 지금 사람이 모자란 곳을 하나 찾는다.
            // 경고뿐 아니라 "제한시간 있는 업무인데 인원 미달" 도 사람이 가야 하는 상황이다.
            // (DAY2 의 발전기 점검·환기 점검이 여기에 걸린다.)
            string needRoom = "";
            int needStaff = 0;
            var w = _sim.Warnings.Active.FirstOrDefault();
            if (w != null && _sim.OnDutyCount(w.RoomId) < w.RequiredStaff)
            {
                needRoom = w.RoomId;
                needStaff = w.RequiredStaff;
            }
            else
            {
                foreach (string room in _sim.GetRoomIds())
                {
                    var task = _sim.GetPrimarySpawnedTask(room);
                    if (task is not { Status: SpawnedTaskStatus.Active, Recurring: false, IsRepair: false })
                        continue;
                    int min = Mathf.Max(1, _sim.GetTaskDef(task.TaskId)?.MinWorkersToProgress ?? 1);
                    if (_sim.OnDutyCount(room) >= min) continue;
                    needRoom = room;
                    needStaff = min;
                    break;
                }
                // 수리도 사람이 필요하다.
                if (needRoom.Length == 0)
                    foreach (string room in _sim.GetRoomIds())
                    {
                        if (!_sim.HasRepairPending(room)) continue;
                        var task = _sim.GetPrimarySpawnedTask(room);
                        int min = task?.MinWorkersOverride > 0 ? task.MinWorkersOverride : 1;
                        if (_sim.OnDutyCount(room) >= min) continue;
                        needRoom = room;
                        needStaff = min;
                        break;
                    }
            }

            if (needRoom.Length == 0)
            {
                if (borrowed.Length > 0)
                {
                    ordered[borrowed] = origin;
                    _sim.AssignToRoom(borrowed, origin);
                    borrowed = ""; origin = "";
                }
                continue;
            }
            if (borrowed.Length > 0) continue;
            string donor = _sim.GetRoomIds().Where(x => x != needRoom)
                .OrderByDescending(_sim.OnDutyCount).FirstOrDefault(x => _sim.OnDutyCount(x) >= 2);
            if (donor == null) continue;
            borrowed = _sim.GetRoomState(donor).OccupantEmployeeIds.FirstOrDefault() ?? "";
            origin = donor;
            if (borrowed.Length > 0) { ordered[borrowed] = needRoom; _sim.AssignToRoom(borrowed, needRoom); }
        }

        r.CoreGain = GameState.Instance.CoreProgress - start;
        r.Warnings = _sim.Warnings.Raised;
        r.Prevented = _sim.Warnings.Prevented;
        r.Breakdowns = IncidentTracker.OpenedCount;
        r.SabotageAt = _sim.Saboteur.ActedAtSeconds;
        var sab = EventLog.Instance.GetAllEntries()
            .FirstOrDefault(e => e.Day == day && e.EventType == LogEventType.Sabotage);
        r.CulpritNamed = sab is { WitnessEmployeeIds.Count: > 0 };
        return r;
    }

    private void Compare(Result d1, Result d2)
    {
        GD.Print($"\n[근무] DAY1 코어 {d1.CoreGain:0.0}% · 경고 {d1.Warnings}회(막음 {d1.Prevented}) · " +
                 $"고장 {d1.Breakdowns}건 · 방해공작 {Fmt(d1.SabotageAt)}");
        GD.Print($"       DAY2 코어 {d2.CoreGain:0.0}% · 경고 {d2.Warnings}회(막음 {d2.Prevented}) · " +
                 $"고장 {d2.Breakdowns}건 · 방해공작 {Fmt(d2.SabotageAt)}");
        GD.Print($"       DAY2 발생 업무: {string.Join(", ", d2.SpawnedTasks.OrderBy(x => x))}");

        Check(d1.UnorderedMoves == 0 && d2.UnorderedMoves == 0,
            "DAY2 에서도 직원은 플레이어 명령으로만 움직인다");
        Check(!d2.SpawnedTasks.Contains("power_generator_check"), "DAY2 에도 발전기 점검(2명 고정 업무)은 없다");
        // 경영 리워크: 환기실 · 의무실이 DAY1 부터 열려 있으므로 그 방 업무가 생기는 것이 정상이다.
        Check(!d1.SpawnedTasks.Contains("vent_circulation_check"), "DAY1 에는 환기 업무가 뜨지 않는다");
        // DAY2 는 DAY1 과 같은 시스템에서 숫자만 조금 조였다. 무작위 근무 몇 번의 결과라
        // 편차가 크므로 "무너지지도, 훨씬 쉬워지지도 않는다" 만 본다.
        float ratio = d1.CoreGain <= 0f ? 1f : d2.CoreGain / d1.CoreGain;
        GD.Print($"       DAY2 코어 성과 = DAY1 의 {ratio:P0}");
        Check(ratio >= 0.6f && ratio <= 1.35f, $"DAY2 성과가 DAY1 과 비슷한 범위다 (코어 {ratio:P0})");
        // 실제 방해 시각은 기회(빈 방 · 목격자)에 따라 흔들린다 — 규칙상의 기회 창으로 본다.
        var p1 = OpsProfile.For(1); var p2 = OpsProfile.For(2);
        Check(p2 != null && p1 != null && p2.SabotageWindowStartSeconds <= p1.SabotageWindowStartSeconds,
            "DAY2 방해공작 기회 창이 DAY1 보다 늦지 않다");
        GD.Print($"       범인이 특정된 근무 — DAY1 {d1.NamedCount}/3 · DAY2 {d2.NamedCount}/3");
        Check(d2.NamedCount <= 1, "DAY2 에서도 범인 특정은 드물다(자동 노출 아님)");
    }

    // --- DAY 전환 ----------------------------------------------------------

    private void CheckDayChain()
    {
        GameState.Instance.ResetRun(1);
        var seen = new List<string>();
        for (int day = 1; day <= 5; day++)
        {
            var plan = DayObjectives.For(day);
            var ops = OpsProfile.For(day);
            seen.Add($"DAY{day}: 업무 {(plan == null ? "없음" : plan.Objectives.Count + "개")} / " +
                     $"운영 Day={ops?.Day.ToString() ?? "없음"} / 근무 {DayObjectives.MaxShiftSeconds:0}초");
            if (day < 5) GameState.Instance.GoToNextDay();
        }
        GD.Print("\n[전환] " + string.Join("\n       ", seen));
        Check(GameState.Instance.CurrentDay == 5, "DAY1→5 전환이 같은 경로로 이어진다");
        Check(Enumerable.Range(1, 5).All(d => DayObjectives.For(d) != null), "DAY1~5 모두 업무 데이터가 있다");
        Check(Enumerable.Range(1, 5).All(d => OpsProfile.For(d) != null),
            "DAY3~5 는 프로필이 없어도 이전 날 값으로 굴러간다");
    }

    // --- 도우미 -------------------------------------------------------------

    private static string Fmt(float v) => v < 0f ? "없음" : $"{v:0}초";

    private void Check(bool ok, string label)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }
}
