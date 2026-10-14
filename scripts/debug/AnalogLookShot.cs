using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 아날로그 호러 필터(AnalogFilter) 비교용 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . --resolution 1665x937 res://scenes/debug/AnalogLookShot.tscn -- <저장 폴더>
//
// 메인 씬을 "DAY1 바로 시작" 경로로 띄워 근무(Live) 화면까지 간 뒤, 같은 순간을
// 필터 끔(analog_raw.png) / 켬(analog_on.png) 두 장으로 저장하고 종료한다.
public partial class AnalogLookShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(60);

        var ctl = ControlRoom3DController.Instance;
        var map = ScheduleMapView.Instance;
        var sim = FacilitySimulation.Instance;
        if (map != null && sim != null && ctl != null)
        {
            sim.AssignToRoom("wolf", "core_room");
            await Frames(3);
            var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
            var vp = ctl.ScheduleMapViewport;
            vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
            await Frame();
            vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        }
        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Frames(120);
        Day1HistoryOverlay.Instance?.CloseWindow();

        // 숨쉬기를 멈춰 카메라를 "자리에 앉은 기본 시점" 에 고정한다 — 손그림 레퍼런스가 그 시점이다.
        if (GetTree().Root.FindChild("PlayerSeatRig", true, false) is SeatedCameraRig rig) rig.IdleMotionEnabled = false;
        await Frames(30);

        // 같은 순간을 필터 끔 / 켬으로. 끄고 켜는 사이 장면이 거의 움직이지 않게 프레임을 짧게 둔다.
        //   analog_raw     : 필터 · 손그림 끔(게임 원래 화면)
        //   analog_nohud   : 위와 같은데 HUD 도 끔(손그림을 뽑을 때 HUD 자리를 가려내는 용도)
        //   analog_nopaint : 필터만 켬
        //   analog_on      : 필터 + 손그림
        if (AnalogFilter.Instance != null) AnalogFilter.Instance.Enabled = false;
        await Frames(3);
        Save(dir, "analog_raw.png");
        var hud = new System.Collections.Generic.List<CanvasLayer>();
        foreach (var n in GetTree().Root.FindChildren("*", "CanvasLayer", true, false))
            if (n is CanvasLayer cl && cl.Visible && cl.GetViewport() == GetTree().Root && cl != AnalogFilter.Instance) { cl.Visible = false; hud.Add(cl); }
        await Frames(3);
        Save(dir, "analog_nohud.png");
        foreach (var cl in hud) cl.Visible = true;
        if (AnalogFilter.Instance != null) AnalogFilter.Instance.Enabled = true;
        if (AnalogPaint.Instance != null) AnalogPaint.Instance.Enabled = false;
        await Frames(3);
        Save(dir, "analog_nopaint.png");
        if (AnalogPaint.Instance != null) AnalogPaint.Instance.Enabled = true;
        await Frames(3);
        Save(dir, "analog_on.png");

        // analog_barepaint : 3D 만(HUD · 오버레이 끔) + 손그림, 필터는 효과 0 — 손그림 텍스처를 다듬을 때 쓴다
        var fm = GD.Load<ShaderMaterial>("res://shaders/analog_vhs_material.tres");
        var keep = new System.Collections.Generic.Dictionary<string, Variant>();
        foreach (var k in new[] { "lut_amount", "grain", "aberration", "vignette" }) { keep[k] = fm.GetShaderParameter(k); fm.SetShaderParameter(k, 0f); }
        var hidden = new System.Collections.Generic.List<CanvasLayer>();
        foreach (var n in GetTree().Root.FindChildren("*", "CanvasLayer", true, false))
            if (n is CanvasLayer cl && cl.Visible && cl.GetViewport() == GetTree().Root && cl != AnalogFilter.Instance) { cl.Visible = false; hidden.Add(cl); }
        await Frames(3);
        Save(dir, "analog_barepaint.png");
        foreach (var cl in hidden) cl.Visible = true;
        foreach (var kv in keep) fm.SetShaderParameter(kv.Key, kv.Value);
        await Frames(2);

        PrintCorners();
        if (System.Array.IndexOf(args, "rays") >= 0) await DumpRays(dir);
        if (System.Array.IndexOf(args, "views") >= 0) await OtherViews(dir);
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // 기본 시점이 아닐 때도 손그림이 물건에 붙어 따라가는지 — 모니터 확대 · 패드 들기 · 고개 돌리기.
    private async System.Threading.Tasks.Task OtherViews(string dir)
    {
        if (GetTree().Root.FindChild("PlayerSeatRig", true, false) is not SeatedCameraRig rig) return;
        if (GetTree().Root.FindChild("M01_Screen", true, false) is Node3D scr)
        {
            rig.FocusOnScreen(scr.GlobalPosition, scr.GlobalTransform.Basis.Z.Normalized(), 0.75f, 0.01f);
            await Frames(40);
            Save(dir, "view_zoom_m01.png");
            rig.ReturnToSeat(0.01f);
            await Frames(30);
        }
        if (GetTree().Root.FindChild("Telephone", true, false) is Node3D phone)
        {
            rig.TurnToLookAt(phone.GlobalPosition + new Vector3(-0.35f, 0f, 0f), 0.01f);
            await Frames(40);
            Save(dir, "view_turn.png");
            rig.ReturnToSeat(0.01f);
            await Frames(30);
        }
        AdminPad3D.Instance?.Toggle();
        await Frames(90);
        Save(dir, "view_pad.png");
        AdminPad3D.Instance?.Toggle();
        await Frames(60);
    }

    // 모니터 베젤 앞면 네 귀퉁이의 화면 좌표 — 레퍼런스 이미지의 선 위치를 3D 좌표로 되돌릴 때 쓴다.
    private void PrintCorners()
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        foreach (var name in new[] { "M01_Bezel", "M02_Bezel" })
        {
            if (GetTree().Root.FindChild(name, true, false) is not MeshInstance3D mi) continue;
            var sb = new System.Text.StringBuilder($"[corners] {name}");
            foreach (var (x, y) in new[] { (-0.5f, 0.5f), (0.5f, 0.5f), (0.5f, -0.5f), (-0.5f, -0.5f) })
            {
                var p = cam.UnprojectPosition(mi.GlobalTransform * new Vector3(x, y, 0.5f));
                sb.Append($" ({p.X:0.0},{p.Y:0.0})");
            }
            GD.Print(sb.ToString());
            GD.Print($"[xform] {name} {mi.GlobalTransform}");
        }
        GD.Print($"[camera] {cam.GlobalTransform} fov {cam.Fov}");
    }

    // 화면 픽셀마다 카메라 광선이 처음 닿는 3D 지점과 그 물체 — 레퍼런스에 손으로 칠한 그림자가
    // "어느 물체의 어느 자리" 인지 되돌리는 데 쓴다. 메시마다 충돌체를 임시로 만들어 쏜다.
    //   rays.bin : 저장 이미지의 모든 픽셀(가운데)마다 float32 월드x, 월드y, 월드z, int32 노드번호(-1 = 허공)
    //   rays_nodes.txt : 노드번호 → 경로, 그리고 카메라 자세
    private async System.Threading.Tasks.Task DumpRays(string dir)
    {
        var cam = GetViewport().GetCamera3D();
        if (cam == null) return;
        foreach (var mi in GetTree().Root.FindChildren("*", "MeshInstance3D", true, false))
            if (mi is MeshInstance3D m && m.IsVisibleInTree() && m.Mesh != null && m is not AnalogPaint) m.CreateTrimeshCollision();
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        var space = cam.GetWorld3D().DirectSpaceState;
        var img = GetViewport().GetTexture().GetImage();
        var vis = GetViewport().GetVisibleRect().Size;
        int w = img.GetWidth(), h = img.GetHeight();
        float kx = vis.X / w, ky = vis.Y / h;
        var nodes = new System.Collections.Generic.Dictionary<string, int>();
        using var fs = System.IO.File.Create(dir + "/rays.bin");
        using var bw = new System.IO.BinaryWriter(fs);
        var q = new PhysicsRayQueryParameters3D();
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var sp = new Vector2((x + 0.5f) * kx, (y + 0.5f) * ky);
            var from = cam.ProjectRayOrigin(sp);
            q.From = from;
            q.To = from + cam.ProjectRayNormal(sp) * 20f;
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) { bw.Write(0f); bw.Write(0f); bw.Write(0f); bw.Write(-1); continue; }
            var pos = (Vector3)hit["position"];
            var owner = ((Node)(GodotObject)hit["collider"]).GetParent();
            string path = owner.GetPath().ToString();
            if (!nodes.TryGetValue(path, out int id)) { id = nodes.Count; nodes[path] = id; }
            bw.Write(pos.X); bw.Write(pos.Y); bw.Write(pos.Z); bw.Write(id);
        }
        var nb = new System.Text.StringBuilder();
        foreach (var kv in nodes) nb.Append($"{kv.Value} {kv.Key}\n");
        var ct = cam.GlobalTransform;
        nb.Append($"# size {w} {h} visible {vis.X} {vis.Y}\n");
        nb.Append($"# camera {ct.Basis.X.X} {ct.Basis.X.Y} {ct.Basis.X.Z} {ct.Basis.Y.X} {ct.Basis.Y.Y} {ct.Basis.Y.Z} {ct.Basis.Z.X} {ct.Basis.Z.Y} {ct.Basis.Z.Z} {ct.Origin.X} {ct.Origin.Y} {ct.Origin.Z} fov {cam.Fov}\n");
        System.IO.File.WriteAllText(dir + "/rays_nodes.txt", nb.ToString());
    }

    private void Save(string dir, string file)
        => GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
