using Godot;
using NSP.Prologue;
using NSP.View;

namespace NSP.Debug;

// 프롤로그 3D 컷씬 확인용 캡처. 창 모드로 실행한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/PrologueStageShot.tscn -- <저장 폴더> <장면 id|all>
//
// 장면 id 는 Prologue3DDirector 가 아는 것 그대로 — archive_facility · disaster_lab ·
// core_explode … "all" 이면 전부 차례로 돌며 몇 장씩 찍는다(지시서 §53).
public partial class PrologueStageShot : Node
{
    private static readonly (string Id, double[] At)[] Shots =
    {
        ("archive_facility", new[] { 1.0, 4.0, 7.5 }),
        ("archive_director", new[] { 1.2, 5.0 }),
        ("archive_habitat", new[] { 1.2, 4.5 }),
        ("archive_core", new[] { 1.0, 4.0, 7.0 }),
        ("core_warning", new[] { 0.6, 2.0 }),
        ("surface_wide", new[] { 0.6, 1.4, 2.6, 3.6, 4.6 }),
        ("surface_road", new[] { 0.6, 1.6, 2.8 }),
        ("surface_last", new[] { 0.5, 1.6, 2.6 }),
        ("disaster_lab", new[] { 0.8, 1.8, 2.3, 3.2 }),
        ("disaster_run", new[] { 0.2, 1.0, 2.2, 3.6 }),
        ("disaster_bulkhead", new[] { 0.8, 2.0, 3.0 }),
        ("disaster_cctv", new[] { 0.5, 0.7, 0.86, 1.3 }),
        ("core_drop", new[] { 0.8, 3.0, 5.0, 7.0 }),
        ("core_explode", new[] { 0.4, 0.62, 1.1, 2.2, 3.6 }),
        ("disaster_after", new[] { 1.5, 6.0 }),
        ("director_last", new[] { 1.0, 3.2, 5.0, 6.2 }),
    };

    public override void _Ready() => _ = Run();

    private Prologue3DDirector _dir;

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        string which = args.Length > 1 ? args[1] : "all";

        _dir = new Prologue3DDirector();
        AddChild(_dir);
        _dir.EnsureStage();
        await _dir.SetFullscreen(true, 0.01f);
        await Seconds(0.4);

        foreach (var (id, at) in Shots)
        {
            if (which != "all" && which != id) continue;
            _dir.Begin(id);
            double t = 0;
            foreach (double mark in at)
            {
                await Seconds(mark - t);
                t = mark;
                Shot(dir, $"{id}_{mark:0.0}");
            }
            await Seconds(0.4);
        }

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private void Shot(string dir, string name) =>
        GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");

    private async System.Threading.Tasks.Task Seconds(double s)
    {
        if (s <= 0) { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); return; }
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
