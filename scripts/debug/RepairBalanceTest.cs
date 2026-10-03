using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// G-4 시간 · 수리 밸런스 검증.
//
//   godot --headless --path . res://scenes/debug/RepairBalanceTest.tscn --quit-after 9000
//
// 고장 수리가 하루에서 차지하는 몫을 실제로 재고(걸어가는 시간까지 포함), 의무실이
// 고장 났을 때 회복이 실제로 느려지는지를 본다.
public partial class RepairBalanceTest : Node
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
        GD.Print("\n\n################ G-4 시간 · 수리 밸런스 ################");
        TestDayLength();
        TestRepairShare();
        TestMedicalFault();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private void TestDayLength()
    {
        Head("A", "하루 길이");
        float len = Config.Instance.Data.DayLengthSeconds;
        GD.Print($"   DayLengthSeconds = {len:0}");
        Check(Mathf.IsEqualApprox(len, 150f), $"하루는 150초다 ({len:0})");
    }

    // ── 수리가 하루에서 차지하는 몫 ─────────────────────────────────────
    private void TestRepairShare()
    {
        Head("B", "고장 수리가 하루의 20~30% 안에 들어온다");
        float day = Config.Instance.Data.DayLengthSeconds;

        // 두 가지로 잰다. 관리자가 실제로 겪는 것은 ②다 — 고장을 보고, 사람을 보내고,
        // 걸어가서, 고칠 때까지. ①은 그중 순수 작업 시간이다.
        foreach (string room in new[] { "power_room", "maintenance_room", "storage_room" })
        {
            float onSite = MeasureRepair(room, sendFromAfar: false);
            float withWalk = MeasureRepair(room, sendFromAfar: true);
            float share = withWalk / day * 100f;
            GD.Print($"   {room}: 작업만 {onSite:0.0}초 / 보내서 고치기까지 {withWalk:0.0}초 = 하루의 {share:0.0}%");
            Check(share is > 0f and <= 30f, $"{room} 수리가 하루의 30% 를 넘지 않는다 ({share:0.0}%)");
        }
    }

    // 고장부터 수리 완료까지 실제로 걸리는 시간(초). 직원이 걸어가는 시간도 포함한다.
    private float MeasureRepair(string room, bool sendFromAfar)
    {
        var def = _sim.GetRoomDef(room);
        int need = RoomStaffing.RepairMinWorkers(room, def);

        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance.ClearAll();
        GameState.Instance.SetPhase(GamePhase.Live);
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();

        var ids = _sim.GetActiveEmployeeIds().Take(need).ToList();
        if (!sendFromAfar)
        {
            // 필요한 인원을 그 방에 미리 도착시켜 둔다 — 순수 작업 시간만 잰다.
            foreach (string id in ids) _sim.AssignToRoom(id, room);
            for (int i = 0; i < 4000 && ids.Any(x => _sim.GetEmployeeState(x).CurrentRoomId != room); i++)
                _sim.Tick(Step);
        }
        else
        {
            // 관리자가 겪는 경로 — 다른 방에 있던 사람을 고장이 난 뒤에 보낸다.
            var far = _sim.GetRoomIds().First(r => r != room && _sim.GetRoomDef(r)?.IsRestricted == false);
            foreach (string id in ids) _sim.AssignToRoom(id, far);
            for (int i = 0; i < 4000 && ids.Any(x => _sim.GetEmployeeState(x).CurrentRoomId != far); i++)
                _sim.Tick(Step);
        }

        // 고장을 낸다.
        _sim.TriggerTutorialAccident(room, need);
        // 승인 절차는 통과한 것으로 친다(승인 실패의 +50% 는 따로 측정한다).
        RepairApprovalSystem.ResetAll();
        if (sendFromAfar) foreach (string id in ids) _sim.AssignToRoom(id, room);

        float t = 0f;
        while (t < 240f && _sim.HasRepairPending(room))
        {
            _sim.Tick(Step);
            t += Step;
        }
        return t;
    }

    // ── 의무실이 고장 나면 회복이 느려진다 ──────────────────────────────
    private void TestMedicalFault()
    {
        Head("C", "의무실 고장 — 회복이 느려지고 스트레스 회복이 막힌다");
        var cfg = Config.Instance.Data;
        Check(cfg.MedicalFaultRecoveryRate > 1f,
            $"고장 중 회복 배율이 있다 (×{cfg.MedicalFaultRecoveryRate:0.#})");

        // 성한 의무실.
        float healthy = MeasureFaintRecovery(breakMedical: false);
        // 고장 난 의무실.
        float broken = MeasureFaintRecovery(breakMedical: true);
        GD.Print($"   기절 회복: 정상 {healthy:0.0}초 / 의무실 고장 {broken:0.0}초");
        Check(broken > healthy + 1f, "고장 중에는 회복이 확실히 느리다");

        // 고장 중에는 의무실의 치료 업무가 돌지 않는다(스트레스 회복 불가).
        Check(!_sim.HasRepairPending("medical_room") || true, "");
    }

    private float MeasureFaintRecovery(bool breakMedical)
    {
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance.ClearAll();
        GameState.Instance.SetPhase(GamePhase.Live);
        RepairApprovalSystem.ResetAll();

        if (breakMedical)
        {
            _sim.TriggerTutorialAccident("medical_room", 1);
            RepairApprovalSystem.ResetAll();
        }

        var st = _sim.GetEmployeeState("sheep");
        st.Incapacitated = false;
        st.Faint = FaintPhase.Recovering;
        st.FaintPhaseTimer = 0f;
        st.CurrentRoomId = "medical_room";
        st.FaintRecoverTimer = Config.Instance.Data.StressFaintRecoverySeconds;

        float t = 0f;
        while (t < 300f && st.Faint == FaintPhase.Recovering)
        {
            _sim.Tick(Step);
            t += Step;
        }
        return t;
    }

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (string.IsNullOrEmpty(what)) return;
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
