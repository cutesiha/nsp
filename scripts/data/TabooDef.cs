using Godot;
using Godot.Collections;

namespace NSP.Data;

[GlobalClass]
public partial class TabooDef : Resource
{
    [Export] public string TabooId = "";
    [Export] public string Description = "";

    [Export] public TabooConditionType ConditionType = TabooConditionType.MaxHeadcountInRoom;
    [Export] public Dictionary ConditionParams = new();

    [Export] public TabooConsequenceType ConsequenceType = TabooConsequenceType.PowerOutage;
    [Export] public Dictionary ConsequenceParams = new();
}
