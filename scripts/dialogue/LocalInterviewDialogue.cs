using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Dialogue;

// DAY1 휴게시간용 로컬 인터뷰.
// 대사 문장은 docs/휴게시간_대사목록.md에서 그대로 읽고, 이 클래스는 현재 게임의
// 사실(로그·목격자·위치·런타임 방해자)을 문장 변수에 넣는 역할만 한다.
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
    private const string NoInfo = "CIVILIAN_NO_INFO";
    private const int MaxHistoryTurns = 6;

    public sealed class Question
    {
        public string Id = "";
    }

    private sealed class InterviewContext
    {
        public LogEntry DirectIncident;
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

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal);
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

        var relevant = questionId == Q3Suspicious ? context.DirectSuspiciousAction : context.DirectIncident;
        string targetId = context.OpinionTargetId;
        return ReplaceVariables(template,
            TimeOf(relevant),
            RoomName(relevant?.RoomId),
            context.SelfRoom,
            Codename(targetId),
            Codename(context.DirectSuspiciousAction?.ActorEmployeeId),
            IncidentName(context.DirectIncident),
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
            return;
        }

        using var file = FileAccess.Open(DataPath, FileAccess.ModeFlags.Read);
        if (file == null) return;

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
                    Templates[TemplateKey(characterId, questionId, pendingVariant)] = text;
                pendingVariant = "";
            }
        }
    }

    private static string SelectVariant(string questionId, bool isSaboteur, InterviewContext context)
    {
        if (isSaboteur) return Saboteur;
        return questionId switch
        {
            Q1Anomaly => context.DirectIncident != null ? HaveInfo : NoInfo,
            Q3Suspicious => context.DirectSuspiciousAction != null ? HaveInfo : NoInfo,
            _ => Civilian,
        };
    }

    private static InterviewContext BuildContext(string employeeId)
    {
        var context = new InterviewContext
        {
            DirectIncident = FindDirectIncident(employeeId),
            DirectSuspiciousAction = FindDirectSuspiciousAction(employeeId),
            SelfRoom = FindSelfRoom(employeeId),
            OpinionTargetId = FindOpinionTarget(employeeId),
        };
        return context;
    }

    // 목격자 목록에 본인이 명시된 로그만 "직접 본" 사실로 취급한다.
    private static LogEntry FindDirectIncident(string employeeId)
    {
        return KnownEntries(employeeId)
            .Where(e => e.WitnessEmployeeIds.Contains(employeeId) && IsIncident(e.EventType))
            .OrderByDescending(e => e.GameTimeSeconds)
            .FirstOrDefault();
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
        var direct = FindDirectSuspiciousAction(employeeId);
        if (!string.IsNullOrEmpty(direct?.ActorEmployeeId)) return direct.ActorEmployeeId;

        var sim = FacilitySimulation.Instance;
        if (sim == null) return "";
        return sim.GetEmployeeIds()
            .Where(id => id != employeeId && sim.GetEmployeeState(id)?.Alive == true)
            .OrderBy(id => id, StringComparer.Ordinal)
            .FirstOrDefault() ?? "";
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
        return Templates.GetValueOrDefault(TemplateKey(employeeId, questionId, variant), "");
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
        return $"{(totalMinutes / 60) % 24:00}:{totalMinutes % 60:00}";
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
            LogEventType.Sabotage => "업무 진행 이상",
            LogEventType.TaskFailed => "설비 고장",
            LogEventType.TabooViolation => "금기 위반",
            LogEventType.PowerOutage => "정전",
            LogEventType.CctvDisconnect => "감시 시스템 이상",
            LogEventType.Death => "사망 사고",
            _ => "이상 현상",
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
