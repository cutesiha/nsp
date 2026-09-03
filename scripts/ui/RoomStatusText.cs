using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;

namespace NSP.Ui;

public enum RoomDangerTier
{
    None,
    Delayed,
    Unstable,
    Failure,
}

// RoomView(지도 박스)와 CctvView(카메라 화면)가 같은 방 상태 문구를 공유하기 위한 순수 조회
// 헬퍼. 게임 상태를 읽기만 하고 아무것도 바꾸지 않는다 — 판정은 여전히 FacilitySimulation/
// TabooRuleSystem 몫이다.
public static class RoomStatusText
{
    private static readonly Dictionary<RoomResourceType, string> WorkIcons = new()
    {
        [RoomResourceType.Power] = "🔧",
        [RoomResourceType.Survival] = "💨",
        [RoomResourceType.Materials] = "🔧",
        [RoomResourceType.Stress] = "💊",
        [RoomResourceType.Surveillance] = "📹",
        [RoomResourceType.CoreRepair] = "🔧",
        [RoomResourceType.Storage] = "📦",
    };

    public static string WorkIcon(RoomResourceType type) => WorkIcons.GetValueOrDefault(type, "🔧");

    public static string BuildActivityLine(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var roomDef = sim?.GetRoomDef(roomId);
        var state = sim?.GetRoomState(roomId);
        var st = sim?.GetPrimarySpawnedTask(roomId);
        if (sim == null || roomDef == null || state == null || st == null || st.Status != SpawnedTaskStatus.Active)
            return "";

        if (state.OccupantEmployeeIds.Count == 0)
            return "";

        if (sim.IsRoomBlockedByMaterials(roomId))
            return "📦 자재 부족 — 대기 중";

        var task = sim.GetTaskDef(st.TaskId);
        return $"{WorkIcon(roomDef.ManagedResource)} {task?.DisplayName ?? st.TaskId} 중{AnimatedDots()}";
    }

    // 방 지도 박스 / CCTV 화면에 함께 쓰는 발생 업무 상태 블록.
    //   🔧 발전기 점검  ⏱0:12
    //   ███░░░ 62%          (담당자 없으면 "⌛ 담당자 없음", 완료/실패면 배지)
    public static string BuildRoomStatusBlock(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var roomDef = sim?.GetRoomDef(roomId);
        if (sim == null || roomDef == null || roomDef.IsRestricted) return "";

        var st = sim.GetPrimarySpawnedTask(roomId);
        if (st != null)
        {
            var task = sim.GetTaskDef(st.TaskId);
            string name = task?.DisplayName ?? st.TaskId;

            if (st.Status == SpawnedTaskStatus.Completed) return $"✓ {name} 완료";
            if (st.Status == SpawnedTaskStatus.Failed) return $"🚨 {name} 처리 실패";

            if (st.IsRepair) return $"🔧 {name} 수리 필요";

            string icon = WorkIcon(roomDef.ManagedResource);
            string head = st.Recurring ? $"{icon} {name}" : $"{icon} {name}  ⏱{Clock(st.Remaining)}";

            int here = sim.GetRoomState(roomId)?.OccupantEmployeeIds.Count ?? 0;
            int need = task != null ? Mathf.Max(1, task.MinWorkersToProgress) : 1;

            string body;
            if (sim.IsRoomBlockedByMaterials(roomId))
                body = "📦 자재 부족 — 대기";
            else if (here > 0 && here < need)
                body = $"⚠ {need}명 필요 (현재 {here}명)";
            else if (here > 0)
            {
                float pct = Mathf.Clamp(st.Ratio, 0f, 1f) * 100f;
                body = $"{Bar(st.Ratio)} {pct:0}%";
            }
            else
                body = "⌛ 담당자 없음";

            return $"{head}\n{body}";
        }

        string dangerLine = GetDangerLine(GetDangerTier(roomId));
        if (!string.IsNullOrEmpty(dangerLine)) return dangerLine;
        if (TabooRuleSystem.Instance != null && TabooRuleSystem.Instance.IsRoomAtTabooRisk(roomId))
            return "⚠ 금기 주의";
        return "";
    }

    private static string Clock(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return $"{s / 60:0}:{s % 60:00}";
    }

    public static string Bar(float ratio)
    {
        int filled = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(ratio, 0f, 1f) * 6f), 0, 6);
        return new string('█', filled) + new string('░', 6 - filled);
    }

    // "중..."의 점 개수가 1→2→3→2→1... 순서로 계속 순환하게 만드는 시각 효과 전용 헬퍼.
    // 게임 상태와 무관 — 시간(Time.GetTicksMsec)만 읽는다.
    public static string AnimatedDots()
    {
        int step = (int)(Time.GetTicksMsec() / 350) % 4;
        int dots = step switch { 0 => 1, 1 => 2, 2 => 3, _ => 2 };
        return new string('.', dots);
    }

    public static RoomDangerTier GetDangerTier(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var state = sim?.GetRoomState(roomId);
        if (sim == null || state == null) return RoomDangerTier.None;

        // 이미 실제로 벌어진 지속형 이상 상태 — 잠금(구역 봉쇄)은 플레이어의 의도적 조치라
        // "고장"으로 취급하지 않는다.
        var rdef = sim.GetRoomDef(roomId);
        bool activeFailure = !state.PowerOn || state.CctvDisconnected
            || (rdef?.ManagedResource == RoomResourceType.Power && GameState.Instance.IsPowerAccidentActive())
            || (roomId == "guard_room" && GameState.Instance.CctvSystemOffline)
            || (roomId == "maintenance_room" && GameState.Instance.MaterialsProductionHalted)
            || (roomId == "vent_room" && GameState.Instance.VentilationDown);
        if (activeFailure) return RoomDangerTier.Failure;

        var prim = sim.GetPrimarySpawnedTask(roomId);
        if (prim is { Status: SpawnedTaskStatus.Failed } or { IsRepair: true }) return RoomDangerTier.Failure;

        // 발생 업무의 제한시간이 얼마나 임박했는가(0~1).
        float ratio = sim.GetRoomUrgencyRatio(roomId);

        foreach (var kv in state.TabooHoldTimers)
        {
            if (kv.Value <= 0f) continue;
            var taboo = TabooRuleSystem.Instance?.GetTaboo(kv.Key);
            float hold = taboo?.ConditionParams.GetValueOrDefault("hold_seconds", 0f).AsSingle() ?? 0f;
            if (hold > 0f) ratio = Mathf.Max(ratio, kv.Value / hold);
        }

        if (ratio <= 0f) return RoomDangerTier.None;
        return ratio < 0.5f ? RoomDangerTier.Delayed : RoomDangerTier.Unstable;
    }

    // 이미 고장난 방이 "무엇을 못 하게 됐는지" 한 줄로. 고장이 아니면 빈 문자열.
    public static string GetFailureCause(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var state = sim?.GetRoomState(roomId);
        if (sim == null || state == null) return "";

        var gs = GameState.Instance;
        var rdef = sim.GetRoomDef(roomId);
        if (rdef?.ManagedResource == RoomResourceType.Power && gs.IsPowerAccidentActive()) return "전력 용량 저하";
        if (roomId == "guard_room" && gs.CctvSystemOffline) return "감시 시스템 오프라인";
        if (roomId == "maintenance_room" && gs.MaterialsProductionHalted) return "자재 생산 정지";
        if (roomId == "vent_room" && gs.VentilationDown) return "환기 정지";
        if (!state.PowerOn) return "전력 차단";
        if (state.CctvDisconnected) return "CCTV 단절";

        var prim = sim.GetPrimarySpawnedTask(roomId);
        if (prim is { Status: SpawnedTaskStatus.Failed } or { IsRepair: true }) return "설비 고장";
        return "";
    }

    public static string GetDangerLine(RoomDangerTier tier) => tier switch
    {
        RoomDangerTier.Delayed => "⚠ 점검 지연",
        RoomDangerTier.Unstable => "❗ 상태 불안정",
        RoomDangerTier.Failure => "🚨 고장",
        _ => "",
    };

    public static string BuildCoreBar(float progressPercent)
    {
        int filled = Mathf.Clamp(Mathf.RoundToInt(progressPercent / 10f), 0, 10);
        return new string('█', filled) + new string('░', 10 - filled) + $" {progressPercent:0}%";
    }
}
