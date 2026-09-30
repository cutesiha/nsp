using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 기본 질문 · 통화의 계획(DialogueResponsePlan)을 ReplyFrame 으로 옮겨 DialogueComposer 에 넘긴다.
//
// Local Dialogue V2 에서 이 클래스는 더 이상 문장을 갖고 있지 않다.
//   1) DialogueResponsePlanner   무엇을 말할지(사실 · 방해자 전략)
//   2) DialogueUtterancePlanner  어떤 모양으로 말할지(되받기 · 반응 · 덧붙임 · 문장 수)
//   3) 여기                      둘을 ReplyFrame 하나로 묶고, 근무 기억을 얹는다
//   4) DialogueComposer          data/dialogue/lines 의 완성 문장 틀로 표현한다
//
// 사건 표현은 LogEntry.Description 을 쓰지 않고 EventType 에서 다시 만든다.
// 간접 인지일 때는 감각(소리·진동·불빛) 표현만 쓰며 원인을 말하지 않는다.
public static class KoreanDialogueComposer
{
    public static string Compose(DialogueContext ctx, DialogueResponsePlan plan, RecallResult memory = null) =>
        DialogueComposer.Compose(ToFrame(ctx, plan, memory));

    public static ReplyFrame ToFrame(DialogueContext ctx, DialogueResponsePlan plan, RecallResult memory = null)
    {
        var voice = DialogueVoices.Get(ctx.EmployeeId);
        var vars = Vars(ctx, plan, voice);
        string coreSlot = CompanionVariant(ctx, plan, memory, vars) ?? CoreSlot(plan);
        var up = DialogueUtterancePlanner.Plan(ctx, plan, voice, vars, coreSlot);

        var f = new ReplyFrame
        {
            EmployeeId = ctx.EmployeeId,
            CustomSlot = up.CoreSlot,
            FallbackSlot = "noanomaly",
            TimeWord = up.TimeWord,
            ExtraSlot = up.ExtraSlot,
            MaxSentences = up.MaxSentences,
            MaxExclamations = up.MaxExclamations,
        };
        foreach (var kv in vars) f.Vars[kv.Key] = kv.Value;

        switch (up.Shape)
        {
            case UtteranceShape.TopicEchoCore: f.OpenerText = up.EchoText; break;
            case UtteranceShape.ReactionCore: f.OpenerSlot = up.ReactionSlot; break;
            case UtteranceShape.RepeatCore: f.OpenerSlot = up.RepeatSlot; break;
            case UtteranceShape.CoreBackQuestion: f.BackSlot = up.BackQuestionSlot; break;
        }
        f.Caveats.AddRange(up.CaveatSlots);
        ApplyEquipmentDenial(f, ctx, plan.Core is CoreKind.SelfLocation);

        if (memory != null)
        {
            f.Addenda.AddRange(memory.Addenda);
            // 결번자 흉내의 어긋남 — 겁먹어야 할 자리에서 놀란 기색이 없다.
            if (memory.SuppressFear && f.OpenerSlot is "emotion.fear" or "emotion.alarm") f.OpenerSlot = "";
        }
        return f;
    }

    // 결번자가 "설비 근처에는 가지 않았다"고 못 박은 사건이면, 그 답변 뒤에 한 줄을 붙인다.
    //
    // 붙는 자리는 Caveats 다 — 문장 수 상한에 걸려 빠지면 안 되기 때문이다. 이 한 줄은
    // 꾸밈이 아니라 **주장**이고, 나가는 순간 조사 자료의 진술 카드가 된다(§3-2).
    // 그래서 여기서 바로 기록한다.
    //
    // 붙는 답변은 셋뿐이다 — 자기 위치(SelfLocation) · 그 방에 있던 이유(PresenceReason) ·
    // 거기서 한 일(ActionAtDestination). 그 밖의 질문에 끼워 넣으면 묻지도 않은 변명이 된다.
    // 결백한 직원은 애초에 DeniesEquipmentContact 가 false 라 이 자리에 오지 않는다.
    public static void ApplyEquipmentDenial(ReplyFrame f, DialogueContext ctx, bool topicFits)
    {
        if (f == null || ctx == null || !topicFits || string.IsNullOrEmpty(ctx.ClaimKey)) return;
        var claim = DialogueClaimState.Get(ctx.EmployeeId, ctx.CurrentDay, ctx.ClaimKey);
        if (!claim.DeniesEquipmentContact) return;
        if (f.Caveats.Contains(EquipmentDenialSlot)) return;

        f.Caveats.Add(EquipmentDenialSlot);
        PlayerKnownEvidence.RecordBehaviorClaim(ctx.EmployeeId, ctx.ClaimKey, EquipmentDenialText,
            ctx.HasSubjectTime ? ctx.SubjectTime : -1f);
    }

    public const string EquipmentDenialSlot = "Denial.equipment";
    // 카드에 남는 주장 문구. 표현(슬롯 문장)은 캐릭터마다 달라도 주장은 하나다.
    public const string EquipmentDenialText = "설비 근처에 가지 않았다";

    // --- 동석자까지 한 문장으로 (B-2) --------------------------------------------
    //
    // "핵심 + 근무 기억 한 줄" 조합 중 가장 잦은 두 가지(혼자였다 · 누구와 있었다)를 사람이 통째로 쓴
    // 한 문장으로 바꾼다. 그 캐릭터 파일에 변형 슬롯이 있을 때만 — 없으면 null 을 돌려주고 기존
    // selfloc + mem.alone/with 조합이 그대로 나간다(캐릭터 파일을 하나씩 채워도 되게).
    //   · selfloc.alone / selfloc.with     — "그때 어디 있었나". 동석자는 내세우는 방(결번자가 거짓
    //     알리바이를 대는 중이면 주장한 방) 기준으로 센다 — ShiftMemory.Recall 이 Lying 일 때와 같다.
    //   · incident.direct.with             — 사고를 직접 봤고 옆에 누가 있었다(mem.with.incident 후보가 있을 때만).
    // 변형을 쓰면 그 기억 줄은 빼고 "이미 한 이야기"로 적는다 — 같은 세션에서 동료 얘기가 두 번 나오지 않게.
    private static string CompanionVariant(DialogueContext ctx, DialogueResponsePlan plan, RecallResult memory,
        Dictionary<string, string> vars)
    {
        if (plan.Core == CoreKind.SelfLocation && ctx.HasSubjectTime)
        {
            var claim = DialogueClaimState.Get(ctx.EmployeeId, ctx.CurrentDay, ctx.ClaimKey);
            bool lying = ctx.IsSaboteur && !claim.ClaimTruthful && !string.IsNullOrEmpty(claim.ClaimedRoomId);
            string room = lying ? claim.ClaimedRoomId
                : string.IsNullOrEmpty(plan.RoomId) ? ctx.AssignedRoomId : plan.RoomId;
            if (string.IsNullOrEmpty(room)) return null;
            var others = DialogueContextBuilder.OccupantsAt(room, ctx.CurrentDay, ctx.SubjectTime, ctx.EmployeeId);
            string slot = others.Count == 0 ? "selfloc.alone" : "selfloc.with";
            if (!DialogueLineBank.HasOwn(ctx.EmployeeId, slot)) return null;
            vars["room"] = RoomName(room);
            if (others.Count > 0)
            {
                vars["who"] = Codename(others[0]);
                // "그 시각 그 방에서 저 사람과 함께 있었다" — 상대의 위치까지 걸린 진술이다(AskWhoWasPresent 와 같다).
                PlayerKnownEvidence.RecordSighting(ctx.EmployeeId, others[0], room, ctx.SubjectTime);
            }
            DropCompanionMemory(memory, a => a.Slot.StartsWith("mem.with") || a.Slot == "mem.alone");
            return slot;
        }

        if (plan.Core == CoreKind.IncidentDirect && memory != null)
        {
            var with = memory.Addenda.FirstOrDefault(a => a.Slot == "mem.with.incident" && !ShiftMemory.WasSaid(a));
            if (with == null || !with.Vars.TryGetValue("who", out var who) || string.IsNullOrEmpty(who)) return null;
            if (!DialogueLineBank.HasOwn(ctx.EmployeeId, "incident.direct.with")) return null;
            vars["who"] = who;
            DropCompanionMemory(memory, a => a == with);
            return "incident.direct.with";
        }
        return null;
    }

    private static void DropCompanionMemory(RecallResult memory, System.Func<ReplyAddendum, bool> pick)
    {
        if (memory == null) return;
        foreach (var a in memory.Addenda.Where(pick).ToList())
        {
            ShiftMemory.MarkSaid(a);
            memory.Addenda.Remove(a);
        }
    }

    // --- 슬롯 결정 ------------------------------------------------------

    private static string CoreSlot(DialogueResponsePlan plan) => plan.Core switch
    {
        CoreKind.SelfLocation => "selfloc",
        CoreKind.IncidentDirect => "incident.direct",
        CoreKind.IncidentIndirect => "incident.indirect",
        CoreKind.NoAnomaly => "noanomaly",
        CoreKind.SuspiciousSighting => "sight",
        CoreKind.NoSighting => "nosight",
        CoreKind.Opinion => "opinion",
        CoreKind.DenyAccusation => plan.EvidenceCount > 0 ? "deny.evidence" : "deny",
        CoreKind.StatusReport => "status." + plan.StatusNote,
        CoreKind.Comply => "comply",
        CoreKind.IncidentReport => plan.StatusNote == "blackout" ? "report.blackout"
            : plan.Knowledge == KnowledgeLevel.Direct ? "report.direct" : "report.indirect",
        CoreKind.DispatchAccept => "accept",
        CoreKind.DispatchDecline => "decline",
        // 꼬리질문 답변 — 결(StatusNote)까지 슬롯 이름에 붙인다.
        CoreKind.PreviousLocation => "prevloc." + plan.StatusNote,
        CoreKind.NextAction => "nextact." + plan.StatusNote,
        CoreKind.WhoWasPresent => "present." + plan.StatusNote,
        CoreKind.WitnessAnswer => "witness." + plan.StatusNote,
        CoreKind.HeardDetail => "heard",
        CoreKind.SeenConfirm => "seen." + plan.StatusNote,
        CoreKind.CertaintyAnswer => "certain." + plan.StatusNote,
        CoreKind.IncidentDetail => "detail." + plan.StatusNote,
        CoreKind.SightingPlace => "sightplace",
        CoreKind.ReasonAnswer => "reason." + plan.StatusNote,
        CoreKind.OpinionReason => "opinionq." + plan.StatusNote,
        CoreKind.ChallengeResponse => "challenge." + plan.StatusNote,
        _ => "noanomaly",
    };

    // --- 변수 ------------------------------------------------------------

    private static Dictionary<string, string> Vars(DialogueContext ctx, DialogueResponsePlan plan, DialogueVoiceDef voice)
    {
        string iroom = RoomName(plan.IncidentRoomId);
        return new Dictionary<string, string>
        {
            ["room"] = RoomName(string.IsNullOrEmpty(plan.RoomId) ? ctx.AssignedRoomId : plan.RoomId),
            ["iroom"] = iroom,
            ["sroom"] = plan.Core == CoreKind.SuspiciousSighting ? iroom : "",
            ["who"] = Codename(!string.IsNullOrEmpty(plan.SubjectEmployeeId)
                ? plan.SubjectEmployeeId
                : ctx.KnownSuspiciousActorId),
            ["target"] = Codename(plan.TargetEmployeeId),
            ["trait"] = TraitStem(plan.TargetEmployeeId),
            ["task"] = ctx.CurrentTaskName ?? "",
            ["mood"] = ctx.DailyMood ?? "",
            // 꼬리질문 답변에 등장하는 보조 대상.
            ["droom"] = RoomName(plan.DetailRoomId),
            ["dname"] = Codename(plan.DetailName),
            ["what"] = IncidentClause(ctx.EmployeeId, plan.IncidentType, KnowledgeLevel.Direct, voice),
            ["sound"] = IncidentClause(ctx.EmployeeId, plan.IncidentType, KnowledgeLevel.Indirect, voice),
            // 어간("기계가 멈추") — "…{what_stem}는 걸 봤죠~" 처럼 문장 안에 들어간다. 캐릭터 파일의
            // phrase.stem.<종류> 에만 있고, 없으면 빈 값이라 그 변수를 쓰는 틀은 뽑히지 않는다.
            ["what_stem"] = DialogueLineBank.Any(ctx.EmployeeId, $"phrase.stem.{plan.IncidentType}", voice.Formal),
        };
    }

    // 사건을 사람이 말하는 표현으로. 직접 목격이면 장면까지, 간접이면 감각까지만.
    // 동사 줄기는 대사 뱅크의 phrase.direct.<종류> / phrase.sound.<종류> 에 있다.
    private static string IncidentClause(string employeeId, LogEventType type, KnowledgeLevel k, DialogueVoiceDef voice)
    {
        string kind = k == KnowledgeLevel.Direct ? "direct" : "sound";
        string stem = DialogueLineBank.Any(employeeId, $"phrase.{kind}.{type}", voice.Formal);
        if (stem.Length == 0) stem = DialogueLineBank.Any(employeeId, $"phrase.{kind}.any", voice.Formal);
        return KoreanParticle.PastEnding(stem, voice.Formal);
    }

    // 인간관계 수치가 없으므로, 공개된 능력치에서만 평가를 만든다. 과거 관계를 창작하지 않는다.
    private static string TraitStem(string employeeId)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(employeeId);
        if (def == null) return "무난한 편";
        // 능력치가 잠긴 날에는 플레이어도 그 수치를 볼 수 없다 — 없는 정보로 평가하지 않는다.
        if (!DayFeatures.StatsEnabled) return "무난한 편";
        var options = new List<string>();
        if (def.Tech >= 3) options.Add("일 처리는 빠른 편");
        if (def.Tech <= 1) options.Add("일이 조금 더딘 편");
        if (def.Courage >= 3) options.Add("겁이 없는 편");
        if (def.Courage <= 1) options.Add("겁이 많은 편");
        if (def.Observation >= 3) options.Add("주변을 잘 보는 편");
        if (def.Observation <= 1) options.Add("주변을 잘 못 보는 편");
        if (options.Count == 0) options.Add("무난한 편");
        return options[(int)(GD.Randi() % (uint)options.Count)];
    }

    // 예전 호출부 호환 — 시각 환산은 DialogueClock 한 곳에서만 한다.
    internal static string ClockText(float seconds) => DialogueClock.Spoken(seconds);

    private static string RoomName(string roomId)
    {
        if (string.IsNullOrEmpty(roomId) || roomId == DialogueContextBuilder.PlayerOnlyRoomId) return "";
        return FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? "";
    }

    private static string Codename(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId)) return "";
        return FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename ?? "";
    }
}
