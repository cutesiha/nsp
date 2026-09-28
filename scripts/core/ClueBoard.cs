using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Dialogue;

namespace NSP.Core;

// 관리자가 "수상하다"고 직접 찍어 둔 단서 — 관리자 패드의 단서 탭에 쌓이는 소지품.
//
// 자료를 **만드는** 곳은 InterviewEvidenceBoard 하나다. 여기서는 새 자료를 만들지 않고,
// 플레이어가 고른 자료를 들고 있기만 한다. 게임이 중요도를 정하지 않는다 — 순전히 메모다.
//
// 다만 자료의 원천(시설 로그 · 진술 · 시청 기록)은 근무가 바뀔 때마다 비워진다. 그래서
// 핀을 찍는 순간의 자료를 **동결 사본**으로 함께 보관한다. 원본이 사라지는 것을 대신
// 붙잡아 두는 것이지 같은 자료를 두 군데서 관리하는 것이 아니다 — 오늘 자료의 판정은
// 여전히 Board 가 만든 원본으로 한다.
//
// 키는 (Day, Id) 다. 자료 Id 는 그 날 안에서만 고유하다.
// 한 판(GameState.ResetRun) 동안 남고, 교육일(DAY0)에 찍은 것은 실제 근무로 넘어오지 않는다.
public static class ClueBoard
{
    public sealed class Entry
    {
        public int Day;
        public string EvidenceId = "";
        // 핀을 찍은 순간의 자료(사본). 오늘 자료는 Refresh 로 더 자세해질 수 있다.
        public InterviewEvidence Evidence;
        // 찍은 차례(1부터). 같은 시각에 여러 장을 찍어도 순서가 갈리게 한다.
        public int Seq;
        // 찍은 때 — 근무 시계(초)와 단계(근무 중 / 휴게시간).
        public float PinnedAtDayTime;
        public GamePhase PinnedPhase;
        // 사건 순간의 CCTV 한 컷. 없으면 null — 화면은 NO SIGNAL 로 대신한다.
        public Texture2D Snapshot;
    }

    // (단서, 찍었는가). 찍으면 true, 풀면 false. 화면 반응(소리 · 토스트 · 뱃지)은 받는 쪽 몫이다.
    public static event Action<Entry, bool> Changed;

    private static readonly List<Entry> _entries = new();
    private static int _seq;

    // 사건 순간에 찍어 둔 CCTV 한 컷 — 아직 아무도 핀을 찍지 않은 사고의 것.
    // 나중에 그 사고를 찍으면 이 컷이 따라 붙는다. 한 판 동안 오래된 것부터 버린다.
    private const int MaxPendingSnapshots = 40;
    private static readonly List<(int Day, string Id, Texture2D Tex)> _pendingSnapshots = new();

    public static IReadOnlyList<Entry> Entries => _entries;
    public static int Count => _entries.Count;

    // 관리자 패드를 마지막으로 연 뒤 새로 찍은 수 — 패드 뱃지.
    public static int UnseenCount { get; private set; }

    // --- 조회 ------------------------------------------------------------

    public static Entry Find(int day, string evidenceId) =>
        string.IsNullOrEmpty(evidenceId) ? null
            : _entries.FirstOrDefault(e => e.Day == day && e.EvidenceId == evidenceId);

    public static bool IsPinned(int day, string evidenceId) => Find(day, evidenceId) != null;

    public static bool IsPinned(InterviewEvidence ev) => ev != null && IsPinned(ev.Day, ev.Id);

    // 이 직원이 등장하는 단서 수(그 사람의 자료이거나, 함께 있던 사람으로 올라 있거나).
    public static int CountFor(string employeeId) =>
        string.IsNullOrEmpty(employeeId) ? 0 : _entries.Count(e => Involves(e, employeeId));

    public static bool Involves(Entry e, string employeeId)
    {
        var ev = e?.Evidence;
        if (ev == null || string.IsNullOrEmpty(employeeId)) return false;
        return ev.SubjectEmployeeId == employeeId || ev.SpeakerEmployeeId == employeeId
               || ev.RelatedEmployeeIds.Contains(employeeId);
    }

    // --- 찍기 / 풀기 -------------------------------------------------------

    // 찍는다. 이미 찍혀 있으면 아무것도 하지 않고 false.
    public static bool Pin(InterviewEvidence ev)
    {
        if (ev == null || string.IsNullOrEmpty(ev.Id) || IsPinned(ev)) return false;
        var entry = new Entry
        {
            Day = ev.Day,
            EvidenceId = ev.Id,
            Evidence = ev.Clone(),
            Seq = ++_seq,
            PinnedAtDayTime = GameState.Instance?.DayTimeSeconds ?? 0f,
            PinnedPhase = GameState.Instance?.CurrentPhase ?? GamePhase.Live,
            Snapshot = PendingSnapshot(ev.Day, ev.Id),
        };
        _entries.Add(entry);
        UnseenCount++;
        Changed?.Invoke(entry, true);
        return true;
    }

    public static bool Unpin(int day, string evidenceId)
    {
        var entry = Find(day, evidenceId);
        if (entry == null) return false;
        _entries.Remove(entry);
        Changed?.Invoke(entry, false);
        return true;
    }

    // 찍혀 있으면 풀고, 아니면 찍는다. 결과(지금 찍혀 있는가)를 돌려준다.
    public static bool Toggle(InterviewEvidence ev)
    {
        if (ev == null) return false;
        if (Unpin(ev.Day, ev.Id)) return false;
        return Pin(ev);
    }

    // 찍어 둔 자료의 내용이 더 자세해졌다(예: 엿들은 대화의 둘째 줄이 막 들렸다).
    // 찍혀 있지 않으면 아무것도 하지 않는다 — 여기서 새로 찍지 않는다.
    public static void Refresh(InterviewEvidence ev)
    {
        var entry = ev == null ? null : Find(ev.Day, ev.Id);
        if (entry != null) entry.Evidence = ev.Clone();
    }

    // 사건 순간의 CCTV 한 컷. 그 사고가 이미 찍혀 있으면 바로 붙이고, 아니면 기다렸다가
    // 찍히는 순간 붙인다. 관리자가 보고 있던 방의 것만 들어온다(CctvSnapshotRecorder).
    public static void OfferSnapshot(int day, string evidenceId, Texture2D snapshot)
    {
        if (string.IsNullOrEmpty(evidenceId) || snapshot == null) return;
        var entry = Find(day, evidenceId);
        if (entry != null) { entry.Snapshot = snapshot; return; }
        _pendingSnapshots.RemoveAll(p => p.Day == day && p.Id == evidenceId);
        _pendingSnapshots.Add((day, evidenceId, snapshot));
        while (_pendingSnapshots.Count > MaxPendingSnapshots) _pendingSnapshots.RemoveAt(0);
    }

    // 이 자료에 찍혀 있는 CCTV 한 컷(단서로 찍었든 아니든). 없으면 null.
    public static Texture2D SnapshotOf(int day, string evidenceId) =>
        Find(day, evidenceId)?.Snapshot ?? PendingSnapshot(day, evidenceId);

    private static Texture2D PendingSnapshot(int day, string evidenceId)
    {
        foreach (var p in _pendingSnapshots)
            if (p.Day == day && p.Id == evidenceId) return p.Tex;
        return null;
    }

    // 관리자 패드를 열어 새 단서를 확인했다.
    public static void MarkSeen() => UnseenCount = 0;

    // 새 게임 · 교육일 종료. 한 판 안에서는 근무가 바뀌어도 부르지 않는다.
    public static void ResetAll()
    {
        _entries.Clear();
        _pendingSnapshots.Clear();
        _seq = 0;
        UnseenCount = 0;
    }
}
