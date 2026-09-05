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
        // ResourceDir: 내보낸 빌드의 .tres.remap 접미사까지 처리한다.
        foreach (string path in ResourceDir.ListFiles("res://data/taboos/", ".tres"))
        {
            var res = GD.Load<TabooDef>(path);
            if (res != null)
                _tabooDefs[res.TabooId] = res;
        }
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
            switch (taboo.ConditionType)
            {
                case TabooConditionType.MaxHeadcountInRoom: TickMaxHeadcount(taboo, delta); break;
                case TabooConditionType.LowCourageAloneInRoom: TickLowCourageAlone(taboo, delta); break;
                case TabooConditionType.ContinuousWorkInRoom: TickContinuousWork(taboo, delta); break;
                case TabooConditionType.TreatTwoHighStress: TickTreatTwoHighStress(taboo, delta); break;
                case TabooConditionType.EmptyStorageWhenMaterialsHigh: TickStorageLeftEmpty(taboo, delta); break;
            }
        }
        TickPenaltyTimers();
    }

    // 2) 담력이 낮은 직원이 그 방에서 혼자 근무하는 상태가 hold_seconds 이상 이어지면 위반.
    private void TickLowCourageAlone(TabooDef taboo, float delta)
    {
        string roomId = taboo.ConditionParams.GetValueOrDefault("room_id", "").AsString();
        var sim = FacilitySimulation.Instance;
        var room = sim?.GetRoomState(roomId);
        if (room == null) return;

        int maxCourage = taboo.ConditionParams.GetValueOrDefault("max_courage", 1).AsInt32();
        float hold = taboo.ConditionParams.GetValueOrDefault("hold_seconds", 8f).AsSingle();

        string only = room.OccupantEmployeeIds.Count == 1 ? room.OccupantEmployeeIds[0] : null;
        bool bad = only != null && (sim.GetEmployeeDef(only)?.Courage ?? 9) <= maxCourage;
        if (!HoldAndFire(room, taboo, bad, hold, delta)) return;

        Violate(taboo, only, roomId,
            $"{sim.GetRoomDef(roomId)?.DisplayName ?? roomId}에 담력이 낮은 직원이 혼자 {hold:0}초 이상 근무했다.");
    }

    // 6) 같은 직원이 그 방에서 hold_seconds 이상 연속 근무하면 위반.
    private void TickContinuousWork(TabooDef taboo, float delta)
    {
        string roomId = taboo.ConditionParams.GetValueOrDefault("room_id", "").AsString();
        var sim = FacilitySimulation.Instance;
        var room = sim?.GetRoomState(roomId);
        if (room == null) return;

        float hold = taboo.ConditionParams.GetValueOrDefault("hold_seconds", 60f).AsSingle();

        // 방의 인원 구성이 바뀌면 "연속" 이 끊긴 것으로 보고 타이머를 다시 센다.
        string key = taboo.TabooId + ":who";
        string now = string.Join(",", room.OccupantEmployeeIds.OrderBy(x => x));
        if (room.TabooWatchKeys.GetValueOrDefault(key, "") != now)
        {
            room.TabooWatchKeys[key] = now;
            room.TabooHoldTimers[taboo.TabooId] = 0f;
        }
        if (!HoldAndFire(room, taboo, room.OccupantEmployeeIds.Count > 0, hold, delta)) return;

        Violate(taboo, room.OccupantEmployeeIds.FirstOrDefault(), roomId,
            $"{sim.GetRoomDef(roomId)?.DisplayName ?? roomId}에서 같은 직원이 {hold:0}초 이상 연속 근무했다.");
    }

    // 7) 스트레스가 기준 이상인 직원이 의무실에 count 명 이상 함께 있으면 위반(동시 치료).
    private void TickTreatTwoHighStress(TabooDef taboo, float delta)
    {
        string roomId = taboo.ConditionParams.GetValueOrDefault("room_id", "").AsString();
        var sim = FacilitySimulation.Instance;
        var room = sim?.GetRoomState(roomId);
        if (room == null) return;

        float stressMin = taboo.ConditionParams.GetValueOrDefault("stress_min", 30f).AsSingle();
        int count = taboo.ConditionParams.GetValueOrDefault("count", 2).AsInt32();

        var targets = room.OccupantEmployeeIds
            .Where(id => (sim.GetEmployeeState(id)?.Stress ?? 0f) >= stressMin).ToList();
        if (!HoldAndFire(room, taboo, targets.Count >= count, 1f, delta)) return;

        _penaltyTargets = targets;
        Violate(taboo, targets.FirstOrDefault(), roomId,
            $"스트레스 {stressMin:0} 이상인 직원 {targets.Count}명이 동시에 치료를 받았다.");
        _penaltyTargets = null;
    }

    // 조건이 유지된 시간을 재고 임계에 닿으면 한 번만 true. 조건이 풀리면 초기화된다.
    private static bool HoldAndFire(RoomState room, TabooDef taboo, bool conditionMet, float hold, float delta)
    {
        if (!conditionMet) { room.TabooHoldTimers[taboo.TabooId] = 0f; return false; }
        float t = room.TabooHoldTimers.GetValueOrDefault(taboo.TabooId, 0f);
        if (t < 0f) return false;                      // 이번 위반에서 이미 발동함(잠금)
        t += delta;
        room.TabooHoldTimers[taboo.TabooId] = t;
        if (t < hold) return false;
        room.TabooHoldTimers[taboo.TabooId] = -1f;
        return true;
    }

    // ── 이벤트로만 알 수 있는 조건 (전화 / CCTV / 종 / 저장고) ────────────────
    // 해당 시스템이 아래 훅을 불러 주면 그 자리에서 판정한다.
    private readonly List<string> _calledEmployeeIds = new();
    private string _lastCctvRoomId = "";
    private int _cctvSwitchStreak;
    private float _bellRangAt = -999f;
    private List<string> _penaltyTargets;

    // 관리자가 어떤 직원에게 전화를 걸었다.
    public void NotifyCallPlaced(string employeeId)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(employeeId)) return;

        foreach (var taboo in GetActiveTaboos())
        {
            if (taboo.ConditionType == TabooConditionType.CallEmployeeInDarkRoom)
            {
                var st = sim.GetEmployeeState(employeeId);
                var room = st == null ? null : sim.GetRoomState(st.CurrentRoomId);
                if (room != null && (room.RedAlertLighting || !room.PowerOn))
                {
                    _penaltyTargets = new List<string> { employeeId };
                    Violate(taboo, employeeId, st.CurrentRoomId, "조명이 꺼진 곳의 직원을 호출했다.");
                    _penaltyTargets = null;
                }
            }
            else if (taboo.ConditionType == TabooConditionType.CallSameEmployeeTwice
                     && _calledEmployeeIds.Contains(employeeId))
            {
                Violate(taboo, employeeId, sim.GetEmployeeState(employeeId)?.CurrentRoomId ?? "",
                    "같은 직원과 두 번 통화했다.");
            }
        }
        _calledEmployeeIds.Add(employeeId);
    }

    // 관리자가 CCTV 감시 대상 방을 바꿨다.
    public void NotifyCctvSwitched(string roomId)
    {
        if (string.IsNullOrEmpty(roomId) || roomId == _lastCctvRoomId) return;
        _lastCctvRoomId = roomId;
        _cctvSwitchStreak++;

        var sim = FacilitySimulation.Instance;
        foreach (var taboo in GetActiveTaboos())
        {
            if (taboo.ConditionType != TabooConditionType.CctvSwitchStreakAlone) continue;
            int streak = taboo.ConditionParams.GetValueOrDefault("streak", 3).AsInt32();
            bool aloneOnGuard = (sim?.GetRoomState("guard_room")?.OccupantEmployeeIds.Count ?? 0) == 1;
            if (!aloneOnGuard || _cctvSwitchStreak < streak) continue;
            _cctvSwitchStreak = 0;
            Violate(taboo, "", "guard_room", $"경비실 근무자가 혼자인 상태로 CCTV를 {streak}회 연속 전환했다.");
        }
    }

    // 시설의 종이 규정 횟수만큼 울렸다(연출 쪽에서 호출).
    public void NotifyBellRang() => _bellRangAt = Now;

    // 직원이 이동을 시작했다 — 종이 울린 직후라면 위반.
    public void NotifyEmployeeMoved(string employeeId, string toRoomId)
    {
        foreach (var taboo in GetActiveTaboos())
        {
            if (taboo.ConditionType != TabooConditionType.MovementAfterBell) continue;
            float window = taboo.ConditionParams.GetValueOrDefault("window_seconds", 30f).AsSingle();
            if (Now - _bellRangAt > window) continue;
            _penaltyTargets = new List<string> { employeeId };
            Violate(taboo, employeeId, toRoomId, "종이 울린 뒤 정해진 시간 안에 직원이 이동했다.");
            _penaltyTargets = null;
        }
    }

    // ⑩ 자재가 기준 이상인데 저장고를 비워 둔(근무자 0명) 상태가 이어지면 위반.
    private void TickStorageLeftEmpty(TabooDef taboo, float delta)
    {
        string roomId = taboo.ConditionParams.GetValueOrDefault("room_id", "storage_room").AsString();
        var sim = FacilitySimulation.Instance;
        var room = sim?.GetRoomState(roomId);
        if (room == null) return;

        int min = taboo.ConditionParams.GetValueOrDefault("materials_min", 30).AsInt32();
        float hold = taboo.ConditionParams.GetValueOrDefault("hold_seconds", 8f).AsSingle();

        bool bad = (GameState.Instance?.Materials ?? 0) >= min && sim.OnDutyCount(roomId) == 0;
        if (!HoldAndFire(room, taboo, bad, hold, delta)) return;

        Violate(taboo, "", roomId, $"자재가 {min}개 이상인데 저장고를 {hold:0}초 이상 비워 두었다.");
    }

    // 근무가 새로 시작될 때 이벤트 기반 상태와 진행 중인 페널티를 초기화한다.
    public void ResetRuntimeState()
    {
        _calledEmployeeIds.Clear();
        _lastCctvRoomId = "";
        _cctvSwitchStreak = 0;
        _bellRangAt = -999f;
        VentHaltUntil = 0f;
        PhoneLockedUntil = 0f;
        CctvScrambledUntil = 0f;
        _workPenaltyUntil = 0f;
        _workPenaltyMultiplier = 1f;
        _trackingLostUntil.Clear();
    }

    // ── 금기 페널티의 지속 상태 (다른 시스템이 읽기만 한다) ────────────────
    // 모든 시각은 근무 시계(DayTimeSeconds) 기준이라 별도 감산이 필요 없다.
    public float VentHaltUntil { get; private set; }
    public float PhoneLockedUntil { get; private set; }
    public float CctvScrambledUntil { get; private set; }
    private float _workPenaltyUntil;
    private float _workPenaltyMultiplier = 1f;
    private readonly Dictionary<string, float> _trackingLostUntil = new();

    private static float Now => GameState.Instance?.DayTimeSeconds ?? 0f;

    public bool IsVentHalted => VentHaltUntil > Now;
    public bool IsPhoneLocked => PhoneLockedUntil > Now;
    public bool IsCctvScrambled => CctvScrambledUntil > Now;
    public bool IsTrackingLost(string employeeId) => _trackingLostUntil.GetValueOrDefault(employeeId, 0f) > Now;
    public float WorkPenaltyMultiplier => _workPenaltyUntil > Now ? _workPenaltyMultiplier : 1f;

    private void TickPenaltyTimers()
    {
        if (_trackingLostUntil.Count == 0) return;
        var expired = _trackingLostUntil.Where(kv => kv.Value <= Now).Select(kv => kv.Key).ToList();
        foreach (var k in expired) _trackingLostUntil.Remove(k);
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
        _applyingTaboo = taboo;                 // 페널티가 ConsequenceParams 를 읽을 수 있게
        ApplyRoomConsequence(taboo.ConsequenceType, roomId, stressAmount);
        _applyingTaboo = null;
    }

    // 지금 적용 중인 금기(페널티가 ConsequenceParams 를 읽기 위해 잠깐 들고 있는다).
    private TabooDef _applyingTaboo;

    private float ParamF(string key, float fallback) =>
        _applyingTaboo?.ConsequenceParams.GetValueOrDefault(key, fallback).AsSingle() ?? fallback;
    private int ParamI(string key, int fallback) =>
        _applyingTaboo?.ConsequenceParams.GetValueOrDefault(key, fallback).AsInt32() ?? fallback;

    // 페널티 대상 = 위반을 일으킨 직원들(_penaltyTargets), 없으면 그 방에 있는 직원 전원.
    private List<string> Targets(string roomId) =>
        _penaltyTargets ?? FacilitySimulation.Instance?.GetRoomState(roomId)?.OccupantEmployeeIds.ToList()
        ?? new List<string>();

    private void AddStressToTargets(string roomId, float amount, string reason)
    {
        foreach (var id in Targets(roomId))
            FacilitySimulation.Instance?.AddStress(id, amount, reason);
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
                // 담력 배율·기절 판정이 걸리도록 반드시 AddStress 창구를 지난다.
                foreach (var id in (room?.OccupantEmployeeIds ?? new List<string>()).ToList())
                    FacilitySimulation.Instance.AddStress(id, stressAmount, $"{roomName} 이상현상");
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
                int lost = Config.Instance.Data.MaintenanceFaultMaterialLoss;
                GameState.Instance.AddMaterials(-lost);
                EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
                    $"⚠ {roomName} 설비 파손 — 자재 생산 정지, 자재 -{lost} (수리 필요)");
                break;
            case TabooConsequenceType.VentilationFault:
                GameState.Instance.SetVentilationDown(true);
                EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
                    $"⚠ {roomName} 환기 정지 — 전 직원 스트레스 지속 상승 (수리 필요)");
                break;
            case TabooConsequenceType.MedicalContamination:
                GameState.Instance.SetMedicalContaminated(true);
                EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
                    $"⚠ {roomName} 의료 장비 오염 — 스트레스 치료 불가 (수리 필요)");
                break;
            case TabooConsequenceType.CoreOutputUnstable:
                GameState.Instance.SetCoreOutputUnstable(true);
                EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
                    $"⚠ {roomName} 봉쇄 코어 출력 불안정 — 복구 정지 + 복구율 감소 (수리 필요)");
                break;
            // ── 금기 위반 페널티 ────────────────────────────────────────
            case TabooConsequenceType.VentHaltAndStress:
                // 환기가 일정 시간 멈추고, 대상 직원의 스트레스가 오른다.
                VentHaltUntil = Now + ParamF("halt_seconds", 30f);
                GameState.Instance.SetVentilationDown(true);
                AddStressToTargets(roomId, ParamF("stress", 10f), "금기 위반");
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    $"⚠ 환기 {ParamF("halt_seconds", 30f):0}초 중단");
                break;

            case TabooConsequenceType.TrackingLost:
                // 이동한 직원의 위치 파악과 전화가 일정 시간 불가.
                foreach (var id in Targets(roomId))
                    _trackingLostUntil[id] = Now + ParamF("seconds", 20f);
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    $"⚠ 일부 직원의 위치·통신이 {ParamF("seconds", 20f):0}초간 두절");
                break;

            case TabooConsequenceType.PhoneLockAndStress:
                PhoneLockedUntil = Now + ParamF("seconds", 20f);
                AddStressToTargets(roomId, ParamF("stress", 8f), "금기 위반");
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    $"⚠ 전화기 {ParamF("seconds", 20f):0}초간 사용 불가");
                break;

            case TabooConsequenceType.PhoneImpostorLock:
                PhoneLockedUntil = Now + ParamF("seconds", 30f);
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    "⚠ 직원이 아닌 목소리가 응답했다 — 통화 잠김");
                break;

            case TabooConsequenceType.MaterialLossAndStress:
                GameState.Instance.AddMaterials(-ParamI("materials", 5));
                AddStressToTargets(roomId, ParamF("stress", 8f), "금기 위반");
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    $"⚠ 자재 -{ParamI("materials", 5)}");
                break;

            case TabooConsequenceType.TreatmentAbortAndStress:
                // 치료가 즉시 중단되고 대상 직원들의 스트레스가 오른다.
                GameState.Instance.SetMedicalContaminated(true);
                AddStressToTargets(roomId, ParamF("stress", 5f), "금기 위반");
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    "⚠ 치료 즉시 중단");
                break;

            case TabooConsequenceType.CctvChannelScramble:
                CctvScrambledUntil = Now + ParamF("seconds", 20f);
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    $"⚠ CCTV 채널 혼선 — {ParamF("seconds", 20f):0}초간 방 이름과 화면 불일치");
                break;

            case TabooConsequenceType.WorkSpeedPenalty:
                _workPenaltyMultiplier = ParamF("multiplier", 0.6f);
                _workPenaltyUntil = Now + ParamF("seconds", 30f);
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    $"⚠ 해당 직원 업무 속도 {(1f - _workPenaltyMultiplier) * 100f:0}% 감소");
                break;

            case TabooConsequenceType.StorageCapAndMaterialLoss:
                GameState.Instance.AddMaterialsCap(-ParamI("cap", 5));
                GameState.Instance.AddMaterials(-ParamI("materials", 5));
                EventLog.Instance?.LogEvent(LogEventType.TabooViolation, "", roomId,
                    $"⚠ 저장 한도 -{ParamI("cap", 5)} · 자재 -{ParamI("materials", 5)}");
                break;

            case TabooConsequenceType.StorageCollapse:
                // 한도가 줄고, 한도를 넘긴 보유 자재는 즉시 파괴된다(AddMaterialsCap 안에서 처리).
                int capLoss = stressAmount > 0f
                    ? Mathf.RoundToInt(stressAmount)
                    : Config.Instance.Data.StorageCollapseCapLoss;
                GameState.Instance.AddMaterialsCap(-capLoss);
                EventLog.Instance?.LogEvent(LogEventType.TaskFailed, "", roomId,
                    $"⚠ {roomName} 보관 선반 붕괴 — 자재 보유 한도 -{capLoss} (초과분 파괴)");
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
            case TabooConsequenceType.MedicalContamination: GameState.Instance.SetMedicalContaminated(false); break;
            case TabooConsequenceType.CoreOutputUnstable: GameState.Instance.SetCoreOutputUnstable(false); break;
            // 보관 선반 붕괴는 한도가 실제로 깎인 것이라 수리해도 자동 복구되지 않는다 —
            // 저장고 상시 업무로 다시 올려야 한다.
            case TabooConsequenceType.StorageCollapse: break;
        }
    }
}
