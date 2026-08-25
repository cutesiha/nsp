using System.Collections.Generic;

namespace NSP.Facility;

public class RoomState
{
    public string RoomId;
    public bool PowerOn = true;
    public bool Locked = false;
    public bool RedAlertLighting = false;
    public bool CctvDisconnected = false;
    public bool InfoDistorted = false;
    public List<string> OccupantEmployeeIds = new();

    public List<string> TaskPriorityOrder = new();
    public Dictionary<string, float> TaskGauges = new();
    public float NeglectTimer = 0f;
}
