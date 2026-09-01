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

    // LIGHTING/CCTV/SENSOR 전력 패널 — 슬롯 3개, 사고로 발전 용량이 줄면 동시에 켤 수 있는
    // 개수도 그만큼 줄어든다(코스트 가중치 없음, 채널당 슬롯 1개).
    [Export] public int PowerCapacityMax = 3;
    // SENSOR 예고가 "임박"으로 뜨기 시작하는 남은시간 임계값(초).
    [Export] public float AlertLeadSeconds = 20f;
    // FAIL-02 환기 정지 중 전 직원에게 매초 누적되는 스트레스.
    [Export] public float VentFaultStressPerSecond = 1.5f;
    // 발전실 금기 이상현상 시 발전실에 있던 두 직원에게 즉시 더해지는 스트레스.
    [Export] public float PowerTabooStress = 35f;

    // 2D 백업 화면(PowerBudgetPanel)에서만 쓰는 옛 코스트 가중치 값 — 3D 전력 패널은 안 씀.
    [Export] public int PowerBudgetTotal = 7;
    [Export] public int PowerCostCctvWatch = 3;
    [Export] public int PowerCostVentRepair = 4;
    [Export] public int PowerCostLighting = 3;

    [Export] public int IsolationCapacity = 1;

    [Export] public float SaboteurDecisionIntervalSeconds = 4f;
    [Export] public float SaboteurWanderChance = 0.15f;
    [Export] public float SaboteurSabotageChance = 0.35f;
    [Export] public float KillAttemptChance = 0.3f;
    [Export] public float SabotageTaskGaugeLoss = 5f;
    [Export] public float SurveillanceSaboteurChanceMultiplier = 0.5f;

    [Export] public int MaterialsCap = 100;
    [Export] public int MaterialsPerCoreGauge = 5;

    // 발생 업무가 완료/실패한 뒤 방 카드에 결과 배지를 몇 초 더 보여줄지.
    [Export] public float ResolvedTaskDisplaySeconds = 2.5f;
}
