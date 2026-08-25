using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Main;

public partial class MainSceneController : Node2D
{
    private float _logAccumulator = 0f;

    public override void _Ready()
    {
        if (GameState.Instance.CurrentPhase != GamePhase.Live)
            GameState.Instance.SetPhase(GamePhase.Live);

        GetNode<Button>("UILayer/EndShiftButton").Pressed += OnEndShiftPressed;

        GD.Print("=== 실시간 운영 시작 ===");
    }

    public override void _Process(double delta)
    {
        if (GameState.Instance.CurrentPhase != GamePhase.Live)
            return;

        GameState.Instance.AdvanceDayTime((float)delta);
        FacilitySimulation.Instance.Tick(delta);

        _logAccumulator += (float)delta;
        if (_logAccumulator >= 2f)
        {
            _logAccumulator = 0f;
            GD.Print($"[t={GameState.Instance.DayTimeSeconds:0}s] 코어 진행도={GameState.Instance.CoreProgress:0.0}% " +
                      $"전력사용={GameState.Instance.GetPowerUsed()}/{Config.Instance.Data.PowerBudgetTotal}");
        }
    }

    private void OnEndShiftPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/settlement/SettlementScreen.tscn");
    }
}
