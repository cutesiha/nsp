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

    // 몇 번째 5일인가. 1 = 첫 5일(스토리 캠페인) · 2 = 표준 · 3 이상 = 고난도.
    // 복구 난이도 표(RecoveryProfile)를 고르는 데만 쓴다 — 스토리 재생 여부와는 무관하다.
    public int RecoveryCycle { get; private set; } = 1;

    public void SetRecoveryCycle(int cycle) => RecoveryCycle = Math.Max(1, cycle);
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

    // 업무평가 — 선택 업무를 달성할 때마다 +1. 게임 성능에는 전혀 영향을 주지 않고
    // 마지막 날의 관리자 평가 등급에만 반영된다.
    public int EvaluationScore { get; private set; }

    // 필수 업무를 못 끝낸 채 끝난 근무. 임의의 수치 패널티는 주지 않고 사실만 남긴다 —
    // 정산 화면과 마지막 관리자 평가가 이 값을 읽는다.
    public int MissedRequiredDays { get; private set; }
    // 방금 끝난 근무에서 못 끝낸 필수 업무 수(0이면 전부 달성).
    public int LastShiftMissedRequired { get; private set; }

    public void RecordShiftObjectives(int missedRequired)
    {
        LastShiftMissedRequired = Math.Max(0, missedRequired);
        if (LastShiftMissedRequired > 0) MissedRequiredDays += 1;
    }
    public void AddEvaluation(int amount) => EvaluationScore = Math.Max(0, EvaluationScore + amount);

    // 5일 누적 — 최종 근무 기록 화면 전용. 오늘 기록(EventLog · IncidentTracker)은 다음 근무 시작에
    // 지워지므로, 근무가 끝날 때마다 그날 숫자를 여기 더해 둔다.
    public int TotalTabooViolations { get; private set; }
    public int TotalIncidents { get; private set; }

    public void AddShiftTotals(int tabooViolations, int incidents)
    {
        TotalTabooViolations += Math.Max(0, tabooViolations);
        TotalIncidents += Math.Max(0, incidents);
    }

    // ── 최종 격리 보고서 ────────────────────────────────────────────────
    // DAY5 가 끝나고 제출하는 마지막 절차. 제출하면 되돌릴 수 없고, 엔딩 분기를 가른다.
    //   FinalAccusedId  지목한 직원 id. 지목하지 않았으면 "".
    //   FinalEvidence   근거로 붙인 단서(일자 + 자료 id). 0~2장. 비어 있어도 제출된다.
    //   WasCaught       지목이 실제 결번과 맞았는가 — 엔딩 축.
    //   WasProven       그 지목을 근거로 증명했는가 — 엔딩을 바꾸지 않고 GUIDE-0 한 줄과 등급에만 쓴다.
    public string FinalAccusedId { get; private set; } = "";
    public IReadOnlyList<(int Day, string EvidenceId)> FinalEvidence => _finalEvidence;
    private readonly List<(int Day, string EvidenceId)> _finalEvidence = new();
    public bool FinalReportSubmitted { get; private set; }

    public bool WasCaught => !string.IsNullOrEmpty(FinalAccusedId)
                             && FinalAccusedId == SaboteurEmployeeId;

    // 붙인 근거 중 한 장이라도 지목한 직원이 등장하는 자료면 "증명"으로 본다.
    public bool WasProven { get; private set; }

    public void SubmitFinalReport(string accusedId, IEnumerable<(int Day, string EvidenceId)> evidence,
        Func<int, string, bool> involvesAccused)
    {
        FinalAccusedId = accusedId ?? "";
        _finalEvidence.Clear();
        if (evidence != null) _finalEvidence.AddRange(evidence);
        WasProven = WasCaught && involvesAccused != null
                    && _finalEvidence.Any(e => involvesAccused(e.Day, e.EvidenceId));
        FinalReportSubmitted = true;
        // 미뤄 둔 도전과제 팝업(「억울한 격리」)은 지목이 끝난 뒤에야 뜬다 —
        // 그 전에 뜨면 업적이 추리의 정답을 먼저 알려 주는 셈이 된다.
        AchievementManager.Instance?.NoteFinalReportSubmitted();
    }

    // 개발 허브 · 캡처용 — 보고서 화면을 거치지 않고 판정만 세운다.
    public void ForceFinalVerdict(string accusedId, bool proven)
    {
        FinalAccusedId = accusedId ?? "";
        _finalEvidence.Clear();
        WasProven = WasCaught && proven;
        FinalReportSubmitted = true;
    }

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

    // 코어 증감 장부. 측정 도구(DayScheduleTest)만 구독한다 — 게임 진행에는 아무 영향이 없다.
    // 사유(reason)가 그대로 넘어가므로 "얼마나 늘었고 무엇 때문에 깎였는가" 를 나눌 수 있다.
    public static System.Action<float, string> CoreLedger;

    public void AddCoreProgress(float delta, string reason)
    {
        CoreLedger?.Invoke(delta, reason);
        float before = CoreProgress;
        CoreProgress = Mathf.Clamp(CoreProgress + delta, 0f, 100f);
        // 「이게 연습이라고?」 — 가상 시뮬레이션에서 100% 에 닿은 **그 순간**을 잡는다.
        // DAY1 로 넘어가며 진행도는 0% 로 돌아가지만(GoToNextDay) 기록은 이미 남아 있다.
        AchievementManager.Instance?.NoteCoreProgress(before, CoreProgress);
    }

    // 자재 보유 한도. 기본 30이고 저장고 상시 업무로 최대 60까지 올릴 수 있다.
    // 사고(보관 선반 붕괴)로 다시 내려갈 수 있으며, 그때 한도를 넘은 자재는 즉시 사라진다.
    public int MaterialsCap { get; private set; } = 30;

    // 검사 전용 — 자재가 실제로 얼마나 들어오고 나갔는지, 한도에 걸려 얼마가 버려졌는지.
    public static System.Action<int, string> MaterialFlowProbe;

    public void AddMaterials(int delta)
    {
        // 한도가 꽉 찬 상태에서 생산된 초과 자재는 그냥 사라진다(넘치는 만큼 버려짐).
        int before = Materials;
        Materials = Mathf.Clamp(Materials + delta, 0, MaterialsCap);
        int real = Materials - before;
        if (MaterialFlowProbe != null && delta != real)
            MaterialFlowProbe(delta - real, delta > 0 ? "cap_discard" : "underflow");
    }

    // 저장고 작업 = 한도 상승(최대 MaterialsCapMax). 사고 = 한도 하락(음수 delta).
    // 한도가 내려가면 초과 보유분은 즉시 파괴된다.
    public void AddMaterialsCap(int delta)
    {
        var cfg = Config.Instance.Data;
        // 난이도 표가 정한 하한 아래로는 내려가지 않는다. 한도가 0 이 되면 생산한 자재가
        // 전부 즉시 폐기되어 코어 복구가 영영 불가능해진다 — 되돌릴 수 없는 판이 된다.
        int floor = Mathf.Clamp(RecoveryProfile.MaterialsCapFloor, 0, cfg.MaterialsCapMax);
        MaterialsCap = Mathf.Clamp(MaterialsCap + delta, floor, cfg.MaterialsCapMax);
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
    // 프롤로그를 거쳐 새 게임을 시작하면 DAY0(교육)부터, 프롤로그를 건너뛰면 DAY1 부터.
    public void ResetRun(int startDay)
    {
        ResetRun();
        CurrentDay = Math.Max(0, startDay);
    }

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
        EvaluationScore = 0;
        TotalTabooViolations = 0;
        TotalIncidents = 0;
        MissedRequiredDays = 0;
        LastShiftMissedRequired = 0;
        FinalAccusedId = "";
        _finalEvidence.Clear();
        WasProven = false;
        FinalReportSubmitted = false;
        RepairPowerAccident();     // 용량 복구 + 세 채널 ON
        ResetFacilityFaults();
        // 관리자 패드의 단서는 한 판 동안만 남는다.
        ClueBoard.ResetAll();
        // 도전과제도 회차용 집계(이번 판에 대화한 직원 등)만 비운다 —
        // **영구 해금 기록은 절대 초기화하지 않는다.**
        AchievementManager.Instance?.OnRunReset();
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

    // 이번 판의 결번을 정한다 — **결번을 뽑는 유일한 입구.**
    //
    // 대회용은 강아지 고정, 기본 모드는 예전처럼 무작위다. 뽑는 곳이 세 군데(새 게임 ·
    // 근무 시작 · 구형 2D 타이틀)라 각자 판단하게 두면 "대회용을 하다 기본 모드로
    // 갔는데 계속 강아지" 같은 일이 생긴다. 판단은 여기 한 줄에서만 한다.
    public void AssignSaboteurForMode(IEnumerable<string> employeeIds)
    {
        var pool = employeeIds?.ToList() ?? new List<string>();
        if (GameModes.FixedSaboteur && pool.Contains(GameModes.FixedSaboteurId))
        {
            SetSaboteur(GameModes.FixedSaboteurId);
            return;
        }
        // 고정 대상이 이번 근무에 없으면(명단에서 빠졌다면) 무작위로 되돌아간다 —
        // 결번이 비어 있는 판이 되는 것보다는 낫다.
        AssignRandomSaboteur(pool);
    }

    public void GoToNextDay()
    {
        CurrentDay += 1;
        // 가상 시뮬레이션(DAY0)에서 올린 복구율은 실적이 아니라 연습이다.
        // 실제 근무 첫날은 반드시 0% 에서 시작한다.
        // 교육 때 찍어 둔 단서도 마찬가지다 — 실제 근무의 패드는 비어서 시작한다.
        if (CurrentDay == 1)
        {
            CoreProgress = 0f;
            ClueBoard.ResetAll();
            // 가상 시뮬레이션은 **아무것도 남기지 않는다.** 직원 상태(스트레스 · 기절 · 관계)도,
            // 그날의 시설 로그와 대화 기록도 전부 교육의 것이다 —
            // 하나라도 넘어오면 "첫날인데 누가 이미 지쳐 있다" 가 된다.
            NSP.Facility.FacilitySimulation.Instance?.ResetRun();
            EventLog.Instance?.ClearAll();
            DialogueHistory.Instance?.ClearAll();
            // 자재와 보관 한도도 마찬가지다. 교육에서 쓰고 남은 자재가 그대로 넘어오면
            // 실제 근무 첫날의 시작 보유량이 그날 교육을 어떻게 했느냐에 따라 달라진다.
            // (교육은 보관 한도를 작게 잡아 자재 부족 상황을 만든다 — TutorialDirector.)
            Materials = Config.Instance.Data.MaterialsStart;
            MaterialsCap = Config.Instance.Data.MaterialsCapBase;
        }
        DayTimeSeconds = 0f;
        CurrentPhase = GamePhase.Prep;
        RepairPowerAccident();
        ResetFacilityFaults();
    }
}
