using System.Collections.Generic;
using NSP.Facility;

namespace NSP.Dialogue;

// DAY1 휴게시간 인터뷰의 어댑터.
//
// 대사 생성은 전부 LocalDialogueGenerator(규칙 기반 로컬 파이프라인)로 옮겼다.
// 이 클래스는 기존 호출부(PhoneCallHud / Phone3D)가 쓰던 공개 API 를 그대로 유지하기 위해 남는다.
// 예전의 "완성 문장 템플릿 + 변수 치환" 풀은 더 이상 사용하지 않는다 —
// docs/휴게시간_대사목록.md 는 캐릭터 말투 참고 자료로만 남겨 둔다.
public static class LocalInterviewDialogue
{
    public const string EventDay1Interview = LocalDialogueGenerator.EventDay1Interview;
    public const string Q1Anomaly = DialogueQuestions.Anomaly;
    public const string Q2Where = DialogueQuestions.Where;
    public const string Q3Suspicious = DialogueQuestions.Suspicious;
    public const string Q4Opinion = DialogueQuestions.Opinion;
    public const string Q5Accuse = DialogueQuestions.Accuse;

    private const int MaxHistoryTurns = 6;

    public sealed class Question
    {
        public string Id = "";
    }

    private static readonly Question[] Day1Questions =
    {
        new() { Id = Q1Anomaly },
        new() { Id = Q2Where },
        new() { Id = Q3Suspicious },
        new() { Id = Q4Opinion },
        new() { Id = Q5Accuse },
    };

    public static IReadOnlyList<Question> Questions => Day1Questions;

    public static string InterviewGreeting(string employeeId) =>
        LocalDialogueGenerator.InterviewGreeting(employeeId);

    public static string GetQuestionText(string employeeId, string questionId) => questionId switch
    {
        Q1Anomaly => "오늘 근무 중 이상한 점은 없었습니까?",
        Q2Where => "사고 당시 어디에 있었습니까?",
        Q3Suspicious => "수상한 행동을 한 직원을 봤습니까?",
        Q4Opinion => $"{Codename(LocalDialogueGenerator.OpinionTargetId(employeeId))} 직원을 어떻게 생각합니까?",
        Q5Accuse => "현재 당신을 의심하고 있습니다.",
        _ => "질문을 선택하십시오.",
    };

    public static string Answer(string employeeId, string questionId) =>
        LocalDialogueGenerator.InterviewAnswer(employeeId, questionId);

    // 직원별 단기 기억. 같은 질문을 다시 받았는지는 DialogueClaimState 가 따로 관리하고,
    // 여기는 화면 밖 흐름(디버그/후속 기능)에서 읽을 수 있는 원문 기록으로 남는다.
    public static void RecordTurn(string employeeId, string playerText, string reply)
    {
        var state = FacilitySimulation.Instance?.GetEmployeeState(employeeId);
        if (state == null) return;
        state.ConversationHistory.Add(new ConversationTurn { Role = "player", Text = playerText });
        state.ConversationHistory.Add(new ConversationTurn { Role = "npc", Text = reply });
        while (state.ConversationHistory.Count > MaxHistoryTurns)
            state.ConversationHistory.RemoveAt(0);
    }

    private static string Codename(string employeeId)
    {
        if (string.IsNullOrEmpty(employeeId)) return "다른";
        return FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Codename ?? employeeId;
    }
}
