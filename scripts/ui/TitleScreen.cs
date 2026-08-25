using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.Ui;

public partial class TitleScreen : Control
{
    public override void _Ready()
    {
        GetNode<Button>("Root/StartButton").Pressed += OnStartPressed;
    }

    private void OnStartPressed()
    {
        EventLog.Instance.ClearAll();
        GameState.Instance.AssignRandomSaboteur(FacilitySimulation.Instance.GetEmployeeIds());

        GetTree().ChangeSceneToFile("res://scenes/prologue/PrologueScreen.tscn");
    }
}
