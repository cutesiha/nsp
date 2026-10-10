using System;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Ui;

namespace NSP.View;

// DAY 메인 스토리의 앞뒤에 붙는 전환 연출(지시서 PART A).
//
// 고치려는 것 하나다 — 일반 게임 화면에서 갑자기 모니터2 스토리가 시작되면
// "스토리 모드가 갑자기 켜진 느낌" 이 난다. 그래서 그 사이에 **관리자가 잠시 눈을 감고
// 숨을 돌리는 시간**을 넣는다. 스토리 구조(모니터2 → 휴게실 3D → 스탠딩)는 그대로다.
//
//   이전 BGM 페이드아웃 → 관리자 날숨 → 가장자리부터 암전 → 암전 중에 휴게실 생활음
//   → 모니터2 에 휴게실 화면 → 눈을 뜬다 → breakroom 페이드인 → 카메라 확대
//   → 0.4~0.7초 조용히 → 첫 대사
//
// **반드시 되돌아온다.** 예외 · 씬 전환 · 강제 종료 어느 쪽으로 빠져나가도 Release() 를
// 지나간다(§10). 암전이 남으면 그 뒤로 게임이 보이지 않고, 카메라 확대가 남으면
// 아무것도 조작할 수 없다.
public static class StoryTransition
{
    // 전환 연출이 돌고 있는가. 공포 ambience 가 이 값을 보고 입을 닫는다(§20).
    public static bool Active { get; private set; }

    // 스토리 BGM. assets/audio/bgm/breakroom.mp3
    public const string StoryMusic = "breakroom";
    // 그 BGM 의 음량. 메인 BGM(Sfx.MusicDefaultDb)보다 조금만 낮게 둔다 —
    // 대사를 덮으면 안 되지만, 너무 깔리면 휴게실로 넘어온 느낌이 나지 않는다.
    private const float StoryMusicDb = -6.5f;
    // 멀리서 들리는 사람들의 웅성거림. 말이 들리면 안 되고 "사람이 있다" 만 들려야 한다.
    private const float MurmurDb = -24f;

    // ── 시간 상수(§2 · §5 · §6) ──────────────────────────────────────
    private const double CloseSeconds = 0.42;   // 눈 감기 0.35~0.5
    private const double DarkSeconds = 0.58;    // 완전 암전 0.4~0.7
    private const double OpenSeconds = 0.42;    // 눈 뜨기 0.35~0.5
    private const double QuietSeconds = 0.55;   // 휴게실이 뜬 뒤의 침묵 0.4~0.7
    private const float BgmFadeSeconds = 1.1f;  // BGM 페이드 0.7~1.5
    private const double ExitHoldSeconds = 0.34; // 카메라가 빠지기 전 휴게실만 남는 시간(§8)

    // 스토리에 들어오기 전에 울리던 곡. 끝나면 이 곡으로 돌아간다(§5).
    private static string _previousMusic = "";
    private static AudioStreamPlayer _murmur;

    // ── 진입 ──────────────────────────────────────────────────────────

    // 스토리 앞 전환. 돌아올 때 화면은 이미 모니터2 확대 상태이고 첫 대사만 기다린다.
    //
    // 돌려주는 값 = **이 호출이 전환을 맡았는가**. false 면 이미 다른 전환이 돌고 있다는
    // 뜻이고, 그때는 Exit 도 부르면 안 된다 — 남의 스토리를 대신 끝내 버리게 된다.
    public static async Task<bool> Enter(Node ctx)
    {
        if (ctx == null || !GodotObject.IsInstanceValid(ctx)) return false;
        // 전환이 이미 돌고 있으면 두 번째는 들어오지 않는다.
        // 개발 허브처럼 배치 화면에 다시 들어가는 길이 있으면 두 개가 겹쳐서,
        // 눈이 감겼다 떴다를 반복하고 두 BGM 이 서로를 밀어낸다.
        if (Active)
        {
            GD.PushWarning("StoryTransition: 이미 전환이 돌고 있어 두 번째 요청을 건너뜁니다.");
            return false;
        }
        Active = true;
        try
        {
            var sfx = Sfx.Instance;
            _previousMusic = sfx?.CurrentMusic ?? "";

            // ① 이전 BGM 페이드아웃. 기다리지 않는다 — 날숨과 암전이 그 위에서 흐른다.
            sfx?.FadeOutMusic(BgmFadeSeconds);

            // ② 관리자의 피곤한 날숨 하나. DAY 가 올라갈수록 아주 조금만 또렷해진다(§3).
            //    과호흡으로 만들지 않는다 — 공포 효과가 아니라 "관리자도 여기 있다" 다.
            int day = GameState.Instance?.CurrentDay ?? 1;
            sfx?.Play("admin_exhale", ExhaleDb(day), ExhalePitch(day));
            await Wait(ctx, 0.5);

            // ③ 가장자리부터 어두워져 중앙까지(§2).
            var blink = BlinkOverlay.Instance;
            if (blink != null && GodotObject.IsInstanceValid(blink)) await blink.Close(CloseSeconds);
            else await Wait(ctx, CloseSeconds);

            // ④ 암전 중에 **소리가 먼저** 휴게실을 소개한다(§4).
            //    "사람들이 먼저 자리를 잡고 있었다" 만 남기면 된다 — 전부 아주 작게.
            StartMurmur(ctx);
            PlayRoomLife(ctx);
            await Wait(ctx, DarkSeconds);

            // ⑤ 눈을 뜨기 전에 모니터2 화면을 휴게실로 바꿔 둔다 — 뜨는 순간 이미 거기
            //    있어야 한다. 카메라 확대는 아직 하지 않는다(§1 의 순서).
            StoryCutinDirector.Instance?.EnterMonitor(focusNow: false);

            // ⑥ 눈을 뜬다.
            if (blink != null && GodotObject.IsInstanceValid(blink)) await blink.Open(OpenSeconds);
            else await Wait(ctx, OpenSeconds);

            // ⑦ 스토리 BGM 페이드인. 이전 곡은 이미 빠졌으므로 겹쳐 울리지 않는다(§5).
            sfx?.CrossfadeMusic(StoryMusic, BgmFadeSeconds, loop: true, 0f, StoryMusicDb, restartIfSame: true);

            // ⑧ 카메라가 모니터2 로 들어간다.
            StoryCutinDirector.Instance?.FocusStoryMonitor();
            await Wait(ctx, 0.36);

            // ⑨ 바로 말하지 않는다 — 앉아 있는 모습만 잠깐 보여 준다(§6).
            await Wait(ctx, QuietSeconds);
        }
        catch (Exception e)
        {
            GD.PushWarning($"StoryTransition: 진입 연출을 건너뜁니다 — {e.Message}");
            Release();
        }
        finally
        {
            Active = false;
        }
        return true;
    }

    // ── 종료 ──────────────────────────────────────────────────────────

    // 스토리 뒤 전환. 마지막 대사가 끝난 직후에 부른다(§8).
    // 끝나면 중앙제어실 시점이고, Shift Card 까지 보여 준 상태다.
    public static async Task Exit(Node ctx)
    {
        if (ctx == null || !GodotObject.IsInstanceValid(ctx)) { Release(); return; }
        Active = true;
        try
        {
            var sfx = Sfx.Instance;

            // ① 스토리 BGM 과 생활음을 내린다.
            sfx?.FadeOutMusic(BgmFadeSeconds);
            StopMurmur(BgmFadeSeconds);

            // ② 카메라가 빠지기 전에 휴게실만 잠깐 남는다(§8).
            await Wait(ctx, ExitHoldSeconds);

            // ③ 모니터2 확대를 풀고 중앙제어실 시점으로.
            StoryCutinDirector.Instance?.ExitMonitor();
            await Wait(ctx, 0.45);

            // ④ Shift Card — "지금은 시설을 복구해야 한다"(§9).
            var card = ShiftCardView.Instance;
            if (card != null && GodotObject.IsInstanceValid(card)) await card.Present();

            // ⑤ 그 날 상태에 맞는 기존 BGM 으로 돌아간다(§5).
            //    배치 화면이면 rest_time 이다. 들어올 때 울리던 곡을 그대로 쓴다.
            //
            // **근무가 이미 시작됐으면 되돌리지 않는다.** 스토리가 흐르는 동안 플레이어가
            // 근무를 시작할 수 있는데, 그때 여기서 곡을 다시 틀면 "근무 화면엔 BGM 없음"
            // (EnterShift 가 일부러 끈 것)이 깨져 실시간 근무 내내 시작 BGM 이 깔린다.
            RestoreMusic(sfx);
        }
        catch (Exception e)
        {
            GD.PushWarning($"StoryTransition: 종료 연출을 건너뜁니다 — {e.Message}");
        }
        finally
        {
            Release();
            Active = false;
        }
    }

    // ── fail-safe (§10) ──────────────────────────────────────────────
    //
    // 어떤 경로로 빠져나와도 이 함수만 지나가면 플레이 가능한 상태로 돌아온다.
    // 연출을 기다리지 않는다 — 기다리는 동안 또 터질 수 있다.
    public static void Release()
    {
        BlinkOverlay.Instance?.OpenNow();
        ShiftCardView.Instance?.HideNow();
        StopMurmurNow();
        Sfx.Instance?.ClearDuck();
        Sfx.Instance?.StopHorror();
        StoryCutinDirector.Instance?.ExitMonitor();

        // 스토리 BGM 이 남아 있으면 되돌린다. 아무 곡도 몰랐다면 그냥 끈다.
        var sfx = Sfx.Instance;
        if (sfx != null && sfx.CurrentMusic == StoryMusic) RestoreMusic(sfx, 0.5f);
        _previousMusic = "";
        Active = false;
    }

    // 스토리가 끝난 뒤 음악을 어떻게 되돌릴 것인가.
    //
    //   실시간 근무 중  → 켜지 않는다. 그 화면의 소리는 시설 환경음이다(§5).
    //   그 밖(배치 · 휴게) → 들어올 때 울리던 곡으로 되돌린다.
    private static void RestoreMusic(Sfx sfx, float fade = BgmFadeSeconds)
    {
        if (sfx == null) return;
        if (GameState.Instance?.CurrentPhase == NSP.Data.GamePhase.Live)
        {
            sfx.FadeOutMusic(fade);
            _previousMusic = "";
            return;
        }
        string back = _previousMusic.Length > 0 ? _previousMusic : "rest_time";
        sfx.CrossfadeMusic(back, fade, loop: true);
        _previousMusic = "";
    }

    // ── 휴게실 생활음 (§4) ───────────────────────────────────────────

    // 사람 몇이 멀리서 웅성거리는 루프. 암전 중에 먼저 깔리고 스토리가 끝날 때까지 남는다.
    private static void StartMurmur(Node ctx)
    {
        if (_murmur != null && GodotObject.IsInstanceValid(_murmur)) return;
        string path = "res://assets/audio/sfx/breakroom_murmur.wav";
        if (!ResourceLoader.Exists(path)) return;
        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;

        _murmur = new AudioStreamPlayer { Stream = stream, VolumeDb = -46f, Bus = GameSettings.BusSfx };
        ctx.GetTree().Root.AddChild(_murmur);
        _murmur.Play();
        // 웅성거림은 끝까지 작다 — 말은 들리지 않고 사람 기척만 남는 선(MurmurDb).
        var tw = _murmur.CreateTween();
        tw.TweenProperty(_murmur, "volume_db", MurmurDb, 0.9).SetTrans(Tween.TransitionType.Sine);
    }

    private static void StopMurmur(float fade)
    {
        if (_murmur == null || !GodotObject.IsInstanceValid(_murmur)) { _murmur = null; return; }
        var p = _murmur;
        _murmur = null;
        var tw = p.CreateTween();
        tw.TweenProperty(p, "volume_db", -60f, Mathf.Max(0.05f, fade)).SetTrans(Tween.TransitionType.Sine);
        tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(p)) p.QueueFree(); }));
    }

    private static void StopMurmurNow()
    {
        if (_murmur != null && GodotObject.IsInstanceValid(_murmur)) _murmur.QueueFree();
        _murmur = null;
    }

    // 자리 잡는 소리 몇 개. 전부 작고, 시각이 서로 겹치지 않는다.
    // 크게 울리면 "사고가 났나?" 가 되어 버린다 — 생활음은 정보가 아니다.
    private static void PlayRoomLife(Node ctx)
    {
        var sfx = Sfx.Instance;
        if (sfx == null) return;
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        // 의자를 끌고 앉는다 — 이 한 쌍은 순서가 정해져 있어야 사람 동작으로 읽힌다.
        sfx.Play("chair_pull", -22f, rng.RandfRange(0.94f, 1.06f));
        Delay(ctx, rng.RandfRange(0.42f, 0.62f), () => Sfx.Instance?.Play("chair_creak", -24f));
        // 컵 · 옷 스침은 들어갈 수도, 안 들어갈 수도 있다 — 매번 같은 소리면 연출로 들린다.
        if (rng.Randf() < 0.75f)
            Delay(ctx, rng.RandfRange(0.25f, 0.9f), () => Sfx.Instance?.Play("cup_set", -25f));
        if (rng.Randf() < 0.6f)
            Delay(ctx, rng.RandfRange(0.1f, 0.7f), () => Sfx.Instance?.Play("cloth_rustle", -28f));
    }

    // ── 관리자 날숨 (§3) ─────────────────────────────────────────────

    // DAY 가 올라가면 아주 약하게 또렷해진다. DAY1 은 거의 정상.
    private static float ExhaleDb(int day) => -24f + Mathf.Clamp(day - 1, 0, 4) * 1.6f;

    // 피로는 숨이 조금 느려지는 쪽으로만 표현한다 — 빨라지면 과호흡이 된다.
    private static float ExhalePitch(int day) => 1f - Mathf.Clamp(day - 1, 0, 4) * 0.018f;

    // ── 대기 도구 ─────────────────────────────────────────────────────

    private static async Task Wait(Node ctx, double seconds)
    {
        if (ctx == null || !GodotObject.IsInstanceValid(ctx)) return;
        var tree = ctx.GetTree();
        if (tree == null) return;
        await ctx.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    private static void Delay(Node ctx, double seconds, Action action)
    {
        var tree = ctx?.GetTree();
        if (tree == null) { action(); return; }
        var t = tree.CreateTimer(seconds);
        t.Timeout += () => action();
    }
}
