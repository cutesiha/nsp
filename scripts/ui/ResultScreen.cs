using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

public partial class ResultScreen : Control
{
    public override void _Ready()
    {
        GameState.Instance.SetPhase(GamePhase.Result);

        var sim = FacilitySimulation.Instance;
        var employeeIds = sim.GetEmployeeIds().ToList();
        int aliveCount = employeeIds.Count(id => sim.GetEmployeeState(id)?.Alive ?? false);
        int tabooViolations = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.TabooViolation);
        int sabotageEvents = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Sabotage);

        string saboteurLine = sim.IsSaboteurIsolated()
            ? "내부 파괴공작자: 격리 완료"
            : "내부 파괴공작자: 미확인";

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("PROTOTYPE RESULT");
        lines.AppendLine();
        lines.AppendLine($"봉쇄 코어 최종 진행도: {GameState.Instance.CoreProgress:0.0}%");
        lines.AppendLine($"생존 직원: {aliveCount} / {employeeIds.Count}");
        lines.AppendLine($"금기 위반: {tabooViolations}건");
        lines.AppendLine($"사보타주 감지: {sabotageEvents}건");
        lines.AppendLine();
        lines.AppendLine(saboteurLine);

        GetNode<Label>("Root/ResultLabel").Text = lines.ToString();
        GetNode<Button>("Root/TitleButton").Pressed += OnTitlePressed;
    }

    private void OnTitlePressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/title/TitleScreen.tscn");
    }
}
