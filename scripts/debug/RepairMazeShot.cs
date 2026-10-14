using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 수리 승인 요청 · 미로 화면 캡처.
//   godot --path . res://scenes/debug/RepairMazeShot.tscn -- <저장 폴더>
public partial class RepairMazeShot : Node
{
    private string _dir = "";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(2.0);

        var pad = AdminPad3D.Instance;
        pad.Open();
        for (int i = 0; i < 400 && !pad.IsHeld; i++) await Frame();
        await Seconds(0.4);

        var task = new SpawnedTask
        {
            TaskId = "repair", RoomId = "power_room", IsRepair = true,
            Status = SpawnedTaskStatus.Active, GaugeRequired = 20f, TimeLimitSeconds = float.MaxValue,
        };
        RepairApprovalSystem.Enqueue("power_room", "발전실", task);
        RepairApprovalSystem.Tick(0.1f);
        await Seconds(0.5);
        Save("repair_1_ask", pad);

        RepairApprovalSystem.Approve();
        await Seconds(0.5);
        Save("repair_2_maze", pad);

        // 몇 칸 걸어 자취를 남긴다.
        var m = RepairApprovalSystem.Maze;
        var path = m.ShortestPath(m.Start, m.Goal);
        for (int i = 1; i < Mathf.Min(5, path.Count); i++)
        {
            var s = path[i] - path[i - 1];
            RepairApprovalSystem.MazeInput(s == new Vector2I(0, -1) ? RepairMaze.Dir.Up
                : s == new Vector2I(0, 1) ? RepairMaze.Dir.Down
                : s == new Vector2I(-1, 0) ? RepairMaze.Dir.Left : RepairMaze.Dir.Right);
        }
        await Seconds(0.5);
        Save("repair_3_walking", pad);

        // 벽으로 밀어 실패 화면.
        foreach (var d in new[] { RepairMaze.Dir.Up, RepairMaze.Dir.Down, RepairMaze.Dir.Left, RepairMaze.Dir.Right })
            if (!m.IsOpen(m.Cursor, d)) { RepairApprovalSystem.MazeInput(d); break; }
        await Seconds(0.3);
        Save("repair_4_failed", pad);

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    private void Save(string name, AdminPad3D pad)
    {
        pad?.TargetViewport?.GetTexture()?.GetImage()?.SavePng($"{_dir}/{name}.png");
        GD.Print("   " + name);
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
