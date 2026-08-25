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
    VentRepair,
    Lighting,
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
}
