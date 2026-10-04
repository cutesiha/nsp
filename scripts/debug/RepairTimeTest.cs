using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 경고(노랑) 안정화와 고장(빨강) 수리가 **실제로** 몇 초 걸리는가.
//
//   godot --headless --path . res://scenes/debug/RepairTimeTest.tscn --quit-after 120000
//
// 관리자가 겪는 시간은 데이터에 적힌 작업 초가 아니다. 경고/고장을 보고 → 사람을 고르고 →
// 그 사람이 걸어가고 → 그제서야 작업이 쌓인다. 걸어가는 시간까지 포함해서 잰다.
//
// 경고는 인원이 다 차 있는 동안에만 작업이 쌓이고, 수리는 최소 인원 미만이면 게이지가
// 한 톨도 차지 않는다 — 그래서 "2명 필요" 인 방은 두 번째 사람이 도착해야 비로소 시작한다.
public partial class RepairTimeTest : Node
{
    private const float Step = 1f / 30f;
    private FacilitySimulation _sim;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        CallDeferred(nameof(RunAll));
    }

    private static readonly string[] Rooms =
    {
        "power_room", "core_room", "maintenance_room", "guard_room", "storage_room", "vent_room", "medical_room",
    };

    private void RunAll()
    {
        GD.Print("\n\n################ 경고 안정화 · 고장 수리 소요 시간 ################");
        GD.Print($"   하루 {Config.Instance.Data.DayLengthSeconds:0}초 · 근무 중 이동 속도 {Config.Instance.Data.EmployeeMoveSpeedInShift:0}");

        GD.Print("\n===== 경고(노랑) — 뜨고 나서 풀릴 때까지 =====");
        GD.Print("   방            필요  제한   작업   걸어가기   실제 총   결과");
        foreach (string room in Rooms) MeasureWarning(room);

        GD.Print("\n===== 고장(빨강) — 나고 나서 고쳐질 때까지 =====");
        GD.Print("   방            필요  작업   걸어가기   실제 총");
        foreach (string room in Rooms) MeasureRepair(room);

        GD.Print("\n===== 승인 실패(무응답 · 거절 · 미로 실패) 시 수리 =====");
        GD.Print("   방            정상   실패 후");
        foreach (string room in Rooms) MeasurePenalty(room);

        GD.Print("\n===== 최악 — 스트레스 '위험' 인 인원으로 수리할 때 =====");
        GD.Print("   방            정상   스트레스 위험");
        foreach (string room in Rooms) MeasureStressed(room);

        GetTree().Quit();
    }

    // ── 경고 ────────────────────────────────────────────────────────────
    private void MeasureWarning(string room)
    {
        var ops = OpsProfile.Room(room);
        if (ops == null || string.IsNullOrEmpty(ops.WarningTitle))
        {
            GD.Print($"   {Name(room),-12}  — 경고 없음(운영 규칙에 설정되지 않은 방)");
            return;
        }

        Reset();
        int need = Mathf.Max(1, ops.WarningRequiredStaff);
        var ids = _sim.GetActiveEmployeeIds().Take(need).ToList();
        string far = FarRoom(room);
        foreach (string id in ids) _sim.AssignToRoom(id, far);
        Settle(ids, far);

        RaiseWarning(ops, need, ops.WarningResponseSeconds);
        var w = _sim.Warnings.ForRoom(room);
        if (w == null) { GD.Print($"   {Name(room),-12}  — 경고를 띄우지 못했다"); return; }
        float stabilize = w.WorkNeeded;

        // 관리자가 곧바로 사람을 보낸다.
        foreach (string id in ids) _sim.AssignToRoom(id, room);
        float t = 0f, arrived = -1f;
        while (t < 200f && _sim.Warnings.ForRoom(room) != null)
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);
            t += Step;
            if (arrived < 0f && _sim.OnDutyCount(room) >= need) arrived = t;
        }
        bool ok = !_sim.HasRepairPending(room);
        GD.Print($"   {Name(room),-12}  {need}명  {ops.WarningResponseSeconds,4:0}초  {stabilize,4:0.0}초  "
                 + $"{(arrived < 0f ? 0f : arrived),6:0.0}초     {t,6:0.0}초   {(ok ? "막음" : "고장")}");
    }

    // ── 수리 ────────────────────────────────────────────────────────────
    private void MeasureRepair(string room)
    {
        Reset();
        var def = _sim.GetRoomDef(room);
        int need = RoomStaffing.RepairMinWorkers(room, def);
        float work = RoomStaffing.RepairSeconds(room, def);
        var ids = _sim.GetActiveEmployeeIds().Take(need).ToList();
        string far = FarRoom(room);
        foreach (string id in ids) _sim.AssignToRoom(id, far);
        Settle(ids, far);

        _sim.TriggerTutorialAccident(room, need);
        RepairApprovalSystem.ResetAll();   // 승인은 통과한 것으로 친다
        foreach (string id in ids) _sim.AssignToRoom(id, room);

        float t = 0f, arrived = -1f;
        while (t < 240f && _sim.HasRepairPending(room))
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);
            t += Step;
            if (arrived < 0f && _sim.OnDutyCount(room) >= need) arrived = t;
        }
        GD.Print($"   {Name(room),-12}  {need}명  {work,4:0.0}초  {(arrived < 0f ? 0f : arrived),6:0.0}초     {t,6:0.0}초");
    }

    // 승인 절차를 놓치면 그 수리가 얼마나 길어지는가.
    private void MeasurePenalty(string room)
    {
        float normal = RepairOnSite(room, penalty: false);
        float failed = RepairOnSite(room, penalty: true);
        GD.Print($"   {Name(room),-12}  {normal,5:0.0}초  {failed,6:0.0}초");
    }

    // 스트레스가 높은 인원은 업무 속도 배율(StressWorkRateDanger)이 걸린다 —
    // 수리 시간이 데이터에 적힌 초보다 길어지는 가장 큰 이유다.
    private void MeasureStressed(string room)
    {
        float normal = RepairOnSite(room, penalty: false);
        float stressed = RepairOnSite(room, penalty: false, stress: true);
        GD.Print($"   {Name(room),-12}  {normal,5:0.0}초  {stressed,8:0.0}초");
    }

    // 인원이 이미 그 방에 있는 상태에서의 순수 작업 시간.
    private float RepairOnSite(string room, bool penalty, bool stress = false)
    {
        Reset();
        var def = _sim.GetRoomDef(room);
        int need = RoomStaffing.RepairMinWorkers(room, def);
        var ids = _sim.GetActiveEmployeeIds().Take(need).ToList();
        foreach (string id in ids) _sim.AssignToRoom(id, room);
        Settle(ids, room);
        if (stress)
            foreach (string id in ids)
                _sim.GetEmployeeState(id).Stress = Config.Instance.Data.StressDangerFrom;

        _sim.TriggerTutorialAccident(room, need);
        if (penalty)
        {
            // 무응답 = 거절. 제한 시간이 다 지나가게 둔다.
            for (float t = 0f; t < Config.Instance.Data.RepairApproveSeconds + 1f; t += Step)
                RepairApprovalSystem.Tick(Step);
        }
        RepairApprovalSystem.ResetAll();

        float s = 0f;
        while (s < 240f && _sim.HasRepairPending(room)) { _sim.Tick(Step); s += Step; }
        return s;
    }

    // ── 도우미 ──────────────────────────────────────────────────────────
    private void Reset()
    {
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void Settle(List<string> ids, string room)
    {
        for (int i = 0; i < 6000 && ids.Any(x => _sim.GetEmployeeState(x).CurrentRoomId != room); i++)
            _sim.Tick(Step);
    }

    private string FarRoom(string room) => _sim.GetRoomIds()
        .First(r => r != room && _sim.GetRoomDef(r)?.IsRestricted == false && _sim.IsRoomActive(r));

    private static string Name(string roomId) =>
        FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;

    // FacilityWarningSystem.Raise 는 비공개다 — 검사에서만 직접 띄운다.
    private void RaiseWarning(RoomOpsDef ops, int staff, float seconds)
    {
        typeof(FacilityWarningSystem)
            .GetMethod("Raise", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(_sim.Warnings, new object[] { ops, _sim, staff, seconds, false });
    }
}
