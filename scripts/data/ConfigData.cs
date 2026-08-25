using Godot;

namespace NSP.Data;

[GlobalClass]
public partial class ConfigData : Resource
{
    [Export] public int MaxDays = 5;
    [Export] public int MaxEmployees = 6;
    [Export] public float StressMax = 100f;
    [Export] public int MurderMaxTotal = 2;
    [Export] public int MurderMaxPerDay = 1;
    [Export] public int ActiveTabooCountPerDay = 2;

    [Export] public float ApiTimeoutSeconds = 8f;
    [Export] public int ApiMaxRetries = 1;
    [Export] public string AiModelId = "claude-3-5-haiku-20241022";
    [Export] public int AiMaxTokens = 300;

    [Export] public float EmployeeMoveSpeed = 80f;
    [Export] public float DayLengthSeconds = 300f;

    [Export] public int PowerBudgetTotal = 7;
    [Export] public int PowerCostCctvWatch = 3;
    [Export] public int PowerCostVentRepair = 4;
    [Export] public int PowerCostLighting = 3;

    [Export] public int IsolationCapacity = 1;

    [Export] public float SaboteurDecisionIntervalSeconds = 4f;
    [Export] public float SaboteurWanderChance = 0.15f;
    [Export] public float SaboteurSabotageChance = 0.35f;
    [Export] public float KillAttemptChance = 0.3f;
    [Export] public float SabotageCoreProgressLoss = 4f;
    [Export] public float SurveillanceSaboteurChanceMultiplier = 0.5f;

    [Export] public int MaterialsCap = 100;
    [Export] public int MaterialsPerCoreGauge = 5;
}
