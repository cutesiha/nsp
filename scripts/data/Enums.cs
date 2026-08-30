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
}

public enum TabooConditionType
{
    MaxHeadcountInRoom,
    MinHeadcountInRoomAfterHour,
    CodenameSpokenUnderRedLight,
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
}
