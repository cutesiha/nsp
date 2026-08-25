using Godot;

namespace NSP.Ui;

public partial class EmployeeChip : Button
{
    public string EmployeeId = "";

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new Button
        {
            Text = Text,
            Size = Size,
            Modulate = new Color(1f, 1f, 1f, 0.85f),
        };
        SetDragPreview(preview);
        return EmployeeId;
    }
}
