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

    public static string BuildActivityLine(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var roomDef = sim?.GetRoomDef(roomId);
        var state = sim?.GetRoomState(roomId);
        var task = sim?.GetActiveTaskForRoom(roomId);
        if (sim == null || roomDef == null || state == null || task == null)
            return "";

        if (state.OccupantEmployeeIds.Count == 0)
            return "";

        if (sim.IsRoomBlockedByMaterials(roomId))
            return "📦 자재 부족 — 대기 중";

        string icon = WorkIcons.GetValueOrDefault(roomDef.ManagedResource, "🔧");
        return $"{icon} {task.DisplayName} 중{AnimatedDots()}";
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
        bool activeFailure = !state.PowerOn || state.CctvDisconnected
            || (sim.GetRoomDef(roomId)?.ManagedResource == RoomResourceType.Power && GameState.Instance.IsPowerAccidentActive());
        if (activeFailure) return RoomDangerTier.Failure;

        float ratio = 0f;

        var task = sim.GetActiveTaskForRoom(roomId);
        if (task != null && task.HasNeglectConsequence && state.NeglectTimer > 0f)
            ratio = Mathf.Max(ratio, state.NeglectTimer / task.NeglectThresholdSeconds);

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
