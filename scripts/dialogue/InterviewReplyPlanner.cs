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
    // 결번 개체는 이미 정해 둔 전략대로 흐리거나 불리한 기록이 쌓였으면 정면 부정한다.
    public static string ConfrontAnswer(string employeeId, EvidenceContradiction.Result result, out string variant)
    {
        variant = "neutral";
        if (string.IsNullOrEmpty(employeeId) || result == null) return "…";

        // 추궁은 **그 자료들이 가리키는 날**의 일로 계산한다. 오늘 추궁한다고 해서
        // 오늘 동선으로 답하면, 어제 일을 묻는데 오늘 있던 방이 나온다.
        int day = result.SubjectDay > 0 ? result.SubjectDay : DialogueContextBuilder.Day();
        var ctx = Anchor(employeeId, result.AnchorTime, result.Later?.IncidentKey ?? "", day);
        var plan = DialogueResponsePlanner.Plan(ctx);
        var claim = DialogueClaimState.Get(employeeId, day, ctx.ClaimKey);

        if (result.Kind != ConfrontKind.None)
        {
            variant = "honest";
            if (ctx.IsSaboteur && !claim.ClaimTruthful)
                variant = ctx.EvidenceAgainstCount >= 2 || plan.Deception == DeceptionMode.Deny ? "deny" : "evasive";

            // 행동 추궁 — 이미 "설비 근처에 가지 않았다"고 못 박아 둔 사건이라면 물러설 수 없다.
            // 여기서 흐리면 방금 한 주장을 스스로 접는 셈이고, 알리바이 일관성이 깨진다.
            // 결번 개체는 위치에 대해서는 거짓말하지 않으므로(제자리 범행) ClaimTruthful 이 참일 때가
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
        // 이 질문이 가리키는 날. 오늘이 아닐 수 있다.
        int day = q.AnchorDay > 0 ? q.AnchorDay : DialogueContextBuilder.Day();
        float t = q.HasAnchorTime ? q.AnchorTime : (GameState.Instance?.DayTimeSeconds ?? 0f);

        var ctx = Anchor(id, q.HasAnchorTime ? q.AnchorTime : -1f, q.IncidentKey, day);
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
                RecordClaim(id, ctx.ClaimKey, truthful ? q.ToRoomId : plan.RoomId, t, day);
                break;

            case InterviewIntent.AskPresenceReason:
                f.Topic = ReplyTopic.PresenceReason;
                FillPresenceReason(f, q, id, day, t, truthful);
                memTopic = RecallTopic.Presence;
                memRoom = truthful ? q.SubjectRoomId : plan.RoomId;
                if (f.Variant == "task") covered.Add(MemoryKind.Worked);
                RecordClaim(id, ctx.ClaimKey, truthful ? q.SubjectRoomId : plan.RoomId, t, day);
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
                // 이름을 입에 올렸다 — 꼬리질문이 이 사람을 파고들 수 있다.
                if (others.Count > 0) { f.MentionedEmployeeId = others[0]; f.MentionedRoomId = room; }
                // "그 시각 그 방에서 저 사람과 함께 있었다" — 상대의 위치까지 걸린 진술이다.
                if (others.Count > 0) RecordSighting(id, others[0], room, t, day);
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
                // 그 뒤에 쓰러졌다면 제 발로 간 곳이 없다 — 들것에 실려 의무실로 갔다.
                // 그 길은 동선 로그에 남지 않으므로(본인의 이동이 아니다) 여기서 따로 말한다.
                if (FaintedAfter(id, day, t) || CarriedToMedical(id, day, next, t))
                {
                    f.Variant = "carried";
                    f.Set("next", RoomName(FacilitySimulation.MedicalRoomIdPublic));
                    break;
                }
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

            // ── 2단계 · 이동 ────────────────────────────────────────────

            case InterviewIntent.AskArrivalState:
            {
                f.Topic = ReplyTopic.ArrivalState;
                string room = q.ToRoomId;
                // 도착했을 때 그 방에 실제로 무슨 일이 있었는가 — 기록에서만 찾는다.
                var trouble = IncidentIn(room, day, q.AnchorTime);
                var (kind, task) = WorkAt(id, day, room, t);
                if (trouble != null)
                {
                    f.Variant = "incident";
                    f.Set("what", IncidentWord(trouble.EventType));
                }
                else if (kind != "none") { f.Variant = "task"; f.Set("task", task); }
                else f.Variant = "calm";
                f.Set("room", RoomName(room));
                memTopic = RecallTopic.Presence;
                memRoom = room;
                break;
            }

            case InterviewIntent.AskVisitPurpose:
            {
                f.Topic = ReplyTopic.VisitPurpose;
                string other = q.OtherEmployeeId;
                var (kind, task) = WorkAt(id, day, q.ToRoomId, t);
                // 그 방에서 실제로 한 업무가 있으면 그것이 이유다. 없으면 업무 외 방문이고,
                // 그걸 어떻게 말하는지는 성향이 가른다 — 없는 용건을 지어내지는 않는다.
                if (kind != "none") { f.Variant = "work"; f.Set("task", task); }
                else f.Variant = truthful ? "admit" : "evasive";
                f.Set("who", Codename(other));
                f.Set("room", RoomName(q.ToRoomId));
                f.MentionedEmployeeId = other;
                f.MentionedRoomId = q.ToRoomId;
                break;
            }

            // ── 2단계 · 목격 ────────────────────────────────────────────
            //
            // '봤다' / '만졌다' / '조작했다' 는 전부 다른 주장이다. 이 직원이 실제로
            // 어디까지 봤는지(SightingStatement.Detail)를 넘어서 말하지 않는다.

            case InterviewIntent.AskSeenPersonAction:
            {
                f.Topic = ReplyTopic.SeenPersonAction;
                string other = q.OtherEmployeeId;
                string detail = SightingDetail(id, other, day);
                string room = SightingRoom(id, other, day, q.SubjectRoomId);
                if (!string.IsNullOrEmpty(detail)) { f.Variant = "detail"; f.Set("what", detail); }
                else { f.Variant = "onlythere"; f.SaidDontKnow = true; }   // 거기 있던 것만 봤다
                f.Set("who", Codename(other));
                f.Set("room", RoomName(room));
                f.MentionedEmployeeId = other;
                f.MentionedRoomId = room;
                f.MentionedDetail = detail;
                break;
            }

            case InterviewIntent.AskSeenScope:
            {
                f.Topic = ReplyTopic.SeenScope;
                string other = q.OtherEmployeeId;
                string detail = SightingDetail(id, other, day);
                // 설비를 만지는 것까지 봤다고 기록된 경우에만 "봤다"고 답한다.
                bool sawEquipment = !string.IsNullOrEmpty(detail)
                                    && (detail.Contains("설비") || detail.Contains("패널") || detail.Contains("조작"));
                f.Variant = sawEquipment ? "saw" : "no";
                if (!sawEquipment) f.SaidDontKnow = true;
                f.Set("who", Codename(other));
                f.MentionedEmployeeId = other;
                break;
            }

            case InterviewIntent.AskWhySuspicious:
            {
                f.Topic = ReplyTopic.WhySuspicious;
                string other = q.OtherEmployeeId;
                string room = SightingRoom(id, other, day, q.SubjectRoomId);
                // 의심의 근거는 "그 직후 그 방에서 사고가 났다" 처럼 **실제 사건**이어야 한다.
                var after = IncidentIn(room, day, q.AnchorTime);
                if (after != null)
                {
                    f.Variant = "incident";
                    f.Set("what", IncidentWord(after.EventType));
                    f.Set("room", RoomName(room));
                }
                else
                {
                    // 근거가 없으면 수상하다고 단정하지 않는다 — 이게 §3-⑥ 고양이 사례다.
                    f.Variant = "notreally";
                    f.SaidDontKnow = true;
                }
                f.Set("who", Codename(other));
                f.MentionedEmployeeId = other;
                break;
            }

            // ── 2단계 · 설비 사고 / 방해공작 ────────────────────────────

            case InterviewIntent.AskEquipmentFault:
            {
                f.Topic = ReplyTopic.EquipmentFault;
                string room = q.SubjectRoomId;
                // 사고 전에 그 방에 이미 고장·수리가 있었는가 — 로그에서만 찾는다.
                bool knew = DialogueContextBuilder.KnowledgeOf(id, ctx.Subject?.Entry) != KnowledgeLevel.None;
                var (kind, _) = WorkAt(id, day, room, t);
                if (kind == "repair") f.Variant = "repairing";      // 고치던 중이었다
                else if (!knew) f.Variant = "unaware";              // 사고 자체를 몰랐다
                else f.Variant = "none";                            // 이상은 못 느꼈다
                f.Set("room", RoomName(room));
                memTopic = RecallTopic.Presence;
                memRoom = room;
                break;
            }

            case InterviewIntent.AskInspectionWork:
            {
                f.Topic = ReplyTopic.InspectionWork;
                var (kind, task) = WorkAt(id, day, q.SubjectRoomId, t);
                f.Variant = kind == "repair" ? "repair" : kind == "task" ? "task" : "none";
                f.Set("task", task);
                f.Set("room", RoomName(q.SubjectRoomId));
                covered.Add(MemoryKind.Worked);
                break;
            }

            // Q4 — 이 유형의 중심. 네 갈래(점검만 / 손대지 않았다 / 수리했다 / 기억 안 난다).
            //
            // 결번 개체의 전략은 **한 번 정해지면 바뀌지 않는다**(DialogueClaim.DeniesEquipmentContact).
            // 질문을 다시 눌렀다고 "사실은 만졌다" 로 흔들리면 추리가 성립하지 않는다.
            // 자백은 플레이어가 증거를 들이밀었을 때(ConfrontAnswer)만 나온다.
            case InterviewIntent.AskEquipmentTouch:
            {
                f.Topic = ReplyTopic.EquipmentTouch;
                var (kind, task) = WorkAt(id, day, q.SubjectRoomId, t);

                if (ctx.IsSaboteur && claim.DeniesEquipmentContact)
                {
                    // "확인만 했다" — 접촉 자체를 부인하지는 않되 조작은 부인한다.
                    // "설비 근처에는 가지 않았다" 를 자료로도 남긴다 — 나중에 동료의 목격
                    // 증언이나 CCTV 와 맞대어 행동 추궁이 성립하는 바로 그 진술이다.
                    f.Variant = "deny";
                    KoreanDialogueComposer.ApplyEquipmentDenial(f, ctx, true, addCaveat: false);
                }
                else if (kind == "repair") { f.Variant = "repair"; f.Set("task", task); }
                else if (kind == "task") { f.Variant = "inspect"; f.Set("task", task); }
                else if (DialogueContextBuilder.RoomAt(id, day, t) == q.SubjectRoomId)
                    f.Variant = "nearby";       // 거기 있었지만 설비는 건드리지 않았다
                else { f.Variant = "notthere"; f.SaidDontKnow = true; }

                f.Set("room", RoomName(q.SubjectRoomId));
                memTopic = RecallTopic.Presence;
                memRoom = q.SubjectRoomId;
                break;
            }

            case InterviewIntent.AskWhoReported:
            {
                f.Topic = ReplyTopic.WhoReported;
                // 실제로 관리자에게 알린 기록이 있는가. 없으면 알리지 않은 것이다.
                bool called = CallMemoryLog.For(id, day)
                    .Any(r => r.Kind == CallRecordKind.Reported
                              && Mathf.Abs(r.Time - t) <= ShiftMemory.RecallWindowMinutes * DialogueClock.SecondsPerMinute);
                var others = DialogueContextBuilder.OccupantsAt(q.SubjectRoomId, day, t, id);
                if (called) f.Variant = "manager";
                else if (others.Count > 0)
                {
                    f.Variant = "colleague";
                    f.Set("who", Codename(others[0]));
                    f.MentionedEmployeeId = others[0];
                    f.MentionedRoomId = q.SubjectRoomId;
                }
                else { f.Variant = "noone"; f.SaidDontKnow = true; }
                break;
            }

            case InterviewIntent.AskTouchDetail:
            {
                f.Topic = ReplyTopic.TouchDetail;
                var (kind, task) = WorkAt(id, day, q.SubjectRoomId, t);
                if (ctx.IsSaboteur && claim.DeniesEquipmentContact) f.Variant = "denysetting";
                else if (kind != "none") { f.Variant = "task"; f.Set("task", task); }
                else { f.Variant = "vague"; f.SaidDontKnow = true; }
                break;
            }

            case InterviewIntent.AskRepairConfirm:
            {
                f.Topic = ReplyTopic.RepairConfirm;
                // 수리 완료 기록이 실제로 남아 있는가.
                bool done = EventLog.Instance?.GetAllEntries().Any(e => e.Day == day
                    && e.ActorEmployeeId == id && e.RoomId == q.SubjectRoomId
                    && e.EventType == LogEventType.TaskComplete) ?? false;
                f.Variant = done ? "confirmed" : "notconfirmed";
                if (!done) f.SaidDontKnow = true;
                break;
            }

            // ── 2단계 · 정전 ────────────────────────────────────────────
            //
            // 직접 겪은 것 / 경보만 들은 것 / 나중에 들은 것 / 몰랐던 것을 가른다.
            // 이 구분은 이미 KnowledgeLevel 이 들고 있다 — 새로 짐작하지 않는다.

            case InterviewIntent.AskBlackoutExperience:
            {
                f.Topic = ReplyTopic.BlackoutExperience;
                var know = DialogueContextBuilder.KnowledgeOf(id, ctx.Subject?.Entry);
                f.Variant = know switch
                {
                    KnowledgeLevel.Direct => "sawit",     // 불이 꺼지는 걸 직접 봤다
                    KnowledgeLevel.Indirect => "alarm",   // 경보만 들었다
                    KnowledgeLevel.Later => "heard",      // 나중에 들었다
                    _ => "unaware",                       // 몰랐다
                };
                if (know is KnowledgeLevel.Later or KnowledgeLevel.None) f.SaidDontKnow = true;
                f.Set("room", RoomName(plan.RoomId));
                memTopic = RecallTopic.Location;
                memRoom = plan.RoomId;
                break;
            }

            case InterviewIntent.AskBlackoutSigns:
            {
                f.Topic = ReplyTopic.BlackoutSigns;
                // "직전에 이상한 소리를 들었다" 는 그 방에서 실제로 앞선 고장이 있었을 때만.
                var before = IncidentIn(plan.RoomId, day, t);
                var know = DialogueContextBuilder.KnowledgeOf(id, ctx.Subject?.Entry);
                if (before != null && know == KnowledgeLevel.Direct)
                {
                    f.Variant = "noise";
                    f.Set("room", RoomName(plan.RoomId));
                }
                else { f.Variant = "nothing"; f.SaidDontKnow = true; }
                break;
            }

            case InterviewIntent.AskAfterBlackoutMet:
            {
                f.Topic = ReplyTopic.AfterBlackoutMet;
                // 정전 직후 그 방에 실제로 같이 있던 사람만.
                var others = DialogueContextBuilder.OccupantsAt(plan.RoomId, day, t, id);
                if (others.Count > 0)
                {
                    f.Variant = "met";
                    f.Set("who", Codename(others[0]));
                    RecordSighting(id, others[0], plan.RoomId, t, day);
                    f.MentionedEmployeeId = others[0];
                    f.MentionedRoomId = plan.RoomId;
                }
                else { f.Variant = "noone"; f.SaidDontKnow = true; }
                break;
            }

            case InterviewIntent.AskHeardFromWhom:
            {
                f.Topic = ReplyTopic.HeardFromWhom;
                var others = DialogueContextBuilder.OccupantsAt(plan.RoomId, day, t, id);
                if (others.Count > 0)
                {
                    f.Variant = "person";
                    f.Set("who", Codename(others[0]));
                    f.MentionedEmployeeId = others[0];
                }
                else { f.Variant = "vague"; f.SaidDontKnow = true; }
                break;
            }

            // ── 2단계 · 기절 / 구조 ─────────────────────────────────────

            case InterviewIntent.AskLastMemory:
            {
                f.Topic = ReplyTopic.LastMemory;
                // 쓰러지기 직전에 그 방에서 실제로 하던 일. 없으면 흐릿하다고 답한다 —
                // 스트레스가 높았다는 이유만으로 원인을 지어내지 않는다.
                var (kind, task) = WorkAt(id, day, q.SubjectRoomId, t);
                if (kind != "none") { f.Variant = "task"; f.Set("task", task); }
                else { f.Variant = "blank"; f.SaidDontKnow = true; }
                f.Set("room", RoomName(q.SubjectRoomId));
                break;
            }

            case InterviewIntent.AskWokeWhere:
            {
                f.Topic = ReplyTopic.WokeWhere;
                // 의무실로 옮겨졌는가 — 실제 이송 기록으로만 판단한다.
                bool moved = EventLog.Instance?.GetAllEntries().Any(e => e.Day == day
                    && (e.Description ?? "").Contains("이송")
                    && (e.Description ?? "").Contains(Codename(id))) ?? false;
                f.Variant = moved ? "medical" : "sameroom";
                f.Set("room", RoomName(moved ? FacilitySimulation.MedicalRoomIdPublic : q.SubjectRoomId));
                break;
            }

            case InterviewIntent.AskFoundWhere:
            {
                f.Topic = ReplyTopic.FoundWhere;
                f.Variant = "room";
                f.Set("who", Codename(q.OtherEmployeeId));
                f.Set("room", RoomName(q.SubjectRoomId));
                f.MentionedEmployeeId = q.OtherEmployeeId;
                f.MentionedRoomId = q.SubjectRoomId;
                break;
            }

            case InterviewIntent.AskFoundCondition:
            {
                f.Topic = ReplyTopic.FoundCondition;
                f.Variant = "unconscious";
                f.Set("who", Codename(q.OtherEmployeeId));
                // 그때 주변에 또 누가 있었는지는 **본 만큼만** 말한다.
                var others = DialogueContextBuilder.OccupantsAt(q.SubjectRoomId, day, t, id)
                    .Where(x => x != q.OtherEmployeeId).ToList();
                if (others.Count > 0)
                {
                    f.Variant = "withothers";
                    f.Set("other", Codename(others[0]));
                    f.MentionedEmployeeId = others[0];
                    f.MentionedRoomId = q.SubjectRoomId;
                }
                break;
            }

            case InterviewIntent.AskRescueAction:
            {
                f.Topic = ReplyTopic.RescueAction;
                f.Variant = "carried";
                f.Set("who", Codename(q.OtherEmployeeId));
                f.Set("room", RoomName(FacilitySimulation.MedicalRoomIdPublic));
                covered.Add(MemoryKind.Relocated);
                break;
            }

            // ── 2단계 · 이상 개체 ───────────────────────────────────────

            case InterviewIntent.AskAnomalySeenHow:
            {
                f.Topic = ReplyTopic.AnomalySeenHow;
                var know = DialogueContextBuilder.KnowledgeOf(id, ctx.Subject?.Entry);
                f.Variant = know switch
                {
                    KnowledgeLevel.Direct => "saw",
                    KnowledgeLevel.Indirect => "heard",   // 소리만 들었다
                    KnowledgeLevel.Later => "told",
                    _ => "nothing",
                };
                if (know != KnowledgeLevel.Direct) f.SaidDontKnow = true;
                break;
            }

            case InterviewIntent.AskAnomalyDirection:
            {
                f.Topic = ReplyTopic.AnomalyDirection;
                // 직접 본 사람만 방향을 말할 수 있다.
                bool direct = DialogueContextBuilder.KnowledgeOf(id, ctx.Subject?.Entry)
                              == KnowledgeLevel.Direct;
                f.Variant = direct ? "away" : "unknown";
                if (!direct) f.SaidDontKnow = true;
                f.Set("room", RoomName(q.SubjectRoomId));
                break;
            }

            case InterviewIntent.AskIncidentKnown:
            {
                f.Topic = ReplyTopic.IncidentKnown;
                var level = ctx.SubjectKnowledge;
                // 방해자가 다른 방에 있었다고 주장 중이면, 그 방에서 알 수 있는 만큼만 안다.
                if (ctx.IsSaboteur && !truthful)
                    level = KnowledgeFromRoom(plan.RoomId, q.SubjectRoomId);
                f.Variant = level switch
                {
                    KnowledgeLevel.Direct => "direct",
                    KnowledgeLevel.Indirect => "indirect",
                    // 그 순간에는 몰랐지만 나중에 그 방에 가 봤다 — "알고는 있다, 다만 그때는 딴 데 있었다".
                    KnowledgeLevel.Later => "later",
                    _ => "none",
                };
                f.Set("room", RoomName(q.SubjectRoomId));
                // 그때 자기가 있던 곳 — "알고 있다"와 "그 순간엔 거기 없었다"가 한 문장에 같이 들어간다.
                f.Set("where", RoomName(ctx.RoomAtSubject));
                // "그 사고는 처음 듣는다" 뒤에는 기억을 붙이지 않는다 — 가리킬 순간이 없다.
                memTopic = f.Variant == "none" ? RecallTopic.None : RecallTopic.Anomaly;
                memRoom = truthful ? ctx.RoomAtSubject : plan.RoomId;
                break;
            }

            case InterviewIntent.AskWhereAtIncident:
            {
                f.Topic = ReplyTopic.WhereAtIncident;
                f.Variant = "any";
                // 내세우는 방 — 결번 개체가 거짓 알리바이를 대는 중이면 주장한 방(ShiftMemory.Recall 의 Lying 과 같은 기준).
                string room = !truthful && !string.IsNullOrEmpty(claim.ClaimedRoomId) ? claim.ClaimedRoomId : plan.RoomId;
                // 끌어낼 기록이 없으면 방 이름을 대지 않는다. 빈 값은 '통로'로 읽히므로
                // 그대로 두면 가 본 적도 없는 통로에 있었다는 진술이 되어 자료로 남는다.
                if (string.IsNullOrEmpty(room))
                {
                    f.Variant = "unknown";
                    f.FallbackSlot = "Unknown.any";
                    memTopic = RecallTopic.Location;
                    break;
                }
                f.Set("room", RoomName(room));
                // B-2: 동석자 여부까지 사람이 통째로 쓴 한 문장(alone/with)이 캐릭터 파일에 있으면 그것을 쓴다.
                // 없으면 .any + 근무 기억(mem.alone/with) 조합을 그대로 쓴다.
                var others = DialogueContextBuilder.OccupantsAt(room, day, t, id);
                string variant = others.Count == 0 ? "alone" : "with";
                if (DialogueLineBank.HasOwn(id, "WhereAtIncident." + variant))
                {
                    f.Variant = variant;
                    if (others.Count > 0)
                    {
                        f.Set("who", Codename(others[0]));
                        RecordSighting(id, others[0], room, t, day);
                        f.MentionedEmployeeId = others[0]; f.MentionedRoomId = room;
                    }
                    covered.Add(MemoryKind.Companion);
                    if (q.HasAnchorTime) ShiftMemory.MarkCompanionAsked(id, day, q.AnchorTime);
                }
                memTopic = RecallTopic.Location;
                memRoom = room;
                // 이 답변은 그대로 '이 직원의 진술' 자료가 된다.
                RecordClaim(id, ctx.ClaimKey, room, t, day);
                break;
            }

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
                if (others.Count > 0)
                {
                    RecordSighting(id, others[0], plan.RoomId, t, day);
                    f.MentionedEmployeeId = others[0];
                    f.MentionedRoomId = plan.RoomId;
                }
                else f.SaidDontKnow = true;
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
                RecordClaim(id, ctx.ClaimKey, plan.RoomId, t, day);
                // 남의 증언을 인정/부정하는 답이다 — 위치를 새로 묻는 게 아니므로 기억을 붙이지 않는다.
                memTopic = RecallTopic.None;
                break;
            }

            case InterviewIntent.AskRestate:
                f.Topic = ReplyTopic.Restate;
                f.Variant = "same";
                f.Set("room", RoomName(plan.RoomId));
                // 같은 주장을 되풀이하는 것이므로 진술 자료도 그대로 유지된다.
                RecordClaim(id, ctx.ClaimKey, plan.RoomId, t, day);
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

            // ── 이동 따지기 — 같은 사실을 묻되 어조가 다르다. 답은 이동 이유와 같은 규칙으로 정하고,
            //    앞에 발끈하는 한마디(react.accused)를 붙인다.
            case InterviewIntent.ConfrontUnorderedMove:
                f.Topic = ReplyTopic.MoveReason;
                FillMoveReason(f, q, id, day, truthful);
                f.OpenerSlot = "react.accused";
                memTopic = RecallTopic.Movement;
                memRoom = truthful ? q.ToRoomId : plan.RoomId;
                covered.Add(MemoryKind.Relocated);
                covered.Add(MemoryKind.Dispatched);
                if (f.Variant is "task" or "repair") covered.Add(MemoryKind.Worked);
                RecordClaim(id, ctx.ClaimKey, truthful ? q.ToRoomId : plan.RoomId, t, day);
                break;

            // ── 통화 기록 ──
            case InterviewIntent.AskCallReason:
                f.Topic = ReplyTopic.CallReason;
                f.Variant = q.CallKind switch
                {
                    CallRecordKind.Missed => "missed",
                    CallRecordKind.ManagerCalled => "manager",
                    _ => q.CallEvent switch
                    {
                        DialogueRepository.EventIdleVisit => "idle",
                        DialogueRepository.EventIdleWorry => "worry",
                        _ => "report",
                    },
                };
                f.Set("room", RoomName(string.IsNullOrEmpty(q.CallRoomId) ? q.SubjectRoomId : q.CallRoomId));
                f.Set("here", RoomName(q.SubjectRoomId));
                // 왜 전화했나 — 위치 · 동선을 묻는 답이 아니다. 기억은 "통화 뒤 무엇을 했나"(CallAfter)에만.
                memTopic = RecallTopic.None;
                covered.Add(MemoryKind.CallReported);
                covered.Add(MemoryKind.CallMissed);
                covered.Add(MemoryKind.ManagerCalled);
                break;

            case InterviewIntent.AskCallAfter:
            {
                f.Topic = ReplyTopic.CallAfter;
                string next = truthful ? DialogueContextBuilder.RoomAfter(id, day, t) : "";
                var (kind, task) = WorkAt(id, day, q.SubjectRoomId, t);
                if (!truthful) f.Variant = "evasive";
                else if (!string.IsNullOrEmpty(next)) { f.Variant = "moved"; f.Set("next", RoomName(next)); }
                else if (kind is "task" or "repair") { f.Variant = "task"; f.Set("task", kind == "repair" ? "수리" : task); }
                else f.Variant = "stayed";
                f.Set("room", RoomName(q.SubjectRoomId));
                memTopic = RecallTopic.Presence;
                memRoom = q.SubjectRoomId;
                if (f.Variant == "task") covered.Add(MemoryKind.Worked);
                if (f.Variant == "moved") covered.Add(MemoryKind.Relocated);
                break;
            }

            // 근무 태만 추궁 — 지시 없이 자리를 옮긴 기록이 화면에 떴으면 발뺌할 수 없다(caught).
            // 그런 기록이 없으면 "자리는 지켰다"고 항변한다(justify). 결번 개체는 흐린다.
            case InterviewIntent.ConfrontNeglect:
            {
                f.Topic = ReplyTopic.Neglect;
                bool left = PlayerKnownEvidence.VisibleMoves(id, day).Any(m => !m.PlayerOrdered);
                f.Variant = !truthful ? "evasive" : left ? "caught" : "justify";
                f.Set("n", q.RepeatIndex.ToString());
                f.Set("room", RoomName(q.CallRoomId));
                f.OpenerSlot = "react.accused";
                memTopic = RecallTopic.None;   // 추궁에 대한 항변 — 기억을 덧붙이지 않는다
                break;
            }

            // ── 이상 개체 조우 — 성격(AvoidsDanger)이 겁을 정한다. 양 · 토끼는 무너지고, 늑대는 담담하다.
            case InterviewIntent.AskGhostWellbeing:
                f.Topic = ReplyTopic.GhostState;
                f.Variant = Frightened(id, ctx) ? "shaken" : "ok";
                f.Set("room", RoomName(q.SubjectRoomId));
                // 안부 · 행동 답은 그 자체로 완결이다 — 동석자는 AskGhostOthers 가 따로 묻는다.
                memTopic = RecallTopic.None;
                break;

            case InterviewIntent.AskGhostAppearance:
                f.Topic = ReplyTopic.GhostLook;
                f.Variant = Frightened(id, ctx) ? "fear" : "calm";
                f.Set("room", RoomName(q.SubjectRoomId));
                break;

            case InterviewIntent.AskGhostWhatHappened:
            {
                f.Topic = ReplyTopic.GhostAct;
                int avoid = EmployeeTraits.Get(id).AvoidsDanger;
                f.Variant = avoid >= 2 ? "hid" : avoid == 1 ? "froze" : "worked";
                // 놓친 개체 — 설비까지 부서졌다는 보정 한 줄.
                if (q.IncidentType == LogEventType.AnomalyIncident) f.Caveats.Add("GhostAct.struck");
                f.Set("room", RoomName(q.SubjectRoomId));
                memTopic = RecallTopic.None;
                break;
            }

            case InterviewIntent.AskGhostOthers:
            {
                f.Topic = ReplyTopic.GhostOthers;
                string other = q.OtherEmployeeId;
                if (string.IsNullOrEmpty(other)) { f.Variant = "none"; break; }
                int oa = EmployeeTraits.Get(other).AvoidsDanger;
                f.Variant = oa >= 2 ? "panicked" : oa == 1 ? "froze" : "calm";
                f.Set("who", Codename(other));
                f.Set("room", RoomName(q.SubjectRoomId));
                // "그 사람이 그때 그 방에서 …했다" — 행동까지 실린 목격 증언으로 남는다.
                string detail = f.Variant switch
                {
                    "panicked" => "이상 개체 앞에서 주저앉았다",
                    "froze" => "이상 개체 앞에서 굳어 있었다",
                    _ => "이상 개체 앞에서도 자리를 지켰다",
                };
                PlayerKnownEvidence.RecordSighting(id, other, q.SubjectRoomId, t, detail, day);
                break;
            }

            // ── 알리바이 · 따지기 ──
            // 내세우는 위치(결번 개체는 주장한 방)에 같이 있던 사람을 댄다 — 그 사람의 위치까지 걸린 진술이다.
            case InterviewIntent.AskAlibiProof:
            {
                f.Topic = ReplyTopic.AlibiProof;
                string room = plan.RoomId;
                var others = DialogueContextBuilder.OccupantsAt(room, day, t, id);
                if (others.Count > 0)
                {
                    f.Variant = "witness"; f.Set("who", Codename(others[0]));
                    RecordSighting(id, others[0], room, t, day);
                    f.MentionedEmployeeId = others[0]; f.MentionedRoomId = room;
                }
                else if (!truthful) f.Variant = "evasive";
                else f.Variant = PlayerKnownEvidence.HasRoomRecord(room) ? "cctv" : "none";
                f.Set("room", RoomName(room));
                RecordClaim(id, ctx.ClaimKey, room, t, day);
                memTopic = RecallTopic.Location;
                memRoom = room;
                covered.Add(MemoryKind.Companion);
                if (q.HasAnchorTime) ShiftMemory.MarkCompanionAsked(id, day, q.AnchorTime);
                break;
            }

            // 결백한 직원은 하던 일 · 있던 곳을 댄다. 결번 개체는 흐리거나(거짓 알리바이) "손대지 않았다"고
            // 못 박는다 — 그 주장(Denial.equipment)이 카드로 남아 동료 증언과 부딪힐 수 있다(§3-2).
            case InterviewIntent.AskProveInnocence:
            {
                f.Topic = ReplyTopic.Innocence;
                string room = plan.RoomId;
                bool inRoom = !string.IsNullOrEmpty(q.SubjectRoomId) && room == q.SubjectRoomId;
                var (kind, task) = WorkAt(id, day, room, t);
                if (ctx.IsSaboteur && !truthful) f.Variant = "evasive";
                else if (ctx.IsSaboteur) f.Variant = "deny";
                else if (!inRoom) f.Variant = "elsewhere";
                else if (kind is "task" or "repair") { f.Variant = "task"; f.Set("task", kind == "repair" ? "수리" : task); }
                else f.Variant = "plain";
                f.Set("room", RoomName(room));
                f.OpenerSlot = "react.accused";
                RecordClaim(id, ctx.ClaimKey, room, t, day);
                memTopic = RecallTopic.Presence;
                memRoom = room;
                if (f.Variant == "task") covered.Add(MemoryKind.Worked);
                break;
            }

            // 직접 본 수상한 행동이 있을 때만 사람을 댄다 — 그 목격이 행동까지 실린 증언 카드가 된다.
            // 본 것이 없으면 아무도 지목하지 않는다. 결번 개체는 이름 없이 남을 흘려 넣는다.
            case InterviewIntent.AskSuspectOpinion:
            {
                f.Topic = ReplyTopic.Suspect;
                string suspect = truthful ? ctx.KnownSuspiciousActorId : "";
                if (!string.IsNullOrEmpty(suspect))
                {
                    f.Variant = "named";
                    f.Set("who", Codename(suspect));
                    f.Set("room", RoomName(ctx.KnownSuspicious?.RoomId ?? ""));
                    PlayerKnownEvidence.RecordSighting(id, suspect, ctx.KnownSuspicious?.RoomId ?? "",
                        ctx.KnownSuspicious?.TimeSeconds ?? -1f, ctx.KnownSuspiciousDetail, day);
                }
                else f.Variant = ctx.IsSaboteur ? "deflect" : "none";
                break;
            }

            // "기록상 거기 있었다" — 결백한 직원은 인정하고, 거짓 알리바이를 댄 결번 개체는 부정한다
            // (그 부정이 진술 카드로 남아 CCTV · 로그와 맞부딪힌다). 제자리 범행이면 있었던 건 인정하되 흐린다.
            case InterviewIntent.PressPresence:
                f.Topic = ReplyTopic.PressPresence;
                f.Variant = !truthful ? "deny" : ctx.IsSaboteur ? "evasive" : "admit";
                f.Set("room", RoomName(q.SubjectRoomId));
                f.OpenerSlot = "react.accused";
                RecordClaim(id, ctx.ClaimKey, plan.RoomId, t, day);
                memTopic = RecallTopic.Location;
                memRoom = plan.RoomId;
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

        // 결번의 "설비 쪽엔 손도 안 댔다" — 자기 위치 · 그 방에 있던 이유 · 거기서 한 일 · 무관하다는
        // 근거 · 재석 추궁을 답할 때만 붙는다(KoreanDialogueComposer 와 같은 규칙). 결백한 직원은 오지 않는다.
        KoreanDialogueComposer.ApplyEquipmentDenial(f, ctx,
            f.Topic is ReplyTopic.PresenceReason or ReplyTopic.ActionThere
                or ReplyTopic.Innocence or ReplyTopic.PressPresence);
        return f;
    }

    // 이상 개체 앞에서 무너지는 쪽인가 — 성격 축(GhostHauntSystem.FearScale)과 지금 상태(기절)로 본다.
    private static bool Frightened(string employeeId, DialogueContext ctx) =>
        GhostHauntSystem.FearScale(employeeId) >= 1.2f || ctx.Incapacitated;

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
        // 기록이 없으면 빈 값이다 — 지금 배치된 방으로 메우지 않는다.
        return ctx.RoomAtSubject;
    }

    // 이동 이유는 실제 로그에서만 찾는다. 찾지 못하면 지어내지 않는다.
    // 그날 쓰러져서 의무실로 **실려 간** 기록이 있는가(그 시각 전후).
    //
    // 이게 없으면 "왜 의무실에 갔습니까" 에 "상태 보러 갔다 / 잠깐 들렀다" 같은 답이 나온다.
    // 의식이 없는 채로 동료가 업고 간 사람에게는 말이 안 되는 답이다 — 본인은 간 게 아니다.
    private static bool CarriedToMedical(string id, int day, string toRoomId, float time)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (toRoomId != FacilitySimulation.MedicalRoomIdPublic) return false;
        var log = EventLog.Instance;
        if (log == null) return false;
        // 기절은 의무실 도착보다 앞선다. 같은 근무 안에서 그 앞쪽을 본다.
        return log.GetAllEntries().Any(e => e.Day == day
                                            && e.Detail == LogDetail.Fainted
                                            && e.ActorEmployeeId == id
                                            && e.GameTimeSeconds <= time + 1f);
    }


    // 그 시각 **이후에** 쓰러진 기록이 있는가.
    //
    // "그 뒤에 어디로 갔습니까" 에 쓰인다. 들것에 실려 간 길은 본인의 이동이 아니므로
    // 시설 로그의 동선에 남지 않는다(FacilityLogFormatter 가 일부러 뺀다). 그 상태로
    // 동선만 보고 답하면 "안 옮겼습니다" 가 나오는데, 그건 사실이 아니다.
    private static bool FaintedAfter(string id, int day, float time)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var log = EventLog.Instance;
        if (log == null) return false;
        return log.GetAllEntries().Any(e => e.Day == day
                                            && e.Detail == LogDetail.Fainted
                                            && e.ActorEmployeeId == id
                                            && e.GameTimeSeconds >= time - 1f);
    }

    // 이 이동이 실제로 어떤 이동이었는가. **질문을 만드는 쪽과 답하는 쪽이 같은 기준을
    // 써야** 한다 — 사유마다 첫 질문이 달라지는데(기획안 §3-⑤) 판정이 어긋나면
    // "도착했을 때 어땠나" 를 묻고 "쓰러져서 실려 갔다" 가 돌아온다.
    //
    //   carried    쓰러져서 동료가 업고 갔다 — 본인에게 이동 이유를 묻지 않는다
    //   ordered    관리자가 직접 배치했다
    //   dispatched 관리자가 전화로 "가 보라"고 했다(사고 대응)
    //   repair     그 방의 수리 작업 때문에 갔다
    //   task       그 방의 업무 때문에 갔다
    //   plain      업무 기록이 없는 이동 — 무단 이동
    public static string MoveKindOf(string id, int day, string toRoomId, float at, bool playerOrdered)
    {
        if (CarriedToMedical(id, day, toRoomId, at)) return "carried";
        if (playerOrdered) return "ordered";
        float window = ShiftMemory.RecallWindowMinutes * DialogueClock.SecondsPerMinute;
        if (CallMemoryLog.For(id, day).Any(r => r.Kind == CallRecordKind.OrderedGo && r.RoomId == toRoomId
                                                && r.Time <= at + 1f && r.Time >= at - window))
            return "dispatched";
        var (kind, _) = WorkAt(id, day, toRoomId, at);
        return kind == "repair" ? "repair" : kind == "task" ? "task" : "plain";
    }

    private static void FillMoveReason(ReplyFrame f, InterviewQuestion q, string id, int day, bool truthful)
    {
        string kind = MoveKindOf(id, day, q.ToRoomId, q.AnchorTime, q.PlayerOrderedMove);
        if (kind == "task")
        {
            var (_, task) = WorkAt(id, day, q.ToRoomId, q.AnchorTime);
            f.Variant = "task";
            f.Set("task", task);
            return;
        }
        // 업무 기록이 없는 이동. 결백한 직원에게는 그냥 별일 아닌 이동이고,
        // 방해자에게는 설명할 수 없는 이동이다 — 여기서 갈린다.
        f.Variant = kind == "plain" ? (truthful ? "plain" : "evasive") : kind;
    }

    private static void FillPresenceReason(ReplyFrame f, InterviewQuestion q, string id, int day,
        float t, bool truthful)
    {
        // 의무실에 "있었던" 게 아니라 누워 있었다 — 쓰러진 뒤라면 그것부터 말한다.
        if (CarriedToMedical(id, day, q.SubjectRoomId, t)) { f.Variant = "carried"; return; }
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
    // anchorDay 가 0 이면 오늘이다. 자료가 어제 것이면 반드시 그 날로 들어와야 한다 —
    // 사건 키도 그 날의 로그에서 찾아야 하고(오늘에서 찾으면 사건이 통째로 사라진다),
    // 위치도 그 날의 동선에서 읽어야 한다.
    private static DialogueContext Anchor(string employeeId, float anchorTime, string incidentKey, int anchorDay = 0)
    {
        int day = anchorDay > 0 ? anchorDay : DialogueContextBuilder.Day();
        var subject = DialogueContextBuilder.FindByKey(day, incidentKey);
        string claimKey = !string.IsNullOrEmpty(incidentKey) ? incidentKey
            : anchorTime >= 0f ? $"t:{anchorTime:0.0}" : "no_incident";

        var ctx = DialogueContextBuilder.BuildAt(employeeId, DialogueConversationKind.Interview,
            DialogueQuestions.Where, subject, anchorTime, claimKey, day);
        ctx.TargetEmployeeId = LocalDialogueGenerator.OpinionTargetId(employeeId);
        return ctx;
    }

    // 직원이 관리자에게 말한 위치는 그대로 플레이어의 자료가 된다.
    //
    // 모든 대사를 자료로 만들지는 않는다(기분·소감·되묻기는 남기지 않는다).
    // 여기 들어오는 것은 "언제 · 어디" 가 붙은 주장뿐이고, 그래야 나중에 로그·CCTV 와
    // 맞대어 볼 수 있다.
    // subjectDay 는 이 주장이 가리키는 근무일이다. 말한 날이 아니다.
    private static void RecordClaim(string employeeId, string claimKey, string roomId, float time, int subjectDay)
    {
        if (string.IsNullOrEmpty(roomId) || time < 0f) return;
        PlayerKnownEvidence.RecordLocationStatement(employeeId, claimKey, roomId, true, time, subjectDay);
    }

    // "그 사람을 거기서 봤다" 는 말도 자료가 된다 — 다른 직원을 심문할 때 그대로 쓴다.
    private static void RecordSighting(string speakerId, string subjectId, string roomId, float time, int subjectDay)
    {
        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(roomId)) return;
        PlayerKnownEvidence.RecordSighting(speakerId, subjectId, roomId, time, "", subjectDay);
    }

    // ── 2단계 도우미 — 전부 "기록에 있는 것만" 돌려준다 ──────────────────

    // 이 직원이 저 직원에 대해 실제로 말해 둔 목격 내용. 없으면 빈 값이다.
    // 비어 있다는 것은 "거기 있는 것만 봤다" 는 뜻이지 "아무 일도 없었다" 가 아니다.
    private static string SightingDetail(string speakerId, string subjectId, int day)
    {
        if (string.IsNullOrEmpty(subjectId)) return "";
        var s = PlayerKnownEvidence.AllStatementsOfSighting(speakerId, subjectId, day);
        return s?.Detail ?? "";
    }

    // 그 목격이 일어난 방. 기록이 없으면 질문이 들고 온 방을 쓴다.
    private static string SightingRoom(string speakerId, string subjectId, int day, string fallback)
    {
        var s = PlayerKnownEvidence.AllStatementsOfSighting(speakerId, subjectId, day);
        return string.IsNullOrEmpty(s?.RoomId) ? fallback : s.RoomId;
    }

    // 그 방에서 그 시각 앞뒤로 실제로 일어난 사고 한 건. 없으면 null — 지어내지 않는다.
    private static LogEntry IncidentIn(string roomId, int day, float at)
    {
        if (string.IsNullOrEmpty(roomId)) return null;
        float window = EvidenceContradiction.WindowMinutes * DialogueClock.SecondsPerMinute * 2f;
        return EventLog.Instance?.GetAllEntries().FirstOrDefault(e => e.Day == day
            && e.RoomId == roomId
            && Mathf.Abs(e.GameTimeSeconds - at) <= window
            && InterviewEvidenceBoard.IsIncidentType(e.EventType));
    }

    // 사고를 부르는 짧은 말. 로그 문장을 그대로 읽지 않는다 — 직원이 쓸 말이 아니다.
    private static string IncidentWord(LogEventType type) => type switch
    {
        LogEventType.PowerOutage => "정전",
        LogEventType.TaskFailed => "설비 고장",
        LogEventType.Sabotage => "설비 사고",
        LogEventType.AnomalyIncident => "이상 현상",
        LogEventType.CctvDisconnect => "감시 장비 고장",
        LogEventType.Death => "사망 사고",
        _ => "사고",
    };

    private static string RoomName(string roomId) => InterviewEvidenceBoard.RoomName(roomId);
    private static string Codename(string employeeId) => InterviewEvidenceBoard.Codename(employeeId);
}
