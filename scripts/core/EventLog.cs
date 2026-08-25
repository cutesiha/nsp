using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;

namespace NSP.Core;

public partial class EventLog : Node
{
    public static EventLog Instance { get; private set; }

    private readonly List<LogEntry> _entries = new();

    public override void _EnterTree()
    {
        Instance = this;
    }

    public void Log(LogEntry entry)
    {
        _entries.Add(entry);
        EmitSignal(SignalName.EntryLogged);
    }

    public void LogEvent(LogEventType type, string actorEmployeeId, string roomId, string description, IEnumerable<string> witnesses = null)
    {
        var entry = new LogEntry
        {
            Day = GameState.Instance?.CurrentDay ?? 0,
            GameTimeSeconds = GameState.Instance?.DayTimeSeconds ?? 0f,
            EventType = type,
            ActorEmployeeId = actorEmployeeId,
            RoomId = roomId,
            Description = description,
            WitnessEmployeeIds = witnesses?.ToList() ?? new List<string>(),
        };
        Log(entry);
    }

    public void ClearAll()
    {
        _entries.Clear();
    }

    public IReadOnlyList<LogEntry> GetAllEntries() => _entries;

    public IReadOnlyList<LogEntry> GetEntriesKnownBy(string employeeId)
    {
        return _entries
            .Where(e => e.ActorEmployeeId == employeeId || e.WitnessEmployeeIds.Contains(employeeId))
            .ToList();
    }

    [Signal] public delegate void EntryLoggedEventHandler();
}
