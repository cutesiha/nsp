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

    public ReplyAddendum Set(string key, string value)
    {
        Vars[key] = value ?? "";
        return this;
    }
}
