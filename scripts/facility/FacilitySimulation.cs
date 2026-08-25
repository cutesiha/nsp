using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Taboo;

namespace NSP.Facility;

public partial class FacilitySimulation : Node
{
    public static FacilitySimulation Instance { get; private set; }

    private const string IsolationRoomId = "isolation_room";
    private const string GuardRoomId = "guard_room";
    private const string CentralOfficeId = "central_office";
    private const int RoomSlotCapacity = 2;

    public string RelocatingEmployeeId { get; private set; } = "";

    private readonly Dictionary<string, EmployeeDef> _employeeDefs = new();
    private readonly Dictionary<string, RoomDef> _roomDefs = new();
    private readonly Dictionary<string, TaskDef> _taskDefs = new();

    private readonly Dictionary<string, EmployeeState> _employeeStates = new();
    private readonly Dictionary<string, RoomState> _roomStates = new();
    private readonly Dictionary<string, Vector2> _roomVisualCenters = new();
    private readonly Dictionary<string, Color> _roomVisualColors = new();
    private readonly Random _rng = new();
    private float _saboteurDecisionTimer = 0f;
    private int _killsToday = 0;

    public string SurveillanceTargetRoomId { get; private set; } = "";

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        LoadDefinitions("res://data/employees/", _employeeDefs, d => d.EmployeeId);
        LoadDefinitions("res://data/rooms/", _roomDefs, d => d.RoomId);
        LoadDefinitions("res://data/tasks/", _taskDefs, d => d.TaskId);

        foreach (var def in _employeeDefs.Values)
        {
            var startRoom = _roomDefs.Values.FirstOrDefault(r => r.RoomId == def.StartRoomId) ?? _roomDefs.Values.FirstOrDefault();
            _employeeStates[def.EmployeeId] = new EmployeeState
            {
                EmployeeId = def.EmployeeId,
                CurrentRoomId = startRoom?.RoomId ?? "",
                Position = GetRoomPosition(startRoom?.RoomId ?? ""),
            };
        }

        foreach (var def in _roomDefs.Values)
        {
            _roomStates[def.RoomId] = new RoomState { RoomId = def.RoomId };
        }

        foreach (var room in _roomStates.Values)
        {
            room.TaskPriorityOrder = _taskDefs.Values
                .Where(t => t.RoomId == room.RoomId)
                .OrderBy(t => t.Priority)
                .Select(t => t.TaskId)
                .ToList();
        }

        foreach (var kv in _employeeStates)
        {
            if (_roomStates.TryGetValue(kv.Value.CurrentRoomId, out var room))
                room.OccupantEmployeeIds.Add(kv.Key);
        }
    }

    private void LoadDefinitions<T>(string folder, Dictionary<string, T> target, System.Func<T, string> idSelector) where T : Resource
    {
        using var dir = DirAccess.Open(folder);
        if (dir == null)
        {
            GD.PushWarning($"FacilitySimulation: data folder not found: {folder}");
            return;
        }
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            if (fileName.EndsWith(".tres"))
            {
                var res = GD.Load<T>(folder + fileName);
                if (res != null)
                    target[idSelector(res)] = res;
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    public IReadOnlyCollection<string> GetEmployeeIds() => _employeeStates.Keys;
    public IReadOnlyCollection<string> GetRoomIds() => _roomStates.Keys;
    public IEnumerable<TaskDef> GetTaskDefs() => _taskDefs.Values;

    public EmployeeDef GetEmployeeDef(string id) => _employeeDefs.GetValueOrDefault(id);
    public RoomDef GetRoomDef(string id) => _roomDefs.GetValueOrDefault(id);
    public TaskDef GetTaskDef(string id) => _taskDefs.GetValueOrDefault(id);
    public EmployeeState GetEmployeeState(string id) => _employeeStates.GetValueOrDefault(id);
    public RoomState GetRoomState(string id) => _roomStates.GetValueOrDefault(id);

    public bool IsSaboteurIsolated()
    {
        string saboteurId = GameState.Instance.SaboteurEmployeeId;
        if (string.IsNullOrEmpty(saboteurId)) return false;
        return _employeeStates.GetValueOrDefault(saboteurId)?.Isolated ?? false;
    }

    public void SetRoomVisualCenter(string roomId, Vector2 center)
    {
        _roomVisualCenters[roomId] = center;

        foreach (var emp in _employeeStates.Values)
        {
            if (emp.CurrentRoomId == roomId && !emp.IsMoving)
                emp.Position = center;
        }
    }

    private Vector2 GetRoomPosition(string roomId)
    {
        if (_roomVisualCenters.TryGetValue(roomId, out var center))
            return center;

        return _roomDefs.GetValueOrDefault(roomId)?.MapPosition ?? Vector2.Zero;
    }

    public void SetRoomVisualColor(string roomId, Color color)
    {
        _roomVisualColors[roomId] = color;
    }

    public Color GetRoomVisualColor(string roomId)
    {
        return _roomVisualColors.TryGetValue(roomId, out var color) ? color : new Color(0.2f, 0.2f, 0.22f);
    }

    public void SetSurveillanceTarget(string roomId)
    {
        SurveillanceTargetRoomId = roomId;
    }

    public bool IsRoomUnderActiveCctv(string roomId)
    {
        return SurveillanceTargetRoomId == roomId && GameState.Instance.GetPowerAllocated(PowerConsumer.CctvWatch) > 0;
    }

    // --- Room task priority queue ---------------------------------------

    public List<TaskDef> GetRoomTasksInPriorityOrder(string roomId)
    {
        if (!_roomStates.TryGetValue(roomId, out var room)) return new List<TaskDef>();
        return room.TaskPriorityOrder.Select(id => _taskDefs.GetValueOrDefault(id)).Where(t => t != null).ToList();
    }

    public TaskDef GetActiveTaskForRoom(string roomId)
    {
        if (!_roomStates.TryGetValue(roomId, out var room) || room.TaskPriorityOrder.Count == 0) return null;
        return _taskDefs.GetValueOrDefault(room.TaskPriorityOrder[0]);
    }

    public float GetTaskGauge(string roomId, string taskId)
    {
        return _roomStates.GetValueOrDefault(roomId)?.TaskGauges.GetValueOrDefault(taskId, 0f) ?? 0f;
    }

    public void ReorderRoomTask(string roomId, string taskId, bool moveUp)
    {
        if (!_roomStates.TryGetValue(roomId, out var room)) return;
        int idx = room.TaskPriorityOrder.IndexOf(taskId);
        if (idx < 0) return;

        int newIdx = moveUp ? idx - 1 : idx + 1;
        if (newIdx < 0 || newIdx >= room.TaskPriorityOrder.Count) return;

        (room.TaskPriorityOrder[idx], room.TaskPriorityOrder[newIdx]) = (room.TaskPriorityOrder[newIdx], room.TaskPriorityOrder[idx]);
    }

    public void MoveTaskToIndex(string roomId, string taskId, int newIndex)
    {
        if (!_roomStates.TryGetValue(roomId, out var room)) return;
        int idx = room.TaskPriorityOrder.IndexOf(taskId);
        if (idx < 0) return;

        newIndex = Mathf.Clamp(newIndex, 0, room.TaskPriorityOrder.Count - 1);
        if (newIndex == idx) return;

        room.TaskPriorityOrder.RemoveAt(idx);
        room.TaskPriorityOrder.Insert(newIndex, taskId);
    }

    // --- Assignment -------------------------------------------------------

    public bool AssignToRoom(string employeeId, string roomId)
    {
        if (!_employeeStates.TryGetValue(employeeId, out var emp) || emp.Isolated || !emp.Alive)
            return false;
        if (!MoveEmployeeTo(employeeId, roomId))
            return false;

        emp.AssignedRoomId = roomId;
        var roomDef = _roomDefs.GetValueOrDefault(roomId);
        EventLog.Instance?.LogEvent(LogEventType.TaskStart, employeeId, roomId, $"{employeeId} → {roomDef?.DisplayName ?? roomId} 배정");
        return true;
    }

    public void ClearAssignment(string employeeId)
    {
        if (!_employeeStates.TryGetValue(employeeId, out var emp)) return;

        EventLog.Instance?.LogEvent(LogEventType.TaskEnd, employeeId, emp.CurrentRoomId, $"{employeeId} 배치 해제");
        emp.AssignedRoomId = "";
        emp.TargetRoomId = emp.CurrentRoomId;
        emp.IsMoving = false;
        emp.PathQueue.Clear();
    }

    public bool MoveEmployeeTo(string employeeId, string roomId)
    {
        if (!_employeeStates.TryGetValue(employeeId, out var emp) || emp.Isolated || !emp.Alive)
            return false;
        if (!_roomStates.TryGetValue(roomId, out var room) || room.Locked)
            return false;
        if (!_roomDefs.TryGetValue(roomId, out var roomDef) || roomDef.IsRestricted)
            return false;

        return BeginPathTo(emp, roomId);
    }

    private bool BeginPathTo(EmployeeState emp, string destinationRoomId)
    {
        if (emp.CurrentRoomId == destinationRoomId)
        {
            emp.PathQueue.Clear();
            emp.TargetRoomId = destinationRoomId;
            emp.IsMoving = false;
            return true;
        }

        var path = FindPath(emp.CurrentRoomId, destinationRoomId);
        if (path.Count == 0) return false;

        emp.PathQueue = path;
        AdvanceToNextWaypoint(emp);
        return true;
    }

    private void AdvanceToNextWaypoint(EmployeeState emp)
    {
        if (emp.PathQueue.Count == 0)
        {
            emp.TargetRoomId = emp.CurrentRoomId;
            emp.IsMoving = false;
            return;
        }

        string next = emp.PathQueue[0];
        emp.PathQueue.RemoveAt(0);
        emp.TargetRoomId = next;
        emp.IsMoving = emp.CurrentRoomId != next;
    }

    private List<string> FindPath(string fromRoomId, string toRoomId)
    {
        var result = new List<string>();
        if (fromRoomId == toRoomId || !_roomDefs.ContainsKey(fromRoomId) || !_roomDefs.ContainsKey(toRoomId))
            return result;

        var cameFrom = new Dictionary<string, string>();
        var visited = new HashSet<string> { fromRoomId };
        var queue = new Queue<string>();
        queue.Enqueue(fromRoomId);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (current == toRoomId) break;

            var def = _roomDefs.GetValueOrDefault(current);
            if (def == null) continue;

            foreach (var neighborId in def.ConnectedRoomIds)
            {
                if (!_roomDefs.ContainsKey(neighborId) || !visited.Add(neighborId)) continue;
                cameFrom[neighborId] = current;
                queue.Enqueue(neighborId);
            }
        }

        if (!cameFrom.ContainsKey(toRoomId)) return result;

        string node = toRoomId;
        while (node != fromRoomId)
        {
            result.Add(node);
            node = cameFrom[node];
        }
        result.Reverse();
        return result;
    }

    public void SetRoomLocked(string roomId, bool locked)
    {
        if (!_roomStates.TryGetValue(roomId, out var room)) return;

        room.Locked = locked;
        EventLog.Instance?.LogEvent(LogEventType.Relocation, "", roomId, locked ? $"{roomId} 구역 봉쇄" : $"{roomId} 봉쇄 해제");

        if (locked)
            EvacuateRoom(roomId);
    }

    private void EvacuateRoom(string roomId)
    {
        var occupants = _employeeStates.Values
            .Where(e => e.Alive && !e.Isolated && (e.CurrentRoomId == roomId || e.AssignedRoomId == roomId))
            .Select(e => e.EmployeeId)
            .Distinct()
            .ToList();

        foreach (var employeeId in occupants)
        {
            string fallback = FindNearestAvailableRoom(roomId);
            ClearAssignment(employeeId);
            if (!string.IsNullOrEmpty(fallback))
                AssignToRoom(employeeId, fallback);
        }
    }

    private string FindNearestAvailableRoom(string fromRoomId)
    {
        var visited = new HashSet<string> { fromRoomId };
        var queue = new Queue<string>();
        queue.Enqueue(fromRoomId);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            var def = _roomDefs.GetValueOrDefault(current);
            if (def == null) continue;

            foreach (var neighborId in def.ConnectedRoomIds)
            {
                if (!_roomDefs.ContainsKey(neighborId) || !visited.Add(neighborId)) continue;
                if (CanAssignToRoom(neighborId))
                    return neighborId;
                queue.Enqueue(neighborId);
            }
        }
        return "";
    }

    public int GetAssignedCount(string roomId) =>
        _employeeStates.Values.Count(e => e.Alive && !e.Isolated && e.AssignedRoomId == roomId);

    public bool CanAssignToRoom(string roomId)
    {
        if (!_roomDefs.TryGetValue(roomId, out var def) || def.IsRestricted) return false;
        if (!_roomStates.TryGetValue(roomId, out var state) || state.Locked) return false;
        return GetAssignedCount(roomId) < RoomSlotCapacity;
    }

    public void StartRelocating(string employeeId) => RelocatingEmployeeId = employeeId;
    public void CancelRelocating() => RelocatingEmployeeId = "";

    public bool IsolateEmployee(string employeeId)
    {
        int currentlyIsolated = _employeeStates.Values.Count(e => e.Isolated);
        if (currentlyIsolated >= Config.Instance.Data.IsolationCapacity)
            return false;
        if (!_employeeStates.TryGetValue(employeeId, out var emp))
            return false;

        emp.PreIsolationRoomId = !string.IsNullOrEmpty(emp.AssignedRoomId) ? emp.AssignedRoomId : emp.CurrentRoomId;
        emp.Isolated = true;
        emp.AssignedRoomId = "";
        RemoveOccupant(emp.CurrentRoomId, employeeId);
        BeginPathTo(emp, IsolationRoomId);
        EventLog.Instance?.LogEvent(LogEventType.Isolation, employeeId, IsolationRoomId, $"{employeeId} 격리됨 → 격리실로 이송");
        return true;
    }

    public bool CancelIsolation(string employeeId)
    {
        if (!_employeeStates.TryGetValue(employeeId, out var emp) || !emp.Isolated)
            return false;

        emp.Isolated = false;
        string returnRoom = emp.PreIsolationRoomId;
        emp.PreIsolationRoomId = "";

        bool reassigned = !string.IsNullOrEmpty(returnRoom) && CanAssignToRoom(returnRoom) && AssignToRoom(employeeId, returnRoom);
        if (!reassigned)
            BeginPathTo(emp, CentralOfficeId);

        EventLog.Instance?.LogEvent(LogEventType.Isolation, employeeId, returnRoom, $"{employeeId} 격리 취소됨");
        return true;
    }

    public void Tick(double delta)
    {
        float d = (float)delta;
        foreach (var emp in _employeeStates.Values)
        {
            if (!emp.Alive) continue;
            TickMovement(emp, d);
        }
        TickTaskProgress(d);
        TickLighting();
        TickSaboteur(d);
    }

    private void TickLighting()
    {
        bool lightingOk = GameState.Instance.GetPowerAllocated(PowerConsumer.Lighting) >= Config.Instance.Data.PowerCostLighting;
        foreach (var room in _roomStates.Values)
            room.RedAlertLighting = !lightingOk;
    }

    private bool IsGuardRoomStaffed()
    {
        return (_roomStates.GetValueOrDefault(GuardRoomId)?.OccupantEmployeeIds.Count ?? 0) > 0;
    }

    private void TickSaboteur(float delta)
    {
        string saboteurId = GameState.Instance.SaboteurEmployeeId;
        if (string.IsNullOrEmpty(saboteurId)) return;
        if (!_employeeStates.TryGetValue(saboteurId, out var saboteur) || !saboteur.Alive || saboteur.Isolated)
            return;

        _saboteurDecisionTimer += delta;
        if (_saboteurDecisionTimer < Config.Instance.Data.SaboteurDecisionIntervalSeconds)
            return;
        _saboteurDecisionTimer = 0f;

        if (saboteur.IsMoving) return;

        var room = _roomStates.GetValueOrDefault(saboteur.CurrentRoomId);
        if (room == null) return;

        var others = room.OccupantEmployeeIds.Where(id => id != saboteur.EmployeeId).ToList();
        bool underCctv = IsRoomUnderActiveCctv(saboteur.CurrentRoomId);
        float surveillanceMult = IsGuardRoomStaffed() ? Config.Instance.Data.SurveillanceSaboteurChanceMultiplier : 1f;

        if (_killsToday < Config.Instance.Data.MurderMaxPerDay && others.Count == 1 && !underCctv)
        {
            var victim = _employeeStates.GetValueOrDefault(others[0]);
            if (victim is { Alive: true, Isolated: false } && _rng.NextDouble() < Config.Instance.Data.KillAttemptChance * surveillanceMult)
            {
                KillEmployee(others[0], saboteur.CurrentRoomId);
                return;
            }
        }

        if (others.Count == 0 && !underCctv && _rng.NextDouble() < Config.Instance.Data.SaboteurSabotageChance * surveillanceMult)
        {
            Sabotage(saboteur.EmployeeId, saboteur.CurrentRoomId);
            return;
        }

        if (_rng.NextDouble() < Config.Instance.Data.SaboteurWanderChance)
        {
            var candidates = _roomDefs.Values.Where(r => !r.IsRestricted && r.RoomId != saboteur.CurrentRoomId).ToList();
            if (candidates.Count > 0)
            {
                var target = candidates[_rng.Next(candidates.Count)];
                MoveEmployeeTo(saboteur.EmployeeId, target.RoomId);
            }
        }
    }

    private void KillEmployee(string victimId, string roomId)
    {
        if (!_employeeStates.TryGetValue(victimId, out var victim)) return;

        victim.Alive = false;
        RemoveOccupant(roomId, victimId);
        _killsToday++;

        var def = _employeeDefs.GetValueOrDefault(victimId);
        EventLog.Instance?.LogEvent(LogEventType.Death, victimId, roomId,
            $"{def?.Codename ?? victimId} 활동 중단 확인. 발견 당시 목격자 없음.");
    }

    private void Sabotage(string actorEmployeeId, string roomId)
    {
        var roomDef = _roomDefs.GetValueOrDefault(roomId);
        GameState.Instance.AddCoreProgress(-Config.Instance.Data.SabotageCoreProgressLoss, "원인 불명의 지연");
        EventLog.Instance?.LogEvent(LogEventType.Sabotage, actorEmployeeId, roomId,
            $"{roomDef?.DisplayName ?? roomId}에서 코어 진행도가 비정상적으로 감소했다.");
    }

    private void TickMovement(EmployeeState emp, float delta)
    {
        if (!emp.IsMoving || string.IsNullOrEmpty(emp.TargetRoomId)) return;
        if (!_roomDefs.ContainsKey(emp.TargetRoomId)) return;

        Vector2 targetPosition = GetRoomPosition(emp.TargetRoomId);
        float speed = Config.Instance.Data.EmployeeMoveSpeed;
        Vector2 toTarget = targetPosition - emp.Position;
        float dist = toTarget.Length();

        if (dist <= speed * delta)
        {
            emp.Position = targetPosition;
            ArriveAtRoom(emp);
            AdvanceToNextWaypoint(emp);
        }
        else
        {
            emp.Position += toTarget.Normalized() * speed * delta;
        }
    }

    private void ArriveAtRoom(EmployeeState emp)
    {
        string previousRoom = emp.CurrentRoomId;
        if (previousRoom != emp.TargetRoomId)
        {
            RemoveOccupant(previousRoom, emp.EmployeeId);
            EventLog.Instance?.LogEvent(LogEventType.RoomExit, emp.EmployeeId, previousRoom, $"{emp.EmployeeId} {previousRoom} 퇴장",
                GetOtherOccupants(previousRoom, emp.EmployeeId));

            LogNeglectIfRoomLeftEmptyMidTask(previousRoom, emp.EmployeeId);
        }

        emp.CurrentRoomId = emp.TargetRoomId;
        if (_roomStates.TryGetValue(emp.CurrentRoomId, out var room) && !room.OccupantEmployeeIds.Contains(emp.EmployeeId))
            room.OccupantEmployeeIds.Add(emp.EmployeeId);

        EventLog.Instance?.LogEvent(LogEventType.RoomEnter, emp.EmployeeId, emp.CurrentRoomId, $"{emp.EmployeeId} {emp.CurrentRoomId} 입장",
            GetOtherOccupants(emp.CurrentRoomId, emp.EmployeeId));

        TabooRuleSystem.Instance?.EvaluateOnRoomChange(emp.EmployeeId, emp.CurrentRoomId);
    }

    private void LogNeglectIfRoomLeftEmptyMidTask(string roomId, string departingEmployeeId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        if (room == null || room.OccupantEmployeeIds.Count > 0) return;

        var activeTask = GetActiveTaskForRoom(roomId);
        if (activeTask == null) return;

        float gauge = room.TaskGauges.GetValueOrDefault(activeTask.TaskId, 0f);
        if (gauge >= activeTask.GaugeRequired) return;

        var roomDef = _roomDefs.GetValueOrDefault(roomId);
        EventLog.Instance?.LogEvent(LogEventType.Neglect, departingEmployeeId, roomId,
            $"{departingEmployeeId} — 1순위 업무 '{activeTask.DisplayName}' 미완 상태로 {roomDef?.DisplayName ?? roomId} 이탈");
    }

    private void RemoveOccupant(string roomId, string employeeId)
    {
        if (_roomStates.TryGetValue(roomId, out var room))
            room.OccupantEmployeeIds.Remove(employeeId);
    }

    private List<string> GetOtherOccupants(string roomId, string excludeEmployeeId)
    {
        if (!_roomStates.TryGetValue(roomId, out var room)) return new List<string>();
        return room.OccupantEmployeeIds.Where(id => id != excludeEmployeeId).ToList();
    }

    // --- Room task gauges / neglect / effects ------------------------------

    private void TickTaskProgress(float delta)
    {
        foreach (var room in _roomStates.Values)
        {
            var activeTask = GetActiveTaskForRoom(room.RoomId);
            if (activeTask == null)
            {
                room.NeglectTimer = 0f;
                continue;
            }

            bool blockedByMaterials = activeTask.EffectType == TaskEffectType.AddCoreProgress
                && GameState.Instance.Materials < Config.Instance.Data.MaterialsPerCoreGauge;

            var workers = room.OccupantEmployeeIds
                .Select(id => _employeeStates.GetValueOrDefault(id))
                .Where(e => e != null && e.Alive && !e.Isolated)
                .ToList();

            if (workers.Count > 0 && blockedByMaterials)
            {
                room.NeglectTimer = 0f;
                continue;
            }

            if (workers.Count == 0)
            {
                if (activeTask.HasNeglectConsequence)
                {
                    room.NeglectTimer += delta;
                    if (room.NeglectTimer >= activeTask.NeglectThresholdSeconds)
                    {
                        ApplyNeglectConsequence(activeTask, room.RoomId);
                        room.NeglectTimer = 0f;
                    }
                }
                continue;
            }

            room.NeglectTimer = 0f;
            int statSum = workers.Sum(w => _employeeDefs[w.EmployeeId].GetStat(activeTask.RequiredStat));
            float gauge = room.TaskGauges.GetValueOrDefault(activeTask.TaskId, 0f) + statSum * delta;

            if (gauge >= activeTask.GaugeRequired)
            {
                gauge -= activeTask.GaugeRequired;
                ApplyTaskEffect(activeTask, room.RoomId);
            }
            room.TaskGauges[activeTask.TaskId] = gauge;
        }
    }

    private void ApplyTaskEffect(TaskDef task, string roomId)
    {
        switch (task.EffectType)
        {
            case TaskEffectType.AddMaterials:
                GameState.Instance.AddMaterials((int)task.EffectAmount);
                break;
            case TaskEffectType.AddCoreProgress:
                GameState.Instance.AddMaterials(-Config.Instance.Data.MaterialsPerCoreGauge);
                GameState.Instance.AddCoreProgress(task.EffectAmount, task.DisplayName);
                break;
            case TaskEffectType.ReduceStress:
                foreach (var occId in _roomStates.GetValueOrDefault(roomId)?.OccupantEmployeeIds ?? new List<string>())
                {
                    var occ = _employeeStates.GetValueOrDefault(occId);
                    if (occ != null)
                        occ.Stress = Mathf.Clamp(occ.Stress - task.EffectAmount, 0f, Config.Instance.Data.StressMax);
                }
                break;
            case TaskEffectType.BoostPowerCapacity:
                GameState.Instance.AddPowerCapacityBonus((int)task.EffectAmount);
                break;
        }
    }

    private void ApplyNeglectConsequence(TaskDef task, string roomId)
    {
        var roomDef = _roomDefs.GetValueOrDefault(roomId);
        EventLog.Instance?.LogEvent(LogEventType.Neglect, "", roomId,
            $"{roomDef?.DisplayName ?? roomId} — '{task.DisplayName}' 장기 방치로 사고 발생");
        TabooRuleSystem.Instance?.ApplyRoomConsequence(task.NeglectConsequenceType, roomId);
    }
}
