using NSP.Data;

namespace NSP.Dialogue;

// 플레이어가 자료를 고른 뒤 실제로 던지는 질문의 "의도".
// 자동 추궁은 여기에 없다 — 모순 제시는 플레이어가 자료 두 장을 직접 골라야만 생긴다.
public enum InterviewIntent
{
    None,

    // ── 이동 기록 ────────────────────────────────────────────────────
    AskMoveReason,          // 왜 그 방으로 이동했는가
    AskWhoWasPresent,       // 그때 같이 있던 직원
    AskNextLocation,        // 그 뒤에 어디로 갔는가
    AskActionAtDestination, // 도착해서 무엇을 했는가

    // ── 사고 기록 ────────────────────────────────────────────────────
    AskIncidentKnown,       // 이 사고를 알고 있었는가
    AskWhereAtIncident,     // 그 시각 어디에 있었는가
    AskBeforeIncident,      // 사고 직전에 무엇을 했는가
    AskWhoSeenNear,         // 그 무렵 누구를 봤는가

    // ── CCTV ────────────────────────────────────────────────────────
    AskPresenceReason,      // 그 시각 그 방에 있던 이유
    AskRouteAround,         // 그 앞뒤의 이동 경로

    // ── 증언 / 이전 진술 ──────────────────────────────────────────────
    AskConfirmTestimony,    // 다른 직원의 증언이 사실인가
    AskRestate,             // 그 진술을 다시 설명해 달라

    // ── 오늘의 기분 ──────────────────────────────────────────────────
    AskMoodReason,          // 왜 그런 기분이었는가
    AskMoodBefore,          // 근무 전부터 그랬는가
    AskMoodRelated,         // 특정 직원·사건과 관련 있는가

    // ── 증거 없이 물을 수 있는 기본 질문 ────────────────────────────────
    BasicShift,             // 오늘 근무는 어땠는가
    BasicAnomaly,           // 오늘 이상한 점을 느꼈는가
    BasicSuspicious,        // 수상한 행동을 한 사람을 봤는가

    // ── 중립 꼬리질문 ────────────────────────────────────────────────
    // (FollowExactTime 은 더 이상 만들지 않는다 — 시각은 기록에 이미 있다. 열거값만 호환용으로 남긴다.)
    FollowExactTime,        // 정확히 몇 시였는가

    // ── 플레이어가 자료 두 장으로 직접 제시하는 모순 ─────────────────────
    Confront,

    // ── 통화 기록 ────────────────────────────────────────────────────
    AskCallReason,          // 그 시각 왜 전화했는가(잡담 · 신고 · 못 받은 전화 · 관리자가 건 전화)
    AskCallAfter,           // 통화를 끝낸 뒤 무엇을 했는가
    ConfrontNeglect,        // 놀러 가고 싶다는 전화가 잦다 — 근무 태만 아닌가

    // ── 이상 개체 조우 ────────────────────────────────────────────────
    AskGhostWellbeing,      // 괜찮은가 · 지금 상태는
    AskGhostAppearance,     // 어떻게 생겼는가
    AskGhostWhatHappened,   // 나타났을 때 무엇을 했는가
    AskGhostOthers,         // 같이 있던 사람은 어떻게 했는가

    // ── 알리바이 · 따지기(사고 기록) ──────────────────────────────────
    AskAlibiProof,          // 그 시각 위치를 누가 증명해 주는가
    AskProveInnocence,      // 이 사고와 무관하다는 근거를 대라
    AskSuspectOpinion,      // 누가 그랬다고 생각하는가
    PressPresence,          // 기록상 그 방에 있었다 — 설명하라(관리자가 실제로 아는 재석만)

    // ── 이동 기록 따지기 ──────────────────────────────────────────────
    ConfrontUnorderedMove,  // 지시 없이 이동했다 — 근무지 이탈 아닌가
}

// 질문 한 건이 들고 다니는 맥락 전부.
//
// 예전 꼬리질문의 가장 큰 문제는 "어느 사건, 몇 시, 어느 방" 이 질문에 붙어 있지 않아
// 서로 다른 사건의 값이 섞였다는 점이다. 질문은 반드시 이 객체로만 만들어지고,
// 답변 생성기도 이 값만 본다.
public sealed class InterviewQuestion
{
    public string TargetEmployeeId = "";
    public InterviewIntent Intent = InterviewIntent.None;

    // 이 질문의 근거가 된 자료. 기본 질문이면 비어 있다.
    public string EvidenceId = "";
    // 모순 추궁일 때의 두 번째 자료.
    public string SecondEvidenceId = "";

    public string IncidentKey = "";
    public LogEventType IncidentType;

    public float AnchorTime;
    public bool HasAnchorTime;

    public string FromRoomId = "";
    public string ToRoomId = "";
    public string SubjectRoomId = "";

    // 이 질문이 언급하는 다른 직원(증언자 등).
    public string OtherEmployeeId = "";
    // 이동 기록 질문일 때, 그 이동이 관리자의 지시였는가.
    public bool PlayerOrderedMove;
    public string MoodText = "";

    // 통화 기록 질문일 때 — 통화 종류 · 누가 걸었나 · 잡담 전화가 말한 작업실 · 같은 종류의 통화 횟수.
    public string CallEvent = "";
    public CallRecordKind CallKind;
    public string CallRoomId = "";
    public int RepeatIndex;

    // 화면에 뜨는 질문 문장.
    public string Text = "";

    // 묻는 것이 아니라 따지는 질문 — 화면이 붉게 칠해 구분한다(모순 제시 버튼과 같은 색).
    public bool IsConfront => Intent is InterviewIntent.ConfrontNeglect or InterviewIntent.PressPresence
        or InterviewIntent.ConfrontUnorderedMove or InterviewIntent.AskProveInnocence;

    // 같은 자료·같은 의도를 다시 묻지 않게 하는 키.
    public string Key => $"{Intent}|{EvidenceId}|{SecondEvidenceId}";
}
