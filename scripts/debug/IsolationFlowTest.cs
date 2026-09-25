using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 격리 절차 검사 — 명령 → 그 자리 반응 → 제 발로 이동 → 수용, 그리고 취소·기절 우선순위.
//
//   godot --headless --path . res://scenes/debug/IsolationFlowTest.tscn
//
// 제일 중요한 것 둘:
//   ① 격리는 **본인이 걸어간다** — 기절 이송(다른 직원이 운반)과 섞이지 않는다.
//   ② 명령을 받은 순간 곧바로 걷지 않는다 — 캐릭터별 반응이 먼저다.
public partial class IsolationFlowTest : Node
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
        GD.Print("################ 격리 절차 검사 ################");

        CheckReactBeforeWalking();
        CheckWalksThereAlone();
        CheckNobodyCarries();
        CheckPerCharacterReaction();
        CheckPerCharacterStruggle();
        CheckCancelWhileReacting();
        CheckCancelWhileWalking();
        CheckCancelAfterConfined();
        CheckFaintBeatsIsolation();
        CheckFaintInsideKeepsIsolation();
        CheckTwoAtOnce();
        CheckSaboteurLooksTheSame();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── §6 명령을 받은 순간에는 아직 한 발도 떼지 않는다 ─────────────
    private void CheckReactBeforeWalking()
    {
        GD.Print("\n---------------- 명령 직후: 그 자리에서 반응 ----------------");
        Start();
        Place("cat", "core_room");
        Place("wolf", "core_room");
        Settle();

        var st = _sim.GetEmployeeState("cat");
        string wasIn = st.CurrentRoomId;
        Check(_sim.IsolateEmployee("cat"), "격리 명령이 받아들여졌다");
        Check(st.Isolated, "격리 상태가 되었다");
        Check(st.Isolation == IsolationPhase.React, $"그 자리에서 반응하는 단계다 ({st.Isolation})");
        Check(!st.IsMoving && st.PathQueue.Count == 0, "아직 경로가 없다 — 걷지 않는다");
        Check(st.AssignedRoomId.Length == 0, "배치가 풀렸다 — 업무 기여 0");
        Check(_sim.OnDutyCount(wasIn) == 1, "작업실 인원에서 빠졌다(남은 사람은 늑대 하나)");

        // 반응이 끝나기 전에는 움직이지 않는다.
        float react = IsolationSystem.ReactSeconds("cat");
        bool movedEarly = false;
        for (float t = 0f; t < react - 0.1f; t += Step)
        {
            Tick();
            if (st.IsMoving || st.PathQueue.Count > 0 || st.CurrentRoomId != wasIn) movedEarly = true;
        }
        Check(!movedEarly, $"반응 {react:0.0}초 동안 제자리에 있다");
        Check(st.Isolation == IsolationPhase.React, "아직 반응 단계다");

        // 반응이 끝나면 스스로 출발한다.
        for (float t = 0f; t < 1f && st.Isolation == IsolationPhase.React; t += Step) Tick();
        Check(st.Isolation == IsolationPhase.Walking, $"반응이 끝나고 이동 단계로 넘어갔다 ({st.Isolation})");
        Check(st.IsMoving, "스스로 걷기 시작했다");
        Check(st.TargetRoomId.Length > 0, $"격리실 쪽으로 향한다 ({st.TargetRoomId})");
    }

    // ── §13 제 발로 격리실까지 간다 ───────────────────────────────────
    private void CheckWalksThereAlone()
    {
        GD.Print("\n---------------- 제 발로 격리실까지 ----------------");
        Start();
        Place("sheep", "generator_room");
        Settle();

        var st = _sim.GetEmployeeState("sheep");
        _sim.IsolateEmployee("sheep");
        bool walked = false;
        for (float t = 0f; t < 60f; t += Step)
        {
            Tick();
            if (st.IsMoving) walked = true;
            if (st.Isolation == IsolationPhase.InRoom) break;
        }
        Check(walked, "스스로 걸어서 이동했다");
        Check(st.Isolation == IsolationPhase.InRoom, $"격리실에 수용되었다 ({st.Isolation})");
        Check(st.CurrentRoomId == "isolation_room", $"실제로 격리실에 있다 ({st.CurrentRoomId})");
        Check(!st.IsMoving, "도착해 멈췄다");
        Check(HasLog("격리 명령") && HasLog("격리실 수용"), "명령과 수용이 모두 기록되었다");
    }

    // ── §2 아무도 끌고 가지 않는다(기절 이송과 섞이지 않는다) ─────────
    private void CheckNobodyCarries()
    {
        GD.Print("\n---------------- 아무도 끌고 가지 않는다 ----------------");
        Start();
        Place("rabbit", "maintenance_room");
        Place("wolf", "maintenance_room");
        Place("dog", "maintenance_room");
        Settle();

        var st = _sim.GetEmployeeState("rabbit");
        _sim.IsolateEmployee("rabbit");
        bool carried = false, hadResponder = false, faintPhase = false;
        for (float t = 0f; t < 60f; t += Step)
        {
            Tick();
            foreach (string other in Others("rabbit"))
                if (_sim.GetEmployeeState(other)?.CarryingVictimId == "rabbit") carried = true;
            if (st.ResponderId.Length > 0) hadResponder = true;
            if (st.Faint != FaintPhase.None) faintPhase = true;
            if (st.Isolation == IsolationPhase.InRoom) break;
        }
        Check(!carried, "누구도 격리 대상을 업거나 끌고 가지 않았다");
        Check(!hadResponder, "구조자가 붙지 않았다 — 격리는 의식이 있는 상태다");
        Check(!faintPhase, "기절 흐름이 끼어들지 않았다");
        Check(st.Isolation == IsolationPhase.InRoom, "혼자 걸어 수용되었다");
    }

    // ── §6~§12 반응 시간은 캐릭터마다 다르고 0.8~2.0초 안이다 ─────────
    private void CheckPerCharacterReaction()
    {
        GD.Print("\n---------------- 캐릭터별 반응 시간 ----------------");
        var seen = new Dictionary<string, float>();
        foreach (string id in new[] { "rabbit", "cat", "fox", "sheep", "wolf", "dog" })
        {
            float s = IsolationSystem.ReactSeconds(id);
            seen[id] = s;
            Check(s is >= 0.8f and <= 2.0f, $"{id} 반응 {s:0.00}초 (0.8~2.0)");
        }
        Check(seen["sheep"] > seen["wolf"], "양이 늑대보다 오래 머뭇거린다");
        Check(seen["sheep"] > seen["fox"], "양이 여우보다 오래 머뭇거린다");
        Check(seen.Values.Distinct().Count() >= 5, $"서로 다른 값이 {seen.Values.Distinct().Count()}종류");
    }

    // ── §25 구속된 뒤의 발버둥도 캐릭터마다 다르다 ───────────────────
    private void CheckPerCharacterStruggle()
    {
        GD.Print("\n---------------- 캐릭터별 발버둥 ----------------");
        var s = new Dictionary<string, float>();
        foreach (string id in new[] { "rabbit", "cat", "fox", "sheep", "wolf", "dog" })
        {
            s[id] = IsolationSystem.StruggleOf(id);
            Check(s[id] is >= 0f and <= 1f, $"{id} 발버둥 {s[id]:0.00} (0~1)");
        }
        Check(s["sheep"] > s["fox"], "양이 여우보다 훨씬 심하게 몸부림친다");
        Check(s["rabbit"] > s["cat"], "토끼가 고양이보다 심하다");
        Check(!IsolationSystem.SettlesDown("sheep"), "양은 시간이 지나도 진정되지 않는다");
        Check(IsolationSystem.SettlesDown("wolf"), "늑대는 한 번 힘줘 보고 진정된다");
    }

    // ── §34 눕기 전까지는 취소할 수 있다 ─────────────────────────────
    private void CheckCancelWhileReacting()
    {
        GD.Print("\n---------------- 반응 중 취소 ----------------");
        Start();
        Place("dog", "core_room");
        Settle();

        var st = _sim.GetEmployeeState("dog");
        _sim.IsolateEmployee("dog");
        Tick();
        Check(st.Isolation == IsolationPhase.React, "아직 반응 중이다");
        Check(_sim.CancelIsolation("dog"), "취소가 받아들여졌다");
        Check(!st.Isolated, "격리가 풀렸다");
        Check(st.Isolation == IsolationPhase.None, $"단계가 지워졌다 ({st.Isolation})");
        Settle();
        Check(st.AssignedRoomId == "core_room", $"원래 작업실로 돌아갔다 ({st.AssignedRoomId})");
        Check(st.CurrentRoomId == "core_room", "실제로 원래 작업실에 있다");
    }

    private void CheckCancelWhileWalking()
    {
        GD.Print("\n---------------- 이동 중 취소 ----------------");
        Start();
        Place("fox", "storage_room");
        Settle();

        var st = _sim.GetEmployeeState("fox");
        _sim.IsolateEmployee("fox");
        for (float t = 0f; t < 10f && st.Isolation != IsolationPhase.Walking; t += Step) Tick();
        Check(st.Isolation == IsolationPhase.Walking, "격리실로 걸어가는 중이다");
        Check(_sim.CancelIsolation("fox"), "취소가 받아들여졌다");
        Check(st.Isolation == IsolationPhase.None, "단계가 지워졌다");
        Settle();
        Check(st.CurrentRoomId == "storage_room", $"원래 작업실로 되돌아왔다 ({st.CurrentRoomId})");
        Check(st.CurrentRoomId != "isolation_room", "격리실까지 가지 않았다");
    }

    private void CheckCancelAfterConfined()
    {
        GD.Print("\n---------------- 수용 후 해제 ----------------");
        Start();
        Place("wolf", "core_room");
        Settle();

        var st = _sim.GetEmployeeState("wolf");
        _sim.IsolateEmployee("wolf");
        for (float t = 0f; t < 60f && st.Isolation != IsolationPhase.InRoom; t += Step) Tick();
        Check(st.Isolation == IsolationPhase.InRoom, "수용되었다");
        Check(_sim.CancelIsolation("wolf"), "해제가 받아들여졌다");
        Check(!st.Isolated && st.Isolation == IsolationPhase.None, "격리 상태와 단계가 모두 풀렸다");
        Settle();
        Check(st.CurrentRoomId == "core_room", $"원래 작업실로 걸어 돌아왔다 ({st.CurrentRoomId})");
        Check(HasLog("격리 해제"), "해제가 기록되었다");
    }

    // ── §35 기절이 격리보다 우선이다 ─────────────────────────────────
    private void CheckFaintBeatsIsolation()
    {
        GD.Print("\n---------------- 걸어가던 중 기절 ----------------");
        Start();
        Place("sheep", "generator_room");
        Place("wolf", "generator_room");
        Settle();

        var st = _sim.GetEmployeeState("sheep");
        _sim.IsolateEmployee("sheep");
        for (float t = 0f; t < 10f && st.Isolation != IsolationPhase.Walking; t += Step) Tick();
        Check(st.Isolation == IsolationPhase.Walking, "격리실로 걸어가는 중이다");

        _sim.AddStress("sheep", 80f, "검사");
        Tick();
        Check(st.Incapacitated, "쓰러졌다");
        Check(st.Isolation == IsolationPhase.None, $"격리 절차가 중단되었다 ({st.Isolation})");
        Check(!st.Isolated, "격리 상태가 풀렸다");
        Check(!st.IsMoving && st.PathQueue.Count == 0, "쓰러진 뒤에는 스스로 걷지 않는다");
    }

    private void CheckFaintInsideKeepsIsolation()
    {
        GD.Print("\n---------------- 수용된 뒤 기절 ----------------");
        Start();
        Place("rabbit", "core_room");
        Settle();

        var st = _sim.GetEmployeeState("rabbit");
        _sim.IsolateEmployee("rabbit");
        for (float t = 0f; t < 60f && st.Isolation != IsolationPhase.InRoom; t += Step) Tick();
        Check(st.Isolation == IsolationPhase.InRoom, "수용되었다");

        _sim.AddStress("rabbit", 80f, "검사");
        Tick();
        Check(st.Isolation == IsolationPhase.InRoom, "격리실 안에서는 격리가 유지된다");
        Check(st.Isolated, "격리 상태가 유지된다");
        Check(!st.IsMoving, "스스로 의무실로 걸어가지 않는다");
    }

    // ── 두 명 동시 격리 ──────────────────────────────────────────────
    private void CheckTwoAtOnce()
    {
        GD.Print("\n---------------- 두 명 동시 격리 ----------------");
        Start();
        Place("cat", "core_room");
        Place("dog", "maintenance_room");
        Place("fox", "storage_room");
        Settle();

        // 침대는 두 개지만 data/config.tres 의 IsolationCapacity 는 1이다.
        // 밸런스 값을 건드리지 않고, 침대 두 개 경로가 실제로 도는지만 여기서 확인한다.
        int cap = Config.Instance.Data.IsolationCapacity;
        Config.Instance.Data.IsolationCapacity = 2;

        Check(_sim.IsolateEmployee("cat"), "첫 번째 격리");
        Check(_sim.IsolateEmployee("dog"), "두 번째 격리");
        Check(!_sim.IsolateEmployee("fox"), "정원 2를 넘기지 않는다");

        var a = _sim.GetEmployeeState("cat");
        var b = _sim.GetEmployeeState("dog");
        // 반응 시간이 다르므로 출발 순간도 다르다.
        Check(IsolationSystem.ReactSeconds("cat") != IsolationSystem.ReactSeconds("dog"),
            "두 사람의 반응 시간이 다르다");
        for (float t = 0f; t < 60f; t += Step)
        {
            Tick();
            if (a.Isolation == IsolationPhase.InRoom && b.Isolation == IsolationPhase.InRoom) break;
        }
        Check(a.Isolation == IsolationPhase.InRoom && b.Isolation == IsolationPhase.InRoom,
            "둘 다 제 발로 수용되었다");
        Check(a.CurrentRoomId == "isolation_room" && b.CurrentRoomId == "isolation_room", "둘 다 격리실에 있다");
        Check(!_sim.AssignToRoom("cat", "core_room"), "격리 중에는 배치할 수 없다");

        // 한 명만 풀어도 다른 한 명은 그대로 수용된 상태다(밴드가 서로 섞이지 않는다).
        Check(_sim.CancelIsolation("cat"), "한 명만 해제");
        Check(b.Isolated && b.Isolation == IsolationPhase.InRoom, "다른 한 명은 그대로 수용 상태다");

        Config.Instance.Data.IsolationCapacity = cap;
        Check(Config.Instance.Data.IsolationCapacity == cap, $"정원 설정을 원래대로 돌려놓았다 ({cap})");
    }

    // ── §40 반응만 보고 범인을 알 수 없다 ────────────────────────────
    private void CheckSaboteurLooksTheSame()
    {
        GD.Print("\n---------------- 방해자도 같게 보인다 ----------------");
        Start();
        string saboteur = GameState.Instance.SaboteurEmployeeId;
        Check(saboteur.Length > 0, $"오늘의 방해자가 정해졌다 ({saboteur})");

        // 반응 시간·발버둥은 캐릭터 고유값뿐이다 — 방해자 여부가 끼어들지 않는다.
        float react = IsolationSystem.ReactSeconds(saboteur);
        float struggle = IsolationSystem.StruggleOf(saboteur);
        Start();   // 다른 사람이 방해자가 되게 다시 뽑는다
        Check(Mathf.IsEqualApprox(react, IsolationSystem.ReactSeconds(saboteur)),
            "방해자가 바뀌어도 같은 캐릭터의 반응 시간은 그대로다");
        Check(Mathf.IsEqualApprox(struggle, IsolationSystem.StruggleOf(saboteur)),
            "발버둥 강도도 그대로다");

        // 방해자를 격리해도 절차가 다르지 않다.
        Place(saboteur, "core_room");
        Settle();
        var st = _sim.GetEmployeeState(saboteur);
        _sim.IsolateEmployee(saboteur);
        Check(st.Isolation == IsolationPhase.React, "방해자도 그 자리에서 먼저 반응한다");
        for (float t = 0f; t < 60f && st.Isolation != IsolationPhase.InRoom; t += Step) Tick();
        Check(st.Isolation == IsolationPhase.InRoom, "방해자도 제 발로 걸어가 수용된다");
    }

    // ── 도구 ─────────────────────────────────────────────────────────

    private void Start()
    {
        GameState.Instance.ResetRun(2);
        GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        _sim.ResetRun();
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void Place(string id, string roomId) => _sim.AssignToRoom(id, roomId);

    private void Settle(float seconds = 40f)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            Tick();
            bool allThere = _sim.GetActiveEmployeeIds().All(id =>
            {
                var st = _sim.GetEmployeeState(id);
                return st == null || !st.IsMoving;
            });
            if (allThere && t > 1f) return;
        }
    }

    private IEnumerable<string> Others(string except) =>
        _sim.GetActiveEmployeeIds().Where(x => x != except);

    private bool HasLog(string contains) =>
        EventLog.Instance.GetAllEntries().Any(e => e.Description.Contains(contains));

    private void Tick()
    {
        GameState.Instance.AdvanceDayTime(Step);
        _sim.Tick(Step);
    }

    private void Check(bool ok, string label)
    {
        if (ok) { _pass++; GD.Print($"  PASS  {label}"); }
        else { _fail++; GD.PrintErr($"  FAIL  {label}"); }
    }
}
