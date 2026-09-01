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
//   · 같은 직원 / 같은 사건 전화가 중복으로 쌓이지 않게 막는다.
// 발신자 선정과 대사 이벤트만 정한다 — 실제 벨/patience/포기는 Phone3D, 대사는 PhoneCallHud.
public partial class IncomingCallDirector : Node
{
    // 한 통화가 끝난 뒤 다음 전화까지의 짧은 간격(초).
    [Export] public float GapBetweenCallsSeconds = 2.2f;
    [Export] public int MaxQueueLength = 5;

    private sealed class Pending
    {
        public string EmployeeId = "";
        public string DialogueEvent = "";
        public string DedupeKey = "";
    }

    private readonly List<Pending> _queue = new();
    private Pending _active;
    private double _gapUntil;
    private bool _blackoutWas;
    private bool _eventWired, _phoneWired;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Process(double delta)
    {
        Wire();

        if (GameState.Instance?.CurrentPhase != GamePhase.Live)
        {
            // 근무가 아니면 큐를 비운다(정산/휴게/다음 날로 이월하지 않는다).
            if (_queue.Count > 0) _queue.Clear();
            _active = null;
            _blackoutWas = false;
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
                var witness = e.WitnessEmployeeIds.FirstOrDefault(Available);
                if (!string.IsNullOrEmpty(witness))
                    Enqueue(witness, DialogueRepository.EventWitnessSuspicious, "witness:" + witness);
                else
                    EnqueueAccident(e.RoomId, e.ActorEmployeeId);
                break;

            // 시설 사고(방치로 인한 고장 등) → 근처 직원이 "사고 발견"(①).
            case LogEventType.TaskFailed:
                EnqueueAccident(e.RoomId, "");
                break;

            // 사망(비명) → 인접 방 직원이 "옆방에서 비명"(②).
            case LogEventType.Death:
                EnqueueScream(e.RoomId);
                break;
        }
    }

    // 정전(전력 용량 0) 진입 순간 → 근무 가능한 직원 아무나 "정전 발생"(③).
    private void PollBlackout()
    {
        bool now = GameState.Instance != null && GameState.Instance.PowerCapacity == 0;
        if (now && !_blackoutWas)
        {
            string caller = RandomAvailable();
            if (!string.IsNullOrEmpty(caller))
                Enqueue(caller, DialogueRepository.EventBlackout, "blackout");
        }
        _blackoutWas = now;
    }

    private void EnqueueAccident(string roomId, string excludeEmployeeId)
    {
        string caller = NearestAvailable(roomId, excludeEmployeeId);
        if (!string.IsNullOrEmpty(caller))
            Enqueue(caller, DialogueRepository.EventAccidentNearby, "accident:" + roomId);
    }

    private void EnqueueScream(string roomId)
    {
        var sim = FacilitySimulation.Instance;
        var roomDef = sim?.GetRoomDef(roomId);
        if (roomDef == null) return;
        var candidates = roomDef.ConnectedRoomIds
            .SelectMany(r => sim.GetRoomState(r)?.OccupantEmployeeIds ?? new List<string>())
            .Where(Available)
            .Distinct()
            .ToList();
        if (candidates.Count == 0) return;
        string caller = candidates[_rng.RandiRange(0, candidates.Count - 1)];
        Enqueue(caller, DialogueRepository.EventScreamNextRoom, "scream:" + roomId);
    }

    // --- 큐 -----------------------------------------------------------

    private void Enqueue(string employeeId, string dialogueEvent, string dedupeKey)
    {
        if (string.IsNullOrEmpty(employeeId) || !Available(employeeId)) return;
        if (_queue.Count >= MaxQueueLength) return;
        // 같은 사건 / 같은 직원 전화가 이미 진행 중이거나 대기 중이면 새로 넣지 않는다.
        if (_active != null && (_active.DedupeKey == dedupeKey || _active.EmployeeId == employeeId)) return;
        if (_queue.Any(p => p.DedupeKey == dedupeKey || p.EmployeeId == employeeId)) return;

        _queue.Add(new Pending { EmployeeId = employeeId, DialogueEvent = dialogueEvent, DedupeKey = dedupeKey });
    }

    private void PumpQueue()
    {
        if (_active != null) return;
        if (Phone3D.Instance == null || Phone3D.Instance.IsBusy) return;
        if (Time.GetTicksMsec() / 1000.0 < _gapUntil) return;

        while (_queue.Count > 0)
        {
            var p = _queue[0];
            _queue.RemoveAt(0);
            if (!Available(p.EmployeeId)) continue; // 대기 중 사망/격리되면 건너뛴다
            _active = p;
            Phone3D.Instance.RingIncoming(p.EmployeeId, p.DialogueEvent);
            return;
        }
    }

    private void OnAnswered() { /* 통화 연결 — _active 는 통화 종료까지 유지 */ }

    private void OnCallEnded()
    {
        _active = null;
        _gapUntil = Time.GetTicksMsec() / 1000.0 + GapBetweenCallsSeconds;
    }

    private void OnCallMissed(string employeeId, string dialogueEvent)
    {
        _active = null;
        _gapUntil = Time.GetTicksMsec() / 1000.0 + GapBetweenCallsSeconds;
    }

    // --- 발신자 선정 헬퍼 -------------------------------------------

    private static bool Available(string employeeId)
    {
        var st = FacilitySimulation.Instance?.GetEmployeeState(employeeId);
        return st is { Alive: true, Isolated: false };
    }

    private string RandomAvailable()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null) return "";
        var pool = sim.GetEmployeeIds().Where(Available).ToList();
        return pool.Count == 0 ? "" : pool[_rng.RandiRange(0, pool.Count - 1)];
    }

    // roomId 에서 방 연결 구조상 가장 가까운 층부터 훑어 근무 가능한 직원을 찾는다.
    // 같은 거리에 여러 명이면 무작위. exclude 는 후보에서 뺀다(예: 파괴 당사자).
    private string NearestAvailable(string roomId, string exclude)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(roomId)) return RandomAvailable();

        var visited = new HashSet<string> { roomId };
        var frontier = new List<string> { roomId };

        while (frontier.Count > 0)
        {
            var here = frontier
                .SelectMany(r => sim.GetRoomState(r)?.OccupantEmployeeIds ?? new List<string>())
                .Where(id => id != exclude && Available(id))
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
