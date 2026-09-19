using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 한 직원을 심문할 때 화면 오른쪽에 뜨는 「조사 자료」를 만든다.
//
// 재료는 셋뿐이다.
//   · 시설 로그 화면에 실제로 떴던 줄            (FacilityLogFormatter)
//   · 관리자가 CCTV 로 직접 본 장면 / 직원이 한 진술 (PlayerKnownEvidence)
//   · 근무표에 적혀 있던 오늘의 기분              (EmployeeState.DailyMood)
//
// EventLog 원본을 여기서 직접 읽지 않는다. 사고 자료조차 "시설 로그 화면에 떴는가"를
// 거쳐서 들어온다 — 플레이어가 보지 못한 사실은 자료가 될 수 없다.
public static class InterviewEvidenceBoard
{
    // 이 직원을 심문할 때 쓸 수 있는 자료 전부. 시간 순으로 정렬해 돌려준다.
    public static List<InterviewEvidence> Build(string targetEmployeeId)
    {
        var list = new List<InterviewEvidence>();
        if (string.IsNullOrEmpty(targetEmployeeId)) return list;

        int day = DialogueContextBuilder.Day();
        var rows = FacilityLogFormatter.Build(EventLog.Instance?.GetAllEntries(), day);

        AddMood(list, targetEmployeeId);
        AddLogRows(list, rows, targetEmployeeId);
        AddCctv(list, targetEmployeeId);
        AddTestimonies(list, targetEmployeeId);
        AddOwnStatements(list, targetEmployeeId);

        // 시간이 없는 자료(기분)는 항상 맨 위에 둔다 — 근무 전에 적어 낸 것이므로.
        return list
            .OrderBy(e => e.HasTime ? 1 : 0)
            .ThenBy(e => e.AnchorTime)
            .ToList();
    }

    public static InterviewEvidence Find(IEnumerable<InterviewEvidence> board, string id) =>
        string.IsNullOrEmpty(id) ? null : board?.FirstOrDefault(e => e.Id == id);

    // --- 오늘의 기분 ----------------------------------------------------

    private static void AddMood(List<InterviewEvidence> list, string id)
    {
        string mood = FacilitySimulation.Instance?.GetDailyMood(id) ?? "";
        if (string.IsNullOrEmpty(mood)) return;
        list.Add(new InterviewEvidence
        {
            Id = "mood:" + id,
            Kind = EvidenceKind.Mood,
            Header = "자가보고",
            TimeText = "",
            Body = "오늘의 기분 : " + mood,
            SubjectEmployeeId = id,
            MoodText = mood,
            HasTime = false,
        });
    }

    // --- 시설 로그 화면 -------------------------------------------------

    private static void AddLogRows(List<InterviewEvidence> list, List<DisplayLogEntry> rows, string target)
    {
        if (rows == null) return;
        int n = 0;
        foreach (var r in rows)
        {
            n++;
            // ① 이 직원의 이동 — 화면에 뜬 것만.
            if (r.RelatedEmployeeId == target
                && !string.IsNullOrEmpty(r.FromRoomId) && !string.IsNullOrEmpty(r.ToRoomId))
            {
                list.Add(new InterviewEvidence
                {
                    Id = $"move:{n}",
                    Kind = EvidenceKind.Movement,
                    Header = "시설 로그",
                    TimeText = DialogueClock.Text(r.Timestamp),
                    Body = $"{RoomName(r.FromRoomId)} → {RoomName(r.ToRoomId)}"
                           + (r.PlayerOrdered ? "  (지시)" : ""),
                    SubjectEmployeeId = target,
                    AnchorTime = r.Timestamp,
                    HasTime = true,
                    Position = PositionClaim.Arrived,
                    FromRoomId = r.FromRoomId,
                    ToRoomId = r.ToRoomId,
                    SubjectRoomId = r.ToRoomId,
                    PlayerOrdered = r.PlayerOrdered,
                });
                continue;
            }

            // ② 사고 기록 — 특정 직원의 것이 아니라 시설 전체의 사건이다.
            if (!IsIncidentRow(r)) continue;
            string room = IncidentRoomOf(r);
            list.Add(new InterviewEvidence
            {
                Id = $"incident:{n}",
                Kind = EvidenceKind.Incident,
                Header = "사고 기록",
                TimeText = DialogueClock.Text(r.Timestamp),
                Body = r.Text,
                SubjectEmployeeId = "",          // 누구의 것도 아니다 — 모두에게 물을 수 있다
                AnchorTime = r.Timestamp,
                HasTime = true,
                Position = PositionClaim.None,   // 위치 주장이 아니므로 모순 근거가 아니다
                SubjectRoomId = room,
                IncidentType = r.SourceEventType,
                IncidentKey = $"{r.SourceEventType}:{room}:{r.Timestamp:0.0}",
            });
        }
    }

    // 화면 로그 한 줄이 "사고"인가. 이동/격리/스트레스 경고는 사고가 아니다.
    private static bool IsIncidentRow(DisplayLogEntry r) =>
        r.SourceEventType is LogEventType.TaskFailed or LogEventType.PowerOutage
            or LogEventType.TabooViolation or LogEventType.CctvDisconnect or LogEventType.Death;

    // 화면 줄에는 RoomId 가 직접 실려 있지 않다. EventLog 에서 같은 시각·같은 종류의
    // 사건을 찾아 방만 가져온다(문장은 화면에 뜬 것을 그대로 쓴다).
    private static string IncidentRoomOf(DisplayLogEntry r)
    {
        var e = EventLog.Instance?.GetAllEntries()
            .FirstOrDefault(x => x.EventType == r.SourceEventType
                                 && Mathf.IsEqualApprox(x.GameTimeSeconds, r.Timestamp));
        return e?.RoomId ?? "";
    }

    // --- CCTV -----------------------------------------------------------

    private static void AddCctv(List<InterviewEvidence> list, string target)
    {
        int n = 0;
        foreach (var o in PlayerKnownEvidence.CctvSightingsOf(target))
        {
            n++;
            list.Add(new InterviewEvidence
            {
                Id = $"cctv:{n}:{o.Time:0.0}",
                Kind = EvidenceKind.Cctv,
                Header = "CCTV",
                TimeText = DialogueClock.Text(o.Time),
                Body = $"{RoomName(o.RoomId)} / {Codename(target)} 확인",
                SubjectEmployeeId = target,
                AnchorTime = o.Time,
                HasTime = true,
                Position = PositionClaim.AtRoom,
                SubjectRoomId = o.RoomId,
            });
        }
    }

    // --- 다른 직원의 증언 ------------------------------------------------

    private static void AddTestimonies(List<InterviewEvidence> list, string target)
    {
        int n = 0;
        foreach (var s in PlayerKnownEvidence.SightingsOf(target))
        {
            n++;
            list.Add(new InterviewEvidence
            {
                Id = $"say:{n}:{s.SpeakerId}",
                Kind = EvidenceKind.Testimony,
                Header = Codename(s.SpeakerId) + "의 증언",
                TimeText = s.HasTime ? DialogueClock.Text(s.AnchorTime) : "",
                Body = $"\"{RoomName(s.RoomId)}에서 {Codename(target)}를 봤습니다.\"",
                SubjectEmployeeId = target,
                SpeakerEmployeeId = s.SpeakerId,
                AnchorTime = s.AnchorTime,
                HasTime = s.HasTime,
                Position = PositionClaim.AtRoom,
                SubjectRoomId = s.RoomId,
            });
        }
    }

    // --- 이 직원이 앞서 한 진술 -------------------------------------------

    private static void AddOwnStatements(List<InterviewEvidence> list, string target)
    {
        int n = 0;
        foreach (var st in PlayerKnownEvidence.StatementsBy(target))
        {
            n++;
            string when = st.HasTime ? DialogueClock.Spoken(st.AnchorTime) + "에는 " : "";
            list.Add(new InterviewEvidence
            {
                Id = $"claim:{n}:{st.IncidentKey}",
                Kind = EvidenceKind.OwnStatement,
                Header = Codename(target) + "의 진술",
                TimeText = st.HasTime ? DialogueClock.Text(st.AnchorTime) : "",
                Body = $"\"{when}{RoomName(st.RoomId)}에 있었습니다.\"",
                SubjectEmployeeId = target,
                SpeakerEmployeeId = target,
                AnchorTime = st.AnchorTime,
                HasTime = st.HasTime,
                Position = PositionClaim.AtRoom,
                SubjectRoomId = st.RoomId,
                IncidentKey = st.IncidentKey,
            });
        }
    }

    // --- 공통 -----------------------------------------------------------

    public static string RoomName(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return "통로";
        return FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;
    }

    public static string Codename(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId)) return "직원";
        return FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename ?? employeeId;
    }
}
