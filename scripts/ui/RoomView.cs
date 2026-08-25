using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.Taboo;

namespace NSP.Ui;

public partial class RoomView : ColorRect
{
    [Export] public string RoomId = "";

    private static readonly Color RelocateHighlight = new(0.85f, 0.25f, 0.25f, 0.9f);

    private Label _label;
    private Color _normalColor;

    public override void _Ready()
    {
        _label = GetNodeOrNull<Label>("Label");
        _normalColor = Color;
        MouseFilter = MouseFilterEnum.Stop;

        FacilitySimulation.Instance?.SetRoomVisualCenter(RoomId, Position + Size / 2f);
        FacilitySimulation.Instance?.SetRoomVisualColor(RoomId, _normalColor);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        var sim = FacilitySimulation.Instance;
        string relocatingId = sim.RelocatingEmployeeId;

        if (!string.IsNullOrEmpty(relocatingId))
        {
            if (IsValidRelocateTarget(sim, relocatingId))
            {
                sim.ClearAssignment(relocatingId);
                sim.AssignToRoom(relocatingId, RoomId);
                sim.CancelRelocating();
                EmployeeDetailCard.Instance?.Refresh();
            }
            return;
        }

        sim.SetSurveillanceTarget(RoomId);
        EmployeeDetailCard.Instance?.HideCard();
        RoomDetailCard.Instance?.Show(RoomId);
    }

    public override void _Process(double delta)
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || string.IsNullOrEmpty(RoomId)) return;

        var def = sim.GetRoomDef(RoomId);
        var state = sim.GetRoomState(RoomId);
        if (def == null || state == null) return;

        if (!string.IsNullOrEmpty(sim.RelocatingEmployeeId) && IsValidRelocateTarget(sim, sim.RelocatingEmployeeId))
        {
            Color = RelocateHighlight;
        }
        else
        {
            Color = state.PowerOn ? _normalColor : new Color(0.06f, 0.06f, 0.07f);
        }

        string text = def.DisplayName;
        if (def.IsRestricted) text += "\n[출입제한]";
        if (def.IsCoreRoom) text += $"\n{GameState.Instance.CoreProgress:0}%";
        if (!state.PowerOn) text += "\n[정전]";
        if (state.CctvDisconnected) text += "\n[CCTV단절]";
        if (state.Locked) text += "\n[봉쇄]";
        if (state.RedAlertLighting) text += "\n[적색등]";
        if (TabooRuleSystem.Instance != null && TabooRuleSystem.Instance.IsRoomAtTabooRisk(RoomId))
            text += "\n⚠ 금기 위험";

        if (_label != null)
            _label.Text = text;
    }

    private bool IsValidRelocateTarget(FacilitySimulation sim, string relocatingEmployeeId)
    {
        if (!sim.CanAssignToRoom(RoomId)) return false;
        var state = sim.GetEmployeeState(relocatingEmployeeId);
        return state != null && state.CurrentRoomId != RoomId;
    }
}
