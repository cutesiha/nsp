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
        public string SpeakerId = "";
        public string IncidentKey = "";
        public string RoomId = "";
        public bool StatedExactTime;
    }

    // 어떤 직원이 "그 사람을 여기서 봤다"고 말한 내용.
    public sealed class SightingStatement
    {
        public string SpeakerId = "";
        public string SubjectId = "";
        public string RoomId = "";
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
        public string RoomId = "";
        public float Time;
        public List<string> Occupants = new();
    }

    private static readonly List<LocationStatement> _locations = new();
    private static readonly List<SightingStatement> _sightings = new();
    private static readonly List<CctvObservation> _cctv = new();

    // --- 진술 기록 ------------------------------------------------------

    public static void RecordLocationStatement(string speakerId, string incidentKey, string roomId, bool exactTime)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(roomId)) return;
        var found = _locations.FirstOrDefault(x => x.SpeakerId == speakerId && x.IncidentKey == incidentKey);
        if (found != null)
        {
            found.RoomId = roomId;
            found.StatedExactTime |= exactTime;
            return;
        }
        _locations.Add(new LocationStatement
        {
            SpeakerId = speakerId, IncidentKey = incidentKey ?? "", RoomId = roomId, StatedExactTime = exactTime,
        });
    }

    // CCTV 시청 기록. FacilitySimulation 이 3초 연속 시청마다 한 번씩 호출한다.
    public static void RecordCctvObservation(string roomId, float time, IEnumerable<string> occupants)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        _cctv.Add(new CctvObservation
        {
            RoomId = roomId,
            Time = time,
            Occupants = occupants != null ? new List<string>(occupants) : new List<string>(),
        });
        // 하루치가 계속 쌓이지 않게 상한만 둔다.
        while (_cctv.Count > 200) _cctv.RemoveAt(0);
    }

    // 그 시각 언저리에 CCTV로 이 직원을 실제로 본 작업실. 없으면 빈 값.
    public static string CctvSeenRoomOf(string employeeId, float aroundTime, float window)
    {
        foreach (var o in _cctv)
            if (Mathf.Abs(o.Time - aroundTime) <= window && o.Occupants.Contains(employeeId))
                return o.RoomId;
        return "";
    }

    // 그 시각 언저리에 그 방을 지켜봤는데 이 직원이 거기 없었는가.
    public static bool CctvWatchedWithout(string employeeId, string roomId, float aroundTime, float window)
    {
        if (string.IsNullOrEmpty(roomId)) return false;
        foreach (var o in _cctv)
            if (o.RoomId == roomId && Mathf.Abs(o.Time - aroundTime) <= window
                && !o.Occupants.Contains(employeeId))
                return true;
        return false;
    }

    public static int CctvObservationCount => _cctv.Count;

    public static void RecordSighting(string speakerId, string subjectId, string roomId)
    {
        if (string.IsNullOrEmpty(speakerId) || string.IsNullOrEmpty(subjectId)) return;
        if (_sightings.Any(x => x.SpeakerId == speakerId && x.SubjectId == subjectId && x.RoomId == roomId)) return;
        _sightings.Add(new SightingStatement { SpeakerId = speakerId, SubjectId = subjectId, RoomId = roomId ?? "" });
    }

    // --- 조회 ------------------------------------------------------------

    // 이 직원이 그 사건에 대해 스스로 말한 위치.
    public static LocationStatement OwnClaim(string employeeId, string incidentKey) =>
        _locations.FirstOrDefault(x => x.SpeakerId == employeeId && x.IncidentKey == incidentKey);

    // 다른 직원이 이 직원을 봤다고 말한 기록.
    public static IEnumerable<SightingStatement> SightingsOf(string employeeId) =>
        _sightings.Where(x => x.SubjectId == employeeId && x.SpeakerId != employeeId);

    // 이 직원이 다른 직원을 봤다고 말한 기록.
    public static IEnumerable<SightingStatement> SightingsBy(string employeeId) =>
        _sightings.Where(x => x.SpeakerId == employeeId);

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

    public static void ResetAll()
    {
        _locations.Clear();
        _sightings.Clear();
        _cctv.Clear();
    }
}
