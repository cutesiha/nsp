using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Ui;

public partial class PowerBudgetPanel : Control
{
    private CheckButton _cctvButton;
    private CheckButton _ventButton;
    private CheckButton _lightingButton;
    private Label _remainingLabel;

    public override void _Ready()
    {
        _cctvButton = GetNode<CheckButton>("Rows/CctvRow/CheckButton");
        _ventButton = GetNode<CheckButton>("Rows/VentRow/CheckButton");
        _lightingButton = GetNode<CheckButton>("Rows/LightingRow/CheckButton");
        _remainingLabel = GetNode<Label>("RemainingLabel");

        _cctvButton.Toggled += pressed => OnToggled(PowerConsumer.CctvWatch, _cctvButton, pressed, Config.Instance.Data.PowerCostCctvWatch);
        _ventButton.Toggled += pressed => OnToggled(PowerConsumer.VentRepair, _ventButton, pressed, Config.Instance.Data.PowerCostVentRepair);
        _lightingButton.Toggled += pressed => OnToggled(PowerConsumer.Lighting, _lightingButton, pressed, Config.Instance.Data.PowerCostLighting);

        UpdateRemainingLabel();
    }

    public override void _Process(double delta)
    {
        UpdateRemainingLabel();
    }

    private void OnToggled(PowerConsumer consumer, CheckButton button, bool pressed, int cost)
    {
        bool ok = GameState.Instance.TrySetPowerAllocation(consumer, pressed ? cost : 0);
        if (!ok)
            button.SetPressedNoSignal(false);

        UpdateRemainingLabel();
    }

    private void UpdateRemainingLabel()
    {
        _remainingLabel.Text = $"잔여 전력: {GameState.Instance.GetPowerRemaining()} / {GameState.Instance.GetPowerBudgetTotal()}";
    }
}
