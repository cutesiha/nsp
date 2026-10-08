using Godot;
using NSP.View;

namespace NSP.Debug;

// 트루엔딩 컷씬 무대 확인용 캡처.
//   godot --path . res://scenes/debug/EndingStageShot.tscn -- <저장 폴더> [set]
public partial class EndingStageShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        string which = args.Length > 1 ? args[1] : "restraint";

        var layer = new CanvasLayer { Layer = 100 };
        AddChild(layer);
        var stage = new EndingCutsceneStage();
        AddChild(stage);
        stage.Attach(layer);
        stage.Screen.Modulate = Colors.White;

        await Seconds(0.3);

        if (which == "restraint")
        {
            stage.ShowSet(EndingCutsceneStage.Set.Restraint);
            stage.Offender?.Spawn("wolf");
            await Seconds(0.6);
            stage.SetCam(new Vector3(0f, 1.05f, 1.75f), new Vector3(0f, 0.78f, 0f), 50f);
            await Seconds(0.6); Shot(dir, "r1_close");
            Report(stage.Offender);
            _ = stage.Offender.StruggleSequence();
            await Seconds(1.0); Shot(dir, "r2_struggle");
            await Seconds(2.6);
            stage.Offender?.RevealFace();
            await stage.Offender.LiftHead(1.2);
            await Seconds(0.3); Shot(dir, "r3_face");
            stage.SetCam(new Vector3(0f, 1.35f, 5.4f), new Vector3(0f, 0.85f, 0f), 55f);
            await Seconds(0.5); Shot(dir, "r4_bars");
            stage.SetCam(new Vector3(0f, 1.28f, 0.72f), new Vector3(0f, 1.24f, 0f), 42f);
            await Seconds(0.4); Shot(dir, "r5_faceclose");
        }
        else if (which == "core")
        {
            stage.ShowSet(EndingCutsceneStage.Set.CoreHall);
            stage.CoreReset();
            stage.SetCam(new Vector3(0f, 2.2f, 17f), new Vector3(0f, 8f, -3f), 60f);
            await Seconds(0.5); Shot(dir, "c1_dark");
            for (int i = 0; i < 10; i++) stage.CoreFloorLamp(i);
            stage.CoreAlignRings(1.0);
            stage.CoreClamp(0.8);
            stage.CoreCharge(5.5f, 1.2);
            await Seconds(1.8); Shot(dir, "c2_charged");
            stage.SetCam(new Vector3(0f, 1.2f, 9f), new Vector3(0f, 8.2f, -3f), 68f);
            await Seconds(0.5); Shot(dir, "c3_low");
        }
        else
        {
            stage.ShowSet(EndingCutsceneStage.Set.Corridor);
            stage.CorridorReset();
            stage.SetCam(new Vector3(0f, 1.7f, 8f), new Vector3(0f, 1.8f, -20f), 55f);
            await Seconds(0.5); Shot(dir, "m1_corridor_off");
            for (int i = 0; i < 4; i++) stage.DoorLamp(i);
            await Seconds(0.4); Shot(dir, "m2_corridor_on");
            stage.BulkheadClose(0.8); await Seconds(1.0); stage.BulkheadLocked();
            stage.SetCam(new Vector3(0f, 1.8f, -17f), new Vector3(0f, 2.2f, -24f), 55f);
            await Seconds(0.5); Shot(dir, "m3_bulkhead");
            stage.PanelPower(2.4f);
            stage.PanelShowGuide(NSP.Prologue.GuideArt.Portrait("smile", out _));
            stage.SetCam(new Vector3(0.4f, 1.9f, 6.2f), new Vector3(2.4f, 1.9f, 4f), 50f);
            await Seconds(0.5); Shot(dir, "m4_panel");
            stage.MachinePower(); stage.MachineRun(true);
            stage.SetCam(new Vector3(1.55f, 2.15f, 15.4f), new Vector3(-0.2f, 1.15f, 11f), 54f);
            await Seconds(0.5); Shot(dir, "m5_machine");
        }

        GD.Print("saved");
        GetTree().Quit();
    }

    private static void Report(Node root)
    {
        foreach (var c in Walk(root))
            if (c is MeshInstance3D mi && mi.GlobalPosition.Y > 1.30f)
                GD.Print($"HIGH {mi.Name} parent={mi.GetParent().Name} y={mi.GlobalPosition.Y:0.00} pos={mi.GlobalPosition}");
    }

    private static System.Collections.Generic.IEnumerable<Node> Walk(Node n)
    {
        foreach (var c in n.GetChildren()) { yield return c; foreach (var g in Walk(c)) yield return g; }
    }

    private void Shot(string dir, string name) =>
        GetViewport().GetTexture().GetImage()?.SavePng($"{dir}/{name}.png");

    private async System.Threading.Tasks.Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), "timeout");
}
