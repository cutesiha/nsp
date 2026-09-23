using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// 로컬 동적 대사 생성기의 진입점.
//
//   게임의 사실 → DialogueContextBuilder → DialogueResponsePlanner
//              → (ShiftMemory 근무 기억) → KoreanDialogueComposer → DialogueComposer
//
// 외부 API를 전혀 쓰지 않으며, 여기서 나가는 모든 문장은 실제 게임 데이터에서 나온다.
// 인터뷰 / 일반 통화 / 수신 전화가 모두 같은 파이프라인을 타고, 문장 틀은 전부
// data/dialogue/lines/*.txt 에 있다.
public static class LocalDialogueGenerator
{
    public const string EventDay1Interview = "day1_local_interview";

    // Q4는 인터뷰 대상마다 서로 다른 직원을 묻는다. 한 바퀴를 도는 고정 매핑이라
    // 자기 자신을 묻거나 여러 인터뷰가 같은 직원에게 몰리지 않는다.
    private static readonly Dictionary<string, string> OpinionTargets = new()
    {
        ["sheep"] = "wolf",
        ["wolf"] = "dog",
        ["dog"] = "cat",
        ["cat"] = "rabbit",
        ["rabbit"] = "fox",
        ["fox"] = "sheep",
    };

    public static string OpinionTargetId(string employeeId) => OpinionTargets.GetValueOrDefault(employeeId, "");

    // 선택지가 열리기 전의 짧은 인사. 사실을 담지 않으므로 캐릭터 말투만 고정으로 둔다.
    public static string InterviewGreeting(string employeeId) => Line(employeeId, "greet.interview", "네, 말씀하세요.");

    // 대사 뱅크에서 한 줄. 없으면 폴백.
    private static string Line(string employeeId, string slot, string fallback)
    {
        var f = new ReplyFrame { EmployeeId = employeeId, CustomSlot = slot, MaxSentences = 1 };
        string text = DialogueLineBank.Has(employeeId, slot, DialogueVoices.Get(employeeId).Formal)
            ? DialogueComposer.Compose(f) : "";
        return string.IsNullOrEmpty(text) || text == "…" ? fallback : text;
    }

    // --- 인터뷰 --------------------------------------------------------

    // 인터뷰 한 턴의 결과 — 답변과, 그 답변을 듣고 한 번 더 캐물을 수 있는 꼬리질문.
    public sealed class InterviewTurn
    {
        public string Answer = "";
        public List<FollowUpQuestion> FollowUps = new();
    }

    // DAY0 교육용 고정 답변 후크. TutorialDirector 가 설정하고 DAY1 진입 시 반드시 해제한다.
    // null 을 돌려주면 평소대로 로그 기반 대사를 생성한다 — 일반 플레이에는 아무 영향이 없다.
    public static System.Func<string, string, string> ScriptedAnswerOverride;

    public static InterviewTurn Interview(string employeeId, string questionId)
    {
        string scripted = ScriptedAnswerOverride?.Invoke(employeeId, questionId);
        if (!string.IsNullOrEmpty(scripted))
            return new InterviewTurn { Answer = scripted };

        // "오늘 이상한 점?" 은 하루의 대표 사고가 아니라 이 직원이 실제로 겪은 가장 최근 사고로 답한다.
        // (대표 사고 하나에 묶으면, 발전실 사고를 직접 수습한 직원이 "이상 없었습니다" 라고 말하게 된다.)
        LogEntry subject = questionId == DialogueQuestions.Anomaly
            ? DialogueContextBuilder.MostRecentKnownIncident(employeeId, DialogueContextBuilder.Day(), excludeOwnActs: true)
            : null;
        var ctx = Context(employeeId, DialogueConversationKind.Interview, questionId, "", subject);
        MarkAsked(ctx, questionId);
        var plan = DialogueResponsePlanner.Plan(ctx);
        string answer = KoreanDialogueComposer.Compose(ctx, plan, Recall(ctx, plan));
        // 꼬리질문은 "이번 답변을 듣기 전까지 플레이어가 알던 것"으로 판단한다.
        // 그래서 증거 기록은 후보를 만든 뒤에 한다.
        var follows = FollowUpQuestionGenerator.Generate(ctx, plan);
        RecordEvidence(ctx, plan);
        return new InterviewTurn { Answer = answer, FollowUps = follows };
    }

    public static string InterviewAnswer(string employeeId, string questionId) =>
        Interview(employeeId, questionId).Answer;

    // 꼬리질문 답변. 기본 질문과 같은 사건을 기준으로 하며, 이미 세운 알리바이와 어긋나지 않는다.
    public static string FollowUpAnswer(string employeeId, FollowUpQuestion question)
    {
        if (question == null) return "";
        int day = DialogueContextBuilder.Day();
        var subject = EventLog.Instance?.GetAllEntries()
            .FirstOrDefault(e => e.Day == day
                && DialogueFact.From(e, KnowledgeLevel.None).Key == question.SubjectIncidentKey);

        string questionId = FollowUpQuestionGenerator.IntentKey(question.Intent);
        var ctx = Context(employeeId, DialogueConversationKind.Interview, questionId, "", subject);
        ctx.BaseQuestionId = question.BaseQuestionId;
        // 꼬리질문은 기본 질문과 같은 사건에 묶인다. 사건을 못 찾았더라도 주장 키는 유지한다.
        if (!string.IsNullOrEmpty(question.SubjectIncidentKey)) ctx.ClaimKey = question.SubjectIncidentKey;
        MarkAsked(ctx, questionId);
        var plan = DialogueResponsePlanner.Plan(ctx);
        string answer = KoreanDialogueComposer.Compose(ctx, plan);
        RecordEvidence(ctx, plan);
        return answer;
    }

    // 직원이 관리자에게 실제로 말한 내용만 "플레이어가 아는 것"으로 남긴다.
    private static void RecordEvidence(DialogueContext ctx, DialogueResponsePlan plan)
    {
        string key = ctx.ClaimKey;
        // 진술이 가리키는 시각. 기준 시각이 없으면 붙이지 않는다(-1) —
        // 시각 없는 진술은 모순 판정의 근거가 되지 못한다.
        float when = ctx.HasSubjectTime ? ctx.SubjectTime : -1f;
        switch (plan.Core)
        {
            case CoreKind.SelfLocation:
                PlayerKnownEvidence.RecordLocationStatement(ctx.EmployeeId, key, plan.RoomId,
                    plan.Time == TimeRef.Exact, when);
                break;
            case CoreKind.SuspiciousSighting:
                // "누구를 어디서 봤다" 뿐 아니라 "그때 무엇을 하고 있었다" 까지 남긴다.
                // 이 게임에서 가장 중요한 단서가 바로 이 한 줄이다(§1-3).
                PlayerKnownEvidence.RecordSighting(ctx.EmployeeId, plan.SubjectEmployeeId,
                    plan.IncidentRoomId, ctx.KnownSuspicious?.TimeSeconds ?? when,
                    ctx.KnownSuspiciousDetail);
                break;
            case CoreKind.SightingPlace:
                PlayerKnownEvidence.RecordSighting(ctx.EmployeeId, plan.SubjectEmployeeId,
                    plan.RoomId, ctx.KnownSuspicious?.TimeSeconds ?? when,
                    ctx.KnownSuspiciousDetail);
                break;

            // 최초 진술(이상한 점)에서 "저도 그 방에 있었어요" 라고 말한 것도 위치 진술이다.
            // 예전에는 이 분기가 없어 화면에 크게 뜨는 답변이 자료로는 쓰이지 못했다(§1-4).
            case CoreKind.IncidentDirect:
            case CoreKind.IncidentIndirect:
                string said = SpokenRoom(ctx, plan, key);
                if (ctx.HasSubjectTime && !string.IsNullOrEmpty(said))
                    PlayerKnownEvidence.RecordLocationStatement(ctx.EmployeeId, key, said,
                        plan.Time == TimeRef.Exact, when);
                break;
        }
    }

    // 이 답변에서 직원이 "자기가 있었다"고 말한 작업실.
    //
    // 사건을 말하는 답(PlanAnomaly)은 plan.RoomId 를 채우지 않는다 — 그 계산은 위치를
    // 묻는 답(PlanWhere)에만 있다. 여기서 같은 규칙을 그대로 쓴다.
    //   결번자 : 자기가 대고 있는 방(ClaimedRoomId) — 실제 방을 남기면 진술이 아니라 진실이 샌다
    //   그 외   : 그 시각 실제로 있던 방
    private static string SpokenRoom(DialogueContext ctx, DialogueResponsePlan plan, string claimKey)
    {
        if (!string.IsNullOrEmpty(plan.RoomId)) return plan.RoomId;
        string room = ctx.IsSaboteur
            ? DialogueClaimState.Get(ctx.EmployeeId, DialogueContextBuilder.Day(), claimKey).ClaimedRoomId
            : ctx.RoomAtSubject;
        return string.IsNullOrEmpty(room) ? ctx.AssignedRoomId : room;
    }

    // --- 플레이어가 거는 일반 통화 ---------------------------------------

    public static string GeneralGreeting(string employeeId) =>
        Line(employeeId, "greet.call", DialogueRepository.Greeting(employeeId));

    // 질문 텍스트는 기존 구조(docs/NSP_DIALOGUE_RUNTIME.md)를 그대로 쓰고, 대답만 현재 상태에서 만든다.
    public static string GeneralAnswer(string employeeId, int index)
    {
        string questionId = index switch
        {
            0 => DialogueQuestions.GeneralStatus,
            1 => DialogueQuestions.GeneralFocus,
            _ => DialogueQuestions.GeneralAnomaly,
        };

        // "이상현상은 없었나요?" 는 고정된 인터뷰 대상 사건이 아니라
        // 이 직원이 지금까지 실제로 알게 된 가장 최근 사건을 기준으로 답한다.
        LogEntry subject = questionId == DialogueQuestions.GeneralAnomaly
            ? DialogueContextBuilder.MostRecentKnownIncident(employeeId, DialogueContextBuilder.Day())
            : null;

        var ctx = Context(employeeId, DialogueConversationKind.OutgoingCall, questionId, "", subject);
        MarkAsked(ctx, questionId);
        var plan = DialogueResponsePlanner.Plan(ctx);
        return KoreanDialogueComposer.Compose(ctx, plan, Recall(ctx, plan));
    }

    // --- 직원이 거는 수신 전화 -------------------------------------------

    public sealed class CallChoice
    {
        public string Text = "";
        public string Reply = "";
    }

    public sealed class CallLine
    {
        public string Opening = "";
        public readonly List<CallChoice> Choices = new();
    }

    // index 0 = 움직이라는 지시, index 1 = 그대로 두라는 지시.
    // IncomingCallDirector 의 출동 판정이 이 순서에 의존하므로 절대 뒤집지 않는다.
    public static CallLine BuildIncomingCall(string employeeId, string dialogueEvent, string roomId)
    {
        // 교육용 고정 통화는 생성하지 않는다 — 대사 파일의 문장을 그대로 쓴다.
        if (dialogueEvent == DialogueRepository.EventTutorialRepairDone) return null;

        // 잡담 전화는 사건 기록을 필요로 하지 않는다 — 사건이 아니기 때문이다.
        if (dialogueEvent is DialogueRepository.EventIdleVisit or DialogueRepository.EventIdleWorry)
            return BuildIdleCall(employeeId, dialogueEvent, roomId);

        var subject = FindEventSubject(employeeId, dialogueEvent, roomId);
        var line = new CallLine();

        if (dialogueEvent == DialogueRepository.EventWitnessSuspicious)
        {
            var ctx0 = Context(employeeId, DialogueConversationKind.IncomingCall,
                DialogueQuestions.Suspicious, dialogueEvent, subject);
            var plan0 = DialogueResponsePlanner.Plan(ctx0);
            // 실제 목격 기록이 없으면 이 전화 자체가 성립하지 않는다 — 폴백으로 넘긴다.
            if (plan0.Core != CoreKind.SuspiciousSighting) return null;
            line.Opening = CallPrefix(employeeId) + " " + KoreanDialogueComposer.Compose(ctx0, plan0);
            AddChoices(line, employeeId, "계속 지켜봐주세요.", "신경 쓰지 말고 업무를 계속하세요.");
            return line;
        }

        var ctx = Context(employeeId, DialogueConversationKind.IncomingCall,
            DialogueQuestions.IncidentReport, dialogueEvent, subject);
        var plan = DialogueResponsePlanner.Plan(ctx);
        if (plan.Core != CoreKind.IncidentReport) return null;

        if (dialogueEvent == DialogueRepository.EventBlackout)
        {
            plan.StatusNote = "blackout";
            plan.Knowledge = KnowledgeLevel.Direct;
            plan.NeedsIndirectCaveat = false;
            line.Opening = KoreanDialogueComposer.Compose(ctx, plan);
            AddChoices(line, employeeId, "비상등이 있는 곳으로 이동하세요.", "그 자리에서 대기하세요.");
            return line;
        }

        line.Opening = KoreanDialogueComposer.Compose(ctx, plan);
        AddChoices(line, employeeId, "확인하러 가주세요.", "지금 자리에서 대기하세요.");
        return line;
    }

    // 조용한 시간의 전화. 사실을 만들지 않는다 — 말하는 것은 "가도 되느냐" 하나뿐이고,
    // 그 판단은 관리자가 한다. 허락 여부에 따라 대답만 달라진다.
    //
    // 선택지 0 이 "가라"인 것은 사고 신고 전화와 같은 규약이다(IncomingCallDirector 의
    // 출동 처리가 그 순서에 의존한다).
    private static CallLine BuildIdleCall(string employeeId, string dialogueEvent, string roomId)
    {
        bool worry = dialogueEvent == DialogueRepository.EventIdleWorry;
        string room = InterviewEvidenceBoard.RoomName(roomId);
        string who = worry ? CodenameIn(roomId, employeeId) : "";
        // 걱정하는 전화인데 그 방에 아무도 없으면 할 말이 없다.
        if (worry && string.IsNullOrEmpty(who)) return null;
        if (string.IsNullOrEmpty(room)) return null;

        var line = new CallLine
        {
            Opening = Slot(employeeId, worry ? "idle.worry" : "idle.visit",
                worry ? $"{room}에서 이상한 소리가 납니다. 확인하러 가도 되겠습니까?"
                      : $"여기 혼자라서요. 옆 작업실에 잠깐 가 봐도 될까요?",
                ("room", room), ("who", who)),
        };
        line.Choices.Add(new CallChoice
        {
            Text = worry ? "그러십시오." : "그렇게 하십시오.",
            Reply = Slot(employeeId, (worry ? "idle.worry" : "idle.visit") + ".allowed",
                "알겠습니다. 다녀오겠습니다.", ("room", room), ("who", who)),
        });
        line.Choices.Add(new CallChoice
        {
            Text = worry ? "아니오. 제가 확인해 보겠습니다." : "안 됩니다. 작업을 계속 하십시오.",
            Reply = Slot(employeeId, (worry ? "idle.worry" : "idle.visit") + ".denied",
                "…알겠습니다.", ("room", room), ("who", who)),
        });
        return line;
    }

    // 그 방에 있는 사람의 코드네임(전화 건 사람 자신은 뺀다).
    private static string CodenameIn(string roomId, string exceptId)
    {
        var sim = NSP.Facility.FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(roomId)) return "";
        foreach (string id in sim.OnDutyEmployeeIds(roomId))
            if (id != exceptId) return sim.GetEmployeeDef(id)?.Codename ?? "";
        return "";
    }

    // 변수를 끼운 슬롯 한 줄. 뱅크에 없거나 변수가 비면 폴백을 쓴다.
    private static string Slot(string employeeId, string slot, string fallback,
        params (string Key, string Value)[] vars)
    {
        var f = new ReplyFrame { EmployeeId = employeeId, CustomSlot = slot, MaxSentences = 2 };
        foreach (var (k, v) in vars) f.Set(k, v);
        string text = DialogueLineBank.Has(employeeId, slot, DialogueVoices.Get(employeeId).Formal)
            ? DialogueComposer.Compose(f) : "";
        return string.IsNullOrEmpty(text) || text == "…"
            ? KoreanParticle.Resolve(fallback) : text;
    }

    private static void AddChoices(CallLine line, string employeeId, string goText, string stayText)
    {
        line.Choices.Add(new CallChoice { Text = goText, Reply = DispatchReply(employeeId, true) });
        line.Choices.Add(new CallChoice { Text = stayText, Reply = DispatchReply(employeeId, false) });
    }

    private static string DispatchReply(string employeeId, bool accept)
    {
        var ctx = Context(employeeId, DialogueConversationKind.IncomingCall,
            accept ? DialogueQuestions.DispatchAccept : DialogueQuestions.DispatchDecline, "", null);
        var plan = DialogueResponsePlanner.Plan(ctx);
        return KoreanDialogueComposer.Compose(ctx, plan);
    }

    private static string CallPrefix(string employeeId) => Line(employeeId, "call.prefix", "관리자님.");

    // 이 전화가 다루는 사건. 실제 로그에서만 찾는다 — 없으면 전화 대사를 만들지 않는다.
    private static LogEntry FindEventSubject(string employeeId, string dialogueEvent, string roomId)
    {
        var log = EventLog.Instance;
        if (log == null) return null;
        int day = DialogueContextBuilder.Day();
        var today = log.GetAllEntries().Where(e => e.Day == day);

        return dialogueEvent switch
        {
            DialogueRepository.EventScreamNextRoom => today
                .Where(e => e.EventType == LogEventType.Death && e.RoomId == roomId)
                .OrderByDescending(e => e.GameTimeSeconds).FirstOrDefault(),
            DialogueRepository.EventBlackout => today
                .Where(e => e.EventType == LogEventType.PowerOutage)
                .OrderByDescending(e => e.GameTimeSeconds).FirstOrDefault(),
            DialogueRepository.EventWitnessSuspicious => today
                .Where(e => DialogueContextBuilder.IsSuspiciousAction(e.EventType)
                            && e.WitnessEmployeeIds.Contains(employeeId))
                .OrderByDescending(e => e.GameTimeSeconds).FirstOrDefault(),
            _ => today
                .Where(e => DialogueContextBuilder.IsIncident(e.EventType) && e.RoomId == roomId)
                .OrderByDescending(e => e.GameTimeSeconds).FirstOrDefault(),
        };
    }

    // --- 공통 -----------------------------------------------------------

    private static DialogueContext Context(string employeeId, DialogueConversationKind kind,
        string questionId, string eventId, LogEntry subject)
    {
        var ctx = DialogueContextBuilder.Build(employeeId, kind, questionId, eventId, subject);
        ctx.TargetEmployeeId = OpinionTargetId(employeeId);
        return ctx;
    }

    // --- 근무 기억 ---------------------------------------------------------

    // 이 답변 뒤에 덧붙일 기억을 고른다. 질문 종류가 "무엇을 떠올릴지"를 정하고,
    // 결번자가 거짓 알리바이를 대는 중이면 주장한 방을 기준으로만 떠올린다.
    private static RecallResult Recall(DialogueContext ctx, DialogueResponsePlan plan)
    {
        var topic = ctx.QuestionId switch
        {
            DialogueQuestions.ShiftReview => RecallTopic.ShiftReview,
            // 아는 사고가 없다는 답에는 "그쯤" 같은 기억을 붙이지 않는다 — 가리킬 시각이 없다.
            DialogueQuestions.Anomaly or DialogueQuestions.GeneralAnomaly
                when plan.Core is CoreKind.IncidentDirect or CoreKind.IncidentIndirect => RecallTopic.Anomaly,
            DialogueQuestions.Suspicious when plan.Core == CoreKind.NoSighting => RecallTopic.Suspicious,
            DialogueQuestions.Where => RecallTopic.Location,
            DialogueQuestions.Accuse => RecallTopic.Accuse,
            DialogueQuestions.GeneralStatus => RecallTopic.Status,
            _ => RecallTopic.None,
        };
        if (topic == RecallTopic.None) return null;

        var claim = DialogueClaimState.Get(ctx.EmployeeId, ctx.CurrentDay, ctx.ClaimKey);
        bool lying = ctx.IsSaboteur && !claim.ClaimTruthful && !string.IsNullOrEmpty(claim.ClaimedRoomId);
        string room = lying ? claim.ClaimedRoomId
            : !string.IsNullOrEmpty(plan.RoomId) && topic == RecallTopic.Location ? plan.RoomId
            : ctx.RoomAtSubject;

        var req = new RecallRequest
        {
            EmployeeId = ctx.EmployeeId,
            Day = ctx.CurrentDay,
            Topic = topic,
            IsSaboteur = ctx.IsSaboteur,
            Lying = lying,
            IsRepeat = ctx.IsRepeat,
            SubjectIncidentKey = ctx.Subject?.Key ?? "",
            AnchorRoom = room ?? "",
            AnchorTime = topic switch
            {
                // 하루 전체를 돌아보는 질문은 시각에 묶지 않는다.
                RecallTopic.ShiftReview or RecallTopic.Suspicious => -1f,
                // 근무 중 통화는 "방금 전" 을 떠올린다.
                RecallTopic.Status => ctx.CurrentGameTime,
                _ => ctx.HasSubjectTime ? ctx.SubjectTime : -1f,
            },
        };
        if (topic == RecallTopic.Status)
            req.AnchorRoom = string.IsNullOrEmpty(ctx.CurrentRoomId) ? ctx.AssignedRoomId : ctx.CurrentRoomId;
        // 휴게시간 "오늘 근무" 답의 핵심이 이미 오늘 겪은 사고를 말한다.
        if (topic == RecallTopic.ShiftReview && plan.StatusNote == "busy")
        {
            req.Covered.Add(MemoryKind.IncidentHere);
            req.Covered.Add(MemoryKind.IncidentHeard);
        }
        return ShiftMemory.Recall(req);
    }

    // 같은 질문을 다시 받았는지 기록한다. 핵심 주장은 그대로 두고 표현만 바뀐다.
    private static void MarkAsked(DialogueContext ctx, string questionId)
    {
        var claim = DialogueClaimState.Get(ctx.EmployeeId, ctx.CurrentDay, ctx.ClaimKey);
        ctx.AskCount = claim.Ask(questionId);
        ctx.IsRepeat = ctx.AskCount > 0;
    }

    // --- 개발용 비교 출력 ------------------------------------------------
    // 같은 사실을 6명에게 넣고 출력을 나란히 본다. 릴리즈 UI 에는 노출하지 않는다.
    public static string DebugCompare(string questionId)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{questionId} / SAME FACT]");
        var sim = FacilitySimulation.Instance;
        var ids = sim != null ? sim.GetEmployeeIds().ToList() : DialogueVoiceProfiles.Ids.ToList();
        foreach (string id in ids)
        {
            var ctx = Context(id, DialogueConversationKind.Interview, questionId, "", null);
            var plan = DialogueResponsePlanner.Plan(ctx);
            string codename = sim?.GetEmployeeDef(id)?.Codename ?? id;
            sb.AppendLine($"{id.ToUpperInvariant()} ({codename}): {KoreanDialogueComposer.Compose(ctx, plan)}");
        }
        return sb.ToString();
    }

    public static void DebugPrintAll()
    {
        foreach (string q in new[]
        {
            DialogueQuestions.Anomaly, DialogueQuestions.Where, DialogueQuestions.Suspicious,
            DialogueQuestions.Opinion, DialogueQuestions.Accuse,
        })
            GD.Print(DebugCompare(q));
    }
}
