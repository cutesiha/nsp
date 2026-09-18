namespace NSP.Dialogue;

// 꼬리질문의 "의도". 캐릭터별 고정 꼬리질문 목록을 만들지 않고, 상황에 맞는 의도를 고른 뒤
// 그 의도의 표현 풀에서 플레이어 질문 문장을 뽑는다.
public enum FollowUpIntent
{
    None,

    // ── 일반 추가 질문(증거 없이도 가능) ──────────────────────────────
    AskExactLocation,       // 정확히 어디였는지
    AskPreviousLocation,    // 그 전에는 어디에 있었는지
    AskNextAction,          // 그 뒤에 무엇을 했는지
    AskWhoWasPresent,       // 당시 같이 있던 사람
    AskWitness,             // 위치를 확인해 줄 사람이 있는지
    AskDetails,             // 구체적으로 어떤 상황이었는지
    AskCertainty,           // 확신하는지
    AskReason,              // 그렇게 판단한 이유
    AskWhatWasHeard,        // 어떤 소리를 들었는지
    AskWhatWasSeen,         // 직접 본 것인지
    AskTodayDifference,     // 오늘도 평소와 같았는지
    AskSuspicion,           // 수상하다고 느낀 적이 있는지
    AskDefense,             // 의심을 풀 설명이 있는지
    AskRouteAgain,          // 동선을 다시 설명
    AskReasonForMovement,   // 왜 그 방으로 이동했는지

    // ── 추궁(플레이어가 실제로 확보한 증거가 있을 때만) ────────────────
    ChallengeLocation,          // 다른 곳에 있었다는 기록
    ChallengeTime,              // 시간 진술이 맞지 않음
    ChallengeWitness,           // 다른 직원의 증언과 다름
    ChallengeOmission,          // 아까 말하지 않은 이동
    ChallengeContradiction,     // 앞뒤 진술이 다름
    ChallengeSuspiciousMovement,// 지시 없이 움직인 기록
    ChallengeClaimConsistency,  // 앞서 말한 위치와 지금 설명이 다름
}

// 화면에 띄울 꼬리질문 후보 하나.
public sealed class FollowUpQuestion
{
    public FollowUpIntent Intent;
    public string Text = "";
    // 어느 기본 질문의 답변에서 나온 꼬리질문인가. 같은 의도라도 Q1(사고)과 Q3(목격)에서
    // 물어보는 대상이 달라 답변 기준이 바뀐다.
    public string BaseQuestionId = "";
    // 이 인터뷰가 다루는 사건. 꼬리질문도 반드시 같은 사건을 기준으로 답한다.
    public string SubjectIncidentKey = "";
    // 질문이 가리키는 다른 직원(목격자/지목 대상). 없으면 빈 값.
    public string TargetEmployeeId = "";
    // 추궁의 근거가 된 장소(플레이어가 실제로 아는 것만 들어온다).
    public string EvidenceRoomId = "";
    public int Priority;
    public bool IsChallenge;
    // 왜 이 후보가 만들어졌는지 — 개발용 로그에만 쓴다.
    public string Reason = "";
}
