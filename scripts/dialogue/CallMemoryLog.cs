using System.Collections.Generic;
using System.Linq;
using NSP.Core;

namespace NSP.Dialogue;

public enum CallRecordKind
{
    Reported,       // 직원이 먼저 전화해 무언가를 알렸다(사고 · 비명 · 정전 · 수상한 행동)
    OrderedGo,      // 그 통화에서 관리자가 "확인하러 가라"고 했다
    OrderedStay,    // 그 통화에서 관리자가 "그 자리에서 대기하라"고 했다
    Missed,         // 직원이 걸었는데 관리자가 받지 않았다
    ManagerCalled,  // 관리자가 먼저 걸었다(일반 통화)
}

// 통화에서 "실제로 오간 일"의 구조화된 기록.
//
// DialogueHistory 는 화면에 뜬 문장을 그대로 저장한다 — 대사가 그 문장을 다시 읽어
// 뜻을 추측하면 안 된다. 그래서 전화 화면(PhoneCallHud)과 수신 전화 관리자
// (IncomingCallDirector)가 사실이 확정되는 순간 여기에 한 줄씩 남기고,
// 근무 기억(ShiftMemory)은 이 기록만 읽는다.
public sealed class CallRecord
{
    public int Day;
    public float Time;
    public string EmployeeId = "";
    public CallRecordKind Kind;
    // 통화가 다룬 사건의 작업실(없으면 빈 값). 일반 통화면 그때 직원이 있던 방.
    public string RoomId = "";
    public string DialogueEvent = "";
}

public static class CallMemoryLog
{
    private static readonly List<CallRecord> _records = new();
    private static int _version;

    // 근무 기억 캐시가 "새 통화가 생겼는지"를 알 수 있게 한다.
    public static int Version => _version;

    public static IReadOnlyList<CallRecord> All => _records;

    public static IEnumerable<CallRecord> For(string employeeId, int day) =>
        _records.Where(r => r.Day == day && r.EmployeeId == employeeId);

    public static void Record(string employeeId, CallRecordKind kind, string roomId = "", string dialogueEvent = "")
    {
        if (string.IsNullOrEmpty(employeeId)) return;
        _records.Add(new CallRecord
        {
            Day = GameState.Instance?.CurrentDay ?? 1,
            Time = GameState.Instance?.DayTimeSeconds ?? 0f,
            EmployeeId = employeeId,
            Kind = kind,
            RoomId = roomId ?? "",
            DialogueEvent = dialogueEvent ?? "",
        });
        _version++;
    }

    // 검증 씬에서 시각을 직접 지정해 넣을 때.
    public static void RecordAt(int day, float time, string employeeId, CallRecordKind kind,
        string roomId = "", string dialogueEvent = "")
    {
        _records.Add(new CallRecord
        {
            Day = day, Time = time, EmployeeId = employeeId ?? "", Kind = kind,
            RoomId = roomId ?? "", DialogueEvent = dialogueEvent ?? "",
        });
        _version++;
    }

    public static void ResetAll()
    {
        _records.Clear();
        _version++;
    }
}
