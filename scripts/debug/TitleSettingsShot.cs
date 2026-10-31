using Godot;
using NSP.View;

namespace NSP.Debug;

// 시작 화면 「환경 설정」 이 모니터1 화면 안에서 열리는지 — 창 모드로 실행한다.
//
//   godot --path . res://scenes/debug/TitleSettingsShot.tscn -- <저장 폴더>
//
// 메뉴에서 환경 설정을 고르고, 화면 안의 [뒤로] 를 **실제 마우스 클릭처럼**(제어실 화면 좌표 →
// 3D 모니터 → 단말기 화면) 눌러 메뉴로 돌아오는지까지 본다.
public partial class TitleSettingsShot : Node
{
    private string _dir = "";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 1200 && TitleTerminalView.Instance?.CurrentMode
                 != TitleTerminalView.Mode.Standby; i++) await Frame();
        await Seconds(0.8);
        Press(Key.Space);
        for (int i = 0; i < 2400 && TitleRoomDirector.Instance?.AtMenu != true; i++) await Frame();
        await Seconds(0.8);

        // 커서를 「환경 설정」(2번)으로.
        for (int i = 0; i < 6 && TitleTerminalView.Instance?.SelectedId != "config"; i++)
        {
            Press(Key.S);
            await Seconds(0.25);
        }
        Ok(TitleTerminalView.Instance?.SelectedId == "config", "커서가 환경 설정에 있다");
        Press(Key.Space);
        await Seconds(1.2);
        Shot("01_settings_open");
        Ok(TitleRoomDirector.Instance?.AtMenu == false, "설정을 보는 동안 메뉴 조작은 쉰다");

        // 화면 안 [뒤로] 버튼을 찾아 그 자리를 실제로 클릭한다.
        var ctl = ControlRoom3DController.Instance;
        var vp = ctl?.TitleTerminalViewport;
        var back = FindButton(vp, "뒤로");
        Ok(back != null, "설정 화면이 모니터1 단말기 화면 안에 있다");
        if (back != null && ScreenPointOf(back, out Vector2 click))
        {
            Click(click);
            await Seconds(0.8);
            Shot("02_after_back");
            Ok(TitleRoomDirector.Instance?.AtMenu == true, "[뒤로] 클릭으로 메뉴로 돌아왔다");
        }

        // 한 번 더 열고 ESC 로 닫는다.
        Press(Key.Space);
        await Seconds(0.8);
        Press(Key.Escape);
        await Seconds(0.8);
        Ok(TitleRoomDirector.Instance?.AtMenu == true, "ESC 로 닫고 메뉴로 돌아왔다");

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    private static Button FindButton(Node n, string text)
    {
        if (n == null) return null;
        if (n is Button b && b.Text == text && b.IsVisibleInTree()) return b;
        foreach (var c in n.GetChildren())
            if (FindButton(c, text) is { } found) return found;
        return null;
    }

    // 단말기 화면 안 컨트롤의 가운데 → 제어실 화면 좌표.
    private bool ScreenPointOf(Control c, out Vector2 screen)
    {
        screen = Vector2.Zero;
        var ctl = ControlRoom3DController.Instance;
        var vp = c.GetViewport() as SubViewport;
        var cam = GetViewport().GetCamera3D();
        if (ctl == null || vp == null || cam == null) return false;
        foreach (var s in ctl.Screens)
        {
            if (s.TargetViewport != vp) continue;
            Vector2 px = c.GetGlobalTransformWithCanvas() * (c.Size * 0.5f);
            float u = px.X / vp.Size.X, v = px.Y / vp.Size.Y;
            Vector3 world = s.GlobalTransform * new Vector3(u - 0.5f, s.FlipV ? v - 0.5f : 0.5f - v, 0f);
            screen = cam.UnprojectPosition(world);
            return true;
        }
        return false;
    }

    private void Click(Vector2 at)
    {
        Input.WarpMouse(at);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
        Input.ParseInputEvent(new InputEventMouseButton
            { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
    }

    private static void Press(Key code)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = false });
    }

    private void Shot(string name)
    {
        GetViewport().GetTexture().GetImage()?.SavePng($"{_dir}/title_{name}.png");
        GD.Print($"   shot {name}");
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
