using System.Collections.Generic;
using System.Linq;

namespace NSP.Core;

// 실제로 "발생한" 사고를 해결될 때까지 들고 있는다.
//
// 시뮬레이션(FacilitySimulation / TabooRuleSystem)이 사고를 일으킬 때 여기에 알려 주고,
// 하나의 사고 때문에 뒤따라 망가진 시설 기능은 그 사고의 "결과 줄"로 붙는다.
// 그래서 발전실 사고 하나가 전력·CCTV·조명까지 끊어도 사고 4건으로 보이지 않는다.
//
// 판정은 하지 않는다 — 이 클래스가 없어도 게임 로직은 그대로 돈다.
public static class IncidentTracker
{
    private const int RecentKeep = 5;

    private static readonly List<IncidentDisplayData> _active = new();
    private static readonly List<IncidentDisplayData> _recent = new();
    private static int _serial;

    public static IReadOnlyList<IncidentDisplayData> Active => _active;
    public static IReadOnlyList<IncidentDisplayData> Recent => _recent;
    // 실제 사고만 센다. 원인 불명 이상(Anomaly)은 수리할 대상이 없는 알림이라 "사고 중" 이 아니다 —
    // 이걸 같이 세면 DAY1~3 의 동시 사고 제한(CanStartNewIncident)이 근무 끝까지 막혀 버린다.
    public static int ActiveCount => _active.Count(x => !x.IsAnomaly);

    // 원인 불명 이상이 경고 단말기에 머무는 시간(초). 이 뒤에는 저절로 내려간다 —
    // 고칠 업무가 없으므로 Resolve 가 불리지 않는다(예전에는 근무 끝까지 코어실이 빨갛게 남았다).
    private const float AnomalyHoldSeconds = 15f;

    // 마지막으로 사고가 하나 끝난(또는 시작된) 시각 — DAY1 동시 사고 제한에 쓴다.
    public static float LastIncidentAt { get; private set; } = -999f;

    // 이번 근무에서 실제로 '발생한' 사고 건수(정산 표시와 검증용).
    // 사고의 파생 결과나 주기적 손실 로그는 여기 들어오지 않는다.
    public static int OpenedCount { get; private set; }
    // 그중 수리를 마쳐 해결된 건수. 오늘의 업무("경고 또는 고장 N회 해결")가 읽는다.
    public static int ResolvedCount { get; private set; }

    // 사고 발생. 같은 방에 이미 열린 사고가 있으면 새로 만들지 않는다.
    public static IncidentDisplayData Open(string roomId, string title, string cause,
        string actionHint, int repairWorkers)
    {
        var found = _active.FirstOrDefault(x => x.RoomId == roomId);
        if (found is { IsAnomaly: false }) return found;
        // 같은 방에 원인 불명 이상만 떠 있었다 — 그 자리를 진짜 사고로 바꾼다(결과 줄은 이어 받는다).
        if (found != null)
        {
            found.IsAnomaly = false;
            found.Title = string.IsNullOrEmpty(title) ? "시설 고장" : title;
            found.State = IncidentState.Active;
            found.CauseText = cause;
            found.ActionHint = string.IsNullOrEmpty(actionHint) ? "설비 수리 필요" : actionHint;
            found.Severity = AlertSeverity.Critical;
            found.StartedAt = Now;
            found.RepairWorkers = System.Math.Max(1, repairWorkers);
            LastIncidentAt = Now;
            OpenedCount++;
            return found;
        }

        var data = new IncidentDisplayData
        {
            IncidentId = $"inc{++_serial}",
            RoomId = roomId ?? "",
            Title = string.IsNullOrEmpty(title) ? "시설 고장" : title,
            State = IncidentState.Active,
            CauseText = cause,
            ActionHint = string.IsNullOrEmpty(actionHint) ? "설비 수리 필요" : actionHint,
            Severity = AlertSeverity.Critical,
            StartedAt = Now,
            RepairWorkers = System.Math.Max(1, repairWorkers),
        };
        _active.Add(data);
        LastIncidentAt = Now;
        OpenedCount++;
        return data;
    }

    // 원인을 알 수 없는 이상(방해공작 등). 범인 정보는 절대 넘기지 않는다.
    //
    // 고장이 아니라 **알림**이다 — 주의(Caution) 단계로만 뜨고(지도에서 방을 빨갛게 칠하지 않는다)
    // AnomalyHoldSeconds 뒤에 저절로 내려간다. 그 방에 진짜 사고가 나면 Open 이 이 자리를 넘겨받는다.
    public static void Anomaly(string roomId, string title, string consequence)
    {
        var found = _active.FirstOrDefault(x => x.RoomId == roomId);
        if (found != null)
        {
            AddLine(found, consequence);
            if (found.IsAnomaly) found.StartedAt = Now;   // 같은 이상이 또 났다 — 머무는 시간을 다시 센다
            return;
        }
        var data = new IncidentDisplayData
        {
            IncidentId = $"inc{++_serial}",
            RoomId = roomId ?? "",
            Title = string.IsNullOrEmpty(title) ? "비정상 동작 감지" : title,
            State = IncidentState.Caution,
            CauseText = "판별 불가",
            ActionHint = "현장 확인 필요",
            Severity = AlertSeverity.Warning,
            StartedAt = Now,
            IsAnomaly = true,
        };
        AddLine(data, consequence);
        _active.Add(data);
    }

    // 사고의 결과로 시설 기능이 하나 더 나갔다. 같은 사고의 파생이면 줄만 붙는다.
    public static void AddConsequence(string roomId, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        // 그 방의 사고가 우선. 없으면 가장 최근에 열린 사고에 붙인다(전력 손실 같은 전역 결과).
        var target = _active.FirstOrDefault(x => x.RoomId == roomId && !x.IsAnomaly)
                     ?? _active.FirstOrDefault(x => x.RoomId == roomId)
                     ?? _active.Where(x => !x.IsAnomaly).OrderByDescending(x => x.StartedAt).FirstOrDefault();
        if (target == null) return;
        AddLine(target, line);
    }

    // 해당 방의 사고가 해결됐다.
    public static void Resolve(string roomId)
    {
        // 고쳤으면 그 방에 걸려 있던 원인 불명 알림도 같이 내린다.
        _active.RemoveAll(x => x.RoomId == roomId && x.IsAnomaly);
        var found = _active.FirstOrDefault(x => x.RoomId == roomId);
        if (found == null) return;
        _active.Remove(found);
        found.State = IncidentState.Resolved;
        found.ResolvedAt = Now;
        found.Severity = AlertSeverity.Notice;
        _recent.Insert(0, found);
        while (_recent.Count > RecentKeep) _recent.RemoveAt(_recent.Count - 1);
        LastIncidentAt = Now;
        ResolvedCount++;
    }

    public static bool HasActive(string roomId) => _active.Any(x => x.RoomId == roomId && !x.IsAnomaly);

    // 머무는 시간이 지난 원인 불명 알림을 내린다. FacilitySimulation.Tick 이 매 틱 부른다.
    public static void Prune()
    {
        float now = Now;
        _active.RemoveAll(x => x.IsAnomaly && now - x.StartedAt >= AnomalyHoldSeconds);
    }

    public static void Reset()
    {
        _active.Clear();
        _recent.Clear();
        _serial = 0;
        LastIncidentAt = -999f;
        OpenedCount = 0;
        ResolvedCount = 0;
    }

    private static void AddLine(IncidentDisplayData data, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        string trimmed = line.Trim();
        if (!data.ConsequenceLines.Contains(trimmed)) data.ConsequenceLines.Add(trimmed);
    }

    private static float Now => GameState.Instance?.DayTimeSeconds ?? 0f;
}
