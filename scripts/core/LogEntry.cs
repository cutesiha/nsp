using System.Collections.Generic;
using NSP.Data;

namespace NSP.Core;

public class LogEntry
{
    public int Day;
    public float GameTimeSeconds;
    public LogEventType EventType;
    public string ActorEmployeeId = "";
    public string RoomId = "";
    public string Description = "";
    public List<string> WitnessEmployeeIds = new();
}
