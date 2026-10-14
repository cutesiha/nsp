using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 작업실 방 카드 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/RoomEffectShot.tscn -- <저장 폴더>
//
// DAY1 근무를 띄우고 **일부 방만 채운 채** 잠시 돌린 뒤, 일곱 방을 차례로 골라
// 왼쪽 모니터(방 카드)와 오른쪽 모니터(미니맵 · 시설 로그)를 PNG 로 남긴다.
// (a) 첫 줄과 (c) 붉은 줄이 방마다 실제로 뜨는지 눈으로 확인하기 위한 것이다.
public partial class RoomEffectShot : Node
{
    // 채울 방 — 나머지 넷(저장고 · 환기실 · 경비실 · 의무실)은 일부러 비워 둔다.
    private static readonly string[] Staffed = { "core_room", "core_room", "power_room", "maintenance_room" };

    private static readonly string[] Rooms =
    {
        "core_room", "power_room", "maintenance_room", "storage_room",
        "vent_room", "guard_room", "medical_room",
    };

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(60);

        var sim = FacilitySimulation.Instance;
        var map = ScheduleMapView.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || map == null || ctl == null) { GD.PrintErr("씬이 뜨지 않았다"); GetTree().Quit(); return; }

        var roster = sim.GetActiveEmployeeIds().ToList();
        for (int i = 0; i < Staffed.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], Staffed[i]);
        await Frames(5);

        // 배치 화면의 [근무 시작] 을 누른다.
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var vp = ctl.ScheduleMapViewport;
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);

        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();

        // 숫자가 실제로 쌓일 때까지 근무를 돌린다(코어·자재·경비 기록이 붙는다).
        await Seconds(35);

        foreach (string roomId in Rooms)
        {
            // 방을 고른다 — 교육에서 쓰는 강조 통로를 그대로 쓴다(강조는 꺼 둔다).
            FacilityMonitorView.HighlightRoomNumber(roomId, 0f);
            await Frames(8);
            string name = sim.GetRoomDef(roomId)?.DisplayName ?? roomId;
            Save(ctl.FacilityViewport, dir, $"roomcard_{roomId}.png");
            GD.Print($"   {name,-8} (a) {RoomEffectText.Headline(roomId)}");
            string idle = RoomEffectText.Idle(roomId);
            if (!string.IsNullOrEmpty(idle)) GD.Print($"            (c) {idle.Replace("\n", " / ")}");
        }

        // 시설 로그 — 방 효과 줄이 색으로 갈라지는지 보이는 화면.
        Day1HistoryOverlay.Instance?.OpenLog();
        await Frames(30);
        Save(GetViewport(), dir, "roomeffect_log.png");

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private static void Save(Viewport vp, string dir, string file)
        => vp.GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
