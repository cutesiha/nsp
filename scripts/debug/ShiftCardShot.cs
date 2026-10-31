using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Debug;

// DAY 시작 카드(ShiftCardView) 확인용 캡처 — 화면 정중앙에 뜨는가, 「DAY N」 이 KIMM 볼드로 크게 뜨는가.
//   godot --path . res://scenes/debug/ShiftCardShot.tscn -- <저장 폴더>
public partial class ShiftCardShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GameState.Instance?.ResetRun(1);
        var bg = new ColorRect { Color = new Color(0.10f, 0.14f, 0.16f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var layer = new CanvasLayer { Layer = 1 };
        AddChild(layer);
        layer.AddChild(bg);
        // 화면 가운데 십자선 — 카드가 정중앙에 있는지 눈으로 맞춰 본다.
        var h = new ColorRect { Color = new Color(1f, 0.3f, 0.3f, 0.6f), Position = new Vector2(0, 539), Size = new Vector2(1920, 2) };
        var v = new ColorRect { Color = new Color(1f, 0.3f, 0.3f, 0.6f), Position = new Vector2(959, 0), Size = new Vector2(2, 1080) };
        layer.AddChild(h);
        layer.AddChild(v);

        _ = ShiftCardView.Instance?.Present();
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(System.IO.Path.Combine(dir, "shift_card.png"));
        GD.Print($"saved {dir}/shift_card.png  size {img.GetSize()}");
        GetTree().Quit();
    }
}
