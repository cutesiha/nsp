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

    // 사망 사실이 로그로 발견되는 시점과 무관하게, 실제 사망 순간의 음향/시야 연출에 사용한다.
    [Signal] public delegate void EmployeeKilledEventHandler(string employeeId);

    private const string IsolationRoomId = "isolation_room";
    private const string GuardRoomId = "guard_room";
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
    private bool _cctvWasOperational = true;
    private bool _powerLossMurderTriggeredThisShift;

    // DAY1 고정 스케줄(data/spawns/*.tres, SpawnAtSeconds 순) + 실제로 발생한 업무 인스턴스들.
    private readonly List<TaskSpawnDef> _schedule = new();
    private readonly List<SpawnedTask> _activeTasks = new();
    private int _scheduleCursor = 0;

    private string _surveillanceTargetRoomId = "";
    private string _forcedSurveillanceRoomId = "";
    private double _forcedSurveillanceUntil = -1;
    public string SurveillanceTargetRoomId => IsSurveillanceForced()
        ? _forcedSurveillanceRoomId
        : _surveillanceTargetRoomId;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        LoadDefinitions("res://data/employees/", _employeeDefs, d => d.EmployeeId);
        LoadDefinitions("res://data/rooms/", _roomDefs, d => d.RoomId);
        LoadDefinitions("res://data/tasks/", _taskDefs, d => d.TaskId);
        LoadSchedule("res://data/spawns/");

        // 데이터가 하나도 안 실리면(특히 내보낸 빌드) 게임이 통째로 비어버리므로 항상 로그를 남긴다.
        GD.Print($"FacilitySimulation: employees={_employeeDefs.Count} rooms={_roomDefs.Count} tasks={_taskDefs.Count} spawns={_schedule.Count}");
        if (_roomDefs.Count == 0 || _employeeDefs.Count == 0)
            GD.PushError("FacilitySimulation: 정의 데이터를 불러오지 못했습니다 (res://data/* 스캔 실패).");

        BuildInitialStates();
    }

    // 직원/작업실 상태를 정의 데이터 기준 초기값으로 만든다. 최초 기동과
    // "처음부터 다시 시작"(ResetRun) 이 같은 코드를 쓴다.
    private void BuildInitialStates()
    {
        _employeeStates.Clear();
        _roomStates.Clear();

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

    // 시작화면으로 돌아가 처음부터 다시 시작. autoload 라 씬을 다시 로드해도 남아 있는
    // 배치/사망/격리/스트레스/발생 업무를 전부 지우고 DAY 1 초기 상태로 되돌린다.
    public void ResetRun()
    {
        _activeTasks.Clear();
        _scheduleCursor = 0;
        _saboteurDecisionTimer = 0f;
        _killsToday = 0;
        _cctvWasOperational = true;
        _powerLossMurderTriggeredThisShift = false;
        _surveillanceTargetRoomId = "";
        _forcedSurveillanceRoomId = "";
        _forcedSurveillanceUntil = -1;
        _roomVisualCenters.Clear();
        _roomVisualColors.Clear();
        BuildInitialStates();
    }

    private void LoadSchedule(string folder)
    {
        // ResourceDir: 내보낸 빌드의 .tres.remap 접미사까지 처리한다.
        foreach (string path in ResourceDir.ListFiles(folder, ".tres"))
        {
            var res = GD.Load<TaskSpawnDef>(path);
            if (res != null)
                _schedule.Add(res);
        }
        _schedule.Sort((a, b) => a.SpawnAtSeconds.CompareTo(b.SpawnAtSeconds));
    }

    private void LoadDefinitions<T>(string folder, Dictionary<string, T> target, System.Func<T, string> idSelector) where T : Resource
    {
        foreach (string path in ResourceDir.ListFiles(folder, ".tres"))
        {
            var res = GD.Load<T>(path);
            if (res != null)
                target[idSelector(res)] = res;
        }
    }

    public IReadOnlyCollection<string> GetEmployeeIds() => _employeeStates.Keys;
    public IReadOnlyCollection<string> GetRoomIds() => _roomStates.Keys;
    public IEnumerable<TaskDef> GetTaskDefs() => _taskDefs.Values;

    public EmployeeDef GetEmployeeDef(string id) => _employeeDefs.GetValueOrDefault(id);
    public RoomDef GetRoomDef(string id) => _roomDefs.GetValueOrDefault(id);
    public TaskDef GetTaskDef(string id) => _taskDefs.GetValueOrDefault(id);
    public EmployeeState GetEmployeeState(string id) => _employeeStates.GetValueOrDefault(id);
    public RoomState GetRoomState(string id) => _roomStates.GetValueOrDefault(id);

    // 스트레스 연동 지점(단일 창구). 지금은 EmployeeState.Stress 필드에 값만 더한다 —
    // 스트레스가 업무 효율/명령 거부로 이어지는 로직은 아직 없음(TODO).
    public void AddStress(string employeeId, float amount, string reason = "")
    {
        var st = _employeeStates.GetValueOrDefault(employeeId);
        if (st == null || !st.Alive) return;
        st.Stress = Mathf.Clamp(st.Stress + amount, 0f, Config.Instance.Data.StressMax);
        if (!string.IsNullOrEmpty(reason))
            EventLog.Instance?.LogEvent(LogEventType.Neglect, employeeId, st.CurrentRoomId,
                $"{Codename(employeeId)} 스트레스 {(amount >= 0 ? "+" : "")}{amount:0} ({reason}) → {st.Stress:0}");
    }

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
        if (IsSurveillanceForced()) return;
        _surveillanceTargetRoomId = roomId;
    }

    public void ForceSurveillanceTarget(string roomId, float seconds)
    {
        _forcedSurveillanceRoomId = roomId ?? "";
        _forcedSurveillanceUntil = Time.GetTicksMsec() / 1000.0 + Mathf.Max(0.1f, seconds);
        _surveillanceTargetRoomId = _forcedSurveillanceRoomId;
    }

    public void ReleaseForcedSurveillance(string roomId = "")
    {
        if (!string.IsNullOrEmpty(roomId) && _forcedSurveillanceRoomId != roomId) return;
        _forcedSurveillanceRoomId = "";
        _forcedSurveillanceUntil = -1;
    }

    private bool IsSurveillanceForced()
    {
        if (string.IsNullOrEmpty(_forcedSurveillanceRoomId)) return false;
        if (Time.GetTicksMsec() / 1000.0 < _forcedSurveillanceUntil) return true;
        _forcedSurveillanceRoomId = "";
        _forcedSurveillanceUntil = -1;
        return false;
    }

    public bool IsRoomUnderActiveCctv(string roomId)
    {
        return SurveillanceTargetRoomId == roomId
            && GameState.Instance.IsCctvOperational();
    }

    // --- Spawned task instances ----------------------------------------

    // 이 방에 발생해 있는 모든 업무 인스턴스(진행 중 + 방금 완료/실패해 잔여 표시 중).
    public IReadOnlyList<SpawnedTask> GetActiveTasksForRoom(string roomId) =>
        _activeTasks.Where(t => t.RoomId == roomId).ToList();

    // 이 방에서 지금 "대표로 보여줄" 업무. 긴급(제한시간 있는) 진행 중 업무 > 상시 업무 >
    // 방금 완료/실패한 업무 순. 없으면 null.
    public SpawnedTask GetPrimarySpawnedTask(string roomId)
    {
        SpawnedTask best = null;
        foreach (var t in _activeTasks)
        {
            if (t.RoomId != roomId) continue;
            if (t.Status == SpawnedTaskStatus.Active && !t.Recurring)
            {
                if (best is not { Status: SpawnedTaskStatus.Active, Recurring: false } || t.Remaining < best.Remaining)
                    best = t;
            }
        }
        if (best != null) return best;

        return _activeTasks.FirstOrDefault(t => t.RoomId == roomId && t.Status == SpawnedTaskStatus.Active && t.Recurring)
            ?? _activeTasks.FirstOrDefault(t => t.RoomId == roomId && t.Status != SpawnedTaskStatus.Active);
    }

    // 방의 긴급 업무 중 제한시간 소진 비율(0~1)의 최댓값 — 위험 표시/공포 연출 트리거용.
    public float GetRoomUrgencyRatio(string roomId)
    {
        float r = 0f;
        foreach (var t in _activeTasks)
            if (t.RoomId == roomId && t.Status == SpawnedTaskStatus.Active && !t.Recurring && !t.IsRepair && t.TimeLimitSeconds > 0f)
                r = Mathf.Max(r, t.Elapsed / t.TimeLimitSeconds);
        return r;
    }

    // --- Room task list (표시용, 정적) ---------------------------------

    // 방이 원래 담당하는 업무 목록(우선도 순). RoomDetailCard 의 "요구 능력" 표시 등에 쓰인다.
    public List<TaskDef> GetRoomTasksInPriorityOrder(string roomId)
    {
        if (!_roomStates.TryGetValue(roomId, out var room)) return new List<TaskDef>();
        return room.TaskPriorityOrder.Select(id => _taskDefs.GetValueOrDefault(id)).Where(t => t != null).ToList();
    }

    // 이 방에서 지금 진행 중인 업무의 TaskDef(없으면 null). 사보타주·NPC 대화 컨텍스트·상태 표시가 참조.
    public TaskDef GetActiveTaskForRoom(string roomId)
    {
        var st = GetPrimarySpawnedTask(roomId);
        return st == null ? null : _taskDefs.GetValueOrDefault(st.TaskId);
    }

    public float GetTaskGauge(string roomId, string taskId)
    {
        return _activeTasks.FirstOrDefault(t => t.RoomId == roomId && t.TaskId == taskId)?.Gauge ?? 0f;
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
        // 배정/재배치는 "직원을 그 방으로 보낸다"는 관리자 행동. 실제 업무 수행 시작(TaskStart)은
        // 직원이 방에 도착해 발생 업무의 게이지를 채우기 시작할 때 따로 기록된다.
        EventLog.Instance?.LogEvent(LogEventType.Relocation, employeeId, roomId, $"{Codename(employeeId)} → {RoomName(roomId)} 배치");
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
        if (emp.CurrentRoomId == destinationRoomId && !emp.IsMoving)
        {
            emp.PathQueue.Clear();
            emp.TargetRoomId = destinationRoomId;
            emp.IsMoving = false;
            return true;
        }

        // 이동 중이면 지금 향하던 방까지는 마저 걸어간 뒤 거기서부터 새 경로를 잇는다 —
        // 통로 한복판에서 갑자기 방향을 꺾지 않게 한다.
        string start = emp.IsMoving && !string.IsNullOrEmpty(emp.TargetRoomId) ? emp.TargetRoomId : emp.CurrentRoomId;
        if (start == destinationRoomId)
        {
            emp.PathQueue.Clear();
            return true;
        }

        var path = FindPath(start, destinationRoomId);
        if (path.Count == 0) return false;

        emp.PathQueue = path;
        if (!emp.IsMoving)
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

    // 통로는 두 방이 같은 행/열이 아니면 직각으로 한 번 꺾인다. 꺾임 지점은 **이동 방향과
    // 무관하게 항상 같은 모서리**여야 CorridorLine.cs가 그리는 회색 선과 정확히 겹친다.
    // 규칙: 세로 구간은 더 위쪽(작은 Y) 방의 X에, 가로 구간은 더 아래쪽 방의 Y에 둔다.
    // (CorridorLine.cs의 ComputeElbow와 동일 규칙 — 한쪽만 바꾸면 안 됨.)
    private Vector2? ComputeElbowWaypoint(string fromRoomId, string toRoomId)
    {
        Vector2 from = GetRoomPosition(fromRoomId);
        Vector2 to = GetRoomPosition(toRoomId);
        if (Mathf.IsEqualApprox(from.X, to.X) || Mathf.IsEqualApprox(from.Y, to.Y))
            return null;

        Vector2 upper = from.Y <= to.Y ? from : to;
        Vector2 lower = from.Y <= to.Y ? to : from;
        return new Vector2(upper.X, lower.Y);
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
        {
            // 중앙 제어실은 관리자 전용 구역이다. 원래 작업실로 돌아갈 수 없을 때도
            // 중앙 제어실로 보내지 말고, 인접한 배치 가능 작업실을 새 담당 구역으로 잡는다.
            string fallback = FindNearestAvailableRoom(string.IsNullOrEmpty(returnRoom) ? emp.CurrentRoomId : returnRoom);
            if (!string.IsNullOrEmpty(fallback))
                AssignToRoom(employeeId, fallback);
        }

        EventLog.Instance?.LogEvent(LogEventType.Isolation, employeeId, returnRoom, $"{Codename(employeeId)} - 격리 해제");
        return true;
    }

    // 근무 시작 시 호출 — 이전 판/스폰 상태가 이월되지 않게 한다.
    // (직원 위치/생존/코어 진행도 등 GameState 전체 리셋은 기존 미구현 이슈로 별도.)
    public void ResetForNewShift()
    {
        _activeTasks.Clear();
        _scheduleCursor = 0;
        _saboteurDecisionTimer = 0f;
        _killsToday = 0;
        _cctvWasOperational = true;
        _powerLossMurderTriggeredThisShift = false;
        GameState.Instance.ResetFacilityFaults();
        foreach (var room in _roomStates.Values)
        {
            room.NeglectTimer = 0f;
            room.TabooHoldTimers.Clear();
            room.TaskGauges.Clear();
            room.CctvDisconnected = false;
            room.InfoDistorted = false;
            room.PowerOn = true;
            room.Locked = false;
        }
        // 새 근무의 초기 배치 이동은 다시 원래 속도로 걷는다.
        foreach (var emp in _employeeStates.Values)
            emp.InitialDeployDone = false;
        GameState.Instance.RepairPowerAccident();
        GameState.Instance.ResetDayClock();
    }

    public void Tick(double delta)
    {
        float d = (float)delta;
        TickSchedule();
        foreach (var emp in _employeeStates.Values)
        {
            if (!emp.Alive) continue;
            TickMovement(emp, d);
        }
        TickActiveTasks(d);
        TickLighting();
        TickPowerLossMurder();
        TabooRuleSystem.Instance?.Tick(d);
        TickSaboteur(d);
        TickPowerRestoreReveal();
        TickVentilationFault(d);
    }

    // 고정 스케줄에 따라 시간이 되면 업무를 발생시킨다.
    private void TickSchedule()
    {
        float now = GameState.Instance.DayTimeSeconds;
        while (_scheduleCursor < _schedule.Count && now >= _schedule[_scheduleCursor].SpawnAtSeconds)
        {
            SpawnFromDef(_schedule[_scheduleCursor]);
            _scheduleCursor++;
        }
    }

    private void SpawnFromDef(TaskSpawnDef def)
    {
        var taskDef = _taskDefs.GetValueOrDefault(def.TaskId);
        if (taskDef == null)
        {
            GD.PushWarning($"FacilitySimulation: spawn references unknown task '{def.TaskId}'");
            return;
        }
        string roomId = !string.IsNullOrEmpty(def.RoomId) ? def.RoomId : taskDef.RoomId;

        // 같은 방에 같은 업무가 이미 진행 중이면 중복 발생시키지 않는다.
        if (_activeTasks.Any(t => t.TaskId == taskDef.TaskId && t.RoomId == roomId && t.Status == SpawnedTaskStatus.Active))
            return;

        _activeTasks.Add(new SpawnedTask
        {
            TaskId = taskDef.TaskId,
            RoomId = roomId,
            Recurring = def.Recurring,
            TimeLimitSeconds = taskDef.TimeLimitSeconds,
            GaugeRequired = taskDef.GaugeRequired,
        });

        string desc = def.Recurring
            ? $"⚙ {RoomName(roomId)} · '{taskDef.DisplayName}' 상시 업무 시작"
            : $"⚠ {RoomName(roomId)}에 '{taskDef.DisplayName}' 업무 발생 (제한 {FormatClock(taskDef.TimeLimitSeconds)})";
        EventLog.Instance?.LogEvent(LogEventType.TaskSpawned, "", roomId, desc);
    }

    private static string FormatClock(float seconds)
    {
        int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return $"{s / 60:0}:{s % 60:00}";
    }

    private void TickLighting()
    {
        bool lightingOk = GameState.Instance.IsConsumerPowered(PowerConsumer.Lighting);
        foreach (var room in _roomStates.Values)
            room.RedAlertLighting = !lightingOk;
    }

    private bool IsGuardRoomStaffed()
    {
        return (_roomStates.GetValueOrDefault(GuardRoomId)?.OccupantEmployeeIds.Count ?? 0) > 0;
    }

    // CCTV 또는 미니맵용 조명 전력이 끊기면 그 사각을 이용한 살인이 DAY1 근무당 딱 한 번 발생한다.
    // 기존 MurderMaxPerDay/_killsToday를 같이 사용하므로 일반 방해자 AI가 추가 살인을 만들지 못한다.
    private void TickPowerLossMurder()
    {
        if (_powerLossMurderTriggeredThisShift
            || _killsToday >= Config.Instance.Data.MurderMaxPerDay)
            return;

        bool cctvCut = !GameState.Instance.IsConsumerPowered(PowerConsumer.CctvWatch);
        bool mapLightingCut = !GameState.Instance.IsConsumerPowered(PowerConsumer.Lighting);
        if (!cctvCut && !mapLightingCut) return;

        string saboteurId = GameState.Instance.SaboteurEmployeeId;
        if (string.IsNullOrEmpty(saboteurId)
            || !_employeeStates.TryGetValue(saboteurId, out var saboteur)
            || !saboteur.Alive || saboteur.Isolated)
            return;

        var candidates = _employeeStates.Values
            .Where(e => e.EmployeeId != saboteurId && e.Alive && !e.Isolated
                && !string.IsNullOrEmpty(e.CurrentRoomId) && e.CurrentRoomId != "central_office")
            .ToList();
        if (candidates.Count == 0) return;

        // 같은 방의 직원을 우선하되, 배치상 단독 근무 중이어도 전력 사각 사건 자체는 발생시킨다.
        // 후자의 경우 시체 위치는 희생자의 실제 작업실을 유지해 근무표/인터뷰 기록을 깨지 않는다.
        var victim = candidates.FirstOrDefault(e => e.CurrentRoomId == saboteur.CurrentRoomId)
            ?? candidates[_rng.Next(candidates.Count)];
        _powerLossMurderTriggeredThisShift = true;
        KillEmployee(victim.EmployeeId, victim.CurrentRoomId);
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

        // 배치된 직원은 관리자의 재배치 또는 실제 시설 문제(격리/대피) 없이는
        // 자기 담당 작업실을 떠나지 않는다. 파괴공작자는 현재 작업실에서만
        // 방해·위장 행동을 하며, 무작위 방 이동으로 근무표를 깨지 않는다.
    }

    // CCTV가 꺼져 있는(정전/전력 미배분) 동안 벌어진 살인은 그 순간 바로 로그에 남기지 않는다
    // — 관리자는 그 시간 동안 아무것도 볼 수 없었어야 한다. 대신 DiscoveredDead=false 로만
    // 표시해두고, CCTV 전력이 다시 들어오는 순간 TickPowerRestoreReveal 이 "신호 소실" 로 발견한다.
    private void KillEmployee(string victimId, string roomId)
    {
        if (!_employeeStates.TryGetValue(victimId, out var victim)) return;

        bool blackout = !GameState.Instance.IsCctvOperational();

        victim.Alive = false;
        victim.DiscoveredDead = !blackout;
        RemoveOccupant(roomId, victimId);
        _killsToday++;
        EmitSignal(SignalName.EmployeeKilled, victimId);

        if (blackout) return;

        var def = _employeeDefs.GetValueOrDefault(victimId);
        EventLog.Instance?.LogEvent(LogEventType.Death, victimId, roomId,
            $"⚠ {def?.Codename ?? victimId} 활동 중단 확인. 발견 당시 목격자 없음.");
    }

    // CCTV 전력이 꺼졌다가(정전 등) 다시 들어오는 순간, 그동안 아무도 모르게 벌어진 죽음이
    // 있으면 그제서야 "발견"된다 — 정전 중엔 관리자가 아무것도 볼 수 없었다는 것을 그대로
    // 반영한다(NSP_DAY1_EVENTS §12: 발전이 수리되어서 죽은 게 아니라, 전력이 끊겨 있어서
    // 그동안 무슨 일이 있었는지 못 보고 있다가 복구 후에야 알게 되는 것).
    private void TickPowerRestoreReveal()
    {
        bool poweredNow = GameState.Instance.IsCctvOperational();
        if (poweredNow && !_cctvWasOperational)
        {
            foreach (var emp in _employeeStates.Values)
            {
                if (emp.Alive || emp.DiscoveredDead) continue;
                emp.DiscoveredDead = true;

                var def = _employeeDefs.GetValueOrDefault(emp.EmployeeId);
                var roomDef = _roomDefs.GetValueOrDefault(emp.CurrentRoomId);
                EventLog.Instance?.LogEvent(LogEventType.Death, emp.EmployeeId, emp.CurrentRoomId,
                    $"⚠ LIFE SIGNAL LOST — {def?.Codename ?? emp.EmployeeId} 신호 소실. " +
                    $"{roomDef?.DisplayName ?? emp.CurrentRoomId}에서 발견, 목격자 없음.");
            }
        }
        _cctvWasOperational = poweredNow;
    }

    // FAIL-02: 환기가 정지된 동안(수리 전까지) 전 직원 스트레스가 계속 오른다.
    private void TickVentilationFault(float delta)
    {
        if (!GameState.Instance.VentilationDown) return;
        float rise = Config.Instance.Data.VentFaultStressPerSecond * delta;
        foreach (var emp in _employeeStates.Values)
        {
            if (!emp.Alive || emp.Isolated) continue;
            emp.Stress = Mathf.Clamp(emp.Stress + rise, 0f, Config.Instance.Data.StressMax);
        }
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
            var st = _activeTasks.FirstOrDefault(t => t.RoomId == roomId && t.TaskId == activeTask.TaskId && t.Status == SpawnedTaskStatus.Active);
            if (st != null)
                st.Gauge = Mathf.Max(0f, st.Gauge - Config.Instance.Data.SabotageTaskGaugeLoss);
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
        // 최초 배치 자리로 가는 길은 기존 속도, 자리를 잡은 뒤의 이동은 훨씬 느리게.
        var cfg = Config.Instance.Data;
        float speed = emp.InitialDeployDone ? cfg.EmployeeMoveSpeedInShift : cfg.EmployeeMoveSpeed;
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

        // 배치된 자리에 처음 도착 = 초기 배치 완료. 이후 이동은 근무 중 저속으로 걷는다.
        if (!emp.InitialDeployDone && emp.CurrentRoomId == emp.AssignedRoomId)
            emp.InitialDeployDone = true;

        TabooRuleSystem.Instance?.EvaluateOnRoomChange(emp.EmployeeId, emp.CurrentRoomId);
    }

    private void LogNeglectIfRoomLeftEmptyMidTask(string roomId, string departingEmployeeId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        if (room == null || room.OccupantEmployeeIds.Count > 0) return;

        var st = GetPrimarySpawnedTask(roomId);
        if (st == null || st.Status != SpawnedTaskStatus.Active || st.Recurring) return;
        if (st.Gauge >= st.GaugeRequired) return;

        string taskName = _taskDefs.GetValueOrDefault(st.TaskId)?.DisplayName ?? st.TaskId;
        EventLog.Instance?.LogEvent(LogEventType.Neglect, departingEmployeeId, roomId,
            $"⚠ {Codename(departingEmployeeId)} - {RoomName(roomId)} '{taskName}' 미완료 상태로 이탈");
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

    // --- Spawned task progress / resolution / effects --------------------

    // 코어실 수리는 자재 풀이 비면 게이지가 멈춘다(NSP_REALTIME_OPS §3) — UI 상태 표시도
    // 같은 조건을 읽어야 해서 재사용 가능하게 public으로 뺐다. 판정 로직은 그대로 하나뿐.
    public bool IsRoomBlockedByMaterials(string roomId)
    {
        var st = GetPrimarySpawnedTask(roomId);
        if (st == null || st.Status != SpawnedTaskStatus.Active) return false;
        var task = _taskDefs.GetValueOrDefault(st.TaskId);
        return task != null && task.EffectType == TaskEffectType.AddCoreProgress
            && GameState.Instance.Materials < Config.Instance.Data.MaterialsPerCoreGauge;
    }

    private void TickActiveTasks(float delta)
    {
        for (int i = _activeTasks.Count - 1; i >= 0; i--)
        {
            var st = _activeTasks[i];
            var taskDef = _taskDefs.GetValueOrDefault(st.TaskId);
            if (taskDef == null) { _activeTasks.RemoveAt(i); continue; }

            // 완료/실패한 업무는 잔여 표시 시간이 끝나면 리스트에서 제거한다.
            if (st.Status != SpawnedTaskStatus.Active)
            {
                st.ResolveDisplayTimer -= delta;
                if (st.ResolveDisplayTimer <= 0f)
                    _activeTasks.RemoveAt(i);
                continue;
            }

            st.Elapsed += delta;

            var room = _roomStates.GetValueOrDefault(st.RoomId);
            var workers = room == null ? new List<EmployeeState>() : room.OccupantEmployeeIds
                .Select(id => _employeeStates.GetValueOrDefault(id))
                .Where(e => e != null && e.Alive && !e.Isolated)
                .ToList();

            bool blockedByMaterials = taskDef.EffectType == TaskEffectType.AddCoreProgress
                && GameState.Instance.Materials < Config.Instance.Data.MaterialsPerCoreGauge;

            if (workers.Count > 0)
            {
                // 직원이 실제로 이 방에서 발생 업무를 수행하기 시작함 = TaskStart (1인 1회).
                foreach (var w in workers)
                {
                    if (!st.StartedWorkerIds.Add(w.EmployeeId)) continue;
                    EventLog.Instance?.LogEvent(LogEventType.TaskStart, w.EmployeeId, st.RoomId,
                        $"{Codename(w.EmployeeId)} {RoomName(st.RoomId)} 도착 / {taskDef.DisplayName} 시작",
                        workers.Where(x => x.EmployeeId != w.EmployeeId).Select(x => x.EmployeeId));
                }

                // 최소 필요 인원 미만이면 게이지가 전혀 차지 않는다 — DAY1 발전기 점검(2명 필요)이
                // DAY1 금기(발전실 2명 금지)와 반드시 충돌하도록 만드는 지점.
                if (workers.Count >= Mathf.Max(1, taskDef.MinWorkersToProgress) && !blockedByMaterials)
                {
                    // 요구 능력치가 높으면 '조금' 빨라지고, 낮으면 눈에 띄게 느려진다.
                    float rate = workers.Sum(w => StatWorkRate(_employeeDefs[w.EmployeeId].GetStat(taskDef.RequiredStat)));
                    st.Gauge += rate * delta;
                }
            }

            if (st.Gauge >= st.GaugeRequired)
                ResolveTask(st, taskDef, true);
            else if (!st.Recurring && st.Elapsed >= st.TimeLimitSeconds)
                ResolveTask(st, taskDef, false);
        }
    }

    // 직원 1명이 그 업무에 기여하는 초당 게이지량. 능력치 2를 기준(1.0배)으로 두고,
    // 3은 소폭 가산, 1 이하는 크게 감산해 "적임자가 아니면 확 느려진다"를 만든다.
    private static float StatWorkRate(int stat)
    {
        var cfg = Config.Instance.Data;
        float mult = stat switch
        {
            >= 3 => cfg.StatHighMultiplier,
            2 => cfg.StatMatchMultiplier,
            1 => cfg.StatLowMultiplier,
            _ => cfg.StatVeryLowMultiplier,
        };
        return cfg.BaseTaskWorkRate * mult;
    }

    private void ResolveTask(SpawnedTask st, TaskDef taskDef, bool completed)
    {
        if (completed && st.Recurring)
        {
            // 상시 업무: 효과 적용 후 게이지만 되돌리고 계속 순환 (제거하지 않는다).
            ApplyTaskEffect(taskDef, st.RoomId);
            st.Gauge = Mathf.Max(0f, st.Gauge - st.GaugeRequired);
            return;
        }

        if (completed && st.IsRepair)
        {
            // 수리 완료 — 걸려 있던 시설 페널티를 되돌린다.
            TabooRuleSystem.Instance?.RepairRoomConsequence(taskDef.NeglectConsequenceType, st.RoomId);
            EventLog.Instance?.LogEvent(LogEventType.TaskComplete, "", st.RoomId,
                $"✓ {RoomName(st.RoomId)} — '{taskDef.DisplayName}' 수리 완료 · 기능 복구");
            st.Status = SpawnedTaskStatus.Completed;
        }
        else if (completed)
        {
            ApplyTaskEffect(taskDef, st.RoomId); // TaskComplete 배지 로그 포함
            st.Status = SpawnedTaskStatus.Completed;
        }
        else if (taskDef.HasNeglectConsequence)
        {
            // 제한시간 초과 — 고장 발생. 업무는 사라지지 않고 "수리" 업무로 전환되어
            // 담당 직원이 완료해야 기능이 복구된다.
            EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", st.RoomId,
                $"🚨 {RoomName(st.RoomId)} — '{taskDef.DisplayName}' 제한시간 초과, 고장 발생");
            TabooRuleSystem.Instance?.ApplyRoomConsequence(taskDef.NeglectConsequenceType, st.RoomId, taskDef.NeglectConsequenceAmount);

            st.IsRepair = true;
            st.Status = SpawnedTaskStatus.Active;
            st.Gauge = 0f;
            st.Elapsed = 0f;
            st.TimeLimitSeconds = float.MaxValue;
            st.StartedWorkerIds.Clear();
            return;
        }
        else
        {
            st.Status = SpawnedTaskStatus.Failed;
            EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", st.RoomId,
                $"🚨 {RoomName(st.RoomId)} — '{taskDef.DisplayName}' 제한시간 초과, 처리 실패");
        }
        st.ResolveDisplayTimer = Config.Instance.Data.ResolvedTaskDisplaySeconds;
    }

    private void ApplyTaskEffect(TaskDef task, string roomId)
    {
        string badge = $"✓ {task.DisplayName} 완료";
        switch (task.EffectType)
        {
            case TaskEffectType.AddMaterials:
                // FAIL-03: 정비 설비가 고장 나 있으면 생산이 멈춘다(수리 완료 전까지).
                if (GameState.Instance.MaterialsProductionHalted)
                {
                    badge += " · ⚠ 설비 고장 — 생산 정지";
                    break;
                }
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
}
