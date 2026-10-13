using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 딴짓 한 번이 지나는 단계.
//   Working → Entering → Distracted → (적발되면) Noticed → Recovering → Returning → Done
// 적발되지 않으면 Distracted 가 끝날 때 Noticed 를 건너뛰고 Recovering 으로 간다.
public enum DistractionPhase
{
    None,
    Entering,     // 하던 일을 멈추고 자리를 잡는다(걸어가는 것도 여기)
    Distracted,   // 실제로 딴짓을 하고 있다 — 이 동안에만 목격이 성립한다
    Noticed,      // CCTV 를 알아챘다(개인별 반응)
    Recovering,   // 행동을 정리한다(휴대폰 넣기 · 일어나기)
    Returning,    // 원래 작업 자리로 돌아간다
    Done,
}

// 실제로 일어난 딴짓 한 번. **시뮬레이션이 소유한다.**
//
// 3D 애니메이션은 이 기록을 읽어 그릴 뿐이고, 거꾸로 화면이 이 기록을 만들지 않는다.
// 그래서 CCTV 채널을 돌렸다 돌아와도 같은 사건이 그 사이 흐른 시간만큼 진행돼 있다.
public sealed class DistractionEvent
{
    public string EventId = "";
    public string EmployeeId = "";
    public DistractionKind Kind;
    public int Day;
    // 행동이 **일어난** 작업실. 전화 시점에 직원이 다른 방으로 옮겨져도 바뀌지 않는다(지시서 §11-3).
    public string RoomId = "";
    public string RoomName = "";
    public float StartedAt;            // 근무 시각(초)
    public DistractionPhase Phase;
    public float PhaseElapsed;
    public float Elapsed;
    public float DistractLimit;        // 이번에는 몇 초까지 하는가(Min~Max 중 하나)

    // 관리자가 CCTV 로 **실제로** 본 적이 있는가. 같은 사건을 여러 번 봐도 한 번만 센다.
    public bool HasBeenObserved;
    public float ObservedAt = -1f;
    public bool HasReactedToCctv;      // 적발 반응을 재생했는가(표현 계층이 올린다)
    public bool HasBeenAskedByManager; // 전화로 이미 캐물었는가

    // 고양이 ↔ 강아지가 공유하는 실제 통화 사건.
    public string LinkedPhoneCallEventId = "";

    public bool Aborted;               // 더 높은 상태(기절·격리·괴물 등)에 끊겼다
    public string AbortReason = "";    // 무엇에 끊겼는가(검수·QA 용)
    public int Serial;                 // 표현 계층이 "같은 사건인가" 를 가리는 번호
    public float SeenSeconds;          // 지금 연속으로 보이고 있는 시간(목격 판정용)

    public bool Running => Phase is not (DistractionPhase.None or DistractionPhase.Done);
    public DistractionDef Def => DistractionCatalog.Get(Kind);
    public string Code => Def?.Code ?? "";
    public bool Strong => Def?.Strong ?? false;
}

// 고양이와 강아지가 **실제로** 통화한 한 건. 두 사람은 이 하나의 사건을 기준으로 답한다.
// (한쪽은 DAY1 에 했다고 하고 다른 쪽은 DAY2 라고 하는 모순이 생기지 않게 한다.)
public sealed class PersonalCallEvent
{
    public string EventId = "";
    public int Day;
    public string AId = "";            // 통화자 두 명
    public string BId = "";
    public float StartedAt;
    public float EndedAt = -1f;
    public string EndReason = "";      // "" 정상 종료 · "manager_call" 관리자 전화로 끊김 · "aborted" 상황 중단
    public bool Active => EndedAt < 0f;

    // 관리자에게 "상대가 누구인지" 를 말해 준 쪽.
    public readonly HashSet<string> Admitted = new();
    // 교차 확인 질문("상대와 통화했습니까?")을 이미 쓴 쪽 — 사건당 한 번뿐이다.
    public readonly HashSet<string> CrossAsked = new();

    public string Other(string id) => id == AId ? BId : id == BId ? AId : "";
    public bool Involves(string id) => id == AId || id == BId;
}

// 근무 중 직원들의 딴짓을 발생시키고, 진행시키고, 목격을 기록한다. **시뮬레이션 쪽이다.**
//
// 규칙(지시서 §2 · §3 · §14):
//  · 한 번에 한 명만(고양이·강아지의 **한 통화**는 예외 — 같은 사건이므로 둘이 동시에 보인다)
//  · 한 직원은 하루 두 번까지, 같은 딴짓은 하루 한 번까지
//  · 어제 걸린 사람은 오늘 덜 뽑힌다(특정 직원만 매일 걸리는 현상 방지)
//  · 목격은 **실제 피드 표시 조건**이 충족된 동안에만 쌓인다(NoteSeen 을 부르는 쪽이 판정한다)
//  · 하루 목격 5건이 되면 더 만들지 않는다. 목격이 모자라면 기회 간격을 좁히고,
//    지금 보고 있는 방을 조금 더 자주 고른다 — 보지 않은 것을 봤다고 기록하지는 않는다
public sealed class DistractionSystem
{
    // 하루에 "중복 없는 실제 목격" 을 이만큼까지만 만든다(지시서 §2).
    public const int MaxWitnessedPerDay = 5;
    public const int MaxEventsPerEmployeePerDay = 2;
    public const int MaxEventsPerDay = 9;

    // 근무 시작 후 첫 기회까지 · 기회 사이 간격(초).
    private const float FirstChanceSeconds = 6.5f;
    private const float GapSeconds = 12.5f;
    private const float GapJitter = 3.5f;
    private const float HurryGapSeconds = 8.5f;     // 목격이 모자랄 때
    private const float LateGapSeconds = 6f;       // 근무 후반인데도 목격이 모자랄 때
    private const float MaxHoldSeconds = 12f;     // CCTV 가 꺼져 있을 때 기회를 아껴 두는 한도
    private const float AfterEndGapSeconds = 3.5f;

    // 관측이 성립하기까지 연속으로 보여야 하는 시간(초). 채널을 휙 넘기는 것은 관측이 아니다.
    private const float ObserveSeconds = 0.55f;
    // 이만큼 끊기면 "보던 것을 멈췄다" 로 본다(한두 프레임의 공백은 무시).
    private const float SeenGraceSeconds = 0.3f;

    // 딴짓 중에는 그 직원의 업무 기여가 준다(지시서 §2 — 밸런스를 깨뜨리지 않을 만큼만).
    private const float WorkRateWhileAway = 0.55f;
    private const float WorkRateWhileSettling = 0.85f;

    private readonly List<DistractionEvent> _events = new();      // 오늘 + 지난 날 기록(최근 것만 보관)
    private readonly List<PersonalCallEvent> _calls = new();
    private readonly Dictionary<string, DistractionEvent> _active = new();
    private readonly Dictionary<string, float> _seenGrace = new();
    private readonly HashSet<string> _witnessedYesterday = new();

    private readonly RandomNumberGenerator _rng = new();
    private int _shiftVersion = -1;
    private int _day = -1;
    private int _serial;
    private int _eventNo;
    private float _nextChanceAt;
    private float _holdSince = -1f;

    public IReadOnlyList<DistractionEvent> Events => _events;
    public IReadOnlyList<PersonalCallEvent> PersonalCalls => _calls;

    // 검증용 — 오늘 실제로 일어난 횟수와 그중 관리자가 목격한 횟수.
    public int StartedToday { get; private set; }
    public int WitnessedToday { get; private set; }

    // 스스로 기회를 만드는 발생기를 잠시 멈춘다. **검사 씬 전용**이다 —
    // 정해진 딴짓 하나만 일으켜 조건을 확인할 때 다른 딴짓이 끼어들지 않게 한다.
    public bool AutoSpawn = true;

    public DistractionSystem()
    {
        _rng.Randomize();
    }

    // ── 한 프레임 ────────────────────────────────────────────────────────

    public void Tick(float delta, FacilitySimulation sim)
    {
        if (sim == null) return;
        var gs = GameState.Instance;
        if (gs == null) return;

        if (_shiftVersion != FacilitySimulation.ShiftStartVersion) BeginShift(gs.CurrentDay);
        // 근무가 끝났다 — 하던 딴짓도 거기서 끝난다(보고서 화면에 그 자세가 남지 않게).
        // 목격 기록은 그대로 둔다 — 휴게시간 조사와 다음 날 기록에 쓰인다.
        if (gs.CurrentPhase != GamePhase.Live)
        {
            if (_active.Count > 0) EndAllActive(gs.DayTimeSeconds);
            return;
        }

        float now = gs.DayTimeSeconds;
        TickWatch(delta, sim);
        TickActive(delta, now, sim);
        TickChance(now, sim);
    }

    private void BeginShift(int day)
    {
        _shiftVersion = FacilitySimulation.ShiftStartVersion;
        // 어제 걸린 사람은 오늘 덜 뽑는다(같은 사람이 매일 걸리지 않게).
        _witnessedYesterday.Clear();
        foreach (var e in _events)
            if (e.Day == _day && e.HasBeenObserved) _witnessedYesterday.Add(e.EmployeeId);

        _day = day;
        _active.Clear();
        _seenGrace.Clear();
        _watchRoom = "";
        _watchSeconds = 0f;
        _holdSince = -1f;
        StartedToday = 0;
        WitnessedToday = 0;
        _nextChanceAt = FirstChanceSeconds;
        // 끝난 통화는 모두 닫는다(근무가 바뀌었다).
        foreach (var c in _calls)
            if (c.Active) { c.EndedAt = 0f; c.EndReason = "shift_end"; }
        // 기록은 이틀치만 들고 있는다 — 과거 기록이 필요한 조사 연결은 Day 로 고른다.
        _events.RemoveAll(e => e.Day < day - 1);
        _calls.RemoveAll(c => c.Day < day - 1);
    }

    private void EndAllActive(float now)
    {
        foreach (var (_, e) in _active)
        {
            e.Aborted = true;
            e.AbortReason = "근무 종료";
            End(e, now, "shift_end");
        }
        _active.Clear();
    }

    private void TickActive(float delta, float now, FacilitySimulation sim)
    {
        if (_active.Count == 0) return;
        var finished = new List<string>();
        foreach (var (id, e) in _active)
        {
            // 보이지 않게 된 지 조금 지나면 "연속 관측" 을 끊는다.
            if (_seenGrace.TryGetValue(id, out float g))
            {
                g -= delta;
                if (g <= 0f) { _seenGrace.Remove(id); e.SeenSeconds = 0f; }
                else _seenGrace[id] = g;
            }

            e.Elapsed += delta;
            e.PhaseElapsed += delta;

            // 더 높은 상태가 되면 즉시 끊는다(지시서 §13).
            string stop = StopReason(sim, e);
            if (stop != null)
            {
                e.Aborted = true;
                e.AbortReason = stop;
                End(e, now, "aborted");
                finished.Add(id);
                continue;
            }

            var def = e.Def;
            switch (e.Phase)
            {
                case DistractionPhase.Entering:
                    if (e.PhaseElapsed >= def.EnterSeconds) Advance(e, DistractionPhase.Distracted);
                    break;
                case DistractionPhase.Distracted:
                    if (e.PhaseElapsed >= e.DistractLimit) Advance(e, DistractionPhase.Recovering);
                    break;
                case DistractionPhase.Noticed:
                    if (e.PhaseElapsed >= def.NoticeSeconds) Advance(e, DistractionPhase.Recovering);
                    break;
                case DistractionPhase.Recovering:
                    if (e.PhaseElapsed >= def.RecoverSeconds) Advance(e, DistractionPhase.Returning);
                    break;
                case DistractionPhase.Returning:
                    if (e.PhaseElapsed >= def.ReturnSeconds) { End(e, now, ""); finished.Add(id); }
                    break;
            }
        }
        foreach (string id in finished) _active.Remove(id);
    }

    private static void Advance(DistractionEvent e, DistractionPhase phase)
    {
        e.Phase = phase;
        e.PhaseElapsed = 0f;
    }

    private void End(DistractionEvent e, float now, string reason)
    {
        e.Phase = DistractionPhase.Done;
        e.PhaseElapsed = 0f;
        // 짝 통화는 두 사람이 같은 사건을 공유한다 — 한쪽이 끝나면 통화 자체를 닫는다.
        var call = CallById(e.LinkedPhoneCallEventId);
        if (call is { Active: true })
        {
            call.EndedAt = now;
            call.EndReason = reason;
        }
        _nextChanceAt = Mathf.Max(_nextChanceAt, now + AfterEndGapSeconds);
    }

    // ── 목격 ─────────────────────────────────────────────────────────────

    // 표현 계층이 **실제 피드 조건이 모두 맞는 동안** 매 프레임 부른다.
    //   · CCTV 전력 ON · 그 방 채널 선택 · 신호 정상 · CRT 가 CCTV 를 띄우고 있음
    //   · 그 직원이 화면에 실제로 보이는 상태 · 딴짓 동작을 재생 중
    // 여기서는 그 상태가 ObserveSeconds 이상 이어졌는지만 센다.
    public void NoteSeen(string employeeId, float delta)
    {
        if (!_active.TryGetValue(employeeId, out var e)) return;
        if (e.Phase != DistractionPhase.Distracted) return;   // 자리 잡는 중·정리하는 중은 아니다
        _seenGrace[employeeId] = SeenGraceSeconds;
        if (e.HasBeenObserved) return;
        e.SeenSeconds += delta;
        if (e.SeenSeconds < ObserveSeconds) return;

        e.HasBeenObserved = true;
        e.ObservedAt = GameState.Instance?.DayTimeSeconds ?? 0f;
        WitnessedToday++;
        // 적발 — 즉시 개인별 반응으로 넘어간다.
        Advance(e, DistractionPhase.Noticed);
        // 통화를 목격한 것만으로는 "상대가 누구" 를 알 수 없다 — 교차 질문은 직원이
        // 전화로 직접 말한 뒤에야(MarkAdmitted) 열린다.
    }

    // 표현 계층이 적발 반응을 실제로 재생했다.
    public void NoteReacted(string employeeId)
    {
        if (_active.TryGetValue(employeeId, out var e)) e.HasReactedToCctv = true;
    }

    // 방 구조상 그 행동을 할 자리가 없다 — 아직 아무도 못 봤으니 조용히 되돌린다(지시서 §12-3).
    public void ReportPlaceUnavailable(string employeeId)
    {
        if (!_active.TryGetValue(employeeId, out var e)) return;
        if (e.HasBeenObserved) return;
        e.Aborted = true;
        e.AbortReason = "자리 없음";
        End(e, GameState.Instance?.DayTimeSeconds ?? 0f, "no_place");
        _active.Remove(employeeId);
        // 같은 딴짓을 다시 고르지 않도록 "오늘 썼다" 로 남겨 둔다(기록은 남는다).
    }

    // 관리자가 이 직원에게 전화를 걸었다 — 개인 통화는 끊기고 관리자 전화가 우선한다(지시서 §10-3).
    public void NotifyManagerCall(string employeeId)
    {
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        foreach (var c in _calls)
        {
            if (!c.Active || !c.Involves(employeeId)) continue;
            c.EndedAt = now;
            c.EndReason = "manager_call";
            // 두 사람 모두 통화를 정리한다(이미 적발된 쪽의 기록은 그대로 남는다).
            foreach (string side in new[] { c.AId, c.BId })
                if (_active.TryGetValue(side, out var e) && e.LinkedPhoneCallEventId == c.EventId
                    && e.Phase is DistractionPhase.Entering or DistractionPhase.Distracted)
                    Advance(e, DistractionPhase.Recovering);
        }
    }

    // ── 조회 ─────────────────────────────────────────────────────────────

    public DistractionEvent ActiveOf(string employeeId) =>
        _active.TryGetValue(employeeId, out var e) ? e : null;

    public bool AnyActive => _active.Count > 0;

    // 업무 기여 배율(1 = 평소). 딴짓 중에는 조금 줄어든다 — 생산량을 크게 떨어뜨리지는 않는다.
    public float WorkRateFactor(string employeeId)
    {
        if (!_active.TryGetValue(employeeId, out var e)) return 1f;
        return e.Phase switch
        {
            DistractionPhase.Entering or DistractionPhase.Distracted or DistractionPhase.Noticed => WorkRateWhileAway,
            DistractionPhase.Recovering or DistractionPhase.Returning => WorkRateWhileSettling,
            _ => 1f,
        };
    }

    // 오늘 이 직원의 딴짓 중 **관리자가 실제로 목격한** 것들. 최근 것이 먼저.
    public List<DistractionEvent> WitnessedTodayOf(string employeeId)
    {
        var list = new List<DistractionEvent>();
        foreach (var e in _events)
            if (e.Day == _day && e.EmployeeId == employeeId && e.HasBeenObserved) list.Add(e);
        list.Sort((a, b) => b.ObservedAt.CompareTo(a.ObservedAt));
        return list;
    }

    // 전화에서 지적할 수 있는 가장 최근 목격(아직 묻지 않은 것). 없으면 null.
    public DistractionEvent LatestUnasked(string employeeId)
    {
        foreach (var e in WitnessedTodayOf(employeeId))
            if (!e.HasBeenAskedByManager) return e;
        return null;
    }

    public void MarkAsked(DistractionEvent e)
    {
        if (e == null) return;
        e.HasBeenAskedByManager = true;
    }

    // 그 직원이 관리자에게 **통화 상대가 누구인지** 를 말했다 → 상대에게 교차 질문이 열린다.
    // 강아지는 첫 대답에서 바로 말하고, 고양이는 꼬리질문("누구와 통화했습니까?")에 답할 때 말한다.
    public void MarkAdmitted(DistractionEvent e)
    {
        var call = CallById(e?.LinkedPhoneCallEventId);
        if (call != null) call.Admitted.Add(e.EmployeeId);
    }

    public PersonalCallEvent CallById(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return null;
        foreach (var c in _calls)
            if (c.EventId == eventId) return c;
        return null;
    }

    // 이 직원에게 "상대와 통화했습니까?" 를 물을 수 있는가 — 상대가 먼저 실토한 통화가 있고,
    // 이 사건에 대해 아직 교차 질문을 쓰지 않았을 때만(일회용).
    public PersonalCallEvent PendingCrossQuestion(string employeeId)
    {
        for (int i = _calls.Count - 1; i >= 0; i--)
        {
            var c = _calls[i];
            if (c.Day != _day || !c.Involves(employeeId)) continue;
            string other = c.Other(employeeId);
            if (!c.Admitted.Contains(other)) continue;        // 상대가 아직 말하지 않았다
            if (c.CrossAsked.Contains(employeeId)) continue;  // 이미 물었다
            return c;
        }
        return null;
    }

    public void MarkCrossAsked(PersonalCallEvent call, string employeeId)
    {
        if (call == null || string.IsNullOrEmpty(employeeId)) return;
        call.CrossAsked.Add(employeeId);
    }

    // ── 발생 ─────────────────────────────────────────────────────────────

    private void TickChance(float now, FacilitySimulation sim)
    {
        if (!AutoSpawn || now < _nextChanceAt) return;
        if (_active.Count > 0)
        {
            // 한 번에 한 명만 — 지금 하는 사람이 끝날 때까지 기다린다.
            _nextChanceAt = now + 1.5f;
            return;
        }

        bool hurry = WitnessedToday < 2;
        // CCTV 가 통째로 꺼져 있으면(전력 OFF · 설비 고장) 아무도 볼 수 없다 —
        // 목격이 모자란 동안에는 기회를 아껴 두었다가 화면이 돌아온 뒤에 쓴다(지시서 §2 "기회를 연기").
        // 넉넉할 때는 그냥 일어나게 둔다 — 아무도 못 본 딴짓도 있어야 한다.
        if (hurry && GameState.Instance?.IsCctvOperational() != true)
        {
            if (_holdSince < 0f) _holdSince = now;
            // 다만 오래 꺼져 있는 근무를 통째로 비워 두지는 않는다 — 잠깐 아끼고 나면 그냥 일어난다.
            if (now - _holdSince < MaxHoldSeconds)
            {
                _nextChanceAt = now + 2.5f;
                return;
            }
        }
        else _holdSince = -1f;
        // 근무가 절반 넘게 지났는데 아직 두 번도 못 봤다 — 기회를 더 자주, 보고 있는 방 쪽으로 돌린다.
        // (없는 목격을 만들어 내지는 않는다. 기회의 **때와 자리**만 조정한다 — 지시서 §2.)
        float left = DayObjectives.RemainingSeconds;
        float full = DayObjectives.MaxShiftSeconds;
        bool late = hurry && left > 0f && full > 0f && left < full * 0.5f;
        _nextChanceAt = now + (late ? LateGapSeconds : hurry ? HurryGapSeconds : GapSeconds)
                            + _rng.RandfRange(0f, late ? GapJitter * 0.4f : GapJitter);

        if (WitnessedToday >= MaxWitnessedPerDay) return;
        if (StartedToday >= MaxEventsPerDay) return;
        // 근무가 거의 끝났으면 새로 시작하지 않는다(끝까지 못 보여 준다).
        if (left > 0f && left < 14f) return;

        TrySpawn(now, sim, hurry, late);
    }

    private void TrySpawn(float now, FacilitySimulation sim, bool hurry, bool late)
    {
        // 지금 관리자가 실제로 보고 있는 방 — 그쪽에서 일어나면 자연스럽게 걸린다.
        // (채널을 바꿨다는 이유만으로 새로 만들지는 않는다. 기회가 돌아온 이 순간에만 본다.)
        string watched = WatchedRoom(sim);
        string selected = SelectedRoom(sim);
        float watchBias = late ? 9f : hurry ? 6f : 2.4f;

        var pool = new List<(DistractionDef Def, string Room, float Weight)>();
        foreach (string id in sim.GetEmployeeIds())
        {
            if (!Eligible(sim, id, out var st, out string room)) continue;
            foreach (var def in DistractionCatalog.For(id))
            {
                float w = Weigh(sim, st, def, room, watched, selected, watchBias, hurry);
                if (w > 0f) pool.Add((def, room, w));
            }
        }
        if (pool.Count == 0) return;

        float total = 0f;
        foreach (var p in pool) total += p.Weight;
        float roll = _rng.RandfRange(0f, total);
        foreach (var p in pool)
        {
            roll -= p.Weight;
            if (roll > 0f) continue;
            Start(sim, p.Def, p.Room, now);
            return;
        }
        Start(sim, pool[^1].Def, pool[^1].Room, now);
    }

    // 지금 **알아볼 만큼 계속 보고 있는** 방(3초 이상). 없으면 빈 문자열.
    // 채널을 휙휙 넘기는 것은 보고 있는 것이 아니다 — 여기서 직접 센다(다른 계통에 기대지 않는다).
    private string WatchedRoom(FacilitySimulation sim) =>
        _watchSeconds >= WatchStreakSeconds ? _watchRoom : "";

    // 같은 방을 연속해서 본 시간. 채널을 바꾸거나 신호가 끊기면 0 으로 돌아간다.
    private const float WatchStreakSeconds = 3f;
    private string _watchRoom = "";
    private float _watchSeconds;

    private void TickWatch(float delta, FacilitySimulation sim)
    {
        var gs = GameState.Instance;
        string room = gs != null && gs.IsCctvOperational() ? sim.SurveillanceTargetRoomId ?? "" : "";
        if (string.IsNullOrEmpty(room) || room != _watchRoom || sim.IsRoomCctvBlocked(room))
        {
            _watchRoom = room;
            _watchSeconds = 0f;
            return;
        }
        _watchSeconds += delta;
    }

    // 지금 채널이 **맞춰져 있는** 방(막 돌렸을 수도 있다). 신호가 살아 있어야 한다.
    private string SelectedRoom(FacilitySimulation sim)
    {
        var gs = GameState.Instance;
        if (gs == null || !gs.IsCctvOperational()) return "";
        string room = sim.SurveillanceTargetRoomId ?? "";
        return string.IsNullOrEmpty(room) || sim.IsRoomCctvBlocked(room) ? "" : room;
    }

    private bool Eligible(FacilitySimulation sim, string id, out EmployeeState st, out string room)
    {
        room = "";
        st = sim.GetEmployeeState(id);
        if (st == null || !st.Alive || !sim.IsOnDuty(id)) return false;
        if (st.Isolated || st.Isolation != IsolationPhase.None) return false;
        if (st.Incapacitated || st.Faint != FaintPhase.None) return false;
        if (!string.IsNullOrEmpty(st.CarryingVictimId)) return false;
        if (!string.IsNullOrEmpty(st.ResponderId)) return false;
        if (st.IsMoving || st.BlockedByBarrier) return false;
        if (!string.IsNullOrEmpty(st.TargetRoomId) && st.TargetRoomId != st.CurrentRoomId) return false;
        if (sim.IsPanicked(id)) return false;
        if (st.WorkBlockedUntil > (GameState.Instance?.DayTimeSeconds ?? 0f)) return false;

        room = st.CurrentRoomId ?? "";
        if (string.IsNullOrEmpty(room)) return false;
        if (room is "isolation_room" or "central_office" or "rest_room") return false;
        if (room == FacilitySimulation.DeployOriginRoomId) return false;
        // 같은 방에 괴물이 있거나 쓰러진 동료가 있으면 딴짓할 상황이 아니다.
        if (sim.Ghost is { Active: true } && sim.Ghost.ActiveRoomId == room) return false;
        if (sim.Rescue?.FindUnmovedVictimIn(room) != null) return false;
        return true;
    }

    private float Weigh(FacilitySimulation sim, EmployeeState st, DistractionDef def, string room,
                        string watched, string selected, float watchBias, bool hurry)
    {
        string id = st.EmployeeId;
        if (CountToday(id) >= MaxEventsPerEmployeePerDay) return 0f;
        if (UsedToday(id, def.Kind)) return 0f;                       // 같은 딴짓 반복 금지
        if (!DistractionPlaces.Has(room, def.Place)) return 0f;       // 자리가 없는 방
        if (def.NeedsGoodMood && !DistractionMood.IsGood(st.DailyMood)) return 0f;
        if (!string.IsNullOrEmpty(def.PairedWith) && PartnerFor(sim, def) == null) return 0f;

        float w = Fairness(id);
        // 짝 통화는 상대까지 함께 걸리는 사건이다 — 상대 쪽 사정도 같이 본다.
        // (이것이 없으면 "어제 걸린 사람" 보정이 짝에게는 통하지 않아 같은 직원이 매일 걸린다.)
        if (!string.IsNullOrEmpty(def.PairedWith)) w *= Fairness(def.PairedWith);
        // 지금 보고 있는 방에서 일어나면 자연스럽게 걸린다. 막 채널을 맞춘 방도 조금 좋게 본다
        // (자리를 잡는 데 걸리는 몇 초 사이에 관리자가 그 방을 보고 있을 가능성이 높다).
        if (!string.IsNullOrEmpty(watched) && room == watched) w *= watchBias;
        else if (!string.IsNullOrEmpty(selected) && room == selected) w *= watchBias * 0.6f;
        else if (sim.IsRoomCctvBlocked(room)) w *= 0.3f;              // 봐도 안 보이는 방
        // 목격이 모자랄 때는 **자리를 옮기지 않는** 딴짓을 조금 더 자주 고른다 —
        // 걸어가는 시간이 없어 더 빨리 화면에 드러난다. 보지 않은 것을 봤다고 하지는 않는다.
        if (hurry && def.Place == DistractionPlace.AtSpot) w *= 1.6f;
        return w;
    }

    // 같은 사람이 매일 걸리지 않게 하는 보정 — 어제 걸렸거나 오늘 이미 한 번 했으면 덜 뽑는다.
    private float Fairness(string id)
    {
        float w = 1f;
        if (_witnessedYesterday.Contains(id)) w *= 0.45f;
        if (CountToday(id) >= 1) w *= 0.4f;
        return w;
    }

    // 짝 통화의 상대 — 둘 다 조건이 맞고 **다른 방**에 있어야 한다(같은 방이면 마주 보고 말하면 된다).
    private DistractionDef PartnerFor(FacilitySimulation sim, DistractionDef def)
    {
        if (!Eligible(sim, def.PairedWith, out var pst, out string proom)) return null;
        if (CountToday(def.PairedWith) >= MaxEventsPerEmployeePerDay) return null;
        var mine = sim.GetEmployeeState(def.EmployeeId);
        if (mine == null || proom == mine.CurrentRoomId) return null;
        foreach (var other in DistractionCatalog.For(def.PairedWith))
        {
            if (!DistractionCatalog.IsPersonalCall(other.Kind)) continue;
            if (UsedToday(def.PairedWith, other.Kind)) return null;
            if (!DistractionPlaces.Has(proom, other.Place)) return null;
            return other;
        }
        return null;
    }

    private bool Start(FacilitySimulation sim, DistractionDef def, string room, float now)
    {
        // 개인 통화는 두 사람이 **하나의 사건**을 공유한다 — 상대를 먼저 확인하고,
        // 둘을 같은 시각에 함께 만든다(한쪽만 통화 중인 모순이 생기지 않게).
        DistractionDef partnerDef = null;
        string proom = "";
        if (!string.IsNullOrEmpty(def.PairedWith))
        {
            partnerDef = PartnerFor(sim, def);
            proom = sim.GetEmployeeState(def.PairedWith)?.CurrentRoomId ?? "";
            if (partnerDef == null || string.IsNullOrEmpty(proom)) return false;
        }

        var e = Create(sim, def, room, now);
        if (partnerDef == null) return true;

        var call = new PersonalCallEvent
        {
            EventId = $"C{_day}-{++_eventNo}",
            Day = _day, AId = def.EmployeeId, BId = def.PairedWith, StartedAt = now,
        };
        _calls.Add(call);
        e.LinkedPhoneCallEventId = call.EventId;
        var pe = Create(sim, partnerDef, proom, now);
        pe.LinkedPhoneCallEventId = call.EventId;
        return true;
    }

    private DistractionEvent Create(FacilitySimulation sim, DistractionDef def, string room, float now)
    {
        var e = new DistractionEvent
        {
            EventId = $"D{_day}-{++_eventNo}",
            EmployeeId = def.EmployeeId,
            Kind = def.Kind,
            Day = _day,
            RoomId = room,
            RoomName = sim.RoomDisplayName(room),
            StartedAt = now,
            Phase = DistractionPhase.Entering,
            DistractLimit = _rng.RandfRange(def.DistractMinSeconds, def.DistractMaxSeconds),
            Serial = ++_serial,
        };
        _events.Add(e);
        _active[def.EmployeeId] = e;
        StartedToday++;
        return e;
    }

    // 지금도 계속해도 되는 상황인가(지시서 §13 우선순위). 계속해도 되면 null.
    private static string StopReason(FacilitySimulation sim, DistractionEvent e)
    {
        var st = sim.GetEmployeeState(e.EmployeeId);
        if (st == null || !st.Alive) return "사망";
        if (st.Isolated || st.Isolation != IsolationPhase.None) return "격리";
        if (st.Incapacitated || st.Faint != FaintPhase.None) return "기절";
        if (!string.IsNullOrEmpty(st.CarryingVictimId)) return "환자 이송";
        if (!string.IsNullOrEmpty(st.ResponderId)) return "구조 대응";
        if (st.CurrentRoomId != e.RoomId) return "다른 방으로";
        if (st.IsMoving) return "이동";
        if (sim.IsPanicked(e.EmployeeId)) return "공황";
        if (sim.Ghost is { Active: true } && sim.Ghost.ActiveRoomId == e.RoomId) return "괴물";
        return null;
    }

    private int CountToday(string id)
    {
        int n = 0;
        foreach (var e in _events)
            if (e.Day == _day && e.EmployeeId == id) n++;
        return n;
    }

    private bool UsedToday(string id, DistractionKind kind)
    {
        foreach (var e in _events)
            if (e.Day == _day && e.EmployeeId == id && e.Kind == kind) return true;
        return false;
    }

    // ── 검증·디버그 전용 ─────────────────────────────────────────────────

    // 프리뷰·자동 테스트에서 특정 딴짓을 지금 일으킨다. 게임 플레이 중에는 쓰지 않는다.
    public DistractionEvent ForceStart(FacilitySimulation sim, DistractionKind kind)
    {
        var def = DistractionCatalog.Get(kind);
        if (def == null || sim == null) return null;
        if (!Eligible(sim, def.EmployeeId, out _, out string room)) return null;
        if (_active.ContainsKey(def.EmployeeId)) return null;
        float now = GameState.Instance?.DayTimeSeconds ?? 0f;
        return Start(sim, def, room, now) ? ActiveOf(def.EmployeeId) : null;
    }

    // 다음 기회를 지금으로 당긴다(테스트에서 기다리지 않기 위해).
    public void RushNextChance() => _nextChanceAt = 0f;
}

// 기분이 "분명히 좋은" 날인가 — 스트레스가 낮다는 이유만으로 좋다고 보지 않는다(지시서 §7).
// 표현은 전부 data/moods/*.tres 의 실제 문구다.
public static class DistractionMood
{
    private static readonly HashSet<string> Good = new()
    {
        "기분 좋음", "기대됨", "활기참", "즐거움", "꽤 좋음", "의욕적임", "여유로움", "편안함",
    };

    public static bool IsGood(string dailyMood) =>
        !string.IsNullOrEmpty(dailyMood) && Good.Contains(dailyMood.Trim());
}
