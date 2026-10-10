using Godot;
using NSP.Core;
using NSP.Data;
using System.Collections.Generic;
using System.Linq;

namespace NSP.Dialogue;

// 「자료 한 장 → 물어볼 수 있는 것들 → 질문 문장」.
//
// 질문 문장은 반드시 그 자료의 값(시각·작업실·상대)을 그대로 인용한다.
// 문장을 조립할 때 다른 사건의 값을 끌어오지 않는다 — 옛 꼬리질문이 엉뚱한 방 이름을
// 말하던 원인이 그것이었다.
public static class InterviewQuestionFactory
{
    // 증거 없이도 언제든 물을 수 있는 기본 질문. 인터뷰의 중심이 아니라 도입부다.
    public static List<InterviewQuestion> BasicQuestions(string targetEmployeeId)
    {
        var list = new List<InterviewQuestion>();
        void Add(InterviewIntent intent, string text) => list.Add(new InterviewQuestion
        {
            TargetEmployeeId = targetEmployeeId, Intent = intent, Text = text,
        });

        Add(InterviewIntent.BasicShift, "오늘 근무는 어땠습니까?");
        Add(InterviewIntent.BasicAnomaly, "오늘 이상한 점을 느꼈습니까?");
        Add(InterviewIntent.BasicSuspicious, "수상한 행동을 한 사람을 봤습니까?");
        return list;
    }

    // 이 자료로 물어볼 수 있는 것들.
    public static List<InterviewQuestion> For(string targetEmployeeId, InterviewEvidence ev)
    {
        var list = new List<InterviewQuestion>();
        if (ev == null || string.IsNullOrEmpty(targetEmployeeId)) return list;

        // 다른 직원의 자료로는 이 사람에게 물을 수 없다(사고 기록은 주인이 없으므로 예외).
        //
        // 기절 기록은 한 번 더 예외다 — 쓰러진 사람의 자료이지만, 그 자리에 있었거나
        // 실제로 옮긴 **다른 직원**에게 물어야 발견·구조 경위가 나온다(기획안 §3-④ · §4).
        // 누구에게 열리는지는 아래 분기에서 실제 기록으로 다시 가린다.
        if (!string.IsNullOrEmpty(ev.SubjectEmployeeId) && ev.SubjectEmployeeId != targetEmployeeId
            && !IsFaintCard(ev))
            return list;

        switch (ev.Kind)
        {
            // 이동 — 지시 없는 이동은 "근무지 이탈 아니냐"고 따질 수 있다. 어디로 갔는지 · 몇 시였는지는
            // 기록에 이미 있으니 묻지 않는다(다음 행선지는 답변 뒤 꼬리질문으로만).
            // 이동 — **사유에 따라 첫 질문이 다르다**(기획안 §3-⑤).
            // 한 벌의 질문을 모든 이동에 똑같이 붙이면, 쓰러져 실려 간 사람에게
            // "왜 근무지를 벗어났습니까" 를 묻게 된다.
            case EvidenceKind.Movement:
            {
                string kind = InterviewReplyPlanner.MoveKindOf(
                    targetEmployeeId, ev.Day, ev.ToRoomId, ev.AnchorTime, ev.PlayerOrdered);

                // 의식이 없는 채로 실려 간 이동은 본인에게 이유를 묻지 않는다.
                // 물을 수 있는 것은 "정신을 차렸을 때 어땠는가" 쪽이다.
                if (kind != "carried") Add(list, targetEmployeeId, ev, InterviewIntent.AskMoveReason);

                switch (kind)
                {
                    case "dispatched":   // 사고 대응 지시 — 현장이 어땠는지가 핵심이다
                        Add(list, targetEmployeeId, ev, InterviewIntent.AskArrivalState);
                        break;
                    case "ordered":      // 관리자가 배치했다 — 가서 이상한 점이 없었는지
                        Add(list, targetEmployeeId, ev, InterviewIntent.AskArrivalState);
                        break;
                    case "plain":        // 업무 기록 없는 이동 — 근무지 이탈로 따질 수 있다
                        if (!ev.PlayerOrdered)
                            Add(list, targetEmployeeId, ev, InterviewIntent.ConfrontUnorderedMove);
                        break;
                }

                // 도착한 방에 실제로 동료가 있었을 때만 "만나러 갔는가" 를 물을 수 있다.
                // 없는 만남을 전제한 질문은 만들지 않는다.
                string met = ColleagueAt(targetEmployeeId, ev);
                if (!string.IsNullOrEmpty(met) && kind is "plain" or "task")
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskVisitPurpose, met);

                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                if (kind != "carried") Add(list, targetEmployeeId, ev, InterviewIntent.AskActionAtDestination);
                break;
            }

            // 사고 — 알리바이를 대라 · 증명하라 · 무관하다는 근거를 대라 · 누가 그랬다고 보나.
            // "기록상 거기 있었다"는 관리자가 실제로 그 재석을 알 때만 열린다.
            // 기절 기록 — 쓰러진 사람 · 그 자리에 있던 사람 · 실제로 옮긴 사람에게
            // 물을 것이 전부 다르다(기획안 §3-④). 의식이 없던 사람에게 "그때 뭘 봤나" 를
            // 물으면 안 되고, 같은 방에 있었다는 이유만으로 "구조했나" 를 물어도 안 된다.
            case EvidenceKind.Incident when IsFaintCard(ev):
            {
                bool isVictim = ev.SubjectEmployeeId == targetEmployeeId;
                if (isVictim)
                {
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskLastMemory);
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskWokeWhere);
                    break;   // 의식이 없던 동안의 일은 묻지 않는다
                }
                bool wasThere = DialogueContextBuilder
                    .OccupantsAt(ev.SubjectRoomId, ev.SubjectDay, ev.AnchorTime, ev.SubjectEmployeeId)
                    .Contains(targetEmployeeId);
                if (wasThere)
                {
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskFoundWhere, ev.SubjectEmployeeId);
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskFoundCondition, ev.SubjectEmployeeId);
                }
                // 옮긴 기록이 실제로 있을 때만 구조를 묻는다.
                if (Transported(targetEmployeeId, ev))
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskRescueAction, ev.SubjectEmployeeId);
                if (wasThere || Transported(targetEmployeeId, ev))
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                break;
            }

            // 정전 — 직접 겪었는가 · 경보만 들었는가 · 나중에 들었는가를 가른다(기획안 §3-①).
            case EvidenceKind.Incident when ev.IncidentType == LogEventType.PowerOutage:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskBlackoutExperience);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskBlackoutSigns);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskAfterBlackoutMet);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhereAtIncident);
                if (WorkedInRoom(targetEmployeeId, ev))
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskInspectionWork);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskSuspectOpinion);
                break;

            case EvidenceKind.Incident:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskIncidentKnown);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhereAtIncident);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskAlibiProof);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskProveInnocence);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskSuspectOpinion);
                if (PlayerKnowsPresence(targetEmployeeId, ev)) Add(list, targetEmployeeId, ev, InterviewIntent.PressPresence);

                // ── 설비 쪽으로 좁혀 들어가는 질문(기획안 §3-②) ──
                // 설비가 걸린 사고일 때만. 사망·금기 위반에는 "설정을 바꿨습니까" 가 의미 없다.
                if (IsEquipmentIncident(ev.IncidentType))
                {
                    Add(list, targetEmployeeId, ev, InterviewIntent.AskEquipmentFault);

                    // 그 방에서 실제로 작업한 기록이 있을 때만 "어떤 점검을 했는가" 를 묻는다.
                    if (WorkedInRoom(targetEmployeeId, ev))
                        Add(list, targetEmployeeId, ev, InterviewIntent.AskInspectionWork);

                    // 핵심 질문. 관리자가 **쥐고 있는 근거**가 하나라도 있을 때 열린다 —
                    // 시스템만 아는 사실로 질문을 열면 질문 목록 자체가 정답을 흘린다.
                    if (PlayerLinksToRoom(targetEmployeeId, ev))
                        Add(list, targetEmployeeId, ev, InterviewIntent.AskEquipmentTouch);

                    // 사고를 안 사람에게만 "누구에게 알렸는가" 를 묻는다.
                    if (KnewIncident(targetEmployeeId, ev))
                        Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoReported);
                }
                break;

            // 통화 — 왜 걸었나 · 끝나고 무엇을 했나 · (놀러 가겠다는 전화가 잦으면) 근무 태만 아니냐.
            case EvidenceKind.Call:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskCallReason);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskCallAfter);
                if (ev.CallEvent == DialogueRepository.EventIdleVisit
                    && InterviewEvidenceBoard.IdleVisitCallCount(targetEmployeeId) >= 2)
                    Add(list, targetEmployeeId, ev, InterviewIntent.ConfrontNeglect);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                break;

            // 이상 개체 조우 — 괜찮은가 · 어떻게 생겼나 · 무엇을 했나 · 같이 있던 사람은.
            // 이상 개체 — "무엇을 보았는가" 앞에 "직접 보기는 했는가" 를 둔다.
            // 모습을 못 보고 소리만 들은 사람에게 생김새를 물으면 없는 목격이 만들어진다.
            case EvidenceKind.Anomaly:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskAnomalySeenHow);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskGhostWellbeing);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskGhostAppearance);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskGhostWhatHappened);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskAnomalyDirection);
                if (ev.RelatedEmployeeIds.Count > 0) Add(list, targetEmployeeId, ev, InterviewIntent.AskGhostOthers);
                break;

            case EvidenceKind.Cctv:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskPresenceReason);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskActionAtDestination);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskRouteAround);
                break;

            case EvidenceKind.Testimony:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskConfirmTestimony);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskPresenceReason);
                break;

            case EvidenceKind.OwnStatement:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskRestate);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskWhoWasPresent);
                break;

            // 엿들은 대화 — 그때 그 방에서 무엇을 하고 있었는지부터 묻는다.
            // (대화 내용 자체로 추궁하는 질문은 아직 없다 — 지금은 재석 근거로만 쓴다.)
            case EvidenceKind.Overheard:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskPresenceReason);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskActionAtDestination);
                break;

            case EvidenceKind.Mood:
                Add(list, targetEmployeeId, ev, InterviewIntent.AskMoodReason);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskMoodBefore);
                Add(list, targetEmployeeId, ev, InterviewIntent.AskMoodRelated);
                break;
        }
        return list;
    }

    private static void Add(List<InterviewQuestion> list, string target, InterviewEvidence ev,
        InterviewIntent intent, string otherId = "")
    {
        var q = Make(target, ev, intent, otherId);
        if (!string.IsNullOrEmpty(q.Text)) list.Add(q);
    }

    // 기절 기록인가. 사고 자료와 같은 종류로 들어오지만 물을 것이 전혀 다르다.
    public static bool IsFaintCard(InterviewEvidence ev) =>
        ev != null && ev.Kind == EvidenceKind.Incident
        && !string.IsNullOrEmpty(ev.SubjectEmployeeId)
        && ev.Id.StartsWith("faint:");

    // 이 직원이 쓰러진 동료를 **실제로 옮긴** 기록이 있는가.
    // 같은 방에 있었다는 것만으로 구조했다고 단정하지 않기 위한 문이다(기획안 §3-④).
    public static bool Transported(string target, InterviewEvidence ev)
    {
        if (ev == null || string.IsNullOrEmpty(ev.SubjectEmployeeId)) return false;
        // 시간 창으로 자르지 않는다 — 이송은 쓰러진 뒤 얼마 만에 이뤄질지 정해져 있지
        // 않다(구조자가 멀리 있으면 늦는다). 대신 **그 환자를 옮겼다는 기록** 인지를 본다.
        string victim = InterviewEvidenceBoard.Codename(ev.SubjectEmployeeId);
        return EventLog.Instance?.GetAllEntries().Any(e => e.Day == ev.SubjectDay
            && e.ActorEmployeeId == target
            && e.GameTimeSeconds >= ev.AnchorTime - 1f
            && (e.Description ?? "").Contains("이송")
            && (e.Description ?? "").Contains(victim)) ?? false;
    }

    // 설비가 걸린 사고인가. 여기에만 "설정을 바꿨습니까" 가 성립한다.
    public static bool IsEquipmentIncident(LogEventType type) =>
        type is LogEventType.TaskFailed or LogEventType.PowerOutage
            or LogEventType.Sabotage or LogEventType.CctvDisconnect;

    // 그 방에서 이 직원이 실제로 작업한 기록이 있는가(점검·수리 질문의 조건).
    public static bool WorkedInRoom(string target, InterviewEvidence ev)
    {
        if (ev == null || string.IsNullOrEmpty(ev.SubjectRoomId)) return false;
        float window = EvidenceContradiction.WindowMinutes * DialogueClock.SecondsPerMinute * 2f;
        return EventLog.Instance?.GetAllEntries().Any(e => e.Day == ev.SubjectDay
            && e.ActorEmployeeId == target && e.RoomId == ev.SubjectRoomId
            && e.EventType is LogEventType.TaskStart or LogEventType.TaskComplete
            && Mathf.Abs(e.GameTimeSeconds - ev.AnchorTime) <= window) ?? false;
    }

    // 관리자가 이 직원과 그 방을 잇는 **근거를 쥐고 있는가**.
    //
    // 재석을 알거나, CCTV 로 그 방에서 봤거나, 시설 로그에 그 방으로 가는 이동이 떴을 때.
    // 시스템만 아는 사실로 질문을 열면 "이 질문이 떴다 = 범인이다" 가 되어 추리가 사라진다.
    public static bool PlayerLinksToRoom(string target, InterviewEvidence ev)
    {
        if (ev == null || !ev.HasTime || string.IsNullOrEmpty(ev.SubjectRoomId)) return false;
        if (PlayerKnowsPresence(target, ev)) return true;
        float window = EvidenceContradiction.WindowMinutes * DialogueClock.SecondsPerMinute * 2f;
        if (PlayerKnownEvidence.CctvSeenRoomOf(target, ev.AnchorTime, window) == ev.SubjectRoomId) return true;
        return PlayerKnownEvidence.VisibleMoves(target, ev.SubjectDay)
            .Any(m => m.ToRoomId == ev.SubjectRoomId && Mathf.Abs(m.Timestamp - ev.AnchorTime) <= window * 2f);
    }

    // 이 직원이 그 사고를 실제로 알고 있었는가.
    public static bool KnewIncident(string target, InterviewEvidence ev)
    {
        var entry = DialogueContextBuilder.FindByKey(ev?.SubjectDay ?? 0, ev?.IncidentKey ?? "");
        return entry != null && DialogueContextBuilder.KnowledgeOf(target, entry) != KnowledgeLevel.None;
    }

    // 그 이동으로 도착한 방에 **실제로 같이 있던 동료**. 없으면 빈 값.
    //
    // 시스템이 모르는 만남을 지어내지 않기 위한 문이다 — 이 값이 비어 있으면
    // "그 직원을 만나러 갔습니까" 라는 질문 자체가 생기지 않는다.
    public static string ColleagueAt(string target, InterviewEvidence ev)
    {
        if (ev == null || !ev.HasTime || string.IsNullOrEmpty(ev.ToRoomId)) return "";
        var others = DialogueContextBuilder.OccupantsAt(ev.ToRoomId, ev.SubjectDay, ev.AnchorTime, target);
        return others.Count > 0 ? others[0] : "";
    }

    // 사고 시각에 이 직원이 사고 난 방에 있었다는 것을 관리자가 아는가 — CCTV 로 직접 봤거나, 시설 로그
    // 화면의 배치 · 이동 줄이 그 방을 가리킬 때. 그때만 "기록상 거기 있었다"고 따질 수 있다.
    // (시스템만 아는 사실로 추궁 문장을 만들지 않는다 — PlayerKnownEvidence 의 규칙.)
    public static bool PlayerKnowsPresence(string target, InterviewEvidence incident)
    {
        if (incident == null || !incident.HasTime || string.IsNullOrEmpty(incident.SubjectRoomId)) return false;
        float window = EvidenceContradiction.WindowMinutes * DialogueClock.SecondsPerMinute;
        if (PlayerKnownEvidence.CctvSeenRoomOf(target, incident.AnchorTime, window) == incident.SubjectRoomId) return true;
        // 자료의 날로 본다. 오늘로 고정하면 어제 사고를 두고 오늘 동선으로 "거기 있었다"를
        // 판정해, 있지도 않았던 재석을 근거로 추궁 질문이 열린다.
        int day = incident.Day > 0 ? incident.Day : DialogueContextBuilder.Day();
        return DialogueContextBuilder.RoomAt(target, day, incident.AnchorTime) == incident.SubjectRoomId;
    }

    // 자료의 값을 그대로 옮겨 담은 질문 하나.
    // otherId 를 주면 "그 질문이 가리키는 다른 직원" 을 자료 대신 직접 지정한다.
    // 목격 / 동료 방문 질문처럼 상대가 자료가 아니라 **답변에서 나온 사람** 일 때 쓴다.
    public static InterviewQuestion Make(string target, InterviewEvidence ev, InterviewIntent intent,
                                         string otherId = "")
    {
        var q = new InterviewQuestion
        {
            TargetEmployeeId = target,
            Intent = intent,
            EvidenceId = ev?.Id ?? "",
            IncidentKey = ev?.IncidentKey ?? "",
            IncidentType = ev?.IncidentType ?? default,
            AnchorTime = ev?.AnchorTime ?? 0f,
            HasAnchorTime = ev?.HasTime ?? false,
            // 자료의 날짜를 그대로 물려받는다 — 답변은 이 날의 동선으로 계산된다.
            AnchorDay = ev?.Day ?? 0,
            FromRoomId = ev?.FromRoomId ?? "",
            ToRoomId = ev?.ToRoomId ?? "",
            SubjectRoomId = ev?.SubjectRoomId ?? "",
            // 개체 조우 자료는 "그때 같이 있던 사람"이 곧 질문의 상대다.
            OtherEmployeeId = ev?.Kind == EvidenceKind.Anomaly ? ev.RelatedEmployeeIds.FirstOrDefault() ?? ""
                : ev?.SpeakerEmployeeId == target ? "" : ev?.SpeakerEmployeeId ?? "",
            PlayerOrderedMove = ev?.PlayerOrdered ?? false,
            MoodText = ev?.MoodText ?? "",
            CallEvent = ev?.CallEvent ?? "",
            CallKind = ev?.CallKind ?? default,
            CallRoomId = ev?.CallRoomId ?? "",
            RepeatIndex = ev?.RepeatIndex ?? 0,
        };
        // 근무 태만 추궁은 카드 한 장이 아니라 오늘 전체 횟수를 말한다.
        if (intent == InterviewIntent.ConfrontNeglect) q.RepeatIndex = InterviewEvidenceBoard.IdleVisitCallCount(target);
        if (!string.IsNullOrEmpty(otherId)) q.OtherEmployeeId = otherId;
        q.Text = KoreanParticle.Resolve(Text(q, ev));
        return q;
    }

    // 질문 문장. 시각은 자료에 시각이 있을 때만 말한다.
    private static string Text(InterviewQuestion q, InterviewEvidence ev)
    {
        string at = q.HasAnchorTime ? DialogueClock.Spoken(q.AnchorTime) + "경 " : "";
        string from = InterviewEvidenceBoard.RoomName(q.FromRoomId);
        string to = InterviewEvidenceBoard.RoomName(q.ToRoomId);
        string here = InterviewEvidenceBoard.RoomName(q.SubjectRoomId);
        string who = InterviewEvidenceBoard.Codename(q.OtherEmployeeId);

        return q.Intent switch
        {
            InterviewIntent.AskMoveReason =>
                $"{at}{from}에서 {to}으로/로 이동한 이유는 무엇입니까?",
            InterviewIntent.AskWhoWasPresent =>
                $"{at}함께 있던 직원이 있었습니까?",
            InterviewIntent.AskNextLocation =>
                $"{to}을/를 나온 뒤에는 어디로 이동했습니까?",
            InterviewIntent.AskActionAtDestination =>
                $"{here}에서는 무엇을 했습니까?",

            InterviewIntent.AskIncidentKnown =>
                string.IsNullOrEmpty(here)
                    ? $"{at}이 사고를 알고 있었습니까?"
                    : $"{at}{here}에서 난 이 사고를 알고 있었습니까?",
            InterviewIntent.AskWhereAtIncident =>
                $"{at}당신은 어디에 있었습니까?",
            InterviewIntent.AskBeforeIncident =>
                $"이 사고 직전에는 무엇을 하고 있었습니까?",
            InterviewIntent.AskWhoSeenNear =>
                $"{at}그 근처에서 누구를 봤습니까?",

            InterviewIntent.AskPresenceReason =>
                $"{at}{here}에 있었던 이유는 무엇입니까?",
            InterviewIntent.AskRouteAround =>
                $"{at}전후로 어떤 경로로 움직였습니까?",

            InterviewIntent.AskConfirmTestimony =>
                $"{who} 직원은 {at}{here}에서 당신을 봤다고 진술했습니다. 사실입니까?",
            InterviewIntent.AskRestate =>
                $"{at}{here}에 있었다고 하셨습니다. 다시 설명해 주십시오.",

            InterviewIntent.AskMoodReason =>
                $"오늘 기분을 '{q.MoodText}'이라고/라고 적으셨습니다. 이유가 무엇입니까?",
            InterviewIntent.AskMoodBefore =>
                "근무 전부터 그런 상태였습니까?",
            InterviewIntent.AskMoodRelated =>
                "특정 직원이나 사건과 관련이 있습니까?",

            InterviewIntent.FollowExactTime =>
                "정확히 몇 시였습니까?",

            // ── 이동 따지기 ──
            InterviewIntent.ConfrontUnorderedMove =>
                $"{at}지시도 없이 {from}에서 {to}으로/로 이동했습니다. 근무지 이탈 아닙니까?",

            // ── 2단계 · 이동 사유별 첫 질문 ──
            InterviewIntent.AskArrivalState =>
                $"{to}에 도착했을 때 어떤 상황이었습니까?",
            InterviewIntent.AskVisitPurpose =>
                $"{to}에는 {who}이/가 있었습니다. 만나러 간 이유가 있었습니까?",

            // ── 2단계 · 목격 5단계 ──
            // 인물 → 행동 → 관찰 범위 → 의심 근거. '봤다' 와 '만졌다' 와 '조작했다' 는
            // 전부 다른 주장이므로 한 질문에 뭉치지 않는다(기획안 §3-⑥).
            InterviewIntent.AskSeenPersonAction =>
                $"{who}은/는 그때 무엇을 하고 있었습니까?",
            InterviewIntent.AskSeenScope =>
                $"{who}이/가 설비를 조작하는 장면까지 보셨습니까?",
            InterviewIntent.AskWhySuspicious =>
                $"그런데 왜 그 행동이 수상하다고 생각했습니까?",

            // ── 2단계 · 설비 사고 ──
            InterviewIntent.AskEquipmentFault =>
                $"사고 전에 {here} 설비에 이상이 있었습니까?",
            InterviewIntent.AskInspectionWork =>
                $"{at}어떤 점검이나 수리 작업을 진행했습니까?",
            InterviewIntent.AskEquipmentTouch =>
                $"설비에 직접 손을 대거나 설정을 변경했습니까?",
            InterviewIntent.AskWhoReported =>
                $"고장이 발생했을 때 누구에게 먼저 알렸습니까?",
            InterviewIntent.AskTouchDetail =>
                $"어느 부분을 만졌습니까? 설정값을 변경했습니까?",
            InterviewIntent.AskRepairConfirm =>
                $"수리한 뒤 정상 작동을 확인했습니까?",

            // ── 2단계 · 정전 ──
            InterviewIntent.AskBlackoutExperience =>
                $"{at}정전이 발생했을 때 어떤 상황이었습니까?",
            InterviewIntent.AskBlackoutSigns =>
                $"정전 직전에 평소와 다른 소리나 징후는 없었습니까?",
            InterviewIntent.AskAfterBlackoutMet =>
                $"정전 직후에 다른 직원을 만난 적 있습니까?",
            InterviewIntent.AskHeardFromWhom =>
                $"그 이야기는 누구에게 들었습니까?",

            // ── 2단계 · 기절 / 구조 ──
            InterviewIntent.AskLastMemory =>
                $"쓰러지기 직전에 마지막으로 기억나는 것은 무엇입니까?",
            InterviewIntent.AskWokeWhere =>
                $"정신을 차렸을 때 어디에 있었습니까?",
            InterviewIntent.AskFoundWhere =>
                $"{who}을/를 어디에서 발견했습니까?",
            InterviewIntent.AskFoundCondition =>
                $"발견했을 때 어떤 상태였습니까?",
            InterviewIntent.AskRescueAction =>
                $"어떤 조치를 취했습니까?",

            // ── 2단계 · 이상 개체 ──
            InterviewIntent.AskAnomalySeenHow =>
                $"그것을 직접 보았습니까?",
            InterviewIntent.AskAnomalyDirection =>
                $"그것이 사라지거나 이동하는 방향을 보았습니까?",

            // ── 통화 기록 ──
            InterviewIntent.AskCallReason => q.CallKind switch
            {
                CallRecordKind.Missed => $"{at}전화를 걸었다가 왜 끊었습니까? 무슨 일이었습니까?",
                CallRecordKind.ManagerCalled => $"{at}통화했을 때 무엇을 하고 있었습니까?",
                _ => q.CallEvent switch
                {
                    DialogueRepository.EventIdleVisit =>
                        $"{at}{InterviewEvidenceBoard.RoomName(q.CallRoomId)}에 가고 싶다고 전화한 이유는 무엇입니까?",
                    DialogueRepository.EventIdleWorry =>
                        $"{at}{InterviewEvidenceBoard.RoomName(q.CallRoomId)}에서 무슨 소리를 들었습니까?",
                    _ => $"{at}전화로 알린 내용을 다시 설명해 주십시오.",
                },
            },
            InterviewIntent.AskCallAfter =>
                "통화를 끝낸 뒤에는 무엇을 했습니까?",
            InterviewIntent.ConfrontNeglect =>
                $"오늘 {q.RepeatIndex}번이나 다른 작업실에 놀러 가고 싶다고 전화했습니다. 근무 태만 아닙니까?",

            // ── 이상 개체 조우 ──
            InterviewIntent.AskGhostWellbeing =>
                $"{at}{here}에서 그것을 마주쳤을 때 괜찮았습니까? 지금 상태는 어떻습니까?",
            InterviewIntent.AskGhostAppearance =>
                "그것은 어떻게 생겼습니까? 본 대로 말해 주십시오.",
            InterviewIntent.AskGhostWhatHappened =>
                $"{at}그것이 나타났을 때 무엇을 했습니까?",
            InterviewIntent.AskGhostOthers =>
                $"그때 함께 있던 {who} 직원은 어떻게 했습니까?",

            // ── 알리바이 · 따지기 ──
            InterviewIntent.AskAlibiProof =>
                $"{at}당신이 있던 곳을 누가 증명해 줄 수 있습니까?",
            InterviewIntent.AskProveInnocence =>
                string.IsNullOrEmpty(here)
                    ? "이 사고와 무관하다는 근거를 대십시오."
                    : $"{here} 사고와 무관하다는 근거를 대십시오.",
            InterviewIntent.AskSuspectOpinion =>
                "누가 그랬다고 생각합니까? 짚이는 사람이 있습니까?",
            InterviewIntent.PressPresence =>
                $"기록상 {at}당신은 {here}에 있었습니다. 설명하십시오.",

            _ => "",
        };
    }
}
