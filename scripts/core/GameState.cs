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

    // 발전기 사고(TABOO-01/FAIL-01)로 인한 임시 최대 전력 용량 감소. 누적되지 않는 단일
    // 상태값 — 사고가 이미 진행 중이면 새 사고가 더 깎지 않고, 발전기 점검을 한 번 완료하면
    // 정상으로 완전히 복구된다(부분 회복 없음). 날짜가 바뀌면 이월되지 않고 초기화된다.
    public int PowerAccidentPenalty { get; private set; } = 0;
    public GameResult Result { get; private set; } = GameResult.None;

    public string SaboteurEmployeeId { get; private set; } = "";

    private readonly Random _rng = new();

    // ── 전력 패널(LIGHTING / CCTV / SENSOR) ────────────────────────────
    // 3개 채널에 슬롯 1개씩 — "몇 W를 쓰는가"가 아니라 "용량 안에서 몇 개를 동시에 켤 수
    // 있는가"만 다루는 물리 스위치 모델. On/Off는 플레이어가 3D 전력 패널에서 직접 고른다
    // (TryTogglePower). 용량이 줄어 이미 켜진 채널 수가 새 용량을 넘으면, 아래 우선순위로
    // 자동으로 끈다 — index 0 이 가장 먼저 차단(CCTV), 마지막 index(SENSOR)가 가장 오래
    // 버틴다(기존 자동 전력배분 시절의 우선순위를 그대로 계승).
    private static readonly PowerConsumer[] SwitchChannels =
        { PowerConsumer.CctvWatch, PowerConsumer.Lighting, PowerConsumer.Sensor };
    private static readonly PowerConsumer[] ShedPriority =
        { PowerConsumer.CctvWatch, PowerConsumer.Lighting, PowerConsumer.Sensor };

    private readonly Dictionary<PowerConsumer, bool> _switchOn = new()
    {
        [PowerConsumer.CctvWatch] = true,
        [PowerConsumer.Lighting] = true,
        [PowerConsumer.Sensor] = true,
    };

    public override void _EnterTree()
    {
        Instance = this;
    }

    // 발전 사고로 깎인 만큼 뺀 실제 용량(0~PowerCapacityMax).
    public int PowerCapacity => Math.Max(0, Config.Instance.Data.PowerCapacityMax - PowerAccidentPenalty);

    private int OnCount() => SwitchChannels.Count(c => _switchOn[c]);

    // VentRepair는 새 전력 패널에 없는 채널(2D 백업 화면 호환용) — 항상 켜진 것으로 취급한다.
    public bool IsConsumerPowered(PowerConsumer consumer) =>
        consumer == PowerConsumer.VentRepair || _switchOn.GetValueOrDefault(consumer);

    // 플레이어가 전력 패널 스위치를 누른다. 끄는 것은 항상 성공. 켜는 것은 현재 용량 안에
    // 여유가 있을 때만 성공 — 초과분은 거부만 하고(다른 채널을 먼저 꺼야 함) 자동으로 다른
    // 채널을 대신 끄지 않는다(플레이어가 직접 고르게 한다).
    public bool TryTogglePower(PowerConsumer consumer)
    {
        if (consumer == PowerConsumer.VentRepair) return false;

        if (_switchOn[consumer])
        {
            _switchOn[consumer] = false;
            return true;
        }
        if (OnCount() >= PowerCapacity) return false;
        _switchOn[consumer] = true;
        return true;
    }

    // 용량이 줄어든 뒤에도 여전히 켜진 채널 수가 새 용량을 넘으면 우선순위대로 강제로 끈다.
    private void ShedToCapacity()
    {
        foreach (var c in ShedPriority)
        {
            if (OnCount() <= PowerCapacity) break;
            _switchOn[c] = false;
        }
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

    // 발전 사고 시 최대 전력 용량 감소량. 서로 다른 원인(발전기 방치=3, TABOO-01=2 등)이
    // 겹치면 더 깊은 쪽으로 맞춘다. 발전기 점검을 한 번 완료하면 RepairPowerAccident 로
    // 완전히 복구된다. 용량이 줄어 이미 켜진 채널이 넘치면 바로 우선순위대로 강제 차단한다.
    public void TriggerPowerAccident(int penaltyAmount)
    {
        PowerAccidentPenalty = Math.Max(PowerAccidentPenalty, Math.Max(0, penaltyAmount));
        ShedToCapacity();
    }

    // 부분 복구 없음 — 발전기 점검 완료 시 용량과 세 채널 스위치를 전부 정상(ON)으로 되돌린다.
    public void RepairPowerAccident()
    {
        PowerAccidentPenalty = 0;
        foreach (var c in SwitchChannels) _switchOn[c] = true;
    }

    public bool IsPowerAccidentActive() => PowerAccidentPenalty > 0;

    // 각 소비처의 실제 사용량(슬롯 1개 = 1) — 2D 백업 화면 호환용.
    public int GetPowerAllocated(PowerConsumer consumer) => IsConsumerPowered(consumer) ? 1 : 0;

    public int GetPowerUsed() => OnCount();

    public int GetPowerBudgetTotal() => PowerCapacity;

    public int GetPowerRemaining() => PowerCapacity - OnCount();

    // 스위치가 용량을 넘어서게 켜질 수 없는 구조라 항상 예산 내로 유지된다.
    public bool IsPowerOverBudget() => false;

    // 2D 백업 화면(PowerBudgetPanel) 호환용 — amount>0 이면 켜기 시도, 0이면 끄기로 취급한다.
    public bool TrySetPowerAllocation(PowerConsumer consumer, int amount)
    {
        bool wantOn = amount > 0;
        if (wantOn == IsConsumerPowered(consumer)) return true;
        return TryTogglePower(consumer);
    }

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
        RepairPowerAccident();
    }
}
