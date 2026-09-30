using Godot;
using NSP.View;

namespace NSP.Debug;

// 시작 화면 모니터2 — 직원 여섯 명의 신원 카드(증명사진) 캡처.
//
//   godot --path . res://scenes/debug/TitleStaffShot.tscn -- <저장 폴더>
public partial class TitleStaffShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        // 화면이 만들어질 때까지 기다렸다가, 타이틀 연출을 기다리지 않고 직접 켠다.
        for (int i = 0; i < 2000 && TitleStaffIdView.Instance == null; i++) await Frame();
        await Seconds(1.0);
        TitleStaffIdView.Instance?.PowerOn();
        // 카드가 한 장씩 뜨는 연출이 끝날 때까지 기다린다.
        await Seconds(5.0);
        GD.Print($"staffOn={TitleStaffIdView.Instance?.PoweredOn}");
        ControlRoom3DController.Instance?.TitleStaffViewport?.GetTexture()?.GetImage()
            ?.SavePng($"{dir}/title_staff.png");
        GetViewport().GetTexture().GetImage()?.SavePng($"{dir}/title_screen.png");
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
