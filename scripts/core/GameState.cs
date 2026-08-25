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
    public int PowerCapacityBonus { get; private set; } = 0;
    public GameResult Result { get; private set; } = GameResult.None;

    public string SaboteurEmployeeId { get; private set; } = "";

    private readonly Dictionary<PowerConsumer, int> _powerAllocated = new();
    private readonly Random _rng = new();

    public override void _EnterTree()
    {
        Instance = this;
        foreach (PowerConsumer consumer in System.Enum.GetValues(typeof(PowerConsumer)))
        {
            _powerAllocated[consumer] = 0;
        }
    }

    public void AdvanceDayTime(float deltaSeconds)
    {
        DayTimeSeconds += deltaSeconds;
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

    public void AddPowerCapacityBonus(int delta)
    {
        PowerCapacityBonus = Math.Max(0, PowerCapacityBonus + delta);
    }

    private int GetEffectivePowerBudget() => Config.Instance.Data.PowerBudgetTotal + PowerCapacityBonus;

    public bool TrySetPowerAllocation(PowerConsumer consumer, int amount)
    {
        int budget = GetEffectivePowerBudget();
        int otherUsage = 0;
        foreach (var kv in _powerAllocated)
        {
            if (kv.Key != consumer)
                otherUsage += kv.Value;
        }
        if (otherUsage + amount > budget)
            return false;

        _powerAllocated[consumer] = amount;
        return true;
    }

    public int GetPowerAllocated(PowerConsumer consumer) => _powerAllocated.GetValueOrDefault(consumer, 0);

    public int GetPowerUsed()
    {
        int total = 0;
        foreach (var v in _powerAllocated.Values) total += v;
        return total;
    }

    public int GetPowerBudgetTotal() => GetEffectivePowerBudget();

    public int GetPowerRemaining() => GetEffectivePowerBudget() - GetPowerUsed();

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
    }
}
