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
    public static int ActiveCount => _active.Count;

    // 마지막으로 사고가 하나 끝난(또는 시작된) 시각 — DAY1 동시 사고 제한에 쓴다.
    public static float LastIncidentAt { get; private set; } = -999f;

    // 사고 발생. 같은 방에 이미 열린 사고가 있으면 새로 만들지 않는다.
    public static IncidentDisplayData Open(string roomId, string title, string cause,
        string actionHint, int repairWorkers)
    {
        var found = _active.FirstOrDefault(x => x.RoomId == roomId);
        if (found != null) return found;

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
        return data;
    }

    // 원인을 알 수 없는 이상(방해공작 등). 범인 정보는 절대 넘기지 않는다.
    public static void Anomaly(string roomId, string title, string consequence)
    {
        var found = _active.FirstOrDefault(x => x.RoomId == roomId);
        if (found != null)
        {
            AddLine(found, consequence);
            return;
        }
        var data = new IncidentDisplayData
        {
            IncidentId = $"inc{++_serial}",
            RoomId = roomId ?? "",
            Title = string.IsNullOrEmpty(title) ? "비정상 동작 감지" : title,
            State = IncidentState.Active,
            CauseText = "판별 불가",
            ActionHint = "현장 확인 필요",
            Severity = AlertSeverity.Warning,
            StartedAt = Now,
        };
        AddLine(data, consequence);
        _active.Add(data);
    }

    // 사고의 결과로 시설 기능이 하나 더 나갔다. 같은 사고의 파생이면 줄만 붙는다.
    public static void AddConsequence(string roomId, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        // 그 방의 사고가 우선. 없으면 가장 최근에 열린 사고에 붙인다(전력 손실 같은 전역 결과).
        var target = _active.FirstOrDefault(x => x.RoomId == roomId)
                     ?? _active.OrderByDescending(x => x.StartedAt).FirstOrDefault();
        if (target == null) return;
        AddLine(target, line);
    }

    // 해당 방의 사고가 해결됐다.
    public static void Resolve(string roomId)
    {
        var found = _active.FirstOrDefault(x => x.RoomId == roomId);
        if (found == null) return;
        _active.Remove(found);
        found.State = IncidentState.Resolved;
        found.ResolvedAt = Now;
        found.Severity = AlertSeverity.Notice;
        _recent.Insert(0, found);
        while (_recent.Count > RecentKeep) _recent.RemoveAt(_recent.Count - 1);
        LastIncidentAt = Now;
    }

    public static bool HasActive(string roomId) => _active.Any(x => x.RoomId == roomId);

    public static void Reset()
    {
        _active.Clear();
        _recent.Clear();
        _serial = 0;
        LastIncidentAt = -999f;
    }

    private static void AddLine(IncidentDisplayData data, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        string trimmed = line.Trim();
        if (!data.ConsequenceLines.Contains(trimmed)) data.ConsequenceLines.Add(trimmed);
    }

    private static float Now => GameState.Instance?.DayTimeSeconds ?? 0f;
}
