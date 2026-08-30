using Godot;
using Godot.Collections;

namespace NSP.Data;

[GlobalClass]
public partial class RoomDef : Resource
{
    [Export] public string RoomId = "";
    [Export] public string DisplayName = "";
    [Export] public Array<string> ConnectedRoomIds = new();
    [Export] public Vector2 MapPosition = Vector2.Zero;
    [Export] public bool IsCoreRoom = false;
    [Export] public bool IsRestricted = false;
    [Export] public RoomResourceType ManagedResource = RoomResourceType.None;
    // LIGHTING이 꺼져도(정전 등) 이 방에 있는 직원 위치는 계속 보인다.
    [Export] public bool HasEmergencyLighting = false;
}
