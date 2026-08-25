using System.Collections.Generic;
using Godot;

namespace NSP.Facility;

public class ConversationTurn
{
    public string Role;
    public string Text;
}

public class EmployeeState
{
    public string EmployeeId;
    public float Stress;
    public string CurrentRoomId;
    public string AssignedRoomId = "";
    public string TargetRoomId;
    public List<string> PathQueue = new();
    public Vector2 Position;
    public bool IsMoving;
    public bool Alive = true;
    public bool Isolated = false;
    public string PreIsolationRoomId = "";

    public List<ConversationTurn> ConversationHistory = new();
}
