using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// DAY1 운영 루프 자동 검증.
//
//   godot --headless --path . scenes/debug/Day1OpsTest.tscn --quit-after 900
//
// 실제 FacilitySimulation 을 180초 동안 돌려서, 배치를 바꾸면 결과가 실제로 달라지는지
// (= "정답 배치"가 하나로 고정되지 않는지) 확인한다. 난수가 섞이므로 시나리오마다
// 여러 번 돌려 평균을 낸다.
public partial class Day1OpsTest : Node
{
    private const int Runs = 12;
    private const float Step = 1f / 30f;

    private FacilitySimulation _sim;

    // 한 번의 근무 결과.
    private struct Result
    {
        // 여러 번 돌린 평균을 담으므로 전부 실수다(정수로 두면 평균이 내림되어
        // "사고 0건"처럼 보이는 착시가 생긴다).
        public float CoreGain;
        public float Materials;
        public float WarnRaised, WarnPrevented, WarnFailed;
        public float Breakdowns;
        // 이상 개체(괴물)로 생긴 사고. 배치로는 막을 수 없고 CCTV 감시로만 막는다 —
        // 이 검사는 배치를 보는 검사라 설비 고장과 따로 센다.
        public float Anomalies;
        public float Sabotages;
        public float PatrolLogs;
    }

    private sealed class Plan
    {
        public string Label = "";
        public Dictionary<string, int> Crew = new();
        public bool Respond = true;
    }

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ DAY1 운영 루프 검증 ################");
        var profile = OpsProfile.For(1);
        if (profile == null) { GD.PrintErr("data/ops/day1.tres 를 읽지 못했습니다."); return; }
        // 이 테스트는 작업실 5개(코어·발전·정비·경비·저장) 체제의 운영 밸런스를 비교한다.
        // 경영 리워크로 환기실 · 의무실 · 스트레스가 DAY1 부터 켜졌지만, 여기서는 비교 조건을 같게 두려고 예전 조건으로 돌린다.
        var cfg0 = Config.Instance.Data;
        cfg0.StressUnlockDay = 99;
        foreach (var rid in new[] { "vent_room", "medical_room" })
            if (_sim.GetRoomDef(rid) is { } rd) rd.UnlockDay = 99;
        GD.Print($"프로필 로드 OK — Day={profile.Day} 목표복구 {profile.TargetCoreGain:0}% " +
                 $"작업실표 {profile.Rooms.Count}개 예약경고 {profile.ScheduledWarnings.Count}개");

        var stable = new Plan
        {
            Label = "A 안정형   (코어1 발전2 정비1 경비1 저장1)",
            Crew = Crew(core: 1, power: 2, maint: 1, guard: 1, storage: 1),
        };
        var powerOne = new Plan
        {
            Label = "B 발전1명  (코어2 발전1 정비1 경비1 저장1)",
            Crew = Crew(core: 2, power: 1, maint: 1, guard: 1, storage: 1),
        };
        var coreHeavy = new Plan
        {
            Label = "C 코어집중 (코어3 발전1 정비1 경비1 저장0)",
            Crew = Crew(core: 3, power: 1, maint: 1, guard: 1, storage: 0),
        };
        var guardHeavy = new Plan
        {
            Label = "F 감시형   (코어1 발전2 정비1 경비2 저장0)",
            Crew = Crew(core: 1, power: 2, maint: 1, guard: 2, storage: 0),
        };
        var ignore = new Plan
        {
            Label = "E 경고무시 (B 배치 · 대응 안 함)",
            Crew = Crew(core: 2, power: 1, maint: 1, guard: 1, storage: 1),
            Respond = false,
        };
        var idle = new Plan
        {
            Label = "G 무조작   (A 배치 · 대응 안 함)",
            Crew = Crew(core: 1, power: 2, maint: 1, guard: 1, storage: 1),
            Respond = false,
        };

        var a = Average(stable);
        var b = Average(powerOne);
        var c = Average(coreHeavy);
        var f = Average(guardHeavy);
        var e = Average(ignore);
        var g = Average(idle);

        GD.Print("\n  배치                                      코어%   자재  경고(발생/막음/실패) 고장 방해 순찰");
        Row(stable.Label, a);
        Row(powerOne.Label, b);
        Row(coreHeavy.Label, c);
        Row(guardHeavy.Label, f);
        Row(ignore.Label, e);
        Row(idle.Label, g);

        GD.Print("\n---------------- 판정 ----------------");
        int pass = 0, fail = 0;
        // 배치가 안정적이면 **설비 고장**은 나지 않는다. 이상 개체 사고는 배치와 무관하므로
        // 여기서 보지 않는다 — 그건 CCTV 를 돌려 봤는가의 문제다(검사에서는 아무도 안 본다).
        Check("A 안정 배치는 사고 없이 끝난다", a.Breakdowns < 0.6f, ref pass, ref fail);
        // 하루 경고 총량 상한(MaxWarningsPerDay)이 생긴 뒤로는 총 발생 수가 상한에 붙는다 —
        // 발전실 1명의 부담은 '더 많이 뜨거나, 더 많이 놓친다' 둘 중 하나로 드러난다.
        Check("B 발전 1명은 경고 부담이 더 크다(발생 또는 실패가 더 많다)",
            b.WarnRaised > a.WarnRaised + 0.2f || b.WarnFailed > a.WarnFailed + 0.2f, ref pass, ref fail);
        Check("B 발전 1명도 근무를 마칠 수 있다", b.CoreGain > 0.1f, ref pass, ref fail);
        Check("C 코어 집중이 A 보다 복구량이 높다", c.CoreGain > a.CoreGain + 1f, ref pass, ref fail);
        Check("C 코어 집중은 그만큼 위험을 떠안는다",
            c.WarnRaised + c.Breakdowns > a.WarnRaised + a.Breakdowns, ref pass, ref fail);
        Check("C 코어 집중은 자재가 먼저 마른다", c.Materials < a.Materials * 0.5f, ref pass, ref fail);
        Check("D 대응하면 경고를 실제로 막는다", b.WarnPrevented > 0.5f, ref pass, ref fail);
        Check("E 대응하지 않으면 고장이 난다", e.Breakdowns > b.Breakdowns, ref pass, ref fail);
        Check("E 고장은 복구량을 실제로 깎는다", e.CoreGain < b.CoreGain, ref pass, ref fail);
        var guardOps = profile.Rooms.FirstOrDefault(r => r.RoomId == "guard_room");
        bool guardCurveOk = guardOps != null
            && OpsProfile.Curve(guardOps.SabotageChance, 1) < OpsProfile.Curve(guardOps.SabotageChance, 0)
            && OpsProfile.Curve(guardOps.SabotageChance, 2) < OpsProfile.Curve(guardOps.SabotageChance, 1);
        Check("F 경비 인원이 늘수록 방해공작 성공률이 내려간다", guardCurveOk, ref pass, ref fail);
        Check("F 경비 2명은 추가 순찰 기록을 남긴다", f.PatrolLogs > a.PatrolLogs + 0.5f, ref pass, ref fail);
        Check("G 아무것도 안 하면 경고/사고를 반드시 겪는다",
            g.WarnRaised + g.Breakdowns >= 1f, ref pass, ref fail);
        Check("G 무조작이 최선의 결과가 아니다", g.CoreGain < c.CoreGain, ref pass, ref fail);

        float best = Mathf.Max(Mathf.Max(a.CoreGain, b.CoreGain), Mathf.Max(c.CoreGain, f.CoreGain));
        int viable = new[] { a.CoreGain, b.CoreGain, c.CoreGain, f.CoreGain }.Count(v => v >= best * 0.7f);
        Check($"유효 전략이 2개 이상이다 (최고 대비 70% 이상 = {viable}개)", viable >= 2, ref pass, ref fail);

        GD.Print($"\n################ 결과: {pass} PASS / {fail} FAIL ################");

        Trace(powerOne);
    }

    // 한 번만 돌리면서 무슨 일이 일어났는지 시간순으로 찍어 본다(밸런스 조정용).
    private void Trace(Plan plan)
    {
        GD.Print($"\n---------------- \ucd94\uc801: {plan.Label} ----------------");
        Setup(plan);
        float length = Config.Instance.Data.DayLengthSeconds;
        string borrowed = "", origin = "";
        int shown = 0;
        for (float t = 0f; t < length; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            if (plan.Respond) Respond(ref borrowed, ref origin);

            var all = EventLog.Instance.GetAllEntries();
            for (; shown < all.Count; shown++)
            {
                var e = all[shown];
                if (e.EventType is LogEventType.RoomEnter or LogEventType.RoomExit
                    or LogEventType.TaskStart or LogEventType.TaskEnd) continue;
                if (e.Description.Contains("\uc790\uc7ac +") || e.Description.Contains("\ucf54\uc5b4 +")) continue;
                GD.Print($"   {e.GameTimeSeconds,6:0.0}s  {e.Description}");
            }
        }
        GD.Print($"   ==> \ucf54\uc5b4 {GameState.Instance.CoreProgress:0.0}% \uc790\uc7ac {GameState.Instance.Materials} " +
                 $"\uacbd\uace0 {_sim.Warnings.Raised}/{_sim.Warnings.Prevented}/{_sim.Warnings.Failed}");
    }

    private static Dictionary<string, int> Crew(int core, int power, int maint, int guard, int storage) => new()
    {
        ["core_room"] = core,
        ["power_room"] = power,
        ["maintenance_room"] = maint,
        ["guard_room"] = guard,
        ["storage_room"] = storage,
    };

    private static void Row(string label, Result r) =>
        GD.Print($"  {label,-42} {r.CoreGain,5:0.0}  {r.Materials,5:0.0}   " +
                 $"{r.WarnRaised,4:0.0}/{r.WarnPrevented,4:0.0}/{r.WarnFailed,4:0.0}   " +
                 $"{r.Breakdowns,4:0.0} {r.Sabotages,4:0.0} {r.PatrolLogs,4:0.0}");

    private static void Check(string label, bool ok, ref int pass, ref int fail)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) pass++; else fail++;
    }

    // 이번 근무에 이상 개체로 생긴 사고 수.
    private static int AnomalyCount() => EventLog.Instance?.GetAllEntries()
        .Count(e => e.EventType == LogEventType.AnomalyIncident) ?? 0;

    // --- 한 시나리오를 여러 번 돌려 평균 ------------------------------------

    private Result Average(Plan plan)
    {
        var sum = new Result();
        for (int i = 0; i < Runs; i++)
        {
            var r = RunOnce(plan);
            sum.CoreGain += r.CoreGain;
            sum.Materials += r.Materials;
            sum.WarnRaised += r.WarnRaised;
            sum.WarnPrevented += r.WarnPrevented;
            sum.WarnFailed += r.WarnFailed;
            sum.Breakdowns += r.Breakdowns;
            sum.Anomalies += r.Anomalies;
            sum.Sabotages += r.Sabotages;
            sum.PatrolLogs += r.PatrolLogs;
        }
        return new Result
        {
            CoreGain = sum.CoreGain / Runs,
            Materials = sum.Materials / Runs,
            WarnRaised = sum.WarnRaised / Runs,
            WarnPrevented = sum.WarnPrevented / Runs,
            WarnFailed = sum.WarnFailed / Runs,
            Breakdowns = sum.Breakdowns / Runs,
            Anomalies = sum.Anomalies / Runs,
            Sabotages = sum.Sabotages / Runs,
            PatrolLogs = sum.PatrolLogs / Runs,
        };
    }

    private Result RunOnce(Plan plan)
    {
        Setup(plan);

        float length = Config.Instance.Data.DayLengthSeconds;
        string borrowed = "", origin = "";

        for (float t = 0f; t < length; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            if (plan.Respond) Respond(ref borrowed, ref origin);
        }

        var entries = EventLog.Instance.GetAllEntries();
        return new Result
        {
            CoreGain = GameState.Instance.CoreProgress,
            Materials = GameState.Instance.Materials,
            WarnRaised = _sim.Warnings.Raised,
            WarnPrevented = _sim.Warnings.Prevented,
            WarnFailed = _sim.Warnings.Failed,
            Breakdowns = IncidentTracker.OpenedCount - AnomalyCount(),
            Anomalies = AnomalyCount(),
            Sabotages = entries.Count(x => x.EventType == LogEventType.Sabotage),
            PatrolLogs = entries.Count(x => x.Description.Contains("경비 순찰 기록")),
        };
    }

    private void Setup(Plan plan)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();

        // 인원수만 맞춰 배치한다(누가 어느 방인지는 이 테스트의 관심사가 아니다).
        var roster = _sim.GetActiveEmployeeIds().ToList();
        int idx = 0;
        foreach (var (room, n) in plan.Crew)
            for (int i = 0; i < n && idx < roster.Count; i++, idx++)
                _sim.AssignToRoom(roster[idx], room);

        GameState.Instance.AssignRandomSaboteur(roster);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    // 플레이어 대역: 경고가 뜨면 사람이 가장 많은 방에서 한 명을 빼서 보내고,
    // 상황이 끝나면 원래 자리로 되돌린다. 고장이 나면 수리 인원을 맞춰 준다.
    private void Respond(ref string borrowed, ref string origin)
    {
        string need = NeedyRoom(out int required);

        if (string.IsNullOrEmpty(need))
        {
            if (string.IsNullOrEmpty(borrowed)) return;
            _sim.AssignToRoom(borrowed, origin);       // 원위치
            borrowed = "";
            origin = "";
            return;
        }

        if (!string.IsNullOrEmpty(borrowed)) return;   // 이미 한 명 보냈다
        if (_sim.OnDutyCount(need) >= required) return;

        string donor = DonorRoom(need);
        if (string.IsNullOrEmpty(donor)) return;
        string who = _sim.GetRoomState(donor)?.OccupantEmployeeIds.FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(who)) return;

        borrowed = who;
        origin = donor;
        _sim.AssignToRoom(who, need);
    }

    // 지금 사람이 더 필요한 방(경고 > 수리).
    private string NeedyRoom(out int required)
    {
        var w = _sim.Warnings.Active.FirstOrDefault();
        if (w != null) { required = w.RequiredStaff; return w.RoomId; }

        foreach (string roomId in _sim.GetRoomIds())
        {
            if (!_sim.HasRepairPending(roomId)) continue;
            required = RoomStaffing.RepairMinWorkers(roomId, _sim.GetRoomDef(roomId));
            return roomId;
        }
        required = 0;
        return "";
    }

    // 사람을 뺄 방 — 지금 가장 여유 있는 곳(= 인원이 가장 많은 다른 방).
    private string DonorRoom(string exclude)
    {
        string best = "";
        int bestCount = 1;   // 1명뿐인 방에서는 빼지 않는다
        foreach (string roomId in _sim.GetRoomIds())
        {
            if (roomId == exclude) continue;
            int n = _sim.OnDutyCount(roomId);
            if (n <= bestCount) continue;
            bestCount = n;
            best = roomId;
        }
        return best;
    }
}
