using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.View;

namespace NSP.Debug;

// [임시] 실제 게임 월드에서 방 마감이 붙었는지 확인한다.
public partial class DressProbe : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 2400 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.5);

        var world = GetTree().Root.FindChild("FacilityCctvWorld", true, false) as Node3D;
        GD.Print($"PROBE world={(world == null ? "없음" : world.GetPath().ToString())}");
        if (world == null) { GetTree().Quit(); return; }

        var rooms = typeof(FacilityCctvWorld)
            .GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(world)
            as System.Collections.Generic.Dictionary<string, Node3D>;
        GD.Print($"PROBE rooms={(rooms == null ? -1 : rooms.Count)}");
        if (rooms != null)
            foreach (var (id, node) in rooms)
                GD.Print($"PROBE  {id} 마감={(node.GetNodeOrNull("RoomDressing") != null ? "붙음" : "없음")} " +
                         $"자식={node.GetChildCount()}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), "timeout");
}
