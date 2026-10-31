using System.Collections.Generic;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// **마우스가 엉뚱한 곳에 들어가는 고장**을 잡는다.
//
//   godot --path . res://scenes/debug/SurfaceInputProbe.tscn -- <저장 폴더>
//
// 3D 화면(CRT · 책상 위 배치표 · 관리자 패드)의 클릭 판정은 전부 같은 식이다:
//
//     화면에 쏜 광선 → UV(0~1) → **뷰포트 픽셀 좌표** = UV × 뷰포트 크기
//
// 그래서 **뷰포트 크기가 바뀌는데 그 사실이 반영되지 않으면** 화면은 멀쩡한데
// 클릭만 어긋난다. 눈으로는 원인을 알 수 없는 종류의 고장이고, 실제로 그렇게 깨졌다:
//
//   · 성능 작업이 "안 쓰는 화면의 렌더 타깃을 놓아 주는" 코드를 넣으면서,
//     주인이 따로 있는 화면(배치표 · 관리자 패드)까지 2×2 로 줄였다.
//   · CRT 가 어두운 단계에서는 **붙어 있는** 화면도 Once 로 한 장만 그리고 엔진이
//     그 뒤 Disabled 로 내려놓는다 — "안 쓰는 화면" 과 구분되지 않아 같이 줄었다.
//
// 그래서 여기서는 두 가지를 본다.
//   ① 시간이 지나도 **쓰는 화면의 크기가 그대로인가** (놓아 주기가 넘치지 않는가)
//   ② 화면 네 귀퉁이를 찍었을 때 **그 좌표가 뷰포트 크기와 맞는가**
public partial class SurfaceInputProbe : Node
{
    private string _dir = "";
    private int _pass, _fail;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        _ = Run();
    }

    private async System.Threading.Tasks.Task Run()
    {
        if (DisplayServer.GetName() == "headless")
        {
            GD.PrintErr("SurfaceInputProbe: 광선 투사에는 실제 창이 필요하다. 창 모드로 실행할 것.");
            GetTree().Quit(1);
            return;
        }

        // **근무 배치 화면까지 실제로 들어간다.** 프롤로그 컷신에서 멈추면 카메라가
        // 스토리 모니터에 붙어 있어 배치 화면을 누를 수 없다(처음에 그렇게 재서
        // 엉뚱한 좌표가 나왔다). ScareShot 과 같은 길로 들어간다.
        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1800 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(0.8);

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameModes.ForceSelect(GameMode.Competition);
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.5);

        var ctl = ControlRoom3DController.Instance;
        Ok(ctl != null, "제어실이 올라왔다");
        if (ctl == null) { Done(); return; }

        GD.Print("\n################ 근무 배치 - 들어온 직후 ################");
        var before = Snapshot();
        Report(before);
        Shot("schedule_0s");

        // 놓아 주기(IdleReleaseSeconds = 4초)가 돌고도 남을 만큼 기다린다.
        GD.Print("\n################ 8초 뒤 - 크기가 그대로인가 ################");
        await Seconds(8.0);
        var after = Snapshot();
        Report(after);
        Shot("schedule_8s");

        // **지금 쓰이고 있는** 화면이 2×2 로 줄어 있지는 않은가. (0초 때의 뷰포트와
        // 비교하면 안 된다 — 그 사이에 화면이 바뀌면 옛 뷰포트는 줄어 있는 게 맞다.)
        foreach (var (name, vp, size) in after)
            Ok(size.X > 4 && size.Y > 4, $"{name} - 쓰는 화면이 온전한 크기다 ({size})");

        GD.Print("\n################ 클릭 좌표가 뷰포트와 맞는가 ################");
        foreach (var s in Surfaces())
        {
            var vp = s.TargetViewport;
            if (vp == null || !GodotObject.IsInstanceValid(vp)) continue;
            string name = (s as Node)?.Name.ToString() ?? s.GetType().Name;

            // 화면 한가운데를 맞혔다고 치고, 그 표면이 내놓는 좌표가 뷰포트의
            // 한가운데인지 본다. 광선은 표면 앞 1 m 에서 수직으로 쏜다 —
            // 카메라 위치·화각과 무관하게 **그 표면의 변환만** 시험하게.
            if (s is not Node3D n3) continue;
            var fwd = n3.GlobalTransform.Basis.Z.Normalized();
            var origin = n3.GlobalPosition + fwd * 1.0f;
            if (!s.TryProjectRay(origin, -fwd, clamp: false, out var mid)) continue;

            var want = (Vector2)vp.Size * 0.5f;
            bool okv = mid.DistanceTo(want) <= 2f;
            Ok(okv, $"{name} - 한가운데 클릭이 뷰포트 한가운데로 간다 (얻은 {mid.Round()} / 기대 {want.Round()})");
        }

        GD.Print("\n################ 끝에서 끝까지 - 진짜 마우스로 눌러 본다 ################");
        await ClickThrough(ctl);

        // **품질을 바꾼 직후에도 그대로인가.**
        //
        // 여기가 실제로 깨졌던 자리다. 품질을 바꾸면 CRT 뷰포트의 렌더 해상도가 바뀌는데,
        // 모니터는 그 크기를 Configure 때 한 번 복사해 두고 있었다. 그래서 **화면은
        // 멀쩡한데**(프레임 배율도 같이 바뀌니까) 클릭 좌표만 배율만큼 어긋났다.
        foreach (var q in new[] { GameSettings.Quality.VeryLow, GameSettings.Quality.High })
        {
            var was = GameSettings.GraphicsQuality;
            GameSettings.GraphicsQuality = q;
            await Frames(10);
            GD.Print($"\n################ 품질 {was} → {q} 로 바꾼 직후 ################");
            await ClickThrough(ctl);
        }

        Done();
    }

    // ── 끝에서 끝까지 ───────────────────────────────────────────────
    //
    // 가운데를 맞히는 시험은 **배율이 틀려도 통과한다**(가운데는 어떤 배율에서도
    // 가운데다). 그래서 화면 **구석에 있는 실제 버튼**을 노리고, 창 좌표로 진짜
    // 마우스 사건을 밀어 넣어 그 버튼이 눌렸는지 본다.
    //
    //   배치 화면의 직원 카드(뷰 로컬 좌표)
    //     → 뷰포트 픽셀 (× 스케일 프레임 배율)
    //     → 화면 UV      (÷ 뷰포트 크기)
    //     → 모니터 평면 위의 월드 좌표
    //     → 카메라로 역투영한 **창 좌표**
    //     → Viewport.PushInput  ← 게임이 실제로 받는 입력과 같은 것
    private async System.Threading.Tasks.Task ClickThrough(ControlRoom3DController ctl)
    {
        var sim = FacilitySimulation.Instance;
        var vp = ctl.ScheduleMapViewport;
        var cam = GetViewport()?.GetCamera3D();
        var screen = Screen(ctl, "01");
        var map = FindIn<NSP.Ui.ScheduleMapView>(vp);

        Ok(map != null, "모니터1 에 배치 화면이 떠 있다");
        Ok(screen != null && screen.TargetViewport == vp, "모니터1 이 그 화면에 물려 있다");
        if (map == null || screen == null || cam == null || sim == null) return;

        // 배치 화면이 한 번은 그려진 뒤에 누른다 — 레이아웃이 아직 안 잡힌 첫 프레임에
        // 누르면 아무것도 안 잡혀 "어긋났다" 로 잘못 읽힌다.
        await Frames(10);
        map.ComputeLayout(sim);
        var frame = vp.GetChild<Control>(0);
        float k = frame?.Scale.X ?? 1f;

        GD.Print($"   카메라 {cam.Name}, 모니터 중심 월드 {screen.GlobalPosition} → 창 {cam.UnprojectPosition(screen.GlobalPosition).Round()}");
        GD.Print($"   모니터 네 귀퉁이 창 좌표: " +
                 $"↖{cam.UnprojectPosition(screen.GlobalTransform * new Vector3(-0.5f, 0.5f, 0f)).Round()} " +
                 $"↗{cam.UnprojectPosition(screen.GlobalTransform * new Vector3(0.5f, 0.5f, 0f)).Round()} " +
                 $"↙{cam.UnprojectPosition(screen.GlobalTransform * new Vector3(-0.5f, -0.5f, 0f)).Round()} " +
                 $"↘{cam.UnprojectPosition(screen.GlobalTransform * new Vector3(0.5f, -0.5f, 0f)).Round()}");
        GD.Print($"   뷰포트 {vp.Size}, 프레임 배율 {k:0.000}, 창 {DisplayServer.WindowGetSize()}, FlipV {screen.FlipV}");

        foreach (string emp in new[] { "sheep", "cat", "wolf" })
        {
            var card = map.RosterCardOf(emp);
            if (card.Size.X <= 0f) { GD.Print($"   ({emp} 카드를 찾지 못했다 - 건너뜀)"); continue; }

            Vector2 inViewport = card.GetCenter() * k;
            Vector2 uv = inViewport / (Vector2)vp.Size;
            float lx = uv.X - 0.5f;
            float ly = screen.FlipV ? uv.Y - 0.5f : 0.5f - uv.Y;
            Vector3 world = screen.GlobalTransform * new Vector3(lx, ly, 0f);
            Vector2 windowPos = cam.UnprojectPosition(world);
            GD.Print($"   {emp}: 카드 {card}, 뷰포트 {inViewport.Round()}, uv ({uv.X:0.000},{uv.Y:0.000}) " +
                     $"- 로컬 ({lx:0.000},{ly:0.000}), 창 {windowPos.Round()}");

            Push(new InputEventMouseButton
            {
                Position = windowPos, GlobalPosition = windowPos,
                ButtonIndex = MouseButton.Left, Pressed = true,
            });
            await Frames(2);
            Push(new InputEventMouseButton
            {
                Position = windowPos, GlobalPosition = windowPos,
                ButtonIndex = MouseButton.Left, Pressed = false,
            });
            await Frames(3);

            Ok(map.SelectedEmployeeId == emp,
                $"창 좌표 {windowPos.Round()} 를 눌렀더니 **{emp}** 가 잡혔다 (잡힌 것: " +
                $"{(string.IsNullOrEmpty(map.SelectedEmployeeId) ? "없음" : map.SelectedEmployeeId)})");
        }

        Shot("after_click");
    }

    private void Push(InputEvent e) => GetViewport().PushInput(e, inLocalCoords: true);

    private static MonitorScreen3D Screen(ControlRoom3DController ctl, string token)
    {
        foreach (var s in ctl.Screens)
            if (s.Name.ToString().Contains(token)) return s;
        return null;
    }

    private static T FindIn<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
            if (FindIn<T>(c) is { } found) return found;
        return null;
    }

    // 지금 살아 있는 투사 표면 전부.
    private List<IProjectionSurface> Surfaces()
    {
        var into = new List<IProjectionSurface>();
        Walk(GetTree().Root, into);
        return into;

        static void Walk(Node n, List<IProjectionSurface> into)
        {
            if (n is IProjectionSurface s) into.Add(s);
            foreach (var c in n.GetChildren()) Walk(c, into);
        }
    }

    private List<(string Name, SubViewport Vp, Vector2I Size)> Snapshot()
    {
        var list = new List<(string, SubViewport, Vector2I)>();
        foreach (var s in Surfaces())
        {
            var vp = s.TargetViewport;
            if (vp == null || !GodotObject.IsInstanceValid(vp)) continue;
            list.Add(((s as Node)?.Name.ToString() ?? s.GetType().Name, vp, vp.Size));
        }
        return list;
    }

    private static void Report(List<(string Name, SubViewport Vp, Vector2I Size)> list)
    {
        foreach (var (name, vp, size) in list)
            GD.Print($"   {name,-24} {size}  ({vp.RenderTargetUpdateMode})");
    }

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (그림 없음 - {name})"); return; }
        img.SavePng($"{_dir}/surface_{name}.png");
    }

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private void Done()
    {
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
