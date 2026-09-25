using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 기절한 직원이 의무실까지 가는 전 과정.
//
// 가장 중요한 규칙 하나: **의식을 잃은 직원은 단 한 발짝도 스스로 움직이지 않는다.**
// 예전에는 기절하는 순간 BeginPathTo(의무실) 를 불러서, 쓰러진 사람이 제 발로 복도를 걸어
// 의무실로 갔다. 지금은 쓰러진 그 자리에 그대로 누워 있고, 다른 직원이 실제로 와서 업거나
// 부축하거나 안아서 옮겨야만 의무실에 도착한다.
//
//   쓰러짐 → 바닥 → (같은 방 동료가 발견) → 상태 확인 → 관리자에게 전화
//          → 허가 / 거절 / 미응답 → 이송 → 침대에 눕힘 → 회복 → 운반자 복귀
//
// 판정은 전부 여기 있고, 화면(미니맵 경고 · CCTV 동작)은 이벤트를 받아 붙는다.
public enum FaintPhase
{
    None,
    Falling,           // 쓰러지는 중(연출)
    OnFloor,           // 바닥에 누워 있다 — 아직 아무도 오지 않았다
    BeingChecked,      // 동료가 다가와 상태를 살피는 중
    AwaitingDecision,  // 관리자에게 전화를 걸어 두고 답을 기다리는 중
    TransportDenied,   // 관리자가 "그대로 두라" 고 했다
    Transporting,      // 동료가 옮기는 중
    InMedicalBed,      // 침대에 눕히는 중(연출)
    Recovering,        // 침대에 누웠다 — 이제부터 회복 시간이 흐른다
}

// 운반 자세. 캐릭터마다 다르다(§26~34).
public enum CarryStyle
{
    Piggyback,   // 늑대 · 고양이 — 등에 업는다
    Shoulder,    // 양 · 토끼 — 어깨에 팔을 걸고 부축한다
    Bridal,      // 강아지 · 여우 — 두 팔로 안는다
}

public sealed class FaintRescueSystem
{
    // ── 연출 시간(초) ────────────────────────────────────────────────
    // 판정이 아니라 "화면에서 그 동작이 보이는 데 걸리는 시간"이다.
    public const float FallSeconds = 1.25f;      // 쓰러지는 데 걸리는 시간
    public const float ApproachSeconds = 1.1f;   // 놀라 달려와 옆에 앉기까지
    public const float CheckSeconds = 1.6f;      // 상태를 살피는 시간
    public const float LiftSeconds = 1.3f;       // 들어 올리는 데 걸리는 시간
    public const float BedHandoffSeconds = 1.8f; // 침대에 눕히는 데 걸리는 시간

    // 화면이 듣는 신호. 판정은 전부 이 안에서 끝나고, 밖에서는 보여 주기만 한다.
    public event Action<string> Fainted;              // 쓰러진 순간(미니맵 경고 · 쓰러지는 동작)
    public event Action<string, string> Responding;   // (구조자, 환자) 다가가기 시작
    public event Action<string, string> Lifting;      // (운반자, 환자) 들어 올리기 시작
    public event Action<string> BedReached;           // 환자가 침대에 눕혀졌다
    public event Action<string> Recovered;            // 환자가 깨어났다

    private FacilitySimulation _sim;
    private readonly RandomNumberGenerator _rng = new();

    public void Attach(FacilitySimulation sim)
    {
        _sim = sim;
        _rng.Randomize();
    }

    public void Reset()
    {
        foreach (string id in _sim?.GetEmployeeIds() ?? new List<string>())
        {
            var st = _sim.GetEmployeeState(id);
            if (st == null) continue;
            st.Faint = FaintPhase.None;
            st.FaintPhaseTimer = 0f;
            st.ResponderId = st.TransporterId = "";
            st.CarryingVictimId = "";
            st.TransportReturnRoomId = "";
            st.MedicalBedSpotId = "";
        }
    }

    // ── 쓰러진다 ──────────────────────────────────────────────────────

    // FacilitySimulation.CheckFaint 이 부른다. **여기서 이동을 시키지 않는다.**
    public void OnFainted(EmployeeState victim)
    {
        victim.Faint = FaintPhase.Falling;
        victim.FaintPhaseTimer = 0f;
        victim.ResponderId = victim.TransporterId = "";
        victim.MedicalBedSpotId = "";
        // 회복 시간은 침대에 눕기 전까지 흐르지 않는다(§3). 여기서 걸어 두지 않는다.
        victim.FaintRecoverTimer = 0f;
        // 쓰러지는 방향 — 뒤에 벽이나 설비가 있으면 앞으로 넘어진다(§7).
        victim.FellForward = PickFallForward(victim);
        // 걷던 중이었다면 그 자리에서 멈춘다. 의식이 없는 사람은 목적지가 없다.
        victim.IsMoving = false;
        victim.PathQueue.Clear();
        victim.TargetRoomId = victim.CurrentRoomId;
        victim.ElbowWaypoint = null;

        Fainted?.Invoke(victim.EmployeeId);
    }

    // 쓰러지는 방향. 지금은 방 안 위치로만 고른다 — 벽 쪽을 등지고 있으면 앞으로 넘어진다.
    private bool PickFallForward(EmployeeState victim)
    {
        var room = _sim?.GetRoomDef(victim.CurrentRoomId);
        if (room == null) return true;
        // 작업실 안쪽(벽 쪽)에 가까우면 앞으로, 가운데에 있으면 반반.
        return _rng.Randf() < 0.6f;
    }

    // ── 매 틱 ─────────────────────────────────────────────────────────

    public void Tick(float delta)
    {
        if (_sim == null) return;
        var cfg = Config.Instance?.Data;
        if (cfg == null) return;

        foreach (string id in _sim.GetEmployeeIds())
        {
            var st = _sim.GetEmployeeState(id);
            if (st == null || st.Faint == FaintPhase.None) continue;
            st.FaintPhaseTimer += delta;

            switch (st.Faint)
            {
                case FaintPhase.Falling:
                    if (st.FaintPhaseTimer >= FallSeconds) Enter(st, FaintPhase.OnFloor);
                    break;

                case FaintPhase.OnFloor:
                    TryFindResponder(st, dispatchedOnly: false);
                    break;

                case FaintPhase.TransportDenied:
                    // 관리자가 "그대로 두라" 고 했다. 옆에 있던 동료가 다시 전화를 걸지 않는다 —
                    // 그러면 거절할 때마다 같은 전화가 무한히 다시 온다.
                    // 관리자가 **그 방으로 직접 보낸** 구조자만 이 상태를 깬다.
                    TryFindResponder(st, dispatchedOnly: true);
                    break;

                case FaintPhase.BeingChecked:
                    TickCheck(st);
                    break;

                case FaintPhase.AwaitingDecision:
                    // 전화 쪽에서 Approve / Deny / NoAnswer 가 올 때까지 기다린다.
                    // 구조자가 사라졌으면(사망·격리) 처음부터 다시.
                    if (!Usable(st.ResponderId)) { st.ResponderId = ""; Enter(st, FaintPhase.OnFloor); }
                    break;

                case FaintPhase.Transporting:
                    TickTransport(st);
                    break;

                case FaintPhase.InMedicalBed:
                    if (st.FaintPhaseTimer >= BedHandoffSeconds) BeginRecovery(st, cfg);
                    break;

                case FaintPhase.Recovering:
                    TickRecovery(st, delta, cfg);
                    break;
            }
        }
    }

    private void Enter(EmployeeState st, FaintPhase phase)
    {
        st.Faint = phase;
        st.FaintPhaseTimer = 0f;
    }

    // ── 발견 ──────────────────────────────────────────────────────────

    // 같은 방에 쓸 만한 동료가 있으면 한 명만 구조자로 뽑는다(§11 · §45).
    private void TryFindResponder(EmployeeState victim, bool dispatchedOnly)
    {
        if (!string.IsNullOrEmpty(victim.ResponderId) && Usable(victim.ResponderId)) return;

        string best = "";
        bool bestDispatched = false;
        foreach (string id in _sim.GetActiveEmployeeIds())
        {
            if (id == victim.EmployeeId || !Usable(id)) continue;
            var c = _sim.GetEmployeeState(id);
            if (c.CurrentRoomId != victim.CurrentRoomId || c.IsMoving) continue;
            // 이미 다른 환자를 맡고 있으면 안 된다.
            if (!string.IsNullOrEmpty(c.CarryingVictimId)) continue;
            if (IsRespondingToSomeoneElse(id, victim.EmployeeId)) continue;

            // 관리자가 직접 이 방으로 보낸 사람인가(§23).
            // 그렇다면 "구조하라고 보낸 것"이 분명하므로 전화 승인 절차를 건너뛴다.
            bool dispatched = _sim.WasDispatchedForRescue(id, victim.CurrentRoomId);
            if (dispatchedOnly && !dispatched) continue;
            // 직접 보낸 사람이 있으면 그쪽이 우선이다.
            if (best.Length == 0 || (dispatched && !bestDispatched)) { best = id; bestDispatched = dispatched; }
        }
        if (best.Length == 0) return;

        victim.ResponderId = best;
        victim.RescueWasDispatched = bestDispatched;
        Enter(victim, FaintPhase.BeingChecked);
        Responding?.Invoke(best, victim.EmployeeId);
    }

    private void TickCheck(EmployeeState victim)
    {
        if (!Usable(victim.ResponderId)) { victim.ResponderId = ""; Enter(victim, FaintPhase.OnFloor); return; }
        if (victim.FaintPhaseTimer < ApproachSeconds + CheckSeconds) return;

        // 관리자가 보낸 구조자면 바로 옮긴다. 우연히 같은 방에 있던 동료면 먼저 물어본다.
        if (victim.RescueWasDispatched) { BeginTransport(victim, victim.ResponderId); return; }
        Enter(victim, FaintPhase.AwaitingDecision);
        TransportRequested?.Invoke(victim.ResponderId, victim.EmployeeId);
    }

    // 구조자가 관리자에게 전화를 걸어야 한다 — IncomingCallDirector 가 받는다(§15).
    public event Action<string, string> TransportRequested;   // (구조자, 환자)

    // ── 관리자의 답 ───────────────────────────────────────────────────

    // "의무실로 이송하십시오."
    public bool Approve(string victimId)
    {
        var v = _sim?.GetEmployeeState(victimId);
        if (v == null || v.Faint != FaintPhase.AwaitingDecision) return false;
        if (!Usable(v.ResponderId)) return false;
        BeginTransport(v, v.ResponderId);
        return true;
    }

    // "그대로 두십시오." — 옮기지 않는다. 환자는 계속 바닥에 있다(§18).
    public void Deny(string victimId)
    {
        var v = _sim?.GetEmployeeState(victimId);
        if (v == null || v.Faint != FaintPhase.AwaitingDecision) return;
        v.ResponderId = "";
        Enter(v, FaintPhase.TransportDenied);
    }

    // 아예 받지 않았다 → 구조자가 알아서 옮긴다(§20 · §21).
    public void NoAnswer(string victimId)
    {
        var v = _sim?.GetEmployeeState(victimId);
        if (v == null || v.Faint != FaintPhase.AwaitingDecision) return;
        if (!Usable(v.ResponderId)) { Enter(v, FaintPhase.OnFloor); return; }
        BeginTransport(v, v.ResponderId);
    }

    // 거절한 뒤에 마음을 바꿨다 — 그 방 직원에게 직접 지시한다(§19).
    // 반환값 = 실제로 이송이 시작됐는가.
    public bool OrderTransport(string responderId)
    {
        var r = _sim?.GetEmployeeState(responderId);
        if (r == null || !Usable(responderId)) return false;
        var victim = FindUnmovedVictimIn(r.CurrentRoomId);
        if (victim == null) return false;
        victim.ResponderId = responderId;
        BeginTransport(victim, responderId);
        return true;
    }

    // 그 방에 아직 옮겨지지 않은 기절자가 있는가(전화 메뉴가 선택지를 띄울지 정할 때 쓴다).
    public EmployeeState FindUnmovedVictimIn(string roomId)
    {
        if (_sim == null || string.IsNullOrEmpty(roomId)) return null;
        foreach (string id in _sim.GetEmployeeIds())
        {
            var st = _sim.GetEmployeeState(id);
            if (st == null || st.CurrentRoomId != roomId) continue;
            if (st.Faint is FaintPhase.OnFloor or FaintPhase.TransportDenied
                or FaintPhase.BeingChecked or FaintPhase.AwaitingDecision)
                return st;
        }
        return null;
    }

    // ── 이송 ──────────────────────────────────────────────────────────

    private void BeginTransport(EmployeeState victim, string transporterId)
    {
        var t = _sim.GetEmployeeState(transporterId);
        if (t == null) return;

        victim.TransporterId = transporterId;
        victim.ResponderId = transporterId;
        t.CarryingVictimId = victim.EmployeeId;
        // 눕히고 나면 돌아갈 자리. 이송 중에 관리자가 새 배치를 내리면 그쪽이 이긴다(§40).
        t.TransportReturnRoomId = string.IsNullOrEmpty(t.AssignedRoomId) ? t.CurrentRoomId : t.AssignedRoomId;
        Enter(victim, FaintPhase.Transporting);
        Lifting?.Invoke(transporterId, victim.EmployeeId);

        EventLog.Instance?.LogEvent(LogEventType.Neglect, transporterId, victim.CurrentRoomId,
            $"{Name(transporterId)}가 {Name(victim.EmployeeId)}를 의무실로 이송");
    }

    private void TickTransport(EmployeeState victim)
    {
        string tid = victim.TransporterId;
        if (!Usable(tid))
        {
            // 운반자가 사라졌다 — 환자는 그 자리에 남는다.
            var dropped = _sim.GetEmployeeState(tid);
            if (dropped != null) dropped.CarryingVictimId = "";
            victim.TransporterId = victim.ResponderId = "";
            Enter(victim, FaintPhase.OnFloor);
            return;
        }

        var t = _sim.GetEmployeeState(tid);
        // 들어 올리는 동안은 아직 걷지 않는다.
        if (victim.FaintPhaseTimer < LiftSeconds) return;

        // 운반자가 **자기 발로** 의무실로 간다. 환자에게는 길을 주지 않는다.
        if (t.CurrentRoomId != FacilitySimulation.MedicalRoomIdPublic && !t.IsMoving)
        {
            _sim.SendTransporterToMedical(tid);
            return;
        }

        // 환자는 운반자에게 붙어 다닌다 — 위치만 따라간다(경로 계산 없음).
        victim.Position = t.Position;
        victim.IsMoving = false;
        _sim.SyncCarriedRoom(victim, t.CurrentRoomId);

        if (t.CurrentRoomId != FacilitySimulation.MedicalRoomIdPublic || t.IsMoving) return;

        // 도착 — 빈 침대를 하나 잡는다. 없으면 잡힐 때까지 기다린다(§44).
        string bed = ClaimBed(victim);
        if (string.IsNullOrEmpty(bed)) return;
        victim.MedicalBedSpotId = bed;
        Enter(victim, FaintPhase.InMedicalBed);
    }

    // 의무실 침대. 실제 씬의 MedicalBedSpot 수와 같아야 한다(§37 · §44).
    private static readonly string[] Beds = { "MedicalBedSpot1", "MedicalBedSpot2" };

    private string ClaimBed(EmployeeState victim)
    {
        foreach (string bed in Beds)
        {
            bool taken = false;
            foreach (string id in _sim.GetEmployeeIds())
            {
                if (id == victim.EmployeeId) continue;
                var o = _sim.GetEmployeeState(id);
                if (o != null && o.MedicalBedSpotId == bed) { taken = true; break; }
            }
            if (!taken) return bed;
        }
        return "";
    }

    private void BeginRecovery(EmployeeState victim, ConfigData cfg)
    {
        // 여기서부터 회복 시간이 흐른다 — 복도에서 실려 오는 동안은 줄지 않았다(§3 · §39).
        victim.FaintRecoverTimer = cfg.StressFaintRecoverySeconds;
        Enter(victim, FaintPhase.Recovering);
        BedReached?.Invoke(victim.EmployeeId);

        EventLog.Instance?.LogEvent(LogEventType.Neglect, victim.EmployeeId,
            FacilitySimulation.MedicalRoomIdPublic,
            $"{Name(victim.EmployeeId)} — 의무실 도착, 회복까지 {cfg.StressFaintRecoverySeconds:0}초");
        RoomEffectStats.Pulse(FacilitySimulation.MedicalRoomIdPublic);

        // 운반자는 손을 떼고 자기 작업실로 돌아간다(§40).
        var t = _sim.GetEmployeeState(victim.TransporterId);
        if (t != null)
        {
            t.CarryingVictimId = "";
            // 이송 중에 관리자가 새 배치를 내렸으면 그쪽이 이긴다.
            string back = !string.IsNullOrEmpty(t.AssignedRoomId) ? t.AssignedRoomId : t.TransportReturnRoomId;
            if (!string.IsNullOrEmpty(back)) _sim.SendTransporterHome(t.EmployeeId, back);
            t.TransportReturnRoomId = "";
        }
        victim.TransporterId = "";
    }

    private void TickRecovery(EmployeeState victim, float delta, ConfigData cfg)
    {
        if (cfg.StressFaintRecoverySeconds <= 0f) return;
        victim.FaintRecoverTimer -= delta;
        if (victim.FaintRecoverTimer > 0f) return;

        victim.Incapacitated = false;
        victim.Faint = FaintPhase.None;
        victim.FaintPhaseTimer = 0f;
        victim.MedicalBedSpotId = "";
        victim.Stress = Mathf.Clamp(cfg.StressAfterRecovery, cfg.StressMin, cfg.StressFaintFrom - 1f);
        EventLog.Instance?.LogEvent(LogEventType.Neglect, victim.EmployeeId, victim.CurrentRoomId,
            $"{Name(victim.EmployeeId)} 회복 — 스트레스 {victim.Stress:0}, 근무 복귀",
            detail: LogDetail.Recovered);
        Recovered?.Invoke(victim.EmployeeId);
        _sim.SendRecoveredHome(victim);
    }

    // ── 조회 ──────────────────────────────────────────────────────────

    // 이 캐릭터는 환자를 어떻게 옮기는가(§26 · §29 · §32).
    public static CarryStyle StyleOf(string employeeId) => employeeId switch
    {
        "wolf" or "cat" => CarryStyle.Piggyback,
        "sheep" or "rabbit" => CarryStyle.Shoulder,
        _ => CarryStyle.Bridal,          // dog · fox
    };

    // 운반 속도 배율 — 늑대·강아지·여우 > 고양이 > 토끼 > 양 (§35).
    // 차이는 CCTV 에서 느껴질 만큼만 둔다. 밸런스를 흔들 정도로 벌리지 않는다.
    public static float CarrySpeedScale(string employeeId) => employeeId switch
    {
        "wolf" or "dog" or "fox" => 1.0f,
        "cat" => 0.88f,
        "rabbit" => 0.78f,
        _ => 0.66f,                      // sheep
    };

    // 눕히고 돌아갈 때 뛰는가(§41 · §42).
    public static bool RunsBack(string employeeId) => employeeId is "sheep" or "rabbit";

    // 지금 바닥에 쓰러져 있는가(미니맵·CCTV 가 묻는다).
    public static bool IsDown(EmployeeState st) =>
        st != null && st.Faint is FaintPhase.Falling or FaintPhase.OnFloor
            or FaintPhase.BeingChecked or FaintPhase.AwaitingDecision or FaintPhase.TransportDenied;

    private bool IsRespondingToSomeoneElse(string id, string exceptVictimId)
    {
        foreach (string other in _sim.GetEmployeeIds())
        {
            if (other == exceptVictimId) continue;
            var o = _sim.GetEmployeeState(other);
            if (o != null && (o.ResponderId == id || o.TransporterId == id)) return true;
        }
        return false;
    }

    private bool Usable(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var st = _sim?.GetEmployeeState(id);
        return st is { Alive: true, Isolated: false, Incapacitated: false };
    }

    private string Name(string id) => _sim?.GetEmployeeDef(id)?.Codename ?? id;
}
