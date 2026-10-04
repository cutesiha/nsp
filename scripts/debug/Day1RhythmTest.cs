using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// DAY1 의 밀도 검사 — 하루 동안 플레이어가 볼 것이 끊기지 않는가.
//
//   godot --headless --path . res://scenes/debug/Day1RhythmTest.tscn --quit-after 900000
//
// 새 시스템을 세지 않는다. 이미 있는 경로만 센다 —
//   시설 경고 · 괴물 · 작은 교란(MinorTamper) · 실제 방해공작 · 정상 직원의 거짓 단서.
//
// 가장 중요한 수치는 맨 아래의 **최대 공백**이다. 아무 일도 없는 구간이 길면
// DAY1 은 "경고를 기다리는 2분" 으로 되돌아간다.
public partial class Day1RhythmTest : Node
{
    private const float Step = 1f / 30f;
    private const int Runs = 100;

    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private sealed class Run
    {
        public float FirstWarning = -1f, GhostAt = -1f, TamperAt = -1f, SabotageAt = -1f;
        public bool CrossRoom, HardSabotage, TamperBrokeSomething, SaboteurWalked;
        // 교란 기록이 범인을 가리키는가(이름 · 손댄 방이 로그에 적혔는가).
        public bool TamperLogNamesActor;
        // 설비 앞에 선 사람으로 결번 개체가 **유일하게** 특정되는가.
        public bool PanelTouchIsUnique;
        public int FalseLeads, Warnings;
        public float MaxGap;
        public readonly HashSet<string> FalseLeadActors = new();
    }

    private void RunAll()
    {
        GD.Print("\n\n################ DAY1 리듬 — 100회 시뮬레이션 ################");
        float day = Config.Instance.Data.DayLengthSeconds;
        var ops = OpsProfile.For(1);
        GD.Print($"   하루 {day:0}초 · 작은 교란 {ops?.TamperAttemptsPerDay}회 보장"
                 + $"({ops?.TamperWindowStartSeconds:0}~{ops?.TamperWindowEndSeconds:0}초, 다른 방 {ops?.TamperCrossRoomChance:0.00})"
                 + $" · 실제 방해공작 확률 {ops?.SabotageChancePerDay:0.00}");

        var runs = new List<Run>();
        for (int i = 0; i < Runs; i++) runs.Add(Simulate(day));

        Report(runs);
        Verify(runs);
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private Run Simulate(float dayLength)
    {
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();
        GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
        GameState.Instance.SetPhase(GamePhase.Live);

        // 평범한 배치 — 작업실에 고르게 흩는다.
        var rooms = new[] { "core_room", "core_room", "power_room", "power_room", "maintenance_room", "guard_room" };
        var ids = _sim.GetActiveEmployeeIds();
        for (int i = 0; i < ids.Count; i++) _sim.AssignToRoom(ids[i], rooms[i % rooms.Length]);

        string sabId = GameState.Instance.SaboteurEmployeeId;
        string sabStartRoom = "";
        var run = new Run();
        float lastEvent = 0f;
        int seenWarnings = 0, seenLeads = 0;
        int logCount = EventLog.Instance?.GetAllEntries().Count ?? 0;

        for (float t = 0f; t < dayLength; t += Step)
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);

            var sab = _sim.GetEmployeeState(sabId);
            if (string.IsNullOrEmpty(sabStartRoom) && sab != null && !sab.IsMoving)
                sabStartRoom = sab.CurrentRoomId;

            bool any = false;

            if (_sim.Warnings.Raised > seenWarnings)
            {
                seenWarnings = _sim.Warnings.Raised;
                if (run.FirstWarning < 0f) run.FirstWarning = t;
                any = true;
            }
            if (run.GhostAt < 0f && _sim.Ghost.Active) { run.GhostAt = t; any = true; }
            if (run.TamperAt < 0f && _sim.Saboteur.HasTampered)
            {
                run.TamperAt = t;
                run.CrossRoom = _sim.Saboteur.TamperOriginRoomId != _sim.Saboteur.TamperAffectedRoomId;
                // 교란이 실제 고장을 만들면 안 된다 — 그건 방해공작의 몫이다.
                if (_sim.HasRepairPending(_sim.Saboteur.TamperAffectedRoomId)) run.TamperBrokeSomething = true;
                any = true;
            }
            if (run.SabotageAt < 0f && _sim.Saboteur.HasActed)
            {
                run.SabotageAt = t;
                run.HardSabotage = true;
                any = true;
            }
            if (_sim.Behavior.FalseLeads.Count > seenLeads)
            {
                seenLeads = _sim.Behavior.FalseLeads.Count;
                any = true;
            }
            // 사고 · 안정화 · 전화 등 화면에 뜨는 기록이 늘어난 순간도 "볼 것이 있었다" 로 센다.
            int nowCount = EventLog.Instance?.GetAllEntries().Count ?? 0;
            if (nowCount > logCount) { logCount = nowCount; any = true; }

            // 결번 개체는 교란을 위해 스스로 방을 옮기지 않는다.
            if (sab != null && !string.IsNullOrEmpty(sabStartRoom)
                && sab.CurrentRoomId != sabStartRoom && !sab.IsMoving) run.SaboteurWalked = true;

            if (any) { run.MaxGap = Mathf.Max(run.MaxGap, t - lastEvent); lastEvent = t; }
        }

        run.MaxGap = Mathf.Max(run.MaxGap, dayLength - lastEvent);

        // H — 기록만 보고 범인을 고를 수 있으면 추리가 끝난다. 두 가지를 본다.
        //   ① 계통 이상 기록에 사람 이름도, 손댄 방도 들어가지 않는다.
        //   ② 설비 앞에 선 사람이 결번 개체 하나뿐이면 안 된다(정상 직원도 같은 흔적을 남긴다).
        var log = EventLog.Instance?.GetAllEntries() ?? new List<LogEntry>();
        string sabName = _sim.GetEmployeeDef(sabId)?.Codename ?? sabId;
        string originName = _sim.RoomDisplayName(_sim.Saboteur.TamperOriginRoomId);
        foreach (var e in log.Where(x => x.EventType == LogEventType.SignalAnomaly))
            if (!string.IsNullOrEmpty(e.ActorEmployeeId)
                || (e.Description ?? "").Contains(sabName)
                || (run.CrossRoom && (e.Description ?? "").Contains(originName)))
                run.TamperLogNamesActor = true;

        // 설비 앞에 섰다고 기록된 사람들(결번 개체의 교란 + 정상 직원의 거짓 단서).
        // 둘은 **같은 이유 표**(EmployeeBehaviorSystem.Reason)를 쓰므로 문장으로 갈라지지 않는다.
        var touched = new HashSet<string>(log
            .Where(x => x.EventType == LogEventType.Neglect && !string.IsNullOrEmpty(x.ActorEmployeeId)
                        && (x.Description ?? "").Contains(EmployeeBehaviorSystem.Reason(x.ActorEmployeeId)))
            .Select(x => x.ActorEmployeeId));
        run.PanelTouchIsUnique = touched.Count == 1 && touched.Contains(sabId);

        run.Warnings = seenWarnings;
        run.FalseLeads = seenLeads;
        foreach (string s in _sim.Behavior.FalseLeads)
        {
            int sp = s.IndexOf(' ');
            run.FalseLeadActors.Add(sp > 0 ? s[..sp] : s);
        }
        return run;
    }

    private void Report(List<Run> runs)
    {
        GD.Print("\n===== 100회 평균 =====");
        GD.Print($"   첫 경고        {Avg(runs, r => r.FirstWarning):0.0}초");
        GD.Print($"   괴물 등장      {Avg(runs, r => r.GhostAt):0.0}초  (등장률 {Rate(runs, r => r.GhostAt >= 0f):0}%)");
        GD.Print($"   작은 교란      {Avg(runs, r => r.TamperAt):0.0}초  (발생률 {Rate(runs, r => r.TamperAt >= 0f):0}%"
                 + $" · 다른 방에 증상 {Rate(runs.Where(r => r.TamperAt >= 0f).ToList(), r => r.CrossRoom):0}%)");
        GD.Print($"   실제 방해공작  {Rate(runs, r => r.HardSabotage):0}%  (평균 {Avg(runs, r => r.SabotageAt):0.0}초)");
        GD.Print($"   거짓 단서      {runs.Average(r => r.FalseLeads):0.00}회");
        GD.Print($"   시설 경고      {runs.Average(r => r.Warnings):0.00}회");
        GD.Print($"   최대 공백      평균 {runs.Average(r => r.MaxGap):0.0}초 · 최악 {runs.Max(r => r.MaxGap):0.0}초"
                 + $" · 20초 넘는 판 {Rate(runs, r => r.MaxGap > 20f):0}%");
    }

    private void Verify(List<Run> runs)
    {
        GD.Print("\n===== 검증 =====");
        Check(runs.All(r => r.TamperAt >= 0f),
            $"A 작은 교란이 모든 판에서 1회 일어난다 ({Rate(runs, r => r.TamperAt >= 0f):0}%)");

        float hard = Rate(runs, r => r.HardSabotage);
        Check(hard is >= 15f and <= 55f, $"B 실제 방해공작 발생률이 설정 범위 안이다 ({hard:0}%)");

        var noHard = runs.Where(r => !r.HardSabotage).ToList();
        Check(noHard.Count > 0 && noHard.All(r => r.TamperAt >= 0f),
            $"C 방해공작이 없던 판({noHard.Count}회)에서도 교란은 남는다");

        Check(runs.Any(r => r.CrossRoom),
            $"D 증상이 난 방과 손댄 방이 다른 판이 있다 ({Rate(runs, r => r.CrossRoom):0}%)");

        Check(runs.All(r => !r.TamperBrokeSomething), "E 교란이 실제 고장을 만들지 않는다");
        Check(runs.All(r => !r.SaboteurWalked), "F 결번 개체가 스스로 방을 옮기지 않는다");

        // 목격 판정에 확률이 걸려 있어 모든 판을 2회로 못 박을 수는 없다(그러려면 그 확률을 없애야 한다).
        // 대신 "2회 이상인 판이 대부분" 을 본다.
        float twoPlus = Rate(runs, r => r.FalseLeads >= 2);
        Check(twoPlus >= 85f,
            $"G 정상 직원 거짓 단서가 2회 이상인 판이 대부분이다 ({twoPlus:0}% · 평균 {runs.Average(r => r.FalseLeads):0.00} · 최소 {runs.Min(r => r.FalseLeads)})");
        Check(runs.Where(r => r.FalseLeads >= 2).All(r => r.FalseLeadActors.Count >= 2),
            "G-2 거짓 단서가 2회 이상인 판에서 같은 직원에게 몰리지 않는다");

        Check(runs.All(r => !r.TamperLogNamesActor), "H 계통 이상 기록에 사람 이름도 손댄 방도 적히지 않는다");
        Check(runs.All(r => !r.PanelTouchIsUnique),
            $"H-2 설비 앞에 선 사람이 결번 개체 하나뿐인 판이 없다 ({Rate(runs, r => r.PanelTouchIsUnique):0}%)");

        Check(runs.All(r => r.GhostAt >= 0f), $"I 괴물이 모든 판에서 나타난다 ({Rate(runs, r => r.GhostAt >= 0f):0}%)");
        Check(runs.Where(r => r.GhostAt >= 0f).All(r => r.GhostAt <= 55f),
            $"I-2 괴물이 근무 전반에 나온다 (가장 늦은 판 {runs.Max(r => r.GhostAt):0.0}초)");

        float quiet = Rate(runs, r => r.MaxGap > 20f);
        Check(quiet <= 20f, $"공백 20초를 넘는 판이 드물다 ({quiet:0}%)");
    }

    private static float Avg(List<Run> runs, System.Func<Run, float> pick)
    {
        var v = runs.Select(pick).Where(x => x >= 0f).ToList();
        return v.Count == 0 ? -1f : v.Average();
    }

    private static float Rate(List<Run> runs, System.Func<Run, bool> pick) =>
        runs.Count == 0 ? 0f : runs.Count(pick) * 100f / runs.Count;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
