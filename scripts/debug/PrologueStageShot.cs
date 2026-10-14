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
        ("disaster_lab_glass", new[] { 0.3, 0.6, 1.2, 2.2 }),
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

        if (which == "stats") { await Stats(); return; }
        if (which == "knobs") { await Knobs(); return; }

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

    // ── 부하 측정 ────────────────────────────────────────────────────────
    //
    //   godot --path . res://scenes/debug/PrologueStageShot.tscn -- <폴더> stats
    //
    // 장면마다 2초를 돌리며 프레임 시간 · 드로우 콜 · 면 수 · 광원 수를 적는다.
    // "느리다" 를 눈이 아니라 숫자로 보기 위한 자리다 — 어떤 장면이 비싼지 먼저 안 뒤에
    // 그 장면만 손본다.
    private async System.Threading.Tasks.Task Stats()
    {
        GD.Print("################ 프롤로그 장면 부하 ################");
        GD.Print($"{"장면",-20}{"ms",7}{"fps",7}{"draw",8}{"만면",8}{"광원",6}{"그림자",7}{"노드",7}");
        foreach (var (id, _) in Shots)
        {
            _dir.Begin(id);
            await Seconds(0.8);

            double worst = 0, sum = 0;
            const int Samples = 60;
            for (int i = 0; i < Samples; i++)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                double ms = 1000.0 / Mathf.Max(1.0, Performance.GetMonitor(Performance.Monitor.TimeFps));
                sum += ms;
                worst = Mathf.Max(worst, ms);
            }
            double avg = sum / Samples;
            ulong draw = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
            ulong prim = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
            CountNodes(GetTree().Root, out int nodes, out int lights, out int shadows);
            GD.Print($"{id,-20}{avg,7:0.0}{1000.0 / avg,7:0}{draw,8}{prim / 10000.0,8:0.0}{lights,6}{shadows,7}{nodes,7}"
                     + (worst > avg * 2.0 ? $"   최악 {worst:0}ms" : ""));
        }
        GD.Print("####################################################");
        GetTree().Quit();
    }

    // ── 무엇이 비싼가 ───────────────────────────────────────────────────
    //
    //   godot --path . res://scenes/debug/PrologueStageShot.tscn -- <폴더> knobs
    //
    // 같은 장면을 설정만 바꿔 가며 재 본다. 드로우 콜이 몇 백뿐인데도 프레임이 50ms 였다 —
    // 그러면 범인은 모델이 아니라 **화면을 칠하는 비용**(해상도 · 글로우 · 그림자)이다.
    // 어느 쪽인지 숫자로 가린 뒤에 그쪽만 손본다.
    private async System.Threading.Tasks.Task Knobs()
    {
        var vp = _dir.Stage?.StageViewport;
        var we = FindEnv(vp);
        if (vp == null || we == null) { GD.PrintErr("무대 뷰포트를 찾지 못했습니다."); GetTree().Quit(1); return; }

        var size = vp.Size;
        GD.Print("################ 무대 설정별 프레임 시간 ################");
        // 바닥값 — 무대도 장면도 없는 상태. 이보다 빨라질 수는 없다.
        vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        if (_dir.Stage?.Screen != null) _dir.Stage.Screen.Visible = false;
        GD.Print($"[바닥값] 아무것도 안 그림   {await Ms():0.0} ms   (스크립트 {Cpu():0.0} ms)");
        if (_dir.Stage?.Screen != null) _dir.Stage.Screen.Visible = true;
        vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        foreach (string scene in new[] { "archive_core", "archive_facility", "disaster_run" })
        {
            _dir.Begin(scene);
            await Seconds(0.8);
            GD.Print($"[{scene}]");
            GD.Print($"   기준                 {await Ms():0.0} ms   (스크립트 {Cpu():0.0} ms)");

            // 무대를 통째로 멈춘다 — 남는 시간이 '3D 와 무관한 바닥값' 이다.
            vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            GD.Print($"   무대 렌더 끔         {await Ms():0.0} ms");
            vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

            // 스크립트를 멈춘다(그리기는 계속) — 남는 시간이 순수 그래픽 비용이다.
            GetTree().Paused = true;
            GD.Print($"   스크립트 멈춤        {await Ms():0.0} ms");
            GetTree().Paused = false;

            // 전체 화면 오버레이(비네트 · 노이즈 · 컷씬 그림)를 내린다.
            var hidden = HideOverlays(true);
            GD.Print($"   화면 오버레이 끔     {await Ms():0.0} ms");
            foreach (var c in hidden) c.Visible = true;

            we.Environment.GlowEnabled = false;
            GD.Print($"   글로우 끔            {await Ms():0.0} ms");
            we.Environment.GlowEnabled = true;

            vp.Scaling3DScale = 0.75f;
            GD.Print($"   3D 해상도 75%        {await Ms():0.0} ms");
            vp.Scaling3DScale = 0.6f;
            GD.Print($"   3D 해상도 60%        {await Ms():0.0} ms");
            vp.Scaling3DScale = 1f;

            vp.PositionalShadowAtlasSize = 1024;
            GD.Print($"   그림자 아틀라스 1024 {await Ms():0.0} ms");
            vp.PositionalShadowAtlasSize = 2048;

            SetShadows(vp, false);
            GD.Print($"   그림자 전부 끔       {await Ms():0.0} ms");
            SetShadows(vp, true);

            we.Environment.GlowEnabled = false;
            vp.Scaling3DScale = 0.75f;
            SetShadows(vp, false);
            GD.Print($"   셋 다 적용           {await Ms():0.0} ms");
            we.Environment.GlowEnabled = true;
            vp.Scaling3DScale = 1f;
            SetShadows(vp, true);
            vp.Size = size;
        }
        GD.Print("########################################################");
        GetTree().Quit();
    }

    // 창을 덮는 CanvasLayer 안의 보이는 Control 을 전부 내린다(되돌릴 목록을 돌려준다).
    private System.Collections.Generic.List<Control> HideOverlays(bool hide)
    {
        var hidden = new System.Collections.Generic.List<Control>();
        foreach (var layer in GetTree().Root.GetChildren())
            Walk(layer);
        return hidden;

        void Walk(Node n)
        {
            if (n is Control { Visible: true } c && n is not SubViewport)
            {
                c.Visible = !hide;
                hidden.Add(c);
                return;
            }
            if (n is SubViewport) return;
            foreach (var ch in n.GetChildren()) Walk(ch);
        }
    }

    // 스크립트(_process) 가 쓴 시간. 이게 작으면 병목은 그래픽 쪽이다.
    private static double Cpu() => Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;

    private async System.Threading.Tasks.Task<double> Ms()
    {
        for (int i = 0; i < 12; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        double sum = 0;
        const int N = 40;
        for (int i = 0; i < N; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            sum += 1000.0 / Mathf.Max(1.0, Performance.GetMonitor(Performance.Monitor.TimeFps));
        }
        return sum / N;
    }

    private static WorldEnvironment FindEnv(Node n)
    {
        if (n is WorldEnvironment w) return w;
        foreach (var c in n.GetChildren())
            if (FindEnv(c) is { } hit) return hit;
        return null;
    }

    private static readonly System.Collections.Generic.List<Light3D> _wasShadow = new();

    private static void SetShadows(Node n, bool on)
    {
        if (!on)
        {
            _wasShadow.Clear();
            Collect(n);
            foreach (var l in _wasShadow) l.ShadowEnabled = false;
            return;
        }
        foreach (var l in _wasShadow) if (GodotObject.IsInstanceValid(l)) l.ShadowEnabled = true;

        static void Collect(Node node)
        {
            if (node is Light3D { ShadowEnabled: true } l) _wasShadow.Add(l);
            foreach (var c in node.GetChildren()) Collect(c);
        }
    }

    private static void CountNodes(Node n, out int nodes, out int lights, out int shadows)
    {
        nodes = 1; lights = 0; shadows = 0;
        if (n is Light3D { Visible: true } l && Showing(l))
        {
            lights++;
            if (l.ShadowEnabled) shadows++;
        }
        foreach (var c in n.GetChildren())
        {
            CountNodes(c, out int dn, out int dl, out int ds);
            nodes += dn; lights += dl; shadows += ds;
        }
    }

    // 부모가 전부 보이는가 — 꺼 둔 세트의 광원은 세지 않는다.
    private static bool Showing(Node3D n)
    {
        for (Node p = n; p != null; p = p.GetParent())
            if (p is Node3D v && !v.Visible) return false;
        return true;
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
