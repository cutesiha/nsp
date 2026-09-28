using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 관리자 패드 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/AdminPadShot.tscn -- <저장 폴더>
//
// DAY1 근무를 띄우고 Tab 과 같은 경로로 패드를 꺼내, 들어 올리는 도중 · 든 뒤 · 단서/직원 탭 ·
// 내려놓은 뒤를 PNG 로 남긴다. 그때마다 패드 상태와 위치를 출력한다.
public partial class AdminPadShot : Node
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

        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(30);

        var sim = FacilitySimulation.Instance;
        var map = ScheduleMapView.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || map == null || ctl == null) { GD.PrintErr("씬이 뜨지 않았다"); GetTree().Quit(); return; }

        var roster = sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Frames(5);
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var vp = ctl.ScheduleMapViewport;
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(3);

        var pad = AdminPad3D.Instance;
        if (pad == null) { GD.PrintErr("AdminPad3D 가 씬에 없다"); GetTree().Quit(); return; }
        Report("근무 중(패드 놓임)", pad);
        Save("pad_0_desk.png");
        // 근무 시작 때 저절로 뜨는 「오늘의 업무」 창을 닫는다(우클릭 = 닫기).
        Day1HistoryOverlay.Instance?._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        await Seconds(0.3);

        // Tab 과 같은 경로.
        ClueHud.Instance?._Input(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        await Seconds(0.25);
        Report("0.25초", pad);
        Save("pad_1_reach.png");
        await Seconds(0.5);
        Report("0.75초", pad);
        Save("pad_2_lift.png");
        await Seconds(1.0);
        Report("1.75초", pad);
        Save("pad_3_held.png");

        pad.View?.SwitchTab(PadView.Tab.Staff, fade: false);
        await Seconds(0.3);
        Save("pad_4_staff.png");
        pad.View?.SwitchTab(PadView.Tab.Manual, fade: false);
        await Seconds(0.3);
        Save("pad_5_manual.png");

        pad.Close();
        await Seconds(1.2);
        Report("내려놓은 뒤", pad);
        Save("pad_6_down.png");

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    private void Report(string label, AdminPad3D pad)
    {
        var cam = GetViewport().GetCamera3D();
        var body = pad.GetNodeOrNull<Node3D>("PadBody");
        var player = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;
        if (player != null)
            GD.Print($"   손바닥(왼)={player.PalmWorld(true)} 목표={pad.GripWorld} 차이={player.PalmWorld(true).DistanceTo(pad.GripWorld):0.000}m");
        GD.Print($"[{label}] open={pad.IsOpen} held={pad.IsHeld} pause={AdminPad3D.PausesGame} " +
                 $"body={body?.GlobalPosition} grip={pad.GripWorld} cam={cam?.GlobalPosition} " +
                 $"camFwd={-(cam?.GlobalTransform.Basis.Z ?? Vector3.Zero)} " +
                 $"onScreen={(cam != null && body != null && !cam.IsPositionBehind(body.GlobalPosition) ? cam.UnprojectPosition(body.GlobalPosition).ToString() : "뒤")}");
    }

    private void Save(string file)
    {
        var img = GetViewport().GetTexture().GetImage();
        img?.SavePng(_dir + "/" + file);
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
