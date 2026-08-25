using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Taboo;

public partial class TabooRuleSystem : Node
{
    public static TabooRuleSystem Instance { get; private set; }

    private readonly Dictionary<string, TabooDef> _tabooDefs = new();
    public List<string> ActiveTabooIds { get; private set; } = new();

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        using var dir = DirAccess.Open("res://data/taboos/");
        if (dir == null)
        {
            GD.PushWarning("TabooRuleSystem: res://data/taboos/ not found");
            return;
        }
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            if (fileName.EndsWith(".tres"))
            {
                var res = GD.Load<TabooDef>("res://data/taboos/" + fileName);
                if (res != null)
                    _tabooDefs[res.TabooId] = res;
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    public IEnumerable<TabooDef> GetAllTaboos() => _tabooDefs.Values;
    public TabooDef GetTaboo(string id) => _tabooDefs.GetValueOrDefault(id);

    public void ActivateDailyTaboos(IEnumerable<string> tabooIds)
    {
        ActiveTabooIds = tabooIds.ToList();
    }

    public IEnumerable<TabooDef> GetActiveTaboos() => ActiveTabooIds.Select(id => _tabooDefs.GetValueOrDefault(id)).Where(t => t != null);

    public bool IsRoomAtTabooRisk(string roomId)
    {
        foreach (var taboo in GetActiveTaboos())
        {
            if (taboo.ConditionType is TabooConditionType.MaxHeadcountInRoom or TabooConditionType.MinHeadcountInRoomAfterHour
                && taboo.ConditionParams.GetValueOrDefault("room_id", "").AsString() == roomId)
            {
                return true;
            }
        }
        return false;
    }

    public void EvaluateOnRoomChange(string employeeId, string roomId)
    {
        var room = FacilitySimulation.Instance.GetRoomState(roomId);
        if (room == null) return;

        foreach (var taboo in GetActiveTaboos())
        {
            switch (taboo.ConditionType)
            {
                case TabooConditionType.MaxHeadcountInRoom:
                    CheckMaxHeadcount(taboo, room);
                    break;
                case TabooConditionType.MinHeadcountInRoomAfterHour:
                    CheckMinHeadcountAfterHour(taboo, room);
                    break;
            }
        }
    }

    private bool RoomMatches(TabooDef taboo, string actualRoomId)
    {
        string targetRoomId = taboo.ConditionParams.GetValueOrDefault("room_id", "").AsString();
        return targetRoomId == actualRoomId;
    }

    private void CheckMaxHeadcount(TabooDef taboo, RoomState room)
    {
        if (!RoomMatches(taboo, room.RoomId)) return;
        int max = taboo.ConditionParams.GetValueOrDefault("max", 999).AsInt32();
        if (room.OccupantEmployeeIds.Count > max)
        {
            Violate(taboo, "", room.RoomId,
                $"{room.RoomId}에 {room.OccupantEmployeeIds.Count}명 동시 배치 (금기: 최대 {max}명)");
        }
    }

    private void CheckMinHeadcountAfterHour(TabooDef taboo, RoomState room)
    {
        if (!RoomMatches(taboo, room.RoomId)) return;
        float afterSeconds = taboo.ConditionParams.GetValueOrDefault("after_seconds", 0f).AsSingle();
        if (GameState.Instance.DayTimeSeconds < afterSeconds) return;

        int min = taboo.ConditionParams.GetValueOrDefault("min", 2).AsInt32();
        if (room.OccupantEmployeeIds.Count > 0 && room.OccupantEmployeeIds.Count < min)
        {
            Violate(taboo, room.OccupantEmployeeIds.FirstOrDefault(), room.RoomId,
                $"{room.RoomId}에 {room.OccupantEmployeeIds.Count}명만 있음 (금기: 최소 {min}명, 시간 경과)");
        }
    }

    public void CheckCodenameUnderRedLight(string speakerEmployeeId, string roomId)
    {
        var room = FacilitySimulation.Instance.GetRoomState(roomId);
        if (room == null || !room.RedAlertLighting) return;

        foreach (var taboo in GetActiveTaboos())
        {
            if (taboo.ConditionType == TabooConditionType.CodenameSpokenUnderRedLight)
            {
                Violate(taboo, speakerEmployeeId, roomId, $"적색등 아래에서 코드네임 호명 ({roomId})");
            }
        }
    }

    private void Violate(TabooDef taboo, string actorEmployeeId, string roomId, string description)
    {
        EventLog.Instance?.LogEvent(LogEventType.TabooViolation, actorEmployeeId, roomId,
            $"[금기 위반: {taboo.TabooId}] {description}");
        ApplyConsequence(taboo, roomId);
    }

    private void ApplyConsequence(TabooDef taboo, string roomId)
    {
        float stressAmount = taboo.ConsequenceParams.GetValueOrDefault("amount", 10f).AsSingle();
        ApplyRoomConsequence(taboo.ConsequenceType, roomId, stressAmount);
    }

    public void ApplyRoomConsequence(TabooConsequenceType type, string roomId, float stressAmount = 10f)
    {
        var room = FacilitySimulation.Instance.GetRoomState(roomId);
        switch (type)
        {
            case TabooConsequenceType.PowerOutage:
                if (room != null) room.PowerOn = false;
                EventLog.Instance?.LogEvent(LogEventType.PowerOutage, "", roomId, $"{roomId} 정전 발생");
                break;
            case TabooConsequenceType.CctvDisconnect:
                if (room != null) room.CctvDisconnected = true;
                EventLog.Instance?.LogEvent(LogEventType.CctvDisconnect, "", roomId, $"{roomId} CCTV 단절");
                break;
            case TabooConsequenceType.CorridorLock:
                if (room != null) room.Locked = true;
                break;
            case TabooConsequenceType.ObservationCorruption:
                if (room != null) room.InfoDistorted = true;
                break;
            case TabooConsequenceType.StressIncrease:
                foreach (var id in room?.OccupantEmployeeIds ?? new List<string>())
                {
                    var emp = FacilitySimulation.Instance.GetEmployeeState(id);
                    if (emp != null)
                        emp.Stress = Mathf.Clamp(emp.Stress + stressAmount, 0f, Config.Instance.Data.StressMax);
                }
                break;
        }
    }
}
