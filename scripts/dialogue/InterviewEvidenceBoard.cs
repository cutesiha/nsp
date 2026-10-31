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
        AddNamedMentions(list, targetEmployeeId);
        AddOwnStatements(list, targetEmployeeId);
        AddOwnTestimonies(list, targetEmployeeId);
        AddBehaviorClaims(list, targetEmployeeId);
        AddSpokenLines(list, targetEmployeeId);
        AddOverheard(list, targetEmployeeId);
        AddCalls(list, targetEmployeeId);

        // 오늘 확보한 기록에 오늘 날짜를 찍는다. 어제 한 발언처럼 **자기 날짜를 들고 온
        // 카드는 건드리지 않는다** — 덮어쓰면 어제 진술이 오늘 진술로 보인다.
        foreach (var e in list) if (!e.DayIsFixed) e.Day = day;
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
        // 이 직원 노트에 인용 카드로 올라온 말(말한 사람 · 원문). 심문 답의 인용 카드는 대화 기록의
        // 순번을 모르므로 Id 로 짝을 지을 수 없다 — 원문으로 맞춘다.
        var quoted = new HashSet<string>(list.Where(e => e.NamedMention)
            .Select(e => e.SpeakerEmployeeId + "|" + e.QuotedLine.Trim()));
        foreach (string id in sim.GetActiveEmployeeIds())
        {
            if (id == targetEmployeeId) continue;
            foreach (var e in Build(id))
            {
                // 같은 말이 말한 사람 노트와 이 직원 노트에 서로 다른 카드로 올라와 있으면
                // (…이 한 말 / …의 증언) 이 직원 쪽 — 질문에 쓸 수 있는 쪽 — 한 장만 남긴다.
                if (seen.Contains(MirrorIdFor(e, targetEmployeeId))) continue;
                if (e.SpokenLine && quoted.Contains(e.SpeakerEmployeeId + "|" + (e.Body ?? "").Trim())) continue;
                if (!seen.Add(e.Id)) continue;   // 사고 기록처럼 주인이 없는 자료는 한 번만
                list.Add(e);
            }
        }
        return Sort(list);
    }

    // 다른 직원 노트의 카드가 이 직원 노트에서는 어떤 Id 로 올라와 있는가(없으면 빈 값).
    //   told:{말한 사람}:{이 직원}:{방}    ↔  say:{이 직원}:{말한 사람}:{방}
    //   toldnot:{말한 사람}:{이 직원}:{…}  ↔  notsee:{이 직원}:{말한 사람}:{…}
    //   said:{말한 사람}:{날}:{순번}        ↔  named:{이 직원}:{말한 사람}:{날}:{순번}
    private static string MirrorIdFor(InterviewEvidence e, string target)
    {
        if (e == null || string.IsNullOrEmpty(e.Id)) return "";
        var p = e.Id.Split(':', 4);
        if (p.Length < 4) return "";
        return p[0] switch
        {
            "told" when p[2] == target => $"say:{target}:{p[1]}:{p[3]}",
            "toldnot" when p[2] == target => $"notsee:{target}:{p[1]}:{p[3]}",
            "said" => $"named:{target}:{p[1]}:{p[2]}:{p[3]}",
            _ => "",
        };
    }

    // 시간이 없는 자료(기분)는 항상 맨 위에 둔다 — 근무 전에 적어 낸 것이므로.
    private static List<InterviewEvidence> Sort(List<InterviewEvidence> list) => list
        .OrderBy(e => e.HasTime ? 1 : 0)
        // 날을 먼저 가른다 — 지난 근무의 진술이 오늘 기록 사이에 시각 순으로 끼어들면
        // 같은 밤에 일어난 일처럼 읽힌다.
        .ThenBy(e => e.Day)
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
            // 쓰러진 **자리**는 로그 줄에 실려 오지 않는다(화면 줄은 방 색을 쓸 때만 방을
            // 들고 다닌다). 원본 기록에서 직접 찾아야 한다 — 이 방이 비어 있으면
            // "어디서 발견했는가 · 어떤 상태였는가" 를 아무에게도 물을 수 없고(§3-④),
            // 재석 주장으로도 쓸 수 없다(CanAnchorPosition 이 거짓이 된다).
            string fellIn = string.IsNullOrEmpty(r.RoomId) ? IncidentRoomOf(r) : r.RoomId;
            return new InterviewEvidence
            {
                Id = $"faint:{r.RelatedEmployeeId}:{r.Timestamp:0.00}",
                Kind = EvidenceKind.Incident,
                Header = "기절 기록",
                TimeText = DialogueClock.Text(r.Timestamp),
                Body = $"{Codename(r.RelatedEmployeeId)} - 스트레스 한계, 업무 불능",
                SubjectEmployeeId = r.RelatedEmployeeId,
                AnchorTime = r.Timestamp,
                HasTime = true,
                // 쓰러진 그 자리에 있었다는 뜻이다 — 위치 주장으로 쓸 수 있다.
                Position = PositionClaim.AtRoom,
                SubjectRoomId = fellIn,
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
            // 사망은 "생체 신호 소실" 로 — 누구의 신호가 끊겼는지는 본문과 VictimEmployeeId 가 말한다.
            Header = r.SourceEventType == LogEventType.Death ? "생체 신호 소실" : "사고 기록",
            VictimEmployeeId = r.SourceEventType == LogEventType.Death ? r.RelatedEmployeeId ?? "" : "",
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
            //
            // 예외가 하나 있다: 관리자가 그 방을 3초 이상 **직접 지켜보는 동안** 그 사람이
            // 화면 안에서 한 행동(ActorId/ActionText). 이것만 사람을 특정해 적는다.
            // 그래도 "봉쇄 장치를 고의로 망가뜨렸다" 로는 쓰지 않는다 — 화면에서 보인 것은
            // 어디까지나 "업무를 멈추고 설비 쪽으로 갔다" 까지다(지시서 §2-2).
            string detail = o.ActorId == target && !string.IsNullOrEmpty(o.ActionText)
                ? o.ActionText
                : (o.SuspiciousAction ? "설비 쪽에 접근" : "");
            var ev = new InterviewEvidence
            {
                // 순번을 넣지 않는다 — 시청 기록은 상한을 넘으면 앞에서부터 지워져 순번이 밀린다.
                Id = $"cctv:{target}:{o.RoomId}:{o.Time:0.0}",
                Kind = EvidenceKind.Cctv,
                Header = "CCTV",
                TimeText = DialogueClock.Text(o.Time),
                Body = string.IsNullOrEmpty(detail)
                    ? $"{RoomName(o.RoomId)}에서 확인"
                    : $"{RoomName(o.RoomId)}: {detail}",
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
                    ? $"{Codename(s.SpeakerId)}: {RoomName(s.RoomId)}에서 봤다"
                    : $"{Codename(s.SpeakerId)}: {s.Detail}",
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

        // "그 장면은 보지 못했다" 도 자료다 — 다만 **다른 종류의 자료**다.
        //
        // 관찰 범위를 적어 둔 카드이지 부재의 증거가 아니다. 이것으로 "그 사람은
        // 하지 않았다" 를 세울 수 없게 위치 주장(Position)도 행동(BehaviorDetail)도
        // 비워 둔다 — 모순 판정의 재료가 되지 않는다(기획안 §4).
        foreach (var s in PlayerKnownEvidence.NotObservedOf(target))
        {
            list.Add(new InterviewEvidence
            {
                Id = $"notsee:{target}:{s.SpeakerId}:{s.Detail}",
                Kind = EvidenceKind.Testimony,
                Header = Codename(s.SpeakerId) + "의 증언",
                TimeText = s.HasTime ? DialogueClock.Text(s.AnchorTime) : "",
                Body = KoreanParticle.Resolve($"{Codename(s.SpeakerId)}: {s.Detail}은/는 보지 못했다"),
                SubjectEmployeeId = target,
                SpeakerEmployeeId = s.SpeakerId,
                AnchorTime = s.AnchorTime,
                HasTime = s.HasTime,
                Position = PositionClaim.None,
                SubjectRoomId = s.RoomId,
                NotObserved = true,
                RelatedEmployeeIds = { s.SpeakerId },
            });
        }
    }

    // 이 직원이 **다른 사람에 대해 한** 증언도 자료다.
    //
    // 위쪽은 "남들이 이 사람에 대해 한 말" 이고, 이것은 "이 사람이 남에 대해 한 말" 이다.
    // 발언을 골라 되물으려면(기획안 §4) 말한 본인의 노트에도 그 발언이 있어야 한다.
    private static void AddOwnTestimonies(List<InterviewEvidence> list, string target)
    {
        foreach (var s in PlayerKnownEvidence.SightingsBy(target))
        {
            if (s.SubjectId == target) continue;
            list.Add(new InterviewEvidence
            {
                Id = $"told:{target}:{s.SubjectId}:{s.RoomId}",
                Kind = EvidenceKind.Testimony,
                Header = Codename(target) + "이/가 한 말",
                TimeText = s.HasTime ? DialogueClock.Text(s.AnchorTime) : "",
                Body = string.IsNullOrEmpty(s.Detail)
                    ? $"{Codename(s.SubjectId)}을/를 {RoomName(s.RoomId)}에서 봤다"
                    : $"{Codename(s.SubjectId)}: {s.Detail}",
                BehaviorDetail = s.Detail,
                SubjectEmployeeId = s.SubjectId,
                SpeakerEmployeeId = target,
                AnchorTime = s.AnchorTime,
                HasTime = s.HasTime,
                Position = PositionClaim.None,   // 남의 위치 주장이다 — 본인 위치가 아니다
                SubjectRoomId = s.RoomId,
                RelatedEmployeeIds = { s.SubjectId },
            });
        }
        foreach (var s in PlayerKnownEvidence.NotObservedBy(target))
        {
            list.Add(new InterviewEvidence
            {
                Id = $"toldnot:{target}:{s.SubjectId}:{s.Detail}",
                Kind = EvidenceKind.Testimony,
                Header = Codename(target) + "이/가 한 말",
                TimeText = s.HasTime ? DialogueClock.Text(s.AnchorTime) : "",
                Body = KoreanParticle.Resolve($"{Codename(s.SubjectId)}: {s.Detail}은/는 보지 못했다"),
                SubjectEmployeeId = s.SubjectId,
                SpeakerEmployeeId = target,
                AnchorTime = s.AnchorTime,
                HasTime = s.HasTime,
                Position = PositionClaim.None,
                SubjectRoomId = s.RoomId,
                NotObserved = true,
                RelatedEmployeeIds = { s.SubjectId },
            });
        }
    }

    // --- 다른 직원이 이 직원의 이름을 댄 말 --------------------------------
    //
    // "강아지 씨가 좀 이상했어요" 처럼 남이 이 직원의 **이름을 직접 대며** 한 말. 목격 증언(위)으로
    // 정리되지 않는 답이 많다 — 누구에게 알렸는지, 누구에게 들었는지, 엿들은 잡담의 한 줄 같은 것들.
    // 전에는 그런 말이 말한 사람의 노트에만 남거나(자료를 들고 물은 답이면) 아예 카드가 되지 않아서,
    // 그 이름의 주인에게 사고 기록과 함께 들이밀 수가 없었다(2026-10-30 플레이 피드백).
    //
    // 들은 말을 원문 그대로 싣는다. 요약하면 그 사람이 하지 않은 말이 생긴다.
    private static void AddNamedMentions(List<InterviewEvidence> list, string target)
    {
        var history = DialogueHistory.Instance?.GetAllEntries();
        if (history != null)
        {
            foreach (var e in history)
            {
                // 관리자의 말(질문)은 빼고 직원의 말만 — 질문에 이름이 들어 있는 건 당연하다.
                if (e.EntryType == DialogueEntryType.PlayerChoice) continue;
                // 휴게시간 심문의 답은 아래(MentionsOf)가 맡는다 — 화면이 같은 답을 대화 기록에도 남기므로
                // 여기서도 만들면 한 마디가 두 장이 된다. 거기는 질문이 가리킨 사건 · 방 · 사람까지 들고 있다.
                if (e.ConversationType == DialogueConversationType.Interview) continue;
                if (string.IsNullOrEmpty(e.SpeakerId) || e.SpeakerId == target) continue;
                if (!NamesEmployee(e.Text, target)) continue;

                // 사고 자료를 들고 한 질문의 답이면 그 사고의 시각 · 방을 가리킨다.
                // 아니면 그 말을 한 시각에 둔다(원문 발언 카드와 같다).
                string key = LinkedIncidentKey(e.EvidenceIds);
                float time = IncidentTimeOfKey(key, e.Timestamp);
                list.Add(NamedCard($"named:{target}:{e.SpeakerId}:{e.Day}:{e.Seq}", target, e.SpeakerId,
                    e.Text, e.Day, time, IncidentRoomOfKey(key), key));
            }
        }

        // 휴게시간 심문의 답 — 자료를 들고 물은 답이면 그 자료의 사건 · 방 · 시각을 가리킨다.
        // 심문 답은 대화 기록(DialogueHistory, 통화 전용)에 남지 않으므로 따로 받아 둔 것을 읽는다
        // (InterviewSession.NoteMentions). 예전에는 이 길이 없어 "고양이에게 사고 기록을 들고 물었더니
        // 강아지가 수상했다고 했다" 는 말이 강아지 노트에 오지 않았다(2026-10-30 사용자 지시).
        foreach (var m in PlayerKnownEvidence.MentionsOf(target))
        {
            float time = m.HasTime ? m.Time : IncidentTimeOfKey(m.IncidentKey, -1f);
            string room = !string.IsNullOrEmpty(m.RoomId) ? m.RoomId : IncidentRoomOfKey(m.IncidentKey);
            list.Add(NamedCard($"named:{target}:{m.SpeakerId}:ask:{m.Seq}", target, m.SpeakerId,
                m.Line, m.Day, Mathf.Max(0f, time), room, m.IncidentKey, hasTime: time >= 0f));
        }

        // 엿들은 대화 — 대화에 끼지 않은 제3자의 이름이 나온 줄. 대화에 낀 사람이면 엿들은 대화
        // 카드가 이미 그 사람 노트에 있다(AddOverheard).
        foreach (var h in PlayerKnownEvidence.OverheardToday())
        {
            if (h.A == target || h.B == target) continue;
            foreach (var (speaker, line) in new[] { (h.A, h.LineA), (h.B, h.LineB) })
            {
                if (string.IsNullOrEmpty(speaker) || !NamesEmployee(line, target)) continue;
                list.Add(NamedCard($"named:{target}:{speaker}:heard:{h.RoomId}:{h.Time:0.0}", target, speaker,
                    line, h.Day, h.Time, h.RoomId, ""));
            }
        }
    }

    // hasTime 이 거짓이면 언제 일을 말한 것인지 모른다(자료 없이 물은 질문의 답) — 시각을 붙이지 않는다.
    private static InterviewEvidence NamedCard(string id, string target, string speaker, string line, int day,
        float time, string roomId, string incidentKey, bool hasTime = true)
    {
        var ev = new InterviewEvidence
        {
            Id = id,
            Kind = EvidenceKind.Testimony,
            Day = day, SubjectDay = day,
            DayIsFixed = true,
            Header = Codename(speaker) + "의 증언",
            TimeText = hasTime ? DialogueClock.Text(time) : "",
            Body = line,
            QuotedLine = line,
            SubjectEmployeeId = target,
            SpeakerEmployeeId = speaker,
            AnchorTime = time,
            HasTime = hasTime,
            // 이름을 댄 것이지 그 사람이 어디 있었다고 말한 것이 아니다 — 위치 추궁의 근거가 아니다.
            Position = PositionClaim.None,
            SubjectRoomId = roomId ?? "",
            IncidentKey = incidentKey ?? "",
        };
        ev.RelatedEmployeeIds.Add(speaker);
        ev.MentionedEmployeeIds.Add(target);
        return ev;
    }

    // 문장이 그 직원의 코드네임을 **이름으로** 부르는가.
    //
    // "고양이" 안의 "양" 처럼 다른 낱말의 일부로 들어간 것은 이름이 아니다 — 앞 글자가 한글이면
    // 낱말의 중간이다. 한 글자 이름(양)은 뒤도 본다: 조사 · 호칭이 붙거나 낱말이 끝나야 이름이다
    // ("양쪽" · "양호" 는 이름이 아니다).
    public static bool NamesEmployee(string text, string employeeId)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(employeeId)) return false;
        string name = Codename(employeeId);
        // 이름표를 못 찾으면(시뮬레이션이 없는 검사 등) id 가 돌아온다 — 영문 id 로는 찾지 않는다.
        if (string.IsNullOrEmpty(name) || name == employeeId) return false;

        for (int i = text.IndexOf(name, System.StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(name, i + name.Length, System.StringComparison.Ordinal))
        {
            int end = i + name.Length;
            if (i > 0 && IsHangul(text[i - 1])) continue;
            if (name.Length == 1 && end < text.Length && IsHangul(text[end])
                && !SingleNameSuffixes.Any(s => string.CompareOrdinal(text, end, s, 0, s.Length) == 0))
                continue;
            return true;
        }
        return false;
    }

    // 문장이 가리키는 작업실 — 이름이 **딱 하나** 나올 때만(둘 이상이면 어느 쪽 일인지 모른다).
    // 자료 없이 물은 질문의 답("코어실에서 강아지 씨가 서성였어요")을 그 방의 사고와 잇는 데 쓴다.
    public static string RoomNamedIn(string text)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(text)) return "";
        string found = "";
        foreach (string roomId in sim.GetRoomIds())
        {
            string name = sim.GetRoomDef(roomId)?.DisplayName ?? "";
            if (name.Length < 2 || !text.Contains(name, System.StringComparison.Ordinal)) continue;
            if (!string.IsNullOrEmpty(found)) return "";
            found = roomId;
        }
        return found;
    }

    // 한 글자 이름 뒤에 붙어도 이름으로 읽히는 것들.
    private static readonly string[] SingleNameSuffixes =
        { "씨", "님", "직원", "이", "은", "을", "의", "도", "만", "과", "하고", "한테", "에게", "랑" };

    private static bool IsHangul(char c) => c >= '가' && c <= '힣';

    // 그 말이 들고 있던 자료 가운데 사고 자료의 사건 키(없으면 빈 값). 자료 Id 는 "incident:{키}" 이고,
    // 같은 순간의 사고가 여럿이면 뒤에 "#2" 가 붙는다(AddLogRows).
    private static string LinkedIncidentKey(IEnumerable<string> evidenceIds)
    {
        const string prefix = "incident:";
        foreach (string id in evidenceIds ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
            string key = id[prefix.Length..];
            int dup = key.IndexOf('#');
            return dup >= 0 ? key[..dup] : key;
        }
        return "";
    }

    // 사건 키("{종류}:{방}:{시각}")의 시각. 읽지 못하면 fallback.
    // 키는 IncidentKeyOf 가 지금 문화권 형식으로 찍으므로 읽을 때도 같은 형식으로 읽는다.
    private static float IncidentTimeOfKey(string incidentKey, float fallback)
    {
        if (string.IsNullOrEmpty(incidentKey)) return fallback;
        var parts = incidentKey.Split(':');
        return parts.Length >= 3 && float.TryParse(parts[2], out float t) ? t : fallback;
    }

    // --- 이 직원이 앞서 한 진술 -------------------------------------------

    // 날을 가리지 않는다 — **어제 한 말도 오늘 들고 추궁할 수 있어야 한다.**
    //
    // 전에는 오늘 한 말만(StatementsBy) 올라와서, DAY1 에 받아 둔 위치 진술을 DAY3 에
    // 찾을 수가 없었다. 기록은 PlayerKnownEvidence 에 그대로 남아 있는데 화면에 길이
    // 없었던 것이다.
    private static void AddOwnStatements(List<InterviewEvidence> list, string target)
    {
        foreach (var st in PlayerKnownEvidence.AllStatementsBy(target))
        {
            // 번복 이력을 **각각 한 장씩** 꺼낸다. 마지막이 지금의 주장이고 그 앞은 정정 전이다.
            //
            // RoomHistory 에 값만 쌓아 두고 화면에는 최신 발언만 올리면, 진술을 보존한
            // 의미가 없다 — 플레이어가 "아까는 이렇게 말했다" 를 손에 쥘 수 없으니
            // 번복 자체를 추궁할 수 없다.
            var rooms = st.RoomHistory.Count > 0
                ? (IReadOnlyList<string>)st.RoomHistory
                : new[] { st.RoomId };
            for (int i = 0; i < rooms.Count; i++)
            {
                bool latest = i == rooms.Count - 1;
                string room = rooms[i];
                list.Add(new InterviewEvidence
                {
                    // 위치 진술은 (말한 사람 · 사건) 으로 한 건이다(RecordLocationStatement 와 같은 기준).
                    // 같은 사건 키라도 가리키는 날이 다르면 다른 진술이다.
                    // 정정 전 진술은 뒤에 순번을 붙여 서로 다른 자료로 갈라 둔다.
                    Id = $"claim:{target}:{st.SubjectDay}:{st.IncidentKey}" + (latest ? "" : $"#was{i + 1}"),
                    Kind = EvidenceKind.OwnStatement,
                    Day = st.Day, SubjectDay = st.SubjectDay,
                    DayIsFixed = true,
                    Header = Codename(target) + "의 진술" + (latest ? "" : "(정정 전)"),
                    TimeText = st.HasTime ? DialogueClock.Text(st.AnchorTime) : "",
                    Body = (latest ? "본인: " : "본인: 처음엔 ") + $"{RoomName(room)}에 있었다",
                    SubjectEmployeeId = target,
                    SpeakerEmployeeId = target,
                    AnchorTime = st.AnchorTime,
                    HasTime = st.HasTime,
                    Position = PositionClaim.AtRoom,
                    SubjectRoomId = room,
                    IncidentKey = st.IncidentKey,
                    Superseded = !latest,
                });
            }
        }
    }

    // --- 요약하지 않은 원문 발언 -------------------------------------------
    //
    // "오늘은 별일 없었습니다" 처럼 **뭉뚱그린 말**은 어느 요약 카드에도 올라오지 않는다.
    // 위치도 행동도 말하지 않으니 요약할 알맹이가 없기 때문이다. 그런데 추리에서는 바로
    // 그 말이 중요하다 — 사고 기록을 들이밀어 "그 말에 이것도 포함됩니까" 를 물을 수 있어야
    // 한다(기획안 §4).
    //
    // 그래서 **아직 어떤 자료와도 묶이지 않은 발언**만 원문 그대로 카드가 된다. 자료를 들고
    // 한 질문의 답은 이미 그 자료 밑에 요약으로 붙으므로 여기 들어오지 않는다. 그 조건이
    // 이 목록이 대화 전체로 불어나지 않게 막아 주는 문이다.
    private static void AddSpokenLines(List<InterviewEvidence> list, string target)
    {
        var history = DialogueHistory.Instance?.GetAllEntries();
        if (history == null) return;

        foreach (var e in history)
        {
            if (e.SpeakerId != target) continue;
            // **질문에 대한 답만** 자료가 된다(NpcResponse).
            //
            // 인사말·전화 첫마디(NpcLine)는 주장이 아니다. "오셨어요, 뭐가 궁금하세요?" 를
            // 카드로 올려 두면 "그 말은 어디까지입니까" 를 묻게 된다 — 조사가 아니라 희롱이다.
            if (e.EntryType != DialogueEntryType.NpcResponse) continue;
            if (e.EvidenceIds.Count > 0) continue;        // 이미 자료에 묶인 답
            if (string.IsNullOrWhiteSpace(e.Text)) continue;

            list.Add(new InterviewEvidence
            {
                Id = $"said:{target}:{e.Day}:{e.Seq}",
                Kind = EvidenceKind.OwnStatement,
                Day = e.Day, SubjectDay = e.Day,
                DayIsFixed = true,
                Header = Codename(target) + "이/가 한 말",
                TimeText = DialogueClock.Text(e.Timestamp),
                Body = e.Text,
                SubjectEmployeeId = target,
                SpeakerEmployeeId = target,
                AnchorTime = e.Timestamp,
                HasTime = true,
                // 위치도 행동도 주장하지 않는 문장이다 — 모순 판정의 재료가 되지 않는다.
                Position = PositionClaim.None,
                SpokenLine = true,
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
                Body = $"본인: {b.Text}",
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
                Body = struck ? $"{RoomName(room)}: 이상 개체 접촉 사고 당시 그 방에 있었다"
                              : $"{RoomName(room)}: 이상 개체가 나타났을 때 그 방에 있었다",
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
    internal static string IncidentRoomOfKey(string incidentKey)
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
