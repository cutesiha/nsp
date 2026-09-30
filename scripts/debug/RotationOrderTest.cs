using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// G-3 순환 배치 규정 검증.
//
//   godot --headless --path . res://scenes/debug/RotationOrderTest.tscn --quit-after 6000
public partial class RotationOrderTest : Node
{
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
        GD.Print("\n\n################ G-3 순환 배치 규정 검증 ################");
        TestFiresOnce();
        TestSuccess();
        TestMissPenalty();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 하루 중간에 한 번 걸린다 ────────────────────────────────────────
    private void TestFiresOnce()
    {
        Head("A", "하루 절반쯤에 한 번 걸린다");
        Reset();
        var cfg = Config.Instance.Data;

        // 아직 이르다.
        GameState.Instance.AdvanceDayTime(cfg.DayLengthSeconds * cfg.RotationAtDayRatio * 0.5f);
        Tick(0.2f);
        Check(RotationOrderSystem.Current == RotationOrderSystem.Phase.Idle,
            $"이른 시간에는 걸리지 않는다 ({RotationOrderSystem.Current})");

        // 절반을 넘기면 걸린다.
        GameState.Instance.AdvanceDayTime(cfg.DayLengthSeconds * cfg.RotationAtDayRatio);
        Tick(0.2f);
        Check(RotationOrderSystem.Current == RotationOrderSystem.Phase.Waiting,
            $"절반을 넘기면 걸린다 ({RotationOrderSystem.Current})");
        Check(RotationOrderSystem.Needed >= 1, $"옮겨야 할 인원이 정해진다 ({RotationOrderSystem.Needed}명)");
        Check(RotationOrderSystem.SecondsLeft > 0f, $"제한 시간이 있다 ({RotationOrderSystem.SecondsLeft:0}초)");
        Check(HasLog("순환 배치 시간"), "알림이 기록에 남는다");
    }

    // ── 지키면 끝 ───────────────────────────────────────────────────────
    private void TestSuccess()
    {
        Head("B", "제한 시간 안에 옮기면 통과한다");
        Reset();
        Fire();
        int need = RotationOrderSystem.Needed;

        // 요구 인원만큼 다른 방으로 옮긴다.
        var ids = _sim.GetActiveEmployeeIds().Take(need).ToList();
        foreach (string id in ids)
        {
            string was = _sim.GetEmployeeState(id).AssignedRoomId;
            string to = _sim.GetRoomIds().FirstOrDefault(r => r != was && _sim.GetRoomDef(r)?.IsRestricted == false);
            _sim.AssignToRoom(id, to);
        }
        Tick(0.5f);

        Check(RotationOrderSystem.Current == RotationOrderSystem.Phase.Done, "판정이 끝났다");
        Check(!RotationOrderSystem.Failed, "지킨 것으로 판정된다");
        Check(Mathf.IsEqualApprox(RotationOrderSystem.ShiftStressMultiplier(), 1f), "피로 배율은 그대로 1배");
        Check(HasLog("순환 배치 완료"), "완료가 기록에 남는다");
    }

    // ── 안 지키면 피로가 빨라진다 ───────────────────────────────────────
    private void TestMissPenalty()
    {
        Head("C", "못 지키면 남은 근무 동안 피로가 빨라진다");
        Reset();
        Fire();
        var cfg = Config.Instance.Data;

        // 아무도 옮기지 않고 제한 시간을 넘긴다.
        Tick(cfg.RotationWindowSeconds + 1f);
        Check(RotationOrderSystem.Current == RotationOrderSystem.Phase.Done, "판정이 끝났다");
        Check(RotationOrderSystem.Failed, "미이행으로 판정된다");
        Check(RotationOrderSystem.ShiftStressMultiplier() > 1f,
            $"피로 배율이 오른다 (×{RotationOrderSystem.ShiftStressMultiplier():0.#})");
        Check(HasLog("순환 배치 미이행"), "미이행이 기록에 남는다");

        // 실제로 야간 피로가 더 빨리 오르는가.
        //
        // 스트레스에는 사고 · 환기 · 불편한 동석 등 여러 출처가 섞여 있어, 그대로 재면
        // 야간 피로 몫이 묻힌다. 재는 동안만 다른 출처를 꺼 두고 견준다(끝나면 되돌린다).
        float ic = cfg.IncidentStressAmount, vu = cfg.VentUnstaffedStressAmount;
        float vf = cfg.VentFaultStressAmount, un = cfg.UneasyStressAmount;
        cfg.IncidentStressAmount = cfg.VentUnstaffedStressAmount = 0f;
        cfg.VentFaultStressAmount = cfg.UneasyStressAmount = 0f;

        float penalised = MeasureShiftStress(cfg);
        RotationOrderSystem.ResetAll();     // 배율을 1배로 돌린다
        float normal = MeasureShiftStress(cfg);

        cfg.IncidentStressAmount = ic;
        cfg.VentUnstaffedStressAmount = vu;
        cfg.VentFaultStressAmount = vf;
        cfg.UneasyStressAmount = un;

        GD.Print($"   같은 시간 야간 피로: 미이행 {penalised:0.00} / 평소 {normal:0.00}");
        Check(penalised > normal + 0.01f, "미이행 쪽이 실제로 더 빨리 쌓인다");
    }

    // 야간 피로 두 주기 동안 한 사람의 스트레스가 얼마나 오르는가.
    private float MeasureShiftStress(ConfigData cfg)
    {
        var st = _sim.GetEmployeeState("cat");
        st.Stress = 1f;
        st.Incapacitated = false;
        for (float t = 0f; t < cfg.ShiftStressIntervalSeconds * 2f + 0.5f; t += 1f / 30f) _sim.Tick(1f / 30f);
        return st.Stress - 1f;
    }

    // --- 도우미 ---------------------------------------------------------

    private void Fire()
    {
        var cfg = Config.Instance.Data;
        GameState.Instance.AdvanceDayTime(cfg.DayLengthSeconds * cfg.RotationAtDayRatio + 1f);
        Tick(0.2f);
    }

    private bool HasLog(string part) =>
        EventLog.Instance.GetAllEntries().Any(e => (e.Description ?? "").Contains(part));

    private void Reset()
    {
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance.ClearAll();
        GameState.Instance.SetPhase(GamePhase.Live);
        RotationOrderSystem.ResetAll();

        // 전원을 서로 다른 방에 배치해 둔다.
        var rooms = _sim.GetRoomIds().Where(r => _sim.GetRoomDef(r)?.IsRestricted == false).ToList();
        int i = 0;
        foreach (string id in _sim.GetActiveEmployeeIds())
            _sim.AssignToRoom(id, rooms[i++ % rooms.Count]);
    }

    private void Tick(float seconds)
    {
        const float step = 1f / 30f;
        for (float t = 0f; t < seconds; t += step) _sim.Tick(step);
    }

    private void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
