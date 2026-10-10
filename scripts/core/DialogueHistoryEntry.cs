namespace NSP.Core;

public enum DialogueEntryType
{
    NpcLine,
    PlayerChoice,
    NpcResponse,
}

public enum DialogueConversationType
{
    IncomingCall,
    OutgoingCall,
    Interview,
}

// DAY1 대화 기록의 원본 데이터. UI 문구를 역으로 읽지 않고 실제 대사가 확정되는
// 시점에 PhoneCallHud가 이 구조로 저장한다.
public sealed class DialogueHistoryEntry
{
    public int Day;
    public float Timestamp;
    // 기록된 순서. 화면은 이 순서를 절대 바꾸지 않는다(정렬 금지 — 주고받은 차례가 곧 의미다).
    public int Seq;
    public string SpeakerId = "";
    // 이 줄이 속한 통화의 상대 직원. 관리자의 말도 이 값으로 그 직원의 대화에 묶인다.
    public string CounterpartId = "";
    public string SpeakerDisplayName = "";
    public DialogueEntryType EntryType;
    public string Text = "";
    public DialogueConversationType ConversationType;
    // 이 말이 어떤 조사 자료를 들고 한 질문(과 그 대답)인가 — 휴게시간 심문에서만 채워진다.
    // 추궁처럼 두 장을 함께 들이밀면 두 Id 가 다 들어간다. 관리자 패드의 「관련 진술」이 쓴다.
    public System.Collections.Generic.List<string> EvidenceIds = new();

    public bool Mentions(string evidenceId) =>
        !string.IsNullOrEmpty(evidenceId) && EvidenceIds.Contains(evidenceId);
}
