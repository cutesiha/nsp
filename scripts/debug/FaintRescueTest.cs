using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Debug;

// 기절 → 발견 → 이송 → 침대 → 회복 흐름 검사.
//
//   godot --headless --path . res://scenes/debug/FaintRescueTest.tscn
//
// 제일 중요한 것 하나: **의식을 잃은 직원이 스스로 한 발짝도 움직이지 않는가.**
public partial class FaintRescueTest : Node
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
        GD.Print("################ 기절 구조 흐름 검사 ################");

        CheckVictimNeverWalks();
        CheckAlone();
        CheckDeny();
        CheckDenyThenOrder();
        CheckNoAnswer();
        CheckDispatchedRescuer();
        CheckRecoveryTimer();
        CheckTwoVictims();
        CheckCarryStyles();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A · O : 기절자는 절대 스스로 걷지 않는다 ─────────────────────
    private void CheckVictimNeverWalks()
    {
        GD.Print("\n---------------- 기절자는 스스로 걷지 않는다 ----------------");
        Start();
        Place("sheep", "maintenance_room");
        Place("wolf", "maintenance_room");
        Settle();

        Faint("sheep");
        var v = _sim.GetEmployeeState("sheep");
        string fellIn = v.CurrentRoomId;
        Check(v.Faint == FaintPhase.Falling, $"쓰러지는 중이다 ({v.Faint})");
        Check(!v.IsMoving && v.PathQueue.Count == 0, "경로가 주어지지 않았다");

        // 관리자가 배치를 눌러도 움직이지 않는다.
        bool moved = _sim.AssignToRoom("sheep", "core_room");
        Check(!moved, "배치 지시로도 기절자는 움직이지 않는다");

        // 이송이 시작되기 전까지는 쓰러진 방을 벗어나지 않는다.
        bool leftBeforeCarry = false;
        for (float t = 0f; t < 6f; t += Step)
        {
            Tick();
            if (v.Faint == FaintPhase.Transporting) break;
            if (v.CurrentRoomId != fellIn) leftBeforeCarry = true;
            if (v.IsMoving) leftBeforeCarry = true;
        }
        Check(!leftBeforeCarry, $"이송 전까지 {fellIn} 을 벗어나지 않는다");
        Check(v.Faint == FaintPhase.AwaitingDecision || v.Faint == FaintPhase.BeingChecked,
            $"동료가 발견해 확인 중이다 ({v.Faint})");
        Check(v.ResponderId == "wolf", $"같은 방 동료가 구조자로 뽑혔다 ({v.ResponderId})");

        // 승인 → 이송. 이 동안에도 환자는 자기 경로가 없다.
        _sim.Rescue.Approve("sheep");
        Check(v.Faint == FaintPhase.Transporting, "승인하면 이송이 시작된다");
        Check(v.TransporterId == "wolf", "운반자가 지정된다");

        bool victimPathed = false;
        for (float t = 0f; t < 60f; t += Step)
        {
            Tick();
            if (v.PathQueue.Count > 0 || v.IsMoving) victimPathed = true;
            if (v.Faint == FaintPhase.Recovering) break;
        }
        Check(!victimPathed, "이송 내내 환자에게 경로가 생기지 않는다");
        Check(v.CurrentRoomId == "medical_room", $"의무실에 도착했다 ({v.CurrentRoomId})");
        Check(v.Faint == FaintPhase.Recovering, $"침대에 눕혀져 회복 중이다 ({v.Faint})");
        Check(!string.IsNullOrEmpty(v.MedicalBedSpotId), $"침대를 잡았다 ({v.MedicalBedSpotId})");
    }

    // ── M : 혼자 쓰러지면 아무 일도 일어나지 않는다 ──────────────────
    private void CheckAlone()
    {
        GD.Print("\n---------------- 혼자 쓰러졌을 때 ----------------");
        Start();
        Place("sheep", "storage_room");
        foreach (string id in Others("sheep")) Place(id, "core_room");
        Settle();

        Faint("sheep");
        var v = _sim.GetEmployeeState("sheep");
        Run(20f);
        Check(v.Faint == FaintPhase.OnFloor, $"바닥에 그대로 있다 ({v.Faint})");
        Check(v.CurrentRoomId == "storage_room", "쓰러진 방을 벗어나지 않는다");
        Check(string.IsNullOrEmpty(v.ResponderId), "구조자가 없다");
        Check(v.FaintRecoverTimer <= 0f, "회복 시간이 흐르지 않는다");
    }

    // ── J : 명시적 거절 ──────────────────────────────────────────────
    private void CheckDeny()
    {
        GD.Print("\n---------------- 거절하면 옮기지 않는다 ----------------");
        var v = SetupWaiting();
        _sim.Rescue.Deny("sheep");
        Check(v.Faint == FaintPhase.TransportDenied, $"거절 상태가 남는다 ({v.Faint})");
        Run(15f);
        Check(v.CurrentRoomId == "maintenance_room", "환자가 그 방 바닥에 그대로 있다");
        Check(v.Faint != FaintPhase.Recovering, "회복하지 않는다");
    }

    // ── K : 거절 뒤 직접 지시 ────────────────────────────────────────
    private void CheckDenyThenOrder()
    {
        GD.Print("\n---------------- 거절 뒤 다시 지시 ----------------");
        var v = SetupWaiting();
        _sim.Rescue.Deny("sheep");
        Run(4f);

        // 그 방에 아직 옮겨지지 않은 기절자가 있으므로 선택지가 떠야 한다.
        Check(_sim.Rescue.FindUnmovedVictimIn("maintenance_room") != null,
            "그 방에 아직 이송되지 않은 기절자가 있다");
        Check(_sim.Rescue.OrderTransport("wolf"), "직접 지시하면 이송이 시작된다");
        Check(v.Faint == FaintPhase.Transporting, $"이송 중이다 ({v.Faint})");

        RunUntilRecovering(v, 60f);
        Check(v.Faint == FaintPhase.Recovering, "의무실에 도착해 회복 중이다");
    }

    // ── L : 전화를 아예 받지 않음 ────────────────────────────────────
    private void CheckNoAnswer()
    {
        GD.Print("\n---------------- 전화 미응답 ----------------");
        var v = SetupWaiting();
        _sim.Rescue.NoAnswer("sheep");
        Check(v.Faint == FaintPhase.Transporting, $"직원이 알아서 옮긴다 ({v.Faint})");
        RunUntilRecovering(v, 60f);
        Check(v.Faint == FaintPhase.Recovering, "의무실에 도착한다");
    }

    // ── N : 플레이어가 구조자를 보냈을 때 ────────────────────────────
    private void CheckDispatchedRescuer()
    {
        GD.Print("\n---------------- 구조자를 직접 보냈을 때 ----------------");
        Start();
        Place("sheep", "storage_room");
        foreach (string id in Others("sheep")) Place(id, "core_room");
        Settle();
        Faint("sheep");
        Run(3f);

        var v = _sim.GetEmployeeState("sheep");
        Check(v.Faint == FaintPhase.OnFloor, "아직 아무도 없다");

        // 관리자가 건강한 직원을 그 방으로 보낸다 = 구조하라는 뜻.
        _sim.AssignToRoom("wolf", "storage_room");
        bool asked = false;
        for (float t = 0f; t < 40f; t += Step)
        {
            Tick();
            if (v.Faint == FaintPhase.AwaitingDecision) asked = true;
            if (v.Faint == FaintPhase.Transporting) break;
        }
        Check(!asked, "전화로 다시 묻지 않는다");
        Check(v.Faint == FaintPhase.Transporting, $"도착하자마자 이송을 시작한다 ({v.Faint})");
    }

    // ── V : 회복 타이머는 침대에서만 흐른다 ──────────────────────────
    private void CheckRecoveryTimer()
    {
        GD.Print("\n---------------- 회복 타이머 시작 시점 ----------------");
        var v = SetupWaiting();
        Check(v.FaintRecoverTimer <= 0f, "쓰러진 동안에는 회복 시간이 0 이다");
        _sim.Rescue.Approve("sheep");

        float duringCarry = -1f;
        for (float t = 0f; t < 60f; t += Step)
        {
            Tick();
            if (v.Faint == FaintPhase.Transporting) duringCarry = Mathf.Max(duringCarry, v.FaintRecoverTimer);
            if (v.Faint == FaintPhase.Recovering) break;
        }
        Check(duringCarry <= 0f, $"이송 중에도 회복 시간이 흐르지 않는다 (최대 {duringCarry:0.00})");
        float cfg = Config.Instance.Data.StressFaintRecoverySeconds;
        Check(v.FaintRecoverTimer > cfg * 0.9f,
            $"침대에 눕는 순간 회복 시간이 시작된다 ({v.FaintRecoverTimer:0.0} / {cfg:0})");

        // 끝까지 돌려 회복을 확인한다.
        for (float t = 0f; t < cfg + 10f; t += Step)
        {
            Tick();
            if (!v.Incapacitated) break;
        }
        Check(!v.Incapacitated, "회복한다");
        Check(v.Faint == FaintPhase.None, "기절 단계가 정리된다");
    }

    // ── Y : 동시에 두 명 ─────────────────────────────────────────────
    private void CheckTwoVictims()
    {
        GD.Print("\n---------------- 두 명이 동시에 쓰러졌을 때 ----------------");
        Start();
        Place("sheep", "maintenance_room");
        Place("rabbit", "maintenance_room");
        Place("wolf", "maintenance_room");
        Place("dog", "maintenance_room");
        Settle();

        Faint("sheep");
        Faint("rabbit");
        var a = _sim.GetEmployeeState("sheep");
        var b = _sim.GetEmployeeState("rabbit");
        Run(5f);

        Check(a.ResponderId != b.ResponderId || a.ResponderId.Length == 0,
            $"한 사람이 두 환자를 동시에 맡지 않는다 ({a.ResponderId} / {b.ResponderId})");

        _sim.Rescue.NoAnswer("sheep");
        _sim.Rescue.NoAnswer("rabbit");
        for (float t = 0f; t < 80f; t += Step)
        {
            Tick();
            if (a.Faint == FaintPhase.Recovering && b.Faint == FaintPhase.Recovering) break;
        }
        Check(a.Faint == FaintPhase.Recovering && b.Faint == FaintPhase.Recovering,
            $"둘 다 의무실에 도착한다 ({a.Faint} / {b.Faint})");
        Check(a.MedicalBedSpotId != b.MedicalBedSpotId,
            $"같은 침대를 쓰지 않는다 ({a.MedicalBedSpotId} / {b.MedicalBedSpotId})");
    }

    // ── P·Q·R : 운반 자세 ────────────────────────────────────────────
    private void CheckCarryStyles()
    {
        GD.Print("\n---------------- 캐릭터별 운반 자세 ----------------");
        Check(FaintRescueSystem.StyleOf("wolf") == CarryStyle.Piggyback, "늑대 — 업기");
        Check(FaintRescueSystem.StyleOf("cat") == CarryStyle.Piggyback, "고양이 — 업기");
        Check(FaintRescueSystem.StyleOf("sheep") == CarryStyle.Shoulder, "양 — 어깨 부축");
        Check(FaintRescueSystem.StyleOf("rabbit") == CarryStyle.Shoulder, "토끼 — 어깨 부축");
        Check(FaintRescueSystem.StyleOf("dog") == CarryStyle.Bridal, "강아지 — 공주님 안기");
        Check(FaintRescueSystem.StyleOf("fox") == CarryStyle.Bridal, "여우 — 공주님 안기");

        // 속도: 늑대·강아지·여우 > 고양이 > 토끼 > 양
        float wolf = FaintRescueSystem.CarrySpeedScale("wolf");
        float cat = FaintRescueSystem.CarrySpeedScale("cat");
        float rabbit = FaintRescueSystem.CarrySpeedScale("rabbit");
        float sheep = FaintRescueSystem.CarrySpeedScale("sheep");
        Check(wolf > cat && cat > rabbit && rabbit > sheep,
            $"운반 속도 순서 (늑대 {wolf:0.00} > 고양이 {cat:0.00} > 토끼 {rabbit:0.00} > 양 {sheep:0.00})");
        Check(FaintRescueSystem.RunsBack("sheep") && FaintRescueSystem.RunsBack("rabbit"),
            "양·토끼는 뛰어서 복귀한다");
        Check(!FaintRescueSystem.RunsBack("wolf") && !FaintRescueSystem.RunsBack("fox"),
            "나머지는 걸어서 복귀한다");
    }

    // ── 도우미 ──────────────────────────────────────────────────────

    // 전화를 걸어 둔 상태(AwaitingDecision)까지 만들어 준다.
    private EmployeeState SetupWaiting()
    {
        Start();
        Place("sheep", "maintenance_room");
        Place("wolf", "maintenance_room");
        Settle();
        Faint("sheep");
        var v = _sim.GetEmployeeState("sheep");
        for (float t = 0f; t < 12f; t += Step)
        {
            Tick();
            if (v.Faint == FaintPhase.AwaitingDecision) break;
        }
        return v;
    }

    private void RunUntilRecovering(EmployeeState v, float seconds)
    {
        for (float t = 0f; t < seconds; t += Step)
        {
            Tick();
            if (v.Faint == FaintPhase.Recovering) return;
        }
    }

    private void Start()
    {
        GameState.Instance.ResetRun(2);   // 스트레스가 열리는 날
        GameState.Instance.AssignRandomSaboteur(_sim.GetActiveEmployeeIds());
        EventLog.Instance.ClearAll();
        IncidentTracker.Reset();
        _sim.ResetRun();
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
    }

    private void Place(string id, string roomId) => _sim.AssignToRoom(id, roomId);

    // 배치한 사람들이 실제로 그 방에 도착할 때까지 기다린다.
    // 도착 전에 기절시키면 아직 중앙 제어실에 있어서 "같은 방" 판정이 전부 어긋난다.
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

    // 스트레스를 한계까지 올려 실제 경로(AddStress → CheckFaint)로 기절시킨다.
    private void Faint(string id) => _sim.AddStress(id, 80f, "검사");

    private void Tick()
    {
        GameState.Instance.AdvanceDayTime(Step);
        _sim.Tick(Step);
    }

    private void Run(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step) Tick();
    }

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
