using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.Ui;

public partial class SettlementScreen : Control
{
    public override void _Ready()
    {
        GameState.Instance.SetPhase(GamePhase.Settlement);

        var sim = FacilitySimulation.Instance;
        var employeeIds = sim.GetEmployeeIds().ToList();
        int aliveCount = employeeIds.Count(id => sim.GetEmployeeState(id)?.Alive ?? false);
        int tabooViolations = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.TabooViolation);
        int sabotageEvents = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Sabotage);
        int deaths = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Death);
        int neglectEvents = EventLog.Instance.GetAllEntries().Count(e => e.EventType is LogEventType.Neglect or LogEventType.TaskFailed);

        var deadCodenames = employeeIds
            .Where(id => !(sim.GetEmployeeState(id)?.Alive ?? true))
            .Select(id => sim.GetEmployeeDef(id)?.Codename ?? id)
            .ToList();

        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"봉쇄 코어 진행도: {GameState.Instance.CoreProgress:0.0}%");
        lines.AppendLine($"자재: {GameState.Instance.Materials} / {GameState.Instance.MaterialsCap}");
        lines.AppendLine($"생존 직원: {aliveCount} / {employeeIds.Count}");
        if (deadCodenames.Count > 0)
            lines.AppendLine($"사망: {string.Join(", ", deadCodenames)}");
        lines.AppendLine($"금기 위반: {tabooViolations}건");
        lines.AppendLine($"사보타주 감지: {sabotageEvents}건");
        lines.AppendLine($"업무 방치 사고: {neglectEvents}건");
        lines.AppendLine($"사망 사건: {deaths}건");

        GetNode<Label>("Root/SummaryLabel").Text = lines.ToString();
        GetNode<Button>("Root/ContinueButton").Pressed += OnContinuePressed;
    }

    private void OnContinuePressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/rest/RestScene.tscn");
    }
}
