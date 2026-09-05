namespace NSP.Dialogue;

// 대사 생성기가 처리하는 "질문 종류" 식별자.
// 인터뷰 5종은 기존 LocalInterviewDialogue 의 상수와 문자열이 같아야 한다(저장된 흐름 호환).
public static class DialogueQuestions
{
    public const string Anomaly = "Q1_ANOMALY";
    public const string Where = "Q2_WHERE";
    public const string Suspicious = "Q3_SUSPICIOUS";
    public const string Opinion = "Q4_OPINION";
    public const string Accuse = "Q5_ACCUSE";

    // 플레이어가 거는 일반 통화(질문 순서는 docs/NSP_DIALOGUE_RUNTIME.md 와 같다).
    public const string GeneralStatus = "GEN_STATUS";
    public const string GeneralFocus = "GEN_FOCUS";
    public const string GeneralAnomaly = "GEN_ANOMALY";

    // 직원이 거는 수신 전화.
    public const string IncidentReport = "CALL_REPORT";
    public const string DispatchAccept = "CALL_ACCEPT";
    public const string DispatchDecline = "CALL_DECLINE";
}
