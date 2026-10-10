using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 오늘의 업무 / 근무 종료 규칙 자동 검증.
//
//   godot --headless --path . scenes/debug/DayObjectiveTest.tscn --quit-after 900
//
// 실제 FacilitySimulation 을 돌려서 "언제 근무를 끝낼 수 있는가"가 데이터대로
// 움직이는지 확인한다.
public partial class DayObjectiveTest : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 을 찾지 못했습니다."); return; }
        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 오늘의 업무 / 근무 종료 검증 ################");

        // ── Test A : DAY1 업무가 데이터대로 등록되는가 ────────────────
        StartShift(1);
        var lines = DayObjectives.Lines();
        GD.Print($"\n[A] DAY1 업무 {lines.Count}개  (최대 근무시간 {DayObjectives.MaxShiftSeconds:0}초)");
        foreach (var l in lines)
            GD.Print($"    {(l.Required ? "필수" : "선택")}  {l.Def.DisplayText}   {l.ProgressText}");
        Check("A 업무는 필수 2 + 선택 1 = 3개", lines.Count == 3
            && lines.Count(l => l.Required) == 2 && lines.Count(l => !l.Required) == 1);
        Check("A 코어 목표가 18%", lines.Any(l => l.Def.Type == DayObjectiveType.CoreProgress
                                                  && Mathf.IsEqualApprox(l.Def.TargetValue, 18f)));
        Check("A 방해공작 관련 업무는 노출되지 않는다",
            !lines.Any(l => l.Def.DisplayText.Contains("방해") || l.Def.DisplayText.Contains("결번")));

        // ── Test B : 경고만 처리하고 코어 미달 ────────────────────────
        RunUntilWarningResolved(120f);
        int prevented = _sim.Warnings.Prevented;
        GD.Print($"\n[B] 경고 처리 {prevented}회 · 코어 {GameState.Instance.CoreProgress:0.0}%  " +
                 $"→ 필수 {DayObjectives.RequiredDone}/{DayObjectives.RequiredTotal}");
        Check("B 경고 1회 이상 안정화했다", prevented >= 1);
        Check("B 코어가 아직 18% 미만이다", GameState.Instance.CoreProgress < 18f);
        Check("B 필수 1/2 이고 근무 종료 불가", DayObjectives.RequiredDone == 1 && !DayObjectives.CanEndShift);
        Check("B 버튼 표시가 '업무 1/2'", DayObjectives.ShortStatus() == "업무 1/2");

        // ── Test C : 코어까지 달성 ────────────────────────────────────
        GameState.Instance.AddCoreProgress(18f - GameState.Instance.CoreProgress, "테스트");
        GD.Print($"\n[C] 코어 {GameState.Instance.CoreProgress:0.0}% → 필수 {DayObjectives.RequiredDone}/2");
        Check("C 필수 2/2 이고 근무 종료 가능", DayObjectives.RequiredDone == 2 && DayObjectives.CanEndShift);
        Check("C 버튼 표시가 '업무 2/2 ✓'", DayObjectives.ShortStatus() == "업무 2/2 ✓");

        // ── Test D : 끝낼 수 있어도 저절로 끝나지 않는다 ──────────────
        float before = GameState.Instance.CoreProgress;
        float t0 = GameState.Instance.DayTimeSeconds;
        Run(20f);
        GD.Print($"\n[D] 계속 근무 20초 → 코어 {before:0.0}% → {GameState.Instance.CoreProgress:0.0}%");
        Check("D 필수 완료 후에도 근무가 계속된다",
            GameState.Instance.DayTimeSeconds > t0 + 19f);
        Check("D 계속 일하면 코어가 더 오른다", GameState.Instance.CoreProgress > before);
        Check("D 시간이 남아 있으면 자동 종료되지 않는다",
            DayObjectives.RemainingSeconds > 0f && ShouldAutoEnd() == false);

        // ── Test E : 최대 근무시간에서 자동 종료 ──────────────────────
        Run(DayObjectives.MaxShiftSeconds - GameState.Instance.DayTimeSeconds + 1f);
        GD.Print($"\n[E] 경과 {GameState.Instance.DayTimeSeconds:0}초 / 최대 {DayObjectives.MaxShiftSeconds:0}초");
        Check("E 남은 시간이 0", DayObjectives.RemainingSeconds <= 0.01f);
        Check("E 최대 근무시간에서 자동 종료 조건이 선다", ShouldAutoEnd());
        float coreAtEnd = GameState.Instance.CoreProgress;
        Check("E 한 DAY 안에서 코어 100% 는 불가능하다", coreAtEnd < 100f);

        // ── Test F : 필수 미완료 상태로 시간 종료해도 막히지 않는다 ───
        StartShift(1);
        Run(DayObjectives.MaxShiftSeconds + 1f);
        GD.Print($"\n[F] 무조작 종료 — 코어 {GameState.Instance.CoreProgress:0.0}% · " +
                 $"필수 {DayObjectives.RequiredDone}/{DayObjectives.RequiredTotal}");
        Check("F 필수를 못 끝냈어도 시간이 끝나면 종료된다", ShouldAutoEnd());
        Check("F 종료 조건은 필수 업무와 무관하다(소프트락 없음)",
            ShouldAutoEnd() && !DayObjectives.CanEndShift || DayObjectives.CanEndShift);

        // ── Test G : 코어는 누적 — 다음 날로 이어진다 ─────────────────
        StartShift(1);
        GameState.Instance.AddCoreProgress(25f - GameState.Instance.CoreProgress, "테스트");
        float carried = GameState.Instance.CoreProgress;
        GameState.Instance.GoToNextDay();
        GD.Print($"\n[G] DAY1 {carried:0.0}% → DAY{GameState.Instance.CurrentDay} 시작 " +
                 $"{GameState.Instance.CoreProgress:0.0}%");
        Check("G 다음 날에도 코어가 그대로 이어진다",
            Mathf.IsEqualApprox(GameState.Instance.CoreProgress, carried));
        var d2 = DayObjectives.Lines();
        var d2core = d2.FirstOrDefault(l => l.Def.Type == DayObjectiveType.CoreProgress);
        Check("G DAY2 코어 목표가 38%", d2core != null && Mathf.IsEqualApprox(d2core.Def.TargetValue, 38f));
        Check("G 25% 상태에서는 DAY2 목표 미달", d2core is { Done: false });
        GameState.Instance.AddCoreProgress(38f - GameState.Instance.CoreProgress, "테스트");
        Check("G 38% 도달 시 DAY2 코어 업무 완료",
            DayObjectives.Lines().First(l => l.Def.Type == DayObjectiveType.CoreProgress).Done);

        // ── Test H : 선택 업무 실패는 종료를 막지 않는다 ──────────────
        StartShift(1);
        GameState.Instance.AddCoreProgress(20f, "테스트");
        // 고장을 한 건 일으켜 선택 업무(고장 0회)를 깨뜨린다.
        _sim.TriggerWarningFailure("storage_room", "테스트 고장");
        Run(1f);
        var hLines = DayObjectives.Lines();
        var optional = hLines.First(l => !l.Required);
        GD.Print($"\n[H] 선택 업무 '{optional.Def.DisplayText}' → {optional.ProgressText} " +
                 $"(달성 {optional.Done})");
        Check("H 선택 업무가 실패 상태다", !optional.Done);
        Check("H 선택 업무 실패는 필수 판정에 영향이 없다",
            hLines.Count(l => l.Required && l.Done) == DayObjectives.RequiredDone);
        Check("H 업무평가는 0점", DayObjectives.OptionalCompleted() == 0);

        // 평가 등급 표(선택 업무 보상이 등급에만 반영되는지).
        GD.Print($"\n[평가] 100%/0점 → {DayObjectives.Grade(100f, 0)} · " +
                 $"100%/1점 → {DayObjectives.Grade(100f, 1)} · " +
                 $"100%/3점 → {DayObjectives.Grade(100f, 3)} · " +
                 $"100%/5점 → {DayObjectives.Grade(100f, 5)} · " +
                 $"92%/4점 → {DayObjectives.Grade(92f, 4)}");
        Check("평가 등급이 선택 업무 점수에 따라 올라간다",
            DayObjectives.Grade(100f, 0) == "C" && DayObjectives.Grade(100f, 5) == "S");
        Check("코어 100% 미만이면 S/A 가 나오지 않는다",
            DayObjectives.Grade(92f, 5) is "C" or "D");

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
    }

    // ShiftFlowController 가 쓰는 자동 종료 조건과 같은 식.
    private static bool ShouldAutoEnd() =>
        (GameState.Instance?.DayTimeSeconds ?? 0f) >= DayObjectives.MaxShiftSeconds;

    private void Check(string label, bool ok)
    {
        GD.Print(ok ? $"   PASS  {label}" : $"   FAIL  {label}");
        if (ok) _pass++; else _fail++;
    }

    private void StartShift(int day)
    {
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        GameState.Instance.ResetRun(day);
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

        GameState.Instance.AssignRandomSaboteur(roster);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void Run(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    // 경고가 뜨면 사람을 보내 안정화시킨다(플레이어 대역).
    private void RunUntilWarningResolved(float limit)
    {
        string borrowed = "", origin = "";
        for (float t = 0f; t < limit; t += Step)
        {
            if (_sim.Warnings.Prevented >= 1) return;
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);

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
            borrowed = _sim.GetRoomState(donor).OccupantEmployeeIds.FirstOrDefault() ?? "";
            origin = donor;
            if (borrowed.Length > 0) _sim.AssignToRoom(borrowed, w.RoomId);
        }
    }
}
