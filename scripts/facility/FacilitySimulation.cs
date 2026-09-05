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
    private const string MedicalRoomId = "medical_room";
    private const string VentRoomId = "vent_room";
    private const string MaintenanceRoomId = "maintenance_room";
    private const string StorageRoomId = "storage_room";
    private const string CoreRoomId = "core_room";
    private const string PowerRoomId = "power_room";
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

    // 스트레스 연동 지점(단일 창구). 모든 스트레스 증감은 반드시 여기를 지난다.
    //  · 증가분에는 담력 배율이 걸린다 (담력 1=100% / 2=80% / 3=60%). 감소(치료)에는 안 건다.
    //  · 값은 1~50 으로 고정된다.
    //  · 46 이상이 되면 기절 — 의무실로 강제 송환되고 당일 업무 불가.
    public void AddStress(string employeeId, float amount, string reason = "")
    {
        var st = _employeeStates.GetValueOrDefault(employeeId);
        if (st == null || !st.Alive) return;
        var cfg = Config.Instance.Data;

        if (amount > 0f) amount *= CourageStressMultiplier(employeeId);
        st.Stress = Mathf.Clamp(st.Stress + amount, cfg.StressMin, cfg.StressMax);

        if (!string.IsNullOrEmpty(reason))
            EventLog.Instance?.LogEvent(LogEventType.Neglect, employeeId, st.CurrentRoomId,
                $"{Codename(employeeId)} 스트레스 {(amount >= 0 ? "+" : "")}{amount:0.#} ({reason}) → {st.Stress:0}");

        CheckFaint(st);
    }

    // 46~50 = 기절. 근무에서 빠지고 의무실로 옮겨진다(당일 복귀 없음).
    private void CheckFaint(EmployeeState st)
    {
        var cfg = Config.Instance.Data;
        if (st.Incapacitated || st.Stress < cfg.StressFaintFrom || !st.Alive) return;

        st.Incapacitated = true;
        EventLog.Instance?.LogEvent(LogEventType.Neglect, st.EmployeeId, st.CurrentRoomId,
            $"🚨 {Codename(st.EmployeeId)} 스트레스 {st.Stress:0} — 기절, 의무실로 이송 (당일 업무 불가)");

        // 격리 중이 아니면 의무실로 옮긴다. 배치는 유지해 두어 관리자가 상황을 볼 수 있게 한다.
        if (!st.Isolated && _roomDefs.ContainsKey(MedicalRoomId))
            BeginPathTo(st, MedicalRoomId);
    }

    // 스트레스 구간별 업무 속도 배율. 1~10 정상 / 11~30 주의 / 31~45 위험 / 46+ 기절(0).
    public float StressWorkRate(EmployeeState st)
    {
        var cfg = Config.Instance.Data;
        if (st.Incapacitated || st.Stress >= cfg.StressFaintFrom) return 0f;
        if (st.Stress >= cfg.StressDangerFrom) return cfg.StressWorkRateDanger;
        if (st.Stress >= cfg.StressCautionFrom) return cfg.StressWorkRateCaution;
        return cfg.StressWorkRateNormal;
    }

    // 스트레스 구간 이름 — UI 표시에 쓴다(수치 판정은 위 함수들이 한다).
    public string StressBandName(EmployeeState st)
    {
        var cfg = Config.Instance.Data;
        if (st.Incapacitated || st.Stress >= cfg.StressFaintFrom) return "기절";
        if (st.Stress >= cfg.StressDangerFrom) return "위험";
        if (st.Stress >= cfg.StressCautionFrom) return "주의";
        return "정상";
    }

    // --- 능력치 3종의 효과 (Config 의 배열에서만 읽는다) ----------------------
    private static float StatLookup(float[] table, int stat) =>
        table == null || table.Length == 0 ? 1f : table[Mathf.Clamp(stat, 0, table.Length - 1)];

    // 기술 → 업무 속도 배율.
    public float TechWorkMultiplier(string employeeId) =>
        StatLookup(Config.Instance.Data.TechWorkRate, _employeeDefs.GetValueOrDefault(employeeId)?.Tech ?? 2);

    // 담력 → 스트레스 획득량 배율.
    public float CourageStressMultiplier(string employeeId) =>
        StatLookup(Config.Instance.Data.CourageStressGain, _employeeDefs.GetValueOrDefault(employeeId)?.Courage ?? 2);

    // 관찰 → 단서 포착 확률(0~1). 목격/추리 정보가 실제로 남을 확률에 쓴다.
    public float ObservationClueChance(string employeeId) =>
        StatLookup(Config.Instance.Data.ObservationClueChance, _employeeDefs.GetValueOrDefault(employeeId)?.Observation ?? 2);

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
        // 관리자가 직접 채널을 바꾼 것 = 금기 ⑧(연속 전환) 판정 대상.
        if (roomId != _surveillanceTargetRoomId)
            TabooRuleSystem.Instance?.NotifyCctvSwitched(roomId);
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
        TabooRuleSystem.Instance?.NotifyEmployeeMoved(employeeId, roomId);
        // 이미 그 방에 서 있으면 이동이 없어 ArriveAtRoom 이 불리지 않는다 — 점유자로 직접 넣는다.
        AddOccupant(emp.CurrentRoomId, employeeId);
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
        if (!emp.Isolated) RemoveOccupant(emp.CurrentRoomId, employeeId);
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
            room.CctvBlockedUntil = 0f;
            room.InfoDistorted = false;
            room.PowerOn = true;
            room.Locked = false;
            room.UnstaffedTimer = 0f;
        }
        // 새 근무의 초기 배치 이동은 다시 원래 속도로 걷는다.
        // 기절은 "당일 업무 불가"이므로 새 근무가 시작되면 풀린다(스트레스 수치는 이월).
        foreach (var emp in _employeeStates.Values)
        {
            emp.InitialDeployDone = false;
            emp.Incapacitated = false;
            emp.Stress = Mathf.Clamp(emp.Stress, Config.Instance.Data.StressMin, Config.Instance.Data.StressMax);
        }
        _ventStressTimer = 0f;
        _coreUnstableTimer = 0f;
        TabooRuleSystem.Instance?.ResetRuntimeState();
        // 지난 근무의 진술·알리바이는 새 근무로 넘어오지 않는다.
        NSP.Dialogue.DialogueClaimState.ResetAll();
        IncidentTracker.Reset();

        // 방 점유자 목록을 이번 근무의 실제 근무자로 다시 만든다.
        // 프로젝트 로드 시점에는 6명 전원이 각자 StartRoomId 에 점유자로 들어가 있는데,
        // 그대로 두면 근무표에서 빼 놓은 직원까지 그 방에서 업무 게이지를 채우고 금기 인원수에
        // 잡히고 전화도 받는다. 배치된 직원(+격리자)만 남긴다.
        foreach (var room in _roomStates.Values)
            room.OccupantEmployeeIds.Clear();
        foreach (var emp in _employeeStates.Values)
        {
            if (!emp.Alive) continue;
            if (!emp.Isolated && string.IsNullOrEmpty(emp.AssignedRoomId)) continue;
            AddOccupant(emp.CurrentRoomId, emp.EmployeeId);
        }
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
        TickCoreInstability(d);
        TickUnstaffedAccidents(d);
        TickCctvObservation(d);
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

        var others = room.OccupantEmployeeIds
            .Where(id => id != saboteur.EmployeeId)
            .Select(id => _employeeStates.GetValueOrDefault(id))
            .Where(e => e is { Alive: true, Isolated: false })
            .ToList();

        // "미감시" = 지금 이 방을 CCTV로 실제로 보고 있지 않다.
        bool unwatched = !IsRoomUnderActiveCctv(saboteur.CurrentRoomId);
        // "혼란" = CCTV와 조명이 둘 다 죽어 관리자가 위치도 행동도 모르는 상태.
        bool blackoutChaos = !GameState.Instance.IsCctvOperational()
                             && !GameState.Instance.IsConsumerPowered(PowerConsumer.Lighting);

        // 경비실에 근무자가 있으면 방해공작 성공 확률이 40% 낮아진다.
        float surveillanceMult = OnDutyCount(GuardRoomId) > 0
            ? Config.Instance.Data.SurveillanceSaboteurChanceMultiplier : 1f;

        // ── ⑥ 조건부 살인 : 단둘 + 미감시 + 제3자 없음 → 8초간 범행 시도 ──────────
        // 조건이 유지되는 동안에만 타이머가 흐르고, 하나라도 깨지면 즉시 중단된다.
        if (TickKillAttempt(saboteur, others, unwatched, delta)) return;

        if (!unwatched) return;   // 감시 중이면 아래 방해공작은 시도하지 않는다
        var cfg = Config.Instance.Data;
        if (_rng.NextDouble() >= cfg.SaboteurSabotageChance * surveillanceMult) return;

        string here = saboteur.CurrentRoomId;

        // ── ③ 전력 조작 : 발전실에 있을 때 ────────────────────────────────
        if (here == PowerRoomId && !GameState.Instance.IsPowerAccidentActive())
        {
            GameState.Instance.TriggerPowerAccident(cfg.SabotagePowerLoss);
            LogSabotage(saboteur, here, others, $"전력 계통 이상 — 최대 전력 -{cfg.SabotagePowerLoss} (원인 불명)");
            return;
        }

        // ── ⑤ 자재 폐기 : 정비실 / 저장고에 있을 때 ───────────────────────
        if (here is MaintenanceRoomId or StorageRoomId && GameState.Instance.Materials > 0)
        {
            GameState.Instance.AddMaterials(-cfg.SabotageMaterialLoss);
            LogSabotage(saboteur, here, others, $"자재 {cfg.SabotageMaterialLoss}개 분실 (기록 없음)");
            return;
        }

        // ── ①② 복구 작업 방해 : 코어 복구율 감소. 혼란 상태면 더 크게 깎는다 ──
        if (here == CoreRoomId || blackoutChaos)
        {
            float loss = blackoutChaos ? cfg.SabotageCoreLossBlackout : cfg.SabotageCoreLoss;
            GameState.Instance.AddCoreProgress(-loss, "복구 작업 방해");
            LogSabotage(saboteur, here, others, $"봉쇄 코어 복구율 -{loss:0}% (원인 불명)");
            return;
        }

        // ── ④ CCTV 방해 : 그 작업실 CCTV 를 일정 시간 차단한다 ─────────────
        var hereState = _roomStates.GetValueOrDefault(here);
        if (hereState != null)
        {
            hereState.CctvBlockedUntil = GameState.Instance.DayTimeSeconds + cfg.SabotageCctvBlockSeconds;
            LogSabotage(saboteur, here, others,
                $"CCTV 신호 교란 — {cfg.SabotageCctvBlockSeconds:0}초간 화면 없음");
        }

        // 배치된 직원은 관리자의 재배치 또는 실제 시설 문제(격리/대피) 없이는
        // 자기 담당 작업실을 떠나지 않는다. 파괴공작자는 현재 작업실에서만
        // 방해·위장 행동을 하며, 무작위 방 이동으로 근무표를 깨지 않는다.
    }

    // 방해공작 흔적. 실행자 id 는 남기되 로그 문구에는 이름을 쓰지 않는다 —
    // 플레이어는 "무슨 일이 있었는지"만 보고, 누구인지는 로그/CCTV/진술 교차로 좁혀야 한다.
    private void LogSabotage(EmployeeState actor, string roomId, List<EmployeeState> witnesses, string what)
    {
        EventLog.Instance?.LogEvent(LogEventType.Sabotage, actor.EmployeeId, roomId,
            $"⚠ {RoomName(roomId)} — {what}",
            witnesses.Select(w => w.EmployeeId));
    }

    // ⑥ 조건부 살인. 조건이 계속 유지되는 동안 KillAttemptSeconds 만큼 쌓여야 성공한다.
    private string _killAttemptVictimId = "";
    private float _killAttemptTimer;
    private bool TickKillAttempt(EmployeeState saboteur, List<EmployeeState> others, bool unwatched, float delta)
    {
        var cfg = Config.Instance.Data;

        bool alone = others.Count == 1;                       // 단둘 (제3자 없음)
        bool allowed = _killsToday < cfg.MurderMaxPerDay
                       && GameState.Instance.TotalKills < cfg.MurderMaxTotal;

        if (!alone || !unwatched || !allowed)
        {
            _killAttemptVictimId = "";
            _killAttemptTimer = 0f;
            return false;
        }

        string victimId = others[0].EmployeeId;
        if (_killAttemptVictimId != victimId)
        {
            _killAttemptVictimId = victimId;
            _killAttemptTimer = 0f;
        }

        // 이 함수는 판정 주기(SaboteurDecisionIntervalSeconds)마다 불리므로 그 간격만큼 쌓는다.
        _killAttemptTimer += cfg.SaboteurDecisionIntervalSeconds;
        if (_killAttemptTimer < cfg.KillAttemptSeconds) return false;

        _killAttemptVictimId = "";
        _killAttemptTimer = 0f;
        KillEmployee(victimId, saboteur.CurrentRoomId);
        return true;
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
        GameState.Instance.RegisterKill();
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

    // 환기실 상태에 따른 전 직원 스트레스.
    //   · 정상 근무 중        → 증가 없음
    //   · 근무자 없음         → VentUnstaffedStressIntervalSeconds 마다 +1
    //   · 환기 필터 고장(사고) → VentFaultStressIntervalSeconds 마다 +2 (고장이 우선)
    private float _ventStressTimer;
    private void TickVentilationFault(float delta)
    {
        var cfg = Config.Instance.Data;
        // 금기 페널티로 강제 정지된 환기는 시간이 지나면 저절로 풀린다(설비 고장과 구분).
        var taboo = TabooRuleSystem.Instance;
        if (taboo != null && GameState.Instance.VentilationDown && taboo.VentHaltUntil > 0f && !taboo.IsVentHalted
            && !HasActiveRepair(VentRoomId))
            GameState.Instance.SetVentilationDown(false);

        bool broken = GameState.Instance.VentilationDown;
        bool staffed = OnDutyCount(VentRoomId) > 0;

        if (!broken && staffed) { _ventStressTimer = 0f; return; }

        float interval = broken ? cfg.VentFaultStressIntervalSeconds : cfg.VentUnstaffedStressIntervalSeconds;
        float amount = broken ? cfg.VentFaultStressAmount : cfg.VentUnstaffedStressAmount;
        if (interval <= 0f) return;

        _ventStressTimer += delta;
        while (_ventStressTimer >= interval)
        {
            _ventStressTimer -= interval;
            foreach (var emp in _employeeStates.Values)
            {
                if (!emp.Alive || emp.Isolated) continue;
                AddStress(emp.EmployeeId, amount);
            }
        }
    }

    // 그 방의 CCTV 신호가 끊겨 있는가 — 설비 고장(수리 필요) 또는 방해공작에 의한 일시 차단.
    public bool IsRoomCctvBlocked(string roomId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        if (room == null) return false;
        if (room.CctvDisconnected) return true;
        return room.CctvBlockedUntil > GameState.Instance.DayTimeSeconds;
    }

    // 그 방에서 실제로 일할 수 있는 인원(생존 + 배치됨 + 기절 아님).
    public int OnDutyCount(string roomId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        if (room == null) return 0;
        int n = 0;
        foreach (var id in room.OccupantEmployeeIds)
        {
            var e = _employeeStates.GetValueOrDefault(id);
            if (e is { Alive: true, Isolated: false, Incapacitated: false }) n++;
        }
        return n;
    }

    // 봉쇄 코어 출력 불안정(사고) — 수리 전까지 주기적으로 코어 복구율이 깎인다.
    private float _coreUnstableTimer;
    private void TickCoreInstability(float delta)
    {
        var cfg = Config.Instance.Data;
        if (!GameState.Instance.CoreOutputUnstable) { _coreUnstableTimer = 0f; return; }
        if (cfg.CoreUnstableIntervalSeconds <= 0f) return;

        _coreUnstableTimer += delta;
        while (_coreUnstableTimer >= cfg.CoreUnstableIntervalSeconds)
        {
            _coreUnstableTimer -= cfg.CoreUnstableIntervalSeconds;
            GameState.Instance.AddCoreProgress(-cfg.CoreUnstableCoreLoss, "코어 출력 불안정");
            EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", CoreRoomId,
                $"🚨 봉쇄 코어 출력 불안정 — 복구율 -{cfg.CoreUnstableCoreLoss:0}%");
        }
    }

    // 작업실을 근무자 없이 방치하면 그 방의 사고가 발생한다(RoomDef 에 방마다 정의).
    // 사고는 "수리" 업무로 남고, 지정된 인원/시간을 채워야 기능이 복구된다.
    // CCTV 시청 기록 — 한 작업실을 3초 이상 계속 지켜보면 "관리자가 직접 봤다"로 남긴다.
    // 채널을 휙휙 넘기는 것만으로는 증거가 되지 않는다.
    private const float CctvObservationSeconds = 3f;
    private string _watchedRoomId = "";
    private float _watchedSeconds;

    private void TickCctvObservation(float delta)
    {
        string room = GameState.Instance.IsCctvOperational() ? SurveillanceTargetRoomId ?? "" : "";
        if (string.IsNullOrEmpty(room) || room != _watchedRoomId || IsRoomCctvBlocked(room))
        {
            _watchedRoomId = room;
            _watchedSeconds = 0f;
            return;
        }

        _watchedSeconds += delta;
        if (_watchedSeconds < CctvObservationSeconds) return;
        _watchedSeconds = 0f;
        NSP.Dialogue.PlayerKnownEvidence.RecordCctvObservation(room, GameState.Instance.DayTimeSeconds,
            GetRoomState(room)?.OccupantEmployeeIds);
    }

    private void TickUnstaffedAccidents(float delta)
    {
        var cfg = Config.Instance.Data;
        foreach (var (roomId, room) in _roomStates)
        {
            var def = _roomDefs.GetValueOrDefault(roomId);
            if (def == null || def.IsRestricted || def.AccidentConsequence == RoomAccidentNone) continue;

            // 이미 그 방에 사고 수리 업무가 걸려 있으면 타이머를 멈춘다(중복 발생 방지).
            if (HasActiveRepair(roomId)) { room.UnstaffedTimer = 0f; continue; }

            if (OnDutyCount(roomId) > 0) { room.UnstaffedTimer = 0f; continue; }

            float limit = def.UnstaffedAccidentSeconds > 0f
                ? def.UnstaffedAccidentSeconds
                : cfg.UnstaffedAccidentSecondsDefault;

            room.UnstaffedTimer += delta;
            if (room.UnstaffedTimer < limit) continue;

            // DAY1 은 사고가 겹치지 않게 잠시 미룬다. 타이머는 유지되므로
            // 조건이 풀리는 즉시 발생한다(원인은 그대로 "근무자 부재").
            if (!CanStartNewIncident()) { room.UnstaffedTimer = limit; continue; }

            room.UnstaffedTimer = 0f;
            TriggerRoomAccident(roomId, def);
        }
    }

    // DAY1 학습 편의: 대형 작업실 사고를 한 번에 하나로 제한한다. DAY2 이후에는 제한 없음.
    private bool CanStartNewIncident()
    {
        if ((GameState.Instance?.CurrentDay ?? 1) != 1) return true;
        var cfg = Config.Instance.Data;
        if (IncidentTracker.ActiveCount >= Mathf.Max(1, cfg.Day1MaxActiveIncidents)) return false;
        return GameState.Instance.DayTimeSeconds - IncidentTracker.LastIncidentAt >= cfg.IncidentGapSeconds;
    }

    private const TabooConsequenceType RoomAccidentNone = (TabooConsequenceType)(-1);

    private bool HasActiveRepair(string roomId) =>
        _activeTasks.Any(t => t.RoomId == roomId && t.IsRepair && t.Status == SpawnedTaskStatus.Active);

    // 사고 발생 — 결과를 적용하고, 그 방에 수리 업무를 띄운다.
    private void TriggerRoomAccident(string roomId, RoomDef def)
    {
        EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
            $"🚨 {RoomName(roomId)} — {def.AccidentName} (무인 방치)");
        // 먼저 사고를 열어 둔다 — 뒤이어 적용되는 시설 손실이 이 사고의 결과로 묶인다.
        IncidentTracker.Open(roomId, def.AccidentName, "장시간 근무자 부재",
            "설비 수리 필요", def.RepairMinWorkers);
        TabooRuleSystem.Instance?.ApplyRoomConsequence(def.AccidentConsequence, roomId, def.AccidentAmount);

        _activeTasks.Add(new SpawnedTask
        {
            TaskId = def.RepairTaskId,
            RoomId = roomId,
            Recurring = false,
            IsRepair = true,
            Status = SpawnedTaskStatus.Active,
            TimeLimitSeconds = float.MaxValue,
            GaugeRequired = Mathf.Max(1f, def.RepairSeconds),
            MinWorkersOverride = Mathf.Max(1, def.RepairMinWorkers),
        });
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
            // 센서에는 범인을 절대 넘기지 않는다 — 원인은 "판별 불가".
            IncidentTracker.Anomaly(roomId, "비정상 조작 흔적 감지", "설비 상태 이상");
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
        // 배치되지 않은 직원(오늘 비번)은 방에 들어가도 근무 인원으로 세지 않는다.
        if (emp.Isolated || !string.IsNullOrEmpty(emp.AssignedRoomId))
            AddOccupant(emp.CurrentRoomId, emp.EmployeeId);

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

    // 오늘 근무자인가 — 근무표에 배치된 직원만 실제로 일하고, 전화를 받고, 인원수에 잡힌다.
    // 배치되지 않은 직원은 시작 방에 서 있더라도 오늘 근무 인원이 아니다(미니맵도 안 그린다).
    public bool IsOnDuty(string employeeId)
    {
        var e = _employeeStates.GetValueOrDefault(employeeId);
        return e is { Alive: true, Isolated: false } && !string.IsNullOrEmpty(e.AssignedRoomId);
    }

    private void AddOccupant(string roomId, string employeeId)
    {
        if (_roomStates.TryGetValue(roomId, out var room) && !room.OccupantEmployeeIds.Contains(employeeId))
            room.OccupantEmployeeIds.Add(employeeId);
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
            // 기절(스트레스 46+)한 직원은 방에 있어도 업무 인원으로 세지 않는다.
            var workers = room == null ? new List<EmployeeState>() : room.OccupantEmployeeIds
                .Select(id => _employeeStates.GetValueOrDefault(id))
                .Where(e => e != null && e.Alive && !e.Isolated && !e.Incapacitated)
                .ToList();

            bool blockedByMaterials = taskDef.EffectType == TaskEffectType.AddCoreProgress
                && GameState.Instance.Materials < Config.Instance.Data.MaterialsPerCoreGauge;

            // 자재가 없어 코어 복구가 멈추거나 다시 도는 순간만 기록한다(매 틱 기록 금지).
            if (blockedByMaterials != st.MaterialsBlockedLogged && workers.Count > 0)
            {
                st.MaterialsBlockedLogged = blockedByMaterials;
                EventLog.Instance?.LogEvent(LogEventType.ResourceShortage, "", st.RoomId,
                    blockedByMaterials
                        ? $"⚠ {RoomName(st.RoomId)} — 자재 부족으로 '{taskDef.DisplayName}' 정지 (자재 {GameState.Instance.Materials})"
                        : $"✓ {RoomName(st.RoomId)} — 자재 확보, '{taskDef.DisplayName}' 재개");
            }

            if (workers.Count > 0)
            {
                // 직원이 실제로 이 방에서 발생 업무를 수행하기 시작함 = TaskStart (1인 1회).
                foreach (var w in workers)
                {
                    if (!st.StartedWorkerIds.Add(w.EmployeeId)) continue;
                    // 🔧 = 사고 복구 작업. 평상시 업무 시작과 달리 관리자에게도 보여야 하므로
                    // 표식을 남긴다(시설 로그 화면이 이 표식으로 수리 시작을 골라낸다).
                    EventLog.Instance?.LogEvent(LogEventType.TaskStart, w.EmployeeId, st.RoomId,
                        $"{(st.IsRepair ? "🔧 " : "")}{Codename(w.EmployeeId)} {RoomName(st.RoomId)} 도착 / {taskDef.DisplayName} 시작",
                        workers.Where(x => x.EmployeeId != w.EmployeeId).Select(x => x.EmployeeId));
                }

                // 최소 필요 인원 미만이면 게이지가 전혀 차지 않는다 — DAY1 발전기 점검(2명 필요)이
                // DAY1 금기(발전실 2명 금지)와 반드시 충돌하도록 만드는 지점.
                int minWorkers = st.MinWorkersOverride > 0 ? st.MinWorkersOverride : Mathf.Max(1, taskDef.MinWorkersToProgress);
                if (workers.Count >= minWorkers && !blockedByMaterials)
                {
                    // 1초에 (기본 속도 × 기술 배율 × 스트레스 배율) 만큼 게이지가 찬다.
                    // 기본 속도가 1이므로 GaugeRequired 값이 곧 "기술2·정상 스트레스 1명 기준 초"다.
                    float baseRate = Config.Instance.Data.BaseTaskWorkRate;
                    // 금기 위반 페널티(업무 속도 감소)가 걸려 있으면 여기서 같이 곱해진다.
                    float tabooPenalty = TabooRuleSystem.Instance?.WorkPenaltyMultiplier ?? 1f;
                    float rate = workers.Sum(w => baseRate * TechWorkMultiplier(w.EmployeeId) * StressWorkRate(w))
                                 * tabooPenalty;
                    st.Gauge += rate * delta;
                }
            }

            if (st.Gauge >= st.GaugeRequired)
                ResolveTask(st, taskDef, true);
            else if (!st.Recurring && st.Elapsed >= st.TimeLimitSeconds)
                ResolveTask(st, taskDef, false);
        }
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
            // 무인 방치 사고는 RoomDef 가 사고 종류를 소유하므로 그쪽을 우선한다.
            var rdef = _roomDefs.GetValueOrDefault(st.RoomId);
            var consequence = rdef != null && (int)rdef.AccidentConsequence >= 0
                ? rdef.AccidentConsequence
                : taskDef.NeglectConsequenceType;
            TabooRuleSystem.Instance?.RepairRoomConsequence(consequence, st.RoomId);
            IncidentTracker.Resolve(st.RoomId);
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
            IncidentTracker.Open(st.RoomId, AlertSystem.HeadlineFor(st.TaskId), "경고 시간 내 대응 실패",
                $"설비 수리 필요 (최소 {Mathf.Max(1, taskDef.MinWorkersToProgress)}명)",
                taskDef.MinWorkersToProgress);
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
                // 코어 출력 불안정(사고) 중에는 복구가 아예 진행되지 않는다.
                if (GameState.Instance.CoreOutputUnstable)
                {
                    badge += " · ⚠ 코어 출력 불안정 — 복구 정지";
                    break;
                }
                int consumed = Config.Instance.Data.MaterialsPerCoreGauge;
                GameState.Instance.AddMaterials(-consumed);
                GameState.Instance.AddCoreProgress(task.EffectAmount, task.DisplayName);
                badge += $" · 코어 +{task.EffectAmount:0}% · 📦 자재 -{consumed}";
                break;
            case TaskEffectType.RaiseMaterialsCap:
                // 저장고: 자재 보유 한도를 올린다(Config.MaterialsCapMax 상한).
                int before = GameState.Instance.MaterialsCap;
                GameState.Instance.AddMaterialsCap((int)task.EffectAmount);
                int gained = GameState.Instance.MaterialsCap - before;
                badge += gained > 0
                    ? $" · 📦 자재 한도 +{gained} (→ {GameState.Instance.MaterialsCap})"
                    : $" · 자재 한도 최대치({GameState.Instance.MaterialsCap})";
                break;
            case TaskEffectType.ReduceStress:
                // 의료 장비 오염(사고) 중에는 치료가 불가능하다.
                if (GameState.Instance.MedicalContaminated)
                {
                    badge += " · ⚠ 의료 장비 오염 — 치료 불가";
                    break;
                }
                // 치료 대상 = 이 방(의무실)에 있는 직원. 감소에는 담력 배율을 걸지 않는다.
                foreach (var occId in (_roomStates.GetValueOrDefault(roomId)?.OccupantEmployeeIds ?? new List<string>()).ToList())
                    AddStress(occId, -task.EffectAmount);
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
