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
        CheckRelocationRoutes();
        CheckUnstaffedNeedsStay();

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

        // (가) 결번을 코어실에 **혼자** 둔다. 나머지는 다른 방에 흩어 둔다.
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

    // 결번을 코어실에 두고 한 근무를 끝까지 돌린다. 반환값 = 방해공작 건수.
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

    // ── ⑤ 재배치 경로 ───────────────────────────────────────────────
    // 재배치 = 최소 이동. 지도에 없는 지름길로 가지 않고, 직원이 들어갈 수 없는 구역
    // (중앙 제어실 · 격리실)을 지나가지도 않는다.
    private void CheckRelocationRoutes()
    {
        GD.Print("\n---------------- ⑤ 재배치 경로 ----------------");

        void Route(string from, string to, params string[] expect)
        {
            var path = _sim.FindPath(from, to);
            string got = path.Count == 0 ? "길 없음" : string.Join(" → ", path.Select(RoomName));
            Check(path.SequenceEqual(expect),
                  $"{RoomName(from)} → {RoomName(to)} = {got}  (기대: {string.Join(" → ", expect.Select(RoomName))})");
        }

        Route("guard_room", "vent_room", "vent_room");                       // 바로 아래 방
        Route("vent_room", "guard_room", "guard_room");                      // 바로 위 방
        Route("guard_room", "maintenance_room", "maintenance_room");         // 같은 줄 옆방
        Route("storage_room", "medical_room", "maintenance_room", "medical_room");
        Route("power_room", "vent_room", "guard_room", "vent_room");         // 사이에 있는 경비실을 지난다

        // 제한 구역은 목적지일 때만 경로에 들어갈 수 있다.
        var ids = _sim.GetRoomIds().ToList();
        var bad = new List<string>();
        foreach (string from in ids)
        foreach (string to in ids)
        {
            if (from == to) continue;
            var path = _sim.FindPath(from, to);
            foreach (string step in path)
                if (step != to && _sim.GetRoomDef(step)?.IsRestricted == true)
                    bad.Add($"{RoomName(from)}→{RoomName(to)} 가 {RoomName(step)} 경유");
        }
        Check(bad.Count == 0, bad.Count == 0
            ? "어떤 경로도 제한 구역을 지나가지 않는다"
            : $"제한 구역 경유 {bad.Count}건 — {string.Join(" · ", bad.Take(3))}");

        // 직원이 쓰는 작업실끼리는 중앙 제어실 없이도 전부 오갈 수 있어야 한다.
        var work = ids.Where(r => _sim.GetRoomDef(r) is { IsRestricted: false }).ToList();
        var unreachable = new List<string>();
        foreach (string from in work)
        foreach (string to in work)
            if (from != to && _sim.FindPath(from, to).Count == 0)
                unreachable.Add($"{RoomName(from)}→{RoomName(to)}");
        Check(unreachable.Count == 0, unreachable.Count == 0
            ? $"작업실 {work.Count}곳이 서로 전부 이어져 있다"
            : $"길이 끊긴 쌍 {unreachable.Count}건 — {string.Join(" · ", unreachable.Take(3))}");
    }

    private string RoomName(string roomId) => _sim.GetRoomDef(roomId)?.DisplayName ?? roomId;

    // ── G-1 : 무인 경고는 머물러야 풀린다 ────────────────────────────────
    //
    // 예전에는 문만 열고 들어가면 그 순간 타이머가 0으로 돌아갔다. 스쳐 지나가며 경고만 끄고
    // 다시 나오는 것이 최적 플레이가 되어, 방을 비운 대가가 사라졌다.
    private void CheckUnstaffedNeedsStay()
    {
        GD.Print("\n---------------- 무인 경고 해제에 체류가 필요하다 ----------------");
        var sim = FacilitySimulation.Instance;
        var cfg = Config.Instance.Data;
        const float step = 1f / 30f;
        const string room = "power_room";

        Check(cfg.UnstaffedClearSeconds > 0f, $"체류 요구 시간이 설정돼 있다 ({cfg.UnstaffedClearSeconds:0}초)");

        GameState.Instance.ResetRun(2);
        sim.ResetRun();
        sim.ResetForNewShift();
        EventLog.Instance.ClearAll();
        GameState.Instance.SetPhase(GamePhase.Live);

        // 방을 비워 두고 경고를 키운다. 사고가 나 버리면 타이머가 0 으로 돌아가므로
        // 그 방의 실제 한계(ops 값)의 절반까지만 키운다 — 고정 20초로 두면 한계를 줄일 때마다 깨진다.
        foreach (string id in sim.GetActiveEmployeeIds()) sim.ClearAssignment(id);
        var st = sim.GetRoomState(room);
        float limit = NSP.Facility.RoomStaffing.UnstaffedAccidentSeconds(room, sim.GetRoomDef(room));
        for (float t = 0f; t < limit * 0.5f; t += step) sim.Tick(step);
        float grown = st.UnstaffedTimer;
        Check(grown > 1f, $"비워 두면 무인 타이머가 오른다 ({grown:0.0}초)");

        // 잠깐 들렀다 나간다 — 아직 풀리지 않는다.
        sim.AssignToRoom("cat", room);
        for (int i = 0; i < 3000 && sim.GetEmployeeState("cat").CurrentRoomId != room; i++) sim.Tick(step);
        float onArrival = st.UnstaffedTimer;
        for (float t = 0f; t < cfg.UnstaffedClearSeconds * 0.5f; t += step) sim.Tick(step);
        Check(st.UnstaffedTimer > 1f, $"들어온 것만으로는 풀리지 않는다 ({st.UnstaffedTimer:0.0}초)");
        Check(st.UnstaffedTimer <= onArrival + 0.1f, "머무는 동안 사고 타이머는 더 오르지 않는다");

        // 방이 다시 비면 머문 시간이 날아간다.
        sim.ClearAssignment("cat");
        for (int i = 0; i < 3000 && sim.GetEmployeeState("cat").CurrentRoomId == room; i++) sim.Tick(step);
        Check(st.UnstaffedClearTimer <= 0.01f, "방이 다시 비면 머문 시간이 초기화된다");

        // 끝까지 머무르면 풀린다.
        sim.AssignToRoom("cat", room);
        for (int i = 0; i < 3000 && sim.GetEmployeeState("cat").CurrentRoomId != room; i++) sim.Tick(step);
        for (float t = 0f; t < cfg.UnstaffedClearSeconds + 1f; t += step) sim.Tick(step);
        Check(st.UnstaffedTimer <= 0.01f, $"체류 시간을 채우면 경고가 풀린다 ({st.UnstaffedTimer:0.0}초)");
    }

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
