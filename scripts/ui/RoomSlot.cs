using Godot;

namespace NSP.Ui;

public partial class RoomSlot : Button
{
    public string RoomId = "";
    public string AssignedEmployeeId = "";
    public System.Action<string, string> OnEmployeeDropped;

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return string.IsNullOrEmpty(AssignedEmployeeId) && data.VariantType == Variant.Type.String;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        OnEmployeeDropped?.Invoke(data.AsString(), RoomId);
    }
}
