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

    // 로그 표시용 — 내부 id 대신 플레이어가 보는 코드네임/방 이름으로 남기기 위한 헬퍼.
    private string Codename(string employeeId) => _employeeDefs.GetValueOrDefault(employeeId)?.Codename ?? employeeId;
    private string RoomName(string roomId) => _roomDefs.GetValueOrDefault(roomId)?.DisplayName ?? roomId;

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

    // UI가 플로팅 팝업을 방 위치에 띄우기 위한 읽기 전용 노출 — 새 상태 아님, 기존 좌표 그대로.
    public Vector2 GetRoomVisualPosition(string roomId) => GetRoomPosition(roomId);

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
        return SurveillanceTargetRoomId == roomId
            && GameState.Instance.GetPowerAllocated(PowerConsumer.CctvWatch) > 0
            && !GameState.Instance.IsPowerOverBudget();
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
        EventLog.Instance?.LogEvent(LogEventType.TaskStart, employeeId, roomId, $"{Codename(employeeId)} - {RoomName(roomId)} 배정");
        return true;
    }

    public void ClearAssignment(string employeeId)
    {
        if (!_employeeStates.TryGetValue(employeeId, out var emp)) return;

        EventLog.Instance?.LogEvent(LogEventType.TaskEnd, employeeId, emp.CurrentRoomId, $"{Codename(employeeId)} - 배치 해제");
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
        emp.ElbowWaypoint = emp.IsMoving ? ComputeElbowWaypoint(emp.CurrentRoomId, next) : null;
    }

    // CorridorLine.cs가 그리는 통로는 두 방이 같은 행/열에 있지 않으면 직각으로 한 번 꺾인다
    // (a → (a.X, b.Y) → b). 예전에는 이동이 항상 방 중심끼리 직선으로만 이었어서 꺾인 통로
    // 구간에서 직원이 통로를 벗어나 대각선으로 가로질렀다 — 같은 꺾임 공식을 재사용해 고침.
    // 두 방이 이미 같은 행이나 열이면 이 점은 출발점/도착점과 겹쳐 자동으로 직선이 된다.
    private Vector2? ComputeElbowWaypoint(string fromRoomId, string toRoomId)
    {
        Vector2 from = GetRoomPosition(fromRoomId);
        Vector2 to = GetRoomPosition(toRoomId);
        if (Mathf.IsEqualApprox(from.X, to.X) || Mathf.IsEqualApprox(from.Y, to.Y))
            return null;
        return new Vector2(from.X, to.Y);
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
        EventLog.Instance?.LogEvent(LogEventType.Relocation, "", roomId, locked ? $"{RoomName(roomId)} 구역 봉쇄" : $"{RoomName(roomId)} 봉쇄 해제");

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
        EventLog.Instance?.LogEvent(LogEventType.Isolation, employeeId, IsolationRoomId, $"{Codename(employeeId)} - 격리 조치");
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

        EventLog.Instance?.LogEvent(LogEventType.Isolation, employeeId, returnRoom, $"{Codename(employeeId)} - 격리 해제");
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
        TabooRuleSystem.Instance?.Tick(d);
        TickSaboteur(d);
    }

    private void TickLighting()
    {
        bool lightingOk = GameState.Instance.GetPowerAllocated(PowerConsumer.Lighting) >= Config.Instance.Data.PowerCostLighting
            && !GameState.Instance.IsPowerOverBudget();
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

        var activeTask = GetActiveTaskForRoom(room.RoomId);
        if (activeTask != null && !underCctv && _rng.NextDouble() < Config.Instance.Data.SaboteurSabotageChance * surveillanceMult)
        {
            Sabotage(saboteur.EmployeeId, room.RoomId, activeTask, others);
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
            $"⚠ {def?.Codename ?? victimId} 활동 중단 확인. 발견 당시 목격자 없음.");
    }

    // SAB-01 감시 사각: 파괴공작자가 CCTV로 감시되지 않는 작업실에 있을 때, 그 방의 업무를
    // 은밀히 방해한다. 방마다 이미 정의된 방치 결과(NeglectConsequenceType)를 재사용해
    // "방치로 인한 고장"과 "파괴공작으로 인한 고장"이 겉으로는 같은 증상으로 보이게 한다.
    // 같은 방에 있던 다른 직원이 있어도 범행을 막지는 않되, 그 인원을 목격자로 로그에 남긴다
    // (LogEntry.WitnessEmployeeIds — 이후 GetEntriesKnownBy를 통해 대화 시스템이 참조할 수 있음).
    private void Sabotage(string actorEmployeeId, string roomId, TaskDef activeTask, List<string> witnesses)
    {
        var roomDef = _roomDefs.GetValueOrDefault(roomId);
        bool equipmentFault = activeTask.HasNeglectConsequence && _rng.NextDouble() < 0.5;

        if (equipmentFault)
        {
            TabooRuleSystem.Instance?.ApplyRoomConsequence(activeTask.NeglectConsequenceType, roomId, activeTask.NeglectConsequenceAmount);
            EventLog.Instance?.LogEvent(LogEventType.Sabotage, actorEmployeeId, roomId,
                $"⚠ {roomDef?.DisplayName ?? roomId} 설비에서 원인 불명의 이상이 발견됐다.", witnesses);
        }
        else
        {
            var room = _roomStates.GetValueOrDefault(roomId);
            if (room != null)
            {
                float gauge = room.TaskGauges.GetValueOrDefault(activeTask.TaskId, 0f);
                room.TaskGauges[activeTask.TaskId] = Mathf.Max(0f, gauge - Config.Instance.Data.SabotageTaskGaugeLoss);
            }
            EventLog.Instance?.LogEvent(LogEventType.Sabotage, actorEmployeeId, roomId,
                $"⚠ {roomDef?.DisplayName ?? roomId} — '{activeTask.DisplayName}' 진행 기록에 원인 불명의 지연이 있었다.", witnesses);
        }
    }

    private void TickMovement(EmployeeState emp, float delta)
    {
        if (!emp.IsMoving || string.IsNullOrEmpty(emp.TargetRoomId)) return;
        if (!_roomDefs.ContainsKey(emp.TargetRoomId)) return;

        // 꺾임 지점이 남아있으면 방 중심이 아니라 그 지점을 향해 먼저 이동한다 — 통로를 따라
        // 걷는 것처럼 보이게 하기 위함. 방 도착 판정(ArriveAtRoom)은 꺾임을 다 지난 뒤에만.
        Vector2 stepTarget = emp.ElbowWaypoint ?? GetRoomPosition(emp.TargetRoomId);
        float speed = Config.Instance.Data.EmployeeMoveSpeed;
        Vector2 toTarget = stepTarget - emp.Position;
        float dist = toTarget.Length();

        if (dist <= speed * delta)
        {
            emp.Position = stepTarget;
            if (emp.ElbowWaypoint.HasValue)
            {
                emp.ElbowWaypoint = null;
            }
            else
            {
                ArriveAtRoom(emp);
                AdvanceToNextWaypoint(emp);
            }
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
            EventLog.Instance?.LogEvent(LogEventType.RoomExit, emp.EmployeeId, previousRoom, $"{Codename(emp.EmployeeId)} - {RoomName(previousRoom)} 퇴장",
                GetOtherOccupants(previousRoom, emp.EmployeeId));

            LogNeglectIfRoomLeftEmptyMidTask(previousRoom, emp.EmployeeId);
        }

        emp.CurrentRoomId = emp.TargetRoomId;
        if (_roomStates.TryGetValue(emp.CurrentRoomId, out var room) && !room.OccupantEmployeeIds.Contains(emp.EmployeeId))
            room.OccupantEmployeeIds.Add(emp.EmployeeId);

        EventLog.Instance?.LogEvent(LogEventType.RoomEnter, emp.EmployeeId, emp.CurrentRoomId, $"{Codename(emp.EmployeeId)} - {RoomName(emp.CurrentRoomId)} 입장",
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

        EventLog.Instance?.LogEvent(LogEventType.Neglect, departingEmployeeId, roomId,
            $"⚠ {Codename(departingEmployeeId)} - {RoomName(roomId)} '{activeTask.DisplayName}' 미완료 상태로 이탈");
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

    // 코어실 수리는 자재 풀이 비면 게이지가 멈춘다(NSP_REALTIME_OPS §3) — UI 상태 표시도
    // 같은 조건을 읽어야 해서 재사용 가능하게 public으로 뺐다. 판정 로직은 그대로 하나뿐.
    public bool IsRoomBlockedByMaterials(string roomId)
    {
        var task = GetActiveTaskForRoom(roomId);
        return task != null && task.EffectType == TaskEffectType.AddCoreProgress
            && GameState.Instance.Materials < Config.Instance.Data.MaterialsPerCoreGauge;
    }

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

            bool blockedByMaterials = IsRoomBlockedByMaterials(room.RoomId);

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
                // NeglectTimer < 0 = 이번 방치 상태에서 이미 한 번 발동함(잠금). 방이 다시
                // 채워져 위반 상태가 끝나야(아래 "room.NeglectTimer = 0f") 재발동 가능해진다.
                if (activeTask.HasNeglectConsequence && room.NeglectTimer >= 0f)
                {
                    room.NeglectTimer += delta;
                    if (room.NeglectTimer >= activeTask.NeglectThresholdSeconds)
                    {
                        ApplyNeglectConsequence(activeTask, room.RoomId);
                        room.NeglectTimer = -1f;
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
        string badge = $"✓ {task.DisplayName} 완료";
        switch (task.EffectType)
        {
            case TaskEffectType.AddMaterials:
                GameState.Instance.AddMaterials((int)task.EffectAmount);
                badge += $" · 📦 자재 +{task.EffectAmount:0}";
                break;
            case TaskEffectType.AddCoreProgress:
                int consumed = Config.Instance.Data.MaterialsPerCoreGauge;
                GameState.Instance.AddMaterials(-consumed);
                GameState.Instance.AddCoreProgress(task.EffectAmount, task.DisplayName);
                badge += $" · 코어 +{task.EffectAmount:0}% · 📦 자재 -{consumed}";
                break;
            case TaskEffectType.ReduceStress:
                foreach (var occId in _roomStates.GetValueOrDefault(roomId)?.OccupantEmployeeIds ?? new List<string>())
                {
                    var occ = _employeeStates.GetValueOrDefault(occId);
                    if (occ != null)
                        occ.Stress = Mathf.Clamp(occ.Stress - task.EffectAmount, 0f, Config.Instance.Data.StressMax);
                }
                badge += $" · 스트레스 -{task.EffectAmount:0}";
                break;
            case TaskEffectType.BoostPowerCapacity:
                GameState.Instance.RepairPowerAccident();
                badge += " · ⚡ 전력 정상 복구";
                break;
        }
        EventLog.Instance?.LogEvent(LogEventType.TaskComplete, "", roomId, badge);
    }

    private void ApplyNeglectConsequence(TaskDef task, string roomId)
    {
        EventLog.Instance?.LogEvent(LogEventType.Neglect, "", roomId,
            $"⚠ {RoomName(roomId)} — '{task.DisplayName}' 장기 방치로 사고 발생");
        TabooRuleSystem.Instance?.ApplyRoomConsequence(task.NeglectConsequenceType, roomId, task.NeglectConsequenceAmount);
    }
}
