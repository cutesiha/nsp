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
    public static string ConfrontAnswer(string employeeId, EvidenceContradiction.Result result)
    {
        if (string.IsNullOrEmpty(employeeId) || result == null || !result.IsContradiction) return "…";

        int day = DialogueContextBuilder.Day();
        var ctx = Anchor(employeeId, result.AnchorTime, result.Later?.IncidentKey ?? "");
        var plan = DialogueResponsePlanner.Plan(ctx);
        var claim = DialogueClaimState.Get(employeeId, day, ctx.ClaimKey);

        // 결백한 직원은 기록을 받아들이고 진술을 정정한다 — 새 알리바이를 만들지 않는다.
        // 방해자는 이미 정해 둔 전략대로 흐리거나, 불리한 기록이 쌓였으면 정면 부정한다.
        string variant = "honest";
        if (ctx.IsSaboteur && !claim.ClaimTruthful)
            variant = ctx.EvidenceAgainstCount >= 2 || plan.Deception == DeceptionMode.Deny ? "deny" : "evasive";

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
                break;

            case InterviewIntent.AskPresenceReason:
                f.Topic = ReplyTopic.PresenceReason;
                FillPresenceReason(f, q, id, day, t, truthful);
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
                break;
            }

            case InterviewIntent.AskWhereAtIncident:
                f.Topic = ReplyTopic.WhereAtIncident;
                f.Variant = "any";
                f.Set("room", RoomName(plan.RoomId));
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
                break;
            }

            case InterviewIntent.AskWhoSeenNear:
            {
                f.Topic = ReplyTopic.WhoSeenNear;
                var others = DialogueContextBuilder.OccupantsAt(plan.RoomId, day, t, id);
                f.Variant = others.Count > 0 ? "someone" : "none";
                f.Set("who", others.Count > 0 ? Codename(others[0]) : "");
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
                break;
            }

            case InterviewIntent.AskRestate:
                f.Topic = ReplyTopic.Restate;
                f.Variant = "same";
                f.Set("room", RoomName(plan.RoomId));
                break;

            case InterviewIntent.AskMoodReason:
            {
                f.Topic = ReplyTopic.MoodReason;
                var known = DialogueContextBuilder.MostRecentKnownIncident(id, day);
                f.Variant = known != null ? "incident" : "plain";
                f.Set("room", RoomName(known?.RoomId ?? ""));
                break;
            }

            case InterviewIntent.AskMoodBefore:
            {
                f.Topic = ReplyTopic.MoodBefore;
                // 오늘 아는 사건이 있으면 "근무 중에 그렇게 됐다" 쪽이 사실에 맞다.
                f.Variant = DialogueContextBuilder.MostRecentKnownIncident(id, day) != null ? "no" : "yes";
                break;
            }

            case InterviewIntent.AskMoodRelated:
            {
                f.Topic = ReplyTopic.MoodRelated;
                string suspect = truthful ? ctx.KnownSuspiciousActorId : "";
                var known = DialogueContextBuilder.MostRecentKnownIncident(id, day);
                if (!string.IsNullOrEmpty(suspect)) { f.Variant = "person"; f.Set("who", Codename(suspect)); }
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
        return f;
    }

    // --- 사실 조회 -------------------------------------------------------

    // 질문이 가리키는 작업실. 없으면 그 시각의 실제(또는 주장된) 위치.
    private static string AnchorRoom(InterviewQuestion q, DialogueContext ctx)
    {
        if (!string.IsNullOrEmpty(q.ToRoomId)) return q.ToRoomId;
        if (!string.IsNullOrEmpty(q.SubjectRoomId)) return q.SubjectRoomId;
        return string.IsNullOrEmpty(ctx.RoomAtSubject) ? ctx.AssignedRoomId : ctx.RoomAtSubject;
    }

    // 이동 이유는 실제 로그에서만 찾는다. 찾지 못하면 지어내지 않는다.
    private static void FillMoveReason(ReplyFrame f, InterviewQuestion q, string id, int day, bool truthful)
    {
        if (q.PlayerOrderedMove) { f.Variant = "ordered"; return; }

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
        string name = TaskNameFrom(start.Description);
        return string.IsNullOrEmpty(name) ? ("check", "") : ("task", name);
    }

    // 로그 원문에서 업무 이름을 집어낸다.
    // 문장 형식("🔧 까마귀 환기실 도착 / 환기구 청소 시작")에 기대지 않고, 먼저 실제
    // TaskDef 이름과 대조한다 — 로그 문구가 바뀌어도 대사가 깨지지 않게.
    private static string TaskNameFrom(string description)
    {
        string d = (description ?? "").Replace("⚙", "").Replace("🔧", "").Trim();
        if (d.Length == 0) return "";

        var sim = FacilitySimulation.Instance;
        if (sim != null)
            foreach (var def in sim.GetTaskDefs())
                if (!string.IsNullOrEmpty(def?.DisplayName) && d.Contains(def.DisplayName))
                    return def.DisplayName;

        int slash = d.LastIndexOf(" / ", System.StringComparison.Ordinal);
        if (slash < 0) return "";
        d = d[(slash + 3)..].Trim();
        foreach (string tail in new[] { " 시작", " 수행", " 진행" })
            if (d.EndsWith(tail)) d = d[..^tail.Length].Trim();
        return d;
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
    private static void RecordClaim(string employeeId, string claimKey, string roomId, float time)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        PlayerKnownEvidence.RecordLocationStatement(employeeId, claimKey, roomId, true, time);
    }

    private static string RoomName(string roomId) => InterviewEvidenceBoard.RoomName(roomId);
    private static string Codename(string employeeId) => InterviewEvidenceBoard.Codename(employeeId);
}
