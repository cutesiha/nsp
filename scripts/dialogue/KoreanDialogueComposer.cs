using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 계획(DialogueResponsePlan)을 실제 한국어 문장으로 조립한다.
//
// 완성된 문장을 통째로 고르는 방식이 아니다. 대답은 다음 조각들의 부분집합이며,
// 어떤 조각을 몇 개 쓸지는 캐릭터의 DialogueVoiceProfile 이 정한다.
//
//   [반응] [핵심 답변] [보정] [보조 정보] [감정] [끝맺음]
//
// 사건 이름은 LogEntry.Description 을 쓰지 않고 EventType 에서 사람이 말하는 표현으로
// 다시 만든다. 간접 인지일 때는 감각(소리·진동·불빛) 표현만 쓰며 원인을 말하지 않는다.
public static class KoreanDialogueComposer
{
    private sealed class Part
    {
        public string Text = "";
        public int DropOrder;   // 길이 제한에 걸리면 큰 값부터 버린다
        public bool Keep;       // 사실 정확성에 필요한 조각 — 버리지 않는다
    }

    public static string Compose(DialogueContext ctx, DialogueResponsePlan plan)
    {
        string result = "";
        // 직전 대사와 완전히 같은 문장이 나오면 다시 조립한다.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            result = Build(ctx, plan);
            if (string.IsNullOrEmpty(result)) continue;
            if (!DialogueClaimState.WasRecent(ctx.EmployeeId, result)) break;
        }
        if (string.IsNullOrEmpty(result)) result = "…";
        DialogueClaimState.Remember(ctx.EmployeeId, result);
        return result;
    }

    private static string Build(DialogueContext ctx, DialogueResponsePlan plan)
    {
        var p = DialogueVoiceProfiles.Get(ctx.EmployeeId);
        string style = p.EmployeeId;
        var vars = Vars(ctx, plan, p);

        string core = Pick(style, CoreSlot(plan), vars);
        if (string.IsNullOrEmpty(core)) core = Pick(style, "noanomaly", vars);

        // 시간 표현은 기본적으로 붙이지 않는다. 붙이더라도 핵심 답변 앞 한 번뿐이다.
        string time = TimeWord(plan, p);
        if (!string.IsNullOrEmpty(time) && !HasTimeWord(core))
            core = time + " " + core;

        var parts = new List<Part> { new() { Text = core, Keep = true, DropOrder = 0 } };

        // 지시에 대한 대답과 통화 신고는 짧게 끝낸다.
        bool terse = plan.Core is CoreKind.DispatchAccept or CoreKind.DispatchDecline
            or CoreKind.IncidentReport;
        bool noCloser = terse;

        string opener = terse ? ""
            : plan.IsRepeat ? Pick(style, "opener.repeat", vars)
            : Roll(p.OpenerChance) ? Pick(style, "opener", vars) : "";
        // 핵심 답변이 같은 말로 시작하면("네." + "네. 자리를 …", "저, 네…" + "저, 저요…?")
        // 같은 말이 두 번 나오므로 반응을 뺀다.
        if (!string.IsNullOrEmpty(opener) && opener.Length >= 2 && core.StartsWith(opener[..2]))
            opener = "";

        // 사실 정확성 보정 — 간접 목격 / 원인 불명. 길이 제한과 무관하게 남는다.
        if (plan.NeedsIndirectCaveat)
            parts.Add(new Part { Text = Pick(style, "caveat.indirect", vars), Keep = true, DropOrder = 1 });
        if (plan.NeedsUnknownCauseCaveat)
            parts.Add(new Part { Text = Pick(style, "caveat.cause", vars), Keep = true, DropOrder = 1 });

        string support = SupportClause(ctx, plan, p, vars, core);
        string emotion = plan.Emotion != EmotionKind.None ? Pick(style, EmotionSlot(plan.Emotion), vars) : "";
        string closer = !noCloser && Roll(p.CloserChance) ? Pick(style, "closer", vars) : "";

        if (!string.IsNullOrEmpty(support)) parts.Add(new Part { Text = support, DropOrder = 2 });
        if (!string.IsNullOrEmpty(emotion)) parts.Add(new Part { Text = emotion, DropOrder = 3 });
        if (!string.IsNullOrEmpty(closer)) parts.Add(new Part { Text = closer, DropOrder = 4 });

        // 조각 수 제한 — 까마귀는 2개, 올빼미·토끼는 3개까지.
        int max = Mathf.Max(1, p.MaxParts - (string.IsNullOrEmpty(opener) ? 0 : 1));
        while (parts.Count > max)
        {
            var victim = parts.Where(x => !x.Keep).OrderByDescending(x => x.DropOrder).FirstOrDefault();
            if (victim == null) break;
            parts.Remove(victim);
        }

        var ordered = new List<string>();
        // 감정이 먼저 튀어나오는 캐릭터(토끼·해파리)는 반응을 핵심 답변 앞으로 뺀다.
        var emotionPart = parts.FirstOrDefault(x => x.Text == emotion && !string.IsNullOrEmpty(emotion));
        if (p.ReactionFirst && emotionPart != null)
        {
            parts.Remove(emotionPart);
            if (!string.IsNullOrEmpty(opener)) ordered.Add(opener);
            ordered.Add(emotionPart.Text);
        }
        else if (!string.IsNullOrEmpty(opener))
        {
            ordered.Add(opener);
        }
        ordered.AddRange(parts.Select(x => x.Text));

        return Finalize(string.Join(" ", ordered.Where(s => !string.IsNullOrWhiteSpace(s))));
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

    private static string EmotionSlot(EmotionKind e) => e switch
    {
        EmotionKind.Alarm => "emotion.alarm",
        EmotionKind.Fear => "emotion.fear",
        EmotionKind.Annoyance => "emotion.annoy",
        EmotionKind.Amused => "emotion.amused",
        _ => "emotion.composed",
    };

    // 보조 정보는 "무엇을 더 말할까"의 결과다. 방해자의 전략이 여기서 문장이 된다.
    private static string SupportClause(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceProfile p, Dictionary<string, string> vars, string core)
    {
        string style = p.EmployeeId;

        switch (plan.Deception)
        {
            case DeceptionMode.Justify when plan.Core == CoreKind.SelfLocation:
                return Pick(style, "support.justify", vars);
            case DeceptionMode.Minimize when plan.Core == CoreKind.SelfLocation:
                return Pick(style, "support.minimize", vars);
            // 추궁 대응 문장 자체가 이미 회피라 "기억이 안 난다"를 두 번 붙이지 않는다.
            case DeceptionMode.Vague when plan.Core != CoreKind.ChallengeResponse:
                return Pick(style, "support.vague", vars);
            case DeceptionMode.Redirect when !string.IsNullOrEmpty(vars["who"]):
                return Pick(style, "support.redirect", vars);
        }

        if (!plan.AllowSupport) return "";
        // 핵심 답변이 이미 "일하고 있었다"를 담고 있으면 같은 말을 두 번 하지 않는다.
        if (plan.MentionTask && plan.Core == CoreKind.SelfLocation && !MentionsWork(core))
            return Pick(style, "support.task", vars);
        if (plan.Core == CoreKind.Opinion && plan.AllowSupport && Roll(p.SupportChance))
            return Pick(style, "support.seen", vars);
        bool hedgeable = plan.Core is CoreKind.IncidentDirect or CoreKind.IncidentIndirect
            or CoreKind.SuspiciousSighting or CoreKind.Opinion;
        bool alreadyHedged = plan.NeedsIndirectCaveat || plan.NeedsUnknownCauseCaveat;
        if (hedgeable && !alreadyHedged && (plan.Certainty == Certainty.Low || Roll(p.HedgeChance)))
        {
            string hedge = Pick(style, "support.hedge", vars);
            if (!string.IsNullOrEmpty(hedge)) return hedge;
        }
        if (plan.Core is CoreKind.IncidentDirect or CoreKind.IncidentIndirect && Roll(p.SupportChance))
            return Pick(style, "support.nothing", vars);
        if (plan.Core == CoreKind.StatusReport && plan.MentionTask && !string.IsNullOrEmpty(vars["task"]))
            return Pick(style, "support.taskname", vars);
        return "";
    }

    // --- 변수 ------------------------------------------------------------

    private static readonly string[] WorkWords = { "일 하", "일하", "근무", "업무", "작업" };

    private static bool MentionsWork(string text) => WorkWords.Any(text.Contains);

    private static Dictionary<string, string> Vars(DialogueContext ctx, DialogueResponsePlan plan,
        DialogueVoiceProfile p)
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
            // 꼬리질문 답변에 등장하는 보조 대상.
            ["droom"] = RoomName(plan.DetailRoomId),
            ["dname"] = Codename(plan.DetailName),
            ["what"] = IncidentClause(plan.IncidentType, KnowledgeLevel.Direct, p),
            ["sound"] = IncidentClause(plan.IncidentType, KnowledgeLevel.Indirect, p),
        };
    }

    // 사건을 사람이 말하는 표현으로. 직접 목격이면 원인/장면까지, 간접이면 감각까지만.
    private static string IncidentClause(LogEventType type, KnowledgeLevel k, DialogueVoiceProfile p)
    {
        string[] stems = k == KnowledgeLevel.Direct ? DirectStems(type) : IndirectStems(type);
        return Choose(stems) + p.PastEnding + ".";
    }

    private static string[] DirectStems(LogEventType type) => type switch
    {
        LogEventType.TaskFailed => new[] { "설비가 멈췄", "기계가 갑자기 섰", "장비 하나가 나갔" },
        LogEventType.PowerOutage => new[] { "전기가 나갔", "불이 전부 꺼졌", "전력이 끊겼" },
        LogEventType.CctvDisconnect => new[] { "화면이 끊겼", "감시 화면이 나갔" },
        LogEventType.Sabotage => new[] { "장비가 누가 건드린 것처럼 어긋나 있었", "작업 기록이 이상하게 밀려 있었" },
        LogEventType.TabooViolation => new[] { "설명하기 어려운 일이 있었", "그때 뭔가 나타났" },
        LogEventType.Death => new[] { "사람이 쓰러져 있었" },
        _ => new[] { "이상한 일이 있었" },
    };

    private static string[] IndirectStems(LogEventType type) => type switch
    {
        LogEventType.Death => new[] { "비명 같은 게 들렸", "사람 소리가 한 번 크게 났" },
        LogEventType.PowerOutage => new[] { "불이 한 번 크게 깜빡였", "그쪽 조명이 꺼지는 게 보였", "큰 소리가 났" },
        _ => new[] { "큰 소리가 났", "쿵 하는 소리가 들렸", "뭔가 부서지는 소리가 났", "진동이 느껴졌" },
    };

    // 인간관계 수치가 없으므로, 공개된 능력치에서만 평가를 만든다. 과거 관계를 창작하지 않는다.
    private static string TraitStem(string employeeId)
    {
        var def = FacilitySimulation.Instance?.GetEmployeeDef(employeeId);
        if (def == null) return "무난한 편";
        var options = new List<string>();
        if (def.Tech >= 3) options.Add("일 처리는 빠른 편");
        if (def.Tech <= 1) options.Add("일이 조금 더딘 편");
        if (def.Courage >= 3) options.Add("겁이 없는 편");
        if (def.Courage <= 1) options.Add("겁이 많은 편");
        if (def.Observation >= 3) options.Add("주변을 잘 보는 편");
        if (def.Observation <= 1) options.Add("주변을 잘 못 보는 편");
        if (options.Count == 0) options.Add("무난한 편");
        return Choose(options.ToArray());
    }

    private static string TimeWord(DialogueResponsePlan plan, DialogueVoiceProfile p) => plan.Time switch
    {
        TimeRef.Vague => Choose(new[] { "그때", "아까", "조금 전", "그쯤" }),
        TimeRef.Exact => ClockText(plan.IncidentTimeSeconds) + (p.Register == SpeechRegister.Formal ? "경" : "쯤"),
        _ => "",
    };

    // 이미 시점을 품고 있는 문장에는 시간 표현을 덧대지 않는다("아까 계속 …" 방지).
    private static bool HasTimeWord(string s) =>
        s.Contains("그때") || s.Contains("아까") || s.Contains("조금 전") || s.Contains("그쯤")
        || s.StartsWith("계속");

    // 근무 시계(0초 = 22:00)를 실제 시각 표기로.
    private static string ClockText(float seconds)
    {
        float length = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        int totalMinutes = 22 * 60 + Mathf.FloorToInt(seconds * (360f / Mathf.Max(1f, length)));
        int hour = (totalMinutes / 60) % 24;
        int minute = totalMinutes % 60;
        return minute == 0 ? $"{hour}시" : $"{hour}시 {minute}분";
    }

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

    // --- 조각 선택 -------------------------------------------------------

    private static bool Roll(float chance) => GD.Randf() < chance;

    private static string Choose(string[] pool) =>
        pool == null || pool.Length == 0 ? "" : pool[(int)(GD.Randi() % (uint)pool.Length)];

    // 값이 비어 있는 변수를 요구하는 문장은 후보에서 제외한다 —
    // "{who} 씨를 봤어요" 가 "씨를 봤어요" 로 새어 나가지 않게 하는 장치.
    private static string Pick(string style, string slot, Dictionary<string, string> vars)
    {
        var pool = Pools.GetValueOrDefault($"{style}|{slot}") ?? Pools.GetValueOrDefault($"any|{slot}");
        if (pool == null || pool.Length == 0) return "";

        var usable = pool.Where(t => Tokens(t).All(k => vars.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v))).ToArray();
        if (usable.Length == 0) return "";

        string text = Choose(usable);
        foreach (var kv in vars) text = text.Replace("{" + kv.Key + "}", kv.Value);
        return text;
    }

    private static readonly Regex TokenPattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private static IEnumerable<string> Tokens(string text)
    {
        foreach (Match m in TokenPattern.Matches(text)) yield return m.Groups[1].Value;
    }

    // 조사 정리 + 남은 자리표시자 제거 + 공백 정리. 최종 출력은 반드시 여기를 통과한다.
    private static string Finalize(string text)
    {
        text = KoreanParticle.Resolve(text);
        text = TokenPattern.Replace(text, "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        text = Regex.Replace(text, @"\s+([.,!?…])", "$1");
        if (text.Length > 0 && !".!?…~".Contains(text[^1])) text += ".";
        return text;
    }

    // ===================================================================
    // 캐릭터별 문장 조각 풀.
    // 여기 있는 것은 "완성된 대답"이 아니라 대답을 구성하는 한 조각이다.
    // ===================================================================
    private static readonly Dictionary<string, string[]> Pools = new()
    {
        // ── 핵심: 사건 당시 내 위치 ────────────────────────────────────
        ["owl|selfloc"] = new[] { "{room}에 있었습니다.", "{room}에서 근무 중이었습니다.", "{room}입니다. 배치받은 자리 그대로였습니다." },
        ["cat|selfloc"] = new[] { "{room}이요/요.", "{room}에 있었어요.", "{room}이요/요. 계속 거기 있었고요." },
        ["jellyfish|selfloc"] = new[] { "{room}에 있었어요.", "{room}이요/요... 거기 있었어요.", "저는 {room}에 있었어요." },
        ["rabbit|selfloc"] = new[] { "{room}이었/였어요!", "{room}에 있었어요!", "{room}에서 일하고 있었어요!" },
        ["crow|selfloc"] = new[] { "{room}에 있었습니다.", "{room}입니다.", "계속 {room}에 있었습니다." },
        ["fox|selfloc"] = new[] { "{room}에 있었죠.", "{room}이요/요. 거기 있었어요.", "{room}에서 제 일 하고 있었어요." },

        // ── 핵심: 직접 본 사건 ─────────────────────────────────────────
        ["owl|incident.direct"] = new[] { "{iroom}에서 {what}", "{iroom} 쪽입니다. {what}" },
        ["cat|incident.direct"] = new[] { "{iroom}에서 {what}", "{iroom}이요/요. {what}" },
        ["jellyfish|incident.direct"] = new[] { "{iroom}에서 {what}", "{iroom}에서요... {what}" },
        ["rabbit|incident.direct"] = new[] { "{iroom}에서 {what}", "{iroom}이요/요! {what}" },
        ["crow|incident.direct"] = new[] { "{iroom}에서 {what}", "{iroom}입니다. {what}" },
        ["fox|incident.direct"] = new[] { "{iroom} 쪽에서 {what}", "{iroom}에서요. {what}" },

        // ── 핵심: 벽 너머로 알게 된 사건(감각만) ────────────────────────
        ["owl|incident.indirect"] = new[] { "{iroom} 쪽에서 {sound}", "옆 작업실에 있었습니다만, {iroom} 쪽에서 {sound}" },
        ["cat|incident.indirect"] = new[] { "{iroom} 쪽에서 {sound}", "{iroom} 쪽이요/요. {sound}" },
        ["jellyfish|incident.indirect"] = new[] { "{iroom} 쪽에서 {sound}", "저, {iroom} 쪽에서요... {sound}" },
        ["rabbit|incident.indirect"] = new[] { "{iroom} 쪽에서 {sound}", "{iroom} 쪽이요/요! {sound}" },
        ["crow|incident.indirect"] = new[] { "{iroom} 쪽에서 {sound}", "{iroom} 방향이었습니다. {sound}" },
        ["fox|incident.indirect"] = new[] { "{iroom} 쪽에서 {sound}", "{iroom} 쪽이었/였어요. {sound}" },

        // ── 핵심: 아는 이상 없음 ───────────────────────────────────────
        ["owl|noanomaly"] = new[] { "특별한 건 없었습니다.", "제가 있던 곳에서는 이상을 확인하지 못했습니다." },
        ["cat|noanomaly"] = new[] { "없었어요.", "제가 본 건 없어요.", "별일 없었어요." },
        ["jellyfish|noanomaly"] = new[] { "저는... 특별한 건 못 봤어요.", "아뇨, 아무것도 못 봤어요." },
        ["rabbit|noanomaly"] = new[] { "없었어요!", "저는 못 봤어요. 있었으면 바로 말씀드렸을 거예요!" },
        ["crow|noanomaly"] = new[] { "없습니다.", "확인한 이상은 없습니다." },
        ["fox|noanomaly"] = new[] { "글쎄요. 제가 있던 쪽은 조용했어요.", "딱히 없었는데요." },

        // ── 핵심: 실제로 목격한 다른 직원 ───────────────────────────────
        ["owl|sight"] = new[] { "{who} 직원의 행동이 평소와 달랐습니다.", "{sroom}에서 {who} 직원이 이상하게 움직이는 걸 봤습니다." },
        ["cat|sight"] = new[] { "{who} 씨요. 행동이 좀 이상했어요.", "{sroom}에서 {who} 씨가 뭔가 하고 있던데요." },
        ["jellyfish|sight"] = new[] { "{who} 씨가... 조금 이상해 보였어요.", "{sroom}에서 {who} 씨를 봤는데요, 평소랑 달랐어요." },
        ["rabbit|sight"] = new[] { "{who} 씨요! 좀 이상했어요.", "{sroom}에서 {who} 씨가 뭔가 하고 있었어요!" },
        ["crow|sight"] = new[] { "{who}. 행동이 비정상이었습니다.", "{sroom}에서 {who} 직원을 봤습니다." },
        ["fox|sight"] = new[] { "{who} 씨가 좀 재미있는 걸 하고 있던데요.", "{sroom}에서 {who} 씨를 봤어요." },

        // ── 핵심: 목격 없음 ────────────────────────────────────────────
        ["owl|nosight"] = new[] { "확인되지 않은 사람을 지목할 생각은 없습니다.", "그런 장면은 보지 못했습니다." },
        ["cat|nosight"] = new[] { "못 봤어요.", "없어요. 아무나 찍고 싶진 않은데요." },
        ["jellyfish|nosight"] = new[] { "아뇨... 다른 분을 볼 여유가 없었어요.", "저는 못 봤어요." },
        ["rabbit|nosight"] = new[] { "아뇨! 제가 본 사람 중엔 없었어요.", "못 봤어요!" },
        ["crow|nosight"] = new[] { "목격하지 못했습니다.", "없습니다." },
        ["fox|nosight"] = new[] { "딱히요. 애매한 걸로 사람 몰아가긴 싫어서요.", "본 건 없는데요." },

        // ── 핵심: 다른 직원 평가 ───────────────────────────────────────
        ["owl|opinion"] = new[] { "{target} 직원은 {trait}입니다.", "{target} 직원에 대해서는 {trait}이라고 봅니다." },
        ["cat|opinion"] = new[] { "{target} 씨요? {trait}이죠.", "{trait}이에요. 그 정도요." },
        ["jellyfish|opinion"] = new[] { "{target} 씨는... {trait}인 것 같아요.", "잘은 모르겠지만 {trait}인 것 같아요." },
        ["rabbit|opinion"] = new[] { "{target} 씨요? {trait}인 것 같아요!", "{trait}이에요! 저는 괜찮다고 생각해요." },
        ["crow|opinion"] = new[] { "{trait}입니다.", "{target}. {trait}입니다." },
        ["fox|opinion"] = new[] { "{target} 씨요? {trait}이죠.", "{trait}이라고 해두죠." },

        // ── 핵심: 의심에 대한 대응(증거 없음) ───────────────────────────
        ["owl|deny"] = new[] { "저는 아닙니다. 기록부터 확인해주십시오.", "그렇게 보실 수는 있습니다. 다만 근거를 함께 봐주십시오." },
        ["cat|deny"] = new[] { "저 아니에요.", "저 아니에요. 시간 낭비하지 마세요." },
        ["jellyfish|deny"] = new[] { "저, 저요...? 아니에요.", "제가요? 정말 아무것도 안 했어요." },
        ["rabbit|deny"] = new[] { "네?! 저 아니에요!", "저 진짜 아니에요. 확인해보시면 아실 거예요!" },
        ["crow|deny"] = new[] { "아닙니다.", "아닙니다. 기록을 확인하십시오." },
        ["fox|deny"] = new[] { "저를요? 어떤 근거인지부터 듣고 싶은데요.", "그럴 수도 있죠. 근데 저는 아니에요." },

        // ── 핵심: 의심에 대한 대응(실제 기록이 있을 때) ──────────────────
        ["owl|deny.evidence"] = new[] { "그 기록이 저를 가리키는 건 압니다. 제가 한 일은 아닙니다.", "제 동선이 이상하게 보였다면 설명드리겠습니다." },
        ["cat|deny.evidence"] = new[] { "그거 때문이죠? 그건 설명할 수 있어요.", "기록만 보면 그렇게 보이겠네요. 그래도 저 아니에요." },
        ["jellyfish|deny.evidence"] = new[] { "그, 그게... 보이신 것처럼은 아니에요.", "제가 거기 있었던 건 맞는데요... 그건 아니에요." },
        ["rabbit|deny.evidence"] = new[] { "아, 그거 보셨구나. 근데 그거 오해예요!", "제가 움직인 건 맞아요! 근데 그런 건 아니에요." },
        ["crow|deny.evidence"] = new[] { "그 기록은 압니다. 제가 한 일은 아닙니다.", "동선은 인정합니다. 나머지는 아닙니다." },
        ["fox|deny.evidence"] = new[] { "그 기록 말씀이시죠? 보이는 것만큼 단순하진 않아요.", "제가 좀 눈에 띄었나 보네요. 그래도 아닙니다." },

        // ── 핵심: 지금 근무 상태 ───────────────────────────────────────
        ["owl|status.ok"] = new[] { "지금까지는 문제 없습니다.", "예정대로 진행 중입니다." },
        ["cat|status.ok"] = new[] { "별문제 없어요.", "잘 되고 있어요." },
        ["jellyfish|status.ok"] = new[] { "지금은... 괜찮아요.", "네, 하고 있어요." },
        ["rabbit|status.ok"] = new[] { "잘 되고 있어요!", "네! 순조로워요!" },
        ["crow|status.ok"] = new[] { "정상 진행 중입니다.", "특이사항 없습니다." },
        ["fox|status.ok"] = new[] { "순조롭습니다.", "걱정하실 정도는 아니에요." },

        ["owl|status.blocked"] = new[] { "자재가 없어 작업이 멈춰 있습니다.", "자재부터 채워주셔야 진행이 됩니다." },
        ["cat|status.blocked"] = new[] { "자재가 없어서 손 놓고 있어요.", "자재요. 없으면 못 해요." },
        ["jellyfish|status.blocked"] = new[] { "저, 자재가 떨어져서... 못 하고 있어요.", "자재가 없어요... 어떡하죠?" },
        ["rabbit|status.blocked"] = new[] { "자재가 없어요! 이거 어떡하죠?", "자재가 다 떨어졌어요!" },
        ["crow|status.blocked"] = new[] { "자재 부족. 작업 중단 상태입니다.", "자재가 없습니다." },
        ["fox|status.blocked"] = new[] { "자재가 비었네요. 이건 제 능력 밖인데요.", "자재가 없어서 쉬는 중이에요." },

        ["owl|status.repair"] = new[] { "고장 난 설비를 수리하고 있습니다. 시간이 걸립니다.", "복구 작업 중입니다." },
        ["cat|status.repair"] = new[] { "고장부터 잡고 있어요. 원래 일은 다 밀렸고요.", "수리 중이에요." },
        ["jellyfish|status.repair"] = new[] { "여기 고장이 나서요... 고치고는 있어요.", "고치는 중인데... 잘 될지 모르겠어요." },
        ["rabbit|status.repair"] = new[] { "여기 망가졌어요! 지금 고치는 중이에요!", "수리 중이에요!" },
        ["crow|status.repair"] = new[] { "설비 수리 중입니다.", "복구 작업 중입니다." },
        ["fox|status.repair"] = new[] { "고장 수습 중이에요. 원래 일정은 잊으시는 게 좋겠는데요.", "수리 중입니다." },

        ["owl|status.stress"] = new[] { "솔직히 말씀드리면 상태가 좋지는 않습니다.", "버티고는 있습니다만 여유가 없습니다." },
        ["cat|status.stress"] = new[] { "솔직히 좀 힘들어요. 그래도 하고는 있어요.", "상태 안 좋아요." },
        ["jellyfish|status.stress"] = new[] { "저... 사실 좀 힘들어요. 손이 계속 떨려서요.", "괜찮다고 말하고 싶은데... 잘 모르겠어요." },
        ["rabbit|status.stress"] = new[] { "좀... 힘들긴 해요. 그래도 할 수 있어요!", "솔직히 좀 지쳤어요." },
        ["crow|status.stress"] = new[] { "상태는 좋지 않습니다. 작업은 계속합니다.", "여유는 없습니다." },
        ["fox|status.stress"] = new[] { "썩 좋진 않네요. 이런 말 하는 것도 오랜만인데요.", "생각보다 힘드네요." },

        ["owl|status.idle"] = new[] { "현재 배정된 작업은 없습니다.", "지금은 대기 중입니다." },
        ["cat|status.idle"] = new[] { "지금 할 일 없는데요.", "일이 없어요." },
        ["jellyfish|status.idle"] = new[] { "지금은... 딱히 할 게 없어요.", "할 일이 없어서 기다리고 있어요." },
        ["rabbit|status.idle"] = new[] { "지금 할 일이 없어요! 뭐 시키실 거 있어요?", "대기 중이에요!" },
        ["crow|status.idle"] = new[] { "대기 중입니다.", "배정된 작업 없습니다." },
        ["fox|status.idle"] = new[] { "지금은 노는 중이죠. 뭐 주실 거라도?", "한가한데요." },

        ["owl|status.moving"] = new[] { "지금 이동 중입니다. 도착하면 바로 시작하겠습니다.", "이동 중입니다." },
        ["cat|status.moving"] = new[] { "이동 중이에요. 좀 기다리세요.", "가고 있어요." },
        ["jellyfish|status.moving"] = new[] { "저, 지금 가는 중이에요...", "이동 중이에요..." },
        ["rabbit|status.moving"] = new[] { "지금 가고 있어요!", "이동 중이에요! 금방 도착해요!" },
        ["crow|status.moving"] = new[] { "이동 중입니다.", "이동 중. 곧 도착합니다." },
        ["fox|status.moving"] = new[] { "가는 중이에요. 재촉은 안 하셔도 돼요.", "이동 중입니다." },

        // ── 핵심: 지시 수용 ────────────────────────────────────────────
        ["owl|comply"] = new[] { "알겠습니다. 지연되지 않도록 하겠습니다.", "네. 더 신경 쓰겠습니다." },
        ["cat|comply"] = new[] { "말 안 하셔도 하고 있는데요.", "네, 네. 알겠어요." },
        ["jellyfish|comply"] = new[] { "죄, 죄송해요. 더 집중할게요...", "네... 신경 쓸게요." },
        ["rabbit|comply"] = new[] { "앗, 네! 제대로 할게요!", "알겠어요! 딴짓 안 할게요!" },
        ["crow|comply"] = new[] { "알겠습니다.", "그렇게 하겠습니다." },
        ["fox|comply"] = new[] { "네, 네. 성실하게 해야겠네요.", "알겠습니다. 찍히기 전에 해야죠." },

        // ── 핵심: 수신 전화 첫 대사(사고 신고) ───────────────────────────
        ["owl|report.direct"] = new[] { "관리자님, {iroom}에서 {what} 제가 확인하러 가도 괜찮겠습니까?", "관리자님, {iroom} 상황을 보고드립니다. {what} 지시 부탁드립니다." },
        ["owl|report.indirect"] = new[] { "관리자님, {iroom} 쪽에서 {sound} 확인하러 가도 괜찮겠습니까?", "{iroom} 방향에서 {sound} 어떻게 할까요?" },
        ["cat|report.direct"] = new[] { "{iroom}에서 {what} 제가 가는 게 제일 빠를 텐데요. 갈까요?", "{iroom} 고장이에요. {what} 어떻게 할까요?" },
        ["cat|report.indirect"] = new[] { "{iroom} 쪽에서 {sound} 가볼까요?", "{iroom} 쪽이요. {sound} 확인 필요할 것 같은데요." },
        ["jellyfish|report.direct"] = new[] { "저, 저기... {iroom}에서 {what} 괜찮은 거 맞죠?", "관리자님...! {iroom}에서 {what} 어, 어떡하죠?" },
        ["jellyfish|report.indirect"] = new[] { "저, 저기... {iroom} 쪽에서 {sound} 괜찮은 거 맞죠?", "{iroom} 쪽에서 {sound} 제가 가야 하는 건 아니죠...?" },
        ["rabbit|report.direct"] = new[] { "관리자님! {iroom}에서 {what} 저 가볼까요?!", "{iroom}이요! {what} 이거 큰일 아니에요?" },
        ["rabbit|report.indirect"] = new[] { "관리자님! {iroom} 쪽에서 {sound} 저 가볼까요?!", "{iroom} 쪽에서 {sound} 확인해볼까요?" },
        ["crow|report.direct"] = new[] { "{iroom}에서 {what} 지시 바랍니다.", "{iroom}. {what} 확인이 필요합니다." },
        ["crow|report.indirect"] = new[] { "{iroom} 쪽에서 {sound} 확인이 필요합니다.", "{iroom} 방향이었습니다. {sound} 지시 바랍니다." },
        ["fox|report.direct"] = new[] { "{iroom}에서 {what} 제가 한번 가볼까요?", "{iroom} 쪽이 좀 시끄럽네요. {what} 어떻게 할까요?" },
        ["fox|report.indirect"] = new[] { "{iroom} 쪽에서 {sound} 제가 한번 가볼까요?", "{iroom} 쪽에서 {sound} 확인해드릴까요?" },

        // ── 핵심: 정전 신고(전 직원이 직접 겪는다) ───────────────────────
        ["owl|report.blackout"] = new[] { "관리자님, 전력이 끊겼습니다. 함부로 움직이지 않고 지시를 기다리겠습니다.", "정전입니다. 비상등은 작동 중입니다만 지시가 필요합니다." },
        ["cat|report.blackout"] = new[] { "정전이에요. 제 위치는 확인되죠? 필요하면 바로 보내세요.", "불 나갔어요. 이대로 손 놓고 있을까요?" },
        ["jellyfish|report.blackout"] = new[] { "관리자님...? 여기 너무 어두워요... 저 가만히 있을까요?", "저, 아무것도 안 보여요... 어떡하죠?" },
        ["rabbit|report.blackout"] = new[] { "조명이 다 꺼졌어요! 관리자님, 저 여기 있어요!", "여보세요?! 완전 깜깜해요! 저 어떡해요?" },
        ["crow|report.blackout"] = new[] { "정전 확인. 현재 위치 유지하겠습니다.", "전력 차단. 지시 바랍니다." },
        ["fox|report.blackout"] = new[] { "완전히 깜깜해졌네요. 지시 주시면 움직이죠.", "이런. 불이 다 나갔는데요. 어떻게 할까요?" },

        // ── 핵심: 지시에 대한 대답 ──────────────────────────────────────
        ["owl|accept"] = new[] { "알겠습니다. 정리하고 바로 이동하겠습니다.", "확인하고 다시 보고드리겠습니다." },
        ["owl|decline"] = new[] { "알겠습니다. 상황이 바뀌면 다시 보고드리겠습니다.", "네. 자리를 지키겠습니다." },
        ["cat|accept"] = new[] { "알겠어요. 빨리 끝내고 오죠.", "가죠." },
        ["cat|decline"] = new[] { "그럼 그대로 두죠. 나중에 더 커질 텐데요.", "네. 알겠어요." },
        ["jellyfish|accept"] = new[] { "제, 제가요...? 네... 빨리 보고 올게요.", "네... 다녀올게요." },
        ["jellyfish|decline"] = new[] { "네...! 다행이에요...", "네, 알겠어요. 여기 있을게요." },
        ["rabbit|accept"] = new[] { "네! 바로 가볼게요!", "알겠어요! 문제 있으면 또 전화할게요!" },
        ["rabbit|decline"] = new[] { "아... 네. 근데 진짜 괜찮은 거죠?", "알겠어요! 여기 있을게요!" },
        ["crow|accept"] = new[] { "확인하겠습니다.", "이동합니다." },
        ["crow|decline"] = new[] { "알겠습니다. 대기합니다.", "알겠습니다." },
        ["fox|accept"] = new[] { "네~ 다녀올게요. 이런 건 익숙해서.", "알겠어요. 금방 보고 오죠." },
        ["fox|decline"] = new[] { "알겠습니다. 저야 편하죠.", "네. 그럼 여기 있을게요." },

        // ── 반응(문장 앞) ──────────────────────────────────────────────
        ["owl|opener"] = new[] { "네.", "확인했습니다." },
        ["cat|opener"] = new[] { "그거요?", "네." },
        ["jellyfish|opener"] = new[] { "아...", "저, 네...", "네, 네..." },
        ["rabbit|opener"] = new[] { "아, 네!", "어...!" },
        ["crow|opener"] = new[] { "네." },
        ["fox|opener"] = new[] { "아~", "네에." },

        ["owl|opener.repeat"] = new[] { "말씀드린 대로입니다.", "다시 말씀드리면," },
        ["cat|opener.repeat"] = new[] { "아까 말했잖아요.", "또요?" },
        ["jellyfish|opener.repeat"] = new[] { "네... 아까 말씀드린 것처럼요.", "저, 아까랑 같은데요..." },
        ["rabbit|opener.repeat"] = new[] { "네! 아까 말한 그대로예요.", "아까도 말했는데요!" },
        ["crow|opener.repeat"] = new[] { "말씀드린 대로입니다.", "같습니다." },
        ["fox|opener.repeat"] = new[] { "아까랑 같은 답인데요.", "다시 여쭤보시네요." },

        // ── 보정: 직접 보지 않았음 ──────────────────────────────────────
        ["owl|caveat.indirect"] = new[] { "직접 보지는 못했습니다.", "제가 확인한 범위는 거기까지입니다." },
        ["cat|caveat.indirect"] = new[] { "직접 본 건 아니에요." },
        ["jellyfish|caveat.indirect"] = new[] { "직접 본 건 아니에요... 제가 잘못 들은 걸 수도 있고요.", "직접 보진 못했어요..." },
        ["rabbit|caveat.indirect"] = new[] { "직접 본 건 아니에요!", "보진 못했어요!" },
        ["crow|caveat.indirect"] = new[] { "직접 보진 못했습니다." },
        ["fox|caveat.indirect"] = new[] { "직접 본 건 아니고요.", "본 건 아니고 들은 거예요." },

        // ── 보정: 원인은 모름 ───────────────────────────────────────────
        ["owl|caveat.cause"] = new[] { "원인까지는 확인하지 못했습니다." },
        ["cat|caveat.cause"] = new[] { "원인은 저도 몰라요." },
        ["jellyfish|caveat.cause"] = new[] { "왜 그랬는지는... 잘 모르겠어요." },
        ["rabbit|caveat.cause"] = new[] { "왜 그런지는 모르겠어요!" },
        ["crow|caveat.cause"] = new[] { "원인은 불명입니다." },
        ["fox|caveat.cause"] = new[] { "원인까지 아는 건 아니에요." },

        // ── 보조: 업무 ─────────────────────────────────────────────────
        ["owl|support.task"] = new[] { "맡은 업무를 계속하고 있었습니다." },
        ["cat|support.task"] = new[] { "배치받은 일 하고 있었고요." },
        ["jellyfish|support.task"] = new[] { "하던 일 하고 있었어요." },
        ["rabbit|support.task"] = new[] { "열심히 하고 있었어요!" },
        ["crow|support.task"] = new[] { "업무 중이었습니다." },
        ["fox|support.task"] = new[] { "제 일 하고 있었죠." },

        ["owl|support.taskname"] = new[] { "{task} 진행 중입니다." },
        ["cat|support.taskname"] = new[] { "{task} 하고 있어요." },
        ["jellyfish|support.taskname"] = new[] { "{task} 하는 중이에요..." },
        ["rabbit|support.taskname"] = new[] { "{task} 하고 있어요!" },
        ["crow|support.taskname"] = new[] { "{task} 진행 중." },
        ["fox|support.taskname"] = new[] { "{task} 중이에요." },

        // ── 보조: 그 밖엔 없음 ──────────────────────────────────────────
        ["owl|support.nothing"] = new[] { "그 밖에는 특별한 것이 없었습니다." },
        ["cat|support.nothing"] = new[] { "그거 말곤 없어요." },
        ["jellyfish|support.nothing"] = new[] { "그거 말고는... 없었던 것 같아요." },
        ["rabbit|support.nothing"] = new[] { "그거 말고는 없었어요!" },
        ["crow|support.nothing"] = new[] { "그 외에는 없습니다." },
        ["fox|support.nothing"] = new[] { "그거 말곤 조용했어요." },

        // ── 보조: 단서 붙이기 ───────────────────────────────────────────
        ["owl|support.hedge"] = new[] { "확실하지 않은 부분은 말씀드리지 않겠습니다." },
        ["jellyfish|support.hedge"] = new[] { "제가 잘못 본 걸 수도 있어요.", "확실하진 않아요..." },
        ["rabbit|support.hedge"] = new[] { "제 생각엔 그래요!" },
        ["fox|support.hedge"] = new[] { "제가 보고 있던 범위 안에서만요." },

        // ── 보조: 방해자 전략 ───────────────────────────────────────────
        ["owl|support.justify"] = new[] { "확인이 필요해 잠시 자리를 옮겼습니다." },
        ["cat|support.justify"] = new[] { "필요해서 잠깐 움직인 거예요." },
        ["jellyfish|support.justify"] = new[] { "잠깐... 확인할 게 있어서 갔어요." },
        ["rabbit|support.justify"] = new[] { "확인할 게 있어서 잠깐 갔었어요!" },
        ["crow|support.justify"] = new[] { "확인 목적이었습니다." },
        ["fox|support.justify"] = new[] { "잠깐 확인할 게 있었거든요." },

        ["owl|support.minimize"] = new[] { "길지 않은 시간이었습니다." },
        ["cat|support.minimize"] = new[] { "잠깐이었어요." },
        ["jellyfish|support.minimize"] = new[] { "아주 잠깐이었어요..." },
        ["rabbit|support.minimize"] = new[] { "진짜 잠깐이었어요!" },
        ["crow|support.minimize"] = new[] { "짧았습니다." },
        ["fox|support.minimize"] = new[] { "얼마 안 걸렸어요." },

        ["owl|support.vague"] = new[] { "정확한 시간까지는 기록해두지 않았습니다." },
        ["cat|support.vague"] = new[] { "일일이 다 기억 안 나는데요." },
        ["jellyfish|support.vague"] = new[] { "중간에 뭘 했는지는... 잘 기억이 안 나요." },
        ["rabbit|support.vague"] = new[] { "정확히는 기억이 안 나요!" },
        ["crow|support.vague"] = new[] { "세부는 기억나지 않습니다." },
        ["fox|support.vague"] = new[] { "세세한 건 기억이 잘 안 나네요." },

        ["owl|support.redirect"] = new[] { "다만 {who} 직원의 동선은 확인해보실 만합니다." },
        ["cat|support.redirect"] = new[] { "{who} 씨 쪽이나 보시죠." },
        ["jellyfish|support.redirect"] = new[] { "그, {who} 씨는... 좀 이상했어요." },
        ["rabbit|support.redirect"] = new[] { "{who} 씨는 좀 이상했어요!" },
        ["crow|support.redirect"] = new[] { "{who} 쪽을 확인하십시오." },
        ["fox|support.redirect"] = new[] { "{who} 씨가 뭘 했는지는 본인한테 물어보시는 게 빠를 텐데요." },

        // ── 보조: 오늘 마주친 적 있음(Q4) ───────────────────────────────
        ["owl|support.seen"] = new[] { "오늘도 같은 구역에서 마주쳤습니다." },
        ["cat|support.seen"] = new[] { "오늘 잠깐 같이 있었고요." },
        ["jellyfish|support.seen"] = new[] { "오늘 잠깐 봤어요." },
        ["rabbit|support.seen"] = new[] { "오늘도 봤어요!" },
        ["crow|support.seen"] = new[] { "오늘 마주쳤습니다." },
        ["fox|support.seen"] = new[] { "오늘도 얼굴은 봤죠." },

        // ── 감정 ───────────────────────────────────────────────────────
        ["owl|emotion.alarm"] = new[] { "가볍게 볼 상황은 아니었습니다." },
        ["cat|emotion.alarm"] = new[] { "정상은 아니었죠." },
        ["jellyfish|emotion.alarm"] = new[] { "너무 놀랐어요." },
        ["rabbit|emotion.alarm"] = new[] { "진짜 깜짝 놀랐어요!" },
        ["fox|emotion.alarm"] = new[] { "쉽게 잊을 장면은 아니던데요." },

        ["owl|emotion.fear"] = new[] { "솔직히 편치는 않았습니다." },
        ["cat|emotion.fear"] = new[] { "기분 나빴어요." },
        ["jellyfish|emotion.fear"] = new[] { "아직도 좀 무서워요..." },
        ["rabbit|emotion.fear"] = new[] { "좀 무서웠어요..." },
        ["fox|emotion.fear"] = new[] { "저도 사람인데 좀 그렇더군요." },

        ["cat|emotion.annoy"] = new[] { "이런 걸 왜 저한테 물으시는지 모르겠지만요." },
        ["fox|emotion.amused"] = new[] { "재미있는 밤이네요." },
        ["owl|emotion.composed"] = new[] { "보고는 그때 드렸습니다." },
        ["crow|emotion.composed"] = new[] { "그게 전부입니다." },

        // 꼬리질문 답변 ─────────────────────────────────────────────
        // 그 전에는 어디에 있었는가
        ["owl|prevloc.same"] = new[] { "계속 {room}에 있었습니다.", "이동 없이 {room}에 있었습니다." },
        ["cat|prevloc.same"] = new[] { "계속 {room}이요/요.", "{room}에 계속 있었어요." },
        ["jellyfish|prevloc.same"] = new[] { "쭉 {room}에 있었어요...", "{room}에서 안 나갔어요." },
        ["rabbit|prevloc.same"] = new[] { "계속 {room}에 있었어요!", "{room}에서 안 움직였어요!" },
        ["crow|prevloc.same"] = new[] { "계속 {room}입니다.", "이동 없습니다." },
        ["fox|prevloc.same"] = new[] { "계속 {room}에 있었죠.", "{room}에서 안 나갔어요." },

        ["owl|prevloc.moved"] = new[] { "그 전에는 {droom}에 있었습니다.", "{droom}에서 {room}으로/로 옮겼습니다." },
        ["cat|prevloc.moved"] = new[] { "{droom}에 있다가 왔어요.", "{droom}이요/요. 거기 있다가 옮겼어요." },
        ["jellyfish|prevloc.moved"] = new[] { "그 전에는... {droom}에 있었어요.", "{droom}에 있다가 {room}으로/로 갔어요." },
        ["rabbit|prevloc.moved"] = new[] { "그 전엔 {droom}에 있었어요!", "{droom}에 있다가 옮겼어요!" },
        ["crow|prevloc.moved"] = new[] { "그 전에는 {droom}입니다.", "{droom}에서 이동했습니다." },
        ["fox|prevloc.moved"] = new[] { "그 전엔 {droom}에 있었어요.", "{droom}에 있다가 옮겼죠." },

        // 그 뒤에는 어떻게 했는가
        ["owl|nextact.stayed"] = new[] { "자리를 지키고 상황을 지켜봤습니다.", "그대로 {room}에 있었습니다." },
        ["cat|nextact.stayed"] = new[] { "그냥 하던 일 계속했어요.", "안 움직였어요." },
        ["jellyfish|nextact.stayed"] = new[] { "무서워서 그냥 있었어요...", "그 자리에 계속 있었어요." },
        ["rabbit|nextact.stayed"] = new[] { "그냥 계속 있었어요! 나가도 되나 싶어서요.", "자리 지켰어요!" },
        ["crow|nextact.stayed"] = new[] { "위치를 유지했습니다.", "그대로 있었습니다." },
        ["fox|nextact.stayed"] = new[] { "가만히 있었죠. 나설 이유가 없어서요.", "그냥 있었어요." },

        ["owl|nextact.moved"] = new[] { "그 뒤에 {droom}으로/로 이동했습니다.", "{droom} 쪽으로 옮겼습니다." },
        ["cat|nextact.moved"] = new[] { "{droom}으로/로 갔어요.", "{droom}이요/요. 그쪽으로 옮겼어요." },
        ["jellyfish|nextact.moved"] = new[] { "그 다음엔... {droom}으로/로 갔어요.", "{droom} 쪽으로 옮겼어요." },
        ["rabbit|nextact.moved"] = new[] { "{droom}으로/로 갔어요!", "바로 {droom} 쪽으로 갔어요!" },
        ["crow|nextact.moved"] = new[] { "{droom}으로/로 이동했습니다.", "이후 {droom}입니다." },
        ["fox|nextact.moved"] = new[] { "{droom}으로/로 옮겼어요.", "{droom} 쪽으로 갔죠." },

        // 당시 같이 있던 사람
        ["owl|present.alone"] = new[] { "혼자였습니다.", "그 시간에는 저 혼자 있었습니다." },
        ["cat|present.alone"] = new[] { "혼자였어요.", "저 혼자요." },
        ["jellyfish|present.alone"] = new[] { "혼자... 있었어요.", "저 혼자였어요." },
        ["rabbit|present.alone"] = new[] { "혼자였어요!", "저밖에 없었어요!" },
        ["crow|present.alone"] = new[] { "혼자였습니다.", "동석자 없습니다." },
        ["fox|present.alone"] = new[] { "혼자였어요.", "아쉽게도 저 혼자요." },

        ["owl|present.with"] = new[] { "{dname} 직원과 같이 있었습니다.", "{dname} 직원이 같은 방에 있었습니다." },
        ["cat|present.with"] = new[] { "{dname} 씨도 있었어요.", "{dname} 씨랑 같이 있었어요." },
        ["jellyfish|present.with"] = new[] { "{dname} 씨도 같이 있었어요.", "{dname} 씨가 있었어요... 아마요." },
        ["rabbit|present.with"] = new[] { "{dname} 씨도 있었어요!", "{dname} 씨랑 같이 있었어요!" },
        ["crow|present.with"] = new[] { "{dname} 직원이 있었습니다.", "{dname}. 같은 방입니다." },
        ["fox|present.with"] = new[] { "{dname} 씨도 있었죠.", "{dname} 씨랑 같이 있었어요." },

        // 위치를 확인해 줄 사람
        ["owl|witness.alone"] = new[] { "확인해 줄 사람은 없습니다. 그건 인정합니다.", "증명해 줄 사람은 없습니다." },
        ["cat|witness.alone"] = new[] { "없어요. 혼자였으니까요.", "없는데요." },
        ["jellyfish|witness.alone"] = new[] { "없어요... 혼자 있어서요.", "그, 그건 없어요..." },
        ["rabbit|witness.alone"] = new[] { "없어요! 혼자 있었거든요.", "아... 없네요." },
        ["crow|witness.alone"] = new[] { "없습니다.", "증인 없습니다." },
        ["fox|witness.alone"] = new[] { "없네요. 하필 혼자였어서.", "그건 없어요." },

        ["owl|witness.with"] = new[] { "{dname} 직원이 확인해 줄 수 있습니다.", "{dname} 직원에게 물어보시면 됩니다." },
        ["cat|witness.with"] = new[] { "{dname} 씨한테 물어보세요.", "{dname} 씨가 봤어요." },
        ["jellyfish|witness.with"] = new[] { "{dname} 씨가... 알 거예요.", "{dname} 씨한테 여쭤보시면 돼요." },
        ["rabbit|witness.with"] = new[] { "{dname} 씨요! 같이 있었어요!", "{dname} 씨한테 물어보세요!" },
        ["crow|witness.with"] = new[] { "{dname}. 확인 가능합니다.", "{dname} 직원입니다." },
        ["fox|witness.with"] = new[] { "{dname} 씨한테 물어보시죠.", "{dname} 씨가 있었어요." },

        // 어떤 소리였는가
        ["owl|heard"] = new[] { "금속이 부딪치는 것 같은 낮고 둔한 소리였습니다.", "짧고 무거운 소리였습니다. 한 번이었습니다." },
        ["cat|heard"] = new[] { "뭔가 크게 부딪치는 소리요.", "쾅 하고 한 번. 그게 다예요." },
        ["jellyfish|heard"] = new[] { "쿵... 하고 벽까지 울리는 소리였어요.", "무슨 소리인지는 잘 모르겠는데, 엄청 컸어요." },
        ["rabbit|heard"] = new[] { "쾅! 하고 엄청 크게 났어요!", "뭐가 터지는 것 같은 소리였어요!" },
        ["crow|heard"] = new[] { "낮고 둔한 충격음. 한 번이었습니다.", "금속음입니다. 짧았습니다." },
        ["fox|heard"] = new[] { "둔탁하게 한 번. 기분 좋은 소리는 아니었어요.", "뭔가 무너지는 듯한 소리였죠." },

        // 직접 본 것인가
        ["owl|seen.saw"] = new[] { "네. 직접 봤습니다.", "제 눈으로 확인했습니다." },
        ["cat|seen.saw"] = new[] { "네, 봤어요.", "직접 봤어요." },
        ["jellyfish|seen.saw"] = new[] { "네... 봤어요.", "직접 봤어요. 정말이에요." },
        ["rabbit|seen.saw"] = new[] { "네! 봤어요!", "제 눈으로 봤어요!" },
        ["crow|seen.saw"] = new[] { "직접 봤습니다.", "확인했습니다." },
        ["fox|seen.saw"] = new[] { "네, 봤어요.", "이건 직접 봤죠." },

        ["owl|seen.heard"] = new[] { "아닙니다. 들은 것뿐입니다.", "본 것은 아닙니다. 소리만 들었습니다." },
        ["cat|seen.heard"] = new[] { "아뇨. 소리만요.", "듣기만 했어요." },
        ["jellyfish|seen.heard"] = new[] { "아, 아니요... 소리만 들었어요.", "본 건 아니에요..." },
        ["rabbit|seen.heard"] = new[] { "아뇨! 소리만 들었어요.", "보진 못했어요!" },
        ["crow|seen.heard"] = new[] { "아닙니다. 청각 정보뿐입니다.", "듣기만 했습니다." },
        ["fox|seen.heard"] = new[] { "아뇨, 들은 거예요.", "보진 못했어요." },

        // 확신하는가
        ["owl|certain.sure"] = new[] { "확실합니다.", "제가 확인한 범위에서는 확실합니다." },
        ["cat|certain.sure"] = new[] { "확실해요.", "네. 확실하니까 말한 거예요." },
        ["jellyfish|certain.sure"] = new[] { "그건... 확실해요.", "네, 그건 맞아요." },
        ["rabbit|certain.sure"] = new[] { "확실해요!", "네! 그건 확실해요!" },
        ["crow|certain.sure"] = new[] { "확실합니다.", "맞습니다." },
        ["fox|certain.sure"] = new[] { "그건 확실해요.", "네, 확실합니다." },

        ["owl|certain.unsure"] = new[] { "단정하기는 어렵습니다.", "확인된 범위를 넘어서는 말씀드릴 수 없습니다." },
        ["cat|certain.unsure"] = new[] { "글쎄요. 거기까진 몰라요.", "확실하냐고 하면 아니죠." },
        ["jellyfish|certain.unsure"] = new[] { "그건... 잘 모르겠어요.", "제가 잘못 안 걸 수도 있어요..." },
        ["rabbit|certain.unsure"] = new[] { "음... 그건 잘 모르겠어요.", "확실하진 않아요!" },
        ["crow|certain.unsure"] = new[] { "단정할 수 없습니다.", "확인 못 했습니다." },
        ["fox|certain.unsure"] = new[] { "글쎄요. 장담은 못 하겠는데요.", "거기까진 모르죠." },

        // 구체적으로 어떤 상황이었는가
        ["owl|detail.direct"] = new[] { "{iroom}에서 {what}", "가까이에서 봤습니다. {what}" },
        ["cat|detail.direct"] = new[] { "{what} 그게 전부예요.", "{iroom}에서 {what}" },
        ["jellyfish|detail.direct"] = new[] { "{what} 순식간이었어요.", "{iroom}에서요... {what}" },
        ["rabbit|detail.direct"] = new[] { "{what} 진짜 순식간이었어요!", "{iroom}에서 {what}" },
        ["crow|detail.direct"] = new[] { "{what} 그 이상은 없습니다.", "{iroom}. {what}" },
        ["fox|detail.direct"] = new[] { "{what} 딱 그 정도예요.", "{iroom} 쪽에서 {what}" },

        ["owl|detail.indirect"] = new[] { "제가 말씀드릴 수 있는 건 소리까지입니다.", "벽 너머라 상황까지는 알 수 없었습니다." },
        ["cat|detail.indirect"] = new[] { "벽 너머라 그 이상은 몰라요.", "소리 말고는 없어요." },
        ["jellyfish|detail.indirect"] = new[] { "소리밖에 못 들어서... 그 이상은 몰라요.", "무슨 일인지까지는 정말 모르겠어요." },
        ["rabbit|detail.indirect"] = new[] { "소리만 들어서 그 이상은 몰라요!", "가서 본 게 아니라서요!" },
        ["crow|detail.indirect"] = new[] { "청각 정보뿐입니다. 그 이상은 불명입니다.", "벽 너머입니다. 확인 불가." },
        ["fox|detail.indirect"] = new[] { "벽 하나 사이라 거기까지예요.", "소리 말곤 아는 게 없어요." },

        ["owl|detail.sight"] = new[] { "그 자리에 있었다는 것까지가 제가 본 전부입니다.", "무엇을 하고 있었는지까지는 확인하지 못했습니다." },
        ["cat|detail.sight"] = new[] { "거기 서 있는 것만 봤어요. 뭘 했는진 몰라요.", "자세히 볼 상황은 아니었어요." },
        ["jellyfish|detail.sight"] = new[] { "거기 있는 것만 봤어요... 뭘 했는지까지는 잘...", "무서워서 오래 못 봤어요." },
        ["rabbit|detail.sight"] = new[] { "거기 있는 건 봤는데 뭘 했는지는 몰라요!", "그냥 있는 것만 봤어요!" },
        ["crow|detail.sight"] = new[] { "위치까지입니다. 행동 내용은 확인하지 못했습니다.", "본 것은 그 자리에 있었다는 것뿐입니다." },
        ["fox|detail.sight"] = new[] { "거기 있던 것까지만요. 그 이상은 저도 몰라요.", "굳이 붙어서 보진 않았거든요." },

        // 어디에서 봤는가
        ["owl|sightplace"] = new[] { "{room}에서 봤습니다." },
        ["cat|sightplace"] = new[] { "{room}이요/요." },
        ["jellyfish|sightplace"] = new[] { "{room}에서요... 거기서 봤어요." },
        ["rabbit|sightplace"] = new[] { "{room}에서 봤어요!" },
        ["crow|sightplace"] = new[] { "{room}입니다." },
        ["fox|sightplace"] = new[] { "{room}에서요." },

        // 그렇게 판단한 이유
        ["owl|reason.sight"] = new[] { "평소 동선이 아니었기 때문입니다.", "그 시간에 그 자리에 있을 이유가 없었습니다." },
        ["cat|reason.sight"] = new[] { "동선이 이상했으니까요.", "거기 있을 일이 아니잖아요." },
        ["jellyfish|reason.sight"] = new[] { "평소랑 달라 보여서요...", "표정이 좀... 이상했어요." },
        ["rabbit|reason.sight"] = new[] { "평소랑 완전 달랐거든요!", "거기 갈 이유가 없잖아요!" },
        ["crow|reason.sight"] = new[] { "평소 동선과 달랐습니다.", "행동 순서가 부자연스러웠습니다." },
        ["fox|reason.sight"] = new[] { "그 시간에 거기 있을 이유가 없죠.", "평소랑 달랐거든요." },

        ["owl|reason.move"] = new[] { "확인이 필요하다고 판단했습니다.", "보고 전에 상황을 확인하려 했습니다." },
        ["cat|reason.move"] = new[] { "필요해서 갔어요.", "가야 할 일이 있었으니까요." },
        ["jellyfish|reason.move"] = new[] { "그, 확인할 게 있어서요...", "무서워서 그쪽으로 간 것도 있고요..." },
        ["rabbit|reason.move"] = new[] { "확인하고 싶어서 갔어요!", "궁금해서 가봤어요!" },
        ["crow|reason.move"] = new[] { "확인 목적입니다.", "필요한 이동이었습니다." },
        ["fox|reason.move"] = new[] { "확인할 게 있었거든요.", "그럴 만한 이유가 있었어요." },

        ["owl|reason.opinion"] = new[] { "같이 일해 본 인상이 그렇습니다.", "근무 태도를 보고 판단했습니다." },
        ["cat|reason.opinion"] = new[] { "일하는 거 보면 알죠.", "그냥 보면 알아요." },
        ["jellyfish|reason.opinion"] = new[] { "그냥... 그렇게 느꼈어요.", "제 느낌이라 확실하진 않아요." },
        ["rabbit|reason.opinion"] = new[] { "같이 일해보면 알아요!", "그냥 그런 느낌이에요!" },
        ["crow|reason.opinion"] = new[] { "관찰한 대로입니다.", "근무 태도 기준입니다." },
        ["fox|reason.opinion"] = new[] { "같이 일해보면 보이죠.", "뭐, 인상이 그래요." },

        // 오늘도 평소와 같았는가 / 수상하다고 느낀 적
        ["owl|opinionq.today"] = new[] { "평소와 크게 다르지 않았습니다.", "오늘 특별히 달라 보이지는 않았습니다." },
        ["cat|opinionq.today"] = new[] { "똑같았어요.", "다를 게 있었나요?" },
        ["jellyfish|opinionq.today"] = new[] { "비슷했던 것 같아요...", "잘 모르겠어요. 오늘은 정신이 없어서요." },
        ["rabbit|opinionq.today"] = new[] { "네! 평소랑 똑같았어요!", "음... 비슷했던 것 같아요!" },
        ["crow|opinionq.today"] = new[] { "차이 없습니다.", "특이사항 없습니다." },
        ["fox|opinionq.today"] = new[] { "평소랑 비슷했죠.", "글쎄요, 다들 오늘은 좀 예민하긴 했지만." },

        ["owl|opinionq.suspicion"] = new[] { "근거 없이 의심할 생각은 없습니다.", "그럴 만한 장면은 보지 못했습니다." },
        ["cat|opinionq.suspicion"] = new[] { "딱히요.", "그런 생각까진 안 해봤어요." },
        ["jellyfish|opinionq.suspicion"] = new[] { "그런 건... 잘 모르겠어요.", "제가 의심할 처지는 아니라서요..." },
        ["rabbit|opinionq.suspicion"] = new[] { "아뇨! 그런 생각은 안 해봤어요.", "설마요!" },
        ["crow|opinionq.suspicion"] = new[] { "판단할 근거가 없습니다.", "없습니다." },
        ["fox|opinionq.suspicion"] = new[] { "여기선 다들 조금씩 수상하죠. 저 포함해서요.", "그런 건 관리자님 몫 아닌가요?" },

        // 추궁에 대한 대응
        ["owl|challenge.honest"] = new[] { "제가 아는 대로 말씀드렸습니다. 기록을 대조해 주십시오.", "숨긴 것은 없습니다. 확인해 보시면 됩니다." },
        ["cat|challenge.honest"] = new[] { "말한 그대로예요. 확인해보세요.", "숨길 게 있으면 말을 안 했겠죠." },
        ["jellyfish|challenge.honest"] = new[] { "저, 저는 사실대로 말했어요... 정말이에요.", "숨긴 건 없어요..." },
        ["rabbit|challenge.honest"] = new[] { "저 진짜 그대로 말한 거예요!", "확인해보시면 아실 거예요!" },
        ["crow|challenge.honest"] = new[] { "사실대로 말했습니다. 기록을 확인하십시오.", "숨긴 것 없습니다." },
        ["fox|challenge.honest"] = new[] { "제 말이 이상하게 들렸다면 다시 설명드리죠.", "숨길 이유가 없는데요." },

        ["owl|challenge.evasive"] = new[] { "기록이 그렇다면 제가 놓친 부분이 있을 겁니다.", "그 부분은 제 기록에 남기지 않았습니다." },
        ["cat|challenge.evasive"] = new[] { "그게 그렇게 중요한가요?", "잠깐 움직인 것까지 다 말해야 해요?" },
        ["jellyfish|challenge.evasive"] = new[] { "그, 그게... 기억이 잘 안 나서요...", "제가 뭘 잘못 말했나요...?" },
        ["rabbit|challenge.evasive"] = new[] { "어? 그게... 그건 별거 아니었어요!", "일부러 안 말한 건 아니에요!" },
        ["crow|challenge.evasive"] = new[] { "그 부분은 기억나지 않습니다.", "말할 만한 일이 아니었습니다." },
        ["fox|challenge.evasive"] = new[] { "기록이 그렇다면 그런 거겠죠. 저는 기억이 좀 다르네요.", "굳이 말할 일이라고 생각을 못 했어요." },

        // ── 끝맺음 ─────────────────────────────────────────────────────
        ["owl|closer"] = new[] { "필요하시면 기록을 확인해주십시오." },
        ["cat|closer"] = new[] { "더 물어보실 거 있어요?" },
        ["jellyfish|closer"] = new[] { "이 정도밖에 못 도와드려서 죄송해요..." },
        ["rabbit|closer"] = new[] { "또 필요하면 말씀해주세요!" },
        ["crow|closer"] = new[] { "이상입니다." },
        ["fox|closer"] = new[] { "이 정도면 되셨나요?", "그래서, 뭐가 더 궁금하신데요?" },
    };
}
