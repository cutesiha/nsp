using System.Collections.Generic;
using NSP.Data;

namespace NSP.Dialogue;

public enum DialogueConversationKind
{
    Interview,
    IncomingCall,
    OutgoingCall,
}

// 대사 한 줄을 만들 때 필요한 "게임의 사실" 전부. 여기 없는 정보로는 말하지 않는다.
// 모든 값은 DialogueContextBuilder 가 실제 게임 상태/로그에서만 채운다.
public sealed class DialogueContext
{
    public string EmployeeId = "";
    public bool IsSaboteur;
    public DialogueConversationKind Conversation = DialogueConversationKind.Interview;
    public string QuestionId = "";
    // 꼬리질문일 때, 이 질문이 나온 기본 질문(Q1~Q5). 그 외에는 빈 값.
    public string BaseQuestionId = "";
    public string EventId = "";

    public int CurrentDay = 1;
    public float CurrentGameTime;

    public string CurrentRoomId = "";
    public string AssignedRoomId = "";
    public float Stress = 1f;
    public string StressBand = "";
    public bool Incapacitated;
    public bool Isolated;
    public bool IsMoving;

    // --- 이번 대화가 다루는 하나의 사건 ---------------------------------
    // 한 인터뷰 안에서는 모든 질문이 같은 사건을 기준으로 답한다.
    public DialogueFact Subject;
    // 사건이 일어난 그 시각에 이 직원이 실제로 있던 작업실(마지막 위치가 아니다).
    public string RoomAtSubject = "";
    public KnowledgeLevel SubjectKnowledge = KnowledgeLevel.None;
    // 이 직원이 그 사건을 일으킨 당사자인가(방해공작 로그 기준).
    public bool IsSubjectActor;

    // --- 다른 직원에 대해 실제로 목격한 것 -------------------------------
    public DialogueFact KnownSuspicious;
    public string KnownSuspiciousActorId = "";
    // 오늘 이 직원이 눈으로 본 다른 직원 목록(Q3 지목/REDIRECT 후보의 상한).
    public readonly List<string> SeenEmployeeIds = new();

    // Q4 대상.
    public string TargetEmployeeId = "";

    // --- 현재 근무 상태(일반 통화용) ------------------------------------
    public string CurrentTaskName = "";
    public bool HasActiveTask;
    public bool RoomBlockedByMaterials;
    public bool RoomUnderRepair;
    public bool RoomCctvBlocked;
    public bool FacilityBlackout;

    // --- 반복 질문 ------------------------------------------------------
    public bool IsRepeat;
    public int AskCount;

    // 이 직원에게 실제로 불리한 기록이 몇 건이나 있는가(방해자의 위기감 판단용).
    public int EvidenceAgainstCount;
}
