using Godot;
using NSP.Core;

namespace NSP.Facility;

// "지금 각 방에 몇 명이 있는가" → "그래서 무엇이 달라지는가" 를 한 곳에서 답한다.
//
// 판정은 하지 않는다. FacilitySimulation 이 업무 속도·자재 소모·방해공작 확률을 계산할 때
// 이 창구로만 인원 효과를 묻는다. 수치는 전부 data/ops/*.tres (OpsProfileDef) 에 있다.
public static class RoomStaffing
{
    private static FacilitySimulation Sim => FacilitySimulation.Instance;

    public static int Count(string roomId) => Sim?.OnDutyCount(roomId) ?? 0;

    // 그 방 업무의 진행 속도 배율. 0명이면 0.
    // 같은 방에 불편(Uneasy) 관계 쌍이 있으면 쌍마다 config UneasyEfficiencyMultiplier 가 곱해진다.
    public static float Efficiency(string roomId)
    {
        var ops = OpsProfile.Room(roomId);
        float baseRate = ops == null
            ? Mathf.Max(0, Count(roomId))                        // 표가 없으면 옛 방식(머릿수 합)
            : OpsProfile.Curve(ops.Efficiency, Count(roomId), 0f);
        return baseRate * RelationEfficiency(roomId);
    }

    // --- 직원 관계 (RelationshipSystem) -------------------------------------

    // 그 방에서 지금 근무 중인 사람들 가운데 사이가 나쁜(Uneasy 이하) 쌍.
    // 동실 거부(Refuse) 쌍은 배치 화면이 막지만, 근무 중 재배치로 모였다면 불편과 같은 페널티를 받는다.
    public static System.Collections.Generic.List<(string A, string B)> TensePairs(string roomId)
    {
        var pairs = new System.Collections.Generic.List<(string, string)>();
        var ids = Sim?.OnDutyEmployeeIds(roomId);
        if (ids == null) return pairs;
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
                if (RelationshipSystem.Band(ids[i], ids[j]) <= PairBand.Uneasy)
                    pairs.Add((ids[i], ids[j]));
        return pairs;
    }

    public static float RelationEfficiency(string roomId)
    {
        int tense = TensePairs(roomId).Count;
        if (tense == 0) return 1f;
        float m = Config.Instance?.Data?.UneasyEfficiencyMultiplier ?? 1f;
        return Mathf.Pow(Mathf.Clamp(m, 0f, 1f), tense);
    }

    // 시설 전체 업무에 걸리는 배율. 발전실 인원(그리고 발전실 고장)이 여기에 들어온다.
    public static float FacilityOutput()
    {
        float mult = 1f;
        foreach (var ops in OpsProfile.AllRooms())
        {
            if (Sim != null && Sim.HasRepairPending(ops.RoomId) && ops.OutputWhileBroken < 1f)
                mult *= Mathf.Max(0.05f, ops.OutputWhileBroken);
            else
                mult *= OpsProfile.Curve(ops.FacilityOutput, Count(ops.RoomId));
            // 미세 이상이 도는 동안은 출력이 살짝 내려앉는다(100% → 94% → 100%).
            if (Sim != null && Sim.HasMicroFault(ops.RoomId)) mult *= 0.94f;
        }
        return mult;
    }

    // 시설 전체 경고 발생 확률에 걸리는 배율(정비 인력이 많으면 낮아진다).
    public static float WarningChanceMultiplier()
    {
        float mult = 1f;
        foreach (var ops in OpsProfile.AllRooms())
            mult *= OpsProfile.Curve(ops.FacilityWarningChance, Count(ops.RoomId));
        return mult;
    }

    // 방해공작 성공 확률에 걸리는 배율(경비 인력이 많으면 낮아진다).
    public static float SabotageChanceMultiplier()
    {
        float mult = 1f;
        foreach (var ops in OpsProfile.AllRooms())
            mult *= OpsProfile.Curve(ops.SabotageChance, Count(ops.RoomId));
        return mult;
    }

    // 코어 복구 1회당 실제 자재 소모량(저장고 인력이 많으면 줄어든다).
    public static int CoreMaterialCost()
    {
        float mult = 1f;
        foreach (var ops in OpsProfile.AllRooms())
            mult *= OpsProfile.Curve(ops.MaterialCost, Count(ops.RoomId));
        int baseCost = Config.Instance?.Data?.MaterialsPerCoreGauge ?? 2;
        return Mathf.Max(1, Mathf.RoundToInt(baseCost * mult));
    }

    // 코어 복구 1% 당 실제 자재 소모량. 관리자가 실제로 세는 단위가 % 라서
    // 화면(화면 위 자재 줄 · 저장고 방 카드)은 전부 이 값을 쓴다.
    public static int CoreMaterialCostPerPercent()
    {
        float perRun = Sim?.GetTaskDef("core_direct_repair")?.EffectAmount ?? 1f;
        return Mathf.Max(1, Mathf.RoundToInt(CoreMaterialCost() / Mathf.Max(0.01f, perRun)));
    }

    // 저장고를 비웠을 때 자재 소모가 몇 % 늘어나는가(0 이면 변화 없음).
    public static float EmptyStorageWastePercent()
    {
        var ops = OpsProfile.Room("storage_room");
        if (ops == null) return 0f;
        return (OpsProfile.Curve(ops.MaterialCost, 0) - 1f) * 100f;
    }

    // 무인 방치 사고까지의 시간. 0 이하면 "비워 둬도 사고가 나지 않는다".
    public static float UnstaffedAccidentSeconds(string roomId, NSP.Data.RoomDef def)
    {
        var ops = OpsProfile.Room(roomId);
        if (ops != null) return ops.UnstaffedAccidentSeconds;
        if (def == null) return 0f;
        return def.UnstaffedAccidentSeconds > 0f
            ? def.UnstaffedAccidentSeconds
            : Config.Instance?.Data?.UnstaffedAccidentSecondsDefault ?? 25f;
    }

    public static float RepairSeconds(string roomId, NSP.Data.RoomDef def)
    {
        var ops = OpsProfile.Room(roomId);
        if (ops != null && ops.RepairSeconds > 0f) return ops.RepairSeconds;
        return Mathf.Max(1f, def?.RepairSeconds ?? 15f);
    }

    public static int RepairMinWorkers(string roomId, NSP.Data.RoomDef def)
    {
        var ops = OpsProfile.Room(roomId);
        if (ops != null && ops.RepairMinWorkers > 0) return ops.RepairMinWorkers;
        return Mathf.Max(1, def?.RepairMinWorkers ?? 1);
    }
}
