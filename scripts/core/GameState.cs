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
    public int Materials { get; private set; } = 20;

    // 발전기 사고(TABOO-01/FAIL-01)로 인한 임시 최대 전력 용량 감소. 누적되지 않는 단일
    // 상태값 — 사고가 이미 진행 중이면 새 사고가 더 깎지 않고, 발전기 점검을 한 번 완료하면
    // 정상으로 완전히 복구된다(부분 회복 없음). 날짜가 바뀌면 이월되지 않고 초기화된다.
    public int PowerAccidentPenalty { get; private set; } = 0;
    public GameResult Result { get; private set; } = GameResult.None;

    public string SaboteurEmployeeId { get; private set; } = "";

    // 이번 판(5일) 전체 누적 살인 성공 횟수. 기획상 최대 2회.
    public int TotalKills { get; private set; }
    public void RegisterKill() => TotalKills++;

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

    // Config autoload 가 준비된 뒤 자재 시작값/한도를 데이터에서 읽어 온다
    // (필드 초기값은 Config 가 없을 때의 안전값일 뿐이다).
    public override void _Ready()
    {
        var cfg = Config.Instance?.Data;
        if (cfg == null) return;
        Materials = cfg.MaterialsStart;
        MaterialsCap = cfg.MaterialsCapBase;
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

    // 자재 보유 한도. 기본 30이고 저장고 상시 업무로 최대 60까지 올릴 수 있다.
    // 사고(보관 선반 붕괴)로 다시 내려갈 수 있으며, 그때 한도를 넘은 자재는 즉시 사라진다.
    public int MaterialsCap { get; private set; } = 30;

    public void AddMaterials(int delta)
    {
        // 한도가 꽉 찬 상태에서 생산된 초과 자재는 그냥 사라진다(넘치는 만큼 버려짐).
        Materials = Mathf.Clamp(Materials + delta, 0, MaterialsCap);
    }

    // 저장고 작업 = 한도 상승(최대 MaterialsCapMax). 사고 = 한도 하락(음수 delta).
    // 한도가 내려가면 초과 보유분은 즉시 파괴된다.
    public void AddMaterialsCap(int delta)
    {
        var cfg = Config.Instance.Data;
        MaterialsCap = Mathf.Clamp(MaterialsCap + delta, 0, cfg.MaterialsCapMax);
        if (Materials > MaterialsCap) Materials = MaterialsCap;
    }

    // 발전 사고 시 최대 전력 용량 감소량. 서로 다른 원인(발전기 방치=3, TABOO-01=2 등)이
    // 겹치면 더 깊은 쪽으로 맞춘다. 발전기 점검을 한 번 완료하면 RepairPowerAccident 로
    // 완전히 복구된다. 용량이 줄어 이미 켜진 채널이 넘치면 바로 우선순위대로 강제 차단한다.
    public void TriggerPowerAccident(int penaltyAmount)
    {
        int before = PowerCapacity;
        PowerAccidentPenalty = Math.Max(PowerAccidentPenalty, Math.Max(0, penaltyAmount));
        ShedToCapacity();
        LogCapacityChange(before);
    }

    // 부분 복구 없음 — 발전기 점검 완료 시 용량과 세 채널 스위치를 전부 정상(ON)으로 되돌린다.
    public void RepairPowerAccident()
    {
        int before = PowerCapacity;
        PowerAccidentPenalty = 0;
        foreach (var c in SwitchChannels) _switchOn[c] = true;
        LogCapacityChange(before);
    }

    // 사용 가능한 전력이 실제로 바뀐 순간만 전후 값과 함께 남긴다.
    // 근무 시작 시의 초기화(ResetForNewShift)는 근무 중이 아니므로 기록되지 않는다.
    private void LogCapacityChange(int before)
    {
        int after = PowerCapacity;
        if (after == before || CurrentPhase != GamePhase.Live) return;
        int delta = after - before;
        EventLog.Instance?.LogEvent(LogEventType.PowerCapacityChanged, "", "",
            delta < 0 ? $"⚠ 전력 {before} → {after} [{-delta} 감소]"
                      : $"✓ 전력 {before} → {after} [{delta} 증가]");
        // 전력 손실은 대개 발전실 사고의 결과다 — 가장 최근 사고에 결과 줄로 붙인다.
        if (delta < 0) IncidentTracker.AddConsequence("", $"전력 {before} → {after}");
    }

    public bool IsPowerAccidentActive() => PowerAccidentPenalty > 0;

    // ── 시설 설비 고장(전력과 별개) ────────────────────────────────────
    // 해당 방 사고 업무를 방치해 발생하고, 그 방에서 "수리" 업무를 완료해야 풀린다.
    // CctvSystemOffline: FAIL-04 — CCTV에 전력을 줘도 수리 전까지 신호가 안 뜬다.
    // MaterialsProductionHalted: FAIL-03 — 정비실 자재 생산이 멈춘다(기존 자재는 사용 가능).
    // VentilationDown: FAIL-02 — 환기 정지, 수리 전까지 전 직원 스트레스가 계속 오른다.
    public bool CctvSystemOffline { get; private set; }
    public bool MaterialsProductionHalted { get; private set; }
    public bool VentilationDown { get; private set; }
    // 의료 장비 오염 — 의무실 스트레스 치료가 수리 전까지 불가.
    public bool MedicalContaminated { get; private set; }
    // 봉쇄 코어 출력 불안정 — 코어 복구 정지 + 주기적으로 복구율 감소.
    public bool CoreOutputUnstable { get; private set; }

    public void SetCctvSystemOffline(bool v) => CctvSystemOffline = v;
    public void SetMaterialsProductionHalted(bool v) => MaterialsProductionHalted = v;
    public void SetVentilationDown(bool v) => VentilationDown = v;
    public void SetMedicalContaminated(bool v) => MedicalContaminated = v;
    public void SetCoreOutputUnstable(bool v) => CoreOutputUnstable = v;

    public void ResetFacilityFaults()
    {
        CctvSystemOffline = false;
        MaterialsProductionHalted = false;
        VentilationDown = false;
        MedicalContaminated = false;
        CoreOutputUnstable = false;
    }

    // 처음부터 다시 시작(시작화면으로 돌아가기). autoload 라 씬을 다시 로드해도 살아남는
    // 진행 상태를 전부 DAY 1 초기값으로 되돌린다.
    public void ResetRun()
    {
        CurrentDay = 1;
        CurrentPhase = GamePhase.Prep;
        DayTimeSeconds = 0f;
        CoreProgress = 0f;
        Materials = Config.Instance.Data.MaterialsStart;
        MaterialsCap = Config.Instance.Data.MaterialsCapBase;
        Result = GameResult.None;
        SaboteurEmployeeId = "";
        TotalKills = 0;
        RepairPowerAccident();     // 용량 복구 + 세 채널 ON
        ResetFacilityFaults();
    }

    // CCTV를 실제로 볼 수 있는가 = 전력이 있고 + 설비 고장(FAIL-04)이 아니어야 한다.
    public bool IsCctvOperational() => IsConsumerPowered(PowerConsumer.CctvWatch) && !CctvSystemOffline;

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
        ResetFacilityFaults();
    }
}
