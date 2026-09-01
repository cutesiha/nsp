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
            if (taboo.ConditionType == TabooConditionType.MinHeadcountInRoomAfterHour)
                CheckMinHeadcountAfterHour(taboo, room);
        }
    }

    // 시간 기반 조건(예: 정원 초과가 일정 시간 이상 지속)은 매 프레임 검사해야 하므로
    // 방 입장/퇴장 이벤트가 아니라 FacilitySimulation.Tick()에서 매 프레임 호출된다.
    public void Tick(float delta)
    {
        foreach (var taboo in GetActiveTaboos())
        {
            if (taboo.ConditionType == TabooConditionType.MaxHeadcountInRoom)
                TickMaxHeadcount(taboo, delta);
        }
    }

    private bool RoomMatches(TabooDef taboo, string actualRoomId)
    {
        string targetRoomId = taboo.ConditionParams.GetValueOrDefault("room_id", "").AsString();
        return targetRoomId == actualRoomId;
    }

    private void TickMaxHeadcount(TabooDef taboo, float delta)
    {
        string roomId = taboo.ConditionParams.GetValueOrDefault("room_id", "").AsString();
        var room = FacilitySimulation.Instance.GetRoomState(roomId);
        if (room == null) return;

        int max = taboo.ConditionParams.GetValueOrDefault("max", 999).AsInt32();
        float holdSeconds = taboo.ConditionParams.GetValueOrDefault("hold_seconds", 0f).AsSingle();

        if (room.OccupantEmployeeIds.Count <= max)
        {
            // 인원이 정상으로 돌아옴 = 이번 위반 상태 종료. 다음 위반을 위해 완전히 초기화한다.
            room.TabooHoldTimers[taboo.TabooId] = 0f;
            return;
        }

        float timer = room.TabooHoldTimers.GetValueOrDefault(taboo.TabooId, 0f);
        if (timer < 0f) return; // 이미 이번 위반 상태에서 발동함(잠금) — 위 조건대로 정상화되기 전까지 재발동 안 함

        timer += delta;
        if (timer >= holdSeconds)
        {
            var roomDef = FacilitySimulation.Instance.GetRoomDef(roomId);
            Violate(taboo, "", roomId,
                $"{roomDef?.DisplayName ?? roomId}에 {room.OccupantEmployeeIds.Count}명이 {holdSeconds:0}초 이상 함께 있었다 (금기: 최대 {max}명). " +
                "조명이 점멸하고 발전 설비가 과부하로 전력 여유가 줄었다.");
            timer = -1f; // 발동 완료(잠금) 표시
        }
        room.TabooHoldTimers[taboo.TabooId] = timer;
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
            $"⚠ 금기 위반 — {description}");

        // 일부 금기는 결과 적용을 전용 연출 이벤트(예: PowerRoomTabooEvent)에 맡긴다 —
        // 위반 로그는 즉시 남기되, 실제 운영 페널티는 연출이 끝나는 타이밍에 적용한다.
        if (taboo.ConditionParams.GetValueOrDefault("defer_consequence", false).AsBool())
            return;

        ApplyConsequence(taboo, roomId);
    }

    // 지연된 금기 결과를 나중에 적용한다(defer_consequence 금기 전용).
    public void ApplyDeferredConsequence(string tabooId, string roomId)
    {
        var taboo = _tabooDefs.GetValueOrDefault(tabooId);
        if (taboo != null) ApplyConsequence(taboo, roomId);
    }

    private void ApplyConsequence(TabooDef taboo, string roomId)
    {
        float stressAmount = taboo.ConsequenceParams.GetValueOrDefault("amount", 10f).AsSingle();
        ApplyRoomConsequence(taboo.ConsequenceType, roomId, stressAmount);
    }

    public void ApplyRoomConsequence(TabooConsequenceType type, string roomId, float stressAmount = 10f)
    {
        var room = FacilitySimulation.Instance.GetRoomState(roomId);
        string roomName = FacilitySimulation.Instance.GetRoomDef(roomId)?.DisplayName ?? roomId;
        switch (type)
        {
            case TabooConsequenceType.PowerOutage:
                if (room != null) room.PowerOn = false;
                EventLog.Instance?.LogEvent(LogEventType.PowerOutage, "", roomId, $"⚠ {roomName} 정전 발생");
                break;
            case TabooConsequenceType.CctvDisconnect:
                if (room != null) room.CctvDisconnected = true;
                EventLog.Instance?.LogEvent(LogEventType.CctvDisconnect, "", roomId, $"⚠ {roomName} CCTV 단절");
                break;
            case TabooConsequenceType.CorridorLock:
                if (room != null) room.Locked = true;
                EventLog.Instance?.LogEvent(LogEventType.Relocation, "", roomId, $"⚠ {roomName} 통로 봉쇄");
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
            case TabooConsequenceType.PowerCapacityLoss:
                GameState.Instance.TriggerPowerAccident(Mathf.RoundToInt(stressAmount));
                EventLog.Instance?.LogEvent(LogEventType.PowerOutage, "", roomId,
                    $"⚠ {roomName} 발전 설비 과부하 — 최대 사용 가능 전력 감소");
                break;
            case TabooConsequenceType.CctvSystemFault:
                GameState.Instance.SetCctvSystemOffline(true);
                EventLog.Instance?.LogEvent(LogEventType.CctvDisconnect, "", roomId,
                    $"⚠ CCTV 시스템 강제 OFFLINE — {roomName} 감시 설비 고장 (수리 필요)");
                break;
            case TabooConsequenceType.MaterialsHalt:
                GameState.Instance.SetMaterialsProductionHalted(true);
                EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
                    $"⚠ {roomName} 설비 파손 — 자재 생산 정지 (수리 필요)");
                break;
            case TabooConsequenceType.VentilationFault:
                GameState.Instance.SetVentilationDown(true);
                EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
                    $"⚠ {roomName} 환기 정지 — 전 직원 스트레스 지속 상승 (수리 필요)");
                break;
        }
    }

    // 실패한 사고 업무를 "수리" 로 완료했을 때, 걸려 있던 시설 페널티를 되돌린다.
    public void RepairRoomConsequence(TabooConsequenceType type, string roomId)
    {
        var room = FacilitySimulation.Instance.GetRoomState(roomId);
        switch (type)
        {
            case TabooConsequenceType.PowerOutage: if (room != null) room.PowerOn = true; break;
            case TabooConsequenceType.CctvDisconnect: if (room != null) room.CctvDisconnected = false; break;
            case TabooConsequenceType.CorridorLock: if (room != null) room.Locked = false; break;
            case TabooConsequenceType.ObservationCorruption: if (room != null) room.InfoDistorted = false; break;
            case TabooConsequenceType.PowerCapacityLoss: GameState.Instance.RepairPowerAccident(); break;
            case TabooConsequenceType.CctvSystemFault: GameState.Instance.SetCctvSystemOffline(false); break;
            case TabooConsequenceType.MaterialsHalt: GameState.Instance.SetMaterialsProductionHalted(false); break;
            case TabooConsequenceType.VentilationFault: GameState.Instance.SetVentilationDown(false); break;
        }
    }
}
