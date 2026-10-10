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
// 채워지는 경로는 네 가지뿐이다.
//   1) 직원이 관리자에게 직접 말한 진술 — LocalDialogueGenerator 가 답변을 만들 때 기록
//   2) 시설 로그 화면에 실제로 표시된 줄 — FacilityLogFormatter 결과에서 읽음
//   3) 관리자가 CCTV 로 지켜본 장면 — FacilitySimulation 이 기록
//   4) CCTV 오디오로 엿들은 대화 — 자막이 실제로 화면에 뜬 것만(CctvOverheardCaption)
//
// 플레이어가 손으로 찍어 둔 단서(★)는 여기 없다 — 며칠에 걸쳐 남는 소지품이라
// ClueBoard 가 따로 들고 있는다.
public static class PlayerKnownEvidence
{
    // 어떤 직원이 "사건 당시 나는 여기 있었다"고 말한 내용.
    public sealed class LocationStatement
    {
        // **말한 날**. 휴게시간 조사 자료는 기본적으로 오늘 말한 것만 본다.
        public int Day = 1;
        // **그 말이 가리키는 날**. 어제 일을 오늘 말할 수 있으므로 Day 와 다를 수 있고,
        // 모순 판정은 Day 가 아니라 이 값을 증거의 날짜와 맞대어 본다.
        public int SubjectDay = 1;
        public string SpeakerId = "";
        public string IncidentKey = "";
        public string RoomId = "";
        public bool StatedExactTime;
        // 이 진술이 가리키는 시각(근무 시작 = 0초). 모순 판정은 이 값이 있어야만 성립한다.
        public float AnchorTime;
        public bool HasTime;

        // 처음 한 말부터 정정까지 **순서대로**. 마지막이 지금의 주장이다.
        //
        // 진술을 고치는 것은 가능하지만 **이전 진술을 지우는 것은 불가능해야** 한다
        // (기획안 §5). 덮어쓰면 "아까는 경비실이라고 하지 않았습니까" 를 물을 근거가 사라진다.
        public readonly List<string> RoomHistory = new();
        public bool WasCorrected => RoomHistory.Count > 1;
        public string FirstRoomId => RoomHistory.Count > 0 ? RoomHistory[0] : RoomId;
    }

    // 어떤 직원이 "그때 나는 이런 행동은 하지 않았다"고 말한 내용.
    //
    // 지금은 결번의 "설비 근처에 가지 않았다" 하나뿐이다. 위치 진술과 따로 두는 이유는
    // 이것이 **행동**에 대한 주장이라 동료의 목격 증언과 맞대어지기 때문이다(행동 추궁).
    public sealed class BehaviorClaim
    {
        public int Day = 1;          // 말한 날
        public int SubjectDay = 1;   // 그 말이 가리키는 날
        public string SpeakerId = "";
        public string IncidentKey = "";
        public string Text = "";
        public float AnchorTime;
        public bool HasTime;
    }

    // 어떤 직원이 "그 사람을 여기서 봤다"고 말한 내용.
    public sealed class SightingStatement
    {
        public int Day = 1;          // 말한 날
        public int SubjectDay = 1;   // 그 말이 가리키는 날
        public string SpeakerId = "";
        public string SubjectId = "";
        public string RoomId = "";
        public float AnchorTime;
        public bool HasTime;
        // 그때 무엇을 하고 있었는지까지 들었을 때만 채워진다("설비 쪽에 평소보다 오래 머물렀다").
        // 방 이름만 남기면 이 게임에서 가장 중요한 단서의 알맹이가 빠진다.
        public string Detail = "";

        // **못 봤다** 는 진술인가. "조작하는 장면은 보지 못했다" 같은 말이다.
        //
        // 이것은 "그 사람이 조작하지 않았다" 는 증거가 **아니다**. 증언자가 그 행동을
        // 관찰하지 못했다는 뜻일 뿐이다. 둘을 섞으면 무고한 직원을 범인으로 몰거나
        // 반대로 진범에게 알리바이를 만들어 준다(기획안 §4 · §5).
        public bool NotObserved;
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

    // CCTV 오디오로 들은 두 사람의 대화 한 번(A 가 말하고 B 가 받는다).
    // B 의 줄은 화면에 뜬 뒤에야 채워진다 — A 만 듣고 채널을 돌렸으면 A 의 줄만 남는다.
    public sealed class OverheardRecord
    {
        public int Day = 1;
        public string RoomId = "";
        public string A = "", B = "";
        public string LineA = "", LineB = "";
        // 대화가 시작된 근무 시각(초).
        public float Time;
    }

    private static readonly List<LocationStatement> _locations = new();
    private static readonly List<SightingStatement> _sightings = new();
    private static readonly List<BehaviorClaim> _behaviors = new();
    private static readonly List<CctvObservation> _cctv = new();
    private static readonly List<OverheardRecord> _overheard = new();

    // 지금 보고 있는 근무. 지난 DAY 의 자료는 조사 자료에 섞이지 않는다.
    private static int Today => GameState.Instance?.CurrentDay ?? 1;

    // --- 엿들은 대화 ------------------------------------------------------

    // 자막에 한 줄이 뜰 때마다 부른다. 같은 대화(같은 방 · 같은 두 사람 · 같은 시작 시각)면
    // 새로 만들지 않고 들은 줄만 채운다 — A 의 줄에서 한 번, B 의 줄에서 한 번.
    public static OverheardRecord RecordOverheard(string roomId, string a, string b, string lineA, string lineB,
        float time)
    {
        if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return null;
        var found = _overheard.FirstOrDefault(x => x.Day == Today && x.RoomId == roomId
            && x.A == a && x.B == b && Mathf.IsEqualApprox(x.Time, time));
        if (found != null)
        {
            if (!string.IsNullOrEmpty(lineA)) found.LineA = lineA;
            if (!string.IsNullOrEmpty(lineB)) found.LineB = lineB;
            return found;
        }
        var rec = new OverheardRecord
        {
            Day = Today, RoomId = roomId, A = a, B = b,
            LineA = lineA ?? "", LineB = lineB ?? "", Time = Mathf.Max(0f, time),
        };
        _overheard.Add(rec);
        // 같은 방을 오래 보고 있으면 대화가 계속 쌓인다 — 하루치 상한만 둔다.
        while (_overheard.Count > 200) _overheard.RemoveAt(0);
        return rec;
    }

    // 이 직원이 끼어 있던 오늘의 대화.
    public static IReadOnlyList<OverheardRecord> OverheardWith(string employeeId) =>
        _overheard.Where(x => x.Day == Today && (x.A == employeeId || x.B == employeeId)).ToList();

    public static int OverheardCount => _overheard.Count(x => x.Day == Today);

    // --- 진술 기록 ------------------------------------------------------

    // subjectDay 는 **그 말이 가리키는 근무일**. 0 이면 오늘 일을 말한 것으로 본다.
    // 같은 사건 키라도 날이 다르면 다른 주장이다 — 사건 키에는 날짜가 들어 있지 않아서,
    // 날을 같이 보지 않으면 어제 저장고 사고와 오늘 저장고 사고가 한 장으로 뭉친다.
    public static void RecordLocationStatement(string speakerId, string incidentKey, string roomId, bool exactTime,
        float anchorTime = -1f, int subjectDay = 0)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(roomId)) return;
        int subject = subjectDay > 0 ? subjectDay : Today;
        var found = _locations.FirstOrDefault(x => x.SubjectDay == subject
            && x.SpeakerId == speakerId && x.IncidentKey == incidentKey);
        if (found != null)
        {
            // 같은 방을 다시 말한 것은 반복일 뿐 정정이 아니다. 방이 바뀐 때만 이력에 쌓는다.
            if (found.RoomId != roomId) found.RoomHistory.Add(roomId);
            found.RoomId = roomId;
            found.StatedExactTime |= exactTime;
            if (anchorTime >= 0f) { found.AnchorTime = anchorTime; found.HasTime = true; }
            return;
        }
        var fresh = new LocationStatement
        {
            Day = Today, SubjectDay = subject,
            SpeakerId = speakerId, IncidentKey = incidentKey ?? "", RoomId = roomId, StatedExactTime = exactTime,
            AnchorTime = Mathf.Max(0f, anchorTime), HasTime = anchorTime >= 0f,
        };
        fresh.RoomHistory.Add(roomId);
        _locations.Add(fresh);
    }

    // 직원이 관리자에게 "그런 행동은 하지 않았다"고 말한 것을 남긴다.
    //
    // 이 클래스의 규칙 그대로 — **플레이어가 실제로 들은 말만** 들어온다. 그래서 호출부는
    // 그 문장이 실제로 답변에 실려 나갈 때 한 번만 부른다. 같은 사건에 대해 두 번 말해도
    // 자료는 한 장이다(진술이 늘어나는 것이 아니라 같은 주장을 반복한 것이므로).
    public static void RecordBehaviorClaim(string speakerId, string incidentKey, string text,
        float anchorTime = -1f, int subjectDay = 0)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(text)) return;
        int subject = subjectDay > 0 ? subjectDay : Today;
        var found = _behaviors.FirstOrDefault(x => x.SubjectDay == subject
            && x.SpeakerId == speakerId && x.IncidentKey == (incidentKey ?? "") && x.Text == text);
        if (found != null)
        {
            if (anchorTime >= 0f) { found.AnchorTime = anchorTime; found.HasTime = true; }
            return;
        }
        _behaviors.Add(new BehaviorClaim
        {
            Day = Today, SubjectDay = subject,
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
        // 「누군가의 흔적」 — 수상한 움직임이 실제로 **유효한 관찰 기록**으로 남은 경우만.
        // 위에서 걸러진 중복 기록(같은 인원 구성)은 여기까지 오지 않는다.
        if (suspiciousAction) NSP.Core.AchievementManager.Instance?.NoteSuspiciousObserved();
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
    // "그 장면은 보지 못했다" 는 진술. 목격과 **따로** 쌓는다 — 부재의 증거가 아니다.
    public static void RecordNotObserved(string speakerId, string subjectId, string roomId,
        string what, float anchorTime = -1f, int subjectDay = 0)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(subjectId)) return;
        int subject = subjectDay > 0 ? subjectDay : Today;
        if (_sightings.Any(x => x.SubjectDay == subject && x.SpeakerId == speakerId
                                && x.SubjectId == subjectId && x.NotObserved && x.Detail == what)) return;
        _sightings.Add(new SightingStatement
        {
            Day = Today, SubjectDay = subject,
            SpeakerId = speakerId, SubjectId = subjectId, RoomId = roomId ?? "",
            AnchorTime = Mathf.Max(0f, anchorTime), HasTime = anchorTime >= 0f,
            Detail = what ?? "", NotObserved = true,
        });
    }

    public static void RecordSighting(string speakerId, string subjectId, string roomId, float anchorTime = -1f,
        string detail = "", int subjectDay = 0)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(subjectId)) return;
        int subject = subjectDay > 0 ? subjectDay : Today;
        var found = _sightings.FirstOrDefault(x => x.SubjectDay == subject && x.SpeakerId == speakerId
            && x.SubjectId == subjectId && x.RoomId == roomId && !x.NotObserved);
        if (found != null)
        {
            // 같은 목격을 다시 들었을 때 내용이 더 자세해졌다면 그것만 채워 넣는다.
            if (string.IsNullOrEmpty(found.Detail) && !string.IsNullOrEmpty(detail)) found.Detail = detail;
            return;
        }
        _sightings.Add(new SightingStatement
        {
            Day = Today, SubjectDay = subject,
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
    // 본 것만. "못 봤다" 는 여기 들어오지 않는다 — 부재의 증거가 아니기 때문이다.
    public static IEnumerable<SightingStatement> SightingsOf(string employeeId) =>
        _sightings.Where(x => x.Day == Today && x.SubjectId == employeeId
                              && x.SpeakerId != employeeId && !x.NotObserved);

    // "그 장면은 못 봤다" 는 진술만. 따로 꺼내 쓴다.
    public static IEnumerable<SightingStatement> NotObservedOf(string employeeId) =>
        _sightings.Where(x => x.Day == Today && x.SubjectId == employeeId
                              && x.SpeakerId != employeeId && x.NotObserved);

    // 이 직원이 다른 직원을 봤다고 말한 기록.
    public static IEnumerable<SightingStatement> SightingsBy(string employeeId) =>
        _sightings.Where(x => x.Day == Today && x.SpeakerId == employeeId && !x.NotObserved);

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

    // 날을 가리지 않는다 — 어제 한 말도 포함. 어제 사건에 대한 그제 발언을 오늘 추궁할 때
    // 쓰인다(3단계 대화 기록 추궁과 검증).
    public static IReadOnlyList<LocationStatement> AllStatementsBy(string employeeId) =>
        _locations.Where(x => x.SpeakerId == employeeId).ToList();

    // 이 직원이 저 직원에 대해 그 날 말해 둔 목격 한 건. 없으면 null.
    // 날을 가리지 않는 조회가 필요하므로 SightingsBy(오늘만)와 따로 둔다.
    public static SightingStatement AllStatementsOfSighting(string speakerId, string subjectId, int day) =>
        _sightings.FirstOrDefault(x => x.SubjectDay == day
            && x.SpeakerId == speakerId && x.SubjectId == subjectId && !x.NotObserved);

    // 관리자가 CCTV 로 이 직원을 실제로 본 모든 순간(최근 순).
    public static IReadOnlyList<CctvObservation> CctvSightingsOf(string employeeId) =>
        _cctv.Where(o => o.Day == Today && o.Occupants.Contains(employeeId)).ToList();

    public static void ResetAll()
    {
        _locations.Clear();
        _sightings.Clear();
        _behaviors.Clear();
        _cctv.Clear();
        _overheard.Clear();
    }
}
