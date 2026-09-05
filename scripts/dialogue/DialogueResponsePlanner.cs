using System.Linq;
using Godot;
using NSP.Data;

namespace NSP.Dialogue;

// "무엇을 말할 것인가"를 결정한다. 문장은 만들지 않는다.
//
//  · 일반 직원은 거짓말하지 않는다. 다만 성격에 따라 말하는 양이 다르다.
//  · 방해자는 먼저 진짜 사실을 계산한 뒤, 그 질문이 자신에게 얼마나 위험한지 보고
//    전략(진실/생략/축소/합리화/회피/전가/부정)을 고른다. 전략과 알리바이는
//    DialogueClaimState 에 저장되어 같은 사건 내내 유지된다.
public static class DialogueResponsePlanner
{
    public static DialogueResponsePlan Plan(DialogueContext ctx)
    {
        var profile = DialogueVoiceProfiles.Get(ctx.EmployeeId);
        var claim = DialogueClaimState.Get(ctx.EmployeeId, ctx.CurrentDay, ctx.Subject?.Key ?? "no_incident");

        var plan = new DialogueResponsePlan
        {
            EvidenceCount = ctx.EvidenceAgainstCount,
            IsRepeat = ctx.IsRepeat,
            TargetEmployeeId = ctx.TargetEmployeeId,
        };

        var mode = ctx.IsSaboteur ? DecideMode(ctx, claim, profile) : DeceptionMode.None;
        plan.Deception = mode;
        if (ctx.IsSaboteur) EnsureClaimedRoom(ctx, claim, mode);

        switch (ctx.QuestionId)
        {
            case DialogueQuestions.Anomaly: PlanAnomaly(ctx, plan, profile, claim); break;
            case DialogueQuestions.Where: PlanWhere(ctx, plan, profile, claim); break;
            case DialogueQuestions.Suspicious: PlanSuspicious(ctx, plan, profile, claim); break;
            case DialogueQuestions.Opinion: PlanOpinion(ctx, plan, profile); break;
            case DialogueQuestions.Accuse: PlanAccuse(ctx, plan, profile); break;
            case DialogueQuestions.GeneralStatus: PlanStatus(ctx, plan, profile); break;
            case DialogueQuestions.GeneralFocus: PlanComply(ctx, plan, profile); break;
            case DialogueQuestions.GeneralAnomaly: PlanAnomaly(ctx, plan, profile, claim); break;
            case DialogueQuestions.IncidentReport: PlanIncidentReport(ctx, plan, profile); break;
            case DialogueQuestions.DispatchAccept:
                plan.Core = CoreKind.DispatchAccept;
                plan.AllowSupport = false;
                break;
            case DialogueQuestions.DispatchDecline:
                plan.Core = CoreKind.DispatchDecline;
                plan.AllowSupport = false;
                break;
            default: plan.Core = CoreKind.NoAnomaly; break;
        }

        ApplyStyle(ctx, plan, profile);
        return plan;
    }

    // --- 질문별 계획 ----------------------------------------------------

    // Q1 / 일반통화 "이상현상": 실제로 아는 만큼만. 간접 인지는 감각 정보까지가 상한이다.
    private static void PlanAnomaly(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceProfile profile, DialogueClaim claim)
    {
        var fact = ctx.Subject;
        var knowledge = ctx.SubjectKnowledge;

        // 방해자는 자기 알리바이가 허용하는 범위에서만 사건을 안다고 말한다.
        if (ctx.IsSaboteur && fact != null && plan.Deception != DeceptionMode.Truth)
        {
            knowledge = KnowledgeFromRoom(claim.ClaimedRoomId, fact.RoomId);
            plan.NeedsUnknownCauseCaveat = ctx.IsSubjectActor;
        }

        if (fact == null || knowledge == KnowledgeLevel.None)
        {
            plan.Core = CoreKind.NoAnomaly;
            plan.Certainty = Certainty.Medium;
            return;
        }

        plan.IncidentRoomId = fact.RoomId;
        plan.IncidentType = fact.Type;
        plan.IncidentTimeSeconds = fact.TimeSeconds;
        plan.Knowledge = knowledge;

        if (knowledge == KnowledgeLevel.Direct)
        {
            plan.Core = CoreKind.IncidentDirect;
            plan.Certainty = Certainty.High;
            plan.Emotion = EmotionOf(fact.Type);
        }
        else
        {
            plan.Core = CoreKind.IncidentIndirect;
            plan.Certainty = Certainty.Medium;
            // 간접 목격자가 직접 본 것처럼 말하지 않도록 하는 보정. 생략되지 않는다.
            plan.NeedsIndirectCaveat = true;
            plan.Emotion = EmotionOf(fact.Type);
        }
    }

    // Q2: 사건이 일어난 그 순간의 위치. 시간·사고명을 자동으로 덧붙이지 않는다.
    private static void PlanWhere(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceProfile profile, DialogueClaim claim)
    {
        plan.Core = CoreKind.SelfLocation;
        plan.RoomId = ctx.IsSaboteur ? claim.ClaimedRoomId : ctx.RoomAtSubject;
        if (string.IsNullOrEmpty(plan.RoomId)) plan.RoomId = ctx.AssignedRoomId;
        plan.Certainty = Certainty.High;

        if (!ctx.IsSaboteur) return;

        switch (plan.Deception)
        {
            case DeceptionMode.Vague:
                plan.Certainty = Certainty.Low;
                break;
            case DeceptionMode.Minimize:
            case DeceptionMode.Justify:
                // 이동 자체는 인정하고 이유/규모를 붙인다.
                plan.AllowSupport = true;
                break;
            case DeceptionMode.Deny:
                plan.Certainty = Certainty.High;
                break;
        }
    }

    // Q3: 실제 목격 기록이 없으면 어떤 경우에도 사람을 지목하지 않는다.
    private static void PlanSuspicious(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceProfile profile, DialogueClaim claim)
    {
        bool canPoint = ctx.KnownSuspicious != null && !string.IsNullOrEmpty(ctx.KnownSuspiciousActorId);
        if (!canPoint)
        {
            plan.Core = CoreKind.NoSighting;
            plan.Certainty = Certainty.Medium;
            return;
        }

        plan.Core = CoreKind.SuspiciousSighting;
        plan.SubjectEmployeeId = string.IsNullOrEmpty(claim.MentionedSuspectId)
            ? ctx.KnownSuspiciousActorId
            : claim.MentionedSuspectId;
        claim.MentionedSuspectId = plan.SubjectEmployeeId;
        plan.IncidentRoomId = ctx.KnownSuspicious.RoomId;
        plan.Certainty = Certainty.Medium;
    }

    private static void PlanOpinion(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceProfile profile)
    {
        plan.Core = CoreKind.Opinion;
        plan.Certainty = Certainty.Medium;
        // 오늘 실제로 마주친 적이 있어야 "오늘 봤다"는 보조 문장을 허용한다.
        plan.AllowSupport = ctx.SeenEmployeeIds.Contains(ctx.TargetEmployeeId);
    }

    private static void PlanAccuse(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceProfile profile)
    {
        plan.Core = CoreKind.DenyAccusation;
        plan.Certainty = ctx.EvidenceAgainstCount > 0 ? Certainty.Medium : Certainty.High;
        plan.Emotion = ctx.EvidenceAgainstCount > 0 ? EmotionKind.Alarm : EmotionKind.Composed;
        if (ctx.IsSaboteur && ctx.EvidenceAgainstCount >= 2)
            plan.Deception = DeceptionMode.Deny;
    }

    // 일반 통화 "작업은 잘 되어가나요?" — 지금 상태를 실제로 읽어 답한다.
    private static void PlanStatus(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceProfile profile)
    {
        plan.Core = CoreKind.StatusReport;
        plan.RoomId = string.IsNullOrEmpty(ctx.CurrentRoomId) ? ctx.AssignedRoomId : ctx.CurrentRoomId;

        if (ctx.IsMoving) plan.StatusNote = "moving";
        else if (ctx.RoomUnderRepair) plan.StatusNote = "repair";
        else if (ctx.RoomBlockedByMaterials) plan.StatusNote = "blocked";
        else if (ctx.Stress >= 31f) plan.StatusNote = "stress";
        else if (!ctx.HasActiveTask) plan.StatusNote = "idle";
        else plan.StatusNote = "ok";

        plan.Certainty = plan.StatusNote == "ok" ? Certainty.High : Certainty.Medium;
        plan.Emotion = plan.StatusNote switch
        {
            "stress" => EmotionKind.Fear,
            "blocked" => EmotionKind.Annoyance,
            "repair" => EmotionKind.Alarm,
            _ => EmotionKind.None,
        };
        plan.MentionTask = ctx.HasActiveTask && GD.Randf() < profile.TaskMentionChance;
    }

    private static void PlanComply(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceProfile profile)
    {
        plan.Core = CoreKind.Comply;
        plan.Certainty = Certainty.High;
        plan.Emotion = ctx.Stress >= 31f ? EmotionKind.Fear : EmotionKind.None;
    }

    // 직원이 먼저 거는 사고 신고. 실제 RoomId / EventType 을 사용한다.
    private static void PlanIncidentReport(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceProfile profile)
    {
        plan.Core = CoreKind.IncidentReport;
        var fact = ctx.Subject;
        if (fact == null)
        {
            plan.Core = CoreKind.NoAnomaly;
            return;
        }
        plan.IncidentRoomId = fact.RoomId;
        plan.IncidentType = fact.Type;
        plan.IncidentTimeSeconds = fact.TimeSeconds;
        plan.Knowledge = fact.Knowledge == KnowledgeLevel.Direct ? KnowledgeLevel.Direct : KnowledgeLevel.Indirect;
        // 신고 문장 자체가 "쪽에서 소리가 났다" 형태라 간접임이 이미 드러난다.
        // 여기에 보정문을 또 붙이면 지시 요청 뒤에 설명이 붙어 어색해진다.
        plan.NeedsIndirectCaveat = false;
        plan.AllowSupport = false;
        plan.Certainty = plan.Knowledge == KnowledgeLevel.Direct ? Certainty.High : Certainty.Medium;
        plan.Emotion = EmotionOf(fact.Type);
    }

    // --- 성격 반영 ------------------------------------------------------

    // 시간 언급·보조 정보 여부는 여기서만 정한다. 기본은 "말하지 않는다"이다.
    private static void ApplyStyle(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceProfile profile)
    {
        // 통화 중 사고 신고는 지금 벌어진 일이라 시각을 말하지 않는다.
        bool timeMatters = plan.Core is CoreKind.IncidentDirect or CoreKind.IncidentIndirect;
        if (timeMatters)
        {
            float r = GD.Randf();
            if (r < profile.ExactTimeChance) plan.Time = TimeRef.Exact;
            else if (r < profile.ExactTimeChance + profile.VagueTimeChance) plan.Time = TimeRef.Vague;
        }
        else if (plan.Core == CoreKind.SelfLocation && GD.Randf() < profile.VagueTimeChance * 0.6f)
        {
            plan.Time = TimeRef.Vague;
        }

        // 방해자가 회피 중이면 정확한 시각을 말하지 않는다.
        if (plan.Deception is DeceptionMode.Vague or DeceptionMode.Omit && plan.Time == TimeRef.Exact)
            plan.Time = TimeRef.Vague;
        // 반복 질문에서는 "아까 …" 반응과 시간 표현이 겹친다.
        if (plan.IsRepeat && plan.Time == TimeRef.Vague) plan.Time = TimeRef.None;

        if (plan.Core == CoreKind.SelfLocation)
            plan.MentionTask = GD.Randf() < profile.TaskMentionChance;

        if (plan.Emotion != EmotionKind.None && GD.Randf() > profile.EmotionChance)
            plan.Emotion = EmotionKind.None;
    }

    private static EmotionKind EmotionOf(LogEventType type) => type switch
    {
        LogEventType.Death => EmotionKind.Fear,
        LogEventType.PowerOutage => EmotionKind.Alarm,
        LogEventType.TabooViolation => EmotionKind.Fear,
        LogEventType.TaskFailed => EmotionKind.Alarm,
        _ => EmotionKind.Composed,
    };

    // --- 방해자 전략 ----------------------------------------------------

    private static DeceptionMode DecideMode(DialogueContext ctx, DialogueClaim claim, DialogueVoiceProfile profile)
    {
        if (claim.ModeDecided) return claim.Mode;
        claim.ModeDecided = true;

        // 진실을 말해도 불리하지 않으면 그냥 진실을 말한다.
        bool outOfPlace = !string.IsNullOrEmpty(ctx.AssignedRoomId)
                          && !string.IsNullOrEmpty(ctx.RoomAtSubject)
                          && ctx.RoomAtSubject != ctx.AssignedRoomId;
        if (!ctx.IsSubjectActor && !outOfPlace && ctx.EvidenceAgainstCount == 0)
        {
            claim.Mode = DeceptionMode.Truth;
            return claim.Mode;
        }

        var eligible = profile.DeceptionOrder
            .Where(m => m != DeceptionMode.Deny || ctx.EvidenceAgainstCount >= 2)
            .Where(m => m != DeceptionMode.Redirect || ctx.KnownSuspicious != null)
            .ToList();
        if (eligible.Count == 0) { claim.Mode = DeceptionMode.Omit; return claim.Mode; }

        // 선호 순서를 지키되 항상 1순위만 쓰지는 않는다.
        float r = GD.Randf();
        int index = r < 0.55f ? 0 : r < 0.85f ? 1 : 2;
        claim.Mode = eligible[Mathf.Min(index, eligible.Count - 1)];
        return claim.Mode;
    }

    private static void EnsureClaimedRoom(DialogueContext ctx, DialogueClaim claim, DeceptionMode mode)
    {
        if (!string.IsNullOrEmpty(claim.ClaimedRoomId)) return;

        string real = string.IsNullOrEmpty(ctx.RoomAtSubject) ? ctx.AssignedRoomId : ctx.RoomAtSubject;
        bool hides = mode is DeceptionMode.Omit or DeceptionMode.Vague
            or DeceptionMode.Redirect or DeceptionMode.Deny;
        string cover = string.IsNullOrEmpty(ctx.AssignedRoomId) ? real : ctx.AssignedRoomId;

        claim.ClaimedRoomId = hides && cover != real ? cover : real;
        claim.ClaimTruthful = claim.ClaimedRoomId == real;
    }

    // 주장한 위치에서 그 사건을 어디까지 알 수 있는가 — 거짓말도 앞뒤가 맞아야 한다.
    private static KnowledgeLevel KnowledgeFromRoom(string room, string incidentRoom)
    {
        if (string.IsNullOrEmpty(room) || string.IsNullOrEmpty(incidentRoom)) return KnowledgeLevel.None;
        if (room == incidentRoom) return KnowledgeLevel.Direct;
        return DialogueContextBuilder.IsAdjacent(room, incidentRoom)
            ? KnowledgeLevel.Indirect
            : KnowledgeLevel.None;
    }
}
