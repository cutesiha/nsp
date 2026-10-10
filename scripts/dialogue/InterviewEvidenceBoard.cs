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
//   · 관리자가 CCTV 로 직접 본 장면 · 엿들은 대화 / 직원이 한 진술 (PlayerKnownEvidence)
//   · 근무표에 적혀 있던 오늘의 기분              (EmployeeState.DailyMood)
//
// EventLog 원본을 여기서 직접 읽지 않는다. 사고 자료조차 "시설 로그 화면에 떴는가"를
// 거쳐서 들어온다 — 플레이어가 보지 못한 사실은 자료가 될 수 없다.
//
// 자료 Id 는 **내용**으로 만든다(목록 순번을 쓰지 않는다). 근무 중에 관리자 패드로 찍어 둔
// 자료를 휴게시간에 다시 만들어도 같은 Id 가 나와야 하기 때문이다. Id 는 그 날 안에서만
// 고유하다 — 날짜를 넘어 가리킬 때는 (Day, Id) 로 쓴다(ClueBoard).
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
        AddAnomalies(list, rows, targetEmployeeId);
        AddCctv(list, targetEmployeeId);
        AddTestimonies(list, targetEmployeeId);
        AddOwnStatements(list, targetEmployeeId);
        AddBehaviorClaims(list, targetEmployeeId);
        AddOverheard(list, targetEmployeeId);
        AddCalls(list, targetEmployeeId);

        foreach (var e in list) e.Day = day;
        return Sort(list);
    }

    // 시설 로그 화면의 한 줄이 조사 자료가 되는가 — 되면 그 자료를, 아니면 null.
    //
    // 근무 중 로그 창의 ☆ 가 이것을 쓴다. 휴게시간 조사 노트(Build)와 **같은 함수**로
    // 만들어야 같은 Id 가 나온다 — 근무 중에 찍은 줄이 휴게시간에 ★ 로 보이는 이유다.
    // 자료가 되는 줄은 둘뿐이다: 직원의 이동, 시설 사고.
    public static InterviewEvidence FromLogRow(DisplayLogEntry row)
    {
        if (row == null) return null;
        var ev = LogRowEvidence(row, row.RelatedEmployeeId);
        if (ev != null) ev.Day = DialogueContextBuilder.Day();
        return ev;
    }

    // 로그 화면의 index 번째 줄 → 자료. 같은 순간 · 같은 방 · 같은 종류의 사고 줄이 여럿이면
    // (고장 발생 + 그 결과 같은 줄) 앞 줄부터 차례로 #2, #3 을 붙여 서로 다른 자료로 갈라 준다 —
    // 조사 노트(AddLogRows)와 같은 규칙이다.
    public static InterviewEvidence FromLogRow(IReadOnlyList<DisplayLogEntry> rows, int index)
    {
        if (rows == null || index < 0 || index >= rows.Count) return null;
        var ev = FromLogRow(rows[index]);
        if (ev == null) return null;
        int dup = 0;
        for (int i = 0; i < index; i++)
            if (LogRowEvidence(rows[i], rows[i].RelatedEmployeeId)?.Id == ev.Id) dup++;
        if (dup > 0) ev.Id += $"#{dup + 1}";
        return ev;
    }

    // 엿들은 대화 한 건 → 조사 자료. subjectId 는 둘 중 누구의 자료로 볼 것인가
    // (비우면 먼저 말한 사람). Id 는 누구의 자료로 보든 같다.
    public static InterviewEvidence FromOverheard(PlayerKnownEvidence.OverheardRecord h, string subjectId = "")
    {
        if (h == null) return null;
        string subject = subjectId == h.B ? h.B : h.A;
        string other = subject == h.A ? h.B : h.A;
        var ev = new InterviewEvidence
        {
            Id = $"heard:{h.RoomId}:{h.A}:{h.B}:{h.Time:0.0}",
            Kind = EvidenceKind.Overheard,
            Day = h.Day,
            Header = "엿들은 대화",
            TimeText = DialogueClock.Text(h.Time),
            Body = string.IsNullOrEmpty(h.LineB)
                ? $"{Codename(h.A)}: \"{h.LineA}\""
                : $"{Codename(h.A)}: \"{h.LineA}\" / {Codename(h.B)}: \"{h.LineB}\"",
            SubjectEmployeeId = subject,
            AnchorTime = h.Time,
            HasTime = true,
            // 관리자가 그 방의 소리를 직접 들었다 — CCTV 로 본 것과 같은 무게의 재석 기록이다.
            Position = PositionClaim.AtRoom,
            SubjectRoomId = h.RoomId,
        };
        ev.RelatedEmployeeIds.Add(other);
        return ev;
    }

    // 오늘 확보한 자료 전부(다른 직원 것 포함). 질문에는 쓸 수 없고 읽기용이다 —
    // "지금 이 사람 것만" 과 "전체 흐름" 을 같은 화면에서 오갈 수 있게 한다.
    public static List<InterviewEvidence> BuildAll(string targetEmployeeId)
    {
        var sim = FacilitySimulation.Instance;
        var list = Build(targetEmployeeId);
        if (sim == null) return list;

        var seen = new HashSet<string>(list.Select(e => e.Id));
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (id == targetEmployeeId) continue;
            foreach (var e in Build(id))
            {
                if (!seen.Add(e.Id)) continue;   // 사고 기록처럼 주인이 없는 자료는 한 번만
                list.Add(e);
            }
        }
        return Sort(list);
    }

    // 시간이 없는 자료(기분)는 항상 맨 위에 둔다 — 근무 전에 적어 낸 것이므로.
    private static List<InterviewEvidence> Sort(List<InterviewEvidence> list) => list
        .OrderBy(e => e.HasTime ? 1 : 0)
        .ThenBy(e => e.AnchorTime)
        .ThenBy(e => e.Id, System.StringComparer.Ordinal)
        .ToList();

    // 고른 자료와 같은 시간대(±EvidenceContradiction.WindowMinutes)의 다른 자료들.
    //
    // "이 자료들이 단서다" 라고 알려 주는 기능이 아니다. 사건 하나를 고르면 그 전후를
    // 같이 보고 싶다는 것뿐이고, 판단은 여전히 플레이어가 한다.
    public static HashSet<string> SameWindow(IEnumerable<InterviewEvidence> board, InterviewEvidence focus)
    {
        var ids = new HashSet<string>();
        if (board == null || focus == null || !focus.HasTime) return ids;
        float window = EvidenceContradiction.WindowMinutes * DialogueClock.SecondsPerMinute;
        foreach (var e in board)
        {
            if (e == focus || !e.HasTime) continue;
            if (Mathf.Abs(e.AnchorTime - focus.AnchorTime) <= window) ids.Add(e.Id);
        }
        return ids;
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
            Body = mood,
            SubjectEmployeeId = id,
            MoodText = mood,
            HasTime = false,
        });
    }

    // --- 시설 로그 화면 -------------------------------------------------

    private static void AddLogRows(List<InterviewEvidence> list, List<DisplayLogEntry> rows, string target)
    {
        if (rows == null) return;
        var seen = new Dictionary<string, int>();
        foreach (var r in rows)
        {
            var ev = LogRowEvidence(r, target);
            if (ev == null) continue;
            // 같은 Id 가 또 나오면(같은 순간의 사고 줄 둘) #2, #3 으로 가른다(FromLogRow 와 같은 규칙).
            int n = seen.GetValueOrDefault(ev.Id) + 1;
            seen[ev.Id] = n;
            if (n > 1) ev.Id += $"#{n}";
            list.Add(ev);
        }
    }

    // 로그 한 줄 → 자료 한 장. target 의 이동이거나 시설 사고일 때만 만든다.
    private static InterviewEvidence LogRowEvidence(DisplayLogEntry r, string target)
    {
        // ① 이 직원의 이동 — 화면에 뜬 것만.
        if (!string.IsNullOrEmpty(target) && r.RelatedEmployeeId == target
            && !string.IsNullOrEmpty(r.FromRoomId) && !string.IsNullOrEmpty(r.ToRoomId))
        {
            return new InterviewEvidence
            {
                Id = $"move:{target}:{r.Timestamp:0.00}:{r.ToRoomId}",
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
            };
        }

        // ② 기절 — 그 직원에게 일어난 일이다. 스트레스가 한계를 넘은 순간이 로그에 남으므로
        //    "그 시각 그 사람은 바닥에 누워 있었다" 가 알리바이·모순의 재료가 된다.
        if (r.Detail == LogDetail.Fainted && !string.IsNullOrEmpty(r.RelatedEmployeeId))
        {
            return new InterviewEvidence
            {
                Id = $"faint:{r.RelatedEmployeeId}:{r.Timestamp:0.00}",
                Kind = EvidenceKind.Incident,
                Header = "기절 기록",
                TimeText = DialogueClock.Text(r.Timestamp),
                Body = $"{Codename(r.RelatedEmployeeId)} — 스트레스 한계 · 업무 불능",
                SubjectEmployeeId = r.RelatedEmployeeId,
                AnchorTime = r.Timestamp,
                HasTime = true,
                // 쓰러진 그 자리에 있었다는 뜻이다 — 위치 주장으로 쓸 수 있다.
                Position = PositionClaim.AtRoom,
                SubjectRoomId = r.RoomId,
            };
        }

        // ③ 사고 기록 — 특정 직원의 것이 아니라 시설 전체의 사건이다.
        if (!IsIncidentRow(r)) return null;
        string room = IncidentRoomOf(r);
        string key = IncidentKeyOf(r.SourceEventType, room, r.Timestamp);
        return new InterviewEvidence
        {
            Id = IncidentIdOf(key),
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
            IncidentKey = key,
        };
    }

    // 화면 로그 한 줄이 "사고"인가. 이동/격리/스트레스 경고는 사고가 아니다.
    // 방해공작 줄도 포함한다 — 플레이어가 화면에서 본 사건이므로 조사 자료가 된다.
    // (그 줄에는 범인 이름이 없다. FacilityLogFormatter 가 이미 지워서 내보낸다.)
    // 이상 개체에게 당한 사고(놓친 괴물)도 사고다 — 관리자 패드의 CCTV 스냅샷이 붙는 사건이다.
    private static bool IsIncidentRow(DisplayLogEntry r) => IsIncidentType(r.SourceEventType);

    public static bool IsIncidentType(LogEventType type) =>
        type is LogEventType.TaskFailed or LogEventType.PowerOutage
            or LogEventType.TabooViolation or LogEventType.CctvDisconnect or LogEventType.Death
            or LogEventType.Sabotage or LogEventType.AnomalyIncident;

    // 사고 자료의 키 · Id. 사건이 난 순간(EventLog 기록)에도 같은 Id 를 만들 수 있어야
    // 그 순간 찍은 CCTV 스냅샷을 나중에 그 사고 카드에 붙일 수 있다.
    public static string IncidentKeyOf(LogEventType type, string roomId, float time) =>
        $"{type}:{roomId ?? ""}:{time:0.0}";

    public static string IncidentIdOf(string incidentKey) => "incident:" + incidentKey;

    // 화면 줄에는 RoomId 가 직접 실려 있지 않다. EventLog 에서 같은 시각·같은 종류의
    // 사건을 찾아 방만 가져온다(문장은 화면에 뜬 것을 그대로 쓴다).
    public static string IncidentRoomOf(DisplayLogEntry r)
    {
        var e = EventLog.Instance?.GetAllEntries()
            .FirstOrDefault(x => x.EventType == r.SourceEventType
                                 && Mathf.IsEqualApprox(x.GameTimeSeconds, r.Timestamp));
        return e?.RoomId ?? "";
    }

    // --- CCTV -----------------------------------------------------------

    private static void AddCctv(List<InterviewEvidence> list, string target)
    {
        foreach (var o in PlayerKnownEvidence.CctvSightingsOf(target))
        {
            // 그 화면에 설비 쪽 움직임이 잡혀 있었으면 카드에 그 사실을 싣는다.
            // 누가 그랬는지는 화면도 모른다 — "그 방에 그런 움직임이 있었다"까지만이다.
            string detail = o.SuspiciousAction ? "설비 쪽에 접근" : "";
            var ev = new InterviewEvidence
            {
                // 순번을 넣지 않는다 — 시청 기록은 상한을 넘으면 앞에서부터 지워져 순번이 밀린다.
                Id = $"cctv:{target}:{o.RoomId}:{o.Time:0.0}",
                Kind = EvidenceKind.Cctv,
                Header = "CCTV",
                TimeText = DialogueClock.Text(o.Time),
                Body = string.IsNullOrEmpty(detail)
                    ? $"{RoomName(o.RoomId)}에서 확인"
                    : $"{RoomName(o.RoomId)} · {detail}",
                BehaviorDetail = detail,
                SubjectEmployeeId = target,
                AnchorTime = o.Time,
                HasTime = true,
                Position = PositionClaim.AtRoom,
                SubjectRoomId = o.RoomId,
            };
            ev.RelatedEmployeeIds.AddRange(o.Occupants.Where(x => x != target));
            list.Add(ev);
        }
    }

    // --- 다른 직원의 증언 ------------------------------------------------

    private static void AddTestimonies(List<InterviewEvidence> list, string target)
    {
        foreach (var s in PlayerKnownEvidence.SightingsOf(target))
        {
            list.Add(new InterviewEvidence
            {
                // 목격 증언은 (말한 사람 · 본 사람 · 방) 으로 한 건이다(RecordSighting 과 같은 기준).
                Id = $"say:{target}:{s.SpeakerId}:{s.RoomId}",
                Kind = EvidenceKind.Testimony,
                Header = Codename(s.SpeakerId) + "의 증언",
                TimeText = s.HasTime ? DialogueClock.Text(s.AnchorTime) : "",
                // 무엇을 하고 있었는지까지 들었으면 그것을 싣는다 — 방 이름은 시각 옆 태그로 충분하다.
                Body = string.IsNullOrEmpty(s.Detail)
                    ? $"{Codename(s.SpeakerId)} · {RoomName(s.RoomId)}에서 봤다"
                    : $"{Codename(s.SpeakerId)} · {s.Detail}",
                BehaviorDetail = s.Detail,
                SubjectEmployeeId = target,
                SpeakerEmployeeId = s.SpeakerId,
                AnchorTime = s.AnchorTime,
                HasTime = s.HasTime,
                Position = PositionClaim.AtRoom,
                SubjectRoomId = s.RoomId,
                RelatedEmployeeIds = { s.SpeakerId },
            });
        }
    }

    // --- 이 직원이 앞서 한 진술 -------------------------------------------

    private static void AddOwnStatements(List<InterviewEvidence> list, string target)
    {
        foreach (var st in PlayerKnownEvidence.StatementsBy(target))
        {
            list.Add(new InterviewEvidence
            {
                // 위치 진술은 (말한 사람 · 사건) 으로 한 건이다(RecordLocationStatement 와 같은 기준).
                // 같은 사건 키라도 가리키는 날이 다르면 다른 진술이다.
                Id = $"claim:{target}:{st.SubjectDay}:{st.IncidentKey}",
                Kind = EvidenceKind.OwnStatement,
                Day = st.Day, SubjectDay = st.SubjectDay,
                Header = Codename(target) + "의 진술",
                TimeText = st.HasTime ? DialogueClock.Text(st.AnchorTime) : "",
                Body = $"본인 · {RoomName(st.RoomId)}에 있었다",
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

    // --- 이 직원이 "하지 않았다"고 말한 행동 -------------------------------
    //
    // 위치 진술과 따로 카드가 된다. 이 카드가 동료의 목격 증언과 짝이 되면 행동 추궁이
    // 성립한다 — 결번 개체에게서 잡을 수 있는 유일한 거짓말이 여기서 자료가 된다(§3-2).
    private static void AddBehaviorClaims(List<InterviewEvidence> list, string target)
    {
        int n = 0;
        foreach (var b in PlayerKnownEvidence.BehaviorClaimsBy(target))
        {
            n++;
            list.Add(new InterviewEvidence
            {
                Id = $"deny:{target}:{n}:{b.SubjectDay}:{b.IncidentKey}",
                Kind = EvidenceKind.OwnStatement,
                Day = b.Day, SubjectDay = b.SubjectDay,
                Header = Codename(target) + "의 진술",
                TimeText = b.HasTime ? DialogueClock.Text(b.AnchorTime) : "",
                Body = $"본인 · {b.Text}",
                SubjectEmployeeId = target,
                SpeakerEmployeeId = target,
                AnchorTime = b.AnchorTime,
                HasTime = b.HasTime,
                // 위치를 말하는 진술이 아니다 — 위치 추궁의 근거로 쓰이면 안 된다.
                Position = PositionClaim.None,
                SubjectRoomId = IncidentRoomOfKey(b.IncidentKey),
                IncidentKey = b.IncidentKey,
                BehaviorDetail = b.Text,
            });
        }
    }

    // --- 엿들은 대화 ------------------------------------------------------
    //
    // 대화에 끼어 있던 두 사람 모두의 자료가 된다(Id 는 같으므로 전원 보기에서는 한 장이다).
    // 엿들은 내용을 들고 그 사람에게 물을 수 있게 하는 것이 목적이다.
    private static void AddOverheard(List<InterviewEvidence> list, string target)
    {
        foreach (var h in PlayerKnownEvidence.OverheardWith(target))
        {
            var ev = FromOverheard(h, target);
            if (ev != null) list.Add(ev);
        }
    }

    // --- 관리자와의 통화 ------------------------------------------------
    //
    // 직원이 먼저 건 전화(사고 신고 · 잡담) · 못 받은 전화 · 관리자가 건 전화. 통화는 관리자가 직접 한
    // 일이니 그대로 자료다. 무엇을 말했는지는 통화 종류(DialogueEvent)로만 적는다 — 화면에 떴던 문장을
    // 다시 읽어 뜻을 추측하지 않는다(CallMemoryLog 의 규칙). 통화 안에서 내린 지시(가라/대기)는
    // 그 통화의 일부라 따로 카드가 되지 않는다.
    private static void AddCalls(List<InterviewEvidence> list, string target)
    {
        int day = DialogueContextBuilder.Day();
        var counts = new Dictionary<string, int>();
        foreach (var r in CallMemoryLog.For(target, day).OrderBy(r => r.Time))
        {
            if (r.Kind is CallRecordKind.OrderedGo or CallRecordKind.OrderedStay) continue;
            string ev = r.DialogueEvent ?? "";
            int n = counts.GetValueOrDefault(ev) + 1;
            counts[ev] = n;
            // 전화한 직원은 그 시각 자기 자리(시설 로그의 배치 · 이동 줄로 아는 방)에 있었다 — 재석 근거다.
            string where = DialogueContextBuilder.RoomAt(target, day, r.Time);
            list.Add(new InterviewEvidence
            {
                Id = $"call:{target}:{r.Time:0.0}:{r.Kind}",
                Kind = EvidenceKind.Call,
                Header = "통화 기록",
                TimeText = DialogueClock.Text(r.Time),
                Body = CallBody(r, n),
                SubjectEmployeeId = target,
                AnchorTime = r.Time,
                HasTime = true,
                Position = string.IsNullOrEmpty(where) ? PositionClaim.None : PositionClaim.AtRoom,
                SubjectRoomId = where,
                CallEvent = ev,
                CallKind = r.Kind,
                CallRoomId = IsIdleEvent(ev) ? r.RoomId ?? "" : "",
                RepeatIndex = n,
            });
        }
    }

    // 조용한 시간의 잡담 전화인가(놀러 가도 되나 · 옆 방 소리가 이상하다).
    public static bool IsIdleEvent(string dialogueEvent) =>
        dialogueEvent is DialogueRepository.EventIdleVisit or DialogueRepository.EventIdleWorry;

    // 오늘 이 직원이 "놀러 가도 되냐"고 건 전화 수. 둘 이상이면 근무 태만을 따질 수 있다.
    public static int IdleVisitCallCount(string employeeId) =>
        CallMemoryLog.For(employeeId, DialogueContextBuilder.Day())
            .Count(r => r.Kind == CallRecordKind.Reported && r.DialogueEvent == DialogueRepository.EventIdleVisit);

    private static string CallBody(CallRecord r, int nth)
    {
        string room = RoomName(r.RoomId);
        string what = r.Kind switch
        {
            CallRecordKind.Missed => "전화를 걸었지만 관리자가 받지 않았다",
            CallRecordKind.ManagerCalled => "관리자가 걸었다",
            _ => r.DialogueEvent switch
            {
                DialogueRepository.EventIdleVisit => $"{room}에 놀러 가도 되냐고 물었다",
                DialogueRepository.EventIdleWorry => $"{room}에서 이상한 소리가 난다고 알렸다",
                DialogueRepository.EventAccidentNearby => $"{room} 쪽 사고를 알렸다",
                DialogueRepository.EventScreamNextRoom => $"{room} 쪽 비명을 알렸다",
                DialogueRepository.EventBlackout => "정전을 알렸다",
                DialogueRepository.EventWitnessSuspicious => "수상한 행동을 봤다고 알렸다",
                DialogueRepository.EventFaintTransportRequest => $"{room}의 동료가 쓰러졌다고 알렸다",
                DialogueRepository.EventTutorialRepairDone => "동료를 의무실로 옮겼다고 알렸다",
                _ => "관리자에게 전화했다",
            },
        };
        string count = nth >= 2 && r.DialogueEvent == DialogueRepository.EventIdleVisit ? $"  (오늘 {nth}번째)" : "";
        return what + count;
    }

    // --- 이상 개체 조우 ---------------------------------------------------
    //
    // 시설 로그 화면에 뜬 이상 개체 줄(관측 소멸 · 접촉 사고)마다, 그 순간 그 방에 있던 직원의 자료가 된다.
    // 누가 있었는지는 같은 화면의 배치 · 이동 줄로 아는 사실이다(띠 시간표가 그리는 것과 같다).
    private static void AddAnomalies(List<InterviewEvidence> list, List<DisplayLogEntry> rows, string target)
    {
        if (rows == null) return;
        int day = DialogueContextBuilder.Day();
        foreach (var r in rows)
        {
            if (r.SourceEventType is not (LogEventType.AnomalyDispelled or LogEventType.AnomalyIncident)) continue;
            string room = IncidentRoomOf(r);
            if (string.IsNullOrEmpty(room) || DialogueContextBuilder.RoomAt(target, day, r.Timestamp) != room) continue;
            bool struck = r.SourceEventType == LogEventType.AnomalyIncident;
            var ev = new InterviewEvidence
            {
                Id = $"ghost:{target}:{room}:{r.Timestamp:0.0}",
                Kind = EvidenceKind.Anomaly,
                Header = "이상 개체",
                TimeText = DialogueClock.Text(r.Timestamp),
                Body = struck ? $"{RoomName(room)} · 이상 개체 접촉 사고 당시 그 방에 있었다"
                              : $"{RoomName(room)} · 이상 개체가 나타났을 때 그 방에 있었다",
                SubjectEmployeeId = target,
                AnchorTime = r.Timestamp,
                HasTime = true,
                Position = PositionClaim.AtRoom,
                SubjectRoomId = room,
                IncidentType = r.SourceEventType,
            };
            ev.RelatedEmployeeIds.AddRange(DialogueContextBuilder.OccupantsAt(room, day, r.Timestamp, target));
            list.Add(ev);
        }
    }

    // 주장 키(EventType:Room:Time)에서 방만 꺼낸다. 행동 추궁이 "같은 방"을 보기 때문이다.
    private static string IncidentRoomOfKey(string incidentKey)
    {
        if (string.IsNullOrEmpty(incidentKey)) return "";
        var parts = incidentKey.Split(':');
        return parts.Length >= 2 ? parts[1] : "";
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
