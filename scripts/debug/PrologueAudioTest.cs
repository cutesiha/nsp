using System.Linq;
using Godot;
using NSP.Core;
using NSP.Prologue;
using NSP.View;

namespace NSP.Debug;

// 프롤로그 재난 연출의 소리 · 대본 · 자세 검증.
//
//   godot --headless --path . res://scenes/debug/PrologueAudioTest.tscn --quit-after 2000
//
// "무서운가 · 큰가" 는 기계가 못 잰다 — 그건 들어 봐야 한다. 여기서 재는 것은
// 들어 보기 전에 반드시 맞아야 하는 것들이다.
//   · 쓰는 음원이 실재하는가(없으면 Play 가 조용히 아무것도 안 한다)
//   · 변형 재생(Warp 버스)이 켜지고 **꺼지는가** — 이게 안 꺼져서 경보음 하나가
//     프롤로그가 끝난 뒤에도 끝없이 울렸다
//   · 지연 예약이 취소되는가(컷을 빨리 넘겼을 때 뒤늦게 터지지 않게)
//   · 대본의 컷 구성(유리 컷 클릭 대기 · 지상 두 컷 · 차폐 쿠웅)
//   · 달릴 때 상체가 **앞으로** 숙여지는가
public partial class PrologueAudioTest : Node
{
    private int _pass, _fail;

    public override void _Ready() => CallDeferred(nameof(Run));

    private async void Run()
    {
        GD.Print("\n\n################ 프롤로그 재난 소리 · 연출 검증 ################");
        TestAssets();
        TestWarpBus();
        await TestWarpPlayback();
        await TestDelayCancel();
        TestLoopChannel();
        TestScript();
        await TestRunLean();
        await TestDirectorTeardown();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── A. 음원이 실재하는가 ────────────────────────────────────────────
    private void TestAssets()
    {
        Head("A", "쓰는 음원이 전부 실재한다");
        foreach (string key in new[] { "glass_bomb", "disaster", "storm",
                                       "female_run_shouting", "male_run_shouting", "bulkhead_slam" })
            Check(Sfx.Instance?.Has(key) == true, $"재난음 '{key}'");

        // 사이렌은 2.5초 상승 · 1.5초 유지 · 1.0초 하강 = 한 주기 5초로 합성한다.
        var siren = ResourceLoader.Exists("res://assets/audio/sfx/siren.wav")
            ? GD.Load<AudioStream>("res://assets/audio/sfx/siren.wav") : null;
        double len = siren?.GetLength() ?? 0;
        Check(len > 4.9 && len < 5.1, $"사이렌 한 주기가 5초다(상승 2.5 + 유지 1.5 + 하강 1.0) — {len:0.00}s");

        // 차폐문 쿠웅은 금속 '쨍' 이 아니라 둔탁한 저음이어야 한다 — 길이로 거칠게 본다.
        var slam = ResourceLoader.Exists("res://assets/audio/sfx/bulkhead_slam.wav")
            ? GD.Load<AudioStream>("res://assets/audio/sfx/bulkhead_slam.wav") : null;
        double sl = slam?.GetLength() ?? 0;
        Check(sl > 2.0, $"쿠웅이 2초 이상 울린다(금속 타격음은 1.4초) — {sl:0.00}s");
    }

    // ── B. Warp 버스 ───────────────────────────────────────────────────
    private void TestWarpBus()
    {
        Head("B", "변형 버스가 SFX 아래에 효과 셋으로 선다");
        GameSettings.EnsureWarpBus();
        int idx = AudioServer.GetBusIndex(GameSettings.BusWarp);
        Check(idx >= 0, "Warp 버스가 있다");
        if (idx < 0) return;
        Check(AudioServer.GetBusSend(idx) == GameSettings.BusSfx, "효과음 볼륨 설정을 따른다(SFX 로 보낸다)");
        int n = AudioServer.GetBusEffectCount(idx);
        Check(n == 3, $"효과가 셋이다(저역통과 · 페이저 · 잔향) — {n}개");
        Check(AudioServer.GetBusEffect(idx, 0) is AudioEffectLowPassFilter, "① 저역통과 — 멀고 둔탁하게");
        Check(AudioServer.GetBusEffect(idx, 1) is AudioEffectPhaser, "② 페이저 — 느린 울렁임");
        Check(AudioServer.GetBusEffect(idx, 2) is AudioEffectReverb, "③ 잔향 — 거대한 지하 공간");
        // 두 번 불러도 효과가 쌓이지 않는다.
        GameSettings.EnsureWarpBus();
        Check(AudioServer.GetBusEffectCount(idx) == 3, "두 번 불러도 효과가 늘지 않는다");
    }

    // ── C. 변형 재생이 켜지고 꺼진다 ────────────────────────────────────
    private async System.Threading.Tasks.Task TestWarpPlayback()
    {
        Head("C", "변형 재생이 채널마다 켜지고 꺼진다");
        var sfx = Sfx.Instance;
        if (sfx == null) { Check(false, "Sfx 오토로드가 있다"); return; }

        // 같은 파일을 두 채널에 겹친다 — 유리가 깨지는 소리를 이렇게 쌓는다.
        sfx.PlayWarped("glass_bomb", -4f, 0.92f, "glass_crack");
        sfx.PlayWarped("glass_bomb", -2f, 0.46f, "glass_low");
        await Frames(2);
        Check(sfx.WarpedPlaying("glass_crack"), "① 깨지는 순간 채널이 돈다");
        Check(sfx.WarpedPlaying("glass_low"), "② 끌리는 저음 채널이 **따로** 돈다");

        sfx.StopWarped("glass_crack");
        await Frames(2);
        Check(!sfx.WarpedPlaying("glass_crack"), "한 채널만 끄면 그 채널만 멈춘다");
        Check(sfx.WarpedPlaying("glass_low"), "다른 채널은 계속 돈다");

        // 폭풍우 — 반복 재생. 파일(10.5초)보다 오래 깔려야 한다.
        sfx.PlayWarped("storm", -17f, 0.82f, "storm", loop: true);
        await Frames(2);
        Check(sfx.WarpedPlaying("storm"), "폭풍우가 반복으로 돈다");

        // 원본 캐시를 오염시키지 않는가 — 복제본에만 loop 를 건다.
        var raw = GD.Load<AudioStreamMP3>("res://assets/audio/sfx/storm.mp3");
        Check(raw is { Loop: false }, "원본 파일의 loop 는 건드리지 않는다(원샷 재생이 멈추도록)");

        sfx.StopAllWarped();
        await Frames(2);
        Check(!sfx.WarpedPlaying("storm") && !sfx.WarpedPlaying("glass_low"),
            "StopAllWarped 가 **전부** 멈춘다 — 프롤로그 소리가 게임까지 따라오지 않는다");
    }

    // ── D. 지연 예약이 취소된다 ─────────────────────────────────────────
    private async System.Threading.Tasks.Task TestDelayCancel()
    {
        Head("D", "늦게 깔리는 겹은 컷이 끝나면 취소된다");
        var sfx = Sfx.Instance;
        if (sfx == null) return;

        sfx.PlayWarped("glass_bomb", -8f, 0.30f, "glass_tail", delaySeconds: 0.25f);
        sfx.StopAllWarped();                 // 플레이어가 컷을 바로 넘긴 상황
        await Seconds(0.45);
        Check(!sfx.WarpedPlaying("glass_tail"), "취소된 뒤에는 울리지 않는다");

        // 채널 하나만 꺼도 **그 채널의** 예약은 취소된다 — 컷이 바뀔 때 연출기가
        // 채널별로 끄기 때문에, 이게 안 되면 다음 컷에서 꼬리가 뒤늦게 터진다.
        sfx.PlayWarped("glass_bomb", -8f, 0.30f, "glass_tail", delaySeconds: 0.25f);
        sfx.StopWarped("glass_tail", 0.1f);
        await Seconds(0.45);
        Check(!sfx.WarpedPlaying("glass_tail"), "채널 하나만 꺼도 그 채널 예약이 취소된다");

        sfx.PlayWarped("glass_bomb", -8f, 0.30f, "glass_tail", delaySeconds: 0.12f);
        await Seconds(0.3);
        Check(sfx.WarpedPlaying("glass_tail"), "취소하지 않으면 예약대로 울린다");
        sfx.StopAllWarped();
    }

    // ── E. 이름 있는 루프 채널 ──────────────────────────────────────────
    private void TestLoopChannel()
    {
        Head("E", "루프 채널이 켜지고 꺼진다(경보음 누락 사고의 토대)");
        var sfx = Sfx.Instance;
        if (sfx == null) return;
        sfx.StopLoop("alarm");
        Check(!sfx.IsLooping("alarm"), "시작 전에는 돌지 않는다");
        sfx.Loop("alarm", -40f);
        Check(sfx.IsLooping("alarm"), "켜면 돈다");
        sfx.Loop("alarm", -40f);
        Check(sfx.IsLooping("alarm"), "두 번 켜도 하나다");
        sfx.StopLoop("alarm");
        Check(!sfx.IsLooping("alarm"), "끄면 멈춘다");
    }

    // ── F. 대본의 컷 구성 ───────────────────────────────────────────────
    private void TestScript()
    {
        Head("F", "대재난 대본의 컷 구성");
        var cut = PrologueScript.GetCutscene("prologue_disaster");
        Check(cut != null && cut.Slides.Count > 0, "@cutscene prologue_disaster 가 있다");
        if (cut == null) return;

        // 모르는 scene3d id 는 조용히 정지 그림으로 떨어진다 — 오타를 여기서 잡는다.
        foreach (var s in cut.Slides.Where(s => !string.IsNullOrEmpty(s.Scene3D)))
            Check(Prologue3DDirector.Has(s.Scene3D), $"scene3d '{s.Scene3D}' 를 연출기가 안다");

        // 연구실 : 흔들림 컷 → 유리 컷. 둘 다 클릭을 기다린다.
        int lab = cut.Slides.FindIndex(s => s.Scene3D == "disaster_lab");
        int glass = cut.Slides.FindIndex(s => s.Scene3D == "disaster_lab_glass");
        Check(lab >= 0 && glass == lab + 1, "연구실이 두 컷으로 나뉘어 이어진다");
        if (lab >= 0) Check(cut.Slides[lab].NoSkip > 0f, "① 흔들림 컷은 다 보고 눌러야 넘어간다");
        if (glass >= 0) Check(cut.Slides[glass].NoSkip > 0f, "② 유리 깨지는 컷도 다 보고 눌러야 넘어간다");

        // 지상은 두 컷이다(세 컷이면 늘어진다).
        int surface = cut.Slides.Count(s => s.Scene3D.StartsWith("surface"));
        Check(surface == 2, $"지상 컷이 둘이다 — {surface}개");

        // 비상 차폐 창이 뜨는 컷에서 쿠웅이 울린다.
        var seal = cut.Slides.FirstOrDefault(s => s.WinTitle.Contains("비상 차폐"));
        Check(seal != null && seal.Sfx == "bulkhead_slam", $"차폐 컷의 효과음이 쿠웅이다 — '{seal?.Sfx}'");
        Check(seal != null && seal.SfxLoopStop == "siren", "그 컷에서 사이렌이 멈춘다");
    }

    // ── G. 달릴 때의 상체 ───────────────────────────────────────────────
    //
    // 리그에서 몸통은 +Y 로 올라가는 뼈라 **-X 가 앞으로 숙이는 쪽**이다. 부호가
    // 뒤집혀 있어서 전원이 상체를 뒤로 젖히고 달렸다 — 각도를 직접 읽어 본다.
    private async System.Threading.Tasks.Task TestRunLean()
    {
        Head("G", "달릴 때 상체가 앞으로 숙여진다");
        var actor = new CutsceneActor();
        AddChild(actor);
        actor.Spawn("rabbit", Vector3.Zero);
        var torso = actor.FindChild("Torso", true, false) as Node3D;
        var neck = actor.FindChild("Neck", true, false) as Node3D;
        if (torso == null) { Check(false, "리그의 Torso 를 찾았다"); return; }

        actor.SetRunStyle(CutsceneActor.RunStyle.HeadDown, 0.3f);
        actor.MoveTo(new Vector3(0f, 0f, -10f), 5f, CutsceneActor.Gait.Run);
        await Frames(4);
        float runX = torso.RotationDegrees.X;
        Check(runX < -10f, $"전력질주 — 상체가 앞으로 깊게 숙여진다 ({runX:0.0}°)");
        Check(runX > -55f, $"그래도 접히지는 않는다 ({runX:0.0}°)");
        if (neck != null)
            Check(neck.RotationDegrees.X > 0f,
                $"목은 반대로 들어 앞을 본다 ({neck.RotationDegrees.X:0.0}°)");

        actor.SetGait(CutsceneActor.Gait.Walk);
        await Frames(4);
        float walkX = torso.RotationDegrees.X;
        Check(walkX < 0f, $"걸을 때도 아주 조금 앞으로 기운다 ({walkX:0.0}°)");
        Check(walkX > runX, "달릴 때가 걸을 때보다 더 숙여진다");
        actor.QueueFree();
    }

    // ── H. 컷씬이 끝나면 소리가 하나도 남지 않는다 ──────────────────────
    //
    // 이 검사가 잡는 사고 : 마지막 장면(director_last)이 켠 경보 루프가 프롤로그가
    // 끝난 뒤에도 계속 울렸다. 장면이 켠 소리는 **연출기가 책임지고 끈다**.
    private async System.Threading.Tasks.Task TestDirectorTeardown()
    {
        Head("H", "컷씬이 끝나면 연출기가 켠 소리가 남지 않는다");
        var sfx = Sfx.Instance;
        if (sfx == null) { Check(false, "Sfx 오토로드가 있다"); return; }

        var dir = new Prologue3DDirector();
        AddChild(dir);

        // ① 대재난이 시작되는 장면 — 폭풍우가 깔린다.
        dir.Begin("core_warning");
        await Seconds(0.4);
        Check(sfx.WarpedPlaying("storm"), "대재난 시작 장면이 폭풍우를 깐다");

        // ② 마지막 장면 — 브리핑실 경보.
        dir.Begin("director_last");
        await Seconds(1.0);
        Check(sfx.IsLooping("alarm"), "마지막 장면이 경보를 켠다");
        Check(sfx.WarpedPlaying("storm"), "폭풍우는 컷이 바뀌어도 이어진다");

        // ③ 컷씬 종료.
        dir.End();
        await Seconds(0.8);
        Check(!sfx.IsLooping("alarm"), "End() 가 경보를 끈다 — 프롤로그 뒤 무한 재생 사고");
        Check(!sfx.WarpedPlaying("storm"), "End() 가 폭풍우도 끈다");
        foreach (string key in new[] { "siren", "machinery_loop", "electric_crackle_loop",
                                       "vent_loop", "drone_loop" })
            Check(!sfx.IsLooping(key), $"루프 '{key}' 가 남지 않았다");
        dir.QueueFree();
    }

    // ── 공용 ────────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async System.Threading.Tasks.Task Seconds(double s) =>
        await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private static void Head(string tag, string title) => GD.Print($"\n── {tag}. {title} ──");

    private void Check(bool ok, string label)
    {
        if (ok) { _pass++; GD.Print($"  PASS  {label}"); }
        else { _fail++; GD.Print($"  FAIL  {label}"); }
    }
}
