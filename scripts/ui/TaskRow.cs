using Godot;

namespace NSP.Ui;

public partial class TaskRow : PanelContainer
{
    public string TaskId = "";
    public System.Action<string, string> OnDroppedOnto;

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new Label { Text = "≡ 이동 중...", Modulate = new Color(1f, 1f, 1f, 0.85f) };
        SetDragPreview(preview);
        return TaskId;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return data.VariantType == Variant.Type.String && data.AsString() != TaskId;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        OnDroppedOnto?.Invoke(data.AsString(), TaskId);
    }
}
