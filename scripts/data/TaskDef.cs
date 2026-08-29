using Godot;

namespace NSP.Data;

[GlobalClass]
public partial class TaskDef : Resource
{
    [Export] public string TaskId = "";
    [Export] public string DisplayName = "";
    [Export] public string RoomId = "";
    [Export] public StatType RequiredStat = StatType.Tech;
    [Export] public int Priority = 1;
    [Export] public int RecommendedHeadcount = 1;

    [Export] public float GaugeRequired = 20f;
    [Export] public TaskEffectType EffectType = TaskEffectType.None;
    [Export] public float EffectAmount = 0f;

    // 이 업무가 스폰됐을 때 제한시간(초). 시간 내 게이지를 못 채우면 처리 실패로 간주한다.
    // Recurring(상시) 스폰에는 적용되지 않는다 — 상시 업무는 제자리에서 계속 순환한다.
    [Export] public float TimeLimitSeconds = 25f;

    [Export] public bool HasNeglectConsequence = false;
    [Export] public float NeglectThresholdSeconds = 30f;
    [Export] public TabooConsequenceType NeglectConsequenceType = TabooConsequenceType.PowerOutage;
    [Export] public float NeglectConsequenceAmount = 10f;
}
