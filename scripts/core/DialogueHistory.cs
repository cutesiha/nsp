using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace NSP.Core;

// 현재 프로토타입의 DAY1 전용 대화 기록 저장소. 같은 화자 + 같은 종류 + 같은 문장은
// 공백과 개행을 정규화한 뒤 한 번만 저장한다.
public partial class DialogueHistory : Node
{
    [Signal] public delegate void EntryAddedEventHandler();
    [Signal] public delegate void ClearedEventHandler();

    public static DialogueHistory Instance { get; private set; }

    private readonly List<DialogueHistoryEntry> _entries = new();
    private int _seq;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public bool AddEntry(string speakerId, string speakerDisplayName, DialogueEntryType entryType,
        string text, DialogueConversationType conversationType, string counterpartId = "")
    {
        // DAY2 이후 저장/탭은 이번 프로토타입 범위가 아니다.
        // DAY0(교육)은 기록한다 — 튜토리얼에서 대화 기록 보는 법을 가르치기 때문이다.
        int day = GameState.Instance?.CurrentDay ?? 1;
        if (day > 1) return false;

        string normalizedText = Normalize(text);
        if (normalizedText.Length == 0) return false;

        // 같은 질문을 다른 직원에게 다시 묻거나, 같은 대답이 두 번 나오는 일은 얼마든지 있다.
        // 전에는 그걸 전부 '중복'으로 버려서 대화 기록에 구멍이 났다(질문만 사라지고 대답만
        // 남아 순서가 뒤엉켜 보였다). 이제는 '바로 직전 줄'과만 비교해 같은 호출이 두 번
        // 들어온 사고만 막는다.
        var last = _entries.Count > 0 ? _entries[^1] : null;
        if (last != null && last.SpeakerId == (speakerId ?? "").Trim()
            && last.EntryType == entryType && Normalize(last.Text) == normalizedText)
            return false;

        _entries.Add(new DialogueHistoryEntry
        {
            Day = day,
            Timestamp = GameState.Instance?.DayTimeSeconds ?? 0f,
            Seq = ++_seq,
            SpeakerId = speakerId?.Trim() ?? "",
            CounterpartId = (counterpartId ?? "").Trim(),
            SpeakerDisplayName = speakerDisplayName?.Trim() ?? "",
            EntryType = entryType,
            Text = text?.Trim() ?? "",
            ConversationType = conversationType,
        });
        EmitSignal(SignalName.EntryAdded);
        return true;
    }

    // 그 직원과 오간 대화만. 관리자의 말도 상대가 그 직원이면 함께 나온다.
    // 순서는 기록된 차례 그대로다.
    public static bool Involves(DialogueHistoryEntry e, string employeeId) =>
        e != null && !string.IsNullOrEmpty(employeeId)
        && (e.SpeakerId == employeeId || e.CounterpartId == employeeId);

    public IReadOnlyList<DialogueHistoryEntry> GetAllEntries() => _entries;

    public void ClearAll()
    {
        _entries.Clear();
        _seq = 0;
        EmitSignal(SignalName.Cleared);
    }

    private static string Normalize(string value) =>
        Regex.Replace((value ?? "").Trim(), @"\s+", " ");
}
