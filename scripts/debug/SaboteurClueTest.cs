using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.Debug;

// DAY1 방해공작 전조 · 단서 구조 자동 검증.
//
//   godot --headless --path . scenes/debug/SaboteurClueTest.tscn --quit-after 900
//
// 실제 FacilitySimulation 을 6번(직원 한 명씩 결번자로) 돌려서
// "방해공작이 근무 중 실제로 움직이던 직원의 행동으로 보이는가"를 확인한다.
public partial class SaboteurClueTest : Node
{
    private const float Step = 1f / 30f;

    private FacilitySimulation _sim;
    private int _pass, _fail;

    // 한 번의 근무에서 뽑아낸 사실.
    private sealed class Run
    {
        public string SaboteurId = "";
        public LogEntry Sabotage;
        public float ArrivedAt = -1f;
        public string TargetRoom = "";
        public string RoomAtSabotage = "";
        public SaboteurPhase EndPhase;
        public string AssignedRoom = "";
        public int CluePaths;
        public List<string> ClueNames = new();
        public int OutsiderMoves;      // 결번자가 아닌 직원의 근무 중 이동(오해 소지)
        public bool OutsiderNearScene; // 그중 사건 현장/인접에 간 사람이 있었나
    }

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ DAY1 방해공작 전조 / 단서 구조 검증 ################");
        var ops = OpsProfile.For(1);
        if (ops == null) { GD.PrintErr("data/ops/day1.tres 를 읽지 못했습니다."); return; }
        GD.Print($"대상 시설 {ops.SabotageTargetRooms.Count}곳 · 시작 {ops.SaboteurStartSeconds:0}초 · " +
                 $"체류 조건 {ops.SabotageDwellSeconds:0}초 · 반응 이동 최대 {ops.MaxReactionMovesPerDay}회");

        var runs = new List<Run>();
        foreach (string id in new[] { "owl", "cat", "jellyfish", "rabbit", "crow", "fox" })
            runs.Add(Simulate(id, ops));

        Report(runs, ops);
        DumpLastShift();
        CheckAbcd(runs, ops);
        CheckE(runs);
        CheckF(runs);
        CheckG(runs);
        CheckH();
        CheckI();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // --- 한 번의 근무 ------------------------------------------------------

    private Run Simulate(string saboteurId, OpsProfileDef ops)
    {
        StartShift(saboteurId);
        float length = DayObjectives.MaxShiftSeconds;
        string borrowed = "", origin = "";

        for (float t = 0f; t < length; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);

            // 관리자 대역 — 경고가 뜨면 가까운 방에서 한 명을 빌려 보낸다.
            var w = _sim.Warnings.Active.FirstOrDefault();
            if (w == null)
            {
                if (borrowed.Length > 0) { _sim.AssignToRoom(borrowed, origin); borrowed = ""; origin = ""; }
                continue;
            }
            if (borrowed.Length > 0 || _sim.OnDutyCount(w.RoomId) >= w.RequiredStaff) continue;
            string donor = _sim.GetRoomIds().Where(r => r != w.RoomId)
                .OrderByDescending(_sim.OnDutyCount).FirstOrDefault(r => _sim.OnDutyCount(r) >= 2);
            if (donor == null) continue;
            borrowed = _sim.GetRoomState(donor).OccupantEmployeeIds.FirstOrDefault(id => id != saboteurId) ?? "";
            origin = donor;
            if (borrowed.Length > 0) _sim.AssignToRoom(borrowed, w.RoomId);
        }

        return Collect(saboteurId);
    }

    private Run Collect(string saboteurId)
    {
        // 근무마다 로그가 통째로 바뀌므로 위치 타임라인 캐시를 반드시 버린다.
        DialogueContextBuilder.Invalidate();
        var run = new Run { SaboteurId = saboteurId };
        var all = EventLog.Instance.GetAllEntries().Where(e => e.Day == 1).ToList();
        run.Sabotage = all.FirstOrDefault(e => e.EventType == LogEventType.Sabotage);
        run.EndPhase = _sim.Saboteur.Phase;
        run.TargetRoom = string.IsNullOrEmpty(_sim.Saboteur.ActedRoomId)
            ? _sim.Saboteur.TargetRoomId : _sim.Saboteur.ActedRoomId;
        if (run.Sabotage == null) return run;

        float t = run.Sabotage.GameTimeSeconds;
        string room = run.Sabotage.RoomId;
        run.RoomAtSabotage = DialogueContextBuilder.RoomAt(saboteurId, 1, t);
        run.AssignedRoom = _sim.GetEmployeeState(saboteurId)?.AssignedRoomId ?? "";

        // 도착 시각 — 마지막으로 그 방에 "들어온"(통과가 아닌) 기록.
        var arrival = all.LastOrDefault(e => e.EventType == LogEventType.RoomEnter
                                             && e.ActorEmployeeId == saboteurId
                                             && e.RoomId == room && !e.PassingThrough
                                             && e.GameTimeSeconds <= t);
        run.ArrivedAt = arrival?.GameTimeSeconds ?? -1f;

        // ── 단서 경로 ──
        var rows = FacilityLogFormatter.Build(all, 1);
        if (rows.Any(r => r.RelatedEmployeeId == saboteurId && r.ToRoomId == room && r.Timestamp <= t))
        { run.CluePaths++; run.ClueNames.Add("시설 로그 이동 기록"); }

        var witnesses = _sim.GetActiveEmployeeIds()
            .Where(id => id != saboteurId
                         && DialogueContextBuilder.KnowledgeOf(id, run.Sabotage) != KnowledgeLevel.None)
            .ToList();
        if (witnesses.Count > 0)
        {
            run.CluePaths++;
            run.ClueNames.Add("증언 " + string.Join("/", witnesses.Select(Codename)));
        }
        if (PlayerKnownEvidence.HasRoomRecord(room))
        { run.CluePaths++; run.ClueNames.Add("CCTV·순찰 기록"); }

        // ── 정상 직원의 오해 소지 ──
        foreach (var r in rows.Where(r => !string.IsNullOrEmpty(r.ToRoomId)
                                          && r.RelatedEmployeeId != saboteurId))
        {
            run.OutsiderMoves++;
            if (r.ToRoomId == room || DialogueContextBuilder.IsAdjacent(r.ToRoomId, room))
                run.OutsiderNearScene = true;
        }
        return run;
    }

    // --- 보고 -------------------------------------------------------------

    private void Report(List<Run> runs, OpsProfileDef ops)
    {
        GD.Print("\n결번자      방해공작      도착 → 실행     단서 경로");
        foreach (var r in runs)
        {
            if (r.Sabotage == null)
            {
                GD.Print($"  {Codename(r.SaboteurId),-6}   발생 안 함  ({r.EndPhase} · 대상 {RoomName(r.TargetRoom)})");
                continue;
            }
            float t = r.Sabotage.GameTimeSeconds;
            GD.Print($"  {Codename(r.SaboteurId),-6}   {SaboteurPlan.Clock(t)} {RoomName(r.Sabotage.RoomId),-5}" +
                     $"   체류 {(r.ArrivedAt < 0f ? -1f : t - r.ArrivedAt):0}초" +
                     $"   {r.CluePaths}개 [{string.Join(", ", r.ClueNames)}]" +
                     $"   타 직원 이동 {r.OutsiderMoves}건");
        }
    }

    // 마지막 근무에서 플레이어가 실제로 보게 되는 시설 로그와, 개발용 사후 요약.
    private void DumpLastShift()
    {
        GD.Print("\n──────── 마지막 근무의 시설 로그(플레이어가 보는 화면) ────────");
        foreach (var row in FacilityLogFormatter.Build(EventLog.Instance.GetAllEntries(), 1))
            GD.Print($"  {SaboteurPlan.Clock(row.Timestamp)}  {row.Text}   [{row.Severity}]");
        GD.Print(_sim.Saboteur.DebugSummary(_sim));
    }

    // --- Test A~D ----------------------------------------------------------

    private void CheckAbcd(List<Run> runs, OpsProfileDef ops)
    {
        var acted = runs.Where(r => r.Sabotage != null).ToList();
        GD.Print($"\n[A~D] 6회 중 {acted.Count}회 방해공작 발생");
        Check("방해공작이 대부분의 근무에서 실제로 발생한다", acted.Count >= 5);

        foreach (var r in acted.Where(r => r.RoomAtSabotage != r.Sabotage.RoomId))
            GD.Print($"    [A 불일치] {Codename(r.SaboteurId)} 기록상 위치 " +
                     $"{RoomName(r.RoomAtSabotage)} ≠ 사건 장소 {RoomName(r.Sabotage.RoomId)}");
        Check("A 결번자가 실제로 그 방에 있을 때만 그 방에서 발생한다",
            acted.All(r => r.RoomAtSabotage == r.Sabotage.RoomId));
        Check("A 대상은 중요 시설(코어/발전/정비)로 제한된다",
            acted.All(r => ops.SabotageTargetRooms.Contains(r.Sabotage.RoomId)));
        Check("A 결번자의 배치실이 아닌 곳에서 벌어진다",
            acted.All(r => r.AssignedRoom != r.Sabotage.RoomId));

        Check("B 근무 시작 직후에는 발생하지 않는다",
            acted.All(r => r.Sabotage.GameTimeSeconds >= ops.SaboteurStartSeconds));

        Check("C 방해공작 전에 그 방으로 들어온 기록(이동 전조)이 있다",
            acted.All(r => r.ArrivedAt >= 0f && r.ArrivedAt < r.Sabotage.GameTimeSeconds));
        Check("C 도착 후 일정 시간 머문 뒤에 손을 댄다(체류 전조)",
            acted.All(r => r.Sabotage.GameTimeSeconds - r.ArrivedAt >= ops.SabotageDwellSeconds - 1f));

        Check("D 사건마다 추적 가능한 단서 경로가 2개 이상이다",
            acted.All(r => r.CluePaths >= 2));
        Check("D 이동 기록만으로 끝나지 않는다(증언 또는 기록이 함께 남는다)",
            acted.All(r => r.ClueNames.Count(n => n != "시설 로그 이동 기록") >= 1));
    }

    // --- Test E ------------------------------------------------------------

    private void CheckE(List<Run> runs)
    {
        int withOutsider = runs.Count(r => r.OutsiderMoves > 0);
        int nearScene = runs.Count(r => r.OutsiderNearScene);
        GD.Print($"\n[E] 정상 직원이 근무 중 자리를 옮긴 근무 {withOutsider}/6 · " +
                 $"그중 사건 현장 근처까지 간 근무 {nearScene}회");
        Check("E 정상 직원도 정상적인 이유로 자리를 옮긴다", withOutsider >= 4);
        Check("E 로그만 보고 '움직인 사람 = 범인'이 되지 않는다",
            runs.Where(r => r.Sabotage != null).All(r => r.OutsiderMoves >= 1));
    }

    // --- Test F ------------------------------------------------------------

    // 일반 직원은 거짓말하지 않는다 — 위치도, 목격도.
    private void CheckF(List<Run> runs)
    {
        var last = runs.Last();
        GameState.Instance.SetSaboteur(last.SaboteurId);
        DialogueContextBuilder.Invalidate();

        bool locationOk = true, sightingOk = true;
        foreach (string id in _sim.GetActiveEmployeeIds())
        {
            if (id == last.SaboteurId) continue;
            var ctx = DialogueContextBuilder.Build(id, DialogueConversationKind.Interview,
                DialogueQuestions.Where, "", null);
            var plan = DialogueResponsePlanner.Plan(ctx);
            if (plan.Deception != DeceptionMode.None) locationOk = false;
            if (plan.Core == CoreKind.SelfLocation && plan.RoomId != ctx.RoomAtSubject) locationOk = false;

            var ctx3 = DialogueContextBuilder.Build(id, DialogueConversationKind.Interview,
                DialogueQuestions.Suspicious, "", null);
            var plan3 = DialogueResponsePlanner.Plan(ctx3);
            if (plan3.Core == CoreKind.SuspiciousSighting && ctx3.KnownSuspicious == null) sightingOk = false;
        }
        GD.Print($"\n[F] 일반 직원 {_sim.GetActiveEmployeeIds().Count - 1}명 진술 검사");
        Check("F 일반 직원은 실제 위치 그대로 진술한다", locationOk);
        Check("F 일반 직원은 실제로 본 것이 없으면 아무도 지목하지 않는다", sightingOk);
    }

    // --- Test G ------------------------------------------------------------

    // 결번자의 거짓말은 한 사건 안에서 바뀌지 않는다.
    private void CheckG(List<Run> runs)
    {
        var run = runs.LastOrDefault(r => r.Sabotage != null);
        if (run == null) { Check("G 결번자 주장 일관성", false); return; }

        GameState.Instance.SetSaboteur(run.SaboteurId);
        DialogueContextBuilder.Invalidate();

        var rooms = new List<string>();
        string claimKey = "";
        foreach (string q in new[] { DialogueQuestions.Where, DialogueQuestions.Where,
                                     DialogueQuestions.Anomaly, DialogueQuestions.Accuse })
        {
            var ctx = DialogueContextBuilder.Build(run.SaboteurId, DialogueConversationKind.Interview, q, "", null);
            claimKey = ctx.ClaimKey;
            var plan = DialogueResponsePlanner.Plan(ctx);
            if (plan.Core == CoreKind.SelfLocation) rooms.Add(plan.RoomId);
        }
        var claim = DialogueClaimState.Get(run.SaboteurId, 1, claimKey);
        GD.Print($"\n[G] 결번자 {Codename(run.SaboteurId)} · 전략 {claim.Mode} · " +
                 $"주장 위치 {RoomName(claim.ClaimedRoomId)} (진실 {claim.ClaimTruthful})");
        Check("G 같은 사건에 대한 주장 위치가 바뀌지 않는다", rooms.Distinct().Count() <= 1);
        Check("G 답변이 DialogueClaimState 의 주장과 일치한다",
            rooms.Count == 0 || rooms[0] == claim.ClaimedRoomId);
    }

    // --- Test H ------------------------------------------------------------

    // 캐릭터에 따라 행동과 증언이 실제로 다른가.
    private void CheckH()
    {
        var rabbit = EmployeeTraits.Get("rabbit");
        var crow = EmployeeTraits.Get("crow");
        var jelly = EmployeeTraits.Get("jellyfish");
        GD.Print($"\n[H] 이동 성향 토끼 {rabbit.MovementTendency} vs 까마귀 {crow.MovementTendency} · " +
                 $"위험 회피 해파리 {jelly.AvoidsDanger} · 관찰력 까마귀 {crow.ObservationalAwareness} " +
                 $"vs 토끼 {rabbit.ObservationalAwareness}");
        Check("H 토끼는 까마귀보다 훨씬 자주 움직인다",
            rabbit.MovementTendency > crow.MovementTendency && rabbit.Curiosity > crow.Curiosity);
        Check("H 해파리는 위험을 가장 강하게 피한다",
            EmployeeTraits.All.Values.All(t => t.AvoidsDanger <= jelly.AvoidsDanger));
        Check("H 진술 정확도가 캐릭터마다 다르다",
            EmployeeTraits.All.Values.Select(t => t.StatementPrecision).Distinct().Count() >= 3);

        // 같은 자리에서 같은 사건을 겪어도 관찰력에 따라 아는 정도가 다르다.
        EventLog.Instance.ClearAll();
        GameState.Instance.SetSaboteur("");
        DialogueClaimState.ResetAll();
        foreach (var (id, room) in new[] { ("crow", "guard_room"), ("rabbit", "guard_room") })
        {
            var st = _sim.GetEmployeeState(id);
            st.AssignedRoomId = st.CurrentRoomId = room;
        }
        var entry = new LogEntry
        {
            Day = 1, GameTimeSeconds = 60f, EventType = LogEventType.Sabotage,
            ActorEmployeeId = "fox", RoomId = "power_room", Description = "(테스트)",
        };
        EventLog.Instance.Log(entry);
        DialogueContextBuilder.Invalidate();
        var crowKnows = DialogueContextBuilder.KnowledgeOf("crow", entry);
        var rabbitKnows = DialogueContextBuilder.KnowledgeOf("rabbit", entry);
        GD.Print($"    옆 방(경비실)에서 — 까마귀 {crowKnows} / 토끼 {rabbitKnows}");
        Check("H 관찰력이 낮으면 옆 방 사건을 알지 못한다",
            crowKnows == KnowledgeLevel.Indirect && rabbitKnows == KnowledgeLevel.None);
    }

    // --- Test I ------------------------------------------------------------

    // 오늘의 기분과 결번자 여부 사이에 상관이 있으면 안 된다.
    private void CheckI()
    {
        const int Rolls = 900;
        var asSaboteur = new Dictionary<string, Dictionary<string, int>>();
        var asNormal = new Dictionary<string, Dictionary<string, int>>();
        var roster = _sim.GetActiveEmployeeIds().ToList();
        foreach (string id in roster)
        {
            asSaboteur[id] = new Dictionary<string, int>();
            asNormal[id] = new Dictionary<string, int>();
        }

        for (int i = 0; i < Rolls; i++)
        {
            string sab = roster[i % roster.Count];
            GameState.Instance.SetSaboteur(sab);
            _sim.RollDailyMoods();
            foreach (string id in roster)
            {
                string mood = _sim.GetDailyMood(id);
                if (string.IsNullOrEmpty(mood)) continue;
                var bucket = id == sab ? asSaboteur[id] : asNormal[id];
                bucket[mood] = bucket.GetValueOrDefault(mood, 0) + 1;
            }
        }

        float worst = 0f;
        string worstLabel = "";
        foreach (string id in roster)
        {
            int nS = asSaboteur[id].Values.Sum(), nN = asNormal[id].Values.Sum();
            if (nS < 20 || nN < 20) continue;
            foreach (string mood in asSaboteur[id].Keys.Union(asNormal[id].Keys))
            {
                float diff = Mathf.Abs(asSaboteur[id].GetValueOrDefault(mood, 0) / (float)nS
                                       - asNormal[id].GetValueOrDefault(mood, 0) / (float)nN);
                if (diff <= worst) continue;
                worst = diff;
                worstLabel = $"{Codename(id)} '{mood}'";
            }
        }
        GD.Print($"\n[I] 기분 {Rolls}회 재배정 — 결번자일 때와 아닐 때의 최대 빈도 차 " +
                 $"{worst:P0} ({worstLabel})");
        // 표현이 30가지쯤 되므로 "가장 크게 벌어진 한 칸"은 우연만으로도 10% 안팎까지 뜬다.
        // 실제 상관이 있으면 30% 이상 벌어지므로 20% 를 경계로 둔다.
        Check("I 오늘의 기분은 결번자 여부와 상관이 없다", worst < 0.2f);
    }

    // --- 도우미 ------------------------------------------------------------

    private void StartShift(string saboteurId)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();

        var plan = new (string Room, int N)[]
        {
            ("core_room", 2), ("power_room", 1), ("maintenance_room", 1),
            ("guard_room", 1), ("storage_room", 1),
        };
        var roster = _sim.GetActiveEmployeeIds().ToList();
        int i = 0;
        foreach (var (room, n) in plan)
            for (int k = 0; k < n && i < roster.Count; k++, i++) _sim.AssignToRoom(roster[i], room);

        GameState.Instance.SetSaboteur(saboteurId);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void Check(string label, bool ok)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }

    private string Codename(string id) => _sim.GetEmployeeDef(id)?.Codename ?? id;
    private string RoomName(string id) => string.IsNullOrEmpty(id) ? "-" : _sim.RoomDisplayName(id);
}
