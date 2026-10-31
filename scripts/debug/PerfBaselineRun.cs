using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 성능 기준선을 **실제로 돌려서** 잰다.
//
//   godot --path . res://scenes/debug/PerfBaselineRun.tscn -- <보고서 경로.md> [품질]
//     품질 : low | medium | high (생략하면 지금 설정 그대로)
//
// **반드시 창 모드로 돌린다.** 헤드리스는 그림을 그리지 않으므로 FPS·draw call·GPU
// 메모리가 전부 무의미해진다. 그래서 이 파일은 헤드리스로 실행되면 측정을 거부한다 —
// 재지 않은 수치를 보고서에 적는 것이 가장 나쁘다.
//
// 상황마다 WarmSeconds 만큼 버린 뒤 SampleSeconds 동안 잰다. 버리는 구간이 없으면
// 전환 직후의 로딩 프레임이 평균에 섞여 "평상시 성능" 이 아니게 된다.
public partial class PerfBaselineRun : Node
{
    private const double WarmSeconds = 1.2;
    private const double SampleSeconds = 4.0;

    private readonly List<PerfStats.Result> _results = new();
    private readonly List<string> _details = new();
    private readonly List<string> _skipped = new();

    public override void _Ready() => _ = Run();

    public override void _Process(double delta) => PerfStats.Sample(delta);

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string outPath = args.Length > 0 ? args[0] : "docs/PERFORMANCE_BASELINE.md";
        string quality = args.Length > 1 ? args[1].ToLowerInvariant() : "";

        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("PerfBaselineRun: 헤드리스에서는 렌더링을 재지 않는다. 창 모드로 실행할 것.");
            GetTree().Quit(1);
            return;
        }

        // V-Sync 를 끄고 재는 선택지.
        //
        // **이게 없으면 측정이 거짓말을 한다.** V-Sync 가 켜져 있으면 한 프레임 작업이
        // 16.7ms 를 아주 조금만 넘겨도 다음 수직동기까지 기다려 33.3ms 가 된다 —
        // 화면에는 "30 FPS" 로 찍히지만 실제 작업은 17ms 일 수 있다. 어디를 얼마나
        // 줄여야 하는지 알려면 기다림이 빠진 **진짜 작업 시간**을 봐야 한다.
        bool noVsync = System.Array.IndexOf(args, "novsync") >= 0;
        if (noVsync) DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);

        if (quality.Length > 0)
        {
            GameSettings.Load();
            GameSettings.GraphicsQuality = quality switch
            {
                "verylow" => GameSettings.Quality.VeryLow,
                "low" => GameSettings.Quality.Low,
                "medium" => GameSettings.Quality.Medium,
                _ => GameSettings.Quality.High,
            };
            GameSettings.Save();
            GD.Print($"   품질 = {GameSettings.GraphicsQuality}");
        }

        // ── 1. 타이틀 ────────────────────────────────────────────────
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        await Frames(30);
        await Measure("타이틀 / 초기 로딩 직후");

        // ── 2. 근무 배치 → 근무 시작 ─────────────────────────────────
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(40);
        await Measure("근무 배치 화면");

        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || ctl == null)
        {
            _skipped.Add("근무 이후 전부 - FacilitySimulation / ControlRoom3DController 를 찾지 못했다");
            await Write(outPath);
            return;
        }

        Deploy(sim);
        GameState.Instance?.SetPhase(GamePhase.Live);
        await Frames(60);

        // ── 3. 평상시(CCTV 켜짐) ─────────────────────────────────────
        sim.SetSurveillanceTarget("power_room");
        await Measure("근무 중, CCTV 켜짐(작업실)");

        // ── 4. CCTV 전원 OFF ─────────────────────────────────────────
        bool cctvWasOn = GameState.Instance?.IsConsumerPowered(PowerConsumer.CctvWatch) ?? false;
        if (cctvWasOn) GameState.Instance?.TryTogglePower(PowerConsumer.CctvWatch);
        await Frames(30);
        await Measure("근무 중, CCTV 전원 OFF");
        if (cctvWasOn && !(GameState.Instance?.IsConsumerPowered(PowerConsumer.CctvWatch) ?? false))
            GameState.Instance?.TryTogglePower(PowerConsumer.CctvWatch);
        await Frames(30);

        // ── 5. 작업실 ↔ 복도 전환 ────────────────────────────────────
        PerfStats.Begin("CCTV 전환(작업실↔복도 반복)");
        for (int i = 0; i < 10; i++)
        {
            if (i % 2 == 0) sim.SetCorridorSurveillance("corridor_west");
            if (i % 2 == 1) sim.SetSurveillanceTarget(i % 4 == 1 ? "storage_room" : "core_room");
            await Frames(14);
        }
        Keep(PerfStats.End());
        sim.SetSurveillanceTarget("power_room");
        sim.SetSurveillanceTarget("power_room");
        await Frames(30);

        // ── 6. 귀신 1회차 / 2회차 ────────────────────────────────────
        await GhostRound(sim, "귀신 최초 등장(작업실)");
        await GhostRound(sim, "귀신 두 번째 등장(같은 개체)");

        // ── 7. 점프스케어 1회차 / 2회차 ──────────────────────────────
        await ScareRound("괴물 점프스케어 1회차");
        await ScareRound("괴물 점프스케어 2회차");

        // ── 8. 휴게시간 ──────────────────────────────────────────────
        GameState.Instance?.SetPhase(GamePhase.Rest);
        RestRosterView.Instance?.Present(false);
        ctl.SetLeftScreen(ctl.RestRosterViewport);
        ctl.SetRightScreen(ctl.InterviewViewport);
        await Frames(60);
        await Measure("휴게시간(휴게실 3D + 인터뷰 화면)");

        await Write(outPath);
    }

    // 귀신 한 번 — 나타나고 사라지기까지.
    private async System.Threading.Tasks.Task GhostRound(FacilitySimulation sim, string label)
    {
        if (sim.Ghost == null) { _skipped.Add(label + " - GhostHauntSystem 없음"); return; }
        sim.SetSurveillanceTarget("power_room");
        await Frames(20);
        PerfStats.Begin(label);
        sim.Ghost.ForceAppear("power_room", sim, 6f);
        await Frames(Mathf.RoundToInt(SampleSeconds * 60));
        Keep(PerfStats.End());
        sim.Ghost.ForceDepart(sim, notify: false);
        await Frames(40);
    }

    // 점프스케어 한 번 — 호출부터 화면에 뜨기까지의 지연이 핵심이다.
    private async System.Threading.Tasks.Task ScareRound(string label)
    {
        var hd = NSP.Ui.HorrorDirector.Instance;
        if (hd == null) { _skipped.Add(label + " - HorrorDirector 없음"); return; }
        await Frames(30);
        PerfStats.Begin(label);
        hd.PlayEntityFaceJumpscare();
        await Frames(Mathf.RoundToInt(SampleSeconds * 60));
        Keep(PerfStats.End());
        await Frames(60);
    }

    private void Deploy(FacilitySimulation sim)
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
            st.AssignedRoomId = kv.Value;
            st.CurrentRoomId = kv.Value;
            st.Alive = true; st.Isolated = false; st.Incapacitated = false;
        }
        sim.RecordShiftStart();
    }

    private async System.Threading.Tasks.Task Measure(string label)
    {
        await Seconds(WarmSeconds);
        PerfStats.Begin(label);
        await Seconds(SampleSeconds);
        Keep(PerfStats.End());
    }

    private void Keep(PerfStats.Result r)
    {
        _results.Add(r);
        _details.Add($"### {r.Label}\n\n{PerfStats.Detail(r)}");
        GD.Print($"   {r.Label} - 평균 {r.AvgFps:0.0} FPS, 1%low {r.LowOnePercentFps:0.0}, 최악 {r.WorstMs:0.0}ms");
    }

    private async System.Threading.Tasks.Task Write(string path)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# NSP 성능 기준선");
        sb.AppendLine();
        sb.AppendLine($"- 측정 시각: {Time.GetDatetimeStringFromSystem()}");
        sb.AppendLine($"- 품질 설정: **{GameSettings.GraphicsQuality}** (RenderScale {ControlRoom3DController.RenderScale:0.00})");
        sb.AppendLine($"- 렌더러: {ProjectSettings.GetSetting("rendering/renderer/rendering_method")}, " +
                      $"드라이버 {RenderingServer.GetVideoAdapterApiVersion()}");
        sb.AppendLine($"- GPU: {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()})");
        sb.AppendLine($"- CPU: {OS.GetProcessorName()}, {OS.GetProcessorCount()} 스레드");
        sb.AppendLine($"- V-Sync: {DisplayServer.WindowGetVsyncMode()}");
        sb.AppendLine($"- 창 크기: {DisplayServer.WindowGetSize()}, 화면 {DisplayServer.ScreenGetSize()}");
        sb.AppendLine($"- 빌드: {(OS.IsDebugBuild() ? "디버그" : "릴리스")}, 엔진 {Engine.GetVersionInfo()["string"]}");
        sb.AppendLine();
        sb.AppendLine("> 각 상황은 전환 뒤 " + WarmSeconds + "초를 버리고 " + SampleSeconds + "초를 쟀다.");
        sb.AppendLine("> **GPU 프레임 시간은 Godot 4.7 이 내주지 않는다 - 측정 불가.**");
        sb.AppendLine();
        sb.AppendLine(PerfStats.Header);
        foreach (var r in _results) sb.AppendLine(PerfStats.Row(r));
        sb.AppendLine();
        sb.AppendLine("## 상황별 세부");
        sb.AppendLine();
        foreach (var d in _details) sb.AppendLine(d);
        if (_skipped.Count > 0)
        {
            sb.AppendLine("## 재지 못한 것");
            sb.AppendLine();
            foreach (var s in _skipped) sb.AppendLine($"- {s}");
        }

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
