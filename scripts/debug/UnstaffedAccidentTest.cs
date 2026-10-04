using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 작업실을 비워 두면 실제로 사고가 나는가 — 방마다 몇 초 만에 나는지 잰다.
//
//   godot --headless --path . res://scenes/debug/UnstaffedAccidentTest.tscn --quit-after 90000
//
// 운영 규칙(data/ops)의 UnstaffedAccidentSeconds 가 방 설정보다 우선한다. 예전에는 거기에
// 75초가 적혀 있어, 하루(150초) 내내 비워 둬도 사고가 한 번 날까 말까 했다.
public partial class UnstaffedAccidentTest : Node
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
        GD.Print("\n\n################ 무인 방치 → 사고 ################");
        GD.Print("   방            규정 초   실제 발생");
        foreach (string room in new[] { "power_room", "core_room", "maintenance_room", "guard_room", "storage_room" })
            Measure(room);
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private void Measure(string room)
    {
        GameState.Instance.ResetRun(2);
        _sim.ResetRun();
        _sim.ResetForNewShift();
        EventLog.Instance?.ClearAll();
        RepairApprovalSystem.ResetAll();
        RotationOrderSystem.ResetAll();
        GameState.Instance.SetPhase(GamePhase.Live);

        // 이 방만 비우고 나머지는 전부 채운다 — 다른 방 사고가 동시 사고 제한에 걸리지 않게.
        var others = _sim.GetRoomIds()
            .Where(r => r != room && _sim.GetRoomDef(r)?.IsRestricted == false && _sim.IsRoomActive(r))
            .ToList();
        var ids = _sim.GetActiveEmployeeIds();
        for (int i = 0; i < ids.Count; i++) _sim.AssignToRoom(ids[i], others[i % others.Count]);
        for (int i = 0; i < 4000 && ids.Any(x => _sim.GetEmployeeState(x).IsMoving); i++) _sim.Tick(Step);

        float limit = RoomStaffing.UnstaffedAccidentSeconds(room, _sim.GetRoomDef(room));
        float t = 0f;
        while (t < 150f && !_sim.HasRepairPending(room))
        {
            _sim.Tick(Step);
            GameState.Instance.AdvanceDayTime(Step);
            t += Step;
        }
        bool fired = _sim.HasRepairPending(room);
        GD.Print($"   {Name(room),-12}  {limit,5:0}초   {(fired ? $"{t,5:0.0}초" : "  없음")}");
        Check(fired, $"{Name(room)} 을 비워 두면 하루 안에 사고가 난다");
    }

    private static string Name(string roomId) =>
        FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
