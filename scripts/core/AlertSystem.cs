using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;

namespace NSP.Core;

public enum AlertKind { TaskWarning, TabooRisk, MaterialLow }
public enum AlertSeverity { Notice, Warning, Critical }

// SENSOR 채널이 보여주는 사고 예고 한 건. 새 위험 판정을 하지 않는다 — 이미 존재하는
// SpawnedTask.Remaining / TabooRuleSystem 홀드 타이머 / GameState.Materials 를 매 프레임
// 다시 읽어 "곧 벌어질 일" 목록으로 정리만 한다.
public class Alert
{
    public string Key = "";
    public AlertKind Kind;
    public string RoomId = "";
    public string Headline = "";
    public string Detail = "";
    public float TimeRemaining = -1f; // -1 = 카운트다운 없음(자재 부족 등)
    public AlertSeverity Severity;
}

public partial class AlertSystem : Node
{
    public static AlertSystem Instance { get; private set; }

    private static readonly Dictionary<string, string> RoomTag = new()
    {
        ["power_room"] = "GENERATOR",
        ["vent_room"] = "VENT",
        ["guard_room"] = "LOCKDOWN",
        ["maintenance_room"] = "EQUIPMENT",
        ["medical_room"] = "MEDICAL",
        ["core_room"] = "CORE",
    };

    public override void _EnterTree() => Instance = this;

    // 심각도(Critical > Warning > Notice) → 남은시간 짧은 순으로 정렬해 반환한다.
    // 화면 쪽(AlertTerminalView)은 이 목록의 맨 앞(가장 급한 것) 하나만 보여준다.
    public List<Alert> GetActiveAlerts()
    {
        var list = new List<Alert>();
        var sim = FacilitySimulation.Instance;
        if (sim == null || GameState.Instance?.CurrentPhase != GamePhase.Live) return list;

        float lead = Config.Instance?.Data?.AlertLeadSeconds ?? 20f;

        foreach (var roomId in sim.GetRoomIds())
        {
            var roomDef = sim.GetRoomDef(roomId);
            if (roomDef == null || roomDef.IsRestricted) continue;

            foreach (var st in sim.GetActiveTasksForRoom(roomId))
            {
                if (st.Status != SpawnedTaskStatus.Active || st.Recurring) continue;
                var taskDef = sim.GetTaskDef(st.TaskId);
                if (taskDef is not { HasNeglectConsequence: true } || st.Remaining > lead) continue;

                list.Add(new Alert
                {
                    Key = $"task:{st.TaskId}:{roomId}",
                    Kind = AlertKind.TaskWarning,
                    RoomId = roomId,
                    Headline = $"{Tag(roomId)} WARNING",
                    Detail = $"{roomDef.DisplayName} 고장 예상",
                    TimeRemaining = st.Remaining,
                    Severity = st.Remaining <= 8f ? AlertSeverity.Critical : AlertSeverity.Warning,
                });
            }

            var roomState = sim.GetRoomState(roomId);
            foreach (var kv in roomState?.TabooHoldTimers ?? new Dictionary<string, float>())
            {
                if (kv.Value <= 0f) continue;
                var taboo = TabooRuleSystem.Instance?.GetTaboo(kv.Key);
                float hold = taboo?.ConditionParams.GetValueOrDefault("hold_seconds", 0f).AsSingle() ?? 0f;
                if (hold <= 0f) continue;
                float remaining = Mathf.Max(0f, hold - kv.Value);

                list.Add(new Alert
                {
                    Key = $"taboo:{kv.Key}:{roomId}",
                    Kind = AlertKind.TabooRisk,
                    RoomId = roomId,
                    Headline = "PROTOCOL RISK",
                    Detail = $"{roomDef.DisplayName} 인원 제한 초과",
                    TimeRemaining = remaining,
                    Severity = remaining <= 5f ? AlertSeverity.Critical : AlertSeverity.Warning,
                });
            }
        }

        int materials = GameState.Instance.Materials;
        int lowThreshold = Config.Instance?.Data?.MaterialsPerCoreGauge ?? 5;
        if (materials <= lowThreshold)
        {
            list.Add(new Alert
            {
                Key = "material_low",
                Kind = AlertKind.MaterialLow,
                RoomId = "",
                Headline = "MATERIAL LOW",
                Detail = $"CORE MATERIAL · {materials} REMAINING",
                TimeRemaining = -1f,
                Severity = AlertSeverity.Notice,
            });
        }

        return list
            .OrderBy(a => a.Severity == AlertSeverity.Critical ? 0 : a.Severity == AlertSeverity.Warning ? 1 : 2)
            .ThenBy(a => a.TimeRemaining < 0f ? float.MaxValue : a.TimeRemaining)
            .ToList();
    }

    private static string Tag(string roomId) => RoomTag.GetValueOrDefault(roomId, roomId.ToUpperInvariant());
}
