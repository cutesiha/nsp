namespace NSP.Data;

public enum GamePhase
{
    Prep,
    Schedule,
    Live,
    Settlement,
    Rest,
    Result,
}

public enum GameResult
{
    None,
    Clear,
    Fail,
}

public enum StatType
{
    Tech,
    Courage,
    Observation,
}

public enum PowerConsumer
{
    CctvWatch,
    VentRepair, // DAY1 신규 전력 패널(LIGHTING/CCTV/SENSOR)에는 없음 — 2D 백업 화면 호환용으로만 남김.
    Lighting,
    Sensor,
}

public enum RoomResourceType
{
    None,
    Power,
    Survival,
    Materials,
    Stress,
    Surveillance,
    CoreRepair,
    Storage,
    Isolation,
}

public enum TaskEffectType
{
    None,
    AddCoreProgress,
    AddMaterials,
    ReduceStress,
    BoostPowerCapacity,
    // 저장고 상시 업무: 자재 보유 한도를 EffectAmount 만큼 올린다(MaterialsCapMax 까지).
    RaiseMaterialsCap,
}

public enum TabooConditionType
{
    MaxHeadcountInRoom,
    MinHeadcountInRoomAfterHour,
    CodenameSpokenUnderRedLight,
    // ② 담력이 낮은 직원이 특정 방에서 혼자 근무 (params: room_id, max_courage, hold_seconds)
    LowCourageAloneInRoom,
    // ③ 종이 3번 울린 뒤 일정 시간 동안 직원 이동 금지 (params: window_seconds) — NotifyBellRang 훅
    MovementAfterBell,
    // ④ 조명이 꺼진 방의 직원에게 전화 (params 없음) — NotifyCallPlaced 훅
    CallEmployeeInDarkRoom,
    // ⑤ 같은 직원과 두 번 통화 (params 없음) — NotifyCallPlaced 훅
    CallSameEmployeeTwice,
    // ⑥ 같은 직원이 한 방에서 연속 근무 (params: room_id, hold_seconds)
    ContinuousWorkInRoom,
    // ⑦ 스트레스가 기준 이상인 직원 두 명을 동시에 치료 (params: room_id, stress_min, count)
    TreatTwoHighStress,
    // ⑧ 경비실 근무자가 혼자일 때 CCTV 연속 전환 (params: streak) — NotifyCctvSwitched 훅
    CctvSwitchStreakAlone,
    // ⑩ 자재가 기준 이상인데 저장고를 비워 둠(근무자 0명)
    //    (params: room_id, materials_min, hold_seconds)
    EmptyStorageWhenMaterialsHigh,
}

public enum TabooConsequenceType
{
    PowerOutage,
    CctvDisconnect,
    CorridorLock,
    ObservationCorruption,
    StressIncrease,
    PowerCapacityLoss,
    // FAIL-04: CCTV 시스템 전체가 강제 OFFLINE(전력을 줘도 수리 전까지 사용 불가).
    CctvSystemFault,
    // FAIL-03: 자재 생산 완전 정지(기존 자재는 사용 가능, 수리 완료 시 재개).
    MaterialsHalt,
    // FAIL-02: 환기 정지 → 전 직원 스트레스 수리 전까지 지속 상승.
    VentilationFault,
    // 의료 장비 오염 — 의무실 스트레스 치료가 수리 전까지 불가.
    MedicalContamination,
    // 봉쇄 코어 출력 불안정 — 코어 복구 정지 + 주기적으로 복구율 감소.
    CoreOutputUnstable,
    // 보관 선반 붕괴 — 자재 보유 한도 감소, 한도 초과분 즉시 파괴.
    StorageCollapse,

    // ── 금기 위반 페널티 ────────────────────────────────────────────────
    // 환기 일정 시간 중단 + 해당 직원 스트레스 증가.
    VentHaltAndStress,
    // 이동한 직원의 위치 파악·전화가 일정 시간 불가.
    TrackingLost,
    // 전화기 자체가 일정 시간 사용 불가 + 해당 직원 스트레스 증가.
    PhoneLockAndStress,
    // 직원이 아닌 목소리가 응답 — 통화가 일정 시간 잠긴다.
    PhoneImpostorLock,
    // 자재 감소 + 해당 직원 스트레스 증가.
    MaterialLossAndStress,
    // 치료 즉시 중단 + 대상 직원들 스트레스 증가.
    TreatmentAbortAndStress,
    // CCTV 채널이 뒤섞여 일정 시간 방 이름과 화면이 불일치.
    CctvChannelScramble,
    // 해당 직원들의 업무 속도 감소(일정 시간).
    WorkSpeedPenalty,
    // 저장 한도 감소 + 보유 자재 감소.
    StorageCapAndMaterialLoss,
}

public enum SaboteurActionType
{
    NormalWork,
    UnauthorizedMove,
    FalseOrder,
    Sabotage,
    KillAttempt,
}

public enum LogEventType
{
    RoomEnter,
    RoomExit,
    TaskStart,
    TaskEnd,
    Relocation,
    TabooViolation,
    PowerOutage,
    CctvDisconnect,
    FalseOrderFollowed,
    Death,
    Isolation,
    Sabotage,
    Neglect,
    TaskComplete,
    // 새 업무/문제가 작업실에 발생함. TaskStart(직원이 실제로 업무 수행을 시작함)와 의미가 다르다.
    TaskSpawned,
    // 발생한 업무를 제한시간 안에 처리하지 못해 사고로 이어짐(큰 붉은 배너 + 공포 연출 대상).
    // Neglect(미완료 상태로 자리 이탈 — 조용한 추리 단서)와 구분한다.
    TaskFailed,
    // 사용 가능한 전력 용량이 실제로 바뀐 순간(전후 값 포함). 사고 자체가 아니라 그 결과이므로
    // 수신 전화(IncomingCallDirector)는 이 종류를 듣지 않는다.
    PowerCapacityChanged,
    // 자원이 모자라 업무가 멈추거나 다시 돌기 시작한 순간(상태가 바뀔 때 한 번만).
    ResourceShortage,
}
