using NSP.Core;
using NSP.Data;

namespace NSP.Dialogue;

// 한 직원이 어떤 사실을 "어느 정도까지" 알고 있는가. 값이 클수록 많이 안다(Direct > Indirect > Later > None).
//   Direct   — 그 방에 있었거나 목격자로 기록됨. 원인·장면까지 말할 수 있다.
//   Indirect — 통로로 연결된 옆 작업실에 있었다. 소리·진동·냄새까지만 말할 수 있다.
//   Later    — 그 순간에는 다른 방에 있었지만, 그 뒤에 그 방으로 가거나(재배치·지시) 그 고장을
//              수리하면서 사고를 알게 됐다. 사고가 있었다는 것과 자기가 한 일까지만 말할 수 있다 —
//              그 순간의 장면·소리는 말할 수 없다(보지도 듣지도 못했다).
//   None     — 모른다. 어떤 세부도 말해서는 안 된다.
public enum KnowledgeLevel
{
    None,
    Later,
    Indirect,
    Direct,
}

// 게임 로그 한 줄을 대사 생성이 쓸 수 있는 형태로 감싼 것.
// Description 원문(내부 표기·이모지 포함)은 여기서 밖으로 나가지 않는다 —
// 대사는 항상 EventType/RoomId 로부터 사람이 말하는 표현으로 다시 만든다.
public sealed class DialogueFact
{
    public LogEntry Entry;
    public LogEventType Type;
    public string RoomId = "";
    public string ActorEmployeeId = "";
    public float TimeSeconds;
    public KnowledgeLevel Knowledge = KnowledgeLevel.None;

    // 같은 사건을 여러 질문·여러 통화에서 동일하게 가리키기 위한 키.
    public string Key => $"{Type}:{RoomId}:{TimeSeconds:0.0}";

    public static DialogueFact From(LogEntry e, KnowledgeLevel knowledge) => e == null ? null : new DialogueFact
    {
        Entry = e,
        Type = e.EventType,
        RoomId = e.RoomId ?? "",
        ActorEmployeeId = e.ActorEmployeeId ?? "",
        TimeSeconds = e.GameTimeSeconds,
        Knowledge = knowledge,
    };
}
