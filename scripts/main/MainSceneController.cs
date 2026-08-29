using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.Main;

public partial class MainSceneController : Node2D
{
    private float _logAccumulator = 0f;

    public override void _Ready()
    {
        if (GameState.Instance.CurrentPhase != GamePhase.Live)
            GameState.Instance.SetPhase(GamePhase.Live);

        // 이전 판/스폰 상태가 이월되지 않게 근무 시작 시 스폰·사고 레이어를 초기화한다.
        FacilitySimulation.Instance.ResetForNewShift();

        GetNode<Button>("UILayer/EndShiftButton").Pressed += OnEndShiftPressed;
        GetNode<Button>("UILayer/LogToggleButton").Pressed += () => LogPanel.Instance?.Toggle();

        ApplyMainScreenFont();

        GD.Print("=== 실시간 운영 시작 ===");
    }

    // 메인 운영 화면에서만(다른 씬의 전역 테마는 그대로 두고) 글씨를 BookkMyungjo_Bold로
    // 바꾼다. UILayer 자체는 CanvasLayer라 테마가 안 걸리므로, 텍스트를 담는 최상위 Control
    // 자식들에 하나씩 걸어 아래로 상속되게 한다 — 나중에 동적으로 생성되는 라벨(통화 대화창,
    // 업무 우선순위 팝업 등)도 같은 테마를 상속해서 별도 처리가 필요 없다.
    private void ApplyMainScreenFont()
    {
        var boldFont = GD.Load<Font>("res://assets/fonts/BookkMyungjo_Bold.ttf");
        if (boldFont == null) return;

        var theme = new Theme { DefaultFont = boldFont };
        string[] paths =
        {
            "UILayer/CoreProgressHud", "UILayer/Monitor01_Map", "UILayer/Monitor02_Cctv",
            "UILayer/Phone", "UILayer/PowerBudgetPanel", "UILayer/EmployeeDetailCard",
            "UILayer/RoomDetailCard", "UILayer/TaskPriorityPopup", "UILayer/CallLayer",
            "UILayer/LogPanel", "UILayer/LogToggleButton", "UILayer/EndShiftButton",
        };
        foreach (var path in paths)
        {
            if (GetNodeOrNull<Control>(path) is { } control)
                control.Theme = theme;
        }
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
            var gs = GameState.Instance;
            string cctv = gs.IsConsumerPowered(PowerConsumer.CctvWatch) ? "ON" : "OFF";
            string light = gs.IsConsumerPowered(PowerConsumer.Lighting) ? "ON" : "OFF";
            GD.Print($"[t={gs.DayTimeSeconds:0}s] 코어={gs.CoreProgress:0.0}% " +
                      $"전력={gs.GetPowerUsed()}/{gs.GetPowerBudgetTotal()} CCTV:{cctv} 조명:{light}");
        }
    }

    private void OnEndShiftPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/settlement/SettlementScreen.tscn");
    }
}
