using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 플레이테스트에서 지적된 운영 규칙 세 가지를 실제로 굴려 확인한다.
//
//   godot --headless --path . res://scenes/debug/OpsRuleTest.tscn
//
//   ① 경고 필요 인원 — 코어실·발전실만 2명, 나머지 작업실은 1명.
//   ② 경고 해제     — 사람이 도착하는 순간이 아니라 짧게 작업해야 풀린다.
//                     그 작업 시간은 실제 고장 수리 시간보다 반드시 짧다.
//   ③ 방해공작      — 혼자 있는 방에서는 손대지 않는다(로그만으로 범인이 드러난다).
//                     DAY1 은 거의 일어나지 않는다.
public partial class OpsRuleTest : Node
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
        GD.Print("################ 운영 규칙 검사 ################");

        CheckWarningStaffData();
        CheckWarningStabilize();
        CheckSaboteurNeedsCompany();
        CheckDay1SabotageIsRare();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── ① 경고 필요 인원 ─────────────────────────────────────────────
    private void CheckWarningStaffData()
    {
        GD.Print("\n---------------- ① 경고 필요 인원 ----------------");
        var twoMan = new HashSet<string> { "core_room", "power_room" };

        for (int day = 1; day <= 5; day++)
        {
            var profile = OpsProfile.For(day);
            if (profile == null) continue;

            foreach (var ops in profile.Rooms)
            {
                if (ops == null || string.IsNullOrEmpty(ops.WarningTitle)) continue;
                int want = twoMan.Contains(ops.RoomId) ? 2 : 1;
                Check(ops.WarningRequiredStaff == want,
                    $"DAY{day} {RoomName(ops.RoomId)} 경고는 {want}명 (실제 {ops.WarningRequiredStaff}명)");
            }

            // 정해진 시각의 경고가 방의 값을 덮어쓰지 않아야 한다.
            // 여기가 이번 문제의 원인이었다 — 전부 2명으로 박혀 있었다.
            foreach (var s in profile.ScheduledWarnings)
            {
                if (s == null) continue;
                int effective = s.RequiredStaff > 0
                    ? s.RequiredStaff
                    : OpsProfile.Room(s.RoomId)?.WarningRequiredStaff ?? 1;
                int want = twoMan.Contains(s.RoomId) ? 2 : 1;
                Check(effective == want,
                    $"DAY{day} 정해진 경고 {RoomName(s.RoomId)} 도 {want}명 (실제 {effective}명)");
            }
        }
    }

    // ── ② 경고는 작업해야 풀린다 ─────────────────────────────────────
    private void CheckWarningStabilize()
    {
        GD.Print("\n---------------- ② 경고 해제 ----------------");

        // 안정화 시간이 고장 수리 시간보다 짧은가 — 경고 때 잡는 쪽이 늘 싸야 한다.
        foreach (var ops in OpsProfile.For(1).Rooms)
        {
            if (ops == null || string.IsNullOrEmpty(ops.WarningTitle)) continue;
            float stab = FacilityWarningSystem.StabilizeSeconds(ops);
            Check(stab > 0f && stab < ops.RepairSeconds,
                $"{RoomName(ops.RoomId)} 안정화 {stab:0.#}초 < 고장 수리 {ops.RepairSeconds:0}초");
        }

        // 실제로 한 번 굴려 본다. 아무도 배치하지 않고 경고가 뜰 때까지 기다린다.
        StartShift(1);
        FacilityWarning w = null;
        for (float t = 0f; t < 110f && w == null; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
            w = _sim.Warnings.Active.FirstOrDefault();
        }
        if (w == null) { Check(false, "근무 중 경고가 한 번은 뜬다"); return; }

        string room = w.RoomId;
        GD.Print($"   {RoomName(room)} 경고 — {w.RequiredStaff}명 필요 · 안정화 {w.WorkNeeded:0.#}초 · " +
                 $"제한 {w.Remaining:0}초");

        // 필요한 만큼 사람을 보낸다.
        var roster = _sim.GetActiveEmployeeIds().ToList();
        for (int i = 0; i < w.RequiredStaff && i < roster.Count; i++) _sim.AssignToRoom(roster[i], room);

        // 도착할 때까지 기다린다(걷는 동안은 아직 인원이 아니다).
        for (float t = 0f; t < 40f && _sim.OnDutyCount(room) < w.RequiredStaff; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
        Check(_sim.OnDutyCount(room) >= w.RequiredStaff, $"{RoomName(room)}에 인원이 도착했다");

        // 도착 직후 — 아직 풀리면 안 된다.
        GameState.Instance.AdvanceDayTime(Step);
        _sim.Tick(Step);
        bool stillOpen = _sim.Warnings.HasActive(room);
        Check(stillOpen, "도착하자마자 경고가 풀리지 않는다");

        // 안정화 시간의 절반쯤 — 여전히 떠 있어야 한다.
        float need = w.WorkNeeded;
        RunSeconds(need * 0.5f);
        Check(_sim.Warnings.HasActive(room), $"절반({need * 0.5f:0.#}초)만 작업했을 때도 아직 떠 있다");
        float before = _sim.Warnings.ForRoom(room)?.Remaining ?? -1f;

        // 인원이 붙어 있는 동안 제한시간은 멈춰 있어야 한다.
        RunSeconds(0.5f);
        float after = _sim.Warnings.ForRoom(room)?.Remaining ?? -1f;
        Check(Mathf.IsEqualApprox(before, after) || after < 0f,
            $"인원이 붙어 있는 동안 제한시간이 멈춘다 ({before:0.##} → {after:0.##})");

        // 나머지를 채우면 풀린다.
        int preventedBefore = _sim.Warnings.Prevented;
        RunSeconds(need * 0.6f + 1f);
        Check(!_sim.Warnings.HasActive(room), $"{need:0.#}초를 채우면 경고가 풀린다");
        Check(_sim.Warnings.Prevented > preventedBefore, "막아 낸 경고로 집계된다");
    }

    // ── ③ 방해공작은 혼자일 때 하지 않는다 ───────────────────────────
    private void CheckSaboteurNeedsCompany()
    {
        GD.Print("\n---------------- ③ 혼자일 때는 손대지 않는다 ----------------");
        const int Trials = 6;

        // (가) 결번자를 코어실에 **혼자** 둔다. 나머지는 다른 방에 흩어 둔다.
        int aloneSabotage = 0;
        for (int i = 0; i < Trials; i++) aloneSabotage += RunSabotageTrial(2, withCompany: false);
        GD.Print($"   혼자 있을 때 — {Trials}회 중 방해공작 {aloneSabotage}건");
        Check(aloneSabotage == 0, "혼자 있는 방에서는 방해공작이 일어나지 않는다");

        // (나) 같은 방에 한 명을 더 둔다 — 이때는 일어난다.
        int companySabotage = 0;
        for (int i = 0; i < Trials; i++) companySabotage += RunSabotageTrial(2, withCompany: true);
        GD.Print($"   동료가 있을 때 — {Trials}회 중 방해공작 {companySabotage}건");
        Check(companySabotage > 0, $"동료가 있으면 방해공작이 일어난다 ({companySabotage}/{Trials})");
    }

    // 결번자를 코어실에 두고 한 근무를 끝까지 돌린다. 반환값 = 방해공작 건수.
    private int RunSabotageTrial(int day, bool withCompany)
    {
        StartShift(day);
        var roster = _sim.GetActiveEmployeeIds().ToList();
        string saboteur = GameState.Instance.SaboteurEmployeeId;

        _sim.AssignToRoom(saboteur, "core_room");
        // 나머지는 한 명씩 다른 방에 흩어 둔다(동료 조건이면 한 명만 코어실에 같이).
        string[] elsewhere = { "power_room", "maintenance_room", "storage_room", "guard_room", "vent_room" };
        int k = 0;
        bool companyPlaced = !withCompany;
        foreach (string id in roster)
        {
            if (id == saboteur) continue;
            if (!companyPlaced) { _sim.AssignToRoom(id, "core_room"); companyPlaced = true; continue; }
            _sim.AssignToRoom(id, elsewhere[k++ % elsewhere.Length]);
        }

        RunSeconds(DayObjectives.MaxShiftSeconds);
        return EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Sabotage);
    }

    // ── ④ DAY1 은 거의 일어나지 않는다 ───────────────────────────────
    private void CheckDay1SabotageIsRare()
    {
        GD.Print("\n---------------- ④ DAY1 방해공작 ----------------");
        const int Trials = 10;
        int total = 0;
        for (int i = 0; i < Trials; i++) total += RunSabotageTrial(1, withCompany: true);
        GD.Print($"   DAY1 — {Trials}회 중 방해공작 {total}건 (동료를 붙여 둔 최악의 배치)");
        Check(total <= Trials / 4,
            $"DAY1 방해공작은 드물다 ({total}/{Trials}회, 기준 {Trials / 4}회 이하)");

        // 비교 — DAY2 는 같은 배치에서 실제로 일어나야 한다.
        int day2 = 0;
        for (int i = 0; i < Trials; i++) day2 += RunSabotageTrial(2, withCompany: true);
        GD.Print($"   DAY2 — {Trials}회 중 방해공작 {day2}건");
        Check(day2 > total, $"DAY2 는 DAY1 보다 잦다 (DAY2 {day2} > DAY1 {total})");
    }

    // ── 도우미 ──────────────────────────────────────────────────────

    private void StartShift(int day)
    {
        GameState.Instance.ResetRun(day);
        GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        _sim.ResetRun();
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void RunSeconds(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            GameState.Instance.AdvanceDayTime(Step);
            _sim.Tick(Step);
        }
    }

    private string RoomName(string roomId) => _sim.GetRoomDef(roomId)?.DisplayName ?? roomId;

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
