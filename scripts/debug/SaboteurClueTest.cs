using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// DAY1 템포 · 결번자 전조 자동 검증.
//
//   godot --headless --path . scenes/debug/SaboteurClueTest.tscn --quit-after 2000
//
// 보는 것은 둘이다.
//   ① 120초 안에 "판단할 거리"가 끊기지 않는가 (최장 무행동 구간)
//   ② 결번자가 플레이어 명령 없이 방을 옮기지 않는가 (한 건이라도 있으면 실패)
public partial class SaboteurClueTest : Node
{
    private const float Step = 1f / 30f;
    private static readonly string[] Roster = { "owl", "cat", "jellyfish", "rabbit", "crow", "fox" };

    private FacilitySimulation _sim;
    private int _pass, _fail;

    // 플레이어(테스트 대역)가 내린 배치 명령. 이 명령 없이 방이 바뀌면 자율 이동이다.
    private readonly Dictionary<string, string> _orderedMoves = new();
    private int _unorderedMoves;
    private readonly List<string> _unorderedLog = new();

    private sealed class Run
    {
        public string SaboteurId = "";
        public float FirstWarningAt = -1f;
        public float SecondEventAt = -1f;
        public float PrepareAt = -1f;
        public float SabotageAt = -1f;
        public string SabotageRoom = "";
        public int Precursors;
        public int CluePaths;
        public float CoreGain;
        public float LongestGap;
        public int Decisions;
        public int Cancels;
    }

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ DAY1 템포 / 결번자 전조 검증 ################");
        var ops = OpsProfile.For(1);
        if (ops == null) { GD.PrintErr("data/ops/day1.tres 실패"); return; }
        float length = DayObjectives.MaxShiftSeconds;
        GD.Print($"근무 {length:0}초 · 방해공작 활성 {ops.SaboteurStartSeconds:0}초 · " +
                 $"준비 {ops.SabotagePrepareMinSeconds:0}~{ops.SabotagePrepareMaxSeconds:0}초 · " +
                 $"목표 구간 {ops.SabotageWindowStartSeconds:0}~{ops.SabotageWindowEndSeconds:0}초");

        var runs = new List<Run>();
        for (int i = 0; i < 12; i++) runs.Add(Simulate(Roster[i % Roster.Length], ops));

        Report(runs, ops, length);
        CheckMovement();
        CheckTempo(runs, ops, length);
        CheckPrecursors(runs);
        CheckCancel(ops);
        CheckNormalBehaviour();
        CheckStrategies();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // --- 한 번의 근무 ------------------------------------------------------

    private Run Simulate(string saboteurId, OpsProfileDef ops)
    {
        StartShift(saboteurId);
        var run = new Run { SaboteurId = saboteurId };
        float length = DayObjectives.MaxShiftSeconds;
        float startCore = GameState.Instance.CoreProgress;
        string borrowed = "", origin = "";
        float lastDecision = 0f;
        int clues = 0;
        var where = Roster.ToDictionary(id => id, id => _sim.GetEmployeeState(id).CurrentRoomId);

        for (float t = 0f; t < length; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            float now = GameState.Instance.DayTimeSeconds;

            // 자율 이동 감시 — 플레이어 명령 없이 방이 바뀌면 그 자리에서 잡는다.
            foreach (string id in Roster)
            {
                var st = _sim.GetEmployeeState(id);
                if (st == null || st.CurrentRoomId == where[id]) continue;
                // 지시한 방에 도착할 때까지는 그 명령의 이동이다(통로를 지나는 것 포함).
                if (!_orderedMoves.ContainsKey(id))
                {
                    _unorderedMoves++;
                    _unorderedLog.Add($"{id} {where[id]} → {st.CurrentRoomId} @{now:0}초");
                }
                else if (_orderedMoves[id] == st.CurrentRoomId) _orderedMoves.Remove(id);
                where[id] = st.CurrentRoomId;
            }

            if (run.PrepareAt < 0f && _sim.Saboteur.Phase == SaboteurPhase.Preparing)
                run.PrepareAt = now;

            var w = _sim.Warnings.Active.FirstOrDefault();
            if (w != null && run.FirstWarningAt < 0f) { run.FirstWarningAt = now; lastDecision = now; run.Decisions++; }
            else if (w != null && run.SecondEventAt < 0f && now - run.FirstWarningAt > 12f)
            { run.SecondEventAt = now; lastDecision = now; run.Decisions++; }

            // 관리자 대역 — 경고가 뜨면 사람을 빌려 보낸다(= 의미 있는 판단).
            if (w == null)
            {
                if (borrowed.Length > 0)
                {
                    Order(borrowed, origin);
                    borrowed = ""; origin = "";
                    lastDecision = now;
                    run.Decisions++;
                }
            }
            else if (borrowed.Length == 0 && _sim.OnDutyCount(w.RoomId) < w.RequiredStaff)
            {
                string donor = _sim.GetRoomIds().Where(r => r != w.RoomId)
                    .OrderByDescending(_sim.OnDutyCount).FirstOrDefault(r => _sim.OnDutyCount(r) >= 2);
                if (donor != null)
                {
                    borrowed = _sim.GetRoomState(donor).OccupantEmployeeIds.FirstOrDefault(id => id != saboteurId) ?? "";
                    origin = donor;
                    if (borrowed.Length > 0) { Order(borrowed, w.RoomId); lastDecision = now; run.Decisions++; }
                }
            }

            if (_sim.Saboteur.HasActed && run.SabotageAt < 0f)
            {
                run.SabotageAt = _sim.Saboteur.ActedAtSeconds;
                run.SabotageRoom = _sim.Saboteur.ActedRoomId;
                lastDecision = now;
                run.Decisions++;
            }

            // 고장이 열려 있는 동안은 수습 중이라 빈 시간이 아니다.
            if (IncidentTracker.ActiveCount > 0) lastDecision = now;
            // 전조(설비 접근·수치 이상)도 "지금 볼 것이 있다"에 해당한다.
            if (_sim.Saboteur.Clues.Count > clues) { clues = _sim.Saboteur.Clues.Count; lastDecision = now; }
            run.LongestGap = Mathf.Max(run.LongestGap, now - lastDecision);
        }

        run.CoreGain = GameState.Instance.CoreProgress - startCore;
        run.Cancels = _sim.Saboteur.CancelCount;
        run.Precursors = _sim.Saboteur.Clues.Count;
        run.CluePaths = CountCluePaths(saboteurId, run);
        return run;
    }

    // 플레이어의 배치 명령. 이 경로로만 방이 바뀌어야 한다.
    private void Order(string employeeId, string roomId)
    {
        _orderedMoves[employeeId] = roomId;
        _sim.AssignToRoom(employeeId, roomId);
    }

    private int CountCluePaths(string saboteurId, Run run)
    {
        if (run.SabotageAt < 0f) return 0;
        DialogueContextBuilder.Invalidate();
        int paths = 0;
        var all = EventLog.Instance.GetAllEntries().Where(e => e.Day == 1).ToList();

        if (all.Any(e => e.EventType == LogEventType.Neglect && e.ActorEmployeeId == saboteurId
                         && e.WitnessEmployeeIds.Count > 0)) paths++;
        var sab = all.FirstOrDefault(e => e.EventType == LogEventType.Sabotage);
        if (sab != null && _sim.GetActiveEmployeeIds().Any(id => id != saboteurId
                && DialogueContextBuilder.KnowledgeOf(id, sab) != KnowledgeLevel.None)) paths++;
        if (PlayerKnownEvidence.HasRoomRecord(run.SabotageRoom)) paths++;
        return paths;
    }

    // --- 보고 -------------------------------------------------------------

    private void Report(List<Run> runs, OpsProfileDef ops, float length)
    {
        GD.Print("\n결번자   첫경고  두번째  준비    방해공작  전조 단서  코어    최장공백  판단");
        foreach (var r in runs)
            GD.Print($"  {Code(r.SaboteurId),-4}  {Fmt(r.FirstWarningAt),6}  {Fmt(r.SecondEventAt),6}  " +
                     $"{Fmt(r.PrepareAt),6}  {Fmt(r.SabotageAt),8}  {r.Precursors,3}  {r.CluePaths,3}   " +
                     $"{r.CoreGain,5:0.0}%  {r.LongestGap,6:0.0}초  {r.Decisions,3}회");

        var acted = runs.Where(r => r.SabotageAt >= 0f).ToList();
        GD.Print($"\n평균 — 첫 경고 {Avg(runs, r => r.FirstWarningAt):0.0}초 · " +
                 $"두 번째 {Avg(runs, r => r.SecondEventAt):0.0}초 · " +
                 $"준비 {Avg(acted, r => r.PrepareAt):0.0}초 · " +
                 $"방해공작 {Avg(acted, r => r.SabotageAt):0.0}초 " +
                 $"({acted.Count}/{runs.Count}회 발생)");
        GD.Print($"평균 코어 {runs.Average(r => r.CoreGain):0.0}% · 최장 무행동 구간 " +
                 $"{runs.Max(r => r.LongestGap):0.0}초 · 평균 판단 {runs.Average(r => r.Decisions):0.0}회");
    }

    // --- Test A/B : 자율 이동 금지 ------------------------------------------

    private void CheckMovement()
    {
        GD.Print($"\n[A/B] 플레이어 명령 없는 방 이동 {_unorderedMoves}건");
        foreach (string s in _unorderedLog.Take(5)) GD.Print("      " + s);
        Check(_unorderedMoves == 0, "A/B 직원은 플레이어 명령으로만 방을 옮긴다");
    }

    // --- Test C/D : 재배치하면 준비가 취소된다 ------------------------------

    private void CheckCancel(OpsProfileDef ops)
    {
        StartShift("cat");
        Order("cat", "power_room");
        float length = DayObjectives.MaxShiftSeconds;
        for (float t = 0f; t < length && _sim.Saboteur.Phase != SaboteurPhase.Preparing; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
        bool prepared = _sim.Saboteur.Phase == SaboteurPhase.Preparing;
        GD.Print($"\n[C/D] 준비 시작 {(prepared ? "O" : "X")} (진행 {_sim.Saboteur.PrepareRatio:P0}) @ " +
                 $"{GameState.Instance.DayTimeSeconds:0}초");
        Check(prepared, "C 결번자가 배치된 자리에서 준비 상태에 들어간다");
        if (!prepared) return;

        Order("cat", "storage_room");
        Advance(4f);
        GD.Print($"      재배치 후 phase={_sim.Saboteur.Phase} · 취소 {_sim.Saboteur.CancelCount}회");
        Check(_sim.Saboteur.Phase != SaboteurPhase.Preparing && _sim.Saboteur.CancelCount > 0,
            "C 재배치하면 준비가 취소된다");

        Advance(20f);
        Check(!_sim.Saboteur.HasActed && _sim.Saboteur.Phase != SaboteurPhase.Done,
            "D 대상이 아닌 새 자리에서는 방해공작이 일어나지 않는다");
    }

    // --- Test E~I : 전조 --------------------------------------------------

    private void CheckPrecursors(List<Run> runs)
    {
        var acted = runs.Where(r => r.SabotageAt >= 0f).ToList();
        GD.Print($"\n[E~I] 방해공작 {acted.Count}회 · 전조 평균 {Avg(acted, r => r.Precursors):0.0}개 · " +
                 $"단서 경로 평균 {Avg(acted, r => r.CluePaths):0.0}개");
        Check(acted.Count >= runs.Count * 3 / 4, "방해공작이 대부분의 근무에서 발생한다");
        Check(acted.All(r => r.Precursors >= 1), "E 방해공작 전에 전조가 최소 1개 생긴다");
        Check(acted.All(r => r.PrepareAt >= 0f && r.PrepareAt < r.SabotageAt),
            "F 전조는 방 이동이 아니라 그 자리에서의 준비 과정이다");
        Check(acted.Count(r => r.CluePaths >= 2) >= acted.Count / 2,
            "I 절반 이상의 근무에서 단서 경로가 2개 이상 남는다");
    }

    // 정상 직원도 같은 모양의 행동을 한다 — 전조 하나로 범인이 정해지면 안 된다.
    private void CheckNormalBehaviour()
    {
        StartShift("crow");
        Advance(DayObjectives.MaxShiftSeconds);
        var odd = EventLog.Instance.GetAllEntries()
            .Where(e => e.Day == 1 && e.EventType == LogEventType.Neglect
                        && !string.IsNullOrEmpty(e.ActorEmployeeId))
            .Select(e => e.ActorEmployeeId).Distinct().ToList();
        GD.Print($"\n[G/H] 이상 행동을 보인 직원 {odd.Count}명 ({string.Join(", ", odd.Select(Code))})");
        Check(odd.Count(id => id != "crow") >= 1, "G 정상 직원도 비슷한 행동을 한다");

        var rows = FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), 1);
        int noise = rows.Count(r => r.Text.Contains("확인했다") || r.Text.Contains("들여다")
                                    || r.Text.Contains("머물렀다"));
        GD.Print($"      시설 로그 {rows.Count}줄 · 미세 행동 노출 {noise}줄");
        Check(noise == 0, "H 전조가 시설 로그를 도배하지 않는다(범인이 노출되지 않는다)");
    }

    // --- 전략별 결과 -------------------------------------------------------

    private void CheckStrategies()
    {
        GD.Print("\n전략별 DAY1 결과");
        var plans = new (string Label, (string Room, int N)[] Crew, bool Respond)[]
        {
            ("안정형   코어1 발전2", new[] { ("core_room", 1), ("power_room", 2), ("maintenance_room", 1), ("guard_room", 1), ("storage_room", 1) }, true),
            ("코어집중 코어3", new[] { ("core_room", 3), ("power_room", 1), ("maintenance_room", 1), ("guard_room", 1) }, true),
            ("경비강화 경비2", new[] { ("core_room", 1), ("power_room", 2), ("maintenance_room", 1), ("guard_room", 2) }, true),
            ("무대응   안정형 배치", new[] { ("core_room", 1), ("power_room", 2), ("maintenance_room", 1), ("guard_room", 1), ("storage_room", 1) }, false),
        };
        float best = 0f;
        var gains = new List<float>();
        foreach (var (label, crew, respond) in plans)
        {
            float sum = 0f;
            const int n = 3;
            for (int i = 0; i < n; i++) sum += RunStrategy(crew, respond);
            float avg = sum / n;
            gains.Add(avg);
            best = Mathf.Max(best, avg);
            GD.Print($"  {label,-18} 코어 {avg:0.0}%  (자재 {GameState.Instance.Materials} · " +
                     $"고장 {IncidentTracker.OpenedCount}건)");
        }
        Check(gains.Count(g => g >= best * 0.7f) >= 2, "유효 전략이 2개 이상이다");
        Check(gains[0] > gains[3], "경고에 대응하는 쪽이 방치보다 낫다");
    }

    private float RunStrategy((string Room, int N)[] crew, bool respond)
    {
        StartShift("fox", crew);
        float start = GameState.Instance.CoreProgress;
        float length = DayObjectives.MaxShiftSeconds;
        string borrowed = "", origin = "";
        for (float t = 0f; t < length; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            if (!respond) continue;

            var w = _sim.Warnings.Active.FirstOrDefault();
            if (w == null)
            {
                if (borrowed.Length > 0) { Order(borrowed, origin); borrowed = ""; origin = ""; }
                continue;
            }
            if (borrowed.Length > 0 || _sim.OnDutyCount(w.RoomId) >= w.RequiredStaff) continue;
            string donor = _sim.GetRoomIds().Where(r => r != w.RoomId)
                .OrderByDescending(_sim.OnDutyCount).FirstOrDefault(r => _sim.OnDutyCount(r) >= 2);
            if (donor == null) continue;
            borrowed = _sim.GetRoomState(donor).OccupantEmployeeIds.FirstOrDefault() ?? "";
            origin = donor;
            if (borrowed.Length > 0) Order(borrowed, w.RoomId);
        }
        return GameState.Instance.CoreProgress - start;
    }

    // --- 템포 -------------------------------------------------------------

    private void CheckTempo(List<Run> runs, OpsProfileDef ops, float length)
    {
        float firstAvg = Avg(runs, r => r.FirstWarningAt);
        var acted = runs.Where(r => r.SabotageAt >= 0f).ToList();
        float sabAvg = Avg(acted, r => r.SabotageAt);
        float worstGap = runs.Max(r => r.LongestGap);

        Check(firstAvg >= 15f && firstAvg <= 32f, $"첫 경고가 18~28초 언저리다 (평균 {firstAvg:0.0}초)");
        Check(acted.All(r => r.SabotageAt <= ops.SabotageWindowEndSeconds + 20f),
            "방해공작이 근무 막판까지 밀리지 않는다");
        Check(sabAvg >= 70f && sabAvg <= 102f, $"방해공작 평균 시각이 80~95초 언저리다 ({sabAvg:0.0}초)");
        Check(worstGap <= 30f, $"30초 넘게 아무 일도 없는 구간이 없다 (최장 {worstGap:0.0}초)");
        Check(runs.Average(r => r.Decisions) >= 4f,
            $"근무당 의미 있는 판단이 4회 이상이다 ({runs.Average(r => r.Decisions):0.0}회)");
    }

    // --- 도우미 -----------------------------------------------------------

    private static string Fmt(float v) => v < 0f ? "-" : $"{v:0}초";

    private static float Avg(List<Run> runs, System.Func<Run, float> sel)
    {
        var vals = runs.Select(sel).Where(v => v >= 0f).ToList();
        return vals.Count == 0 ? -1f : vals.Average();
    }

    private void Advance(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private void StartShift(string saboteurId, (string Room, int N)[] plan = null)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
        _orderedMoves.Clear();

        plan ??= new (string, int)[]
        {
            ("core_room", 2), ("power_room", 1), ("maintenance_room", 1),
            ("guard_room", 1), ("storage_room", 1),
        };
        var roster = _sim.GetActiveEmployeeIds().ToList();
        int i = 0;
        foreach (var (room, n) in plan)
            for (int k = 0; k < n && i < roster.Count; k++, i++) Order(roster[i], room);

        GameState.Instance.SetSaboteur(saboteurId);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void Check(bool ok, string label)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }

    private string Code(string id) => _sim.GetEmployeeDef(id)?.Codename ?? id;
}
