using Godot;

namespace NSP.Data;

[GlobalClass]
public partial class ConfigData : Resource
{
    [Export] public int MaxDays = 5;
    [Export] public int MaxEmployees = 6;

    // --- V3 초반 단순화: 시스템 해금 날짜 (DayFeatures 가 유일하게 읽는다) -------
    // DAY1 은 "배치 → 작업실 운영 → 사고/방해 → 로그·대화 단서 → 방해자 추리 → 코어 복구"
    // 만 남긴다. 아래 값을 1 로 내리면 해당 시스템이 DAY1 부터 다시 켜진다(코드는 그대로다).
    // 작업실(환기실·의무실)의 해금 날짜는 RoomDef.UnlockDay 에서 방마다 따로 지정한다.
    [Export] public int StatsUnlockDay = 2;    // 기술 / 담력 / 관찰
    [Export] public int StressUnlockDay = 2;   // 스트레스 수치 + 기절
    [Export] public int TabooUnlockDay = 2;    // 오늘의 금기

    // --- 오늘의 기분상태 -----------------------------------------------------
    // "잘 모르겠음 / 별생각 없음" 같은 모호한 표현을 한 DAY 에 쓸 수 있는 최대 인원.
    [Export] public int MoodVagueMaxPerDay = 2;

    // --- 스트레스 : 1 ~ 50 구간제 ------------------------------------------
    //  1~10  정상   업무 속도 100%
    // 11~30  주의   업무 속도  85%
    // 31~45  위험   업무 속도  65%
    // 46~50  기절   강제 의무실 송환, 당일 업무 불가
    [Export] public float StressMax = 50f;
    [Export] public float StressMin = 1f;
    [Export] public float StressCautionFrom = 11f;
    [Export] public float StressDangerFrom = 31f;
    [Export] public float StressFaintFrom = 46f;
    // 기절 → 의무실에서 이 시간(초) 뒤 회복해 원래 배치로 복귀. 0 이하면 예전처럼 당일 복귀 없음.
    [Export] public float StressFaintRecoverySeconds = 45f;
    // 회복 직후 스트레스 값(다시 바로 기절하지 않게 '주의' 구간 근처로 내려 둔다).
    [Export] public float StressAfterRecovery = 20f;
    [Export] public float StressWorkRateNormal = 1.00f;
    [Export] public float StressWorkRateCaution = 0.85f;
    [Export] public float StressWorkRateDanger = 0.65f;
    [Export] public int MurderMaxTotal = 2;
    [Export] public int MurderMaxPerDay = 1;
    [Export] public int ActiveTabooCountPerDay = 2;

    [Export] public float ApiTimeoutSeconds = 8f;
    [Export] public int ApiMaxRetries = 1;
    [Export] public string AiModelId = "claude-3-5-haiku-20241022";
    [Export] public int AiMaxTokens = 300;

    // 근무 시작 직후 배치된 자리로 처음 걸어가는 속도.
    [Export] public float EmployeeMoveSpeed = 80f;
    // 자리를 잡은 뒤(재배치 / 사고 확인 이동 등) 근무 중 이동 속도 — 훨씬 느리다.
    [Export] public float EmployeeMoveSpeedInShift = 42f;
    [Export] public float DayLengthSeconds = 180f;

    // DAY1 은 시스템을 배우는 날이라 대형 작업실 사고가 겹치지 않게 한다.
    // 활성 사고가 이 수 이상이거나, 직전 사고에서 이 시간이 지나지 않으면 새 사고를 미룬다.
    [Export] public int Day1MaxActiveIncidents = 1;
    // 위 동시 사고 제한을 며칠째까지 유지하는가. 초반 단순화 규칙을 DAY5 까지 그대로 쓴다(5).
    [Export] public int IncidentLimitLastDay = 5;
    [Export] public float IncidentGapSeconds = 18f;

    // --- 업무 수행 속도 --------------------------------------------------
    // 게이지는 1초에 BaseTaskWorkRate × 기술배율 × 스트레스배율 만큼 찬다.
    // BaseTaskWorkRate = 1 이므로 TaskDef.GaugeRequired 값이 곧 "능력 보통(기술2) 1명이
    // 정상 스트레스로 처리할 때 걸리는 초"가 된다 — 기획 수치를 그대로 적어 넣을 수 있다.
    [Export] public float BaseTaskWorkRate = 1.0f;

    // --- 능력치 3종의 효과 (index = 능력치 값 0~3) ---------------------------
    // 기술   : 업무 속도            1칸 80% / 2칸 100% / 3칸 120%
    [Export] public float[] TechWorkRate = { 0.8f, 0.8f, 1.0f, 1.2f };
    // 담력   : 스트레스 획득량       1칸 100% / 2칸 80% / 3칸 60%
    [Export] public float[] CourageStressGain = { 1.0f, 1.0f, 0.8f, 0.6f };
    // 관찰   : 단서 포착 확률        1칸 40% / 2칸 65% / 3칸 90%
    [Export] public float[] ObservationClueChance = { 0.4f, 0.4f, 0.65f, 0.9f };

    // LIGHTING/CCTV/SENSOR 전력 패널 — 슬롯 3개, 사고로 발전 용량이 줄면 동시에 켤 수 있는
    // 개수도 그만큼 줄어든다(코스트 가중치 없음, 채널당 슬롯 1개).
    [Export] public int PowerCapacityMax = 3;
    // SENSOR 예고가 "임박"으로 뜨기 시작하는 남은시간 임계값(초).
    [Export] public float AlertLeadSeconds = 20f;
    // 발전실 금기 이상현상 시 발전실에 있던 두 직원에게 즉시 더해지는 스트레스.
    [Export] public float PowerTabooStress = 8f;

    // 2D 백업 화면(PowerBudgetPanel)에서만 쓰는 옛 코스트 가중치 값 — 3D 전력 패널은 안 씀.
    [Export] public int PowerBudgetTotal = 7;
    [Export] public int PowerCostCctvWatch = 3;
    [Export] public int PowerCostVentRepair = 4;
    [Export] public int PowerCostLighting = 3;

    [Export] public int IsolationCapacity = 1;

    [Export] public float SaboteurDecisionIntervalSeconds = 4f;
    [Export] public float SaboteurSabotageChance = 0.35f;
    [Export] public float KillAttemptChance = 0.3f;
    [Export] public float SabotageTaskGaugeLoss = 5f;

    // --- 방해공작 6종의 수치 (조건은 FacilitySimulation.TickSaboteur 가 판정) ---------
    [Export] public float SabotageCoreLoss = 3f;           // 복구 작업 방해   : 코어 -3%
    [Export] public float SabotageCoreLossBlackout = 4f;   // 복구 작업 방해 2 : 코어 -4% (CCTV+조명 OFF)
    [Export] public int SabotagePowerLoss = 1;             // 전력 조작        : 전력 -1
    [Export] public float SabotageCctvBlockSeconds = 20f;  // CCTV 방해        : 그 방 20초 차단
    [Export] public int SabotageMaterialLoss = 4;          // 자재 폐기        : 자재 -4
    [Export] public float KillAttemptSeconds = 8f;         // 조건부 살인      : 8초간 범행 시도
    // 경비실에 근무자가 있으면 방해공작 성공 확률이 이 배율만큼 낮아진다(40% 감소).
    [Export] public float SurveillanceSaboteurChanceMultiplier = 0.6f;

    // --- 자재 --------------------------------------------------------------
    [Export] public int MaterialsStart = 20;        // 게임 시작 보유량
    [Export] public int MaterialsCapBase = 30;      // 기본 보유 한도
    [Export] public int MaterialsCapMax = 60;       // 저장고로 늘릴 수 있는 상한
    [Export] public int MaterialsPerCoreGauge = 2;  // 코어 복구 1회당 소모 자재

    // --- 작업실 무인 방치 → 사고 -------------------------------------------
    // 근무자가 한 명도 없는 상태가 이 시간을 넘기면 그 방의 사고가 발생한다.
    // 방마다 다르게 하려면 RoomDef.UnstaffedAccidentSeconds 를 0 보다 크게 준다.
    [Export] public float UnstaffedAccidentSecondsDefault = 75f;

    // --- 작업실 상시 효과 ---------------------------------------------------
    // 야간 근무 자체의 피로 — 배치된 직원 전원이 이 주기마다 조금씩 오른다.
    // 0 이하면 이 항목이 통째로 꺼진다(예전 동작).
    [Export] public float ShiftStressIntervalSeconds = 18f;
    [Export] public float ShiftStressAmount = 1f;
    // 자기 작업실에 수리해야 할 사고가 열려 있는 동안의 추가 압박.
    [Export] public float IncidentStressIntervalSeconds = 12f;
    [Export] public float IncidentStressAmount = 1.5f;
    // 환기실 무인: 이 주기마다 전 직원 스트레스 +1.
    [Export] public float VentUnstaffedStressIntervalSeconds = 15f;
    [Export] public float VentUnstaffedStressAmount = 1f;
    // 환기 필터 고장(사고): 이 주기마다 전 직원 스트레스 +2.
    [Export] public float VentFaultStressIntervalSeconds = 10f;
    [Export] public float VentFaultStressAmount = 2f;
    // 봉쇄 코어 출력 불안정(사고): 이 주기마다 코어 복구율 -1%.
    [Export] public float CoreUnstableIntervalSeconds = 20f;
    [Export] public float CoreUnstableCoreLoss = 1f;
    // 정비 설비 고장(사고) 발생 순간 잃는 자재.
    [Export] public int MaintenanceFaultMaterialLoss = 4;
    // 보관 선반 붕괴(사고)로 줄어드는 보유 한도.
    [Export] public int StorageCollapseCapLoss = 10;

    // 발생 업무가 완료/실패한 뒤 방 카드에 결과 배지를 몇 초 더 보여줄지.
    [Export] public float ResolvedTaskDisplaySeconds = 2.5f;

    // --- 직원 관계 (docs/NSP_관계시스템_설계.md) -----------------------------
    // 관계 시드 테이블. 게임 시작 · 처음부터 다시 시작 때 RelationshipSystem.Load 로 읽는다.
    [Export(PropertyHint.File, "*.tres")] public string RelationshipTablePath = "res://data/relationships/relationships.tres";
    // 불편(Uneasy) 쌍이 같은 방에서 근무할 때의 소프트 페널티. 배치는 막지 않는다.
    //  · 업무 속도 배율 — 그 방에 불편 쌍이 하나 있을 때마다 곱해진다.
    [Export] public float UneasyEfficiencyMultiplier = 0.85f;
    //  · 긴장(스트레스): 이 주기마다 두 사람 스트레스 +. 스트레스가 잠긴 날에는 움직이지 않는다.
    [Export] public float UneasyStressIntervalSeconds = 20f;
    [Export] public float UneasyStressAmount = 1f;
    //  · 말다툼: 이 주기마다 한 번 굴려 성공하면 시설 로그에 언쟁이 남고 두 사람 스트레스 +.
    [Export] public float UneasyArgumentCheckSeconds = 30f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float UneasyArgumentChance = 0.35f;
    [Export] public float UneasyArgumentStress = 2f;
    // CCTV 로 엿듣는 같은 방 두 사람의 대화(Phase 2). 문장 · 간격은 이 파일 안에 있다.
    [Export(PropertyHint.File, "*.tres")] public string OverheardLinesPath = "res://data/relationships/overheard_lines.tres";

    // ── 심문 스탠딩 일러 CRT 셰이더 ──────────────────────────────────────
    // 일러 셰이더는 "청록 톤 + 어둡게 + 중앙 발광"만 맡는다. 스캔라인 · 그레인은 모니터
    // 셰이더(crt_screen.gdshader)가 이미 그리므로 여기서는 끈다(겹치면 모아레).
    [Export(PropertyHint.File, "*.gdshader")] public string StandingShaderPath = "";   // 비우면 스탠딩 셰이더 끔. 다시 켜려면 res://shaders/nsp_crt_glow_standing.gdshader
    [Export] public float StandingScanAmt = 0f;
    [Export] public float StandingGrainAmt = 0f;
    // 긴장 순간(심문 중 모순 추궁 성립) — 더 어둡게 + 빛이 확 번진다. 잠시 뒤 원래 값으로 돌아간다.
    [Export] public float StandingTensionDarkness = 0.6f;
    [Export] public float StandingTensionGlowAmt = 1.5f;
    [Export] public float StandingTensionHoldSeconds = 2.5f;
    [Export] public float StandingTensionFadeSeconds = 1.2f;
}
