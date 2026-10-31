using System.Threading.Tasks;
using Godot;
using NSP.View;

namespace NSP.Debug;

// 켤 때 경고 · 타이틀 모니터2 의 「출처」 창 · 엔딩 크레딧을 찍어 보는 확인 도구(창 모드).
//   godot --path . res://scenes/debug/CreditsProbe.tscn -- <저장 폴더>
public partial class CreditsProbe : Node
{
    private string _dir = "";

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        await Seconds(1.2);
        Shot("c0_warning");
        await Seconds(1.5);
        Key(Godot.Key.Space);                  // 경고 넘기기
        await Seconds(1.5);
        Key(Godot.Key.Space);                  // 타이틀 전원 켜기
        await Seconds(4.0);

        var staff = TitleStaffIdView.Instance;
        staff?.SetButtonHover(TitleStaffIdView.SourcesId);
        await Seconds(0.3);
        SaveTex(staff, "c1_staff_button");
        staff?.OpenSources();
        staff?.SetButtonHover(TitleStaffIdView.SourcesCloseId);
        await Seconds(0.3);
        SaveTex(staff, "c2_staff_sources");
        Shot("c3_title_full");
        staff?.CloseSources();

        var layer = new CanvasLayer { Layer = 118 };
        AddChild(layer);
        var roll = new CreditsRoll();
        layer.AddChild(roll);
        var done = roll.PlayAsync();
        double[] at = { 3.0, 9.0, 16.0, 24.0 };
        double t0 = Time.GetTicksMsec() / 1000.0;
        int n = 0;
        while (!done.IsCompleted)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            double t = Time.GetTicksMsec() / 1000.0 - t0;
            if (n < at.Length && t >= at[n]) { Shot($"c4_credits_{n}"); n++; }
            if (t > 120) break;
        }
        GD.Print($"[probe] credits {Time.GetTicksMsec() / 1000.0 - t0:0.0}s");
        GetTree().Quit();
    }

    private static void Key(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    private void SaveTex(Control view, string name)
    {
        var img = view?.GetViewport()?.GetTexture()?.GetImage();
        img?.SavePng(System.IO.Path.Combine(_dir, name + ".png"));
    }

    private void Shot(string name)
    {
        var img = GetViewport().GetTexture().GetImage();
        img.SavePng(System.IO.Path.Combine(_dir, name + ".png"));
    }

    private async Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
