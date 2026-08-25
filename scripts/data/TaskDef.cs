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

    [Export] public bool HasNeglectConsequence = false;
    [Export] public float NeglectThresholdSeconds = 30f;
    [Export] public TabooConsequenceType NeglectConsequenceType = TabooConsequenceType.PowerOutage;
}
