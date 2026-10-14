using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 중앙제어실 조명 확인용 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/LightingShot.tscn -- <저장 폴더>
//
// 메인 씬을 "DAY1 바로 시작" 경로로 띄워 배치 단계 / 근무(Live) 화면을 PNG 로 저장하고,
// 근무 중 몇 초간의 평균·최저 FPS 를 출력한 뒤 종료한다.
public partial class LightingShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        GD.Print($"renderer = {ProjectSettings.GetSetting("rendering/renderer/rendering_method")} · {RenderingServer.GetVideoAdapterName()}");

        // 시작 화면만 보는 모드 — DAY1 로 건너뛰지 않고 타이틀 그대로 켠 뒤 찍는다.
        if (args.Length > 1 && args[1] == "title") { await ShotTitle(dir); return; }

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        var scene = GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate();
        AddChild(scene);

        for (int i = 0; i < 600 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        // 성능 비교용 옵션: -- <폴더> noshadow / noglow / nothing
        string variant = args.Length > 1 ? args[1] : "";
        if (variant is "noshadow" or "nothing")
            foreach (var l in scene.FindChildren("*ScreenLight", "SpotLight3D", true, false).Cast<SpotLight3D>()) l.ShadowEnabled = false;
        if (variant == "nofill" && scene.FindChild("DeskFillLight", true, false) is Light3D fl) fl.Visible = false;
        if (variant is "noglow" or "nothing")
            ((WorldEnvironment)scene.FindChild("WorldEnvironment", true, false)).Environment.GlowEnabled = false;
        // 화면 전체에 깔리는 오버레이(필름 그레인 · 가운데 가산 하이라이트)가 검정을
        // 얼마나 들어 올리는지 — "뿌옇다" 의 범인을 가리는 변수다.
        if (variant == "nooverlay") AmbientOverlay.Instance?.SetSceneIntensity(0f);
        if (variant is "medium" or "low")
            GameSettings.GraphicsQuality = variant == "medium" ? GameSettings.Quality.Medium : GameSettings.Quality.Low;
        if (variant != "") GD.Print("variant = " + variant);
        await Frames(120);
        Save(dir, $"lighting_schedule{(variant == "" ? "" : "_" + variant)}.png");
        foreach (var l in GetTree().Root.FindChildren("*", "Light3D", true, false).Cast<Light3D>())
            GD.Print($"light {l.GetPath()} vis={l.IsVisibleInTree()} e={l.LightEnergy:0.00} shadow={l.ShadowEnabled}");

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
        await Frames(150);
        // 근무 시작에 저절로 뜨는 「오늘의 업무」 창이 책상을 가린다 — 조명을 보려면 닫는다.
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Frames(30);
        Save(dir, $"lighting_live{(variant == "" ? "" : "_" + variant)}.png");
        ReportContrast();
        // 모니터 화면의 "원본 UI" 도 한 장 — 화면에 보이는 밝은 띠가 UI 인지 3D 인지 가른다.
        ControlRoom3DController.Instance?.FacilityViewport?.GetTexture()?.GetImage()?.SavePng(dir + "/monitor_ui_raw.png");
        await ReportShadowStrength(scene, dir);

        // FPS — 근무 중 4초.
        var samples = new System.Collections.Generic.List<double>();
        ulong t0 = Time.GetTicksMsec();
        ulong last = t0;
        while (Time.GetTicksMsec() - t0 < 4000)
        {
            await Frame();
            ulong now = Time.GetTicksMsec();
            samples.Add(now - last);
            last = now;
        }
        double avgMs = samples.Average();
        GD.Print($"FPS avg = {1000.0 / avgMs:0.0} · worst frame = {samples.Max():0} ms · frames = {samples.Count}");
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // 시작 화면 — 대기 상태 한 장, 전원을 넣어 두 CRT 가 다 켜진 상태 한 장.
    private async System.Threading.Tasks.Task ShotTitle(string dir)
    {
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1200 && TitleRoomDirector.Instance is not { IsRunning: true }; i++) await Frame();
        await Frames(120);
        Save(dir, "title_standby.png");
        ReportContrast();

        // [ PRESS ANY KEY ] — 사람이 누르는 것과 같은 경로로 전원을 넣는다.
        var key = new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true };
        GetTree().Root.PushInput(key, true);
        await Frames(420);          // 표제 타이핑 → 축소 → 장비 점등까지
        Save(dir, "title_poweron.png");
        ReportContrast();
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private void Save(string dir, string file)
        => GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    // 조명 대비를 숫자로 — "구석은 거의 검정 · 책상은 빛 웅덩이" 가 되었는지 눈이 아니라 값으로 본다.
    // 화면 비율로 영역을 잡으므로 해상도가 달라져도 같은 자리를 잰다.
    private void ReportContrast()
    {
        var img = GetViewport().GetTexture().GetImage();
        var s = img.GetSize();

        float Lum(float x0, float y0, float x1, float y1)
        {
            double sum = 0; int n = 0;
            for (int y = (int)(y0 * s.Y); y < (int)(y1 * s.Y); y += 2)
            for (int x = (int)(x0 * s.X); x < (int)(x1 * s.X); x += 2)
            {
                var c = img.GetPixel(x, y);
                sum += c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
                n++;
            }
            return n == 0 ? 0f : (float)(sum / n);
        }

        // 방 구석(화면 귀퉁이 — HUD 가 없는 위쪽 두 곳과 왼쪽 아래).
        float cornerTL = Lum(0.00f, 0.06f, 0.07f, 0.20f);
        float cornerTR = Lum(0.93f, 0.06f, 1.00f, 0.20f);
        float cornerBL = Lum(0.00f, 0.80f, 0.06f, 0.95f);
        float corner = (cornerTL + cornerTR + cornerBL) / 3f;
        float deskPool = DeskLum(img);

        GD.Print($"[대비] 방 구석 {corner:0.000} (좌상 {cornerTL:0.000} · 우상 {cornerTR:0.000} · 좌하 {cornerBL:0.000})"
                 + $" · 책상 {deskPool:0.000} · 책상/구석 = {(corner > 0.0001f ? deskPool / corner : 999f):0.0}배");
        ReportHistogram(img);
    }

    // 화면이 "뿌연가" 를 재는 값. 눈으로는 "빛이 예쁘다" 와 "뿌옇다" 가 섞여 보이지만,
    // 숫자로는 갈린다 — **어두워야 할 곳이 실제로 어두운가**.
    //
    //   검정 바닥(p01 · p05)  이 값이 0 에서 멀어질수록 화면 전체에 막이 낀 것이다.
    //   순수 검정 비율        0.02 아래인 화소가 얼마나 되는가. 글로우가 번지면 0 에 수렴한다.
    //   p95 - p05            밝은 곳과 어두운 곳의 폭. 좁으면 대비가 죽은 것이다.
    private static void ReportHistogram(Image img)
    {
        var s = img.GetSize();
        var lum = new System.Collections.Generic.List<float>(s.X * s.Y / 16);
        int black = 0;
        for (int y = 0; y < s.Y; y += 4)
        for (int x = 0; x < s.X; x += 4)
        {
            var c = img.GetPixel(x, y);
            float l = c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
            lum.Add(l);
            if (l < 0.02f) black++;
        }
        lum.Sort();
        float P(float q) => lum[Mathf.Clamp((int)(q * (lum.Count - 1)), 0, lum.Count - 1)];
        GD.Print($"[밝기분포] p01 {P(0.01f):0.000} · p05 {P(0.05f):0.000} · p50 {P(0.5f):0.000}"
                 + $" · p95 {P(0.95f):0.000} · p99 {P(0.99f):0.000}");
        GD.Print($"[밝기분포] 순수 검정(<0.02) {black * 100f / lum.Count:0.0}% · 폭(p95-p05) {P(0.95f) - P(0.05f):0.000}");
    }

    // 책상면 띠(전화기 · 스위치박스가 놓인 줄)의 평균 밝기.
    private static float DeskLum(Image img)
    {
        var s = img.GetSize();
        double sum = 0; int n = 0;
        for (int y = (int)(0.86f * s.Y); y < (int)(0.97f * s.Y); y += 2)
        for (int x = (int)(0.22f * s.X); x < (int)(0.78f * s.X); x += 2)
        {
            var c = img.GetPixel(x, y);
            sum += c.R * 0.299f + c.G * 0.587f + c.B * 0.114f;
            n++;
        }
        return n == 0 ? 0f : (float)(sum / n);
    }

    // 그림자가 실제로 책상을 어둡게 만드는 양 — 그림자를 껐다 켠 두 장을 빼서 잰다.
    // 눈으로 "또렷한가"를 재는 대신, 그림자가 없을 때보다 책상이 몇 % 어두워지는지를 본다.
    // 채움광이 세면 이 값이 0 에 가까워진다(그림자가 씻겨 나간다).
    private async System.Threading.Tasks.Task ReportShadowStrength(Node scene, string dir)
    {
        var spots = scene.FindChildren("*ScreenLight", "SpotLight3D", true, false).Cast<SpotLight3D>().ToList();
        if (spots.Count == 0) { GD.Print("[그림자] ScreenLight 를 찾지 못함"); return; }

        await Frames(4);
        float withShadow = DeskLum(GetViewport().GetTexture().GetImage());
        foreach (var l in spots) l.ShadowEnabled = false;
        await Frames(6);
        var off = GetViewport().GetTexture().GetImage();
        float noShadow = DeskLum(off);
        off.SavePng(dir + "/lighting_live_noshadow_ref.png");
        foreach (var l in spots) l.ShadowEnabled = true;
        await Frames(4);

        float drop = noShadow > 0.0001f ? (noShadow - withShadow) / noShadow : 0f;
        GD.Print($"[그림자] 책상 밝기 그림자끔 {noShadow:0.000} → 켬 {withShadow:0.000}"
                 + $" · 그림자가 책상을 {drop * 100f:0.0}% 어둡게 한다");
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }
}
