using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 포스터용 고해상도 스틸 한 장을 명령 한 줄로 뽑는다. 창 모드로 실행한다(--headless 불가).
//
//   godot --path . res://scenes/debug/PosterShot.tscn -- <저장 폴더> [가로] [세로] [노출배수] [보조광]
//
// 기본 5000x7000:
//   godot --path . res://scenes/debug/PosterShot.tscn -- C:/poster
// i3 내장그래픽에서 메모리가 모자라 죽으면 A1 150dpi 로 낮춘다:
//   godot --path . res://scenes/debug/PosterShot.tscn -- C:/poster 3508 4961
// 책상 형태가 안 보일 때 — 이 컷만 노출 1.6배 + 보조광 0.35:
//   godot --path . res://scenes/debug/PosterShot.tscn -- C:/poster 5000 7000 1.6 0.35
//
// DAY1 근무 시작 직후 상태까지 게임을 실제로 진행시킨 뒤 찍는다 — 모니터 두 대에
// 평소의 화면(시설도 · CCTV)이 켜져 있는 그림이다. 게임 UI(시계 · POWER · 자막 · CAM
// 라벨 · Tab 아이콘)와 1인칭 팔은 PosterCamera 가 알아서 끈다.
public partial class PosterShot : Node
{
    private string _dir = "";
    private int _width = 5000;
    private int _height = 7000;
    private float _exposure = 1f;
    private float _fill = -1f;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://poster");
        if (args.Length > 2 && int.TryParse(args[1], out int w) && int.TryParse(args[2], out int h))
        {
            _width = w;
            _height = h;
        }
        // 안 넘기면 씬에 잡아 둔 인스펙터 값을 그대로 쓴다.
        if (args.Length > 3 && float.TryParse(args[3], out float ex)) _exposure = ex;
        if (args.Length > 4 && float.TryParse(args[4], out float fl)) _fill = fl;

        // 타이틀 · 프롤로그를 건너뛰고 DAY1 배치 화면으로 바로 들어간다.
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        // 배치를 채우고 근무를 시작한다 — DeskLayoutShot 과 같은 경로다.
        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance.GoToNextDay();
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        var map = ScheduleMapView.Instance;
        var roster = sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "core_room", "maintenance_room", "guard_room", "storage_room", "power_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Seconds(0.5);
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ctl.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Seconds(1.2);

        var cam = GetTree().Root.FindChild("PosterCamera", true, false) as PosterCamera;
        if (cam == null)
        {
            GD.PushError("[PosterShot] 메인 씬에서 PosterCamera 를 못 찾았다.");
            GetTree().Quit(1);
            return;
        }
        cam.CaptureResolution = new Vector2I(_width, _height);
        cam.OutputDirectory = _dir;
        if (_exposure > 0f) cam.ExposureBoost = _exposure;
        if (_fill >= 0f) cam.FillLightEnergy = _fill;
        string saved = await cam.CaptureAsync();
        GD.Print(saved != null ? "saved → " + saved : "[PosterShot] 저장 실패");
        GetTree().Quit(saved != null ? 0 : 1);
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
