using Godot;

namespace NSP.Ui;

public partial class PrologueScreen : Control
{
    public override void _Ready()
    {
        GetNode<Button>("Root/ContinueButton").Pressed += OnContinuePressed;
    }

    private void OnContinuePressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/schedule/ScheduleScene.tscn");
    }
}
