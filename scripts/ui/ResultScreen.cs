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
        int sabotageEvents = EventLog.Instance.GetAllEntries().Count(e => e.EventType == LogEventType.Sabotage);

        string saboteurLine = sim.IsSaboteurIsolated()
            ? "내부 파괴공작자: 격리 완료"
            : "내부 파괴공작자: 미확인";

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("PROTOTYPE RESULT");
        lines.AppendLine();
        lines.AppendLine($"봉쇄 코어 최종 진행도: {GameState.Instance.CoreProgress:0.0}%");
        lines.AppendLine($"생존 직원: {aliveCount} / {employeeIds.Count}");
        lines.AppendLine($"사보타주 감지: {sabotageEvents}건");
        lines.AppendLine();
        lines.AppendLine(saboteurLine);
        lines.AppendLine();
        int score = GameState.Instance.EvaluationScore;
        lines.AppendLine($"업무평가: {score}점");
        // 등급 규칙은 성적표(EndingRecordOverlay)와 같은 하나를 쓴다.
        lines.AppendLine($"관리자 평가 등급: {DayObjectives.FinalGrade(DayObjectives.KindFromState())}");

        GetNode<Label>("Root/ResultLabel").Text = lines.ToString();
        GetNode<Button>("Root/TitleButton").Pressed += OnTitlePressed;
    }

    private void OnTitlePressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/title/TitleScreen.tscn");
    }
}
