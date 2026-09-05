using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;

namespace NSP.Core;

// 경고 단말기와 미니맵이 함께 보는 "지금 시설이 어떤 상태인가" 목록.
//
//   · 아직 사고가 아닌 위험(무인 방치 타이머 / 처리 시한이 남은 업무 / 금기 홀드)은
//     매번 시뮬레이션 상태에서 새로 계산한다.
//   · 이미 발생한 사고는 IncidentTracker 가 해결될 때까지 들고 있다.
//   · 작업실 "무인 페널티"(스트레스 상승)는 사고와 별개의 운영 상태로 따로 표시한다.
//
// 이 클래스는 아무것도 바꾸지 않는다. 읽기 전용 표시 계층이다.
public static class IncidentBoard
{
    // 사고 임박으로 볼 남은 시간(초). 이보다 길면 주의 단계.
    private const float WarningThreshold = 12f;

    private static List<IncidentDisplayData> _cache = new();
    private static ulong _cacheAt;

    // 미니맵·모니터가 방마다 호출하므로 짧게 캐시한다(표시용이라 0.2초 지연은 문제없다).
    public static List<IncidentDisplayData> Snapshot()
    {
        ulong now = Time.GetTicksMsec();
        if (_cacheAt != 0 && now - _cacheAt < 200) return _cache;
        _cacheAt = now;
        _cache = Build();
        return _cache;
    }

    // 그 작업실에서 가장 급한 항목(운영 상태 제외). 없으면 null.
    public static IncidentDisplayData ForRoom(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return null;
        return Snapshot().FirstOrDefault(x => x.RoomId == roomId && !x.IsOperational);
    }

    public static List<IncidentDisplayData> Build()
    {
        var list = new List<IncidentDisplayData>();
        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        if (sim == null || gs == null) return list;

        // ① 이미 발생해 아직 해결되지 않은 사고.
        list.AddRange(IncidentTracker.Active);

        // ② 아직 사고가 아닌 위험 — 센서가 꺼져 있으면 이 예측 정보가 끊긴다.
        if (gs.IsConsumerPowered(PowerConsumer.Sensor))
        {
            AddUnstaffedRisks(sim, list);
            AddTaskRisks(sim, list);
            AddProtocolRisks(sim, list);
        }

        // ③ 사고와 구분되는 운영 상태(무인 페널티).
        AddOperationalStatus(sim, gs, list);

        return list.OrderBy(Rank).ThenBy(x => x.WarningRemainingSeconds < 0f
            ? float.MaxValue
            : x.WarningRemainingSeconds).ToList();
    }

    // 우선순위: 임박한 위험 → 발생한 사고 → 주의 → 운영 상태.
    private static int Rank(IncidentDisplayData d)
    {
        if (d.IsOperational) return 4;
        return d.State switch
        {
            IncidentState.Warning => 0,
            IncidentState.Active => 1,
            IncidentState.Caution => 2,
            _ => 3,
        };
    }

    // 근무자가 없어 사고 타이머가 도는 작업실.
    private static void AddUnstaffedRisks(FacilitySimulation sim, List<IncidentDisplayData> list)
    {
        var cfg = Config.Instance?.Data;
        foreach (string roomId in sim.GetRoomIds())
        {
            var def = sim.GetRoomDef(roomId);
            var room = sim.GetRoomState(roomId);
            if (def == null || room == null || def.IsRestricted) continue;
            if ((int)def.AccidentConsequence < 0) continue;
            if (IncidentTracker.HasActive(roomId)) continue;
            if (room.UnstaffedTimer <= 0.05f) continue;

            float limit = def.UnstaffedAccidentSeconds > 0f
                ? def.UnstaffedAccidentSeconds
                : cfg?.UnstaffedAccidentSecondsDefault ?? 25f;
            float remaining = Mathf.Max(0f, limit - room.UnstaffedTimer);

            list.Add(new IncidentDisplayData
            {
                IncidentId = $"risk:unstaffed:{roomId}",
                RoomId = roomId,
                Title = string.IsNullOrEmpty(def.AccidentName) ? "설비 이상" : def.AccidentName,
                State = remaining <= WarningThreshold ? IncidentState.Warning : IncidentState.Caution,
                CauseText = "근무자 부재",
                WarningRemainingSeconds = remaining,
                ActionHint = "직원 배치 필요",
                Severity = remaining <= WarningThreshold ? AlertSeverity.Critical : AlertSeverity.Warning,
                RepairWorkers = Mathf.Max(1, def.RepairMinWorkers),
                ConsequenceLines = { ConsequenceText(def.AccidentConsequence, def.AccidentAmount) },
            });
        }
    }

    // 제한시간이 붙은 업무를 처리하지 못하면 그 방의 설비가 고장 난다.
    private static void AddTaskRisks(FacilitySimulation sim, List<IncidentDisplayData> list)
    {
        foreach (string roomId in sim.GetRoomIds())
        {
            if (IncidentTracker.HasActive(roomId)) continue;
            foreach (var st in sim.GetActiveTasksForRoom(roomId))
            {
                if (st.Status != SpawnedTaskStatus.Active || st.Recurring || st.IsRepair) continue;
                var taskDef = sim.GetTaskDef(st.TaskId);
                if (taskDef is not { HasNeglectConsequence: true }) continue;

                float remaining = st.Remaining;
                list.Add(new IncidentDisplayData
                {
                    IncidentId = $"risk:task:{st.TaskId}:{roomId}",
                    RoomId = roomId,
                    Title = AlertSystem.HeadlineFor(st.TaskId),
                    State = remaining <= WarningThreshold ? IncidentState.Warning : IncidentState.Caution,
                    CauseText = "점검 미수행",
                    WarningRemainingSeconds = remaining,
                    ActionHint = $"업무 처리 필요 (최소 {Mathf.Max(1, taskDef.MinWorkersToProgress)}명)",
                    Severity = remaining <= WarningThreshold ? AlertSeverity.Critical : AlertSeverity.Warning,
                    RepairWorkers = Mathf.Max(1, taskDef.MinWorkersToProgress),
                    ConsequenceLines = { ConsequenceText(taskDef.NeglectConsequenceType, taskDef.NeglectConsequenceAmount) },
                });
            }
        }
    }

    // 금기 위반 임박 — 사고가 아니라 규정 위반이다.
    private static void AddProtocolRisks(FacilitySimulation sim, List<IncidentDisplayData> list)
    {
        foreach (string roomId in sim.GetRoomIds())
        {
            var room = sim.GetRoomState(roomId);
            if (room == null) continue;
            foreach (var kv in room.TabooHoldTimers)
            {
                if (kv.Value <= 0f) continue;
                var taboo = TabooRuleSystem.Instance?.GetTaboo(kv.Key);
                float hold = taboo?.ConditionParams.GetValueOrDefault("hold_seconds", 0f).AsSingle() ?? 0f;
                if (hold <= 0f) continue;
                float remaining = Mathf.Max(0f, hold - kv.Value);

                list.Add(new IncidentDisplayData
                {
                    IncidentId = $"risk:taboo:{kv.Key}:{roomId}",
                    RoomId = roomId,
                    Title = taboo?.ConditionParams.GetValueOrDefault("alert_headline", "금기 위반 임박").AsString()
                            ?? "금기 위반 임박",
                    State = IncidentState.Warning,
                    CauseText = "금기 조건 유지 중",
                    WarningRemainingSeconds = remaining,
                    ActionHint = "조건 해제 필요",
                    Severity = AlertSeverity.Critical,
                    IsProtocol = true,
                    ConsequenceLines = { "이상현상 발생" },
                });
            }
        }
    }

    // 사고와 구분되는 운영 상태. 지금은 환기실 무인 시의 전 직원 스트레스 상승.
    private static void AddOperationalStatus(FacilitySimulation sim, GameState gs, List<IncidentDisplayData> list)
    {
        var cfg = Config.Instance?.Data;
        foreach (string roomId in sim.GetRoomIds())
        {
            var def = sim.GetRoomDef(roomId);
            if (def == null || def.ManagedResource != RoomResourceType.Stress) continue;
            if (sim.OnDutyCount(roomId) > 0) continue;

            list.Add(new IncidentDisplayData
            {
                IncidentId = $"ops:{roomId}",
                RoomId = roomId,
                Title = "무인 운영",
                State = IncidentState.Caution,
                CauseText = "근무자 부재",
                ActionHint = "직원 배치 필요",
                Severity = AlertSeverity.Notice,
                IsOperational = true,
                ConsequenceLines =
                {
                    $"전 직원 스트레스 +{cfg?.VentUnstaffedStressAmount ?? 1f:0.#} / {cfg?.VentUnstaffedStressIntervalSeconds ?? 15f:0}초",
                },
            });
        }
    }

    // 사고 결과를 사람이 읽는 한 줄로.
    public static string ConsequenceText(TabooConsequenceType type, float amount) => type switch
    {
        TabooConsequenceType.PowerOutage => "정전",
        TabooConsequenceType.PowerCapacityLoss => $"사용 가능 전력 -{amount:0}",
        TabooConsequenceType.CctvDisconnect => "CCTV 신호 단절",
        TabooConsequenceType.CctvSystemFault => "CCTV 시스템 정지 (수리 전까지)",
        TabooConsequenceType.MaterialsHalt => "자재 생산 정지",
        TabooConsequenceType.VentilationFault => "환기 정지 · 전 직원 스트레스 상승",
        TabooConsequenceType.MedicalContamination => "스트레스 치료 불가",
        TabooConsequenceType.CoreOutputUnstable => "코어 복구 정지 · 복구율 감소",
        TabooConsequenceType.StorageCollapse => $"자재 보유 한도 -{amount:0}",
        TabooConsequenceType.CorridorLock => "통로 봉쇄",
        TabooConsequenceType.ObservationCorruption => "정보 왜곡",
        TabooConsequenceType.StressIncrease => $"스트레스 +{amount:0}",
        _ => "시설 기능 손실",
    };
}
