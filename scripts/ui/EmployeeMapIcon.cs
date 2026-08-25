using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.Ui;

public partial class EmployeeMapIcon : ColorRect
{
    [Export] public string EmployeeId = "";

    public static readonly Dictionary<string, EmployeeMapIcon> Registry = new();

    private const float StackOffsetRadius = 20f;

    private Label _label;
    private Color _normalColor;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        _normalColor = Color;

        var def = FacilitySimulation.Instance?.GetEmployeeDef(EmployeeId);
        if (def != null && _label != null)
            _label.Text = def.Codename;

        Registry[EmployeeId] = this;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _ExitTree()
    {
        if (Registry.TryGetValue(EmployeeId, out var self) && self == this)
            Registry.Remove(EmployeeId);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            RoomDetailCard.Instance?.HideCard();
            EmployeeDetailCard.Instance?.Show(EmployeeId);
        }
    }

    public override void _Process(double delta)
    {
        var sim = FacilitySimulation.Instance;
        var state = sim?.GetEmployeeState(EmployeeId);
        if (state == null) return;

        Visible = !state.Isolated;
        Color = state.Alive ? _normalColor : new Color(0.3f, 0.3f, 0.3f);

        Vector2 pos = state.Position;
        if (!state.IsMoving)
            pos += GetStackOffset(sim, state);

        Position = pos - Size / 2f;
    }

    private Vector2 GetStackOffset(FacilitySimulation sim, EmployeeState state)
    {
        var room = sim.GetRoomState(state.CurrentRoomId);
        if (room == null) return Vector2.Zero;

        int idx = room.OccupantEmployeeIds.IndexOf(EmployeeId);
        if (idx <= 0) return Vector2.Zero;

        float angle = idx * Mathf.Tau / 6f;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * StackOffsetRadius;
    }
}
