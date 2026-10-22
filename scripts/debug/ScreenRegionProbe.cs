using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.View;

namespace NSP.Debug;

// 화면의 한 영역에 실제로 그려지는 3D 노드가 무엇인지 찾는 탐침 — 창 모드로 실행한다.
//
//   godot --path . res://scenes/debug/ScreenRegionProbe.tscn -- <x0> <y0> <x1> <y1>
//
// 좌표는 기준 창(1700 × 880) 픽셀. 근무 배치 화면까지 들어간 뒤, 그 영역 안으로 투영되는
// VisualInstance3D(메시 · 라벨 · 빛)를 노드 경로와 함께 찍는다. "에디터 씬에는 없는데 게임을
// 켜면 생기는 물건" 이 어디서 왔는지 찾을 때 쓴다.
public partial class ScreenRegionProbe : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var a = OS.GetCmdlineUserArgs();
        var region = a.Length >= 4
            ? new Rect2(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]) - float.Parse(a[0]), float.Parse(a[3]) - float.Parse(a[1]))
            : new Rect2(1440, 590, 280, 210);
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        // DAY2 근무로 들어간다 — DAY1 배치 화면은 스토리 컷인이 모니터2 를 당긴다.
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance.GoToNextDay();
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        NSP.View.StoryCutinDirector.Instance?.Abort();
        var sim = NSP.Facility.FacilitySimulation.Instance;
        foreach (var id in sim.GetActiveEmployeeIds()) sim.AssignToRoom(id, "core_room");
        var map = NSP.Ui.ScheduleMapView.Instance;
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ControlRoom3DController.Instance.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        Day1HistoryOverlay.Instance?.CloseWindow();
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);

        var outDir = a.Length >= 5 ? a[4] : "";
        if (outDir.Length > 0)
        {
            GetViewport().GetTexture().GetImage()?.SavePng($"{outDir}/paint_on.png");
            if (AnalogPaint.Instance != null) AnalogPaint.Instance.Enabled = false;
            await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage()?.SavePng($"{outDir}/paint_off.png");
            if (AnalogPaint.Instance != null) AnalogPaint.Instance.Enabled = true;
        }
        var cam = GetViewport().GetCamera3D();
        var size = GetViewport().GetVisibleRect().Size;
        float sx = size.X / 1700f, sy = size.Y / 880f;
        var r = new Rect2(region.Position.X * sx, region.Position.Y * sy, region.Size.X * sx, region.Size.Y * sy);
        GD.Print($"\n######## 화면 영역 {r} 안의 3D 노드 ########");
        Walk(GetTree().Root, cam, r);
        GD.Print("######## 끝 ########");
        GetTree().Quit();
    }

    private static void Walk(Node n, Camera3D cam, Rect2 r)
    {
        if (n is VisualInstance3D vi && vi.IsVisibleInTree() && !(n is Camera3D))
        {
            var box = vi.GlobalTransform * vi.GetAabb();
            Vector3 c = box.GetCenter();
            var sb = new Rect2();
            bool any = false;
            for (int k = 0; k < 8; k++)
            {
                var corner = box.GetEndpoint(k);
                if (cam.IsPositionBehind(corner)) continue;
                var q = cam.UnprojectPosition(corner);
                sb = any ? sb.Expand(q) : new Rect2(q, Vector2.Zero);
                any = true;
            }
            if (any && box.Size.Length() < 3f)
            {
                Vector2 p = cam.IsPositionBehind(c) ? sb.GetCenter() : cam.UnprojectPosition(c);
                if (sb.Intersects(r))
                {
                    string extra = "";
                    if (vi is MeshInstance3D mi)
                    {
                        var mat = mi.MaterialOverride ?? mi.GetActiveMaterial(0);
                        if (mat is StandardMaterial3D sm && sm.EmissionEnabled)
                            extra = $" emission={sm.Emission} x{sm.EmissionEnergyMultiplier}";
                        extra += $" mesh={mi.Mesh?.GetType().Name}";
                    }
                    GD.Print($"  {vi.GetType().Name} {vi.GetPath()} center={c} size={box.Size} screen={p}" +
                             $" script={(vi.GetScript().AsGodotObject() as Resource)?.ResourcePath ?? ""}" +
                             $" owner={vi.Owner?.Name}{extra}");
                }
            }
        }
        foreach (var c in n.GetChildren()) Walk(c, cam, r);
    }
}
