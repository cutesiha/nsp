using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

public enum SaboteurPhase
{
    Idle,        // 평소처럼 일한다
    Opportunity, // 배치된 방이 손댈 수 있는 곳이고, 자리를 잡았다
    Preparing,   // 준비 중 — 이 동안 전조가 나온다. 재배치하면 취소된다.
    Done,        // 오늘 몫을 끝냈다
}

// 결번자가 "언제" 손을 댈지 정한다.
//
// V2 에서 바뀐 것 하나가 전부다 — **결번자는 스스로 방을 옮기지 않는다.**
// 예전에는 범행 장소를 찾아 걸어갔다. 그러면 로그에 남는 그 이동이 곧 "이 사람이 범인"
// 이라는 표시가 되어 버린다. 이제 방 간 이동 권한은 오직 플레이어에게 있고,
// 결번자는 **플레이어가 배치해 준 자리에서** 정상 직원인 척 일하다가 기회를 잡는다.
//
//   Idle → Opportunity → Preparing → (방해공작) → Done
//
// 플레이어가 준비 중인 결번자를 다른 방으로 옮기면 준비는 그 자리에서 취소되고,
// 새 방에서 조건을 처음부터 다시 채워야 한다. 배치가 곧 기회이자 방어다.
public sealed class SaboteurPlan
{
    public SaboteurPhase Phase { get; private set; } = SaboteurPhase.Idle;
    public string SaboteurId { get; private set; } = "";
    // 지금 기회를 재고 있는 방. 플레이어가 옮기면 이 값이 바뀌고 준비는 초기화된다.
    public string WatchedRoomId { get; private set; } = "";
    public float SettledSeconds { get; private set; }
    public float PrepareSeconds { get; private set; }
    public float PrepareNeeded { get; private set; }
    public float PreparingStartedAt { get; private set; } = -1f;
    public float ActedAtSeconds { get; private set; } = -1f;
    public string ActedRoomId { get; private set; } = "";
    public int CancelCount { get; private set; }
    // 오늘 몇 번 저질렀는가(하루 한도는 OpsProfileDef.MaxSabotageActionsPerDay 가 정한다).
    public int ActionCount { get; private set; }

    // 디버그 전용 기록(플레이어에게 보여주지 않는다).
    public readonly List<string> Conditions = new();
    public readonly List<string> Clues = new();
    // 이번 준비 구간에서 이미 낸 전조(같은 전조를 반복해 흘리지 않는다).
    private readonly HashSet<string> _firedPrecursors = new();

    // 목표 구간 안에서 매번 같은 초에 터지면 대본처럼 보인다 — 근무마다 조금씩 흔든다.
    private float _windowOffset;

    public bool HasActed => ActedAtSeconds >= 0f;
    public float PrepareRatio => PrepareNeeded <= 0f ? 0f : Mathf.Clamp(PrepareSeconds / PrepareNeeded, 0f, 1f);

    public void Reset()
    {
        Phase = SaboteurPhase.Idle;
        SaboteurId = WatchedRoomId = ActedRoomId = "";
        SettledSeconds = PrepareSeconds = PrepareNeeded = 0f;
        PreparingStartedAt = ActedAtSeconds = -1f;
        CancelCount = 0;
        ActionCount = 0;
        _windowOffset = GD.Randf() * 6f;
        Conditions.Clear();
        Clues.Clear();
        _firedPrecursors.Clear();
    }

    // --- 진행 ------------------------------------------------------------

    public void Tick(float delta, FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops)
    {
        // 대상 작업실이 정해지지 않은 날은 이 기회 판정을 쓰지 않는다(DAY0 교육 등).
        if (sim == null || saboteur == null || ops == null || ops.SabotageTargetRooms.Count == 0) return;
        if (Phase == SaboteurPhase.Done) return;

        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        SaboteurId = saboteur.EmployeeId;

        string room = saboteur.CurrentRoomId;

        // 플레이어가 방을 옮겼다 → 지금까지의 준비는 전부 없던 일이 된다.
        if (room != WatchedRoomId)
        {
            if (Phase == SaboteurPhase.Preparing) CancelCount++;
            WatchedRoomId = room;
            SettledSeconds = PrepareSeconds = 0f;
            PreparingStartedAt = -1f;
            Phase = SaboteurPhase.Idle;
            _firedPrecursors.Clear();
        }

        // 플레이어가 재배치를 지시하는 순간(=걷기 시작) 준비는 그 자리에서 깨진다.
        // 자리를 뜨는 것은 관리자의 개입이고, 그 개입이 방해공작을 막는 유일한 수단이다.
        if (saboteur.IsMoving || !ops.SabotageTargetRooms.Contains(room) || !sim.IsRoomActive(room))
        {
            SettledSeconds = PrepareSeconds = 0f;
            if (Phase != SaboteurPhase.Idle)
            {
                if (Phase == SaboteurPhase.Preparing) CancelCount++;
                Phase = SaboteurPhase.Idle;
                _firedPrecursors.Clear();
            }
            return;
        }

        // 그 방이 지금 시끄럽다(경고가 떠 있거나 수리가 걸려 있다) — 손대지 않고 **기다린다**.
        //
        // 예전에는 여기서도 준비를 0으로 지웠다. 그런데 경고는 근무 내내 이 방 저 방에서
        // 뜨기 때문에, 결번자는 준비를 채우는 족족 잃고 5일을 해도 한 번밖에 못 저질렀다.
        // 자리를 뜨지 않았는데 준비가 사라지는 것도 앞뒤가 맞지 않는다 — 그대로 멈춰 둔다.
        if (sim.HasRepairPending(room) || sim.Warnings.HasActive(room)) return;

        SettledSeconds += delta;

        // ① 활성 시각 전에는 기회 자체가 열리지 않는다.
        if (now < ops.SaboteurStartSeconds) return;
        // ② 지금 **이 방**이 수습 중이면 손대지 않는다 — 사람이 몰려 너무 눈에 띈다.
        //    (다른 방의 사고까지 보면, 사고가 잦은 날에는 결번자가 아무것도 못 한다.)
        if (sim.HasRepairPending(room)) return;
        // ③ 배치된 자리에 자리를 잡아야 한다.
        if (SettledSeconds < ops.SabotageSettleSeconds) return;

        if (Phase == SaboteurPhase.Idle)
        {
            Phase = SaboteurPhase.Opportunity;
            Note(Conditions, "AfterActivationTime");
            Note(Conditions, "InTargetRoomByPlayerOrder");
            Note(Conditions, "SettledInRoom");
        }

        if (Phase == SaboteurPhase.Opportunity)
        {
            Phase = SaboteurPhase.Preparing;
            PreparingStartedAt = now;
            PrepareNeeded = PrepareTime(sim, ops, room, saboteur.EmployeeId);
            PrepareSeconds = 0f;
        }

        if (Phase != SaboteurPhase.Preparing) return;

        PrepareSeconds += delta;
        TickPrecursors(sim, saboteur, room, now);
    }

    // 준비에 걸리는 시간. 사람이 많을수록, 경비가 볼수록 오래 걸린다.
    // 다만 "3명이면 절대 불가" 같은 규칙은 두지 않는다 — 악용되면 추리가 아니라 공식이 된다.
    private static float PrepareTime(FacilitySimulation sim, OpsProfileDef ops, string roomId, string saboteurId)
    {
        float need = Mathf.Lerp(ops.SabotagePrepareMinSeconds, ops.SabotagePrepareMaxSeconds, GD.Randf());
        int others = Mathf.Max(0, sim.OnDutyCount(roomId) - 1);
        // 사람이 많으면 오래 걸리지만, 예전 배율(1.45/1.8)은 경비실 배율까지 겹쳐 두 배 가까이
        // 되면서 근무 안에 준비가 끝나지 않는 날이 많았다. 억제력은 남기고 폭만 줄인다.
        need *= others switch { 0 => 0.85f, 1 => 1f, 2 => 1.25f, _ => 1.45f };
        // 경비실에 사람이 있으면 그만큼 눈치를 본다.
        need *= 1f + 0.13f * sim.OnDutyCount(FacilitySimulation.GuardRoomIdPublic);
        return Mathf.Max(3f, need);
    }

    // --- 전조 --------------------------------------------------------------

    // 준비 중에 새어 나오는 미세한 이상. 세 갈래다.
    //   ① 행동    — CCTV 로 그 방을 보고 있으면 잡힌다
    //   ② 설비    — 출력/진행도가 잠깐 흔들린다(수치를 보고 있으면 눈에 띈다)
    //   ③ 목격    — 같은 방의 관찰력 있는 직원이 기억한다(휴게시간 증언)
    // 어느 것도 "누가 범인이다"를 말하지 않는다.
    private void TickPrecursors(FacilitySimulation sim, EmployeeState saboteur, string roomId, float now)
    {
        float r = PrepareRatio;

        if (r >= 0.35f && _firedPrecursors.Add("action"))
        {
            sim.MarkSuspiciousAction(roomId, saboteur.EmployeeId);
            Note(Clues, $"{Codename(sim, saboteur.EmployeeId)} 설비 접근(CCTV 로 확인 가능)");
        }

        if (r >= 0.6f && _firedPrecursors.Add("fault"))
        {
            sim.TriggerMicroFault(roomId);
            Note(Clues, $"{sim.RoomDisplayName(roomId)} 수치 미세 이상");
        }

        if (r >= 0.8f && _firedPrecursors.Add("witness"))
        {
            var seen = sim.RecordOddBehaviour(saboteur.EmployeeId, roomId, "설비 쪽에 평소보다 오래 머물렀다");
            foreach (string id in seen) Note(Clues, $"{Codename(sim, id)} 목격(증언 가능)");
        }
    }

    // --- 실행 판정 --------------------------------------------------------

    // FacilitySimulation 이 방해공작을 실행하기 직전에 묻는다.
    public bool ReadyToAct(FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops)
    {
        if (ops == null || ops.SabotageTargetRooms.Count == 0) return true;   // 예전 방식
        if (Phase != SaboteurPhase.Preparing) return false;
        if (saboteur.CurrentRoomId != WatchedRoomId) return false;
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        // 목표 구간이 끝나 가면 준비가 조금 덜 됐어도 실행한다 — 대회용 DAY1 의 핵심 사건이
        // 근무 막판까지 밀리면 추리할 시간이 없다. 전조는 이미 절반 이상 나온 뒤다.
        bool deadline = now >= ops.SabotageWindowEndSeconds;
        float need = deadline ? PrepareNeeded * 0.55f : PrepareNeeded;
        if (PrepareSeconds < need) return false;
        return now >= ops.SabotageWindowStartSeconds + _windowOffset;
    }

    // 실제로 저질렀다.
    public void OnActed(FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops, string roomId)
    {
        ActedAtSeconds = GameState.Instance?.DayTimeSeconds ?? 0f;
        ActedRoomId = roomId;
        Note(Conditions, "PreparedLongEnough");
        Note(Conditions, "InsideSabotageWindow");
        CollectClues(sim, saboteur, roomId);
        ActionCount++;
        Phase = SaboteurPhase.Done;
    }

    // 오늘 몫이 남았다 — 다음 차례를 연다. 준비는 처음부터 다시 채운다.
    // (전조도 다시 나오므로, 두 번째 방해공작도 미리 눈치챌 여지가 남는다.)
    public void ArmNextAction()
    {
        Phase = SaboteurPhase.Idle;
        WatchedRoomId = "";
        SettledSeconds = PrepareSeconds = PrepareNeeded = 0f;
        PreparingStartedAt = -1f;
        _windowOffset = GD.Randf() * 6f;
        _firedPrecursors.Clear();
    }

    private void CollectClues(FacilitySimulation sim, EmployeeState saboteur, string roomId)
    {
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (id == saboteur.EmployeeId) continue;
            var st = sim.GetEmployeeState(id);
            if (st is not { Alive: true, Isolated: false }) continue;
            if (st.CurrentRoomId != roomId) continue;
            bool noticed = EmployeeTraits.Get(id).ObservationalAwareness >= EmployeeTraits.AwarenessForWitness;
            Note(Clues, noticed
                ? $"{Codename(sim, id)} 직접 목격"
                : $"{Codename(sim, id)} 같은 방 있었음(목격은 못함)");
        }
        if (NSP.Dialogue.PlayerKnownEvidence.HasRoomRecord(roomId))
            Note(Clues, "CCTV / 순찰 위치 기록");
    }

    private static string Codename(FacilitySimulation sim, string id) =>
        sim.GetEmployeeDef(id)?.Codename ?? id;

    private static void Note(List<string> list, string text)
    {
        if (!list.Contains(text)) list.Add(text);
    }

    // --- 디버그 ------------------------------------------------------------

    public string DebugSummary(FacilitySimulation sim)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("──────── 결번자 디버그 (플레이어 비공개) ────────");
        sb.AppendLine($"Saboteur: {SaboteurId}  ({sim?.GetEmployeeDef(SaboteurId)?.Codename ?? "?"})");
        sb.AppendLine(HasActed
            ? $"Sabotage: {Clock(ActedAtSeconds)} {ActedRoomId}  (준비 시작 {Clock(PreparingStartedAt)})"
            : $"Sabotage: 없음 (phase={Phase}, 준비 {PrepareSeconds:0}/{PrepareNeeded:0}초)");
        sb.AppendLine($"재배치로 취소된 횟수: {CancelCount}");
        sb.AppendLine("Preconditions:");
        foreach (string c in Conditions) sb.AppendLine($"  ✓ {c}");
        sb.AppendLine("Generated Clues:");
        foreach (string c in Clues) sb.AppendLine($"  - {c}");
        sb.AppendLine("False Leads:");
        foreach (string f in EmployeeBehaviorSystem.DebugFalseLeads) sb.AppendLine($"  - {f}");
        return sb.ToString();
    }

    public static string Clock(float seconds)
    {
        float length = Config.Instance?.Data?.DayLengthSeconds ?? 120f;
        int total = 22 * 60 + Mathf.FloorToInt(Mathf.Max(0f, seconds) * (360f / Mathf.Max(1f, length)));
        return $"{total / 60 % 24:00}:{total % 60:00}";
    }
}
