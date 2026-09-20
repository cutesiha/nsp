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
        lines.AppendLine();
        // 관리자 평가 — 선택 업무를 몇 개나 해냈는가로만 갈린다.
        // 선택 업무는 게임 성능에 아무 영향도 주지 않고 이 등급에만 반영된다.
        int score = GameState.Instance.EvaluationScore;
        lines.AppendLine($"업무평가: {score}점");
        lines.AppendLine($"관리자 평가 등급: {DayObjectives.Grade(GameState.Instance.CoreProgress, score)}");

        GetNode<Label>("Root/ResultLabel").Text = lines.ToString();
        GetNode<Button>("Root/TitleButton").Pressed += OnTitlePressed;
    }

    private void OnTitlePressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/title/TitleScreen.tscn");
    }
}
