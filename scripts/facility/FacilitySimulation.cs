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
    public static string GuardRoomIdPublic => GuardRoomId;
    public static string CoreRoomIdPublic => CoreRoomId;
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
    // 근무 시작 때 직원들이 출발하는 곳(중앙 제어실).
    public const string DeployOriginRoomId = "central_office";
    private readonly Dictionary<string, Color> _roomVisualColors = new();
    private readonly Random _rng = new();
    // 직원별 "오늘의 기분" 배정. 기분은 EmployeeState 에만 저장되고 이 클래스는 고르기만 한다.
    // 표현 풀(res://data/moods)을 처음 쓸 때 읽는다 — autoload 생성 순서에 기대지 않는다.
    private DailyMoodSystem _dailyMoods;
    private DailyMoodSystem Moods => _dailyMoods ??= new DailyMoodSystem();
    private float _saboteurDecisionTimer = 0f;
    // 사고가 나기 전에 대응할 시간을 주는 경고 시스템(data/ops/*.tres 가 수치를 쥔다).
    private readonly FacilityWarningSystem _warnings = new();
    public FacilityWarningSystem Warnings => _warnings;

    // 괴물 — 작업실 사고와 분리된 두 번째 사고 계통. CCTV 로 찾아내 계속 보고 있어야 사라진다.
    private readonly GhostHauntSystem _ghost = new();
    public GhostHauntSystem Ghost => _ghost;

    // 오늘 무너진 직원. 동료의 죽음이 확인된 순간, 겁이 많은 사람은 여기 들어온다.
    //
    // 기절(Incapacitated)과 다르다 — 의무실로 옮겨지지도 않고 근무표에서 빠지지도 않는다.
    // 자기 자리에 그대로 앉아 같은 말만 되뇌며 아무 일도 하지 않는다. 그 모습 자체가
    // 관리자가 CCTV 에서 보게 되는 결과다. 하루가 끝나면 풀린다.
    private readonly HashSet<string> _panicked = new();
    public bool IsPanicked(string employeeId) =>
        !string.IsNullOrEmpty(employeeId) && _panicked.Contains(employeeId);

    // 그 사람이 되뇌는 말. 성격마다 다르지만 전부 "오늘 여기서 나가고 싶다"는 소리다.
    public static string PanicMutter(string employeeId) => employeeId switch
    {
        "sheep" => "다 죽을 거야... 다 죽을 거야...",
        "rabbit" => "아니야, 아니야, 나 아니야...",
        "dog" => "제가... 제가 더 빨리 갔어야 했는데...",
        "cat" => "...여기서 나가야 해. 지금.",
        "fox" => "그냥 오늘만... 오늘만 넘기면 되는 거잖아...",
        "wolf" => "다음은 누구지. 다음은 누구야.",
        _ => "...여기서 나가야 해.",
    };

    // 죽음이 확인됐다 — 겁이 많은 사람부터 무너진다.
    private void SpreadPanic(string victimId, string roomId)
    {
        var cfg = Config.Instance.Data;
        foreach (var st in _employeeStates.Values)
        {
            if (st.EmployeeId == victimId || !st.Alive || st.Isolated) continue;
            if (_panicked.Contains(st.EmployeeId)) continue;
            // 성향이 이 선을 넘는 사람만 무너진다. 나머지는 스트레스만 받고 계속 일한다.
            if (EmployeeTraits.Get(st.EmployeeId).AvoidsDanger < cfg.PanicAvoidsDangerFrom)
            {
                AddStress(st.EmployeeId, cfg.PanicStress * 0.5f, "동료 사망");
                continue;
            }
            _panicked.Add(st.EmployeeId);
            AddStress(st.EmployeeId, cfg.PanicStress, "동료 사망");
            EventLog.Instance?.LogEvent(LogEventType.Neglect, st.EmployeeId, st.CurrentRoomId,
                $"{Codename(st.EmployeeId)} | 업무 중단 — 극도의 불안 상태");
        }
    }
    // 오늘 이미 성공시킨 방해공작 횟수 — DAY1 은 한 번으로 제한한다.
    private int _sabotageActionsToday;
    // 결번자가 "언제 어디서" 손을 댈지 정하는 기회 판정(이동·체류 전조를 남긴다).
    private readonly SaboteurPlan _saboteurPlan = new();
    public SaboteurPlan Saboteur => _saboteurPlan;
    // 성격에 따른 정상 직원의 반응 이동 — 결번자의 이동과 똑같이 로그에 남는다.
    private readonly EmployeeBehaviorSystem _behavior = new();
    public EmployeeBehaviorSystem Behavior => _behavior;
    private readonly Dictionary<string, float> _patrolTimers = new();
    // 순찰이 각 방을 마지막으로 확인한 시각(고르게 돌기 위한 것).
    private readonly Dictionary<string, float> _patrolSeenAt = new();
    // 배치표에 없는 자리에 오래 머무는 직원을 경비가 알아채기까지의 시간.
    private readonly Dictionary<string, float> _offPostTimers = new();
    private readonly HashSet<string> _offPostLogged = new();
    private int _killsToday = 0;
    private bool _cctvWasOperational = true;
    private bool _powerLossMurderTriggeredThisShift;
    // 조명 전력이 끊긴 채로 흐른 시간. 관리자가 다시 켜면 0 으로 돌아간다.
    private float _darknessSeconds;
    public float DarknessSeconds => _darknessSeconds;
    // 지금이 "어둠"인가 — 조명이 BlackoutMurderAfterSeconds 이상 꺼져 있었다.
    public bool IsDarknessCritical =>
        _darknessSeconds >= (Config.Instance?.Data?.BlackoutMurderAfterSeconds ?? 18f);

    // DAY1 고정 스케줄(data/spawns/*.tres, SpawnAtSeconds 순) + 실제로 발생한 업무 인스턴스들.
    private readonly List<TaskSpawnDef> _schedule = new();
    private readonly List<SpawnedTask> _activeTasks = new();
    private int _scheduleCursor = 0;
    // 이번 근무에서 각 스폰이 실제로 뜨는 시각(흔들림 적용 후).
    private readonly Dictionary<int, float> _scheduleJitter = new();

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
        // 중요 사건 경보 연출(붉은 점멸 + 상단 배너). 화면 위에만 얹히므로
        // 어느 씬에서 근무하든 같은 방식으로 뜬다.
        AddChild(new NSP.Ui.FacilityAlertHud());
        LoadDefinitions("res://data/employees/", _employeeDefs, d => d.EmployeeId);
        LoadDefinitions("res://data/rooms/", _roomDefs, d => d.RoomId);
        LoadDefinitions("res://data/tasks/", _taskDefs, d => d.TaskId);
        LoadSchedule("res://data/spawns/");
        LoadRelationships();

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
            // 모두 중앙 제어실에서 시작한다 — 근무가 시작되면 거기서 배치된 작업실로 걸어간다.
            var startRoom = _roomDefs.GetValueOrDefault(DeployOriginRoomId)
                            ?? _roomDefs.Values.FirstOrDefault(r => r.RoomId == def.StartRoomId)
                            ?? _roomDefs.Values.FirstOrDefault();
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

        // 근무 배치 화면이 열리기 전에 이미 값이 있어야 한다(ShiftFlowController 가 DAY 마다 다시 굴린다).
        RollDailyMoods();
    }

    // --- 오늘의 기분상태 ----------------------------------------------------
    // 하루가 시작될 때(근무 배치 진입) 한 번만 호출한다. 방해자 여부는 전혀 쓰지 않는다.
    public void RollDailyMoods() => Moods.RollForDay(_employeeStates.Values);

    // 근무 배치 UI / 대화 시스템이 읽는 단일 창구.
    public string GetDailyMood(string employeeId) =>
        _employeeStates.GetValueOrDefault(employeeId)?.DailyMood ?? "";

    // 근무 배치 화면에 하루 한 줄 뜨는 "오늘의 한마디"(표시 전용).
    public string GetDailyRemark(string employeeId) =>
        _employeeStates.GetValueOrDefault(employeeId)?.DailyRemark ?? "";

    // 시작화면으로 돌아가 처음부터 다시 시작. autoload 라 씬을 다시 로드해도 남아 있는
    // 배치/사망/격리/스트레스/발생 업무를 전부 지우고 DAY 1 초기 상태로 되돌린다.
    public void ResetRun()
    {
        _activeTasks.Clear();
        _scheduleCursor = 0;
        _scheduleJitter.Clear();
        _saboteurDecisionTimer = 0f;
        _killsToday = 0;
        _cctvWasOperational = true;
        _powerLossMurderTriggeredThisShift = false;
        _darknessSeconds = 0f;
        _surveillanceTargetRoomId = "";
        _forcedSurveillanceRoomId = "";
        _forcedSurveillanceUntil = -1;
        _roomVisualCenters.Clear();
        _roomVisualColors.Clear();
        _tensionStressTimers.Clear();
        _argumentTimers.Clear();
        // 관계값도 시드로 되돌린다(Phase 3 의 근무 중 변화가 다음 판으로 넘어가지 않게).
        LoadRelationships();
        BuildInitialStates();
    }

    private static void LoadRelationships()
    {
        string path = Config.Instance?.Data?.RelationshipTablePath;
        if (string.IsNullOrEmpty(path)) RelationshipSystem.Load();
        else RelationshipSystem.Load(path);

        string lines = Config.Instance?.Data?.OverheardLinesPath;
        if (string.IsNullOrEmpty(lines)) OverheardDialogue.Load();
        else OverheardDialogue.Load(lines);
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

    // 오늘 실제로 근무에 나오는 직원(EmployeeDef.UnlockDay 기준). DAY0 교육에는 일부만 나온다.
    // 배치표 / 휴게 명단 / 보고서처럼 "오늘의 인원"을 보여주는 곳은 전부 이쪽을 쓴다.
    public List<string> GetActiveEmployeeIds() =>
        _employeeStates.Keys
            .Where(id => (GameState.Instance?.CurrentDay ?? 1) >= (_employeeDefs.GetValueOrDefault(id)?.UnlockDay ?? 1))
            .ToList();

    public bool IsEmployeeActiveToday(string employeeId) =>
        (GameState.Instance?.CurrentDay ?? 1) >= (_employeeDefs.GetValueOrDefault(employeeId)?.UnlockDay ?? 1);
    public IReadOnlyCollection<string> GetRoomIds() => _roomStates.Keys;
    public IEnumerable<TaskDef> GetTaskDefs() => _taskDefs.Values;

    public EmployeeDef GetEmployeeDef(string id) => _employeeDefs.GetValueOrDefault(id);
    public RoomDef GetRoomDef(string id) => _roomDefs.GetValueOrDefault(id);
    public TaskDef GetTaskDef(string id) => _taskDefs.GetValueOrDefault(id);
    public EmployeeState GetEmployeeState(string id) => _employeeStates.GetValueOrDefault(id);
    public RoomState GetRoomState(string id) => _roomStates.GetValueOrDefault(id);

    // 오늘 이 작업실을 쓰는가(RoomDef.UnlockDay 기준). 잠긴 방은 배치도, 업무 발생도,
    // 무인 방치 사고도 없고 시설 지도에도 비활성으로 표시된다.
    public bool IsRoomActive(string roomId) => DayFeatures.IsRoomActive(_roomDefs.GetValueOrDefault(roomId));

    // 스트레스 연동 지점(단일 창구). 모든 스트레스 증감은 반드시 여기를 지난다.
    //  · 증가분에는 담력 배율이 걸린다 (담력 1=100% / 2=80% / 3=60%). 감소(치료)에는 안 건다.
    //  · 값은 1~50 으로 고정된다.
    //  · 46 이상이 되면 기절 — 의무실로 강제 송환되고 당일 업무 불가.
    public void AddStress(string employeeId, float amount, string reason = "")
    {
        // V3 초반 단순화: 스트레스가 잠긴 날에는 수치가 아예 움직이지 않는다(기절도 없다).
        if (!DayFeatures.StressEnabled) return;

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

    // 46~50 = 기절. 근무에서 빠지고 의무실로 옮겨졌다가, 회복 시간이 지나면 복귀한다.
    private void CheckFaint(EmployeeState st)
    {
        var cfg = Config.Instance.Data;
        if (st.Incapacitated || st.Stress < cfg.StressFaintFrom || !st.Alive) return;

        st.Incapacitated = true;
        st.FaintRecoverTimer = cfg.StressFaintRecoverySeconds;
        EventLog.Instance?.LogEvent(LogEventType.Neglect, st.EmployeeId, st.CurrentRoomId,
            cfg.StressFaintRecoverySeconds > 0f
                ? $"🚨 {Codename(st.EmployeeId)} 스트레스 {st.Stress:0} — 기절, 의무실로 이송 (약 {cfg.StressFaintRecoverySeconds:0}초 회복)"
                : $"🚨 {Codename(st.EmployeeId)} 스트레스 {st.Stress:0} — 기절, 의무실로 이송 (당일 업무 불가)");

        // 격리 중이 아니면 의무실로 옮긴다. 배치는 유지해 두어 관리자가 상황을 볼 수 있게 한다.
        if (!st.Isolated && _roomDefs.ContainsKey(MedicalRoomId))
            BeginPathTo(st, MedicalRoomId);
    }

    // 기절 회복 — 의무실에서 회복 시간이 지나면 스트레스를 낮추고 원래 배치로 돌려보낸다.
    private void TickFaintRecovery(float delta)
    {
        var cfg = Config.Instance.Data;
        if (cfg.StressFaintRecoverySeconds <= 0f) return;
        foreach (var st in _employeeStates.Values)
        {
            if (!st.Incapacitated || !st.Alive) continue;
            st.FaintRecoverTimer -= delta;
            if (st.FaintRecoverTimer > 0f) continue;

            st.Incapacitated = false;
            st.Stress = Mathf.Clamp(cfg.StressAfterRecovery, cfg.StressMin, cfg.StressFaintFrom - 1f);
            EventLog.Instance?.LogEvent(LogEventType.Neglect, st.EmployeeId, st.CurrentRoomId,
                $"{Codename(st.EmployeeId)} 회복 — 스트레스 {st.Stress:0}, 근무 복귀");
            if (!st.Isolated && !string.IsNullOrEmpty(st.AssignedRoomId) && st.AssignedRoomId != st.CurrentRoomId)
                BeginPathTo(st, st.AssignedRoomId);
        }
    }

    // 스트레스 구간별 업무 속도 배율. 1~10 정상 / 11~30 주의 / 31~45 위험 / 46+ 기절(0).
    public float StressWorkRate(EmployeeState st)
    {
        var cfg = Config.Instance.Data;
        if (!DayFeatures.StressEnabled) return cfg.StressWorkRateNormal;
        if (st.Incapacitated || st.Stress >= cfg.StressFaintFrom) return 0f;
        if (st.Stress >= cfg.StressDangerFrom) return cfg.StressWorkRateDanger;
        if (st.Stress >= cfg.StressCautionFrom) return cfg.StressWorkRateCaution;
        return cfg.StressWorkRateNormal;
    }

    // 스트레스 구간 이름 — UI 표시에 쓴다(수치 판정은 위 함수들이 한다).
    public string StressBandName(EmployeeState st)
    {
        var cfg = Config.Instance.Data;
        if (!DayFeatures.StressEnabled) return "정상";
        if (st.Incapacitated || st.Stress >= cfg.StressFaintFrom) return "기절";
        if (st.Stress >= cfg.StressDangerFrom) return "위험";
        if (st.Stress >= cfg.StressCautionFrom) return "주의";
        return "정상";
    }

    // --- 능력치 3종의 효과 (Config 의 배열에서만 읽는다) ----------------------
    private static float StatLookup(float[] table, int stat) =>
        table == null || table.Length == 0 ? 1f : table[Mathf.Clamp(stat, 0, table.Length - 1)];

    // 기술 → 업무 속도 배율.
    // 능력치가 잠긴 날(DayFeatures.StatsEnabled == false)에는 전원 "보통(2)"으로 읽어
    // 배율이 1.0 이 된다 — 누구를 어디에 넣어도 업무 속도가 같아진다.
    public float TechWorkMultiplier(string employeeId) =>
        StatLookup(Config.Instance.Data.TechWorkRate,
            DayFeatures.EffectiveStat(_employeeDefs.GetValueOrDefault(employeeId)?.Tech ?? 2));

    // 담력 → 스트레스 획득량 배율.
    public float CourageStressMultiplier(string employeeId) =>
        StatLookup(Config.Instance.Data.CourageStressGain,
            DayFeatures.EffectiveStat(_employeeDefs.GetValueOrDefault(employeeId)?.Courage ?? 2));

    // 관찰 → 단서 포착 확률(0~1). 목격/추리 정보가 실제로 남을 확률에 쓴다.
    public float ObservationClueChance(string employeeId) =>
        StatLookup(Config.Instance.Data.ObservationClueChance,
            DayFeatures.EffectiveStat(_employeeDefs.GetValueOrDefault(employeeId)?.Observation ?? 2));

    // 로그 표시용 — 내부 id 대신 플레이어가 보는 코드네임/방 이름으로 남기기 위한 헬퍼.
    private string Codename(string employeeId) => _employeeDefs.GetValueOrDefault(employeeId)?.Codename ?? employeeId;
    private string RoomName(string roomId) => _roomDefs.GetValueOrDefault(roomId)?.DisplayName ?? roomId;
    // 같은 이름을 경고 시스템·정산 화면도 쓴다.
    public string RoomDisplayName(string roomId) => RoomName(roomId);

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

    // 연출로 CCTV 를 강제 전환하기 직전에 보고 있던 채널. 연출이 끝나면 여기로 돌아간다.
    private string _preForcedSurveillanceRoomId = "";

    public void ForceSurveillanceTarget(string roomId, float seconds)
    {
        if (!IsSurveillanceForced()) _preForcedSurveillanceRoomId = _surveillanceTargetRoomId ?? "";
        _forcedSurveillanceRoomId = roomId ?? "";
        _forcedSurveillanceUntil = Time.GetTicksMsec() / 1000.0 + Mathf.Max(0.1f, seconds);
        _surveillanceTargetRoomId = _forcedSurveillanceRoomId;
    }

    public void ReleaseForcedSurveillance(string roomId = "")
    {
        if (!string.IsNullOrEmpty(roomId) && _forcedSurveillanceRoomId != roomId) return;
        EndForcedSurveillance();
    }

    // 강제 전환 해제 — 연출 전에 보던 채널로 되돌린다. 전력이 없던 상태였다면
    // 채널만 돌아가고 화면은 그대로 "CCTV POWER OFF" 로 표시된다(전력 판정은 건드리지 않는다).
    private void EndForcedSurveillance()
    {
        _forcedSurveillanceRoomId = "";
        _forcedSurveillanceUntil = -1;
        if (!string.IsNullOrEmpty(_preForcedSurveillanceRoomId))
            _surveillanceTargetRoomId = _preForcedSurveillanceRoomId;
        _preForcedSurveillanceRoomId = "";
    }

    private bool IsSurveillanceForced()
    {
        if (string.IsNullOrEmpty(_forcedSurveillanceRoomId)) return false;
        if (Time.GetTicksMsec() / 1000.0 < _forcedSurveillanceUntil) return true;
        // 시간이 다 되어 저절로 풀릴 때도 원래 채널로 되돌린다.
        EndForcedSurveillance();
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

    // 근무 시작 순간의 배치를 적어 둔다 — ControlRoom3DController.BeginShift 가 배치 확정 직후 부른다.
    // 살아 있고 격리되지 않았고 배치된 직원만 그날 근무자다. 나머지는 빈 값(= 오늘 근무하지 않음).
    public static int ShiftStartVersion { get; private set; }

    public void RecordShiftStart()
    {
        int day = GameState.Instance?.CurrentDay ?? 1;
        foreach (var st in _employeeStates.Values)
        {
            st.ShiftStartDay = day;
            st.ShiftStartRoomId = st.Alive && !st.Isolated ? st.AssignedRoomId ?? "" : "";
        }
        ShiftStartVersion++;
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
        if (!DayFeatures.IsRoomActive(roomDef))
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

        // 이동 중에 재배치를 받으면, 지금 서 있는 자리에서 **가까운 쪽** 방을 출발점으로 삼는다.
        // 무조건 "향하던 방까지 마저 간 뒤"로 하면, 옆 방으로 옮기라고 해도 반대편 방을
        // 한 번 찍고 돌아오는 길이 나온다.
        string start = emp.CurrentRoomId;
        if (emp.IsMoving && !string.IsNullOrEmpty(emp.TargetRoomId) && emp.TargetRoomId != emp.CurrentRoomId)
        {
            float toCurrent = emp.Position.DistanceTo(GetRoomPosition(emp.CurrentRoomId));
            float toTarget = emp.Position.DistanceTo(GetRoomPosition(emp.TargetRoomId));
            if (toTarget < toCurrent) start = emp.TargetRoomId;
        }
        if (start == destinationRoomId)
        {
            emp.PathQueue.Clear();
            emp.TargetRoomId = destinationRoomId;
            emp.ElbowWaypoint = null;
            emp.IsMoving = true;
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
    // 규칙은 CorridorElbow 한 곳에 있다(미니맵 FacilityMinimap 도 같은 함수로 통로를 그린다).
    private Vector2? ComputeElbowWaypoint(string fromRoomId, string toRoomId) =>
        CorridorElbow.Compute(GetRoomPosition(fromRoomId), GetRoomPosition(toRoomId),
            GetRoomPosition(DeployOriginRoomId));

    // 통로로 이어진 방(양방향). RoomDef.ConnectedRoomIds 는 한쪽에만 적혀 있을 수 있다.
    private IEnumerable<string> Neighbors(string roomId)
    {
        var def = _roomDefs.GetValueOrDefault(roomId);
        var own = def?.ConnectedRoomIds ?? new Godot.Collections.Array<string>();
        // 지도에서 맞붙어 있는 방(AdjacentRoomIds — 점선으로 그려지는 통로)도 실제로 지나갈 수 있다.
        // 이게 빠져 있어서 "바로 옆 방으로 옮겼는데 엉뚱한 방을 한 번 들렀다 가는" 길이 나왔다.
        var near = def?.AdjacentRoomIds ?? new Godot.Collections.Array<string>();
        return own.Concat(near)
            .Concat(_roomDefs.Values.Where(o => o.ConnectedRoomIds.Contains(roomId) || o.AdjacentRoomIds.Contains(roomId))
                                    .Select(o => o.RoomId))
            .Distinct();
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

            // 통로는 양방향이다. 데이터에는 한쪽 방에만 적힌 연결이 있어서(예: 의무실 → 중앙 제어실)
            // 반대편 방의 목록도 함께 본다 — 안 그러면 환기실 · 의무실로 가는 길이 없다고 판정돼 배치가 조용히 실패했다.
            foreach (var neighborId in Neighbors(current))
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

            // 통로는 양방향이다. 데이터에는 한쪽 방에만 적힌 연결이 있어서(예: 의무실 → 중앙 제어실)
            // 반대편 방의 목록도 함께 본다 — 안 그러면 환기실 · 의무실로 가는 길이 없다고 판정돼 배치가 조용히 실패했다.
            foreach (var neighborId in Neighbors(current))
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
        if (!DayFeatures.IsRoomActive(def)) return false;
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
        _scheduleJitter.Clear();
        _saboteurDecisionTimer = 0f;
        _killsToday = 0;
        _cctvWasOperational = true;
        _powerLossMurderTriggeredThisShift = false;
        _darknessSeconds = 0f;
        _tensionStressTimers.Clear();
        _argumentTimers.Clear();
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
            emp.FaintRecoverTimer = 0f;
            emp.Stress = Mathf.Clamp(emp.Stress, Config.Instance.Data.StressMin, Config.Instance.Data.StressMax);
        }
        _ventStressTimer = 0f;
        _coreUnstableTimer = 0f;
        _warnings.Reset();
        _sabotageActionsToday = 0;
        _saboteurPlan.Reset();
        _behavior.Reset();
        _patrolTimers.Clear();
        _patrolSeenAt.Clear();
        _offPostTimers.Clear();
        _offPostLogged.Clear();
        TabooRuleSystem.Instance?.ResetRuntimeState();
        // 지난 근무의 진술·알리바이는 새 근무로 넘어오지 않는다.
        NSP.Dialogue.DialogueClaimState.ResetAll();
        IncidentTracker.Reset();
        _ghost.Reset();
        _panicked.Clear();

        // 근무 시작 — 배치된 직원은 전부 중앙 제어실에서 출발해 자기 작업실로 걸어간다.
        // (예전에는 지난 근무의 자리나 캐릭터 기본 시작실에서 출발했는데, 그 방이 지도에 없으면
        //  옛 좌표(격리실 아래 먼 곳)에서 나타나 한참을 걸어왔다.)
        // 가상 시뮬레이션(DAY0)과 실제 게임 첫날(DAY1)만 — 이후 DAY 는 전날 자리에서 이어서 움직인다
        // (매일 중앙 제어실에서 걸어 나오면 DAY2+ 의 업무 시간이 그만큼 깎인다).
        if (_roomDefs.ContainsKey(DeployOriginRoomId) && GameState.Instance.CurrentDay <= 1)
        {
            foreach (var emp in _employeeStates.Values)
            {
                if (!emp.Alive || emp.Isolated || string.IsNullOrEmpty(emp.AssignedRoomId)) continue;
                emp.CurrentRoomId = DeployOriginRoomId;
                emp.Position = GetRoomPosition(DeployOriginRoomId);
                emp.IsMoving = false;
                emp.ElbowWaypoint = null;
                emp.PathQueue.Clear();
                emp.TargetRoomId = DeployOriginRoomId;
                BeginPathTo(emp, emp.AssignedRoomId);
            }
        }

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
            // 아직 자리로 걷는 중이면 도착할 때(ArriveAtRoom) 점유자로 들어간다.
            if (emp.IsMoving || emp.CurrentRoomId == DeployOriginRoomId) continue;
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
        TickPowerLossMurder(d);
        TabooRuleSystem.Instance?.Tick(d);
        TickSaboteur(d);
        TickPowerRestoreReveal();
        TickShiftStress(d);
        TickVentilationFault(d);
        TickRoomTension(d);
        TickCoreInstability(d);
        TickUnstaffedAccidents(d);
        _ghost.Tick(d, this);
        TickFaintRecovery(d);
        _warnings.Tick(d, this);
        _behavior.Tick(d, this);
        TickGuardPatrol(d);
        TickOffPostRecords(d);
        TickCctvObservation(d);
    }

    // 고정 스케줄에 따라 시간이 되면 업무를 발생시킨다.
    private void TickSchedule()
    {
        float now = GameState.Instance.DayTimeSeconds;
        while (_scheduleCursor < _schedule.Count && now >= SpawnTimeOf(_scheduleCursor))
        {
            SpawnFromDef(_schedule[_scheduleCursor]);
            _scheduleCursor++;
        }
    }

    // 이번 근무에서 이 스폰이 실제로 뜨는 시각. 흔들림은 근무마다 한 번만 뽑는다.
    private float SpawnTimeOf(int index)
    {
        if (_scheduleJitter.TryGetValue(index, out float at)) return at;
        var def = _schedule[index];
        float jitter = def.JitterSeconds > 0f
            ? (float)(_rng.NextDouble() * 2.0 - 1.0) * def.JitterSeconds
            : 0f;
        at = Mathf.Max(0f, def.SpawnAtSeconds + jitter);
        _scheduleJitter[index] = at;
        return at;
    }

    private void SpawnFromDef(TaskSpawnDef def)
    {
        var taskDef = _taskDefs.GetValueOrDefault(def.TaskId);
        if (taskDef == null)
        {
            GD.PushWarning($"FacilitySimulation: spawn references unknown task '{def.TaskId}'");
            return;
        }
        // 오늘 해당하지 않는 스폰은 건너뛴다(TaskSpawnDef.Day).
        if (def.Day > 0 && def.Day != (GameState.Instance?.CurrentDay ?? 1)) return;

        string roomId = !string.IsNullOrEmpty(def.RoomId) ? def.RoomId : taskDef.RoomId;

        // 오늘 잠겨 있는 작업실(환기실/의무실 등)에는 배치 자체가 불가능하므로 업무도 띄우지 않는다.
        if (!IsRoomActive(roomId)) return;

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

    // 조명 전력을 오래 끊어 두면 그 어둠 속에서 살인이 한 번 일어난다.
    //
    // 예전에는 조명이나 CCTV 전력이 끊긴 **그 순간** 바로 사람이 죽었다. 전력을 잠깐
    // 돌려 쓰는 것조차 즉사로 이어져, 관리자가 전력 패널을 만질 수 없었다.
    // 지금은 어둠이 BlackoutMurderAfterSeconds 만큼 **이어졌을 때만** 일어난다 —
    // 잠깐 끄는 것은 판단이고, 오래 끄는 것은 방치다.
    //
    // 이 경로만 오늘의 AllowMurder 를 보지 않는다. 다른 날에 살인이 없는 것은 결번자가
    // 그럴 생각이 없어서가 아니라 기회가 없어서이고, 그 기회를 만든 것은 관리자다.
    private void TickPowerLossMurder(float delta)
    {
        var dcfg = Config.Instance.Data;
        // 조명이 살아 있으면 어둠은 없던 일이 된다(다시 처음부터 쌓인다).
        if (GameState.Instance.IsConsumerPowered(PowerConsumer.Lighting))
        {
            _darknessSeconds = 0f;
            return;
        }
        _darknessSeconds += delta;

        if (_powerLossMurderTriggeredThisShift
            || _killsToday >= dcfg.MurderMaxPerDay)
            return;
        if (_darknessSeconds < dcfg.BlackoutMurderAfterSeconds) return;

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

        // 오늘의 운영 규칙이 방해공작의 시작 시각과 횟수를 정한다.
        // DAY1 은 근무 후반에 한 번만 — 배우는 날을 사건으로 뒤덮지 않는다.
        var opsToday = OpsProfile.Today;

        // 손대기 전에 실제로 그 방까지 걸어가야 한다. 이동과 체류가 여기서 일어나고,
        // 그 기록이 나중에 플레이어가 되짚을 전조가 된다(SaboteurPlan).
        _saboteurPlan.Tick(delta, this, saboteur, opsToday);

        if (opsToday != null)
        {
            if (GameState.Instance.DayTimeSeconds < opsToday.SaboteurStartSeconds) return;
            if (opsToday.MaxSabotageActionsPerDay > 0
                && _sabotageActionsToday >= opsToday.MaxSabotageActionsPerDay) return;
        }

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

        // 경비실 인원이 많을수록 방해공작이 어려워진다.
        // 인원수별 배율은 data/ops/*.tres 의 RoomOpsDef.SabotageChance 에 있다.
        float surveillanceMult = RoomStaffing.SabotageChanceMultiplier();

        // ── ⑥ 조건부 살인 : 단둘 + 미감시 + 제3자 없음 → 8초간 범행 시도 ──────────
        // 조건이 유지되는 동안에만 타이머가 흐르고, 하나라도 깨지면 즉시 중단된다.
        if (TickKillAttempt(saboteur, others, unwatched, delta)) return;

        if (!unwatched) return;   // 감시 중이면 아래 방해공작은 시도하지 않는다
        // 플레이어가 배치해 준 자리에서 충분히 준비했고, 목표 구간에 들어섰을 때만.
        // 여기서 다시 주사위를 굴리지 않는다 — DAY1 의 핵심 사건이 운으로 사라지면 안 된다.
        // 경비실 인원의 억제력은 "준비 시간이 길어진다"로 이미 반영되어 있다(SaboteurPlan).
        if (!_saboteurPlan.ReadyToAct(this, saboteur, opsToday)) return;
        var cfg = Config.Instance.Data;
        if (opsToday == null && _rng.NextDouble() >= cfg.SaboteurSabotageChance * surveillanceMult) return;

        string here = saboteur.CurrentRoomId;

        // ── ③ 전력 조작 : 발전실에 있을 때 ────────────────────────────────
        if (here == PowerRoomId && !GameState.Instance.IsPowerAccidentActive())
        {
            GameState.Instance.TriggerPowerAccident(cfg.SabotagePowerLoss);
            LogSabotage(saboteur, here, others, $"전력 계통 이상 — 최대 전력 -{cfg.SabotagePowerLoss} (원인 불명)",
                "⚠ 발전 계통에서 비정상적인 손상이 감지되었습니다!");
            return;
        }

        // ── ⑤ 자재 폐기 : 정비실 / 저장고에 있을 때 ───────────────────────
        if (here is MaintenanceRoomId or StorageRoomId && GameState.Instance.Materials > 0)
        {
            GameState.Instance.AddMaterials(-cfg.SabotageMaterialLoss);
            LogSabotage(saboteur, here, others, $"자재 {cfg.SabotageMaterialLoss}개 분실 (기록 없음)",
                "⚠ 시설 자재가 기록 없이 사라졌습니다!");
            return;
        }

        // ── ①② 복구 작업 방해 : 코어 복구율 감소. 혼란 상태면 더 크게 깎는다 ──
        if (here == CoreRoomId || blackoutChaos)
        {
            float loss = blackoutChaos ? cfg.SabotageCoreLossBlackout : cfg.SabotageCoreLoss;
            // 사유를 둘로 나눈다 — 평소의 방해와 "관리자가 조명·CCTV 를 꺼 둔 틈" 은
            // 원인도 대책도 다르다. 화면에 뜨는 문구는 그대로다(로그는 LogSabotage 가 쓴다).
            GameState.Instance.AddCoreProgress(-loss, blackoutChaos ? "정전 혼란" : "복구 작업 방해");
            LogSabotage(saboteur, here, others, $"봉쇄 코어 복구율 -{loss:0}% (원인 불명)",
                "⚠ 봉쇄 코어 복구율이 비정상적으로 감소했습니다!");
            // 깎인 양을 게이지 아래에 잠깐 띄운다 — "내가 쌓은 게 줄었다"가 보여야 한다.
            NSP.Ui.FacilityAlertHud.Instance?.ShowCoreLoss(loss);
            return;
        }

        // ── ④ CCTV 방해 : 그 작업실 CCTV 를 일정 시간 차단한다 ─────────────
        var hereState = _roomStates.GetValueOrDefault(here);
        if (hereState != null)
        {
            hereState.CctvBlockedUntil = GameState.Instance.DayTimeSeconds + cfg.SabotageCctvBlockSeconds;
            LogSabotage(saboteur, here, others,
                $"CCTV 신호 교란 — {cfg.SabotageCctvBlockSeconds:0}초간 화면 없음",
                "⚠ 감시 계통이 인위적으로 차단되었습니다!");
        }

        // 배치된 직원은 관리자의 재배치 또는 실제 시설 문제(격리/대피) 없이는
        // 자기 담당 작업실을 떠나지 않는다. 파괴공작자는 현재 작업실에서만
        // 방해·위장 행동을 하며, 무작위 방 이동으로 근무표를 깨지 않는다.
    }

    // 방해공작 흔적. 실행자 id 는 남기되 로그 문구에는 이름을 쓰지 않는다 —
    // 플레이어는 "무슨 일이 있었는지"만 보고, 누구인지는 로그/CCTV/진술 교차로 좁혀야 한다.
    private void LogSabotage(EmployeeState actor, string roomId, List<EmployeeState> witnesses, string what,
        string alert = "")
    {
        _sabotageActionsToday++;
        _saboteurPlan.OnActed(this, actor, OpsProfile.Today, roomId);
        // 오늘 몫이 남아 있으면 다음 차례를 연다 — 준비 시간을 처음부터 다시 채워야 하므로
        // 연달아 터지지 않고, 그 사이에 전조가 다시 나온다.
        int budget = OpsProfile.Today?.MaxSabotageActionsPerDay ?? 1;
        if (budget <= 0 || _sabotageActionsToday < budget) _saboteurPlan.ArmNextAction();

        // ① 눈치챈 사람 — 같은 방에서 이상을 알아차린 정도. 이름은 모른다.
        //    (설비가 이상하다 / 누가 뭘 건드린 것 같다 까지만 안다.)
        var noticed = witnesses
            .Where(w => EmployeeTraits.Get(w.EmployeeId).ObservationalAwareness
                        >= EmployeeTraits.AwarenessForWitness)
            .Select(w => w.EmployeeId)
            .ToList();

        // ② 사람을 특정한 사람 — 훨씬 어렵다. 관찰력이 최상이고, 준비 단계의 이상 행동까지
        //    이미 눈으로 본 사람만 "저 사람이 했다" 고 말할 수 있다. 조건을 못 채우면
        //    그 사고를 알고는 있어도 범인은 모른다 — CCTV·로그·다른 증언과 맞춰야 한다.
        //    조건 셋을 모두 채워야 한다.
        //      · 관찰력이 최상이고
        //      · 준비 단계의 이상 행동까지 이미 눈으로 봤고
        //      · 그 순간 자기 업무에 매여 있지 않았다
        //    마지막 조건이 핵심이다. 설비를 돌리느라 손이 바쁜 사람은 옆 사람이 무엇을
        //    했는지까지 보지 못한다 — 그래서 "그 방에 있었다"만으로는 범인이 특정되지 않는다.
        var identified = noticed
            .Where(id => EmployeeTraits.Get(id).ObservationalAwareness >= IdentifyAwareness)
            .Where(id => SawPrecursorOf(actor.EmployeeId, id))
            .Where(_ => !IsRoomWorkProgressing(roomId))
            //      · 그리고 하필 그 순간 그쪽을 보고 있었다
            //        (조건을 다 갖춰도 늘 보이지는 않는다 — 특정은 어디까지나 운까지 겹쳐야 한다)
            .Where(_ => _rng.NextDouble() < IdentifyChance)
            .ToList();

        // 로그의 실행자(actor)는 시스템 진실이라 그대로 남지만, 목격자 목록에는
        // "사람을 특정한 사람" 만 들어간다. 대사·전화·심문은 이 목록만 본다.
        EventLog.Instance?.LogEvent(LogEventType.Sabotage, actor.EmployeeId, roomId,
            $"⚠ {RoomName(roomId)} — {what}", identified);

        // 눈치만 챈 사람들은 "이상한 일이 있었다" 까지만 기억한다(실행자 없음).
        var onlyNoticed = noticed.Where(id => !identified.Contains(id)).ToList();
        if (onlyNoticed.Count > 0)
            EventLog.Instance?.LogEvent(LogEventType.Neglect, "", roomId,
                $"{RoomName(roomId)} — 설비 쪽에서 이상한 조작 흔적이 보였다", onlyNoticed);

        // 로그만 남기면 다른 화면을 보고 있을 때 그냥 지나간다 — 화면 전체로 알린다.
        // 연출은 HUD 가 알아서 돌고, 시뮬레이션은 여기서 멈추지 않는다.
        NSP.Ui.FacilityAlertHud.Instance?.ShowCriticalAlert(
            string.IsNullOrEmpty(alert) ? "⚠ 시설 설비에서 인위적인 손상 흔적이 감지되었습니다!" : alert);
    }

    // ⑥ 조건부 살인. 조건이 계속 유지되는 동안 KillAttemptSeconds 만큼 쌓여야 성공한다.
    private string _killAttemptVictimId = "";
    private float _killAttemptTimer;
    private bool TickKillAttempt(EmployeeState saboteur, List<EmployeeState> others, bool unwatched, float delta)
    {
        var cfg = Config.Instance.Data;

        bool alone = others.Count == 1;                       // 단둘 (제3자 없음)
        bool allowed = _killsToday < cfg.MurderMaxPerDay
                       && GameState.Instance.TotalKills < cfg.MurderMaxTotal
                       // 오늘의 운영 규칙이 살인을 허용하는가(DAY1 은 꺼 둔다).
                       && (OpsProfile.Today?.AllowMurder ?? true);

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
        // 조명이 오래 꺼져 있으면 훨씬 빨리 끝낸다 — 아무도 보고 있지 않다는 것을 안다.
        float needed = IsDarknessCritical ? cfg.BlackoutKillAttemptSeconds : cfg.KillAttemptSeconds;
        if (_killAttemptTimer < needed) return false;

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
        // 누가 죽었는지는 알리지 않는다 — 발견 경위는 기존 로그가 맡는다.
        NSP.Ui.FacilityAlertHud.Instance?.Notify(
            "■ 직원 한 명의 생체 신호가 소실되었습니다.", NSP.Ui.NoticeLevel.Critical);

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
        SpreadPanic(victimId, roomId);
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
                // 정전 중에 벌어진 죽음도 **발견된 이 순간**부터 퍼진다.
                // 아무도 몰랐던 동안에는 아무도 무너지지 않는다.
                SpreadPanic(emp.EmployeeId, emp.CurrentRoomId);
            }
        }
        _cctvWasOperational = poweredNow;
    }

    // 환기실 상태에 따른 전 직원 스트레스.
    //   · 정상 근무 중        → 증가 없음
    //   · 근무자 없음         → VentUnstaffedStressIntervalSeconds 마다 +1
    //   · 환기 필터 고장(사고) → VentFaultStressIntervalSeconds 마다 +2 (고장이 우선)
    private float _ventStressTimer;
    // 야간 근무는 그 자체로 사람을 갉는다. 배치된 직원 전원이 조금씩 오르고,
    // 자기 작업실에 사고가 열려 있으면 그 위에 더 붙는다.
    // (수치는 전부 config — 여기서는 "언제 누구에게" 만 정한다.)
    private float _shiftStressTimer, _incidentStressTimer;

    private void TickShiftStress(float delta)
    {
        if (!DayFeatures.StressEnabled) { _shiftStressTimer = _incidentStressTimer = 0f; return; }
        var cfg = Config.Instance.Data;

        if (cfg.ShiftStressIntervalSeconds > 0f)
        {
            _shiftStressTimer += delta;
            while (_shiftStressTimer >= cfg.ShiftStressIntervalSeconds)
            {
                _shiftStressTimer -= cfg.ShiftStressIntervalSeconds;
                foreach (var emp in _employeeStates.Values)
                {
                    // 근무 중인 사람만. 격리·기절은 따로 회복/처리 경로가 있다.
                    if (!emp.Alive || emp.Isolated || emp.Incapacitated) continue;
                    if (string.IsNullOrEmpty(emp.AssignedRoomId)) continue;
                    AddStress(emp.EmployeeId, cfg.ShiftStressAmount);
                }
            }
        }

        if (cfg.IncidentStressIntervalSeconds <= 0f) return;
        _incidentStressTimer += delta;
        while (_incidentStressTimer >= cfg.IncidentStressIntervalSeconds)
        {
            _incidentStressTimer -= cfg.IncidentStressIntervalSeconds;
            foreach (var emp in _employeeStates.Values)
            {
                if (!emp.Alive || emp.Isolated || emp.Incapacitated) continue;
                if (string.IsNullOrEmpty(emp.CurrentRoomId) || !HasActiveRepair(emp.CurrentRoomId)) continue;
                AddStress(emp.EmployeeId, cfg.IncidentStressAmount, "사고 현장");
            }
        }
    }

    private void TickVentilationFault(float delta)
    {
        var cfg = Config.Instance.Data;
        // 스트레스 또는 환기실이 잠긴 날에는 이 계통 전체가 돌지 않는다.
        if (!DayFeatures.StressEnabled || !IsRoomActive(VentRoomId)) { _ventStressTimer = 0f; return; }
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
    // 그 방에서 지금 실제로 근무 중인 사람(살아 있고 격리·기절이 아닌) — OnDutyCount 와 같은 기준.
    public List<string> OnDutyEmployeeIds(string roomId)
    {
        var list = new List<string>();
        var room = _roomStates.GetValueOrDefault(roomId);
        if (room == null) return list;
        foreach (var id in room.OccupantEmployeeIds)
        {
            var e = _employeeStates.GetValueOrDefault(id);
            if (e is { Alive: true, Isolated: false, Incapacitated: false }) list.Add(id);
        }
        return list;
    }

    // 불편(Uneasy) 관계 동실 근무의 소프트 페널티 중 "긴장"과 "말다툼".
    // 업무 속도 쪽은 RoomStaffing.Efficiency 가 맡는다. 수치는 전부 config.tres 의 Uneasy* 값.
    //  · 긴장: UneasyStressIntervalSeconds 마다 두 사람 스트레스 +UneasyStressAmount
    //  · 말다툼: UneasyArgumentCheckSeconds 마다 UneasyArgumentChance 로 언쟁 → 로그 + 두 사람 스트레스
    // 불편 쌍이 흩어지면 그 방의 타이머는 처음부터 다시 잰다.
    private readonly Dictionary<string, float> _tensionStressTimers = new();
    private readonly Dictionary<string, float> _argumentTimers = new();
    private void TickRoomTension(float delta)
    {
        var cfg = Config.Instance.Data;
        foreach (var roomId in _roomStates.Keys)
        {
            var pairs = RoomStaffing.TensePairs(roomId);
            if (pairs.Count == 0)
            {
                _tensionStressTimers.Remove(roomId);
                _argumentTimers.Remove(roomId);
                continue;
            }

            if (cfg.UneasyStressIntervalSeconds > 0f && cfg.UneasyStressAmount > 0f)
            {
                float t = _tensionStressTimers.GetValueOrDefault(roomId) + delta;
                while (t >= cfg.UneasyStressIntervalSeconds)
                {
                    t -= cfg.UneasyStressIntervalSeconds;
                    foreach (var (a, b) in pairs)
                    {
                        AddStress(a, cfg.UneasyStressAmount);
                        AddStress(b, cfg.UneasyStressAmount);
                    }
                }
                _tensionStressTimers[roomId] = t;
            }

            if (cfg.UneasyArgumentCheckSeconds > 0f && cfg.UneasyArgumentChance > 0f)
            {
                float t = _argumentTimers.GetValueOrDefault(roomId) + delta;
                while (t >= cfg.UneasyArgumentCheckSeconds)
                {
                    t -= cfg.UneasyArgumentCheckSeconds;
                    if (_rng.NextDouble() >= cfg.UneasyArgumentChance) continue;
                    var (a, b) = pairs[_rng.Next(pairs.Count)];
                    LogArgument(roomId, a, b);
                }
                _argumentTimers[roomId] = t;
            }
        }
    }

    // 언쟁 한 번 — 시설 로그(경고) + 두 사람 스트레스.
    public void LogArgument(string roomId, string a, string b)
    {
        var cfg = Config.Instance.Data;
        string roomName = _roomDefs.GetValueOrDefault(roomId)?.DisplayName ?? roomId;
        EventLog.Instance?.LogEvent(LogEventType.Argument, a, roomId,
            $"{roomName} — {Codename(a)} · {Codename(b)} 언쟁", new[] { b });
        if (cfg.UneasyArgumentStress > 0f)
        {
            AddStress(a, cfg.UneasyArgumentStress);
            AddStress(b, cfg.UneasyArgumentStress);
        }
    }

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
            GetRoomState(room)?.OccupantEmployeeIds, HasSuspiciousAction(room));
    }

    // 경비실에 인원이 충분하면 주기적으로 순찰 기록이 남는다.
    // "그 시각 그 방에 누가 있었다"는 사실만 남기고 범인은 절대 지목하지 않는다 —
    // 휴게시간 조사 자료의 '질'을 올리는 장치다(기록이 많을수록 모순을 찾기 쉬워진다).
    private void TickGuardPatrol(float delta)
    {
        if (!DayFeatures.AutoIncidentsEnabled) return;
        foreach (var ops in OpsProfile.AllRooms())
        {
            float period = OpsProfile.Curve(ops.PatrolIntervalSeconds, OnDutyCount(ops.RoomId), 0f);
            if (period <= 0f) { _patrolTimers[ops.RoomId] = 0f; continue; }
            // 그 방이 고장 났거나 CCTV 계통이 죽어 있으면 기록이 남지 않는다.
            if (HasActiveRepair(ops.RoomId) || !GameState.Instance.IsCctvOperational()) continue;

            float t = _patrolTimers.GetValueOrDefault(ops.RoomId, 0f) + delta;
            if (t < period) { _patrolTimers[ops.RoomId] = t; continue; }
            _patrolTimers[ops.RoomId] = 0f;
            RecordPatrolSweep(ops.RoomId);
        }
    }

    // 경비실에 사람이 있으면, 자기 배치실이 아닌 방에 오래 머무는 직원은 기록에 남는다.
    //
    // 결번자 전용 장치가 아니다 — 사고를 보러 간 직원도, 대응하러 간 직원도 똑같이 남는다.
    // 그래서 이 기록만으로는 범인을 알 수 없고, 다만 "그 시각 그 방에 누가 있었나"가
    // 휴게시간에 대조할 수 있는 형태로 남는다. 경비실을 비우면 이 기록도 없다.
    private const float OffPostRecordSeconds = 12f;

    private void TickOffPostRecords(float delta)
    {
        if (!DayFeatures.AutoIncidentsEnabled) return;
        if (OnDutyCount(GuardRoomId) <= 0 || !GameState.Instance.IsCctvOperational()) return;

        foreach (var emp in _employeeStates.Values)
        {
            if (!emp.Alive || emp.Isolated || emp.IsMoving) continue;
            if (string.IsNullOrEmpty(emp.AssignedRoomId)) continue;
            if (emp.CurrentRoomId == emp.AssignedRoomId || emp.CurrentRoomId == GuardRoomId
                || IsRoomCctvBlocked(emp.CurrentRoomId))
            {
                _offPostTimers[emp.EmployeeId] = 0f;
                continue;
            }

            float t = _offPostTimers.GetValueOrDefault(emp.EmployeeId, 0f) + delta;
            _offPostTimers[emp.EmployeeId] = t;
            string stamp = emp.EmployeeId + "|" + emp.CurrentRoomId;
            if (t < OffPostRecordSeconds || _offPostLogged.Contains(stamp)) continue;
            _offPostLogged.Add(stamp);
            RecordRoomOccupancy(emp.CurrentRoomId);
        }
    }

    // 그 시각 그 방의 인원을 조사 자료로 남긴다(경비 순찰 기록과 같은 형식).
    private void RecordRoomOccupancy(string roomId)
    {
        var occupants = GetRoomState(roomId)?.OccupantEmployeeIds ?? new List<string>();
        if (occupants.Count == 0) return;
        NSP.Dialogue.PlayerKnownEvidence.RecordCctvObservation(roomId, GameState.Instance.DayTimeSeconds,
            occupants, HasSuspiciousAction(roomId));
        EventLog.Instance?.LogEvent(LogEventType.TaskComplete, "", GuardRoomId,
            $"✓ 경비 순찰 기록 — {RoomName(roomId)} : {string.Join(", ", occupants.Select(Codename))}");
    }

    // 사람이 있는 작업실 하나를 골라 그 시각의 인원을 기록으로 남긴다.
    private void RecordPatrolSweep(string guardRoomId)
    {
        var rooms = _roomStates.Keys
            .Where(id => id != guardRoomId && !IsRoomCctvBlocked(id) && OnDutyCount(id) > 0)
            .ToList();
        if (rooms.Count == 0) return;

        // 무작위로 고르면 같은 방만 반복해서 찍히고 어떤 방은 근무 내내 기록이 없다.
        // 오래 안 본 방부터 돈다 — 순찰이라면 그게 당연하고, 추리 자료도 고르게 쌓인다.
        rooms.Sort((a, b) => _patrolSeenAt.GetValueOrDefault(a, -1f)
            .CompareTo(_patrolSeenAt.GetValueOrDefault(b, -1f)));
        string pick = rooms[0];
        _patrolSeenAt[pick] = GameState.Instance.DayTimeSeconds;
        var occupants = GetRoomState(pick)?.OccupantEmployeeIds ?? new List<string>();
        NSP.Dialogue.PlayerKnownEvidence.RecordCctvObservation(pick, GameState.Instance.DayTimeSeconds,
            occupants, HasSuspiciousAction(pick));
        // 누가 있었는지까지 적어야 나중에 심문에서 근거가 된다.
        string who = string.Join(", ", occupants.Select(Codename));
        EventLog.Instance?.LogEvent(LogEventType.TaskComplete, "", guardRoomId,
            $"✓ 경비 순찰 기록 — {RoomName(pick)} : {who}");
    }

    // DAY0 교육에서 GUIDE-0 가 정해진 시점에 일으키는 사고. 일반 사고와 완전히 같은 경로를
    // 지나므로(수리 업무 · 로그 · 경고 단말기) 플레이어가 배우는 내용이 본편과 동일하다.
    // minWorkers > 0 이면 그 사고의 수리 최소 인원을 그 값으로 덮어쓴다. 교육에서는
    // "지금 그 방에 있는 인원 + 1" 로 잡아, 반드시 한 명을 더 보내야 수리되게 만든다.
    public bool TriggerTutorialAccident(string roomId, int minWorkers = 0)
    {
        var def = _roomDefs.GetValueOrDefault(roomId);
        if (def == null || def.AccidentConsequence == RoomAccidentNone) return false;
        if (HasActiveRepair(roomId)) return false;
        TriggerRoomAccident(roomId, def, minWorkers);
        return true;
    }

    // 그 방에 아직 수리해야 할 사고가 남아 있는가(튜토리얼 진행 판정에도 쓴다).
    public bool HasRepairPending(string roomId) => HasActiveRepair(roomId);

    // ── 방해공작 전조 ────────────────────────────────────────────────
    //
    // 셋 다 "누가 무엇을 하려 한다"를 말하지 않는다. 시설이 아주 조금 이상해지고,
    // 그 방을 보고 있던 사람만 뭔가를 본다. 판단은 플레이어가 한다.

    // ① 행동 — 설비 쪽으로 다가간다. CCTV 로 그 방을 보고 있으면 화면에 잡힌다.
    public void MarkSuspiciousAction(string roomId, string employeeId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        if (room == null) return;
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        room.SuspiciousActionUntil = now + SuspiciousActionSeconds;
        room.SuspiciousActorId = employeeId;
        // 마침 그 방을 보고 있었다면 관리자가 직접 본 것이 된다.
        if (IsRoomUnderActiveCctv(roomId) && !IsRoomCctvBlocked(roomId))
            NSP.Dialogue.PlayerKnownEvidence.RecordCctvObservation(roomId, now,
                room.OccupantEmployeeIds, true);
    }

    private const float SuspiciousActionSeconds = 4f;
    // 사람을 특정하려면 이 정도 관찰력이 필요하다(전조까지 이미 본 경우에만).
    private const int IdentifyAwareness = 3;
    private const double IdentifyChance = 0.35;

    // 그 방의 업무가 지금 실제로 돌아가고 있는가(수리 제외).
    private bool IsRoomWorkProgressing(string roomId) =>
        _activeTasks.Any(t => t.RoomId == roomId && t.Status == SpawnedTaskStatus.Active
                              && !t.IsRepair && t.Progressing);

    // 이 사람이 그 실행자의 준비 단계 이상 행동을 실제로 봤는가.
    // RecordOddBehaviour 가 남긴 기록이 곧 "계속 지켜보고 있었다"는 증거다.
    private bool SawPrecursorOf(string actorId, string watcherId) =>
        EventLog.Instance?.GetAllEntries().Any(e =>
            e.Day == (GameState.Instance?.CurrentDay ?? 1)
            && e.EventType == LogEventType.Neglect
            && e.ActorEmployeeId == actorId
            && e.WitnessEmployeeIds.Contains(watcherId)) ?? false;

    // 지금 이 방에서 누군가 설비 쪽에 붙어 있는가(CCTV 화면 표시용).
    public bool HasSuspiciousAction(string roomId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        return room != null && room.SuspiciousActionUntil > (GameState.Instance?.DayTimeSeconds ?? 0f);
    }

    // ② 설비 — 수치가 잠깐 흔들린다. 고장이 아니고 로그에도 남지 않는다.
    public void TriggerMicroFault(string roomId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        if (room == null) return;
        room.MicroFaultUntil = (GameState.Instance?.DayTimeSeconds ?? 0f) + MicroFaultSeconds;
        // 경비실이 돌아가고 있으면 센서 이상을 보고 그 방을 한 번 확인한다.
        // "누가 있었는지"만 기록에 남는다 — 무슨 일이 있었는지는 알려 주지 않는다.
        if (OnDutyCount(GuardRoomId) > 0 && GameState.Instance.IsCctvOperational()
            && !IsRoomCctvBlocked(roomId))
            RecordRoomOccupancy(roomId);
    }

    private const float MicroFaultSeconds = 2.5f;

    public bool HasMicroFault(string roomId)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        return room != null && room.MicroFaultUntil > (GameState.Instance?.DayTimeSeconds ?? 0f);
    }

    // ③ 목격 — 같은 방의 관찰력 있는 직원이 기억한다. 화면 로그에는 뜨지 않고
    //    휴게시간 증언으로만 나온다(Facility Log 를 미세 행동으로 더럽히지 않는다).
    public List<string> RecordOddBehaviour(string actorId, string roomId, string what)
    {
        var room = _roomStates.GetValueOrDefault(roomId);
        var seen = new List<string>();
        if (room == null) return seen;

        foreach (string id in room.OccupantEmployeeIds)
        {
            if (id == actorId) continue;
            var st = _employeeStates.GetValueOrDefault(id);
            if (st is not { Alive: true, Isolated: false, Incapacitated: false }) continue;
            if (EmployeeTraits.Get(id).ObservationalAwareness < EmployeeTraits.AwarenessForWitness) continue;
            seen.Add(id);
        }
        if (seen.Count == 0) return seen;

        EventLog.Instance?.LogEvent(LogEventType.Neglect, actorId, roomId,
            $"{Codename(actorId)} — {what}", seen);
        return seen;
    }

    // 화면에 보여줄 코어 복구율. 확정된 값에 "지금 차오르는 중인 몫"을 더한다.
    //
    // 내부 판정(목표 달성 등)은 그대로 GameState.CoreProgress 를 쓴다. 화면만 연속적으로
    // 움직인다 — 7초에 한 번 1%씩 튀면 운영이 멈춘 것처럼 보이기 때문이다.
    public float CoreProgressPreview()
    {
        float committed = GameState.Instance?.CoreProgress ?? 0f;
        foreach (var st in _activeTasks)
        {
            if (st.Status != SpawnedTaskStatus.Active || st.IsRepair) continue;
            var def = _taskDefs.GetValueOrDefault(st.TaskId);
            if (def == null || def.EffectType != TaskEffectType.AddCoreProgress) continue;
            if (st.GaugeRequired <= 0f) continue;
            committed += Mathf.Clamp(st.Gauge / st.GaugeRequired, 0f, 1f) * def.EffectAmount;
        }
        return committed;
    }

    // 이 방에 지금 몇 명이 있어서 무엇이 달라지는가 — 한 줄 요약(미니맵 표시용).
    // 재배치 직후 바로 바뀌어야 "내 선택이 시설을 바꿨다"가 읽힌다.
    public string StaffingEffectLine(string roomId)
    {
        var ops = OpsProfile.Room(roomId);
        if (ops == null) return "";
        int n = OnDutyCount(roomId);
        float eff = OpsProfile.Curve(ops.Efficiency, n, 0f);
        if (roomId == PowerRoomId)
        {
            float output = HasRepairPending(roomId)
                ? ops.OutputWhileBroken
                : OpsProfile.Curve(ops.FacilityOutput, n);
            return $"출력 {output * 100f:0}%  {(output >= 0.99f ? "안정" : n == 0 ? "정지" : "불안정")}";
        }
        if (n == 0) return "정지";
        return $"효율 {eff * 100f:0}%";
    }

    // 지금 모두가 달려들어 수습하고 있는 중인가.
    //
    // "고장이 하나 열려 있다"가 아니라 "실제로 수리가 돌아가고 있다"를 본다.
    // 아무도 손대지 않은 채 방치된 고장이 결번자를 근무 내내 숨겨 주면,
    // 플레이어가 대응을 포기할수록 사건이 안 일어나는 이상한 게임이 된다.
    public bool HasSeriousIncidentActive() =>
        _activeTasks.Any(t => t.IsRepair && t.Status == SpawnedTaskStatus.Active && t.Progressing);

    // 개발용 사후 확인(§19). 플레이어에게는 어떤 화면으로도 보여주지 않는다.
    public void PrintSaboteurDebug()
    {
        if (!OS.IsDebugBuild()) return;
        GD.Print(_saboteurPlan.DebugSummary(this));
    }

    private void TickUnstaffedAccidents(float delta)
    {
        // DAY0 는 교육용이라 시뮬레이션이 스스로 사고를 내지 않는다 — 튜토리얼이 직접 낸다.
        if (!DayFeatures.AutoIncidentsEnabled) return;
        var cfg = Config.Instance.Data;
        foreach (var (roomId, room) in _roomStates)
        {
            var def = _roomDefs.GetValueOrDefault(roomId);
            if (def == null || def.IsRestricted || def.AccidentConsequence == RoomAccidentNone) continue;
            // 오늘 잠긴 작업실은 아무도 배치할 수 없다 — 무인 방치 사고도 나지 않는다.
            if (!DayFeatures.IsRoomActive(def)) { room.UnstaffedTimer = 0f; continue; }

            // 이미 그 방에 사고 수리 업무가 걸려 있으면 타이머를 멈춘다(중복 발생 방지).
            if (HasActiveRepair(roomId)) { room.UnstaffedTimer = 0f; continue; }

            if (OnDutyCount(roomId) > 0) { room.UnstaffedTimer = 0f; continue; }

            // 오늘 이 방은 비워 둬도 되는가. 0 이하면 무인 사고가 나지 않는다
            // (저장고·경비실이 그렇다 — 대신 다른 방식으로 손해를 본다).
            float limit = RoomStaffing.UnstaffedAccidentSeconds(roomId, def);
            if (limit <= 0f) { room.UnstaffedTimer = 0f; continue; }

            room.UnstaffedTimer += delta;
            if (room.UnstaffedTimer < limit) continue;

            // DAY1 은 사고가 겹치지 않게 잠시 미룬다. 타이머는 유지되므로
            // 조건이 풀리는 즉시 발생한다(원인은 그대로 "근무자 부재").
            if (!CanStartNewIncident()) { room.UnstaffedTimer = limit; continue; }

            room.UnstaffedTimer = 0f;
            TriggerRoomAccident(roomId, def);
        }
    }

    // 초반 단순화: 대형 작업실 사고를 한 번에 하나로 제한한다.
    // DAY1 전용이었지만 DAY5 까지 같은 규칙을 유지한다(Config.IncidentLimitLastDay).
    private bool CanStartNewIncident()
    {
        var cfg = Config.Instance.Data;
        if ((GameState.Instance?.CurrentDay ?? 1) > cfg.IncidentLimitLastDay) return true;
        if (IncidentTracker.ActiveCount >= Mathf.Max(1, cfg.Day1MaxActiveIncidents)) return false;
        return GameState.Instance.DayTimeSeconds - IncidentTracker.LastIncidentAt >= cfg.IncidentGapSeconds;
    }

    private const TabooConsequenceType RoomAccidentNone = (TabooConsequenceType)(-1);

    private bool HasActiveRepair(string roomId) =>
        _activeTasks.Any(t => t.RoomId == roomId && t.IsRepair && t.Status == SpawnedTaskStatus.Active);

    // 경고에 제때 대응하지 못했다 → 실제 고장. 무인 방치 사고와 완전히 같은 경로를 쓰므로
    // 수리 업무 · 시설 로그 · 경고 단말기 · 미니맵이 지금까지와 똑같이 동작한다.
    public void TriggerWarningFailure(string roomId, string title)
    {
        var def = _roomDefs.GetValueOrDefault(roomId);
        if (def == null || def.AccidentConsequence == RoomAccidentNone) return;
        if (HasActiveRepair(roomId)) return;

        EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
            $"🚨 {RoomName(roomId)} — {title} 대응 실패, 고장 발생");
        NSP.Ui.FacilityAlertHud.Instance?.Notify(
            $"⚠ {RoomName(roomId)} 기능이 정지되었습니다.", NSP.Ui.NoticeLevel.Warning);
        IncidentTracker.Open(roomId, def.AccidentName, "경고 시간 내 대응 실패",
            "설비 수리 필요", RoomStaffing.RepairMinWorkers(roomId, def));
        TabooRuleSystem.Instance?.ApplyRoomConsequence(def.AccidentConsequence, roomId, def.AccidentAmount);
        AddRepairTask(roomId, def, 0);
        _behavior.OnIncident(this, roomId, OpsProfile.Today);
    }

    // 사고 발생 — 결과를 적용하고, 그 방에 수리 업무를 띄운다.
    private void TriggerRoomAccident(string roomId, RoomDef def, int minWorkersOverride = 0)
    {
        EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
            $"🚨 {RoomName(roomId)} — {def.AccidentName} (무인 방치)");
        NSP.Ui.FacilityAlertHud.Instance?.Notify(
            $"⚠ {RoomName(roomId)}에 {def.AccidentName} 사고가 발생했습니다.", NSP.Ui.NoticeLevel.Warning);
        // 먼저 사고를 열어 둔다 — 뒤이어 적용되는 시설 손실이 이 사고의 결과로 묶인다.
        IncidentTracker.Open(roomId, def.AccidentName, "장시간 근무자 부재",
            "설비 수리 필요", RoomStaffing.RepairMinWorkers(roomId, def));
        TabooRuleSystem.Instance?.ApplyRoomConsequence(def.AccidentConsequence, roomId, def.AccidentAmount);
        AddRepairTask(roomId, def, minWorkersOverride);
        _behavior.OnIncident(this, roomId, OpsProfile.Today);
    }

    // 이상 개체를 끝내 찾지 못했다 — 그 작업실 설비가 부서진다.
    //
    // 수리 · 시설 손실 · 미니맵 표시는 무인 방치 사고와 **같은 경로**를 쓴다(고장은 고장이다).
    // 다른 것은 로그 종류와 원인뿐이다 — 관리자가 나중에 "사람 탓이었나 그것 탓이었나"를
    // 구분할 수 있어야 하기 때문이다.
    public void TriggerGhostAccident(string roomId)
    {
        var def = _roomDefs.GetValueOrDefault(roomId);
        if (def == null || def.AccidentConsequence == RoomAccidentNone) return;
        if (HasActiveRepair(roomId)) return;

        EventLog.Instance?.LogEvent(LogEventType.AnomalyIncident, "", roomId,
            $"🚨 {RoomName(roomId)} — {def.AccidentName} (이상 개체 접촉)");
        NSP.Ui.FacilityAlertHud.Instance?.Notify(
            $"⚠ {RoomName(roomId)}에서 원인 불명의 손상이 발생했습니다.", NSP.Ui.NoticeLevel.Critical);
        IncidentTracker.Open(roomId, def.AccidentName, "이상 개체 접촉",
            "설비 수리 필요", RoomStaffing.RepairMinWorkers(roomId, def));
        TabooRuleSystem.Instance?.ApplyRoomConsequence(def.AccidentConsequence, roomId, def.AccidentAmount);
        AddRepairTask(roomId, def, 0);
        _behavior.OnIncident(this, roomId, OpsProfile.Today);
    }

    // 그 방에 수리 업무를 띄운다. 수리가 걸려 있는 동안 그 방의 평소 업무는 멈춘다.
    private void AddRepairTask(string roomId, RoomDef def, int minWorkersOverride)
    {
        // 이미 수리가 걸려 있으면 더 만들지 않는다. 두 개가 겹치면 하나를 끝내도
        // 방이 계속 고장 상태로 남아 "사람을 넣었는데도 안 고쳐지는" 것처럼 보인다.
        if (HasActiveRepair(roomId)) return;
        _activeTasks.Add(new SpawnedTask
        {
            TaskId = def.RepairTaskId,
            RoomId = roomId,
            Recurring = false,
            IsRepair = true,
            Status = SpawnedTaskStatus.Active,
            TimeLimitSeconds = float.MaxValue,
            GaugeRequired = RoomStaffing.RepairSeconds(roomId, def),
            MinWorkersOverride = minWorkersOverride > 0
                ? minWorkersOverride
                : RoomStaffing.RepairMinWorkers(roomId, def),
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
                $"☣ {roomDef?.DisplayName ?? roomId} — 설비에 사람 손을 탄 흔적이 있다. 사고가 아니다.", witnesses);
            // 센서에는 범인을 절대 넘기지 않는다 — "누가" 가 아니라 "사고가 아니다" 까지만.
            IncidentTracker.Anomaly(roomId, "☣ 방해공작 흔적", "설비 손상 — 고의 조작 정황");
        }
        else
        {
            var st = _activeTasks.FirstOrDefault(t => t.RoomId == roomId && t.TaskId == activeTask.TaskId && t.Status == SpawnedTaskStatus.Active);
            if (st != null)
                st.Gauge = Mathf.Max(0f, st.Gauge - Config.Instance.Data.SabotageTaskGaugeLoss);
            EventLog.Instance?.LogEvent(LogEventType.Sabotage, actorEmployeeId, roomId,
                $"☣ {roomDef?.DisplayName ?? roomId} — '{activeTask.DisplayName}' 기록이 사람 손에 되돌려져 있다.", witnesses);
            // 설비가 망가지지 않은 유형이라도 흔적은 남는다 — 센서에서 확인할 수 있어야 한다.
            IncidentTracker.Anomaly(roomId, "☣ 방해공작 흔적",
                $"'{activeTask.DisplayName}' 진행 기록 조작");
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
        // 아직 갈 길이 남아 있으면 이 방은 목적지가 아니라 통로다.
        // (AdvanceToNextWaypoint 는 이 함수 뒤에 불리므로 여기서는 남은 경유지가 그대로 있다.)
        bool passing = emp.PathQueue.Count > 0;
        string previousRoom = emp.CurrentRoomId;
        // 중앙 제어실은 관리자 방이다 — 출근 출발점일 뿐이라 '퇴장' 기록은 남기지 않는다.
        if (previousRoom != emp.TargetRoomId && previousRoom == DeployOriginRoomId)
            RemoveOccupant(previousRoom, emp.EmployeeId);
        else if (previousRoom != emp.TargetRoomId)
        {
            RemoveOccupant(previousRoom, emp.EmployeeId);
            EventLog.Instance?.LogEvent(LogEventType.RoomExit, emp.EmployeeId, previousRoom, $"{Codename(emp.EmployeeId)} - {RoomName(previousRoom)} 퇴장",
                GetOtherOccupants(previousRoom, emp.EmployeeId), passing);

            LogNeglectIfRoomLeftEmptyMidTask(previousRoom, emp.EmployeeId);
        }

        emp.CurrentRoomId = emp.TargetRoomId;
        // 배치되지 않은 직원(오늘 비번)은 방에 들어가도 근무 인원으로 세지 않는다.
        if (emp.Isolated || !string.IsNullOrEmpty(emp.AssignedRoomId))
            AddOccupant(emp.CurrentRoomId, emp.EmployeeId);

        EventLog.Instance?.LogEvent(LogEventType.RoomEnter, emp.EmployeeId, emp.CurrentRoomId,
            $"{Codename(emp.EmployeeId)} - {RoomName(emp.CurrentRoomId)} {(passing ? "통과" : "입장")}",
            GetOtherOccupants(emp.CurrentRoomId, emp.EmployeeId), passing);

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

            // 그 방에 고장이 나 있으면 그 방 인원은 전부 수리에 묶인다 — 평소 업무도,
            // 제한시간도 수리가 끝날 때까지 멈춘다. 사고 하나가 "어느 방에서 사람을 뺄까"가
            // 되는 이유가 바로 이것이다.
            if (!st.IsRepair && HasActiveRepair(st.RoomId)) { st.Progressing = false; continue; }
            // 미세 이상 — 진행도가 잠깐 멈춘다. 사고가 아니라 "뭔가 걸린" 정도다.
            if (!st.IsRepair && HasMicroFault(st.RoomId)) { st.Progressing = false; st.Elapsed += delta; continue; }

            st.Elapsed += delta;
            st.Progressing = false;

            var room = _roomStates.GetValueOrDefault(st.RoomId);
            // 기절(스트레스 46+)한 직원은 방에 있어도 업무 인원으로 세지 않는다.
            // 동료의 죽음을 보고 무너진 직원(_panicked)도 마찬가지다 — 자리에는 있지만 일은 못 한다.
            var workers = room == null ? new List<EmployeeState>() : room.OccupantEmployeeIds
                .Select(id => _employeeStates.GetValueOrDefault(id))
                .Where(e => e != null && e.Alive && !e.Isolated && !e.Incapacitated
                            && !_panicked.Contains(e.EmployeeId))
                .ToList();

            bool blockedByMaterials = taskDef.EffectType == TaskEffectType.AddCoreProgress
                && GameState.Instance.Materials < RoomStaffing.CoreMaterialCost();

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
                st.Progressing = workers.Count >= minWorkers && !blockedByMaterials;
                if (st.Progressing)
                {
                    // 1초에 (기본 속도 × 인원 효율 × 인원 평균 능력 × 발전 안정도) 만큼 찬다.
                    // 머릿수를 그대로 더하지 않는 것이 핵심이다 — 세 번째 사람부터 증가폭이
                    // 줄어야 "한 명 더 넣을까, 다른 방에 둘까"가 선택이 된다.
                    // 인원수별 배율은 data/ops/*.tres 의 RoomOpsDef.Efficiency 에 있다.
                    float baseRate = Config.Instance.Data.BaseTaskWorkRate;
                    // 금기 위반 페널티(업무 속도 감소)가 걸려 있으면 여기서 같이 곱해진다.
                    float tabooPenalty = TabooRuleSystem.Instance?.WorkPenaltyMultiplier ?? 1f;
                    float crew = (float)workers.Average(w => TechWorkMultiplier(w.EmployeeId) * StressWorkRate(w));
                    // 발전이 불안정하면 시설 전체가 느려진다. 다만 수리에는 걸지 않는다 —
                    // 고장 난 방을 고치는 일까지 느려지면 회복 자체가 불가능해진다.
                    float facility = st.IsRepair ? 1f : RoomStaffing.FacilityOutput();
                    float rate = baseRate * RoomStaffing.Efficiency(st.RoomId) * crew * facility * tabooPenalty;
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
            NSP.Ui.FacilityAlertHud.Instance?.Notify(
                $"✓ {RoomName(st.RoomId)} 기능이 복구되었습니다.", NSP.Ui.NoticeLevel.Info);
            // 수리가 끝났다는 건 지도에서 눈으로 찾기 어렵다 — 소리로 알린다.
            Sfx.Instance?.Play("ding", -5f);
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
            // 수리 규격은 업무가 아니라 **그 방**이 정한다(코어실·발전실만 2명).
            var roomDef = _roomDefs.GetValueOrDefault(st.RoomId);
            int repairWorkers = RoomStaffing.RepairMinWorkers(st.RoomId, roomDef);
            IncidentTracker.Open(st.RoomId, AlertSystem.HeadlineFor(st.TaskId), "경고 시간 내 대응 실패",
                $"설비 수리 필요 (최소 {repairWorkers}명)", repairWorkers);
            TabooRuleSystem.Instance?.ApplyRoomConsequence(taskDef.NeglectConsequenceType, st.RoomId, taskDef.NeglectConsequenceAmount);

            st.IsRepair = true;
            st.Status = SpawnedTaskStatus.Active;
            st.Gauge = 0f;
            st.GaugeRequired = RoomStaffing.RepairSeconds(st.RoomId, roomDef);
            st.MinWorkersOverride = repairWorkers;
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
                // 저장고 인원이 자재 소모량을 바꾼다(비워 두면 낭비가 늘어난다).
                int consumed = RoomStaffing.CoreMaterialCost();
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
                // 발전실 사고(금기 이상현상 포함)는 전력이 정상으로 돌아온 시점에 해결된다.
                IncidentTracker.Resolve(roomId);
                badge += " · ⚡ 전력 정상 복구";
                break;
        }
        EventLog.Instance?.LogEvent(LogEventType.TaskComplete, "", roomId, badge);
    }
}
