using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;

namespace NSP.Core;

public partial class GameState : Node
{
    public static GameState Instance { get; private set; }

    public int CurrentDay { get; private set; } = 1;
    public GamePhase CurrentPhase { get; private set; } = GamePhase.Prep;
    public float DayTimeSeconds { get; private set; } = 0f;

    public float CoreProgress { get; private set; } = 0f;
    public int Materials { get; private set; } = 0;

    // 발전기 사고(TABOO-01/FAIL-01)로 인한 임시 최대 전력 감소. 누적되지 않는 단일 상태값 —
    // 사고가 이미 진행 중이면 새 사고가 더 깎지 않고, 발전기 점검을 한 번 완료하면 정상으로
    // 완전히 복구된다(부분 회복 없음). 날짜가 바뀌면 이월되지 않고 초기화된다.
    public int PowerAccidentPenalty { get; private set; } = 0;
    public GameResult Result { get; private set; } = GameResult.None;

    public string SaboteurEmployeeId { get; private set; } = "";

    private readonly Random _rng = new();

    // DAY1 프로토타입: 플레이어가 전력을 직접 배분하지 않는다. 필요한 시설 전력은 근무 시작 시
    // 자동으로 정상 배분된 상태이며(용량이 충분하면 CCTV·조명·환기 전부 ON), 발전 사고로 최대
    // 용량이 줄어 현재 소비량을 감당 못 하면 아래 우선순위로 자동 차단한다.
    //   index 0 = 가장 먼저 차단(CCTV)  →  마지막까지 유지(환기)
    private static readonly PowerConsumer[] LoadShedOrder =
    {
        PowerConsumer.CctvWatch,
        PowerConsumer.Lighting,
        PowerConsumer.VentRepair,
    };

    public override void _EnterTree()
    {
        Instance = this;
    }

    private int CostOf(PowerConsumer consumer) => consumer switch
    {
        PowerConsumer.CctvWatch => Config.Instance.Data.PowerCostCctvWatch,
        PowerConsumer.VentRepair => Config.Instance.Data.PowerCostVentRepair,
        PowerConsumer.Lighting => Config.Instance.Data.PowerCostLighting,
        _ => 0,
    };

    // 현재 최대 용량 안에서, 가장 보호되는 소비처(환기)부터 채워 넣었을 때 이 소비처가 전력을
    // 받는가. 발전기 사고 등으로 용량이 줄면 CCTV → 조명 순으로 자동으로 떨어져 나간다.
    public bool IsConsumerPowered(PowerConsumer consumer)
    {
        int capacity = GetEffectivePowerBudget();
        int used = 0;
        for (int i = LoadShedOrder.Length - 1; i >= 0; i--)
        {
            var c = LoadShedOrder[i];
            bool canPower = used + CostOf(c) <= capacity;
            if (canPower) used += CostOf(c);
            if (c == consumer) return canPower;
        }
        return false;
    }

    public void AdvanceDayTime(float deltaSeconds)
    {
        DayTimeSeconds += deltaSeconds;
    }

    // 근무 시작 시 하루 시계를 0으로. 고정 스폰 스케줄이 매 근무 같은 흐름으로 흐르게 한다.
    // (CoreProgress/Materials/saboteur 등 나머지 세션 상태의 리셋은 별개 이슈로 남아있음.)
    public void ResetDayClock()
    {
        DayTimeSeconds = 0f;
    }

    public void SetPhase(GamePhase phase)
    {
        CurrentPhase = phase;
    }

    public void AddCoreProgress(float delta, string reason)
    {
        CoreProgress = Mathf.Clamp(CoreProgress + delta, 0f, 100f);
    }

    public void AddMaterials(int delta)
    {
        Materials = Mathf.Clamp(Materials + delta, 0, Config.Instance.Data.MaterialsCap);
    }

    // 발전 사고 시 최대 전력 감소량. 서로 다른 원인(발전기 방치=3, TABOO-01=2 등)이 겹치면
    // 더 깊은 쪽으로 맞춘다. 발전기 점검을 한 번 완료하면 RepairPowerAccident 로 완전히 복구된다.
    public void TriggerPowerAccident(int penaltyAmount)
    {
        PowerAccidentPenalty = Math.Max(PowerAccidentPenalty, Math.Max(0, penaltyAmount));
    }

    public void RepairPowerAccident()
    {
        PowerAccidentPenalty = 0;
    }

    public bool IsPowerAccidentActive() => PowerAccidentPenalty > 0;

    private int GetEffectivePowerBudget() => Math.Max(0, Config.Instance.Data.PowerBudgetTotal - PowerAccidentPenalty);

    // 자동 배분 모델에서 각 소비처의 실제 사용량 = 전력을 받으면 비용, 못 받으면 0.
    public int GetPowerAllocated(PowerConsumer consumer) => IsConsumerPowered(consumer) ? CostOf(consumer) : 0;

    public int GetPowerUsed()
    {
        int total = 0;
        foreach (PowerConsumer c in System.Enum.GetValues(typeof(PowerConsumer)))
            total += GetPowerAllocated(c);
        return total;
    }

    public int GetPowerBudgetTotal() => GetEffectivePowerBudget();

    public int GetPowerRemaining() => GetEffectivePowerBudget() - GetPowerUsed();

    // 자동 배분이라 항상 예산 내로 유지된다 — 예전 수동 배분 시절 호출부 호환용으로만 남긴다.
    public bool IsPowerOverBudget() => false;

    // 수동 전력 배분은 DAY1 프로토타입에서 제거됨(전력은 배치 실패의 연쇄 결과로만 사용).
    public bool TrySetPowerAllocation(PowerConsumer consumer, int amount) => false;

    public void SetResult(GameResult result)
    {
        Result = result;
    }

    public void AssignRandomSaboteur(IEnumerable<string> employeeIds)
    {
        var pool = employeeIds.ToList();
        if (pool.Count == 0) return;

        SaboteurEmployeeId = pool[_rng.Next(pool.Count)];
    }

    public void SetSaboteur(string employeeId)
    {
        SaboteurEmployeeId = employeeId;
    }

    public void GoToNextDay()
    {
        CurrentDay += 1;
        DayTimeSeconds = 0f;
        CurrentPhase = GamePhase.Prep;
        PowerAccidentPenalty = 0;
    }
}
