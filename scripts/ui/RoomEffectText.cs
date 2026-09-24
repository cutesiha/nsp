using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

// 작업실 일곱 개가 "지금 무엇을 하고 있는지"를 한 줄로 만든다.
//
// 이 파일은 **표시 전용**이다. 시뮬레이션의 값을 읽어 문장으로 옮기기만 하고,
// 어떤 판정도 하지 않으며 어떤 수치도 바꾸지 않는다.
//
// 방마다 두 줄을 만든다.
//   Headline : 그 방의 핵심 수치 한 줄 — 사람이 있든 없든 늘 보인다.
//   Idle     : 방이 비어 효과가 끊겼을 때 "무엇을 잃고 있는지" — 붉게 쓴다.
//
// 문장에 새 사실을 만들지 않는다. 전부 config / OpsProfile / GameState 의 실제 값이다.
public static class RoomEffectText
{
    // 붉은 줄에 쓰는 색(화면 쪽에서 가져다 쓴다).
    public static readonly Color IdleInk = new(1f, 0.46f, 0.40f);

    // ── (a) 핵심 수치 ────────────────────────────────────────────────
    public static string Headline(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        if (sim == null || gs == null) return "";
        var cfg = Config.Instance?.Data;
        if (cfg == null) return "";

        switch (roomId)
        {
            case "core_room":
                return $"복구 +{RoomEffectStats.CoreUpToday:0}% 오늘 · 자재 {gs.Materials}개 남음";

            case "power_room":
            {
                float output = RoomStaffing.FacilityOutput() * 100f;
                string state = gs.IsPowerAccidentActive() ? "정지" : output >= 90f ? "안정" : "불안정";
                return $"출력 {output:0}% {state}";
            }

            case "maintenance_room":
            {
                var task = sim.GetPrimarySpawnedTask(roomId);
                var def = task == null ? null : sim.GetTaskDef(task.TaskId);
                string next = def != null && def.EffectType == TaskEffectType.AddMaterials
                    ? $" · 다음 생산까지 {Mathf.Max(0f, def.GaugeRequired - task.Gauge):0}초"
                    : "";
                return $"자재 생산 +{RoomEffectStats.MaterialsToday}개 오늘{next}";
            }

            case "storage_room":
                return $"코어 1%당 자재 {RoomStaffing.CoreMaterialCost()}개";

            case "vent_room":
            {
                if (!DayFeatures.StressEnabled) return "오늘은 영향 없음";
                var alive = sim.GetActiveEmployeeIds()
                    .Select(sim.GetEmployeeState).Where(x => x is { Alive: true }).ToList();
                float avg = alive.Count == 0 ? 0f : alive.Average(x => x.Stress);
                return $"직원 평균 스트레스 {avg:0} / {cfg.StressMax:0}";
            }

            case "guard_room":
                return $"오늘 기록 {RoomEffectStats.GuardRecordsToday}건 (순찰 · 이탈 · 센서)";

            case "medical_room":
            {
                var patient = sim.GetActiveEmployeeIds()
                    .Select(sim.GetEmployeeState)
                    .FirstOrDefault(x => x is { Alive: true, Incapacitated: true });
                if (patient == null) return "비어 있음";
                string who = sim.GetEmployeeDef(patient.EmployeeId)?.Codename ?? patient.EmployeeId;
                return $"치료 중: {who} (회복까지 {Mathf.Max(0f, patient.FaintRecoverTimer):0}초)";
            }
        }
        return "";
    }

    // ── (c) 비었을 때 잃는 것 ────────────────────────────────────────
    // 빈 문자열이면 지금은 잃고 있는 것이 없다는 뜻이다.
    public static string Idle(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        var cfg = Config.Instance?.Data;
        if (sim == null || gs == null || cfg == null) return "";
        int here = sim.OnDutyCount(roomId);

        switch (roomId)
        {
            case "core_room":
                if (here == 0) return "코어실 비어 있음 — 복구 정지";
                if (sim.IsRoomBlockedByMaterials(roomId)) return "자재 없음 — 복구 정지 중 (정비실 확인)";
                return "";

            case "power_room":
                return here == 0 ? "발전실 비어 있음 — 출력 35% · 조명·CCTV 불안정" : "";

            case "maintenance_room":
            {
                if (here > 0) return "";
                int cost = Mathf.Max(1, RoomStaffing.CoreMaterialCost());
                return $"정비실 비어 있음 — 자재 생산 중단 ({gs.Materials}개로 코어 약 {gs.Materials / cost}% 분)";
            }

            case "storage_room":
                return here == 0 ? "저장고 비어 있음 — 자재 소모 +25%" : "";

            case "vent_room":
            {
                if (!DayFeatures.StressEnabled) return "";
                bool down = gs.VentilationDown;
                if (here > 0 && !down) return "";
                string line = $"환기실 {(down ? "고장" : "비어 있음")} — 전원 스트레스 " +
                              $"+{cfg.VentUnstaffedStressAmount:0.#} / {cfg.VentUnstaffedStressIntervalSeconds:0}초";
                // 기절선에 가까워진 사람이 있으면 이름을 붙인다 — 숫자보다 이름이 먼저 읽힌다.
                var worst = sim.GetActiveEmployeeIds()
                    .Select(sim.GetEmployeeState)
                    .Where(x => x is { Alive: true, Incapacitated: false } && x.Stress >= 40f)
                    .OrderByDescending(x => x.Stress).FirstOrDefault();
                if (worst != null)
                    line += $"\n{sim.GetEmployeeDef(worst.EmployeeId)?.Codename ?? worst.EmployeeId} " +
                            $"스트레스 {worst.Stress:0} — 기절 임박";
                return line;
            }

            case "guard_room":
                return here == 0 ? "경비실 비어 있음 — 재석 기록 없음 (휴게시간 조사 자료 감소)" : "";

            case "medical_room":
            {
                bool anyPatient = sim.GetActiveEmployeeIds()
                    .Select(sim.GetEmployeeState).Any(x => x is { Alive: true, Incapacitated: true });
                return anyPatient && here == 0 ? "의무실 비어 있음 — 회복 지연" : "";
            }
        }
        return "";
    }
}
