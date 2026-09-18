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
    private readonly HashSet<string> _dedupe = new();

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public bool AddEntry(string speakerId, string speakerDisplayName, DialogueEntryType entryType,
        string text, DialogueConversationType conversationType)
    {
        // DAY2 이후 저장/탭은 이번 프로토타입 범위가 아니다.
        if ((GameState.Instance?.CurrentDay ?? 1) != 1) return false;

        string normalizedText = Normalize(text);
        if (normalizedText.Length == 0) return false;

        string normalizedSpeaker = Normalize(speakerId).ToLowerInvariant();
        string key = $"{normalizedSpeaker}\u001f{entryType}\u001f{normalizedText}";
        if (!_dedupe.Add(key)) return false;

        _entries.Add(new DialogueHistoryEntry
        {
            Day = 1,
            Timestamp = GameState.Instance?.DayTimeSeconds ?? 0f,
            SpeakerId = speakerId?.Trim() ?? "",
            SpeakerDisplayName = speakerDisplayName?.Trim() ?? "",
            EntryType = entryType,
            Text = text?.Trim() ?? "",
            ConversationType = conversationType,
        });
        EmitSignal(SignalName.EntryAdded);
        return true;
    }

    public IReadOnlyList<DialogueHistoryEntry> GetAllEntries() => _entries;

    public void ClearAll()
    {
        _entries.Clear();
        _dedupe.Clear();
        EmitSignal(SignalName.Cleared);
    }

    private static string Normalize(string value) =>
        Regex.Replace((value ?? "").Trim(), @"\s+", " ");
}
