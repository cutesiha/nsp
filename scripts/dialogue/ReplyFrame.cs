using System.Collections.Generic;

namespace NSP.Dialogue;

// 증거 기반 심문의 주제. 기본 질문·통화는 ReplyFrame.CustomSlot 으로 자기 슬롯 이름을 직접 쓴다.
public enum ReplyTopic
{
    MoveReason,
    PresenceReason,
    Companion,
    NextLocation,
    ActionThere,
    IncidentKnown,
    WhereAtIncident,
    BeforeIncident,
    WhoSeenNear,
    RouteAround,
    ConfirmTestimony,
    Restate,
    MoodReason,
    MoodBefore,
    MoodRelated,
    ExactTime,
    Confront,
    // 통화 기록 · 이상 개체 조우 · 알리바이/따지기 (질문 확장 — InterviewQuestionFactory 참고)
    CallReason,
    CallAfter,
    Neglect,
    GhostState,
    GhostLook,
    GhostAct,
    GhostOthers,
    AlibiProof,
    Innocence,
    Suspect,
    PressPresence,
    // 2단계 — 이동 / 목격 질문 트리
    ArrivalState,      // 도착했을 때 현장이 어땠는가
    VisitPurpose,      // 그 직원을 만나러 간 이유
    SeenPersonAction,  // 그 사람은 무엇을 하고 있었는가
    SeenScope,         // 어디까지 봤는가(조작 장면까지 봤는가)
    WhySuspicious,     // 왜 수상하다고 생각했는가
    // 2단계 — 설비 사고 / 방해공작
    EquipmentFault,    // 사고 전에 설비에 이상이 있었는가
    InspectionWork,    // 어떤 점검 / 수리를 했는가
    EquipmentTouch,    // 설비를 만지거나 설정을 바꿨는가
    WhoReported,       // 누구에게 먼저 알렸는가
    TouchDetail,       // 어느 부분을 만졌는가
    RepairConfirm,     // 수리 후 정상 작동을 확인했는가
    // 2단계 — 정전
    BlackoutExperience, BlackoutSigns, AfterBlackoutMet, HeardFromWhom,
    // 2단계 — 기절 / 구조
    LastMemory, WokeWhere, FoundWhere, FoundCondition, RescueAction,
    // 2단계 — 이상 개체
    AnomalySeenHow, AnomalyDirection,
    // 3단계 — 발언 추궁
    AboutTestimony, ObservationWindow, NotObservedScope,
    Unknown,
}

// 답변 한 번이 담을 의미 전부. 여기까지 정해지면 문장은 "어느 틀로 말하느냐"만 남는다.
//
// Local Dialogue V2 — 인터뷰 · 기본 질문 · 전화가 모두 이 프레임 하나로 DialogueComposer 에 간다.
//   핵심(슬롯 + 변수)          반드시 말한다
//   보정(Caveats)              사실 정확성 때문에 빠지면 안 되는 문장(직접 보지는 못했다 등)
//   근무 기억(Addenda)         그 시각 근처에 실제로 겪은 일 1~2개 — ShiftMemory 가 고른다
//   여는 말 / 덧붙임 / 되묻기   말투에 따른 선택 요소(문장 수 상한에 먼저 걸려 빠진다)
public sealed class ReplyFrame
{
    public string EmployeeId = "";
    public ReplyTopic Topic = ReplyTopic.Unknown;
    // 같은 주제 안에서 갈리는 결. 예: Companion 의 with / alone
    public string Variant = "";
    public readonly Dictionary<string, string> Vars = new();

    // 기본 질문·통화처럼 ReplyTopic 으로 표현되지 않는 답은 슬롯 이름을 직접 준다("selfloc" 등).
    public string CustomSlot = "";
    // 핵심 슬롯에 쓸 문장이 하나도 없을 때 대신 쓸 슬롯.
    public string FallbackSlot = "";

    public string Slot => !string.IsNullOrEmpty(CustomSlot)
        ? CustomSlot
        : Topic + (string.IsNullOrEmpty(Variant) ? "" : "." + Variant);

    public readonly List<string> Caveats = new();
    public readonly List<ReplyAddendum> Addenda = new();

    // 핵심 앞에 붙는 말 — 둘 중 하나만 쓴다. 이미 완성된 문장이면 OpenerText.
    public string OpenerText = "";
    public string OpenerSlot = "";
    public string ExtraSlot = "";
    public string BackSlot = "";
    // 핵심 문장 앞에 붙일 시간 표현("그때", "22시 10분쯤"). 핵심이 이미 시점을 품으면 붙지 않는다.
    public string TimeWord = "";

    // 0 이면 말투 설정(DialogueVoiceDef.MaxSentences)을 따른다.
    public int MaxSentences;
    // -1 이면 말투 설정을 따른다.
    public int MaxExclamations = -1;

    // ── 이 답변이 실제로 입에 올린 것 ────────────────────────────────
    //
    // 꼬리질문은 **방금 답변에 실제로 나온 인물 · 방 · 행동** 만 근거로 삼는다.
    // 질문 의도만 보고 고정 목록을 펼치면, 말한 적 없는 사람에 대해 "그 사람은 뭘
    // 하고 있었습니까" 를 묻게 된다(기획안 §9 꼬리질문 규칙).
    public string MentionedEmployeeId = "";
    public string MentionedRoomId = "";
    // 그 사람이 무엇을 하고 있었는지까지 말했는가. 비어 있으면 "있는 것만 봤다" 는 뜻이다.
    public string MentionedDetail = "";
    // 모른다 / 기억나지 않는다고 답했다. 같은 것을 다시 캐묻지 않기 위한 표시.
    public bool SaidDontKnow;

    public ReplyFrame Set(string key, string value)
    {
        Vars[key] = value ?? "";
        return this;
    }
}

// 근무 기억에서 골라 답변 뒤에 덧붙이는 한 문장.
public sealed class ReplyAddendum
{
    public string Slot = "";
    public MemoryKind Kind;
    public readonly Dictionary<string, string> Vars = new();
    // 한 세션에 한 번만 할 이야기의 키(동료 기억). 비어 있으면 몇 번이든 말할 수 있다.
    public string SpokenKey = "";

    public ReplyAddendum Set(string key, string value)
    {
        Vars[key] = value ?? "";
        return this;
    }
}
