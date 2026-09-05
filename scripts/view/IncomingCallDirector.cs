using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 근무 중(GamePhase.Live) 발생하는 게임 이벤트를 직원의 "수신 전화" 로 바꿔 Phone3D 로 흘린다.
// 전화기는 한 번에 한 명하고만 연결되므로 여기서 IncomingCallQueue 를 소유한다.
//   · 여러 이벤트가 동시에 나도 벨/통화/UI 는 절대 겹치지 않는다.
//   · 한 통화가 (받음 / 통화 종료 / 직원이 기다리다 끊음) 중 하나로 끝난 뒤 짧은 간격을 두고
//     다음 큐를 처리한다.
//   · 한 상황(Incident)당 전화는 최대 2통, 서로 다른 직원만. 이미 그 상황으로 전화한
//     직원은 다시 걸지 않는다.
//   · 사고/비명 상황에서 "확인하러 가주세요"(첫 선택지)를 고르면 그 직원이 실제로 해당
//     작업실로 이동하고 상황은 종료된다. 거절하거나 전화를 안 받으면 다른 직원이 2차로
//     건다. 2차에서도 거절하면 아무도 가지 않고 그 상황의 전화는 끝.
// 발신자 선정과 대사 이벤트만 정한다 — 실제 벨/patience/포기는 Phone3D, 대사는 PhoneCallHud.
public partial class IncomingCallDirector : Node
{
    // 같은 상황의 2차 전화까지의 짧은 간격(초).
    [Export] public float GapBetweenCallsSeconds = 2.2f;
    // 다른 상황의 전화까지의 간격(초). DAY1 긴급 사고는 발생 즉시 신고되어야 하므로
    // 기본값은 0이다. 같은 사고의 후속 통화 간격은 GapBetweenCallsSeconds만 적용한다.
    [Export] public float GapBetweenIncidentsSeconds = 0f;
    // 큐에서 이만큼 묵은 전화는 걸지 않고 버린다(이미 지난 상황을 뒤늦게 알리지 않는다).
    [Export] public float StaleCallSeconds = 75f;
    [Export] public int MaxQueueLength = 5;
    // 한 상황당 걸려오는 전화 수 상한.
    [Export] public int MaxCallsPerIncident = 2;

    // "사고 발견"(①) 대사 원문은 6명 중 5명이 방 이름을 '발전실'로 못박고 있다
    // (docs/야간근무지침_상황별_예시_대사_모음.md). 그래서 정비실·환기실 사고에도
    // "발전실에서 폭발음" 이라고 전화가 와서, 플레이어에겐 발전실 전화가 4~5번 오는 것처럼 보인다.
    // 대사를 임의로 고칠 수 없으므로, 사고 발견 통화는 발전실 사고에만 건다.
    // 다른 작업실용 대사를 원문에 추가하면 이 값을 false 로 바꾸면 된다.
    [Export] public bool AccidentCallsPowerRoomOnly = true;

    private sealed class Pending
    {
        public string EmployeeId = "";
        public string DialogueEvent = "";
        public string DedupeKey = "";
        public double QueuedAt;
    }

    // 하나의 사건(사고/비명/정전/목격…)에 대한 전화 진행 상태.
    private sealed class Incident
    {
        public string RoomId = "";              // 사고/비명이 난 작업실 — 출동 목적지
        public string DialogueEvent = "";
        public int Calls;                        // 지금까지 실제로 벨이 울린 횟수
        public readonly HashSet<string> Called = new();
        public bool Closed;                      // 출동 확정 또는 전화 횟수 소진
    }

    private readonly List<Pending> _queue = new();
    private readonly Dictionary<string, Incident> _incidents = new();
    private Pending _active;
    private double _gapUntil;
    // 마지막으로 벨이 울린 상황과, 그 통화가 끝난 시각 — 상황 간 간격 계산용.
    private string _lastIncidentKey = "";
    private double _lastCallEndedAt = -1000.0;
    private bool _blackoutWas;
    private bool _eventWired, _phoneWired, _hudWired;
    private readonly RandomNumberGenerator _rng = new();

    // 첫 선택지가 "그 방으로 가보라"는 지시인 이벤트 — 원본 대사 목록 기준.
    private static bool IsDispatchEvent(string dialogueEvent) =>
        dialogueEvent is DialogueRepository.EventAccidentNearby or DialogueRepository.EventScreamNextRoom;

    public override void _Process(double delta)
    {
        Wire();

        if (GameState.Instance?.CurrentPhase != GamePhase.Live)
        {
            // 근무가 아니면 큐를 비운다(정산/휴게/다음 날로 이월하지 않는다).
            if (_queue.Count > 0) _queue.Clear();
            if (_incidents.Count > 0) _incidents.Clear();
            _active = null;
            _blackoutWas = false;
            _lastIncidentKey = "";
            _lastCallEndedAt = -1000.0;
            return;
        }

        PollBlackout();
        PumpQueue();
    }

    private void Wire()
    {
        if (!_eventWired && EventLog.Instance != null)
        {
            EventLog.Instance.EntryLogged += OnEntryLogged;
            _eventWired = true;
        }
        if (!_phoneWired && Phone3D.Instance != null)
        {
            Phone3D.Instance.PickedUp += OnAnswered;
            Phone3D.Instance.HungUp += OnCallEnded;
            Phone3D.Instance.CallMissed += OnCallMissed;
            _phoneWired = true;
        }
        if (!_hudWired && PhoneCallHud.Instance != null)
        {
            PhoneCallHud.Instance.EventChoiceMade += OnEventChoiceMade;
            _hudWired = true;
        }
    }

    public override void _ExitTree()
    {
        if (_eventWired && EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnEntryLogged;
        if (_phoneWired && Phone3D.Instance != null)
        {
            Phone3D.Instance.PickedUp -= OnAnswered;
            Phone3D.Instance.HungUp -= OnCallEnded;
            Phone3D.Instance.CallMissed -= OnCallMissed;
        }
        if (_hudWired && PhoneCallHud.Instance != null)
            PhoneCallHud.Instance.EventChoiceMade -= OnEventChoiceMade;
    }

    // --- 이벤트 → 전화 요청 --------------------------------------------

    private void OnEntryLogged()
    {
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        var entries = EventLog.Instance?.GetAllEntries();
        if (entries == null || entries.Count == 0) return;
        var e = entries[^1];

        switch (e.EventType)
        {
            // 파괴공작: 목격자가 있으면 "수상한 행동 목격"(⑤), 없으면 "사고 발견"(①) 으로.
            case LogEventType.Sabotage:
                var witness = e.WitnessEmployeeIds.FirstOrDefault(id => Available(id) && !AlreadyCalled(RoomKey(e.RoomId), id));
                if (!string.IsNullOrEmpty(witness))
                    Enqueue(witness, DialogueRepository.EventWitnessSuspicious, RoomKey(e.RoomId), e.RoomId);
                else
                    EnqueueAccident(e.RoomId, e.ActorEmployeeId);
                break;

            // 시설 사고(방치로 인한 고장 등) → 근처 직원이 "사고 발견"(①).
            case LogEventType.TaskFailed:
                EnqueueAccident(e.RoomId, "");
                break;

            // 발전실 금기 이상현상은 연출이 끝난 뒤 PowerOutage 로그로 결과가 확정된다.
            // 이전에는 이 로그를 전화 시스템이 듣지 않아 다음 별도 사고가 날 때까지 신고가
            // 늦어질 수 있었다. 발전실 사고 대사를 그대로 사용해 즉시 큐에 넣는다.
            case LogEventType.PowerOutage:
                EnqueueAccident(e.RoomId, "");
                break;

            // 사망(비명) → 인접 방 직원이 "옆방에서 비명"(②).
            case LogEventType.Death:
                EnqueueScream(e.RoomId);
                break;
        }
    }

    // 정전(전력 용량 0) 진입 순간 → 근무 가능한 직원 아무나 "정전 발생"(③).
    // 정전은 발전실 사고의 '결과'다. 플레이어에게는 "발전실 사고" 하나로 보이므로
    // 발전실과 같은 상황으로 묶는다 — 따로 두면 발전실 2통 + 정전 2통 = 4통이 된다.
    private void PollBlackout()
    {
        bool now = GameState.Instance != null && GameState.Instance.PowerCapacity == 0;
        if (now && !_blackoutWas)
        {
            string room = PowerRoomId();
            string key = RoomKey(room);
            string caller = RandomAvailable(key);
            if (!string.IsNullOrEmpty(caller))
                Enqueue(caller, DialogueRepository.EventBlackout, key, room);
        }
        _blackoutWas = now;
    }

    // 전력을 관리하는 작업실(보통 발전실). 데이터에서 찾고, 없으면 관례 id로 떨어진다.
    private static string PowerRoomId()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return "power_room";
        foreach (var id in sim.GetRoomIds())
            if (sim.GetRoomDef(id)?.ManagedResource == RoomResourceType.Power) return id;
        return "power_room";
    }

    // 한 작업실에서 벌어지는 일(사고 위험 → 고장 → 목격 → 비명)은 전부 "그 방의 한 상황"으로
    // 묶는다. 그래야 발전실 하나 때문에 6명이 번갈아 전화하는 일이 없다(상황당 2통 상한).
    private static string RoomKey(string roomId) => "room:" + roomId;

    private void EnqueueAccident(string roomId, string excludeEmployeeId)
    {
        // 대사가 발전실 기준으로 쓰여 있어, 다른 작업실 사고로는 전화를 걸지 않는다.
        if (AccidentCallsPowerRoomOnly && roomId != PowerRoomId()) return;

        string key = RoomKey(roomId);
        string caller = NearestAvailable(roomId, key, excludeEmployeeId);
        if (!string.IsNullOrEmpty(caller))
            Enqueue(caller, DialogueRepository.EventAccidentNearby, key, roomId);
    }

    private void EnqueueScream(string roomId)
    {
        string key = RoomKey(roomId);
        string caller = ScreamCaller(roomId, key);
        if (!string.IsNullOrEmpty(caller))
            Enqueue(caller, DialogueRepository.EventScreamNextRoom, key, roomId);
    }

    // 비명은 "옆방" 직원만 듣는다 — 인접 방 점유자 중에서 고른다.
    private string ScreamCaller(string roomId, string incidentKey)
    {
        var sim = FacilitySimulation.Instance;
        var roomDef = sim?.GetRoomDef(roomId);
        if (roomDef == null) return "";
        var candidates = roomDef.ConnectedRoomIds
            .SelectMany(r => sim.GetRoomState(r)?.OccupantEmployeeIds ?? new List<string>())
            .Where(id => Available(id) && !AlreadyCalled(incidentKey, id))
            .Distinct()
            .ToList();
        return candidates.Count == 0 ? "" : candidates[_rng.RandiRange(0, candidates.Count - 1)];
    }

    // --- 상황(Incident) -----------------------------------------------

    private Incident GetIncident(string key) => _incidents.GetValueOrDefault(key);

    private bool AlreadyCalled(string incidentKey, string employeeId) =>
        GetIncident(incidentKey)?.Called.Contains(employeeId) ?? false;

    // --- 큐 -----------------------------------------------------------

    // followUp = 지금 통화 중인 그 사건의 "2차 전화"를 일부러 예약하는 경우.
    // 이때만 '같은 사건이 이미 진행 중' 검사를 건너뛴다(그 검사는 로그 중복 방지용).
    private void Enqueue(string employeeId, string dialogueEvent, string dedupeKey, string roomId, bool followUp = false)
    {
        if (string.IsNullOrEmpty(employeeId) || !Available(employeeId)) return;
        if (_queue.Count >= MaxQueueLength) return;
        // 같은 사건 / 같은 직원 전화가 이미 진행 중이거나 대기 중이면 새로 넣지 않는다.
        if (_active != null && _active.EmployeeId == employeeId) return;
        if (!followUp && _active != null && _active.DedupeKey == dedupeKey) return;
        if (_queue.Any(p => p.DedupeKey == dedupeKey || p.EmployeeId == employeeId)) return;

        if (!_incidents.TryGetValue(dedupeKey, out var inc))
        {
            inc = new Incident { RoomId = roomId, DialogueEvent = dialogueEvent };
            _incidents[dedupeKey] = inc;
        }
        // 이미 끝난 상황이거나, 전화 횟수를 다 썼거나, 그 직원이 이 상황으로 이미 걸었으면 안 건다.
        if (inc.Closed || inc.Calls >= MaxCallsPerIncident || !inc.Called.Add(employeeId)) return;

        _queue.Add(new Pending
        {
            EmployeeId = employeeId,
            DialogueEvent = dialogueEvent,
            DedupeKey = dedupeKey,
            QueuedAt = Time.GetTicksMsec() / 1000.0,
        });
    }

    private void PumpQueue()
    {
        if (_active != null) return;
        if (Phone3D.Instance == null || Phone3D.Instance.IsBusy) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _gapUntil) return;

        while (_queue.Count > 0)
        {
            var p = _queue[0];

            // 상황이 이미 끝났거나 / 직원이 사망·격리됐거나 / 너무 묵은 전화는 버린다.
            var inc = GetIncident(p.DedupeKey);
            if (inc is { Closed: true } || !Available(p.EmployeeId) || now - p.QueuedAt > StaleCallSeconds)
            {
                _queue.RemoveAt(0);
                continue;
            }

            // 다른 상황의 전화라면 충분히 간격을 둔 뒤에만 건다(대기열에 그대로 남겨둔다).
            if (p.DedupeKey != _lastIncidentKey && now < _lastCallEndedAt + GapBetweenIncidentsSeconds)
                return;

            _queue.RemoveAt(0);
            if (inc != null) inc.Calls++;
            _lastIncidentKey = p.DedupeKey;
            _active = p;
            Phone3D.Instance.RingIncoming(p.EmployeeId, p.DialogueEvent);
            return;
        }
    }

    private void OnAnswered() { /* 통화 연결 — _active 는 통화 종료까지 유지 */ }

    // 플레이어가 선택지를 골랐다. 사고/비명에서 첫 선택지 = "확인하러 가주세요".
    private void OnEventChoiceMade(string employeeId, string dialogueEvent, int choiceIndex)
    {
        if (_active == null || _active.EmployeeId != employeeId) return;
        var inc = GetIncident(_active.DedupeKey);
        if (inc == null || inc.Closed || !IsDispatchEvent(dialogueEvent)) return;

        if (choiceIndex == 0)
        {
            // 실제로 그 작업실로 보낸다. 이 상황의 전화는 여기서 끝.
            inc.Closed = true;
            if (!string.IsNullOrEmpty(inc.RoomId))
                FacilitySimulation.Instance?.MoveEmployeeTo(employeeId, inc.RoomId);
            return;
        }

        // 거절 — 아직 여유가 있으면 다른 직원이 2차로 건다.
        QueueFollowUp(_active.DedupeKey, inc);
    }

    // 확인을 지시받지 못한 상황: 다른 직원이 한 번 더 건다(상한까지). 상한을 넘으면 종료.
    private void QueueFollowUp(string key, Incident inc)
    {
        if (inc.Closed) return;
        if (inc.Calls >= MaxCallsPerIncident) { inc.Closed = true; return; }

        // 2차 전화는 방금 통화한 그 상황의 대사를 이어 쓴다.
        string ev = _active?.DialogueEvent ?? inc.DialogueEvent;
        string next = ev == DialogueRepository.EventScreamNextRoom
            ? ScreamCaller(inc.RoomId, key)
            : NearestAvailable(inc.RoomId, key, "");
        if (string.IsNullOrEmpty(next)) { inc.Closed = true; return; }

        Enqueue(next, ev, key, inc.RoomId, followUp: true);
    }

    private void OnCallEnded()
    {
        FinishActive();
    }

    private void OnCallMissed(string employeeId, string dialogueEvent)
    {
        // 전화를 안 받은 것도 "확인 지시를 안 한" 것으로 본다 → 다른 직원이 2차로 건다.
        if (_active != null && IsDispatchEvent(_active.DialogueEvent))
        {
            var inc = GetIncident(_active.DedupeKey);
            if (inc != null) QueueFollowUp(_active.DedupeKey, inc);
        }
        FinishActive();
    }

    // 통화 종료. 상황을 여기서 닫지는 않는다 — 닫는 조건은 두 가지뿐이다:
    // 출동 확정(OnEventChoiceMade), 또는 전화 횟수 소진(QueueFollowUp / Enqueue 의 상한 검사).
    private void FinishActive()
    {
        _active = null;
        _lastCallEndedAt = Time.GetTicksMsec() / 1000.0;
        _gapUntil = _lastCallEndedAt + GapBetweenCallsSeconds;
    }

    // --- 발신자 선정 헬퍼 -------------------------------------------

    // 오늘 근무에 배치된 직원만 전화를 건다 — 배치 안 된 직원은 미니맵에도 안 뜨는 비번이다.
    // 전화를 걸어올 수 있는 직원 = 오늘 근무자. 판정은 FacilitySimulation 한 곳에서만 한다
    // (Phone3D 의 발신 대상 판정과 같은 규칙).
    private static bool Available(string employeeId) =>
        FacilitySimulation.Instance?.IsOnDuty(employeeId) ?? false;

    private string RandomAvailable(string incidentKey)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return "";
        var pool = sim.GetEmployeeIds().Where(id => Available(id) && !AlreadyCalled(incidentKey, id)).ToList();
        return pool.Count == 0 ? "" : pool[_rng.RandiRange(0, pool.Count - 1)];
    }

    // roomId 에서 방 연결 구조상 가장 가까운 층부터 훑어 근무 가능한 직원을 찾는다.
    // 같은 거리에 여러 명이면 무작위. exclude 는 후보에서 뺀다(예: 파괴 당사자).
    // 이 상황으로 이미 전화한 직원도 후보에서 제외한다.
    //
    // 사고가 난 방 '안'에 있는 직원은 발신 대상이 아니다 — 그 방은 이미 사람이 있는 상태이므로
    // "확인하러 가주세요" 가 성립하지 않는다. 그래서 인접 방(거리 1)부터 훑기 시작한다.
    private string NearestAvailable(string roomId, string incidentKey, string exclude)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(roomId)) return RandomAvailable(incidentKey);

        var visited = new HashSet<string> { roomId };
        var frontier = (sim.GetRoomDef(roomId)?.ConnectedRoomIds ?? new Godot.Collections.Array<string>())
            .Where(visited.Add).ToList();

        while (frontier.Count > 0)
        {
            var here = frontier
                .SelectMany(r => sim.GetRoomState(r)?.OccupantEmployeeIds ?? new List<string>())
                .Where(id => id != exclude && Available(id) && !AlreadyCalled(incidentKey, id))
                .Distinct()
                .ToList();
            if (here.Count > 0)
                return here[_rng.RandiRange(0, here.Count - 1)];

            var next = new List<string>();
            foreach (var r in frontier)
                foreach (var n in sim.GetRoomDef(r)?.ConnectedRoomIds ?? new Godot.Collections.Array<string>())
                    if (visited.Add(n)) next.Add(n);
            frontier = next;
        }
        return "";
    }
}
