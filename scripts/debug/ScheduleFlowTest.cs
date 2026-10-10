using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// Phase 0 — 실제 메인 씬에서 "배치 단계 → 근무 시작" 흐름이 이어지는지 본다.
//
//   godot --headless --path . res://scenes/debug/ScheduleFlowTest.tscn
//
// 개발용 "DAY1 바로 시작" 경로(ShiftFlowController._skipToDay1Pending)로 메인 씬을 띄운 뒤,
// 두 CRT 에 배치 콘솔이 걸렸는지 확인하고, 코어실에 한 명을 놓고 CRT 안의 "근무 시작"을 눌러
// 근무(Live)로 넘어가는지 본다.
public partial class ScheduleFlowTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        GD.Print("################ Phase 0 — 배치 → 근무 흐름 ################");
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        var scene = GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate();
        AddChild(scene);

        for (int i = 0; i < 240 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        var ctl = ControlRoom3DController.Instance;
        var sim = FacilitySimulation.Instance;
        Check(GameState.Instance?.CurrentPhase == GamePhase.Schedule, "배치 단계로 들어간다");
        await Frames(10);

        var bound = ctl?.Screens.Select(s => s.TargetViewport).ToList();
        Check(bound != null && bound.Contains(ctl.ScheduleMapViewport) && bound.Contains(ctl.ScheduleStaffViewport),
            "두 CRT 에 배치 콘솔(지도 · 직원 정보)이 걸린다");
        var board = scene.FindChild("DeskScheduleBoard", true, false) as Node3D;
        Check(board == null || !board.Visible, "책상 위 종이 배치표는 켜지지 않는다");

        var map = ScheduleMapView.Instance;
        Check(map != null, "배치 콘솔이 준비된다");
        if (map == null || sim == null) { Done(); return; }

        // 코어실에 한 명 → 근무 시작 활성.
        sim.AssignToRoom("wolf", "core_room");
        await Frames(3);
        Check(map.StartEnabled, "코어실에 배치하면 근무 시작 활성");

        // CRT 안의 버튼을 누른다 — 확대된 뷰포트 좌표로 바꿔 밀어 넣는다(실제 CRT 입력과 같은 경로).
        var local = map.StartButtonRect.GetCenter();
        var vpPos = map.GetGlobalTransformWithCanvas() * local;
        var vp = ctl.ScheduleMapViewport;
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);

        for (int i = 0; i < 400 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        Check(GameState.Instance?.CurrentPhase == GamePhase.Live, "근무 시작 → 실제 근무(Live)로 넘어간다");
        await Frames(10);
        bound = ctl.Screens.Select(s => s.TargetViewport).ToList();
        Check(bound.Contains(ctl.FacilityViewport), "근무 중에는 왼쪽 CRT 가 시설 모니터로 돌아온다");
        Check(sim.GetEmployeeState("wolf")?.AssignedRoomId == "core_room", "배치 결과가 근무로 그대로 넘어간다(늑대 = 코어실)");
        Done();
    }

    private void Done()
    {
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
