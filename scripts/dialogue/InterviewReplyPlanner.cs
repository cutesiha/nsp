using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 증거 기반 질문 하나 → 「무엇을 말할지」(ReplyFrame) → 문장.
//
// 결정 계층은 새로 만들지 않는다. 방해자 전략과 알리바이는 기존
// DialogueResponsePlanner / DialogueClaimState 가 그대로 정하고, 여기서는 그 결정을
// 읽어 프레임을 채운다. 바뀐 것은 표현 계층뿐이다.
//
// 질문이 들고 온 (시각 · 작업실 · 사건) 값만 쓴다. 다른 사건의 값을 끌어오지 않는다.
public static class InterviewReplyPlanner
{
    // 같은 질문을 다시 받으면 '같은 주장'을 되풀이한다. 문장은 다시 뽑히므로 표현만 달라진다.
    private static readonly Dictionary<string, ReplyFrame> _memo = new();

    public static void Reset() => _memo.Clear();

    public static string Answer(InterviewQuestion q)
    {
        if (q == null || string.IsNullOrEmpty(q.TargetEmployeeId)) return "…";

        string memoKey = q.TargetEmployeeId + "|" + q.Key;
        if (_memo.TryGetValue(memoKey, out var known)) return InterviewReplyComposer.Compose(known);

        var frame = Build(q);
        _memo[memoKey] = frame;
        return InterviewReplyComposer.Compose(frame);
    }

    // 검증용 — 문장이 아니라 "무엇을 말하기로 했는가"를 그대로 본다.
    // 시나리오 테스트가 표현이 아니라 판단을 검사할 수 있게 열어 둔다.
    public static ReplyFrame FrameFor(InterviewQuestion q)
    {
        if (q == null) return null;
        string memoKey = q.TargetEmployeeId + "|" + q.Key;
        if (_memo.TryGetValue(memoKey, out var known)) return known;
        var frame = Build(q);
        _memo[memoKey] = frame;
        return frame;
    }

    // 플레이어가 자료 두 장으로 들이민 모순에 대한 대응.
    public static string ConfrontAnswer(string employeeId, EvidenceContradiction.Result result) =>
        ConfrontAnswer(employeeId, result, out _);

    // 자료 두 장을 함께 들이밀었을 때의 답. variant 는 화면 연출(긴장 발광 · 휴게실 표식)이 쓴다.
    //
    // 규칙이 성립하지 않아도 답은 한다 — 거절이 아니라 중립으로 받는다(Confront.neutral).
    // 성립한 경우, 결백한 직원은 기록을 받아들이고 진술을 정정하며(새 알리바이를 만들지 않는다),
    // 결번자는 이미 정해 둔 전략대로 흐리거나 불리한 기록이 쌓였으면 정면 부정한다.
    public static string ConfrontAnswer(string employeeId, EvidenceContradiction.Result result, out string variant)
    {
        variant = "neutral";
        if (string.IsNullOrEmpty(employeeId) || result == null) return "…";

        int day = DialogueContextBuilder.Day();
        var ctx = Anchor(employeeId, result.AnchorTime, result.Later?.IncidentKey ?? "");
        var plan = DialogueResponsePlanner.Plan(ctx);
        var claim = DialogueClaimState.Get(employeeId, day, ctx.ClaimKey);

        if (result.Kind != ConfrontKind.None)
        {
            variant = "honest";
            if (ctx.IsSaboteur && !claim.ClaimTruthful)
                variant = ctx.EvidenceAgainstCount >= 2 || plan.Deception == DeceptionMode.Deny ? "deny" : "evasive";

            // 행동 추궁 — 이미 "설비 근처에 가지 않았다"고 못 박아 둔 사건이라면 물러설 수 없다.
            // 여기서 흐리면 방금 한 주장을 스스로 접는 셈이고, 알리바이 일관성이 깨진다.
            // 결번자는 위치에 대해서는 거짓말하지 않으므로(제자리 범행) ClaimTruthful 이 참일 때가
            // 많다 — 그 경우 위 조건만으로는 영영 honest 가 나온다. 이 한 줄이 §3-2 의 요점이다.
            if (result.Kind == ConfrontKind.Behavior && ctx.IsSaboteur && claim.DeniesEquipmentContact)
                variant = "deny";
        }

        var frame = new ReplyFrame { EmployeeId = employeeId, Topic = ReplyTopic.Confront, Variant = variant };
        frame.Set("room", RoomName(result.RoomA)).Set("time", DialogueClock.Spoken(result.AnchorTime));
        return InterviewReplyComposer.Compose(frame);
    }

    // --- 프레임 조립 -----------------------------------------------------

    private static ReplyFrame Build(InterviewQuestion q)
    {
        string id = q.TargetEmployeeId;
        int day = DialogueContextBuilder.Day();
        float t = q.HasAnchorTime ? q.AnchorTime : (GameState.Instance?.DayTimeSeconds ?? 0f);

        var ctx = Anchor(id, q.HasAnchorTime ? q.AnchorTime : -1f, q.IncidentKey);
        var plan = DialogueResponsePlanner.Plan(ctx);
        var claim = DialogueClaimState.Get(id, day, ctx.ClaimKey);
        // 거짓 알리바이를 대고 있으면 실제 동선을 꺼낼 수 없다 — 꺼내는 순간 자백이 된다.
        bool truthful = !ctx.IsSaboteur || claim.ClaimTruthful;

        var f = new ReplyFrame { EmployeeId = id };
        // 근무 기억 — 이 답변 뒤에 무엇을 떠올릴지(주제), 어느 방 기준인지, 핵심이 이미 말한 것.
        var memTopic = RecallTopic.None;
        string memRoom = "";
        var covered = new List<MemoryKind>();
        f.Set("time", DialogueClock.Spoken(t));
        f.Set("from", RoomName(q.FromRoomId));
        f.Set("to", RoomName(q.ToRoomId));
        f.Set("room", RoomName(string.IsNullOrEmpty(q.SubjectRoomId) ? plan.RoomId : q.SubjectRoomId));
        f.Set("mood", q.MoodText);

        switch (q.Intent)
        {
            case InterviewIntent.AskMoveReason:
                f.Topic = ReplyTopic.MoveReason;
                FillMoveReason(f, q, id, day, truthful);
                memTopic = RecallTopic.Movement;
                memRoom = truthful ? q.ToRoomId : plan.RoomId;
                covered.Add(MemoryKind.Relocated);
                covered.Add(MemoryKind.Dispatched);
                if (f.Variant is "task" or "repair") covered.Add(MemoryKind.Worked);
                // "그 방으로 갔다"는 것을 스스로 인정한 진술이다.
                RecordClaim(id, ctx.ClaimKey, truthful ? q.ToRoomId : plan.RoomId, t);
                break;

            case InterviewIntent.AskPresenceReason:
                f.Topic = ReplyTopic.PresenceReason;
                FillPresenceReason(f, q, id, day, t, truthful);
                memTopic = RecallTopic.Presence;
                memRoom = truthful ? q.SubjectRoomId : plan.RoomId;
                if (f.Variant == "task") covered.Add(MemoryKind.Worked);
                RecordClaim(id, ctx.ClaimKey, truthful ? q.SubjectRoomId : plan.RoomId, t);
                break;

            case InterviewIntent.AskWhoWasPresent:
            {
                f.Topic = ReplyTopic.Companion;
                // 거짓 알리바이를 대는 중이면 '주장한 방'의 인원을 댄다 — 거짓말도 앞뒤가 맞아야 한다.
                string room = truthful ? AnchorRoom(q, ctx) : plan.RoomId;
                var others = DialogueContextBuilder.OccupantsAt(room, day, t, id);
                f.Variant = others.Count > 0 ? "with" : "alone";
                f.Set("who", others.Count > 0 ? Codename(others[0]) : "");
                f.Set("room", RoomName(room));
                // "그 시각 그 방에서 저 사람과 함께 있었다" — 상대의 위치까지 걸린 진술이다.
                if (others.Count > 0) RecordSighting(id, others[0], room, t);
                memTopic = RecallTopic.Presence;
                memRoom = room;
                covered.Add(MemoryKind.Companion);
                // 이 시간대의 다른 답에는 "누구랑 있었다"를 다시 덧붙이지 않는다 — 이미 물었고, 이미 답했다.
                if (q.HasAnchorTime) ShiftMemory.MarkCompanionAsked(id, day, q.AnchorTime);
                break;
            }

            case InterviewIntent.AskNextLocation:
            {
                f.Topic = ReplyTopic.NextLocation;
                if (!truthful) { f.Variant = "evasive"; break; }
                string next = DialogueContextBuilder.RoomAfter(id, day, t);
                f.Variant = string.IsNullOrEmpty(next) ? "stayed" : "moved";
                f.Set("next", RoomName(next));
                f.Set("room", RoomName(AnchorRoom(q, ctx)));
                break;
            }

            case InterviewIntent.AskActionAtDestination:
            {
                f.Topic = ReplyTopic.ActionThere;
                string room = truthful ? AnchorRoom(q, ctx) : plan.RoomId;
                var (kind, task) = WorkAt(id, day, room, t);
                f.Variant = !truthful && kind == "none" ? "evasive"
                    : kind == "repair" ? "repair"
                    : kind == "task" ? "task" : "check";
                f.Set("task", task);
                f.Set("room", RoomName(room));
                memTopic = RecallTopic.Presence;
                memRoom = room;
                covered.Add(MemoryKind.Worked);
                break;
            }

            case InterviewIntent.AskIncidentKnown:
            {
                f.Topic = ReplyTopic.IncidentKnown;
                var level = ctx.SubjectKnowledge;
                // 방해자가 다른 방에 있었다고 주장 중이면, 그 방에서 알 수 있는 만큼만 안다.
                if (ctx.IsSaboteur && !truthful)
                    level = KnowledgeFromRoom(plan.RoomId, q.SubjectRoomId);
                f.Variant = level == KnowledgeLevel.Direct ? "direct"
                    : level == KnowledgeLevel.Indirect ? "indirect" : "none";
                f.Set("room", RoomName(q.SubjectRoomId));
                memTopic = RecallTopic.Anomaly;
                memRoom = truthful ? ctx.RoomAtSubject : plan.RoomId;
                break;
            }

            case InterviewIntent.AskWhereAtIncident:
                f.Topic = ReplyTopic.WhereAtIncident;
                f.Variant = "any";
                f.Set("room", RoomName(plan.RoomId));
                memTopic = RecallTopic.Location;
                memRoom = plan.RoomId;
                // 이 답변은 그대로 '이 직원의 진술' 자료가 된다.
                RecordClaim(id, ctx.ClaimKey, plan.RoomId, t);
                break;

            case InterviewIntent.AskBeforeIncident:
            {
                f.Topic = ReplyTopic.BeforeIncident;
                string room = plan.RoomId;
                var (kind, task) = WorkAt(id, day, room, t);
                string prev = truthful ? DialogueContextBuilder.RoomBefore(id, day, t) : "";
                if (kind is "repair" or "task") { f.Variant = "task"; f.Set("task", task); }
                else if (!string.IsNullOrEmpty(prev) && prev != room) { f.Variant = "moved"; f.Set("prev", RoomName(prev)); }
                else f.Variant = "plain";
                f.Set("room", RoomName(room));
                memTopic = RecallTopic.Presence;
                memRoom = room;
                if (f.Variant == "task") covered.Add(MemoryKind.Worked);
                if (f.Variant == "moved") covered.Add(MemoryKind.Relocated);
                break;
            }

            case InterviewIntent.AskWhoSeenNear:
            {
                f.Topic = ReplyTopic.WhoSeenNear;
                var others = DialogueContextBuilder.OccupantsAt(plan.RoomId, day, t, id);
                f.Variant = others.Count > 0 ? "someone" : "none";
                f.Set("who", others.Count > 0 ? Codename(others[0]) : "");
                if (others.Count > 0) RecordSighting(id, others[0], plan.RoomId, t);
                break;
            }

            case InterviewIntent.AskRouteAround:
            {
                f.Topic = ReplyTopic.RouteAround;
                if (!truthful) { f.Variant = "evasive"; break; }
                string prev = DialogueContextBuilder.RoomBefore(id, day, t);
                string next = DialogueContextBuilder.RoomAfter(id, day, t);
                f.Variant = string.IsNullOrEmpty(prev) || string.IsNullOrEmpty(next) ? "short" : "full";
                f.Set("prev", RoomName(prev));
                f.Set("next", RoomName(next));
                f.Set("room", RoomName(AnchorRoom(q, ctx)));
                break;
            }

            case InterviewIntent.AskConfirmTestimony:
            {
                f.Topic = ReplyTopic.ConfirmTestimony;
                // 증언이 말하는 방과 이 직원이 내세우는 위치가 같으면 인정, 다르면 부정.
                bool matches = !string.IsNullOrEmpty(q.SubjectRoomId) && plan.RoomId == q.SubjectRoomId;
                f.Variant = matches ? "admit" : "deny";
                f.Set("room", RoomName(matches ? q.SubjectRoomId : plan.RoomId));
                RecordClaim(id, ctx.ClaimKey, plan.RoomId, t);
                memTopic = RecallTopic.Location;
                memRoom = plan.RoomId;
                break;
            }

            case InterviewIntent.AskRestate:
                f.Topic = ReplyTopic.Restate;
                f.Variant = "same";
                f.Set("room", RoomName(plan.RoomId));
                // 같은 주장을 되풀이하는 것이므로 진술 자료도 그대로 유지된다.
                RecordClaim(id, ctx.ClaimKey, plan.RoomId, t);
                memTopic = RecallTopic.Location;
                memRoom = plan.RoomId;
                break;

            case InterviewIntent.AskMoodReason:
            {
                f.Topic = ReplyTopic.MoodReason;
                // 사고를 이유로 댈 수 있는 것은 불안·예민 계열 기분일 때뿐이다.
                // "여유로움"이라고 적어 놓고 "사고가 신경 쓰여서요"라고 하면 앞뒤가 안 맞는다.
                var known = MoodTones.CanBlameIncident(q.MoodText)
                    ? DialogueContextBuilder.MostRecentKnownIncident(id, day)
                    : null;
                f.Variant = known != null ? "incident"
                    : MoodTones.Of(q.MoodText) == MoodTone.Calm ? "calm" : "plain";
                f.Set("room", RoomName(known?.RoomId ?? ""));
                break;
            }

            case InterviewIntent.AskMoodBefore:
            {
                f.Topic = ReplyTopic.MoodBefore;
                // 근무 중에 기분이 바뀌었다고 말하려면, 그럴 만한 일을 겪었고
                // 지금 적어 낸 기분도 그 방향이어야 한다.
                bool changedByShift = MoodTones.CanBlameIncident(q.MoodText)
                    && DialogueContextBuilder.MostRecentKnownIncident(id, day) != null;
                f.Variant = changedByShift ? "no" : "yes";
                break;
            }

            case InterviewIntent.AskMoodRelated:
            {
                f.Topic = ReplyTopic.MoodRelated;
                string suspect = truthful ? ctx.KnownSuspiciousActorId : "";
                // 여기서도 기분의 방향을 먼저 본다 — 편안하다고 적은 사람이
                // "그 사고 때문이에요" 라고 답하지 않게.
                var known = MoodTones.CanBlameIncident(q.MoodText)
                    ? DialogueContextBuilder.MostRecentKnownIncident(id, day)
                    : null;
                if (!string.IsNullOrEmpty(suspect) && MoodTones.CanBlameIncident(q.MoodText))
                { f.Variant = "person"; f.Set("who", Codename(suspect)); }
                else if (known != null) { f.Variant = "incident"; f.Set("room", RoomName(known.RoomId)); }
                else f.Variant = "none";
                break;
            }

            case InterviewIntent.FollowExactTime:
                f.Topic = ReplyTopic.ExactTime;
                // 시각을 흐리는 것은 회피 전략의 일부다. 결백한 직원은 그냥 말한다.
                f.Variant = !truthful || plan.Deception == DeceptionMode.Vague ? "vague" : "exact";
                break;

            default:
                f.Topic = ReplyTopic.Unknown;
                f.Variant = "any";
                break;
        }

        if (memTopic != RecallTopic.None)
        {
            var req = new RecallRequest
            {
                EmployeeId = id, Day = day, Topic = memTopic,
                AnchorTime = q.HasAnchorTime ? q.AnchorTime : -1f,
                AnchorRoom = memRoom ?? "",
                IsSaboteur = ctx.IsSaboteur,
                Lying = ctx.IsSaboteur && !truthful,
                SubjectIncidentKey = q.IncidentKey ?? "",
            };
            foreach (var k in covered) req.Covered.Add(k);
            // 꼬리질문 "같이 있던 사람"을 이 시간대에 이미 물었으면 동료 이야기는 덧붙이지 않는다.
            if (q.HasAnchorTime && ShiftMemory.CompanionAsked(id, day, q.AnchorTime))
                req.Covered.Add(MemoryKind.Companion);
            var mem = ShiftMemory.Recall(req);
            f.Addenda.AddRange(mem.Addenda);
        }

        // 결번자의 "설비 쪽엔 손도 안 댔다" — 자기 위치 · 그 방에 있던 이유 · 거기서 한 일을
        // 답할 때만 붙는다(KoreanDialogueComposer 와 같은 규칙). 결백한 직원은 오지 않는다.
        KoreanDialogueComposer.ApplyEquipmentDenial(f, ctx,
            f.Topic is ReplyTopic.PresenceReason or ReplyTopic.ActionThere);
        return f;
    }

    // --- 사실 조회 -------------------------------------------------------

    // 질문이 가리키는 "이 직원이 있던" 작업실. 없으면 그 시각의 실제 위치.
    //
    // 사고 자료의 SubjectRoomId 는 "사고가 난 방"이지 이 직원이 있던 방이 아니다.
    // 예전에는 그 값을 그대로 써서, 경비실에 있던 직원이 "그때 같이 있던 사람"을 물으면
    // 사고 난 저장고의 인원을 대는(그리고 그게 증언 자료로 남는) 사고가 났다.
    private static string AnchorRoom(InterviewQuestion q, DialogueContext ctx)
    {
        if (!string.IsNullOrEmpty(q.ToRoomId)) return q.ToRoomId;
        bool incidentRoom = !string.IsNullOrEmpty(q.IncidentKey);
        if (!incidentRoom && !string.IsNullOrEmpty(q.SubjectRoomId)) return q.SubjectRoomId;
        return string.IsNullOrEmpty(ctx.RoomAtSubject) ? ctx.AssignedRoomId : ctx.RoomAtSubject;
    }

    // 이동 이유는 실제 로그에서만 찾는다. 찾지 못하면 지어내지 않는다.
    private static void FillMoveReason(ReplyFrame f, InterviewQuestion q, string id, int day, bool truthful)
    {
        if (q.PlayerOrderedMove) { f.Variant = "ordered"; return; }
        // 관리자가 전화로 "확인하러 가라"고 해서 옮긴 이동 — 통화 기록에서만 찾는다.
        float window = ShiftMemory.RecallWindowMinutes * DialogueClock.SecondsPerMinute;
        if (CallMemoryLog.For(id, day).Any(r => r.Kind == CallRecordKind.OrderedGo && r.RoomId == q.ToRoomId
                                                && r.Time <= q.AnchorTime + 1f && r.Time >= q.AnchorTime - window))
        { f.Variant = "dispatched"; return; }

        var (kind, task) = WorkAt(id, day, q.ToRoomId, q.AnchorTime);
        if (kind == "repair") { f.Variant = "repair"; return; }
        if (kind == "task") { f.Variant = "task"; f.Set("task", task); return; }

        // 업무 기록이 없는 이동. 결백한 직원에게는 그냥 별일 아닌 이동이고,
        // 방해자에게는 설명할 수 없는 이동이다 — 여기서 갈린다.
        f.Variant = truthful ? "plain" : "evasive";
    }

    private static void FillPresenceReason(ReplyFrame f, InterviewQuestion q, string id, int day,
        float t, bool truthful)
    {
        var (kind, task) = WorkAt(id, day, q.SubjectRoomId, t);
        if (kind != "none") { f.Variant = "task"; f.Set("task", task); return; }

        string assigned = FacilitySimulation.Instance?.GetEmployeeState(id)?.AssignedRoomId ?? "";
        if (!string.IsNullOrEmpty(assigned) && assigned == q.SubjectRoomId) { f.Variant = "assigned"; return; }
        f.Variant = truthful ? "check" : "evasive";
    }

    // 그 시각 그 방에서 이 직원이 실제로 하고 있던 업무. ("repair" / "task" / "none")
    private static (string Kind, string Task) WorkAt(string employeeId, int day, string roomId, float time)
    {
        if (string.IsNullOrEmpty(roomId)) return ("none", "");
        var log = EventLog.Instance;
        if (log == null) return ("none", "");

        // 그 시각 전후로 그 방에서 시작한 이 직원의 업무 한 건.
        float window = EvidenceContradiction.WindowMinutes * DialogueClock.SecondsPerMinute * 2f;
        var start = log.GetAllEntries()
            .Where(e => e.Day == day && e.EventType == LogEventType.TaskStart
                        && e.ActorEmployeeId == employeeId && e.RoomId == roomId
                        && e.GameTimeSeconds >= time - window && e.GameTimeSeconds <= time + window)
            .OrderBy(e => Mathf.Abs(e.GameTimeSeconds - time))
            .FirstOrDefault();
        if (start == null) return ("none", "");

        // 수리 업무는 원문에 🔧 표식이 붙는다(FacilityLogFormatter 와 같은 규약).
        if ((start.Description ?? "").StartsWith("🔧")) return ("repair", "수리");

        // 업무 이름을 못 집어내면 "무슨 업무였는지는 말하지 않는다" 로 떨어진다.
        // 빈 이름을 그대로 넘기면 {task} 자리가 비어 문장 후보가 통째로 사라진다.
        string name = ShiftMemory.TaskNameFrom(start.Description);
        return string.IsNullOrEmpty(name) ? ("check", "") : ("task", name);
    }

    // 주장한 방에서 그 사건을 어디까지 알 수 있는가(거짓 알리바이의 일관성).
    private static KnowledgeLevel KnowledgeFromRoom(string room, string incidentRoom)
    {
        if (string.IsNullOrEmpty(room) || string.IsNullOrEmpty(incidentRoom)) return KnowledgeLevel.None;
        if (room == incidentRoom) return KnowledgeLevel.Direct;
        return DialogueContextBuilder.IsAdjacent(room, incidentRoom)
            ? KnowledgeLevel.Indirect : KnowledgeLevel.None;
    }

    // --- 공통 -----------------------------------------------------------

    // 질문이 가리키는 그 순간으로 컨텍스트를 고정한다. 사건이 없으면 시각 자체가 키가 된다.
    private static DialogueContext Anchor(string employeeId, float anchorTime, string incidentKey)
    {
        int day = DialogueContextBuilder.Day();
        var subject = DialogueContextBuilder.FindByKey(day, incidentKey);
        string claimKey = !string.IsNullOrEmpty(incidentKey) ? incidentKey
            : anchorTime >= 0f ? $"t:{anchorTime:0.0}" : "no_incident";

        var ctx = DialogueContextBuilder.BuildAt(employeeId, DialogueConversationKind.Interview,
            DialogueQuestions.Where, subject, anchorTime, claimKey);
        ctx.TargetEmployeeId = LocalDialogueGenerator.OpinionTargetId(employeeId);
        return ctx;
    }

    // 직원이 관리자에게 말한 위치는 그대로 플레이어의 자료가 된다.
    //
    // 모든 대사를 자료로 만들지는 않는다(기분·소감·되묻기는 남기지 않는다).
    // 여기 들어오는 것은 "언제 · 어디" 가 붙은 주장뿐이고, 그래야 나중에 로그·CCTV 와
    // 맞대어 볼 수 있다.
    private static void RecordClaim(string employeeId, string claimKey, string roomId, float time)
    {
        if (string.IsNullOrEmpty(roomId) || time < 0f) return;
        PlayerKnownEvidence.RecordLocationStatement(employeeId, claimKey, roomId, true, time);
    }

    // "그 사람을 거기서 봤다" 는 말도 자료가 된다 — 다른 직원을 심문할 때 그대로 쓴다.
    private static void RecordSighting(string speakerId, string subjectId, string roomId, float time)
    {
        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(roomId)) return;
        PlayerKnownEvidence.RecordSighting(speakerId, subjectId, roomId, time);
    }

    private static string RoomName(string roomId) => InterviewEvidenceBoard.RoomName(roomId);
    private static string Codename(string employeeId) => InterviewEvidenceBoard.Codename(employeeId);
}
