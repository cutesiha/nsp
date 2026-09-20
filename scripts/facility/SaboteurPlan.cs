using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

public enum SaboteurPhase
{
    Waiting,      // 아직 기회가 아니다 — 평소처럼 자기 자리에서 일한다
    Approaching,  // 대상 작업실로 이동 중 (여기서 이동 기록이 남는다 = 전조)
    Dwelling,     // 도착해서 머무는 중 — 정해진 시간을 채워야 손을 댄다
    Returning,    // 저지른 뒤 원래 자리로 돌아가는 중
    Done,
}

// 결번자가 "언제, 어디서" 방해공작을 할지 정하는 기회 판정.
//
// 예전에는 시간이 되면 그 자리에서 바로 터졌다 — 플레이어에게는 랜덤 이벤트로 보였다.
// 지금은 결번자도 시설 안을 실제로 걸어다니는 직원이다.
//
//   ① 근무 후반(OpsProfileDef.SaboteurStartSeconds)이 되고
//   ② 큰 사고가 진행 중이 아니고
//   ③ 자기 배치실이 아닌 중요 시설(SabotageTargetRooms) 중 하나를 골라 실제로 이동하고
//   ④ 그 방에서 SabotageDwellSeconds 만큼 머물고
//   ⑤ 그 방이 CCTV로 감시되고 있지 않고, 사람이 너무 많지 않을 때
//
// 만 손을 댄다. ③의 이동과 ④의 체류는 Facility Log · CCTV · 다른 직원의 기억에
// 그대로 남는다 — 그게 플레이어가 나중에 되짚을 전조다.
//
// 이 클래스는 "기회"만 판단한다. 실제 피해(전력/자재/코어/CCTV)는 지금까지와 똑같이
// FacilitySimulation 이 준다.
public sealed class SaboteurPlan
{
    public SaboteurPhase Phase { get; private set; } = SaboteurPhase.Waiting;
    public string SaboteurId { get; private set; } = "";
    public string TargetRoomId { get; private set; } = "";
    public string HomeRoomId { get; private set; } = "";
    public float DwellSeconds { get; private set; }
    public float LeftHomeAtSeconds { get; private set; } = -1f;
    public float ArrivedAtSeconds { get; private set; } = -1f;
    public float ActedAtSeconds { get; private set; } = -1f;
    public string ActedRoomId { get; private set; } = "";

    // 디버그 전용 기록(§19). 플레이어에게는 절대 보여주지 않는다.
    public readonly List<string> Conditions = new();
    public readonly List<string> Clues = new();

    private float _returnAtSeconds = -1f;
    // 이 시간이 지나면 상황이 완벽하지 않아도 더 미루지 않는다(초).
    private const float LastChanceDelaySeconds = 25f;

    public bool HasActed => ActedAtSeconds >= 0f;

    public void Reset()
    {
        Phase = SaboteurPhase.Waiting;
        SaboteurId = TargetRoomId = HomeRoomId = ActedRoomId = "";
        DwellSeconds = 0f;
        LeftHomeAtSeconds = ArrivedAtSeconds = ActedAtSeconds = _returnAtSeconds = -1f;
        Conditions.Clear();
        Clues.Clear();
    }

    // --- 진행 ------------------------------------------------------------

    public void Tick(float delta, FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops)
    {
        // 대상 작업실이 정해지지 않은 날은 예전 방식 그대로 둔다(DAY0 교육 등).
        if (sim == null || saboteur == null || ops == null || ops.SabotageTargetRooms.Count == 0) return;
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        SaboteurId = saboteur.EmployeeId;

        switch (Phase)
        {
            case SaboteurPhase.Waiting: TickWaiting(sim, saboteur, ops, now); break;
            case SaboteurPhase.Approaching: TickApproaching(sim, saboteur, now); break;
            case SaboteurPhase.Dwelling: TickDwelling(delta, sim, saboteur); break;
            case SaboteurPhase.Returning: TickReturning(sim, saboteur, now); break;
        }
    }

    private void TickWaiting(FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops, float now)
    {
        // ① 근무 후반에만. 대상까지 걸어갈 시간을 감안해 조금 일찍 움직이기 시작한다.
        if (now < ops.SaboteurStartSeconds - ops.SabotageApproachLeadSeconds) return;
        // 이미 오늘 몫을 다 썼다.
        if (HasActed) { Phase = SaboteurPhase.Done; return; }
        // ② 지금 시설이 크게 망가져 모두가 달려가는 중이면 움직이지 않는다 — 눈에 띈다.
        //    다만 근무가 끝나 가면 더 미루지 않는다(오늘 안에 해야 한다).
        bool windowClosing = now > ops.SaboteurStartSeconds + LastChanceDelaySeconds;
        if (!windowClosing && sim.HasSeriousIncidentActive()) return;
        if (saboteur.IsMoving) return;

        string target = PickTarget(sim, saboteur, ops);
        if (string.IsNullOrEmpty(target)) return;

        HomeRoomId = string.IsNullOrEmpty(saboteur.AssignedRoomId)
            ? saboteur.CurrentRoomId
            : saboteur.AssignedRoomId;
        if (!sim.MoveEmployeeTo(saboteur.EmployeeId, target)) return;

        TargetRoomId = target;
        LeftHomeAtSeconds = now;
        Phase = SaboteurPhase.Approaching;
        Note(Conditions, "AfterMinimumTime");
        Note(Conditions, "NoMajorIncidentRunning");
        Note(Clues, $"FacilityLog 이동 기록 {sim.RoomDisplayName(HomeRoomId)} → {sim.RoomDisplayName(target)}");
    }

    private void TickApproaching(FacilitySimulation sim, EmployeeState saboteur, float now)
    {
        if (saboteur.IsMoving || saboteur.CurrentRoomId != TargetRoomId) return;
        Phase = SaboteurPhase.Dwelling;
        ArrivedAtSeconds = now;
        DwellSeconds = 0f;
        Note(Conditions, "InTargetRoom");
    }

    private void TickDwelling(float delta, FacilitySimulation sim, EmployeeState saboteur)
    {
        // 목적지에서 밀려났다(관리자 재배치 등) — 기회를 처음부터 다시 잡는다.
        if (saboteur.CurrentRoomId != TargetRoomId)
        {
            Phase = SaboteurPhase.Waiting;
            TargetRoomId = "";
            DwellSeconds = 0f;
            return;
        }
        // 그 방이 고장 나거나 경고가 떠 버리면 지금 손대는 것은 너무 티가 난다.
        if (sim.HasRepairPending(TargetRoomId) || sim.Warnings.HasActive(TargetRoomId))
        {
            Phase = SaboteurPhase.Waiting;
            TargetRoomId = "";
            DwellSeconds = 0f;
            return;
        }
        DwellSeconds += delta;
    }

    private void TickReturning(FacilitySimulation sim, EmployeeState saboteur, float now)
    {
        if (now < _returnAtSeconds) return;
        Phase = SaboteurPhase.Done;
        if (!string.IsNullOrEmpty(HomeRoomId) && saboteur.CurrentRoomId != HomeRoomId)
            sim.MoveEmployeeTo(saboteur.EmployeeId, HomeRoomId);
    }

    // --- 실행 판정 --------------------------------------------------------

    // FacilitySimulation 이 방해공작을 실행하기 직전에 묻는다.
    public bool ReadyToAct(FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops)
    {
        if (ops == null || ops.SabotageTargetRooms.Count == 0) return true;   // 예전 방식
        if (Phase != SaboteurPhase.Dwelling) return false;
        if (saboteur.CurrentRoomId != TargetRoomId) return false;
        if (DwellSeconds < ops.SabotageDwellSeconds) return false;
        // ⑤ 같은 방에 사람이 너무 많으면 하지 않는다.
        int others = System.Math.Max(0, sim.OnDutyCount(TargetRoomId) - 1);
        if (others > ops.SabotageMaxOthersInRoom) return false;
        return true;
    }

    // 실제로 저질렀다. 남은 흔적을 정리해 두고 원래 자리로 돌아갈 준비를 한다.
    public void OnActed(FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops, string roomId)
    {
        ActedAtSeconds = GameState.Instance?.DayTimeSeconds ?? 0f;
        ActedRoomId = roomId;
        Note(Conditions, "StayedLongEnough");
        Note(Conditions, "SurveillanceWeak");
        CollectClues(sim, saboteur, roomId);

        _returnAtSeconds = ActedAtSeconds + (ops?.SaboteurReturnSeconds ?? 8f);
        Phase = SaboteurPhase.Returning;
    }

    // 이 사건을 나중에 되짚을 수 있는 경로가 실제로 몇 개 남았는지 센다.
    private void CollectClues(FacilitySimulation sim, EmployeeState saboteur, string roomId)
    {
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (id == saboteur.EmployeeId) continue;
            var st = sim.GetEmployeeState(id);
            if (st is not { Alive: true, Isolated: false }) continue;

            if (st.CurrentRoomId == roomId)
            {
                bool noticed = EmployeeTraits.Get(id).ObservationalAwareness >= EmployeeTraits.AwarenessForWitness;
                Note(Clues, noticed
                    ? $"{sim.GetEmployeeDef(id)?.Codename ?? id} 직접 목격"
                    : $"{sim.GetEmployeeDef(id)?.Codename ?? id} 같은 방 있었음(목격은 못함)");
            }
            else if (NSP.Dialogue.DialogueContextBuilder.IsAdjacent(st.CurrentRoomId, roomId)
                     && EmployeeTraits.Get(id).ObservationalAwareness >= EmployeeTraits.AwarenessForIndirect)
            {
                Note(Clues, $"{sim.GetEmployeeDef(id)?.Codename ?? id} 옆 방에서 인지");
            }
        }

        if (NSP.Dialogue.PlayerKnownEvidence.HasRoomRecord(roomId))
            Note(Clues, "CCTV / 순찰 위치 기록");
    }

    // --- 대상 고르기 ------------------------------------------------------

    // 자기 배치실이 아닌 중요 시설 중, 나중에 되짚을 수 있는 흔적이 남을 곳을 고른다.
    // "아무도 없고 아무도 못 보는 방"만 고르면 단서가 하나도 안 남아 추리가 불가능해진다.
    private static string PickTarget(FacilitySimulation sim, EmployeeState saboteur, OpsProfileDef ops)
    {
        string best = "";
        float bestScore = float.MinValue;

        foreach (string roomId in ops.SabotageTargetRooms)
        {
            if (string.IsNullOrEmpty(roomId)) continue;
            if (roomId == saboteur.AssignedRoomId || roomId == saboteur.CurrentRoomId) continue;
            if (!sim.IsRoomActive(roomId)) continue;
            var rs = sim.GetRoomState(roomId);
            if (rs == null || rs.Locked) continue;
            if (sim.HasRepairPending(roomId) || sim.Warnings.HasActive(roomId)) continue;

            int others = sim.OnDutyCount(roomId);
            if (others > ops.SabotageMaxOthersInRoom) continue;

            // 같은 방에 한 명 있으면 "그 시각 거기 누가 있었나"가 진술로 남는다 — 가장 좋은 대상.
            // 아무도 없으면 옆 방 인기척이라도 남아야 한다.
            float score = others == 1 ? 4f : NearbyWitnessCount(sim, roomId) > 0 ? 2f : 0f;
            // 너무 먼 방은 근무 시간 안에 왕복하지 못한다.
            score -= sim.GetRoomVisualPosition(roomId)
                .DistanceTo(sim.GetRoomVisualPosition(saboteur.CurrentRoomId)) * 0.002f;

            if (score <= bestScore) continue;
            bestScore = score;
            best = roomId;
        }
        return best;
    }

    private static int NearbyWitnessCount(FacilitySimulation sim, string roomId)
    {
        int n = 0;
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st is not { Alive: true, Isolated: false }) continue;
            if (NSP.Dialogue.DialogueContextBuilder.IsAdjacent(st.CurrentRoomId, roomId)
                && EmployeeTraits.Get(id).ObservationalAwareness >= EmployeeTraits.AwarenessForIndirect) n++;
        }
        return n;
    }

    private static void Note(List<string> list, string text)
    {
        if (!list.Contains(text)) list.Add(text);
    }

    // --- 디버그(§19) ------------------------------------------------------

    // 개발 중에만 쓴다. 이 문자열은 어떤 UI 에도 올리지 않는다.
    public string DebugSummary(FacilitySimulation sim)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("──────── 결번자 디버그 (플레이어 비공개) ────────");
        sb.AppendLine($"Saboteur: {SaboteurId}  ({sim?.GetEmployeeDef(SaboteurId)?.Codename ?? "?"})");
        sb.AppendLine(HasActed
            ? $"Sabotage: {Clock(ActedAtSeconds)} {ActedRoomId}"
            : $"Sabotage: 없음 (phase={Phase})");
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
        float length = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        int total = 22 * 60 + Mathf.FloorToInt(Mathf.Max(0f, seconds) * (360f / Mathf.Max(1f, length)));
        return $"{total / 60 % 24:00}:{total % 60:00}";
    }
}
