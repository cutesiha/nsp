using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// **어디가 비싼지 가른다.** 기준선(PerfBaselineRun)이 "느리다"를 보여 줬다면,
// 이쪽은 "무엇을 끄면 빨라지는가"를 하나씩 꺼 보며 답한다.
//
//   godot --path . res://scenes/debug/PerfProbeRun.tscn -- <보고서.md>
//
// 끄는 것은 **측정 동안만**이다. 실제 게임 설정을 바꾸지 않는다 — 이 파일은 무엇도
// 고치지 않고, 비용의 출처만 알려 준다. 여기서 나온 답을 보고 고칠 곳을 정한다.
public partial class PerfProbeRun : Node
{
    private const double Warm = 1.0;
    private const double Sample = 3.0;

    private readonly List<PerfStats.Result> _results = new();
    private readonly List<string> _notes = new();

    public override void _Ready() => _ = Run();
    public override void _Process(double delta) => PerfStats.Sample(delta);

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string outPath = args.Length > 0 ? args[0] : "docs/PERF_PROBE.md";

        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("PerfProbeRun: 창 모드로 실행할 것.");
            GetTree().Quit(1);
            return;
        }
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        // 품질을 인자로 못 박는다 — 앞선 측정과 **같은 조건**이 아니면 비교가 거짓말이 된다.
        if (args.Length > 1)
        {
            GameSettings.Load();
            GameSettings.GraphicsQuality = args[1].ToLowerInvariant() switch
            {
                "verylow" => GameSettings.Quality.VeryLow,
                "low" => GameSettings.Quality.Low,
                "medium" => GameSettings.Quality.Medium,
                _ => GameSettings.Quality.High,
            };
            GameSettings.Save();
        }

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(40);

        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || ctl == null) { GD.PrintErr("씬을 찾지 못했다"); GetTree().Quit(1); return; }

        Deploy(sim);
        GameState.Instance?.SetPhase(GamePhase.Live);
        sim.SetSurveillanceTarget("power_room");
        await Frames(90);

        var root = GetTree().Root;
        var world = ctl.GetParent() as Node3D ?? ctl as Node;

        // 끄기 전후를 **번갈아** 잰다.
        //
        // 전에 한 번씩만 재서 ①②③… 순서대로 늘어놓았더니, 끈 쪽이 오히려 느리게
        // 나오는 칸이 생겼다(② 가 ① 보다 10 ms 느렸다). 이 노트북은 측정 내내 성능이
        // 흔들려서, **떨어져 있는 두 줄을 비교하면 그 차이가 토글 때문인지 기계 때문인지
        // 알 수 없다.** 그래서 토글마다 바로 앞에 기준을 한 번 더 재고, 그 짝과만 비교한다.
        var cctvVp = Field<SubViewport>(ctl, "_facilityCctvVp");
        var shadowed = new List<Light3D>();
        CollectShadowLights(root, shadowed);
        float scale0 = root.Scaling3DScale;

        await Measure("〇 몸풀기(버리는 측정)");
        _notes.Add($"- 활성 SubViewport: {CountActiveViewports(root)}개 / 전체 {CountViewports(root)}개");
        _notes.Add($"- 3D 스케일: {scale0:0.00}, CRT 렌더 배율 {ControlRoom3DController.RenderScale:0.00}, 창 {DisplayServer.WindowGetSize()}");
        _notes.Add($"- 조명(OmniLight3D+SpotLight3D): {CountLights(root)}개, 그림자 켠 것 {CountShadowLights(root)}개");
        _notes.Add($"- 보이는 MeshInstance3D: {CountVisibleMeshes(root)}개");

        // ── 창 크기 (인자에 window 를 주면 이것만 재고 끝낸다) ──────────────
        //
        // 이 기계는 **픽셀을 채우는 속도**에 막혀 있다. 그러면 가장 큰 지렛대는
        // 설정이 아니라 창 크기다 — 영상으로 찍을 해상도를 정하는 데 쓴다.
        //
        // 긴 측정은 노트북이 달아올라 기준선 자체가 흔들린다(한 번은 45ms → 86ms 까지
        // 널뛰었다). 그래서 이것만 따로 짧게 돌릴 수 있게 해 두었다.
        bool onlyWindow = System.Array.IndexOf(args, "window") >= 0;
        if (onlyWindow)
        {
            var win0 = DisplayServer.WindowGetSize();
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1024, 576) })
                await Probe($"⑯ 창 {win0.X}×{win0.Y} → {size.X}×{size.Y}",
                    () => DisplayServer.WindowSetSize(size),
                    () => DisplayServer.WindowSetSize(win0));
            await Write(outPath);
            GetTree().Quit();
            return;
        }

        // ② CCTV 3D 월드(두 번째 렌더 패스).
        if (cctvVp != null)
        {
            var before = cctvVp.RenderTargetUpdateMode;
            await Probe("② CCTV 3D 월드 렌더 OFF",
                () => cctvVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                () => cctvVp.RenderTargetUpdateMode = before);
        }
        else _notes.Add("- `_facilityCctvVp` 를 찾지 못해 ②를 건너뛰었다");

        // ③ CRT 화면(2D SubViewport) 전부.
        var saved = new List<(SubViewport Vp, SubViewport.UpdateMode Mode)>();
        await Probe($"③ CRT SubViewport 전부 렌더 OFF",
            () =>
            {
                saved.Clear();
                foreach (var vp in AllSubViewports(root))
                {
                    if (vp == cctvVp) continue;
                    saved.Add((vp, vp.RenderTargetUpdateMode));
                    vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                }
            },
            () => { foreach (var (vp, m) in saved) vp.RenderTargetUpdateMode = m; });

        // ④ 그림자를 전부 — **상한 확인용**이다. 이 값을 그대로 쓸 생각은 없다.
        await Probe($"④ 그림자 전부 OFF (광원 {shadowed.Count}개, 상한)",
            () => { foreach (var l in shadowed) l.ShadowEnabled = false; },
            () => { foreach (var l in shadowed) l.ShadowEnabled = true; });

        // ⑤ 메인 3D 해상도 절반 — 역시 상한 확인용(0.36 은 손가락이 뭉개진다).
        await Probe($"⑤ 메인 3D 스케일 {scale0:0.00} → {scale0 * 0.5f:0.00} (상한)",
            () => root.Scaling3DScale = scale0 * 0.5f,
            () => root.Scaling3DScale = scale0);

        // ⑥ 제어실 3D 를 통째로 숨긴다(= 남는 것은 UI 뿐). 도달 가능한 최대치.
        if (world is Node3D w3)
            await Probe("⑥ 제어실 3D 전체 숨김 (상한)",
                () => w3.Visible = false, () => w3.Visible = true);

        // ── 여기부터는 "정말 넣을 생각이 있는 값" 만 그 값 그대로 잰다 ──────

        // ⑦ 안 쓰는 화면의 렌더 타깃을 버린다(VRAM).
        //
        // SubViewport 는 Disabled 여도 자기 크기만큼 텍스처를 쥐고 있다. 스무 개 중
        // 넷만 쓰는데 나머지도 전부 쥐고 있으면, 시스템 메모리를 나눠 쓰는 내장 GPU
        // 에서는 그것만으로 다른 것이 밀려난다.
        var idle = new List<(SubViewport Vp, Vector2I Size)>();
        await Probe("⑦ 안 쓰는 화면의 렌더 타깃 해제 (VRAM)",
            () =>
            {
                idle.Clear();
                foreach (var vp in AllSubViewports(root))
                {
                    if (vp == cctvVp || vp.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled) continue;
                    idle.Add((vp, vp.Size));
                    vp.Size = new Vector2I(1, 1);
                }
            },
            () => { foreach (var (vp, s) in idle) vp.Size = s; });

        // ⑧ 그림자를 **스포트라이트만** 남긴다.
        //
        // ④가 보여 준 것은 "그림자가 비싸다" 까지다. 이 방에서 그림자가 실제로 필요한
        // 것은 책상 위 기기를 떼어 놓는 스포트라이트뿐이다. 나머지를 끄면 얼마나 도는지,
        // 그러고도 방이 방으로 보이는지를 가른다.
        var offs = new List<Light3D>();
        await Probe("⑧ 그림자 - 스포트라이트만 남김",
            () =>
            {
                offs.Clear();
                foreach (var l in shadowed)
                    if (l is not SpotLight3D) { l.ShadowEnabled = false; offs.Add(l); }
            },
            () => { foreach (var l in offs) l.ShadowEnabled = true; });

        // ⑨ 넣을 생각이 있는 3D 스케일.
        foreach (float s in new[] { 0.60f, 0.52f })
            await Probe($"⑨ 메인 3D 스케일 {scale0:0.00} → {s:0.00}",
                () => root.Scaling3DScale = s,
                () => root.Scaling3DScale = scale0);

        // ⑩ CRT 화면의 렌더 해상도만 더 내린다(글자 크기·배치는 그대로).
        float rs = ControlRoom3DController.RenderScale;
        foreach (float k in new[] { 0.58f, 0.46f })
        {
            var sizes = new List<(SubViewport Vp, Vector2I Size)>();
            await Probe($"⑩ CRT 렌더 배율 {rs:0.00} → {k:0.00}",
                () =>
                {
                    sizes.Clear();
                    foreach (var vp in AllSubViewports(root))
                    {
                        if (vp.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled) continue;
                        sizes.Add((vp, vp.Size));
                        vp.Size = new Vector2I(
                            Mathf.Max(1, Mathf.RoundToInt(vp.Size.X * k / rs)),
                            Mathf.Max(1, Mathf.RoundToInt(vp.Size.Y * k / rs)));
                    }
                },
                () => { foreach (var (vp, s) in sizes) vp.Size = s; });
        }

        // ⑪ 실제 설정으로 — 품질 한 칸 내리기. **이것이 플레이어가 쓰게 될 값**이다.
        //
        // 위의 ⑦~⑩ 은 손으로 하나씩 꺼 본 것이고, 이 줄은 설정 창에서 "매우 낮음" 을
        // 고르는 것과 똑같은 경로를 탄다(GameSettings.GraphicsQuality 대입).
        // 실행 중에 바뀌는지까지 여기서 같이 확인된다 — 안 바뀌면 차이가 0 으로 나온다.
        var q0 = GameSettings.GraphicsQuality;
        await Probe($"⑪ 품질 {GameSettings.QualityLabel} → 매우 낮음 (설정 그대로)",
            () => GameSettings.GraphicsQuality = GameSettings.Quality.VeryLow,
            () => GameSettings.GraphicsQuality = q0);
        _notes.Add($"- ⑪ 뒤 되돌린 품질: {GameSettings.QualityLabel}, CRT 렌더 배율 {ControlRoom3DController.RenderScale:0.00}");

        // ── 아날로그 호러 비주얼 세 겹 ─────────────────────────────────
        //
        // 셋 다 "화면을 한 번 더 그리는" 종류라, 픽셀을 메우는 속도가 느린 내장 GPU
        // 에서 가장 먼저 의심해야 하는 것들이다. **어느 겹이 비싼지**를 가른다.

        // ⑫ 전역 VHS 필터 — 화면 전체(창 해상도)를 한 번 더 읽어 다시 그린다.
        var filter = NSP.Ui.AnalogFilter.Instance;
        if (filter != null)
        {
            bool on0 = filter.Enabled;
            await Probe("⑫ 전역 아날로그 필터(VHS) OFF",
                () => filter.Enabled = false, () => filter.Enabled = on0);
            _notes.Add($"- ⑫ 측정 시작 시 필터는 {(on0 ? "켜짐" : "꺼짐")} 상태였다");
        }
        else _notes.Add("- `AnalogFilter.Instance` 가 없어 ⑫를 건너뛰었다");

        // ⑬ 손그림 칠 — 풀스크린 사각형 + 모니터 앞면 잉크 사각형.
        var paint = AnalogPaint.Instance;
        if (paint != null)
        {
            bool p0 = paint.Visible;
            await Probe("⑬ 손그림 칠(AnalogPaint) OFF",
                () => paint.Visible = false, () => paint.Visible = p0);
        }
        else _notes.Add("- `AnalogPaint.Instance` 가 없어 ⑬을 건너뛰었다");

        // ⑭ CCTV 영상 셰이더 — 영상 사각형(736×452) 하나에만 걸린다. 표본 최대 4개.
        var cctv = CCTVMonitorView.Instance;
        var feedTex = Field<TextureRect>(cctv, "_bgTex");
        if (feedTex != null)
        {
            var m0 = feedTex.Material;
            await Probe("⑭ CCTV 영상 아날로그 셰이더 OFF",
                () => feedTex.Material = null, () => feedTex.Material = m0);
            _notes.Add($"- ⑭ 영상 셰이더: {(m0 == null ? "없음" : "걸려 있음")}, 영상 크기 {feedTex.Size}");
        }
        else _notes.Add("- CCTV `_bgTex` 를 찾지 못해 ⑭를 건너뛰었다");

        // ⑮ 세 겹을 한꺼번에 — 아날로그 비주얼 전체가 차지하는 몫.
        if (filter != null || paint != null || feedTex != null)
        {
            bool f0 = filter?.Enabled ?? false;
            bool pv0 = paint?.Visible ?? false;
            var fm0 = feedTex?.Material;
            await Probe("⑮ 아날로그 비주얼 세 겹 전부 OFF (합계)",
                () =>
                {
                    if (filter != null) filter.Enabled = false;
                    if (paint != null) paint.Visible = false;
                    if (feedTex != null) feedTex.Material = null;
                },
                () =>
                {
                    if (filter != null) filter.Enabled = f0;
                    if (paint != null) paint.Visible = pv0;
                    if (feedTex != null) feedTex.Material = fm0;
                });
        }

        _notes.Add("");
        _notes.Add("### 그림자를 켠 광원");
        _notes.Add("");
        foreach (var l in shadowed) _notes.Add($"- `{l.GetPath()}` ({l.GetType().Name})");

        await Write(outPath);
    }

    // ── 세기 ────────────────────────────────────────────────────────

    private static List<SubViewport> AllSubViewports(Node n, List<SubViewport> into = null)
    {
        into ??= new List<SubViewport>();
        if (n is SubViewport vp) into.Add(vp);
        foreach (var c in n.GetChildren()) AllSubViewports(c, into);
        return into;
    }

    private static int CountViewports(Node n) => AllSubViewports(n).Count;

    private static int CountActiveViewports(Node n)
    {
        int k = 0;
        foreach (var vp in AllSubViewports(n))
            if (vp.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled) k++;
        return k;
    }

    private static int CountLights(Node n)
    {
        int k = n is OmniLight3D or SpotLight3D ? 1 : 0;
        foreach (var c in n.GetChildren()) k += CountLights(c);
        return k;
    }

    private static int CountShadowLights(Node n)
    {
        int k = n is Light3D { ShadowEnabled: true, Visible: true } ? 1 : 0;
        foreach (var c in n.GetChildren()) k += CountShadowLights(c);
        return k;
    }

    private static void CollectShadowLights(Node n, List<Light3D> into)
    {
        if (n is Light3D { ShadowEnabled: true } l) into.Add(l);
        foreach (var c in n.GetChildren()) CollectShadowLights(c, into);
    }

    private static int CountVisibleMeshes(Node n)
    {
        int k = n is MeshInstance3D { Visible: true } ? 1 : 0;
        foreach (var c in n.GetChildren()) k += CountVisibleMeshes(c);
        return k;
    }

    private static T Field<T>(object target, string name) where T : class =>
        target?.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target) as T;

    private static void Deploy(FacilitySimulation sim)
    {
        var rooms = new Dictionary<string, string>
        {
            ["cat"] = "storage_room", ["dog"] = "guard_room", ["wolf"] = "maintenance_room",
            ["rabbit"] = "core_room", ["sheep"] = "power_room", ["fox"] = "vent_room",
        };
        foreach (var kv in rooms)
        {
            var st = sim.GetEmployeeState(kv.Key);
            if (st == null) continue;
            st.AssignedRoomId = kv.Value; st.CurrentRoomId = kv.Value;
            st.Alive = true; st.Isolated = false; st.Incapacitated = false;
        }
        sim.RecordShiftStart();
    }

    // 토글 하나 — **바로 앞에 기준을 한 번 더 재고** 그 짝과만 비교한다.
    // 되돌린 뒤 프레임을 좀 흘려 보내 다음 기준이 앞 토글의 여운을 타지 않게 한다.
    private async System.Threading.Tasks.Task Probe(string label,
        System.Action apply, System.Action revert)
    {
        await Measure("- 기준 - " + label);
        apply();
        await Measure(label);
        revert();
        await Frames(45);
    }

    private async System.Threading.Tasks.Task Measure(string label)
    {
        await Seconds(Warm);
        PerfStats.Begin(label);
        await Seconds(Sample);
        var r = PerfStats.End();
        _results.Add(r);
        GD.Print($"   {label} - {r.AvgFps:0.0} FPS ({r.AvgMs:0.0} ms), draw {r.DrawCalls}");
    }

    private async System.Threading.Tasks.Task Write(string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# NSP 성능 - 무엇이 비싼가 (하나씩 꺼 보기)");
        sb.AppendLine();
        sb.AppendLine($"- {RenderingServer.GetVideoAdapterName()}, {OS.GetProcessorName()}");
        sb.AppendLine($"- 품질 {GameSettings.GraphicsQuality}, V-Sync {DisplayServer.WindowGetVsyncMode()}");
        sb.AppendLine("- 끈 것은 **측정 동안만**이고 바로 되돌린다. 설정을 고치지 않는다.");
        sb.AppendLine();
        // **읽어야 할 표는 이것이다.** 바로 앞 기준과의 차이만이 토글의 값이다.
        sb.AppendLine("## 짝지어 본 차이 (앞 기준 → 끈 뒤)");
        sb.AppendLine();
        sb.AppendLine("| 끈 것 | 기준 ms | 끈 뒤 ms | 차이 ms | 기준 FPS | 끈 뒤 FPS | VRAM MB 차이 | draw call 차이 |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        PerfStats.Result baseline = null;
        foreach (var r in _results)
        {
            if (r.Label.StartsWith("- 기준") || r.Label.StartsWith("〇")) { baseline = r; continue; }
            if (baseline == null) continue;
            double d = r.AvgMs - baseline.AvgMs;
            sb.AppendLine($"| {r.Label} | {baseline.AvgMs:0.0} | {r.AvgMs:0.0} | **{d:+0.0;-0.0;0.0}** | " +
                          $"{baseline.AvgFps:0.0} | {r.AvgFps:0.0} | {r.VideoMemMb - baseline.VideoMemMb:+0;-0;0} | " +
                          $"{r.DrawCalls - baseline.DrawCalls:+0;-0;0} |");
        }
        sb.AppendLine();
        sb.AppendLine("> 차이가 **음수**면 그만큼 빨라진 것이다. 기준 줄끼리 비교하면 안 된다 -");
        sb.AppendLine("> 이 기계는 측정 내내 성능이 흔들려서 떨어진 두 줄은 서로 비교할 수 없다.");
        sb.AppendLine();
        sb.AppendLine("## 잰 그대로");
        sb.AppendLine();
        sb.AppendLine(PerfStats.Header);
        foreach (var r in _results) sb.AppendLine(PerfStats.Row(r));
        sb.AppendLine();
        // VRAM 은 내장 GPU 에서 시스템 메모리를 나눠 쓰므로 프레임 시간만큼 중요하다.
        sb.AppendLine("| 상황 | GPU 메모리 MB | draw call | 렌더 오브젝트 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in _results)
            sb.AppendLine($"| {r.Label} | {r.VideoMemMb:0.0} | {r.DrawCalls} | {r.RenderObjects} |");
        sb.AppendLine();
        sb.AppendLine("## 장면 구성");
        sb.AppendLine();
        foreach (var n in _notes) sb.AppendLine(n);

        string abs = path.StartsWith("res://") ? ProjectSettings.GlobalizePath(path) : path;
        using var f = FileAccess.Open(abs, FileAccess.ModeFlags.Write);
        if (f == null) GD.PrintErr("보고서를 쓰지 못했다: " + abs);
        else { f.StoreString(sb.ToString()); GD.Print("saved → " + abs); }
        await Frames(2);
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
    {
        double t = 0;
        while (t < s) { await Frame(); t += GetProcessDeltaTime(); }
    }
}
