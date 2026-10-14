using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 얼굴 점프스케어가 **여전히 제대로 뜨는가**를 눈으로 확인한다.
//
//   godot --path . res://scenes/debug/EntityProbe.tscn -- <저장 폴더>
//
// 왜 따로 재는가 — 성능 작업에서 괴물 모델을 미리 읽어 두고 안 쓰는 것은
// ProcessMode.Disabled 로 재워 두게 바꿨다(MonsterActor.Sleep). 재운 것을 다시
// 깨우는 책임은 MonsterActor.Use 에 있는데, 이 연출은 Use 를 부른 뒤 **배우를 거치지
// 않고** _faceActor.Root.Visible 을 직접 켠다. 즉 깨우기가 한 군데라도 빠지면
// "모델은 떠 있는데 애니메이션이 멈춰 있다" 가 되고, 0.1초짜리 연출에서는
// 그게 곧 연출이 죽은 것이다. 그래서 숫자가 아니라 그림으로 확인한다.
//
// 확인할 것
//   · 얼굴이 화면을 채우는가(검은 화면 · 빈 화면이 아닌가)
//   · 세 프레임이 서로 **다른가** — 같으면 애니메이션이 멈춘 것이다
//   · 0.3초 뒤에는 사라졌는가
public partial class EntityProbe : Node
{
    private string _dir = "";
    private int _shot;

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
            GD.PrintErr("EntityProbe: 그리지 않으면 확인할 수 없다. 창 모드로 실행할 것.");
            GetTree().Quit(1);
            return;
        }

        // ScareShot 과 **같은 길**로 들어간다 — 이 연출은 앉아 있는 중앙제어실 카메라의
        // 자식으로 붙으므로, 거기까지 실제로 가 있지 않으면 화면에 아무것도 안 나온다.
        DebugEntryPoint.SkipTitleOnNextBoot();
        GameModes.ForceSelect(GameMode.Competition);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 1800 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(0.8);

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
        await Seconds(0.8);

        var ids = sim.GetActiveEmployeeIds();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room", "storage_room", "core_room" };
        for (int i = 0; i < rooms.Length && i < ids.Count; i++) sim.AssignToRoom(ids[i], rooms[i]);
        DebugEntryPoint.Call(flow, "EnterShift");
        for (int i = 0; i < 1800 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.0);

        var hd = NSP.Ui.HorrorDirector.Instance;
        Ok(sim != null, "시뮬레이션이 올라왔다");
        Ok(hd != null, "HorrorDirector 가 있다");
        if (sim == null || hd == null) { GetTree().Quit(1); return; }

        // 사전 준비가 끝났는지 — 끝나 있어야 이 연출이 모델을 새로 읽지 않는다.
        // nowarm 으로 돌리면 준비를 끄고 같은 길을 밟는다 — 대조군.
        bool warm = MonsterWarmup.Done;
        GD.Print(warm ? "   사전 준비 **켬**" : "   사전 준비 **끔**(nowarm · 대조군)");

        // 연출기가 쥔 배우를 직접 들여다본다 — 그림만으로는 판정할 수 없다.
        // 이 연출은 붉은 섬광과 노이즈를 **모델 위에** 덮으므로, 화면이 붉다는 것은
        // 연출이 돌았다는 뜻이지 모델이 살아 있다는 뜻이 아니다.
        var actor = typeof(NSP.Ui.HorrorDirector)
            .GetField("_faceActor", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(hd) as MonsterActor;
        Ok(actor != null, "얼굴 배우를 찾았다");
        if (actor == null) { GetTree().Quit(1); return; }

        // 준비해 둔 모델은 **자고 있어야** 한다 — 깨어 있으면 쓰지도 않는 스켈레톤을
        // 매 프레임 갱신한다(이것 때문에 평상시 FPS 가 25 → 18 로 떨어진 적이 있다).
        int asleep = 0, awake = 0;
        foreach (var c in actor.Root.GetChildren())
            if (c is Node3D s)
            {
                if (s.ProcessMode == ProcessModeEnum.Disabled) asleep++; else awake++;
            }
        GD.Print($"   준비된 모델 {asleep + awake}개 — 자는 중 {asleep} · 깨어 있음 {awake}");
        if (warm) Ok(asleep > 0, "쓰지 않는 모델은 재워 두었다");
        Ok(!actor.Root.Visible, "연출 전에는 꺼져 있다");

        Shot("before");

        GD.Print("\n################ 얼굴 점프스케어 ################");
        hd.PlayEntityFaceJumpscare();
        await Frame();
        Shot("face_a");

        var anim = FindAnim(actor.Root);
        Ok(actor.Root.Visible, "화면에 떴다");
        // 거미는 뼈도 클립도 없다(MonsterActor 머리말) — 재생기가 없는 것이 정상이다.
        if (anim != null)
        {
            Ok(anim.ProcessMode != ProcessModeEnum.Disabled, "쓰는 모델은 깨어 있다");
            Ok(anim.IsPlaying(), "클립이 돌고 있다");
        }
        else GD.Print("   (이 개체는 클립이 없다 — 코드가 몸을 흔든다)");

        double t0 = anim?.CurrentAnimationPosition ?? 0d;
        await Frame();
        Shot("face_b");
        await Frame();
        double t1 = anim?.CurrentAnimationPosition ?? 0d;
        Shot("face_c");
        // 이 연출은 **한 자세를 0.1초 보여 주는 섬광**이다 — MonsterCast.ScareClipAt 가
        // 정면을 보는 지점으로 Seek 한 뒤 그 자세로 뜬다. 그래서 클립 위치가 거의
        // 움직이지 않는 것이 정상이다. 여기서 봐야 할 것은 "진행했는가" 가 아니라
        // **사전 준비를 켠 쪽과 끈 쪽(nowarm)이 같은 자세로 뜨는가** 다 — 같아야
        // 성능 작업이 연출을 바꾸지 않았다고 말할 수 있다. 두 번 돌려 비교한다.
        float seekAt = MonsterCast.ScareClipAt(actor.Def);
        GD.Print($"   개체 {actor.MonsterId} · 클립 위치 {t0:0.000}s → {t1:0.000}s  (Seek 지점 {seekAt:0.000}s)");
        Ok(Mathf.Abs((float)t0 - seekAt) < 0.05f, "의도한 자세(Seek 지점)에서 떴다");

        await Seconds(0.5);
        Shot("after");
        Ok(!actor.Root.Visible, "연출이 끝나고 사라졌다");

        GD.Print($"\nsaved → {_dir}");
        GetTree().Quit();
    }

    private void Shot(string name)
    {
        var img = GetViewport()?.GetTexture()?.GetImage();
        if (img == null) { GD.Print($"   (그림 없음 — {name})"); return; }
        img.SavePng($"{_dir}/face_{_shot:00}_{name}.png");
        _shot++;
    }

    // 지금 **보이는** 모델의 재생기. 재워 둔 것(Disabled)은 건너뛴다.
    private static AnimationPlayer FindAnim(Node n)
    {
        if (n is Node3D s && s.ProcessMode == ProcessModeEnum.Disabled) return null;
        if (n is AnimationPlayer ap) return ap;
        foreach (var c in n.GetChildren())
            if (FindAnim(c) is { } found) return found;
        return null;
    }

    private static void Ok(bool ok, string what) => GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
