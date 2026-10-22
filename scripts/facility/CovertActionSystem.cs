using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 결번 개체가 **감시 공백을 실제로 이용하는** 계통.
//
// 전력은 3포인트지만 발전실 사고 · 전력 조작으로 2 로 줄면 LIGHTING · CCTV · BARRIER 중
// 하나는 반드시 꺼야 한다.
// 관리자가 귀신을 막으려고 차폐에 전력을 돌리는 순간 감시에 구멍이 뚫리고,
// 결번 개체는 그 구멍을 쓴다.
//
//   LIGHTING OFF → 미니맵에서 위치가 사라진다 → **다른 방으로 실제로 걸어간다**
//   CCTV OFF     → 행동을 볼 수 없다        → **봉쇄 계통을 교란한다**
//
// 지켜야 하는 선이 둘 있다.
//
//   ① 전력이 꺼졌다는 **이유만으로** 사고나 살인이 일어나지 않는다.
//      모든 것은 실제 위치 · 실제 경로 · 실제 체류 시간을 거친다. 차폐문에 막히면
//      정말로 못 간다. 순간이동은 없다(지시서 §0 · §3-2 · §5).
//
//   ② 내부에서 일어난 일과 플레이어가 확보한 자료는 다르다.
//      이 시스템은 내부 진실을 전부 기록하지만, 조사자료는 **관리자가 실제로 본 경우에만**
//      생긴다(FacilitySimulation.NoteWorkInterruption 이 그 판정을 한다. 지시서 §2).
//
// 살인 경로는 여기 하나뿐이다. 예전의 TickPowerLossMurder(조명 OFF → 아무나 즉사)와
// TickKillAttempt(같은 방 단둘)는 이 FSM 의 두 입구로 합쳐졌다 — 같은 방에 이미 있으면
// Moving 을 건너뛰고 바로 Preparing 으로 들어간다.
public enum CovertPhase
{
    Idle,          // 평소처럼 자기 자리에서 일한다
    SeekingTarget, // 감시 공백을 확인하고 갈 곳을 고른다
    Moving,        // 실제 복도를 따라 걸어간다(차폐문에 막히면 실패한다)
    Preparing,     // 피해자와 같은 방. 조건이 유지되는 동안만 쌓인다
    Attacking,     // 실행
    Escape,        // 원래 근무지로 되돌아간다
}

public sealed class CovertActionSystem
{
    // ── 감시 공백 누적 ───────────────────────────────────────────────────
    //
    // 전력을 다시 켜도 **0 으로 초기화하지 않는다.** 스위치를 짧게 껐다 켰다 반복해서
    // 시스템을 통째로 무력화할 수 없어야 하기 때문이다(지시서 §7). 대신 천천히 줄어든다 —
    // 관리자가 충분히 오래 정상 운용하면 위험은 정상적으로 해소된다.
    public float DarkSeconds { get; private set; }
    public float BlindSeconds { get; private set; }
    private const float RecoverRate = 0.75f;   // 복구 중에는 이 배속으로 줄어든다

    public CovertPhase Phase { get; private set; } = CovertPhase.Idle;
    public string TargetRoomId { get; private set; } = "";
    public string VictimId { get; private set; } = "";
    public string HomeRoomId { get; private set; } = "";
    public float PrepareSeconds { get; private set; }
    public float PrepareNeeded { get; private set; }

    // 오늘의 집계(검증 · 디버그 전용. 플레이어에게 보여 주지 않는다).
    public int CovertMovesStarted { get; private set; }
    public int CovertMovesArrived { get; private set; }
    public int CovertMovesBlocked { get; private set; }
    public int KillAttempts { get; private set; }
    public int KillsCommitted { get; private set; }
    public int AbortedByPlayer { get; private set; }
    public int ContainmentTampers { get; private set; }
    public int CaughtOnCamera { get; private set; }
    public readonly List<string> Trace = new();

    // 봉쇄 교란 진행.
    private float _tamperWork;
    private float _tamperNotifyTimer;
    private string _tamperRoomId = "";
    // 현행범으로 잡힌 뒤의 자숙 시간. 없으면 1.5초마다 다시 손을 댔다가 다시 잡히는
    // 쳇바퀴가 돌아 같은 장면이 몇 번이고 기록된다.
    private float _caughtCooldown;

    // 이번 은밀 이동의 목적. 살인만이 아니다 — 자기 방에서 손댈 수 없을 때
    // **봉쇄 계통이 있는 다른 방까지 걸어가서** 교란하고 돌아오기도 한다(지시서 §3-1).
    private enum Purpose { None, Kill, Tamper }
    private Purpose _purpose = Purpose.None;
    // 교란하러 나가 있는 중인가 / 나갈 때의 교란 횟수(끝났는지 판정) / 나가 있은 시간.
    private bool _awayForTamper;
    private int _tampersAtDeparture;
    private float _awaySeconds;

    // 다음 기회 탐색까지의 짧은 쉼(매 프레임 후보를 훑지 않게).
    private float _seekCooldown;
    private float _blockedSeconds;
    private float _signTimer;
    // 조명 복구 때 "포기할지" 는 이동 한 건에 한 번만 굴린다.
    private bool _lightAbortRolled;

    private readonly RandomNumberGenerator _rng = new();

    public CovertActionSystem() => _rng.Randomize();

    public void Reset()
    {
        DarkSeconds = BlindSeconds = 0f;
        Phase = CovertPhase.Idle;
        TargetRoomId = VictimId = HomeRoomId = "";
        PrepareSeconds = PrepareNeeded = 0f;
        CovertMovesStarted = CovertMovesArrived = CovertMovesBlocked = 0;
        KillAttempts = KillsCommitted = AbortedByPlayer = ContainmentTampers = CaughtOnCamera = 0;
        _tamperWork = _tamperNotifyTimer = _caughtCooldown = 0f;
        _tamperRoomId = "";
        _seekCooldown = _blockedSeconds = _signTimer = 0f;
        _lightAbortRolled = false;
        _purpose = Purpose.None;
        _awayForTamper = false;
        _tampersAtDeparture = 0;
        _awaySeconds = 0f;
        Trace.Clear();
    }

    // ── 진행 ─────────────────────────────────────────────────────────────

    public void Tick(float delta, FacilitySimulation sim, OpsProfileDef ops)
    {
        if (sim == null || GameState.Instance?.CurrentPhase != GamePhase.Live) return;

        TickBlindness(delta);

        string saboteurId = GameState.Instance.SaboteurEmployeeId;
        var sab = string.IsNullOrEmpty(saboteurId) ? null : sim.GetEmployeeState(saboteurId);

        // 격리 · 사망 · 기절 — 아무것도 할 수 없다. 진행 중이던 것도 그 자리에서 끝난다.
        if (sab is not { Alive: true, Isolated: false, Incapacitated: false })
        {
            if (Phase != CovertPhase.Idle) Abort(sim, saboteurId, "결번 무력화");
            return;
        }
        if (!DayFeatures.AutoIncidentsEnabled) return;

        if (string.IsNullOrEmpty(HomeRoomId)) HomeRoomId = sab.AssignedRoomId;

        TickContainment(delta, sim, sab, ops);
        TickMurderPlan(delta, sim, sab, ops);
    }

    // 지금 관리자가 무엇을 못 보고 있는가.
    private void TickBlindness(float delta)
    {
        var gs = GameState.Instance;
        bool lightOff = !gs.IsConsumerPowered(PowerConsumer.Lighting);
        bool cctvOff = !gs.IsCctvOperational();
        DarkSeconds = lightOff ? DarkSeconds + delta : Mathf.Max(0f, DarkSeconds - delta * RecoverRate);
        BlindSeconds = cctvOff ? BlindSeconds + delta : Mathf.Max(0f, BlindSeconds - delta * RecoverRate);
    }

    // ── CCTV OFF : 봉쇄 계통 교란 ────────────────────────────────────────
    //
    // 이것이 **이상 개체 출몰의 원인**이다(지시서 §1). 결번 개체는 정상 업무를 잠시 멈추고
    // 설비에 붙는다 — 그 동안 그 작업실의 복구량이 실제로 줄어든다(WorkBlockedUntil).
    //
    // 관리자가 도중에 CCTV 를 켜고 **그 방을 보면** 행동이 조사자료가 되고, 결번은 손을 뗀다.
    // 보지 않았다면 남는 것은 "복구가 기대보다 덜 됐다" 와, 한참 뒤의 원인 불명 설비 이상뿐이다.
    private void TickContainment(float delta, FacilitySimulation sim, EmployeeState sab, OpsProfileDef ops)
    {
        if (_caughtCooldown > 0f) { _caughtCooldown -= delta; _tamperWork = 0f; return; }
        int budget = ops?.ContainmentTampersPerDay ?? 0;
        if (budget <= 0 || ContainmentTampers >= budget) { _tamperWork = 0f; return; }
        // 살인 동선을 타는 중에는 설비를 만지지 않는다 — 한 번에 하나만 한다.
        if (Phase is not (CovertPhase.Idle or CovertPhase.SeekingTarget)) { _tamperWork = 0f; return; }

        string room = sab.CurrentRoomId;
        bool eligible = !sab.IsMoving
                        && sim.IsRoomActive(room)
                        && ContainmentRooms(ops).Contains(room)
                        && !sim.HasRepairPending(room);

        // **시작 조건과 계속 조건을 나눈다.**
        //
        // 시작하려면 CCTV 가 충분히 오래 꺼져 있어야 한다. 하지만 일단 손을 대기 시작하면
        // 전원이 들어왔다는 이유만으로 즉시 그만두지 않는다 — 그러면 관리자가 화면을
        // 띄울 틈도 없이 모든 흔적이 사라져, "행동이 진행 중인 장면을 관측" 할 기회
        // 자체가 없어진다(지시서 §4-3). 멈추는 것은 **실제로 관측당했을 때**다.
        bool started = _tamperWork > 0f && _tamperRoomId == room;
        if (!eligible)
        {
            if (_tamperWork > 0f) _tamperWork = Mathf.Max(0f, _tamperWork - delta);
            return;
        }
        if (!started && BlindSeconds < (ops?.ContainmentTamperAfterSeconds ?? 8f))
        {
            if (_tamperWork > 0f) _tamperWork = Mathf.Max(0f, _tamperWork - delta);
            return;
        }

        if (_tamperRoomId != room) { _tamperRoomId = room; _tamperWork = 0f; _tamperNotifyTimer = 0f; }

        // 정상 업무 중단 — 내부 시뮬레이션의 사실이다. 그 작업실의 처리량이 실제로 줄어든다.
        // **늘리기만 한다.** 괴물 직후의 회복 정지(PostGhostRecovery)가 더 길게 걸려 있으면
        // 그쪽을 깎지 않는다.
        float until = GameState.Instance.DayTimeSeconds + 0.5f;
        if (until > sab.WorkBlockedUntil) sab.WorkBlockedUntil = until;
        _tamperWork += delta;

        // 1.5초마다 한 번씩 "지금 보고 있나" 를 묻는다. 보고 있었다면 조사자료가 생기고
        // 결번은 손을 뗀다(지시서 §4-3) — CCTV 를 켠 것만으로 자동 공개되지는 않는다.
        _tamperNotifyTimer -= delta;
        if (_tamperNotifyTimer <= 0f)
        {
            _tamperNotifyTimer = 1.5f;
            if (sim.NoteWorkInterruption(room, sab.EmployeeId, "작업을 중단하고 설비 쪽으로 이동"))
            {
                CaughtOnCamera++;
                AbortedByPlayer++;
                _tamperWork = 0f;
                _tamperRoomId = "";
                _caughtCooldown = 12f;
                Note(sim, $"봉쇄 교란 중단 — CCTV 관측됨 @{sim.RoomDisplayName(room)}");
                return;
            }
        }

        if (_tamperWork < (ops?.ContainmentWorkSeconds ?? 7f)) return;

        _tamperWork = 0f;
        _tamperRoomId = "";
        ContainmentTampers++;
        sim.Containment.Disturb(ops?.ContainmentInstabilityPerTamper ?? 18f,
            sab.EmployeeId, room);
        // 같은 방 직원의 기억에는 남는다(휴게시간 증언). 화면 로그에는 뜨지 않는다.
        sim.RecordOddBehaviour(sab.EmployeeId, room, "설비 패널을 한참 들여다보고 있었다");
        Note(sim, $"봉쇄 교란 성공 @{sim.RoomDisplayName(room)} " +
                  $"(불안정 {sim.Containment.Instability:0})");
    }

    private static IReadOnlyList<string> ContainmentRooms(OpsProfileDef ops)
    {
        if (ops == null) return System.Array.Empty<string>();
        if (ops.ContainmentRooms.Count > 0) return ops.ContainmentRooms;
        return ops.SabotageTargetRooms;
    }

    // ── LIGHTING OFF : 은밀 이동 → 범행 ──────────────────────────────────
    private void TickMurderPlan(float delta, FacilitySimulation sim, EmployeeState sab, OpsProfileDef ops)
    {
        switch (Phase)
        {
            case CovertPhase.Idle: TickIdle(delta, sim, sab, ops); break;
            case CovertPhase.SeekingTarget: TickSeek(sim, sab, ops); break;
            case CovertPhase.Moving: TickMoving(delta, sim, sab, ops); break;
            case CovertPhase.Preparing: TickPreparing(delta, sim, sab, ops); break;
            case CovertPhase.Attacking: TickAttack(sim, sab); break;
            case CovertPhase.Escape: TickEscape(sim, sab); break;
        }
    }

    private void TickIdle(float delta, FacilitySimulation sim, EmployeeState sab, OpsProfileDef ops)
    {
        // 교란하러 나와 있는 중 — 일을 끝냈거나, 위치가 다시 보이게 됐거나,
        // 너무 오래 머물렀으면 돌아간다. 순간이동하지 않는다(실제로 걸어서 복귀한다).
        if (_awayForTamper)
        {
            _awaySeconds += delta;
            bool done = ContainmentTampers > _tampersAtDeparture;
            bool exposed = sim.IsPositionTrackable(sab.CurrentRoomId);
            if (done || exposed || _awaySeconds > 40f)
            {
                _awayForTamper = false;
                _awaySeconds = 0f;
                if (exposed && !done) AbortedByPlayer++;
                if (sab.CurrentRoomId != HomeRoomId) { BeginEscape(sim, sab); return; }
            }
            return;
        }

        if (_seekCooldown > 0f) { _seekCooldown -= delta; return; }
        if (sab.IsMoving) return;

        // 같은 방에 둘뿐이고 아무도 보고 있지 않다 — 굳이 움직일 이유가 없다.
        // (예전 TickKillAttempt 의 경로가 여기로 들어왔다.)
        if (sim.CanKillNow() && SoloVictimHere(sim, sab) is { } here)
        {
            BeginPreparing(sim, sab, here, ops);
            return;
        }

        // 다른 방으로 가려면 **위치 추적이 끊겨 있어야** 한다. 불이 켜져 있으면
        // 미니맵에 그대로 찍히므로 결번 개체는 움직이지 않는다.
        if (!(ops?.AllowCovertMove ?? true)) return;
        if (DarkSeconds < (ops?.CovertMoveAfterSeconds ?? 9f)) return;
        if (sim.IsPositionTrackable(sab.CurrentRoomId)) return;   // 비상등이 있는 방이면 소용없다
        Phase = CovertPhase.SeekingTarget;
    }

    // 같은 방에 **단둘뿐**이고 지금 관측되지 않는가. 그렇다면 그 사람이 대상이다.
    private static string SoloVictimHere(FacilitySimulation sim, EmployeeState sab)
    {
        var room = sim.GetRoomState(sab.CurrentRoomId);
        if (room == null) return null;
        var others = room.OccupantEmployeeIds
            .Where(id => id != sab.EmployeeId)
            .Select(sim.GetEmployeeState)
            .Where(e => e is { Alive: true, Isolated: false })
            .ToList();
        if (others.Count != 1) return null;
        if (sim.IsRoomObservedNow(sab.CurrentRoomId)) return null;
        return others[0].EmployeeId;
    }

    // 갈 수 있고 · 피해자가 있고 · 관리자가 보고 있지 않은 방을 고른다.
    private void TickSeek(FacilitySimulation sim, EmployeeState sab, OpsProfileDef ops)
    {
        _seekCooldown = 2.5f;
        // 살인이 불가능한 날(DAY1 등)에도 **나가야 할 이유**가 있다 — 봉쇄 계통이 있는 방이
        // 지금 자기 자리가 아닐 때. 그런 날의 무단 이동은 교란을 위한 것이다.
        if (!sim.CanKillNow()) { SeekTamperRoom(sim, sab, ops); return; }

        string best = "";
        string bestVictim = "";
        int bestHops = int.MaxValue;

        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (id == sab.EmployeeId) continue;
            var v = sim.GetEmployeeState(id);
            if (v is not { Alive: true, Isolated: false }) continue;
            if (v.IsMoving) continue;                                   // 걷는 사람은 노리지 않는다
            string room = v.CurrentRoomId;
            if (string.IsNullOrEmpty(room) || room == sab.CurrentRoomId) continue;
            if (room == FacilitySimulation.IsolationRoomIdPublic
                || room == FacilitySimulation.DeployOriginRoomId
                || room == FacilitySimulation.MedicalRoomIdPublic) continue;
            if (!sim.IsRoomActive(room)) continue;
            // 그 방에 세 번째 사람이 있으면 범행이 성립하지 않는다.
            if (sim.OnDutyCount(room) != 1) continue;
            // 비상등이 있는 방은 불이 꺼져도 위치가 보인다 — 거기로 걸어가면 들킨다.
            if (sim.IsPositionTrackable(room)) continue;
            // 관리자가 지금 그 화면을 보고 있다.
            if (sim.IsRoomObservedNow(room)) continue;

            // **실제로 갈 수 있는가.** 차폐문이 내려와 있으면 경로가 아예 나오지 않는다.
            var path = sim.FindPath(sab.CurrentRoomId, room);
            if (path.Count == 0) { CovertMovesBlocked++; continue; }
            if (path.Count >= bestHops) continue;
            bestHops = path.Count;
            best = room;
            bestVictim = id;
        }

        if (string.IsNullOrEmpty(best)) { Phase = CovertPhase.Idle; return; }

        if (!sim.BeginCovertMove(sab.EmployeeId, best)) { Phase = CovertPhase.Idle; return; }
        TargetRoomId = best;
        VictimId = bestVictim;
        _purpose = Purpose.Kill;
        CovertMovesStarted++;
        _blockedSeconds = 0f;
        _lightAbortRolled = false;
        Phase = CovertPhase.Moving;
        Note(sim, $"은밀 이동 시작 {sim.RoomDisplayName(sab.CurrentRoomId)} → {sim.RoomDisplayName(best)}");
    }

    // 봉쇄 계통에 손댈 수 있는 방으로 걸어간다. 지금 자기 방이 이미 그런 방이면 나갈 이유가 없다.
    private void SeekTamperRoom(FacilitySimulation sim, EmployeeState sab, OpsProfileDef ops)
    {
        Phase = CovertPhase.Idle;
        int budget = ops?.ContainmentTampersPerDay ?? 0;
        if (budget <= 0 || ContainmentTampers >= budget) return;
        var rooms = ContainmentRooms(ops);
        if (rooms.Count == 0 || rooms.Contains(sab.CurrentRoomId)) return;

        string best = "";
        int bestHops = int.MaxValue;
        foreach (string room in rooms)
        {
            if (room == sab.CurrentRoomId || !sim.IsRoomActive(room)) continue;
            if (sim.HasRepairPending(room)) continue;
            // 비상등이 있는 방으로 가면 불이 꺼져도 위치가 그대로 보인다.
            if (sim.IsPositionTrackable(room)) continue;
            if (sim.IsRoomObservedNow(room)) continue;
            var path = sim.FindPath(sab.CurrentRoomId, room);
            if (path.Count == 0) { CovertMovesBlocked++; continue; }
            if (path.Count >= bestHops) continue;
            bestHops = path.Count;
            best = room;
        }
        if (string.IsNullOrEmpty(best)) return;
        if (!sim.BeginCovertMove(sab.EmployeeId, best)) return;

        TargetRoomId = best;
        VictimId = "";
        _purpose = Purpose.Tamper;
        CovertMovesStarted++;
        _blockedSeconds = 0f;
        _lightAbortRolled = false;
        Phase = CovertPhase.Moving;
        Note(sim, $"은밀 이동 시작(교란) {sim.RoomDisplayName(sab.CurrentRoomId)} -> {sim.RoomDisplayName(best)}");
    }

    private void TickMoving(float delta, FacilitySimulation sim, EmployeeState sab, OpsProfileDef ops)
    {
        // 차폐문에 막혀 멈춰 섰다 — 실제로 못 간다. 오래 막히면 포기한다.
        if (sab.BlockedByBarrier)
        {
            _blockedSeconds += delta;
            if (_blockedSeconds > 6f)
            {
                CovertMovesBlocked++;
                Note(sim, "은밀 이동 실패 — 차폐문에 막힘");
                BeginEscape(sim, sab);
            }
            return;
        }
        _blockedSeconds = 0f;

        // 관리자가 조명을 다시 켰다. 지금 복도 한가운데다 — 돌아가거나, 위험을 안고 계속 간다.
        // 주사위는 **한 번만** 굴린다. 매 프레임 굴리면 조명을 켜는 순간 사실상 100% 포기가 된다.
        if (GameState.Instance.IsConsumerPowered(PowerConsumer.Lighting) && !_lightAbortRolled)
        {
            _lightAbortRolled = true;
            if (_rng.Randf() < (ops?.CovertAbortOnLightChance ?? 0.5f))
            {
                AbortedByPlayer++;
                Note(sim, "은밀 이동 포기 — 조명 복구");
                BeginEscape(sim, sab);
                return;
            }
            // 계속 간다. 다만 이제부터의 발걸음은 시설 로그에 그대로 찍힌다
            // (ArriveAtRoom 이 도착 시점의 관측 가능 여부로 판정한다) — 그게 발각 위험이다.
            Note(sim, "조명 복구에도 이동 강행 (이후 동선은 기록에 남는다)");
        }

        if (sab.IsMoving) return;

        CovertMovesArrived++;
        if (sab.CurrentRoomId != TargetRoomId) { BeginEscape(sim, sab); return; }

        // 교란하러 온 것이면 여기서 FSM 은 쉰다 — TickContainment 가 이어받는다.
        // 돌아가는 판정은 TickIdle 의 _awayForTamper 분기가 맡는다.
        if (_purpose == Purpose.Tamper)
        {
            _awayForTamper = true;
            _tampersAtDeparture = ContainmentTampers;
            _awaySeconds = 0f;
            _purpose = Purpose.None;
            Phase = CovertPhase.Idle;
            Note(sim, $"교란 지점 도착 @{sim.RoomDisplayName(sab.CurrentRoomId)}");
            return;
        }

        var victim = sim.GetEmployeeState(VictimId);
        if (victim is not { Alive: true, Isolated: false } || victim.CurrentRoomId != TargetRoomId)
        {
            Note(sim, "도착했으나 대상이 없다");
            BeginEscape(sim, sab);
            return;
        }
        BeginPreparing(sim, sab, VictimId, ops);
    }

    private void BeginPreparing(FacilitySimulation sim, EmployeeState sab, string victimId, OpsProfileDef ops)
    {
        VictimId = victimId;
        TargetRoomId = sab.CurrentRoomId;
        PrepareSeconds = 0f;
        // 아무도 보고 있지 않다는 확신이 클수록 빨리 끝낸다.
        float need = ops?.CovertKillPrepareSeconds ?? 10f;
        if (DarkSeconds > 20f && BlindSeconds > 10f) need *= 0.6f;
        PrepareNeeded = Mathf.Max(3f, need);
        KillAttempts++;
        _signTimer = PrepareNeeded * 0.45f;
        Phase = CovertPhase.Preparing;
        Note(sim, $"범행 준비 시작 @{sim.RoomDisplayName(TargetRoomId)} " +
                  $"대상 {sim.GetEmployeeDef(victimId)?.Codename ?? victimId}");
    }

    // 조건이 **계속 유지되는 동안에만** 쌓인다. 하나라도 깨지면 멈춘다(지시서 §5-3).
    private void TickPreparing(float delta, FacilitySimulation sim, EmployeeState sab, OpsProfileDef ops)
    {
        var victim = sim.GetEmployeeState(VictimId);
        string room = sab.CurrentRoomId;

        bool ok = victim is { Alive: true, Isolated: false }
                  && victim.CurrentRoomId == room
                  && !sab.IsMoving
                  // 제3자가 들어오면 범행은 성립하지 않는다.
                  && sim.OnDutyCount(room) <= 2
                  // 관리자가 그 화면을 직접 보고 있다.
                  && !sim.IsRoomObservedNow(room)
                  && sim.CanKillNow();

        if (!ok)
        {
            // 관측 때문에 멈춘 경우에는 그 장면이 조사자료로 남는다 —
            // "설비 쪽이 아니라 **사람 쪽**으로 다가가고 있었다" 까지가 화면에 보인 전부다.
            if (sim.IsRoomObservedNow(room))
            {
                sim.NoteWorkInterruption(room, sab.EmployeeId, "작업을 중단하고 동료 쪽으로 다가감");
                CaughtOnCamera++;
            }
            AbortedByPlayer++;
            Note(sim, "범행 준비 중단");
            if (sab.CurrentRoomId != HomeRoomId) BeginEscape(sim, sab);
            else { Phase = CovertPhase.Idle; _seekCooldown = 6f; }
            return;
        }

        PrepareSeconds += delta;

        // ── 위험 징후 ──────────────────────────────────────────────────
        // 관리자가 **실제로 감지할 수 있는 것**만 낸다. CCTV 가 꺼져 있는데 화면 경고를
        // 띄우지 않는다(지시서 §5-4). 소리는 들린다 — 그래서 소리로만 알린다.
        if (_signTimer > 0f && PrepareSeconds >= _signTimer)
        {
            _signTimer = -1f;
            Sfx.Instance?.Play("noise", -14f, 1.2f);
            NSP.Ui.FacilityAlertHud.Instance?.Notify(
                "■ 어딘가에서 짧은 소음이 감지되었습니다.", NSP.Ui.NoticeLevel.Warning);
            // 같은 방의 다른 직원이 기억할 수도 있다(휴게시간 증언).
            sim.RecordOddBehaviour(sab.EmployeeId, room, "자꾸 이쪽을 보면서 가까이 왔다");
        }

        if (PrepareSeconds < PrepareNeeded) return;
        Phase = CovertPhase.Attacking;
    }

    private void TickAttack(FacilitySimulation sim, EmployeeState sab)
    {
        string room = sab.CurrentRoomId;
        if (sim.CommitCovertKill(VictimId, room))
        {
            KillsCommitted++;
            Note(sim, $"살인 @{sim.RoomDisplayName(room)} " +
                      $"대상 {sim.GetEmployeeDef(VictimId)?.Codename ?? VictimId}");
        }
        VictimId = "";
        BeginEscape(sim, sab);
    }

    // 범행 뒤에도 순간이동하지 않는다 — 실제로 걸어서 돌아간다.
    private void BeginEscape(FacilitySimulation sim, EmployeeState sab)
    {
        PrepareSeconds = PrepareNeeded = 0f;
        TargetRoomId = "";
        _purpose = Purpose.None;
        _awayForTamper = false;
        string home = !string.IsNullOrEmpty(HomeRoomId) ? HomeRoomId : sab.AssignedRoomId;
        if (string.IsNullOrEmpty(home) || home == sab.CurrentRoomId)
        {
            sim.EndCovertMove(sab.EmployeeId);
            Phase = CovertPhase.Idle;
            _seekCooldown = 8f;
            return;
        }
        if (!sim.BeginCovertMove(sab.EmployeeId, home))
        {
            sim.EndCovertMove(sab.EmployeeId);
            Phase = CovertPhase.Idle;
            _seekCooldown = 8f;
            return;
        }
        Phase = CovertPhase.Escape;
    }

    private void TickEscape(FacilitySimulation sim, EmployeeState sab)
    {
        if (sab.IsMoving) return;
        sim.EndCovertMove(sab.EmployeeId);
        Phase = CovertPhase.Idle;
        _seekCooldown = 10f;
    }

    private void Abort(FacilitySimulation sim, string saboteurId, string why)
    {
        if (!string.IsNullOrEmpty(saboteurId)) sim.EndCovertMove(saboteurId);
        Phase = CovertPhase.Idle;
        VictimId = TargetRoomId = "";
        PrepareSeconds = PrepareNeeded = 0f;
        _tamperWork = 0f;
        _purpose = Purpose.None;
        _awayForTamper = false;
        Note(sim, why);
    }

    private void Note(FacilitySimulation sim, string what)
    {
        Trace.Add($"{SaboteurPlan.Clock(GameState.Instance?.DayTimeSeconds ?? 0f)} {what}");
        while (Trace.Count > 80) Trace.RemoveAt(0);
    }

    public string DebugSummary(FacilitySimulation sim)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("──────── 감시 공백 디버그 (플레이어 비공개) ────────");
        sb.AppendLine($"phase={Phase}  어둠 {DarkSeconds:0.0}초  CCTV공백 {BlindSeconds:0.0}초");
        sb.AppendLine($"은밀이동 시도 {CovertMovesStarted} / 도착 {CovertMovesArrived} / 차단 {CovertMovesBlocked}");
        sb.AppendLine($"봉쇄교란 {ContainmentTampers}  범행시도 {KillAttempts}  살인 {KillsCommitted}  " +
                      $"중단 {AbortedByPlayer}  CCTV적발 {CaughtOnCamera}");
        foreach (string t in Trace) sb.AppendLine($"  {t}");
        return sb.ToString();
    }
}
