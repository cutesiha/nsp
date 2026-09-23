using System.Collections.Generic;

namespace NSP.Dialogue;

// 한 직원이 "한 사건"에 대해 이미 무엇을 주장했는지 기억한다.
// 질문할 때마다 알리바이가 바뀌는 것을 막는 장치이며, 방해자의 거짓말 일관성은 전적으로
// 이 구조가 보장한다. 표현(문장)은 매번 달라져도 여기에 저장된 주장 자체는 바뀌지 않는다.
public sealed class DialogueClaim
{
    public string EmployeeId = "";
    public int Day;
    public string IncidentKey = "";

    // 사건 당시 위치로 "말한" 작업실. 실제 위치(DialogueContext.RoomAtSubject)와 다를 수 있다.
    public string ClaimedRoomId = "";
    public bool ClaimTruthful = true;
    // 이 사건에 대한 방해자의 전략. 한 번 정해지면 이 사건 동안 바뀌지 않는다.
    public DeceptionMode Mode = DeceptionMode.None;
    public bool ModeDecided;
    // 이미 언급한 다른 직원(REDIRECT). 두 번째 질문에서 다른 사람으로 갈아타지 않는다.
    public string MentionedSuspectId = "";

    // "설비 근처에는 가지 않았다" — 결번자가 이 사건에 대해 대는 단 하나의 반박 가능한 거짓말.
    // 알리바이와 똑같은 규칙으로 다룬다: 한 번 정하면 그 사건 동안 바뀌지 않는다(Mode/ModeDecided).
    // 물을 때마다 말이 달라지면 "기록과 맞대어 본다"는 이 게임의 추리가 성립하지 않는다.
    public bool DeniesEquipmentContact;
    public bool EquipmentDenialDecided;

    public readonly Dictionary<string, int> AskCounts = new();

    // 증가시키지 않고 지금까지 몇 번 물었는지만 본다.
    public int Asked(string questionId) => AskCounts.GetValueOrDefault(questionId, 0);

    public int Ask(string questionId)
    {
        int before = AskCounts.GetValueOrDefault(questionId, 0);
        AskCounts[questionId] = before + 1;
        return before;
    }
}

public static class DialogueClaimState
{
    private const int RecentSurfaceMemory = 5;

    private static readonly Dictionary<string, DialogueClaim> _claims = new();
    // 직전에 내보낸 문장들(직원별). 같은 문장이 연달아 나오는 것을 막는 데만 쓴다.
    private static readonly Dictionary<string, List<string>> _recent = new();

    private static string Key(string employeeId, int day, string incidentKey) =>
        $"{employeeId}|{day}|{incidentKey}";

    public static DialogueClaim Get(string employeeId, int day, string incidentKey)
    {
        string k = Key(employeeId, day, incidentKey ?? "");
        if (_claims.TryGetValue(k, out var claim)) return claim;
        claim = new DialogueClaim { EmployeeId = employeeId, Day = day, IncidentKey = incidentKey ?? "" };
        _claims[k] = claim;
        return claim;
    }

    public static bool WasRecent(string employeeId, string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return _recent.TryGetValue(employeeId, out var list) && list.Contains(text);
    }

    public static void Remember(string employeeId, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (!_recent.TryGetValue(employeeId, out var list))
            _recent[employeeId] = list = new List<string>();
        list.Add(text);
        while (list.Count > RecentSurfaceMemory) list.RemoveAt(0);
    }

    // 새 근무(또는 새 게임)가 시작되면 지난 주장은 의미가 없다.
    public static void ResetAll()
    {
        _claims.Clear();
        _recent.Clear();
        PlayerKnownEvidence.ResetAll();
        InterviewReplyPlanner.Reset();
        DialoguePatternMemory.ResetAll();
        DialogueContextBuilder.Invalidate();
        // 지난 근무의 통화·근무 기억은 새 근무로 넘어오지 않는다(기억은 날짜로도 걸러진다).
        CallMemoryLog.ResetAll();
        ShiftMemory.Invalidate();
    }
}
