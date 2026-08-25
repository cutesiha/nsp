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
}
