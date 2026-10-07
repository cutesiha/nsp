using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.View;

namespace NSP.Debug;

public partial class MatHintShotTmp : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1800 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.5);

        var vp = ControlRoom3DController.Instance?.FacilityViewport;
        GD.Print($"DIAG shotVp={vp?.GetInstanceId()} path={vp?.GetPath()}");
        int n = 0;
        Walk(GetTree().Root, ref n);
        FacilityMonitorView.HighlightMaterials(true);
        await Seconds(0.5);
        vp?.GetTexture()?.GetImage()?.SavePng($"{dir}/mat_on.png");
        GD.Print("saved");
        GetTree().Quit();
    }

    private static void Walk(Node n, ref int count)
    {
        if (n is FacilityMonitorView v)
        {
            count++;
            GD.Print($"DIAG view#{count} path={v.GetPath()} vp={v.GetViewport()?.GetInstanceId()} isInstance={v == FacilityMonitorView.Instance}");
        }
        foreach (var c in n.GetChildren()) Walk(c, ref count);
    }

    private async System.Threading.Tasks.Task Frame() =>
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), "timeout");
}
