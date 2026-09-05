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
//   게임의 사실 → DialogueContextBuilder → DialogueResponsePlanner → KoreanDialogueComposer
//
// 외부 API를 전혀 쓰지 않으며, 여기서 나가는 모든 문장은 실제 게임 데이터에서 나온다.
// 인터뷰 / 일반 통화 / 수신 전화가 모두 같은 파이프라인을 탄다.
public static class LocalDialogueGenerator
{
    public const string EventDay1Interview = "day1_local_interview";

    // Q4는 인터뷰 대상마다 서로 다른 직원을 묻는다. 한 바퀴를 도는 고정 매핑이라
    // 자기 자신을 묻거나 여러 인터뷰가 같은 직원에게 몰리지 않는다.
    private static readonly Dictionary<string, string> OpinionTargets = new()
    {
        ["owl"] = "cat",
        ["cat"] = "jellyfish",
        ["jellyfish"] = "rabbit",
        ["rabbit"] = "crow",
        ["crow"] = "fox",
        ["fox"] = "owl",
    };

    public static string OpinionTargetId(string employeeId) => OpinionTargets.GetValueOrDefault(employeeId, "");

    // 선택지가 열리기 전의 짧은 인사. 사실을 담지 않으므로 캐릭터 말투만 고정으로 둔다.
    public static string InterviewGreeting(string employeeId) => employeeId switch
    {
        "rabbit" => "네, 관리자님! 무슨 일이에요?",
        "fox" => "네~ 관리자님. 저 찾으셨어요?",
        "cat" => "네. 왜 부르셨어요?",
        "crow" => "네.",
        "owl" => "네, 관리자님. 말씀하세요.",
        "jellyfish" => "아..! 관리자님. 듣고 있어요.",
        _ => "네, 말씀하세요.",
    };

    // --- 인터뷰 --------------------------------------------------------

    public static string InterviewAnswer(string employeeId, string questionId)
    {
        var ctx = Context(employeeId, DialogueConversationKind.Interview, questionId, "", null);
        MarkAsked(ctx, questionId);
        var plan = DialogueResponsePlanner.Plan(ctx);
        return KoreanDialogueComposer.Compose(ctx, plan);
    }

    // --- 플레이어가 거는 일반 통화 ---------------------------------------

    public static string GeneralGreeting(string employeeId) => DialogueRepository.Greeting(employeeId);

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
        return KoreanDialogueComposer.Compose(ctx, plan);
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

    private static string CallPrefix(string employeeId) => employeeId switch
    {
        "owl" => "관리자님, 보고드릴 게 있습니다.",
        "cat" => "관리자님. 하나 보고할게요.",
        "jellyfish" => "저, 관리자님... 이거 말씀드려야 할 것 같아서요.",
        "rabbit" => "관리자님! 이거 보셨어요?",
        "crow" => "보고드립니다.",
        "fox" => "관리자님, 잠깐만요.",
        _ => "관리자님.",
    };

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

    // 같은 질문을 다시 받았는지 기록한다. 핵심 주장은 그대로 두고 표현만 바뀐다.
    private static void MarkAsked(DialogueContext ctx, string questionId)
    {
        var claim = DialogueClaimState.Get(ctx.EmployeeId, ctx.CurrentDay, ctx.Subject?.Key ?? "no_incident");
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
