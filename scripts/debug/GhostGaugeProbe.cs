using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// DAY1~5 근무에서 ① 괴물 소멸 게이지와 ② 전화기 클릭 범위를 실제 씬에서 확인한다.
//
//   godot --path . res://scenes/debug/GhostGaugeProbe.tscn -- <저장 폴더>
//
// ① 게이지는 "가상 시뮬에서는 보이는데 이후 DAY 에서는 안 보인다" 는 제보로 만들었다.
//    뜨는 조건은 CCTVMonitorView.TickGhostOverlay 의 한 줄뿐이라
//    (show = feed && ghost.Active && ghost.ActiveRoomId == 보는 방) 그 셋을 따로 찍는다.
//    **화면도 한 장 남긴다** — 조건이 전부 참인데 다른 UI 에 가려 안 읽히는 경우가 있다.
//    (실제로 그랬다: 업무 진행바가 게이지와 같은 자리에 겹쳐 그려지고 있었다.)
//
// ② 전화기 클릭 상자는 DAY1~5 에서만 본체까지 넓어진다. 교육일은 기존 범위 그대로다.
public partial class GhostGaugeProbe : Node
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
        DebugEntryPoint.SkipTitleOnNextBoot();
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        var sim = FacilitySimulation.Instance;
        var gs = GameState.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 2);

        // DAY2 배치로 들어갈 때 그 날의 스토리는 건너뛴다.
        // (스토리가 도는 동안 시뮬레이션이 멈춘다 — 그 상태로 근무를 시작하면 괴물도 시계도
        //  얼어붙어서, 게이지가 안 차는 것이 스토리 탓인지 가릴 수 없게 된다.)
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        var ids = sim.GetActiveEmployeeIds();
        string[] rooms = { "core_room", "core_room", "maintenance_room", "guard_room", "storage_room", "power_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(1.5);

        // ── ① 괴물 소멸 게이지 ────────────────────────────────────────
        GD.Print("\n\n################ 괴물 소멸 게이지 · 전화기 클릭 범위 ################");
        GD.Print("\n===== [①] DAY2 근무에서 소멸 게이지 =====");

        const string room = "core_room";
        Ok(sim.Ghost?.ForceAppear(room, sim, 9999f) ?? false, $"괴물이 {room} 에 떴다");
        sim.SetSurveillanceTarget(room);
        await Seconds(1.2);

        var cctv = CCTVMonitorView.Instance;
        Ok(sim.Ghost?.Active == true, "괴물이 떠 있다");
        Ok(sim.CctvFeedLive, "그 방 영상이 화면에 떠 있다");
        Ok(cctv?.DispelGaugeVisible == true, "게이지가 화면에 나온다");

        float before = sim.Ghost?.WatchedSeconds ?? 0f;
        for (int i = 0; i < 3; i++)
        {
            await Seconds(1.0);
            GD.Print($"      +{i + 1}초  관측 {sim.Ghost?.WatchedSeconds:0.00}s  " +
                     $"진행 {sim.Ghost?.DispelRatio * 100f:0}%  게이지 {cctv?.DispelGaugeVisible}");
            if (i != 1) continue;
            // 게이지가 차 있는 동안 화면을 남긴다 — 조건이 전부 참인데 가려서 안 읽히는
            // 경우가 실제로 있었다(업무 진행바와 같은 자리).
            GetViewport()?.GetTexture()?.GetImage()?.SavePng($"{_dir}/ghost_gauge_seat.png");
            ControlRoom3DController.Instance?.CctvViewport?.GetTexture()?.GetImage()
                ?.SavePng($"{_dir}/ghost_gauge_cctv.png");
        }
        Ok((sim.Ghost?.WatchedSeconds ?? 0f) > before, "보고 있으면 실제로 차오른다");

        // ── ② 전화기 클릭 범위 ────────────────────────────────────────
        GD.Print("\n===== [②] 전화기 클릭 범위 =====");
        var phone = Phone3D.Instance;
        if (phone == null) { Ok(false, "전화기를 찾았다"); Finish(); return; }

        var wide = phone.ClickBoxSize;
        var narrow = phone.NarrowClickSize;
        GD.Print($"      DAY{gs?.CurrentDay} 상자 {wide}   기본 상자 {narrow}");
        Ok(wide.X > narrow.X && wide.Y > narrow.Y && wide.Z > narrow.Z,
            $"DAY1~5 는 본체까지 넓다 (가로 ×{wide.X / narrow.X:0.00} · 세로 ×{wide.Y / narrow.Y:0.00} · 깊이 ×{wide.Z / narrow.Z:0.00})");

        // 교육일로 되돌리면 기존 범위로 돌아와야 한다.
        gs?.ResetRun(0);
        await Seconds(0.3);
        Ok(phone.ClickBoxSize.IsEqualApprox(narrow), $"가상 시뮬(DAY0)은 기존 범위 그대로 ({phone.ClickBoxSize})");

        gs?.ResetRun(2);
        await Seconds(0.3);
        Ok(phone.ClickBoxSize.IsEqualApprox(wide), "다시 DAY2 로 오면 또 넓어진다");

        Finish();
    }

    private void Finish()
    {
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GD.Print($"saved → {_dir}");
        GetTree().Quit();
    }

    private void Ok(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
