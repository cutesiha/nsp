using NSP.Data;

namespace NSP.Dialogue;

// 이 대답에서 "무엇을 말할 것인가". 문장은 여기 없다 — 문장은 KoreanDialogueComposer 가 만든다.
public enum CoreKind
{
    None,
    SelfLocation,       // 사건 당시 내 위치
    IncidentDirect,     // 직접 본 사건
    IncidentIndirect,   // 벽 너머로 알게 된 사건(소리·진동)
    NoAnomaly,          // 아는 이상 없음
    SuspiciousSighting, // 실제로 목격한 다른 직원의 행동
    NoSighting,         // 목격 없음
    Opinion,            // 다른 직원 평가
    DenyAccusation,     // 의심에 대한 대응
    StatusReport,       // 지금 근무가 어떤가(일반 통화)
    Comply,             // 지시 수용(일반 통화)
    IncidentReport,     // 사고 신고(수신 전화 첫 대사)
    DispatchAccept,     // "확인하러 가라" 수락
    DispatchDecline,    // "대기하라" 수용
}

// 정확한 시각은 기본적으로 말하지 않는다. 필요할 때만 Vague/Exact 로 올린다.
public enum TimeRef { None, Vague, Exact }

public enum Certainty { High, Medium, Low }

public enum EmotionKind { None, Alarm, Fear, Annoyance, Amused, Composed }

// 방해자의 응답 전략. 매번 새로 뽑지 않고 사건 단위로 고정된다(DialogueClaimState).
public enum DeceptionMode
{
    None,
    Truth,      // 진실을 말해도 불리하지 않다
    Omit,       // 불리한 부분만 생략
    Minimize,   // 인정하되 대수롭지 않게
    Justify,    // 정상 업무였다고 설명
    Vague,      // 시간·경로를 흐린다
    Redirect,   // 실제로 아는 다른 사건/직원으로 시선 이동
    Deny,       // 정면 부정 — 가장 위험할 때만
}

public sealed class DialogueResponsePlan
{
    public CoreKind Core = CoreKind.None;
    // 핵심 답변 뒤에 반드시 붙어야 하는 보정 문장(예: 직접 보지는 못했다). 생략 대상이 아니다.
    public bool NeedsIndirectCaveat;
    // 방해자가 원인을 아는 척하지 않기 위한 보정.
    public bool NeedsUnknownCauseCaveat;

    public string RoomId = "";          // 핵심 답변에 등장하는 작업실
    public string IncidentRoomId = "";
    public LogEventType IncidentType;
    public KnowledgeLevel Knowledge = KnowledgeLevel.None;
    public float IncidentTimeSeconds;

    public string SubjectEmployeeId = "";  // 지목 대상(실제 목격 기록이 있을 때만 채운다)
    public string TargetEmployeeId = "";   // Q4 평가 대상

    public TimeRef Time = TimeRef.None;
    public Certainty Certainty = Certainty.High;
    public EmotionKind Emotion = EmotionKind.None;
    public DeceptionMode Deception = DeceptionMode.None;

    public bool MentionTask;
    public bool AllowSupport = true;
    public bool IsRepeat;
    public int EvidenceCount;

    // StatusReport 세부: ok / blocked / repair / stress / moving / idle
    public string StatusNote = "ok";
}
