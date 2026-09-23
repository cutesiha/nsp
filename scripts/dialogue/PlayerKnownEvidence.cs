using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;

namespace NSP.Dialogue;

// "플레이어가 실제로 확보한 사실"만 모아 둔다.
//
// 게임 내부(EventLog)는 방해자의 진짜 위치와 행동을 전부 알고 있지만, 그 진실로
// 추궁 질문을 만들면 안 된다. 강한 추궁(Challenge*)은 반드시 여기 있는 것만 근거로 쓴다.
//
//   SystemTruth          = EventLog 원본            (여기 없음)
//   EmployeeKnownFacts   = DialogueContextBuilder   (직원이 아는 것)
//   PlayerKnownEvidence  = 이 클래스                (관리자가 아는 것)
//
// 채워지는 경로는 두 가지뿐이다.
//   1) 직원이 관리자에게 직접 말한 진술 — LocalDialogueGenerator 가 답변을 만들 때 기록
//   2) 시설 로그 화면에 실제로 표시된 줄 — FacilityLogFormatter 결과에서 읽음
public static class PlayerKnownEvidence
{
    // 어떤 직원이 "사건 당시 나는 여기 있었다"고 말한 내용.
    public sealed class LocationStatement
    {
        // 어느 근무의 자료인가. 휴게시간 조사 자료는 항상 오늘 것만 본다.
        public int Day = 1;
        public string SpeakerId = "";
        public string IncidentKey = "";
        public string RoomId = "";
        public bool StatedExactTime;
        // 이 진술이 가리키는 시각(근무 시작 = 0초). 모순 판정은 이 값이 있어야만 성립한다.
        public float AnchorTime;
        public bool HasTime;
    }

    // 어떤 직원이 "그때 나는 이런 행동은 하지 않았다"고 말한 내용.
    //
    // 지금은 결번자의 "설비 근처에 가지 않았다" 하나뿐이다. 위치 진술과 따로 두는 이유는
    // 이것이 **행동**에 대한 주장이라 동료의 목격 증언과 맞대어지기 때문이다(행동 추궁).
    public sealed class BehaviorClaim
    {
        public int Day = 1;
        public string SpeakerId = "";
        public string IncidentKey = "";
        public string Text = "";
        public float AnchorTime;
        public bool HasTime;
    }

    // 어떤 직원이 "그 사람을 여기서 봤다"고 말한 내용.
    public sealed class SightingStatement
    {
        public int Day = 1;
        public string SpeakerId = "";
        public string SubjectId = "";
        public string RoomId = "";
        public float AnchorTime;
        public bool HasTime;
        // 그때 무엇을 하고 있었는지까지 들었을 때만 채워진다("설비 쪽에 평소보다 오래 머물렀다").
        // 방 이름만 남기면 이 게임에서 가장 중요한 단서의 알맹이가 빠진다.
        public string Detail = "";
    }

    // 시설 로그 화면에 실제로 떴던 이동 한 건.
    public sealed class VisibleMove
    {
        public string EmployeeId = "";
        public string FromRoomId = "";
        public string ToRoomId = "";
        public bool PlayerOrdered;
        public float Timestamp;
    }

    // 관리자가 CCTV로 한 작업실을 3초 이상 계속 지켜본 한 건. 그때 그 방에 누가 있었는지가
    // 곧 "관리자가 직접 눈으로 본 사실"이 된다.
    public sealed class CctvObservation
    {
        public int Day = 1;
        public string RoomId = "";
        public float Time;
        public List<string> Occupants = new();
        // 그 화면을 볼 때 그 방에서 누군가 설비 쪽에 붙어 있었는가(화면에 그렇게 떴는가).
        // 누가 그랬는지는 남기지 않는다 — 관리자도 화면에서는 "그 방에 그런 움직임이 있었다"까지만 본다.
        public bool SuspiciousAction;
    }

    private static readonly List<LocationStatement> _locations = new();
    private static readonly List<SightingStatement> _sightings = new();
    private static readonly List<BehaviorClaim> _behaviors = new();
    private static readonly List<CctvObservation> _cctv = new();
    // 플레이어가 손으로 찍어 둔 「중요」 표시. 게임이 판정하지 않는다 — 순전히 메모다.
    private static readonly HashSet<string> _starred = new();

    // 지금 보고 있는 근무. 지난 DAY 의 자료는 조사 자료에 섞이지 않는다.
    private static int Today => GameState.Instance?.CurrentDay ?? 1;

    // --- 중요 표시 ------------------------------------------------------

    public static bool IsStarred(string evidenceId) =>
        !string.IsNullOrEmpty(evidenceId) && _starred.Contains(evidenceId);

    public static void ToggleStar(string evidenceId)
    {
        if (string.IsNullOrEmpty(evidenceId)) return;
        if (!_starred.Remove(evidenceId)) _starred.Add(evidenceId);
    }

    public static int StarredCount => _starred.Count;

    // --- 진술 기록 ------------------------------------------------------

    public static void RecordLocationStatement(string speakerId, string incidentKey, string roomId, bool exactTime,
        float anchorTime = -1f)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(roomId)) return;
        var found = _locations.FirstOrDefault(x => x.Day == Today
            && x.SpeakerId == speakerId && x.IncidentKey == incidentKey);
        if (found != null)
        {
            found.RoomId = roomId;
            found.StatedExactTime |= exactTime;
            if (anchorTime >= 0f) { found.AnchorTime = anchorTime; found.HasTime = true; }
            return;
        }
        _locations.Add(new LocationStatement
        {
            Day = Today,
            SpeakerId = speakerId, IncidentKey = incidentKey ?? "", RoomId = roomId, StatedExactTime = exactTime,
            AnchorTime = Mathf.Max(0f, anchorTime), HasTime = anchorTime >= 0f,
        });
    }

    // 직원이 관리자에게 "그런 행동은 하지 않았다"고 말한 것을 남긴다.
    //
    // 이 클래스의 규칙 그대로 — **플레이어가 실제로 들은 말만** 들어온다. 그래서 호출부는
    // 그 문장이 실제로 답변에 실려 나갈 때 한 번만 부른다. 같은 사건에 대해 두 번 말해도
    // 자료는 한 장이다(진술이 늘어나는 것이 아니라 같은 주장을 반복한 것이므로).
    public static void RecordBehaviorClaim(string speakerId, string incidentKey, string text,
        float anchorTime = -1f)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(text)) return;
        var found = _behaviors.FirstOrDefault(x => x.Day == Today
            && x.SpeakerId == speakerId && x.IncidentKey == (incidentKey ?? "") && x.Text == text);
        if (found != null)
        {
            if (anchorTime >= 0f) { found.AnchorTime = anchorTime; found.HasTime = true; }
            return;
        }
        _behaviors.Add(new BehaviorClaim
        {
            Day = Today,
            SpeakerId = speakerId, IncidentKey = incidentKey ?? "", Text = text,
            AnchorTime = Mathf.Max(0f, anchorTime), HasTime = anchorTime >= 0f,
        });
    }

    // 이 직원이 관리자에게 한 행동 주장 전부.
    public static IReadOnlyList<BehaviorClaim> BehaviorClaimsBy(string employeeId) =>
        _behaviors.Where(x => x.Day == Today && x.SpeakerId == employeeId).ToList();

    // CCTV 시청 기록. FacilitySimulation 이 3초 연속 시청마다 한 번씩 호출한다.
    public static void RecordCctvObservation(string roomId, float time, IEnumerable<string> occupants,
        bool suspiciousAction = false)
    {
        if (string.IsNullOrEmpty(roomId)) return;

        // 같은 방을 계속 보고 있으면 3초마다 같은 기록이 쌓여 조사 자료가 넘쳐난다.
        // 인원 구성이 바뀔 때만 새 기록을 남긴다 — "혼자 있었다" 한 줄, 그 방에 누가
        // 더 들어온 순간 한 줄. 플레이어가 본 장면의 수가 아니라 장면의 종류를 남긴다.
        var now = occupants != null ? new List<string>(occupants) : new List<string>();
        now.Sort(System.StringComparer.Ordinal);
        var last = _cctv.LastOrDefault(o => o.Day == Today && o.RoomId == roomId);
        if (last != null && SameCrew(last.Occupants, now)) return;

        _cctv.Add(new CctvObservation
        {
            Day = Today,
            RoomId = roomId,
            Time = time,
            Occupants = occupants != null ? new List<string>(occupants) : new List<string>(),
            SuspiciousAction = suspiciousAction,
        });
        // 하루치가 계속 쌓이지 않게 상한만 둔다.
        while (_cctv.Count > 200) _cctv.RemoveAt(0);
    }

    // 그 시각 언저리에 CCTV로 이 직원을 실제로 본 작업실. 없으면 빈 값.
    public static string CctvSeenRoomOf(string employeeId, float aroundTime, float window)
    {
        foreach (var o in _cctv)
            if (o.Day == Today && Mathf.Abs(o.Time - aroundTime) <= window
                && o.Occupants.Contains(employeeId))
                return o.RoomId;
        return "";
    }

    // 그 시각 언저리에 그 방을 지켜봤는데 이 직원이 거기 없었는가.
    public static bool CctvWatchedWithout(string employeeId, string roomId, float aroundTime, float window)
    {
        if (string.IsNullOrEmpty(roomId)) return false;
        foreach (var o in _cctv)
            if (o.Day == Today && o.RoomId == roomId && Mathf.Abs(o.Time - aroundTime) <= window
                && !o.Occupants.Contains(employeeId))
                return true;
        return false;
    }

    public static int CctvObservationCount => _cctv.Count;

    private static bool SameCrew(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        var sorted = new List<string>(a);
        sorted.Sort(System.StringComparer.Ordinal);
        for (int i = 0; i < sorted.Count; i++)
            if (sorted[i] != b[i]) return false;
        return true;
    }

    // 그 작업실의 위치 기록(CCTV 시청 또는 경비 순찰)이 하나라도 남았는가.
    public static bool HasRoomRecord(string roomId) =>
        !string.IsNullOrEmpty(roomId) && _cctv.Any(o => o.Day == Today && o.RoomId == roomId);

    // detail 은 "그 사람이 그때 무엇을 하고 있었는가". 들은 적이 없으면 빈 값 그대로 둔다 —
    // 이 클래스의 규칙은 여전히 "플레이어가 실제로 들은 것만 남긴다" 다.
    public static void RecordSighting(string speakerId, string subjectId, string roomId, float anchorTime = -1f,
        string detail = "")
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(subjectId)) return;
        var found = _sightings.FirstOrDefault(x => x.Day == Today && x.SpeakerId == speakerId
            && x.SubjectId == subjectId && x.RoomId == roomId);
        if (found != null)
        {
            // 같은 목격을 다시 들었을 때 내용이 더 자세해졌다면 그것만 채워 넣는다.
            if (string.IsNullOrEmpty(found.Detail) && !string.IsNullOrEmpty(detail)) found.Detail = detail;
            return;
        }
        _sightings.Add(new SightingStatement
        {
            Day = Today,
            SpeakerId = speakerId, SubjectId = subjectId, RoomId = roomId ?? "",
            AnchorTime = Mathf.Max(0f, anchorTime), HasTime = anchorTime >= 0f,
            Detail = detail ?? "",
        });
    }

    // --- 조회 ------------------------------------------------------------

    // 이 직원이 그 사건에 대해 스스로 말한 위치.
    public static LocationStatement OwnClaim(string employeeId, string incidentKey) =>
        _locations.FirstOrDefault(x => x.Day == Today
            && x.SpeakerId == employeeId && x.IncidentKey == incidentKey);

    // 다른 직원이 이 직원을 봤다고 말한 기록.
    public static IEnumerable<SightingStatement> SightingsOf(string employeeId) =>
        _sightings.Where(x => x.Day == Today && x.SubjectId == employeeId && x.SpeakerId != employeeId);

    // 이 직원이 다른 직원을 봤다고 말한 기록.
    public static IEnumerable<SightingStatement> SightingsBy(string employeeId) =>
        _sightings.Where(x => x.Day == Today && x.SpeakerId == employeeId);

    // 시설 로그 화면에 실제로 떴던 이 직원의 이동. 화면에 뜨지 않은 이동은 여기 없다.
    public static List<VisibleMove> VisibleMoves(string employeeId, int day)
    {
        var rows = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(), day);
        return rows
            .Where(r => r.RelatedEmployeeId == employeeId
                        && !string.IsNullOrEmpty(r.FromRoomId) && !string.IsNullOrEmpty(r.ToRoomId))
            .Select(r => new VisibleMove
            {
                EmployeeId = employeeId,
                FromRoomId = r.FromRoomId,
                ToRoomId = r.ToRoomId,
                PlayerOrdered = r.PlayerOrdered,
                Timestamp = r.Timestamp,
            })
            .ToList();
    }

    // 이 직원이 관리자에게 한 모든 위치 진술(최근 순).
    public static IReadOnlyList<LocationStatement> StatementsBy(string employeeId) =>
        _locations.Where(x => x.Day == Today && x.SpeakerId == employeeId).ToList();

    // 관리자가 CCTV 로 이 직원을 실제로 본 모든 순간(최근 순).
    public static IReadOnlyList<CctvObservation> CctvSightingsOf(string employeeId) =>
        _cctv.Where(o => o.Day == Today && o.Occupants.Contains(employeeId)).ToList();

    public static void ResetAll()
    {
        _locations.Clear();
        _sightings.Clear();
        _behaviors.Clear();
        _cctv.Clear();
        _starred.Clear();
    }
}
