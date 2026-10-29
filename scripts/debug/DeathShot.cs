using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 차폐문을 **열어 둔 채** 괴물을 들여보내 관리자 사망 연출을 눈으로 확인한다.
//
//   godot --path . --fixed-fps 30 res://scenes/debug/DeathShot.tscn -- <저장 폴더>
//
// **--fixed-fps 를 꼭 붙인다.** 사망 연출은 전부 합쳐 1초가 안 되는데, 한 장
// 저장할 때마다 실제로는 1초 가까이 걸린다. 그냥 돌리면 한 프레임이 1초어치
// 진행해 버려서 돌진·멈칫·쾅이 통째로 캡처 사이로 빠져나간다. 고정 프레임이면
// 한 프레임이 늘 1/30초어치만 진행하므로 연출 전체가 빠짐없이 찍힌다.
//
// 확인할 것(지시서 §7)
//   · 침입 직전에 인기척 신호가 먼저 온다 — 예고 없는 즉사 금지
//   · 괴물이 **실제 모델로** 카메라 쪽으로 돌진한다
//   · 끝나면 다음 날로 넘어가지 않고 별도의 사망 기록 화면에서 멈춘다
public partial class DeathShot : Node
{
    private string _dir = "";
    private string _monsterId = "absentee";
    private int _shot;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        // 두 번째 인자로 개체를 고른다(absentee · infant · spider). 비우면 결번자.
        _monsterId = args.Length > 1 ? args[1] : "absentee";
        // 세 번째 인자 "facing" 이면 본편을 띄우지 않고 **모델 방향만** 재 본다.
        if (args.Length > 2 && args[2] == "facing") { _ = Facing(); return; }
        _ = Run();
    }

    // ── 모델이 어느 쪽을 보고 있는가 ────────────────────────────────────
    //
    // 돌진 연출은 어두워서 앞뒤가 눈으로 구분되지 않는다. 밝은 빈 공간에 같은
    // 파이프라인(MonsterActor + MonsterDef.YawOffsetDegrees)으로 세워 놓고
    // yaw 0 과 180 을 나란히 찍어, 어느 쪽이 얼굴인지 **보고** 정한다.
    private async System.Threading.Tasks.Task Facing()
    {
        var def = GD.Load<MonsterDef>($"res://data/threats/monster_{_monsterId}.tres");
        if (def == null) { GD.Print("   정의를 찾지 못했다"); GetTree().Quit(); return; }

        var world = new Node3D();
        AddChild(world);
        var cam = new Camera3D { Position = new Vector3(0f, 1.6f, 0f), Current = true, Fov = 60f };
        world.AddChild(cam);           // 기본 방향 그대로 −Z 를 본다
        world.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-28f, 0f, 0f), LightEnergy = 2.2f,
        });
        world.AddChild(new OmniLight3D
        {
            Position = new Vector3(0f, 1.6f, -0.6f), OmniRange = 8f, LightEnergy = 4f,
        });
        world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.30f, 0.32f, 0.36f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.6f, 0.6f, 0.65f),
                AmbientLightEnergy = 1.6f,
            },
        });

        var actor = new MonsterActor();
        actor.Attach(world);
        if (!actor.Use(def)) { GD.Print("   모델을 올리지 못했다"); GetTree().Quit(); return; }

        // 카메라가 −Z 를 보므로 fwd = (0,0,−1) — 돌진 코드의 yaw 는 이때 0 이 된다.
        float floorY = 1.6f - Mathf.Clamp(def.TargetHeight - 0.16f, 0.52f, 1.80f);
        await Seconds(0.6);

        // 정지 자세(Idle)로 앞뒤를 먼저 가린다.
        foreach (float yaw in new[] { 0f, 180f })
        {
            actor.SetPose(new Vector3(0f, floorY, -2.6f), yaw);
            actor.PlayRole(def.AnimIdle, 1f);
            await Seconds(0.5);
            Shot($"facing_{_monsterId}_idle_yaw{(int)yaw:000}");
        }

        // 돌진에 쓰는 클립은 **시간에 따라** 몸이 도는지가 중요하다. 같은 각도로
        // 세워 놓고 클립 안의 여러 시점을 찍는다 — 앞을 보는 구간이 어디인지 본다.
        foreach ((string label, string clip) in new[]
                 { ("run", def.AnimRun), ("jump", def.AnimJumpscare) })
        {
            if (string.IsNullOrEmpty(clip)) continue;
            actor.SetPose(new Vector3(0f, floorY, -2.6f), 0f);
            actor.PlayRole(clip, 1f);
            foreach (double at in new[] { 0.0, 0.2, 0.4, 0.6, 0.9, 1.3, 1.8 })
            {
                actor.SeekRole(at);
                await Frame();
                await Frame();
                Shot($"facing_{_monsterId}_{label}_t{(int)(at * 100):000}");
            }
        }

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Run()
    {
        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        var sim = FacilitySimulation.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 3);
        GameModes.ForceSelect(GameMode.Competition);
        typeof(ShiftFlowController)
            .GetField("_skipScheduleIntroOnce", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        var ids = sim.GetActiveEmployeeIds();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);

        GD.Print("\n\n################ 관리자 사망 ################");

        // 문은 열어 둔다 — 막지 못했을 때 어떻게 되는지를 보는 장면이다.
        var seg = sim.Corridors.ById("corridor_north");
        sim.Corridors.Unseal(seg.Id);
        Ok(!seg.Sealed, "북측 차폐문이 열려 있다");

        var t = sim.Threats.ForceSpawn(_monsterId, "corridor_north");
        Ok(t != null, $"{_monsterId} 가 북측 복도에 들어섰다");
        if (t == null) { GetTree().Quit(); return; }

        int dayBefore = GameState.Instance?.CurrentDay ?? 0;
        t.Phase = ThreatPhase.Approach;
        t.Progress = 0.86f;
        sim.SetCorridorSurveillance("corridor_north");
        await Seconds(0.8);
        Shot("01_문앞까지");

        // 문 앞 → (경고 유예) → 침입.
        for (int i = 0; i < 900 && t.Phase != ThreatPhase.AtDoor; i++) await Frame();
        Ok(t.Phase == ThreatPhase.AtDoor, "문 앞에 섰다(아직 들어오지 않았다)");
        Shot("02_문앞_유예");

        for (int i = 0; i < 1800 && !AdminDeathDirector.IsPlaying; i++) await Frame();
        Ok(AdminDeathDirector.IsPlaying, "침입 — 사망 연출이 시작됐다");

        // ① 정적 1.2초 — 여기서는 **아무것도 없어야 한다.** 한 장만 찍어
        //    화면이 조용한지(붉지 않은지, 괴물이 안 보이는지)만 확인한다.
        //    --fixed-fps 30 기준 한 프레임이 1/30초다.
        for (int f = 0; f < 18; f++) await Frame();   // 약 0.6초
        Shot("03_정적");
        for (int f = 0; f < 16; f++) await Frame();   // 들이밈 직전까지

        // ② 다섯 박자(들이밈 → 젖힘 → 박치기 → 비명) 약 1.7초 — 3프레임(0.1초)마다 찍는다.
        //    박치기는 0.06초라 두 프레임이면 지나가므로 그 언저리만 매 프레임 찍는다.
        for (int i = 0; i < 60; i++)
        {
            await Frame();
            if (i % 3 == 0 || (i >= 32 && i <= 42)) Shot($"04_팍_{i:00}");
        }

        // 기록 화면.
        for (int i = 0; i < 600 && AdminDeathDirector.IsPlaying; i++) await Frame();
        await Seconds(1.2);
        Shot("06_사망기록");
        Ok(GameState.Instance?.CurrentPhase == GamePhase.Result, "근무가 멈춘 상태로 남아 있다");
        Ok(GameState.Instance?.CurrentDay == dayBefore, "다음 날로 넘어가지 않았다");

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (헤드리스 — {name} 저장 안 됨)"); return; }
        img.SavePng($"{_dir}/death_{_shot:00}_{name}.png");
        _shot++;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
