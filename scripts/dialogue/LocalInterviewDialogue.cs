using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// DAY1 휴게시간용 로컬 인터뷰.
// docs/휴게시간_대사목록.md의 대표 대사를 읽고 성격별 보조 문장 풀을 더한 뒤, 현재 게임의
// 사실(로그·목격자·위치·런타임 방해자)을 문장 변수에 넣는다.
public static class LocalInterviewDialogue
{
    private const string PlayerOnlyRoomId = "central_office";
    public const string EventDay1Interview = "day1_local_interview";
    public const string Q1Anomaly = "Q1_ANOMALY";
    public const string Q2Where = "Q2_WHERE";
    public const string Q3Suspicious = "Q3_SUSPICIOUS";
    public const string Q4Opinion = "Q4_OPINION";
    public const string Q5Accuse = "Q5_ACCUSE";

    private const string DataPath = "res://docs/휴게시간_대사목록.md";
    private const string Civilian = "CIVILIAN";
    private const string Saboteur = "SABOTEUR";
    private const string HaveInfo = "CIVILIAN_HAVE_INFO";
    private const string IndirectInfo = "CIVILIAN_INDIRECT_INFO";
    private const string NoInfo = "CIVILIAN_NO_INFO";
    private const int MaxHistoryTurns = 6;

    public sealed class Question
    {
        public string Id = "";
    }

    private sealed class InterviewContext
    {
        public LogEntry DirectIncident;
        public LogEntry IndirectIncident;
        public LogEntry DirectSuspiciousAction;
        public string SelfRoom = "";
        public string OpinionTargetId = "";
    }

    private static readonly Question[] Day1Questions =
    {
        new() { Id = Q1Anomaly },
        new() { Id = Q2Where },
        new() { Id = Q3Suspicious },
        new() { Id = Q4Opinion },
        new() { Id = Q5Accuse },
    };

    private static readonly Dictionary<string, string> CharacterIds = new(StringComparer.Ordinal)
    {
        ["올빼미"] = "owl",
        ["고양이"] = "cat",
        ["해파리"] = "jellyfish",
        ["토끼"] = "rabbit",
        ["까마귀"] = "crow",
        ["여우"] = "fox",
    };

    // Q4는 인터뷰 대상마다 서로 다른 직원을 묻는다. 한 바퀴를 도는 고정 매핑이라
    // 자기 자신을 묻거나 여러 인터뷰가 같은 직원에게 몰리지 않는다.
    private static readonly Dictionary<string, string> OpinionTargets = new(StringComparer.Ordinal)
    {
        ["owl"] = "cat",
        ["cat"] = "jellyfish",
        ["jellyfish"] = "rabbit",
        ["rabbit"] = "crow",
        ["crow"] = "fox",
        ["fox"] = "owl",
    };

    private static readonly Dictionary<string, List<string>> Templates = new(StringComparer.Ordinal);
    private static bool _loaded;

    public static IReadOnlyList<Question> Questions => Day1Questions;

    // 인터뷰 선택지가 열리기 전에 직원이 먼저 답하는 고정 인사.
    // 역할과 무관한 캐릭터 말투만 담고, 방해자 판정은 Answer에서 계속 런타임으로 한다.
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

    public static string GetQuestionText(string employeeId, string questionId)
    {
        return questionId switch
        {
            Q1Anomaly => "오늘 근무 중 이상한 점은 없었습니까?",
            Q2Where => "사고 당시 어디에 있었습니까?",
            Q3Suspicious => "수상한 행동을 한 직원을 봤습니까?",
            Q4Opinion => $"{Codename(FindOpinionTarget(employeeId))} 직원을 어떻게 생각합니까?",
            Q5Accuse => "현재 당신을 의심하고 있습니다.",
            _ => "질문을 선택하십시오.",
        };
    }

    public static string Answer(string employeeId, string questionId)
    {
        EnsureLoaded();
        var context = BuildContext(employeeId);
        bool isSaboteur = GameState.Instance?.SaboteurEmployeeId == employeeId;
        string variant = SelectVariant(questionId, isSaboteur, context);
        string template = FindTemplate(employeeId, questionId, variant);

        // 다른 직원을 지목하는 방해자용 문장에는 실제로 본 행동이 있어야 한다.
        // 없다면 문서의 NO_INFO 문장을 사용해 사실을 새로 만들지 않는다.
        if (isSaboteur && template.Contains("{SUSPECT}") && context.DirectSuspiciousAction == null)
            template = FindTemplate(employeeId, questionId, NoInfo);

        if (string.IsNullOrEmpty(template))
        {
            GD.PushWarning($"LocalInterviewDialogue: {employeeId}/{questionId}/{variant} 대사를 찾지 못했습니다.");
            return "기록을 정리하는 중입니다. 다시 질문해주십시오.";
        }

        var incident = context.DirectIncident ?? context.IndirectIncident;
        var relevant = questionId == Q3Suspicious ? context.DirectSuspiciousAction : incident;
        string targetId = context.OpinionTargetId;
        return ReplaceVariables(template,
            TimeOf(relevant),
            RoomName(relevant?.RoomId),
            context.SelfRoom,
            Codename(targetId),
            Codename(context.DirectSuspiciousAction?.ActorEmployeeId),
            IncidentName(incident),
            DetailOf(relevant));
    }

    public static void RecordTurn(string employeeId, string playerText, string reply)
    {
        var state = FacilitySimulation.Instance?.GetEmployeeState(employeeId);
        if (state == null) return;
        state.ConversationHistory.Add(new ConversationTurn { Role = "player", Text = playerText });
        state.ConversationHistory.Add(new ConversationTurn { Role = "npc", Text = reply });
        while (state.ConversationHistory.Count > MaxHistoryTurns)
            state.ConversationHistory.RemoveAt(0);
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!FileAccess.FileExists(DataPath))
        {
            GD.PushWarning($"LocalInterviewDialogue: {DataPath} 를 찾지 못했습니다.");
            AddSupplementalVariations();
            return;
        }

        using var file = FileAccess.Open(DataPath, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            AddSupplementalVariations();
            return;
        }

        string characterId = "";
        string questionId = "";
        string pendingVariant = "";
        while (!file.EofReached())
        {
            string line = file.GetLine().Trim();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                int marker = line.IndexOf(". ", StringComparison.Ordinal);
                string name = marker >= 0 ? line[(marker + 2)..].Trim() : "";
                characterId = CharacterIds.GetValueOrDefault(name, "");
                questionId = "";
                pendingVariant = "";
                continue;
            }
            if (line.StartsWith("### Q", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                questionId = parts.Length > 1 ? parts[1] : "";
                pendingVariant = "";
                continue;
            }
            if (line.StartsWith("**", StringComparison.Ordinal) && line.EndsWith("**", StringComparison.Ordinal))
            {
                string header = line.Trim('*').Trim();
                pendingVariant = header switch
                {
                    "CIVILIAN / HAVE_INFO" => HaveInfo,
                    "CIVILIAN / INDIRECT_INFO" => IndirectInfo,
                    "CIVILIAN / NO_INFO" => NoInfo,
                    "CIVILIAN" => Civilian,
                    "SABOTEUR" => Saboteur,
                    _ => "",
                };
                continue;
            }
            if (!string.IsNullOrEmpty(pendingVariant) && line.StartsWith(">", StringComparison.Ordinal))
            {
                string text = line[1..].Trim();
                if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
                if (!string.IsNullOrEmpty(characterId) && !string.IsNullOrEmpty(questionId))
                    AddVariations(characterId, questionId, pendingVariant, text);
            }
        }
        AddSupplementalVariations();
    }

    private static string SelectVariant(string questionId, bool isSaboteur, InterviewContext context)
    {
        // 방해자라도 사고 당시 그 방에 없었다면 Q1에서 존재하지 않는 사고 정보를
        // 만들어내지 않는다. 실제로 그 방에 있었을 때만 방해자용 회피 답변을 사용한다.
        if (questionId == Q1Anomaly)
        {
            if (context.DirectIncident != null) return isSaboteur ? Saboteur : HaveInfo;
            if (context.IndirectIncident != null) return IndirectInfo;
            return NoInfo;
        }
        if (isSaboteur) return Saboteur;
        return questionId switch
        {
            Q3Suspicious => context.DirectSuspiciousAction != null ? HaveInfo : NoInfo,
            _ => Civilian,
        };
    }

    private static InterviewContext BuildContext(string employeeId)
    {
        var context = new InterviewContext
        {
            DirectIncident = FindDirectIncident(employeeId),
            IndirectIncident = FindIndirectIncident(employeeId),
            DirectSuspiciousAction = FindDirectSuspiciousAction(employeeId),
            SelfRoom = FindSelfRoom(employeeId),
            OpinionTargetId = FindOpinionTarget(employeeId),
        };
        return context;
    }

    // 명시적 목격자뿐 아니라 사고 시각에 실제로 그 작업실 안에 있던 직원도 해당 사고를
    // 경험한 것으로 본다. 금기 위반/설비 고장 로그는 WitnessEmployeeIds가 비어 있는 경우가
    // 많아서 목격자 배열만 검사하면 같은 방 직원도 Q1에서 "못 봤다"고 답하게 된다.
    private static LogEntry FindDirectIncident(string employeeId)
    {
        var log = EventLog.Instance;
        if (log == null) return null;
        int day = GameState.Instance?.CurrentDay ?? 1;
        return log.GetAllEntries()
            .Where(e => e.Day == day && IsIncident(e.EventType)
                && (e.WitnessEmployeeIds.Contains(employeeId) || WasInRoomAt(employeeId, e)))
            .OrderByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
    }

    // 시작 위치부터 사고 로그 바로 전까지 입·퇴실 기록을 순서대로 재생한다.
    // Relocation은 "이동 명령"이라 실제 도착이 아니므로 위치 증거로 쓰지 않는다.
    private static bool WasInRoomAt(string employeeId, LogEntry incident)
        => EmployeeRoomAt(employeeId, incident) == incident?.RoomId;

    private static string EmployeeRoomAt(string employeeId, LogEntry incident)
    {
        if (incident == null) return "";
        var log = EventLog.Instance;
        if (log == null) return "";

        string room = FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.StartRoomId ?? "";
        foreach (var e in log.GetAllEntries())
        {
            if (ReferenceEquals(e, incident)) break;
            if (e.Day != incident.Day || e.ActorEmployeeId != employeeId) continue;

            switch (e.EventType)
            {
                case LogEventType.RoomEnter:
                case LogEventType.TaskStart:
                    if (!string.IsNullOrEmpty(e.RoomId) && e.RoomId != PlayerOnlyRoomId)
                        room = e.RoomId;
                    break;
                case LogEventType.RoomExit:
                    if (room == e.RoomId) room = "";
                    break;
                case LogEventType.Isolation:
                    room = e.RoomId;
                    break;
                case LogEventType.Death:
                    room = e.RoomId;
                    break;
            }
        }
        return room;
    }

    // 사고실과 통로로 직접 연결된 작업실에 있던 직원은 굉음·진동·비명처럼
    // 벽 너머에서 알 수 있는 범위만 말한다. 중앙 제어실은 직원 위치/인접실에서 제외한다.
    private static LogEntry FindIndirectIncident(string employeeId)
    {
        var log = EventLog.Instance;
        var sim = FacilitySimulation.Instance;
        if (log == null || sim == null) return null;
        int day = GameState.Instance?.CurrentDay ?? 1;
        return log.GetAllEntries()
            .Where(e => e.Day == day && IsIncident(e.EventType)
                && !e.WitnessEmployeeIds.Contains(employeeId)
                && !WasInRoomAt(employeeId, e)
                && IsAdjacent(EmployeeRoomAt(employeeId, e), e.RoomId))
            .OrderByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
    }

    private static bool IsAdjacent(string employeeRoomId, string incidentRoomId)
    {
        if (string.IsNullOrEmpty(employeeRoomId) || string.IsNullOrEmpty(incidentRoomId)
            || employeeRoomId == PlayerOnlyRoomId || incidentRoomId == PlayerOnlyRoomId)
            return false;
        var sim = FacilitySimulation.Instance;
        var from = sim?.GetRoomDef(employeeRoomId);
        var to = sim?.GetRoomDef(incidentRoomId);
        return (from?.ConnectedRoomIds.Contains(incidentRoomId) ?? false)
            || (to?.ConnectedRoomIds.Contains(employeeRoomId) ?? false);
    }

    private static LogEntry FindDirectSuspiciousAction(string employeeId)
    {
        return KnownEntries(employeeId)
            .Where(e => e.WitnessEmployeeIds.Contains(employeeId)
                && !string.IsNullOrEmpty(e.ActorEmployeeId)
                && e.ActorEmployeeId != employeeId
                && IsSuspiciousAction(e.EventType))
            .OrderByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
    }

    private static IEnumerable<LogEntry> KnownEntries(string employeeId)
    {
        var log = EventLog.Instance;
        if (log == null) return Enumerable.Empty<LogEntry>();
        int day = GameState.Instance?.CurrentDay ?? 1;
        return log.GetEntriesKnownBy(employeeId).Where(e => e.Day == day);
    }

    private static string FindSelfRoom(string employeeId)
    {
        var log = EventLog.Instance;
        int day = GameState.Instance?.CurrentDay ?? 1;
        var lastLocation = log == null ? null : log.GetAllEntries()
            .Where(e => e.Day == day && e.ActorEmployeeId == employeeId
                && !string.IsNullOrEmpty(e.RoomId) && e.RoomId != PlayerOnlyRoomId)
            .OrderByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
        if (lastLocation != null) return RoomName(lastLocation.RoomId);

        var state = FacilitySimulation.Instance?.GetEmployeeState(employeeId);
        string roomId = state?.AssignedRoomId;
        if (string.IsNullOrEmpty(roomId) || roomId == PlayerOnlyRoomId)
            roomId = state?.CurrentRoomId;
        return RoomName(roomId);
    }

    private static string FindOpinionTarget(string employeeId)
    {
        return OpinionTargets.GetValueOrDefault(employeeId, "");
    }

    private static bool IsIncident(LogEventType type) => type is LogEventType.Sabotage
        or LogEventType.TaskFailed
        or LogEventType.TabooViolation
        or LogEventType.PowerOutage
        or LogEventType.CctvDisconnect
        or LogEventType.Death;

    private static bool IsSuspiciousAction(LogEventType type) => type is LogEventType.Sabotage
        or LogEventType.Neglect
        or LogEventType.FalseOrderFollowed
        or LogEventType.TabooViolation;

    private static string FindTemplate(string employeeId, string questionId, string variant)
    {
        if (!Templates.TryGetValue(TemplateKey(employeeId, questionId, variant), out var choices)
            || choices.Count == 0) return "";
        return choices[(int)(GD.Randi() % (uint)choices.Count)];
    }

    private static void AddVariations(string employeeId, string questionId, string variant, params string[] lines)
    {
        string key = TemplateKey(employeeId, questionId, variant);
        if (!Templates.TryGetValue(key, out var choices))
            Templates[key] = choices = new List<string>();
        foreach (string line in lines)
            if (!string.IsNullOrWhiteSpace(line) && !choices.Contains(line.Trim())) choices.Add(line.Trim());
    }

    // 문서의 대표 대사는 유지하되, 매 인터뷰가 똑같이 들리지 않도록 성격별 런타임 문장 풀을 보강한다.
    private static void AddSupplementalVariations()
    {
        AddVariations("owl", Q1Anomaly, HaveInfo,
            "{TIME} 무렵 {ROOM}에서 {INCIDENT}을 확인했습니다. 우연으로 넘길 상황은 아니었습니다.",
            "{ROOM} 쪽 이상은 제가 직접 봤습니다. {INCIDENT} 말입니다.");
        AddVariations("owl", Q1Anomaly, IndirectInfo,
            "저는 옆 작업실에 있었습니다만, {TIME}쯤 {ROOM} 쪽에서 큰 소리가 들렸습니다.",
            "직접 보진 못했습니다. 다만 {ROOM} 방향에서 진동이 느껴져 사고가 났다고 판단했습니다.");
        AddVariations("owl", Q1Anomaly, NoInfo,
            "제가 있던 곳에서는 특별한 징후를 확인하지 못했습니다.");
        AddVariations("owl", Q2Where, Civilian, "당시에는 {SELF_ROOM}에서 맡은 업무를 수행하고 있었습니다.");
        AddVariations("owl", Q3Suspicious, HaveInfo, "{SUSPECT} 직원의 행동이 평소와 달랐습니다. 그냥 넘기기는 어렵습니다.");
        AddVariations("owl", Q3Suspicious, NoInfo, "확인되지 않은 사람을 지목할 생각은 없습니다.");
        AddVariations("owl", Q4Opinion, Civilian, "{TARGET} 직원은 감정보다 맡은 일을 우선하는 편입니다.");
        AddVariations("owl", Q5Accuse, Civilian, "의심하실 수는 있습니다. 대신 기록부터 차분히 확인해주십시오.");

        AddVariations("cat", Q1Anomaly, HaveInfo,
            "{TIME}쯤 {ROOM}에서 {INCIDENT}요. 바로 앞에서 봤는데, 정상은 아니었어요.",
            "{ROOM}에서 난 사고 말하는 거죠? {INCIDENT}이었어요. 꽤 위험했고요.");
        AddVariations("cat", Q1Anomaly, IndirectInfo,
            "직접 본 건 아닌데, {ROOM} 쪽에서 쾅 하는 소리는 들었어요. 사고 난 거 아닌가요?",
            "옆 작업실까지 소리가 났어요. {TIME}쯤이었고, {ROOM} 방향이었어요.");
        AddVariations("cat", Q1Anomaly, NoInfo, "제가 있던 데서는 별일 없었어요. 적어도 제가 본 건요.");
        AddVariations("cat", Q2Where, Civilian, "{SELF_ROOM}에 있었어요. 배치받은 일 하고 있었고요.");
        AddVariations("cat", Q3Suspicious, HaveInfo, "{SUSPECT}요. 행동이 좀 이상해서 기억하고 있어요.");
        AddVariations("cat", Q3Suspicious, NoInfo, "못 봤어요. 괜히 아무나 찍고 싶진 않은데요.");
        AddVariations("cat", Q4Opinion, Civilian, "{TARGET}요? 일할 때는 믿을 만한 편이죠.");
        AddVariations("cat", Q5Accuse, Civilian, "저 아니에요. 의심하려면 적어도 근거는 보여주세요.");

        AddVariations("jellyfish", Q1Anomaly, HaveInfo,
            "아, {TIME}쯤 {ROOM}에서요... {INCIDENT}을 봤어요. 아직도 좀 무서워요.",
            "{ROOM}에서 갑자기 문제가 생겼어요. {INCIDENT}... 제가 잘못 본 건 아니에요.");
        AddVariations("jellyfish", Q1Anomaly, IndirectInfo,
            "저, 직접 보진 못했는데요... {ROOM} 쪽에서 엄청 큰 소리가 났어요.",
            "옆쪽에서 뭔가 부서지는 소리가 들렸어요. {ROOM}에서 사고가 난 것 같았어요.");
        AddVariations("jellyfish", Q1Anomaly, NoInfo, "저는 아무것도 못 봤어요... 놓친 게 있는 걸까요?");
        AddVariations("jellyfish", Q2Where, Civilian, "그때는 {SELF_ROOM}에 있었어요. 정말이에요.");
        AddVariations("jellyfish", Q3Suspicious, HaveInfo, "{SUSPECT} 직원이 조금 이상해 보였어요... 제가 예민한 걸 수도 있지만요.");
        AddVariations("jellyfish", Q3Suspicious, NoInfo, "아뇨... 다른 사람을 제대로 볼 여유가 없었어요.");
        AddVariations("jellyfish", Q4Opinion, Civilian, "{TARGET} 직원은... 조금 어렵지만 나쁜 분은 아닌 것 같아요.");
        AddVariations("jellyfish", Q5Accuse, Civilian, "저, 제가요...? 아니에요. 정말 아무것도 안 했어요.");

        AddVariations("rabbit", Q1Anomaly, HaveInfo,
            "네! {TIME}쯤 {ROOM}에서 {INCIDENT}요. 저도 깜짝 놀랐어요.",
            "{ROOM}에서 사고 난 거 봤어요! {INCIDENT} 맞죠? 바로 알려야 한다고 생각했어요.");
        AddVariations("rabbit", Q1Anomaly, IndirectInfo,
            "직접 보진 못했는데, {ROOM} 쪽에서 쾅 소리가 들렸어요! 사고 난 줄 알았어요.",
            "{TIME}쯤 옆방까지 울릴 정도로 소리가 났어요. {ROOM} 쪽이었어요!");
        AddVariations("rabbit", Q1Anomaly, NoInfo, "저는 못 봤어요! 이상했으면 바로 말씀드렸을 거예요.");
        AddVariations("rabbit", Q2Where, Civilian, "그때 {SELF_ROOM}에서 열심히 일하고 있었어요!");
        AddVariations("rabbit", Q3Suspicious, HaveInfo, "{SUSPECT} 직원이 좀 수상했어요. 평소랑 달라 보였거든요!");
        AddVariations("rabbit", Q3Suspicious, NoInfo, "아뇨! 제가 본 사람 중에는 없었어요.");
        AddVariations("rabbit", Q4Opinion, Civilian, "{TARGET} 직원이요? 말은 적어도 자기 일은 잘하는 것 같아요!");
        AddVariations("rabbit", Q5Accuse, Civilian, "네?! 저 정말 아니에요. 확인해보시면 아실 거예요!");

        AddVariations("crow", Q1Anomaly, HaveInfo,
            "{TIME}. {ROOM}. {INCIDENT}을 봤습니다.",
            "{ROOM}에서 사고가 있었습니다. 직접 확인했습니다.");
        AddVariations("crow", Q1Anomaly, IndirectInfo,
            "직접 보진 못했습니다. {ROOM} 쪽에서 큰 소리는 들었습니다.",
            "{TIME}경, 인접 구역에서 충격음. 방향은 {ROOM}이었습니다.");
        AddVariations("crow", Q1Anomaly, NoInfo, "확인한 이상은 없습니다.");
        AddVariations("crow", Q2Where, Civilian, "{SELF_ROOM}. 그곳에 있었습니다.");
        AddVariations("crow", Q3Suspicious, HaveInfo, "{SUSPECT}. 행동이 비정상적이었습니다.");
        AddVariations("crow", Q3Suspicious, NoInfo, "목격하지 못했습니다.");
        AddVariations("crow", Q4Opinion, Civilian, "{TARGET}. 맡은 일은 하는 직원입니다.");
        AddVariations("crow", Q5Accuse, Civilian, "아닙니다. 기록을 확인하십시오.");

        AddVariations("fox", Q1Anomaly, HaveInfo,
            "아, {TIME}쯤 {ROOM}에서 {INCIDENT} 말씀이죠? 그건 저도 똑똑히 봤어요.",
            "{ROOM} 쪽 사고요? {INCIDENT}, 그거였죠. 쉽게 잊을 만한 장면은 아니던데요~");
        AddVariations("fox", Q1Anomaly, IndirectInfo,
            "직접 본 건 아니지만, {ROOM} 쪽에서 꽤 요란한 소리가 들리던데요? 사고였던 것 같은데~",
            "옆 작업실에 있었는데도 들렸어요. {TIME}쯤, {ROOM} 방향에서요.");
        AddVariations("fox", Q1Anomaly, NoInfo, "글쎄요~ 제가 있던 곳은 조용했는데요.");
        AddVariations("fox", Q2Where, Civilian, "그때요? {SELF_ROOM}에서 제 일 하고 있었죠~");
        AddVariations("fox", Q3Suspicious, HaveInfo, "{SUSPECT} 직원이 좀 재미있는 행동을 하더라고요. 평소 같진 않았어요.");
        AddVariations("fox", Q3Suspicious, NoInfo, "딱히요. 애매한 걸로 사람 하나 몰아가긴 싫어서요~");
        AddVariations("fox", Q4Opinion, Civilian, "{TARGET} 직원이요? 속을 읽긴 어렵지만, 그게 꼭 나쁜 건 아니죠.");
        AddVariations("fox", Q5Accuse, Civilian, "저를요? 그럴듯한 이유가 있는지부터 들어보고 싶은데요~");

        // 방해자도 캐릭터 말투를 유지한 채 사실을 축소하거나 논점을 비껴간다.
        AddVariations("owl", Q1Anomaly, Saboteur, "{ROOM}의 {INCIDENT}은 확인했습니다. 다만 제가 본 범위만으로 원인을 단정할 수는 없습니다.");
        AddVariations("owl", Q2Where, Saboteur, "기록상 제 위치는 {SELF_ROOM}입니다. 짧은 이동까지는 따로 적어두지 않았습니다.");
        AddVariations("owl", Q3Suspicious, Saboteur, "확실한 증거 없이 특정 직원을 지목하는 건 원칙에 맞지 않습니다.");
        AddVariations("owl", Q4Opinion, Saboteur, "{TARGET} 직원도 규정 안에서 판단했을 겁니다. 지금 평가하긴 이릅니다.");
        AddVariations("owl", Q5Accuse, Saboteur, "그 판단의 근거를 기록과 함께 제시해주십시오. 추측에는 답하기 어렵습니다.");

        AddVariations("cat", Q1Anomaly, Saboteur, "{ROOM}에서 {INCIDENT}요? 봤죠. 관리가 엉망인데 누가 손댔는지는 어떻게 알아요.");
        AddVariations("cat", Q2Where, Saboteur, "{SELF_ROOM}에 있었어요. 잠깐 움직인 것까지 일일이 설명해야 해요?");
        AddVariations("cat", Q3Suspicious, Saboteur, "다들 비효율적으로 움직이던데요. 그중 하나를 꼭 고르라면 기록부터 보시죠.");
        AddVariations("cat", Q4Opinion, Saboteur, "{TARGET}요? 오늘 동선이 답답하긴 했어요. 제가 신경 쓸 일은 아니지만.");
        AddVariations("cat", Q5Accuse, Saboteur, "잘못 짚으셨어요. 증거도 없이 시간 낭비하지 마세요.");

        AddVariations("jellyfish", Q1Anomaly, Saboteur, "{ROOM}에서 {INCIDENT}... 보긴 했는데요, 너무 놀라서 정확히는 기억이 안 나요.");
        AddVariations("jellyfish", Q2Where, Saboteur, "{SELF_ROOM}에 있었어요... 중간에 어디를 지나갔는지는 정말 잘 모르겠어요.");
        AddVariations("jellyfish", Q3Suspicious, Saboteur, "수상한 사람이라니... 저, 정신이 없어서 누구였는지는 못 봤어요.");
        AddVariations("jellyfish", Q4Opinion, Saboteur, "{TARGET} 직원이요...? 잘 모르겠어요. 제가 함부로 말해도 될까요?");
        AddVariations("jellyfish", Q5Accuse, Saboteur, "저를 의심하시는 거예요...? 너무 무서워서 아무 생각도 안 나요.");

        AddVariations("rabbit", Q1Anomaly, Saboteur, "{ROOM}에서 {INCIDENT}은 봤어요! 저도 원인이 뭔지는 정말 모르겠어요.");
        AddVariations("rabbit", Q2Where, Saboteur, "{SELF_ROOM}에 있었어요! 필요한 일 때문에 잠깐 움직인 건 있었고요.");
        AddVariations("rabbit", Q3Suspicious, Saboteur, "다들 바빠 보였어요! 누가 수상했다고 딱 말하긴 어려워요.");
        AddVariations("rabbit", Q4Opinion, Saboteur, "{TARGET} 직원이요? 오늘 좀 이상하긴 했지만 피곤해서 그랬을 수도 있죠!");
        AddVariations("rabbit", Q5Accuse, Saboteur, "제가요?! 오해예요. 저도 시설을 지키려고 열심히 했어요!");

        AddVariations("crow", Q1Anomaly, Saboteur, "{ROOM}. {INCIDENT}은 확인했습니다. 원인은 불명입니다.");
        AddVariations("crow", Q2Where, Saboteur, "{SELF_ROOM}. 주 이동 기록도 그곳입니다.");
        AddVariations("crow", Q3Suspicious, Saboteur, "판단할 정보가 없습니다.");
        AddVariations("crow", Q4Opinion, Saboteur, "{TARGET}. 평가 보류.");
        AddVariations("crow", Q5Accuse, Saboteur, "근거가 부족합니다.");

        AddVariations("fox", Q1Anomaly, Saboteur, "{ROOM}에서 {INCIDENT} 말씀이죠? 저도 봤지만, 원인까지 아는 건 아니에요~");
        AddVariations("fox", Q2Where, Saboteur, "대부분 {SELF_ROOM}에 있었죠. 잠깐 돌아다닌 게 문제라도 되나요?");
        AddVariations("fox", Q3Suspicious, Saboteur, "수상한 사람이라... 다들 조금씩 이상하던데요? 저만 그런 건 아니잖아요~");
        AddVariations("fox", Q4Opinion, Saboteur, "{TARGET} 직원이요? 오늘따라 바빠 보이긴 했죠. 뭘 했는지는 본인에게 물어보세요~");
        AddVariations("fox", Q5Accuse, Saboteur, "저를 의심하세요? 흥미롭네요~ 어떤 기록을 보고 그러신 건지 궁금한데요.");
    }

    private static string TemplateKey(string employeeId, string questionId, string variant) => $"{employeeId}|{questionId}|{variant}";

    private static string ReplaceVariables(string text, string time, string room, string selfRoom, string target, string suspect, string incident, string detail)
    {
        return text
            .Replace("{TIME}", time)
            .Replace("{ROOM}", room)
            .Replace("{SELF_ROOM}", selfRoom)
            .Replace("{TARGET}", target)
            .Replace("{SUSPECT}", suspect)
            .Replace("{INCIDENT}", incident)
            .Replace("{DETAIL}", detail);
    }

    private static string TimeOf(LogEntry entry)
    {
        float seconds = entry?.GameTimeSeconds ?? GameState.Instance?.DayTimeSeconds ?? 0f;
        float length = Config.Instance?.Data?.DayLengthSeconds ?? 180f;
        int totalMinutes = 22 * 60 + Mathf.FloorToInt(seconds * (360f / Mathf.Max(1f, length)));
        int hour24 = (totalMinutes / 60) % 24;
        int hour12 = hour24 % 12;
        if (hour12 == 0) hour12 = 12;
        string period = hour24 >= 22 ? "밤" : hour24 < 6 ? "새벽" : hour24 < 12 ? "오전" : "오후";
        int minute = totalMinutes % 60;
        return minute == 0 ? $"{period} {hour12}시" : $"{period} {hour12}시 {minute}분";
    }

    private static string RoomName(string roomId)
    {
        // 중앙 제어실은 플레이어만 있는 장소다. 어떤 오래된 로그/상태가 남아 있어도
        // 직원 인터뷰가 이 위치를 언급하지 않게 한다.
        if (roomId == PlayerOnlyRoomId) return "근무 배정 구역";
        if (string.IsNullOrEmpty(roomId)) return "기록상 확인되지 않은 작업실";
        return FacilitySimulation.Instance?.GetRoomDef(roomId)?.DisplayName ?? roomId;
    }

    private static string Codename(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId)) return "다른 직원";
        return FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename ?? employeeId;
    }

    private static string IncidentName(LogEntry entry)
    {
        return entry?.EventType switch
        {
            LogEventType.Sabotage => "하던 작업이 갑자기 꼬였던 거",
            LogEventType.TaskFailed => "기기가 고장 났던 거",
            LogEventType.TabooViolation => "금기 이상 현상이 일어났던 거",
            LogEventType.PowerOutage => "갑자기 전력이 나갔던 거",
            LogEventType.CctvDisconnect => "CCTV 신호가 끊겼던 거",
            LogEventType.Death => "사망 사고가 났던 거",
            _ => "이상한 일이 있었던 거",
        };
    }

    private static string DetailOf(LogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry?.Description)) return "기록된 세부 사항은 없습니다";
        // 🚨는 UTF-16에서 한 글자(char)가 아닌 서로게이트 쌍이다. char 리터럴로 다루면
        // Godot C# 빌드가 CS1012로 멈추므로 문자열을 문자 배열로 변환해 앞 표식만 제거한다.
        return entry.Description.Trim().TrimStart("⚠🚨 ".ToCharArray());
    }
}
