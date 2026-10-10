using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 스토리 컷인 화면 확인용 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/StoryCutinShot.tscn -- <저장 폴더>
//   godot --path . res://scenes/debug/StoryCutinShot.tscn -- <저장 폴더> ingame
//
// 기본은 어두운 바탕 위에 컷인만 올려 배치 · 표정 · 화자 명암을 본다.
// `ingame` 을 주면 **실제 메인 씬(3D 관제실)을 띄우고 근무 중에** 컷인을 올려 찍는다 —
// 컷인이 화면 전체를 덮는 오버레이이지 모니터 안 화면이 아니라는 것이 여기서 보인다.
//
// 대사는 확인용 더미 문장이다 — DAY0 실제 대사가 아니다.
public partial class StoryCutinShot : Node
{
    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        if (args.Length > 1 && args[1] == "ingame") { await RunInGame(dir); return; }
        GameState.Instance?.ResetRun(1);
        FacilitySimulation.Instance?.ResetRun();
        GameState.Instance?.SetPhase(GamePhase.Live);

        // 화면 전체 해상도로 그린다(컷인은 CanvasLayer 다 — 실제 게임과 같은 비율).
        var vp = new SubViewport
        {
            Size = new Vector2I(1920, 1080),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
        };
        AddChild(vp);

        // 3D 관제 화면 대신 어두운 바탕 — 컷인이 배경을 덮지 않는지 보려는 것이다.
        var bg = new ColorRect { Color = new Color(0.05f, 0.06f, 0.07f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        vp.AddChild(bg);

        var hud = new StoryCutinHud();
        vp.AddChild(hud);
        await Frames(4);

        // ① 혼자 — 화면 정중앙
        hud.BeginBeat();
        hud.ShowLine(new StoryLine
        {
            SpeakerEmployeeId = "rabbit", Side = CutinSide.Left, Expression = "smile",
            Text = "관리자님, 이쪽 작업이 멈췄어요. 자재가 부족한 것 같은데요? (확인용 문장)",
        });
        await Frames(90);
        Save(vp, dir, "cutin_1_solo_center.png");

        // ② 한 명 더 등장 — 토끼가 왼쪽으로 비켜 주고 늑대가 오른쪽에서 페이드로
        hud.ShowLine(new StoryLine
        {
            SpeakerEmployeeId = "wolf", Side = CutinSide.Right, Expression = "bad",
            Text = "제가 가 보겠습니다. 혼자서는 안 되는 일입니다. (확인용 문장)",
        });
        await Frames(12);
        Save(vp, dir, "cutin_2_entering_midway.png");   // 비켜 주는 · 나타나는 도중
        await Frames(120);
        Save(vp, dir, "cutin_3_pair_right_speaks.png");

        // ③ 화자 교대 — 왼쪽이 말한다
        hud.ShowLine(new StoryLine
        {
            SpeakerEmployeeId = "rabbit", Side = CutinSide.Left, Expression = "smile",
            Text = "그럼 같이 가요! 혼자 가면 위험하잖아요. (확인용 문장)",
        });
        await Frames(90);
        Save(vp, dir, "cutin_4_pair_left_speaks.png");

        // ④ 한 명 퇴장 — 늑대가 페이드로 빠지고 토끼가 정중앙으로 돌아온다
        hud.ShowLine(new StoryLine
        {
            SpeakerEmployeeId = "rabbit", Side = CutinSide.Left, Expression = "bad",
            ExitSide = CutinSide.Right,
            Text = "...가 버렸네. (확인용 문장)",
        });
        await Frames(12);
        Save(vp, dir, "cutin_5_exiting_midway.png");    // 사라지는 · 가운데로 가는 도중
        await Frames(140);
        Save(vp, dir, "cutin_6_back_to_center.png");

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // --- 실제 관제실 위에서 ------------------------------------------------
    //
    // 메인 씬을 그대로 띄우고 DAY1 근무까지 들어간 뒤, 그 씬에 이미 붙어 있는
    // StoryCutinHud(화면 전체 CanvasLayer)에 대사를 올려 **창 전체**를 찍는다.
    private async System.Threading.Tasks.Task RunInGame(string dir)
    {
        DebugEntryPoint.SkipTitleOnNextBoot();
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        // 배치 화면까지.
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frames(1);
        GD.Print($"phase after boot = {GameState.Instance?.CurrentPhase}");

        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        for (int i = 0; i < 300; i++) await Frames(1);
        Save(dir, "ingame_0_schedule.png");

        // 근무 시작 — 컷인은 근무 중에 뜨는 연출이다.
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frames(1);
        for (int i = 0; i < 180; i++) await Frames(1);
        GD.Print($"phase before cutin = {GameState.Instance?.CurrentPhase}");
        Save(dir, "ingame_1_shift_no_cutin.png");

        // DAY1 시작에는 '오늘의 업무' 패드가 올라와 있다 — 내려놓고 찍는다.
        // (컷인이 도는 동안에는 애초에 패드를 꺼낼 수 없다. AdminPad3D.CanOpen 이
        //  ControlRoom3DController.IsInputLocked 를 보고 거절하는데, 컷인이 그 잠금을 건다.)
        if (AdminPad3D.Instance?.IsOpen == true) AdminPad3D.Instance.Close();
        for (int i = 0; i < 150 && AdminPad3D.Instance?.IsOpen == true; i++) await Frames(1);
        for (int i = 0; i < 60; i++) await Frames(1);
        Save(dir, "ingame_1b_pad_down.png");

        var hud = StoryCutinHud.Instance;
        if (hud == null) { GD.PushError("StoryCutinHud 가 메인 씬에 없다"); GetTree().Quit(1); return; }

        hud.BeginBeat();
        hud.ShowLine(new StoryLine
        {
            SpeakerEmployeeId = "rabbit", Side = CutinSide.Left, Expression = "smile",
            Text = "관리자님, 이쪽 작업이 멈췄어요. 자재가 부족한 것 같은데요? (확인용 문장)",
        });
        for (int i = 0; i < 120; i++) await Frames(1);
        Save(dir, "ingame_2_cutin_single.png");

        hud.ShowLine(new StoryLine
        {
            SpeakerEmployeeId = "dog", Side = CutinSide.Right, Expression = "smile",
            Text = "그럼 정비실부터 봐야 하지 않을까요? 제가 가 볼게요. (확인용 문장)",
        });
        for (int i = 0; i < 120; i++) await Frames(1);
        Save(dir, "ingame_3_cutin_pair.png");

        hud.HideNow();
        for (int i = 0; i < 60; i++) await Frames(1);
        Save(dir, "ingame_4_after.png");

        // ── 모니터2 안의 스토리(DAY1~5 메인) ────────────────────────────
        // 같은 컷인이 모니터 영상 안으로 들어간다. 여기서 보는 것은 글자 크기와
        // 제목 줄("BREAK ROOM" · ● REC)이 서로 밟지 않는가다.
        var dir2 = StoryCutinDirector.Instance;
        if (dir2 != null && dir2.EnterMonitor())
        {
            for (int i = 0; i < 60; i++) await Frames(1);
            hud.BeginBeat();
            hud.ShowLine(new StoryLine
            {
                SpeakerEmployeeId = "rabbit", Side = CutinSide.Left, Expression = "smile",
                Text = "어제 그거... 저 혼자 본 거 아니죠? 사람처럼 생겼는데, 사람은 아니었어요.",
            });
            for (int i = 0; i < 150; i++) await Frames(1);
            Save(dir, "ingame_8_monitor_single.png");
            // 모니터 화면 자체도 따로 찍는다 — 3D 너머로 보면 글자 크기를 가늠할 수 없다.
            ControlRoom3DController.Instance?.StoryViewport?.GetTexture()?.GetImage()
                ?.SavePng(dir + "/ingame_8b_story_screen.png");

            hud.ShowLine(new StoryLine
            {
                SpeakerEmployeeId = "cat", Side = CutinSide.Right, Expression = "",
                Text = "저는 소리만 들었어요. 그쪽 방에서 뭔가... 사람 소리 같은 게 났거든요.",
            });
            for (int i = 0; i < 150; i++) await Frames(1);
            Save(dir, "ingame_9_monitor_pair.png");
            ControlRoom3DController.Instance?.StoryViewport?.GetTexture()?.GetImage()
                ?.SavePng(dir + "/ingame_9b_story_screen.png");

            hud.HideNow();
            dir2.ExitMonitor();
            for (int i = 0; i < 90; i++) await Frames(1);
        }

        // ── Phase 2 : 전화 통화 중 스탠딩 ───────────────────────────────
        // 같은 공용 컴포넌트가 통화창에서도 쓰인다. 영상통화처럼 보이게 하지 않는다 —
        // 전화창은 그대로이고 상대의 모습만 옆에 선다.
        var phone = PhoneCallHud.Instance;
        if (phone != null)
        {
            // 패드가 다시 올라와 있으면 내려놓고 찍는다(패드 화면이 통화창을 가린다).
            if (AdminPad3D.Instance?.IsOpen == true) AdminPad3D.Instance.Close();
            for (int i = 0; i < 150 && AdminPad3D.Instance?.IsOpen == true; i++) await Frames(1);
            for (int i = 0; i < 40; i++) await Frames(1);

            phone.Open("rabbit");
            for (int i = 0; i < 150; i++) await Frames(1);
            Save(dir, "ingame_5_call_rabbit.png");

            phone.RequestClose();
            for (int i = 0; i < 40; i++) await Frames(1);
            phone.Open("wolf");
            for (int i = 0; i < 150; i++) await Frames(1);
            Save(dir, "ingame_6_call_wolf.png");

            phone.RequestClose();
            for (int i = 0; i < 90; i++) await Frames(1);
            Save(dir, "ingame_7_call_closed.png");
        }

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // 창 전체(3D 관제실 + CanvasLayer) 를 그대로 찍는다.
    private void Save(string dir, string file)
        => GetViewport().GetTexture().GetImage().SavePng(dir + "/" + file);

    private static void Save(SubViewport vp, string dir, string file)
        => vp.GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
