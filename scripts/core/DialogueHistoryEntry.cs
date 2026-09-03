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
    public string SpeakerId = "";
    public string SpeakerDisplayName = "";
    public DialogueEntryType EntryType;
    public string Text = "";
    public DialogueConversationType ConversationType;
}
