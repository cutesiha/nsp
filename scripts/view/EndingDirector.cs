using System.Linq;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Facility;
using NSP.Prologue;

namespace NSP.View;

// DAY5 FINAL SHIFT REPORT 의 [계속] 이후 — 엔딩 연출 전체.
//
// 엔딩은 코어 100% 하나로만 갈린다. 두 엔딩은 따로 된 컷신이 아니라 같은 길의 끝만 다르다:
//
//   공통 : 암전 → 불 꺼진 중앙관리실 → CRT 노이즈 → 두 모니터 동시 재부팅
//          → 왼쪽 = FINAL RECOVERY SEQUENCE 진행 막대 / 오른쪽 = 점검 콘솔
//   진엔딩(100%)   : 막대가 100.0% 까지 차고 "복구 완료"
//                    → 오른쪽 콘솔에 SYSTEM STATUS 가 한 줄씩 찍힌다
//                    → 왼쪽에 GUIDE-0(smile) 얼굴창 → 오른쪽에 "야간 근무가 종료되었습니다 /
//                       관리자님, 수고하셨습니다" 가 찍히는 동안 GUIDE-0 이 말한다
//                    → 의자에 기대며 눈을 감음 → TRUE END — 근무 종료
//   배드엔딩(미달) : 막대가 멈춤 → FINAL RECOVERY FAILED · 조명 한 번 꺼졌다 켜짐 · 경고음
//                    → 두 모니터가 시뻘개지고 오른쪽에 SYSTEM WARNING
//                    → 노이즈가 심해지며 왼쪽에 GUIDE-0(sneer) 얼굴창
//                    → "최종 복구에 실패했습니다 / 야간 근무 기록을 종료합니다"
//                    → 팡! 붉은 섬광 · 흔들림(프롤로그의 피격 그대로) → BAD END — 복구 실패
//   → 5일간의 근무 기록(성적표) → [타이틀로] → 변한 시작 화면에서 다시 눈을 뜬다.
//
// 생존자 · 방해자 격리 · 업무 점수로는 엔딩을 나누지 않는다 — 그건 성적표에만 적는다.
// 판정 로직은 없다. GameState 의 숫자를 읽어 보여 주기만 한다.
public partial class EndingDirector : Node
{
    // 엔딩이 도는 동안(기록 화면 포함) — ESC 일시정지 메뉴를 막는다(메뉴가 이 암전 레이어 밑에 깔려 멈춘 것처럼 보인다).
    public static bool IsPlaying { get; private set; }

    private ControlRoom3DController _ctl;
    private TitleOverlay _title;
    private SpotLight3D _m1, _m2, _fill;
    private OmniLight3D _emergency;
    private float _m1E = 1.6f, _m2E = 1.6f, _fillE = 0.2f;
    private Color _m1C, _m2C, _fillC;

    private CanvasLayer _layer;
    private ColorRect _red;
    private ColorRect _black;
    private Label _banner;
    private DizzyOverlay _dizzy;

    private bool _flicker;
    private float _flickerT;
    private readonly RandomNumberGenerator _rng = new();

    // GUIDE-0 이 말하는 중인가 — 화면에 글자가 찍힐 때마다 입과 보이스가 그 신호를 받는다.
    private bool _guideTalking;
    private int _keyCount;

    public void Play(ControlRoom3DController ctl, TitleOverlay title)
    {
        _ctl = ctl;
        _title = title;
        _ = RunAsync();
    }

    public override void _Ready()
    {
        IsPlaying = true;
        // 엔딩 동안 GUIDE-0 의 입은 이 연출기가 직접 돌린다(홀로그램 화면은 떠 있지 않다).
        GuideMouthAnimator.ExternallyDriven = true;
        _rng.Randomize();
        _layer = new CanvasLayer { Layer = 130 };   // 통화창(114) · 경보(122) 위
        AddChild(_layer);

        _red = new ColorRect { Color = new Color(0.85f, 0.05f, 0.03f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _red.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_red);

        _black = new ColorRect { Color = new Color(0f, 0f, 0f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        _black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_black);

        _banner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        _banner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _banner.AddThemeFontOverride("font", ViewFont.Default);
        _banner.AddThemeFontSizeOverride("font_size", ViewFont.FS(40));
        _layer.AddChild(_banner);

        _dizzy = new DizzyOverlay();
        AddChild(_dizzy);
    }

    public override void _Process(double delta)
    {
        // GUIDE-0 입모양 — 평소에는 GuideHologramView 가 돌리지만 엔딩에는 그 화면이 없다.
        // 말하는 동안은 매 프레임 다시 그린다(얼굴창이 스스로 다시 그리는 간격은 입모양보다 느리다).
        if (GuideMouthAnimator.Talking)
        {
            GuideMouthAnimator.Tick(delta);
            GuideCornerFace.NotifyMouthChangedAll();
        }

        if (!_flicker) return;
        // 배드엔딩 — 경고음이 커지는 동안 조명이 불규칙하게 깜빡인다.
        _flickerT -= (float)delta;
        if (_flickerT > 0f) return;
        _flickerT = _rng.RandfRange(0.05f, 0.22f);
        float k = _rng.Randf() < 0.35f ? 0.05f : _rng.RandfRange(0.5f, 1.2f);
        if (_m1 != null) _m1.LightEnergy = _m1E * k;
        if (_m2 != null) _m2.LightEnergy = _m2E * k;
        if (_emergency != null) _emergency.LightEnergy = _rng.RandfRange(1.2f, 3.2f);
    }

    public override void _ExitTree()
    {
        IsPlaying = false;
        ControlRoom3DHorror.ExternalLightingOverride = false;
        GuideCornerFace.ShowAll(false);
        GuideCornerFace.SetEndingWindow(false);
        GuideCornerFace.SetAlarmTint(false);
        GuideMouthAnimator.Reset();
        GuideMouthAnimator.ExternallyDriven = false;
        foreach (var k in new[] { "machinery_loop", "drone_loop", "alarm" }) Sfx.Instance?.StopLoop(k);
    }

    // ────────────────────────────────────────────────────────────────────

    private async Task RunAsync()
    {
        FindLights();
        var gs = GameState.Instance;
        float core = Mathf.Min(gs?.CoreProgress ?? 0f, 100f);
        bool success = core >= 100f - 0.001f;

        ControlRoom3DHorror.ExternalLightingOverride = true;
        gs?.SetPhase(NSP.Data.GamePhase.Result);
        _ctl?.SetInputLocked(true);
        _ctl?.ClearFocus(0.2f);
        Sfx.Instance?.FadeOutMusic(0.8f);

        // ── 공통 1 : 불 꺼짐 ─────────────────────────────────────────────
        await Fade(_black, 1f, 0.6f);
        Sfx.Instance?.Play("power_down", -6f);
        SetLights(0f, 0f);
        _ctl?.SetScreenBrightness(0f);
        // 이 순간부터 계속 울리는 시설의 저음 — 진엔딩에서는 복구 완료와 함께 딱 끊긴다.
        Sfx.Instance?.Loop("machinery_loop", -20f);
        Sfx.Instance?.Loop("drone_loop", -24f);
        await Wait(1.0);

        // ── 공통 2 : 불 꺼진 중앙관리실 ───────────────────────────────────
        SetLights(0.06f, 0.05f);
        await Fade(_black, 0f, 1.1f);
        await Wait(0.9);

        // ── 공통 3 : CRT 노이즈 → 두 모니터 동시에 재부팅 ─────────────────────
        var left = EndingMonitorView.Left;
        var right = EndingMonitorView.Right;
        left?.Clear();
        right?.Clear();
        HookTyping(left);
        HookTyping(right);
        _ctl?.SetLeftScreen(_ctl.EndingLeftViewport);
        _ctl?.SetRightScreen(_ctl.EndingRightViewport);
        _ctl?.SetScreenNoise(0.55f);
        Sfx.Instance?.Play("relay_click", -5f);
        Sfx.Instance?.Play("crt_on", -5f);
        _ctl?.SetScreenBrightness(0.35f);
        await Wait(0.08);
        _ctl?.SetScreenBrightness(0.04f);
        await Wait(0.16);
        Sfx.Instance?.Play("crt_on", -9f);
        var boot = CreateTween().SetParallel(true);
        boot.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenBrightness(v)), 0.04f, 1f, 0.7);
        boot.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenNoise(v)), 0.55f, 0.04f, 1.4);
        boot.TweenMethod(Callable.From<float>(v => SetLights(v, v * 0.6f)), 0.06f, 0.85f, 1.0);
        await Wait(1.4);

        // 왼쪽 막대가 먼저 돈다. 오른쪽 콘솔은 그 결과가 나온 뒤에 딱 한 번 찍힌다
        // (먼저 점검 목록을 띄웠다가 다시 지우면 같은 콘솔이 두 번 뜬 것처럼 보인다).
        left?.Clear("CONTAINMENT CORE", "FINAL RECOVERY SEQUENCE");
        left?.SetBar(0f);
        right?.Clear();
        await Wait(0.6);

        if (success) await TrueEnd(left, right);
        else await BadEnd(left, right, core);

        await ShowRecord(success);
    }

    // ── 진엔딩 ───────────────────────────────────────────────────────────
    private async Task TrueEnd(EndingMonitorView left, EndingMonitorView right)
    {
        // ① 왼쪽 — 막대가 100.0% 까지 차오르고 "복구 완료".
        foreach (float v in new[] { 97.4f, 98.8f, 99.6f, 100f })
        {
            left?.SetBar(v);
            Sfx.Instance?.Play("gauge_tick", -10f);
            await Wait(v >= 100f ? 0.2 : 0.75);
        }
        await Wait(0.9);   // 잠깐 정적
        left?.Push("");
        left?.Push("복구 완료", EndingMonitorView.Tone.Good, 30);

        // 그 순간까지 울리던 기계음이 딱 끊기고, 관리실 조명이 평소보다 밝아진다.
        Sfx.Instance?.StopLoop("machinery_loop");
        Sfx.Instance?.StopLoop("drone_loop");
        Sfx.Instance?.Play("task_done", -8f);
        _ctl?.SetScreenNoise(0.012f);
        var up = CreateTween().SetParallel(true);
        up.TweenMethod(Callable.From<float>(v => SetLights(v, v)), 0.85f, 1.45f, 0.9);
        if (_emergency != null) { _emergency.LightEnergy = 0f; _emergency.Visible = false; }
        await AwaitTyping(left, 1.0);

        // ② 오른쪽 — 점검 결과가 한 줄씩 찍힌다.
        right?.Clear("SYSTEM STATUS");
        right?.Push("CORE ........ STABLE", EndingMonitorView.Tone.Good);
        right?.Push("POWER ....... NORMAL", EndingMonitorView.Tone.Good);
        right?.Push("CONTAINMENT . RESTORED", EndingMonitorView.Tone.Good);
        right?.Push("");
        right?.Push("5일간의 복구 작업이 완료되었습니다.", EndingMonitorView.Tone.Normal, 20);
        await AwaitTyping(right);

        // 경고등이 하나씩 꺼진다.
        for (int i = 0; i < 3; i++)
        {
            await Wait(0.45);
            Sfx.Instance?.Play("relay_click", -13f - i * 2f);
        }
        await Wait(1.6);   // 처음으로 완전한 정적

        // ③ 왼쪽 — GUIDE-0 이 웃는 얼굴로 뜬다.
        left?.Clear();
        _ctl?.SetScreenNoise(0.06f);
        Sfx.Instance?.Play("window_open", -10f);
        ShowGuide("smile");
        await Wait(0.25);
        _ctl?.SetScreenNoise(0.012f);
        await Wait(0.9);

        // ④ 오른쪽 — GUIDE-0 이 말하는 동안 글자가 찍히고 입이 움직인다.
        right?.Clear();
        await GuideSpeak(right, "야간 근무가 종료되었습니다.", "관리자님, 수고하셨습니다.");
        await Wait(2.2);

        // 조명이 조금 따뜻해지고, 긴장이 풀려 의자에 기대며 천천히 눈을 감는다(피격음 · 흔들림 없음).
        var warm = new Color(1f, 0.84f, 0.66f);
        var wt = CreateTween().SetParallel(true);
        if (_m1 != null) wt.TweenProperty(_m1, "light_color", _m1C.Lerp(warm, 0.55f), 2.2);
        if (_m2 != null) wt.TweenProperty(_m2, "light_color", _m2C.Lerp(warm, 0.55f), 2.2);
        if (_fill != null) wt.TweenProperty(_fill, "light_color", warm, 2.2);
        await Wait(1.2);
        _ctl?.LeanBackInChair(3.4f);
        await Wait(1.0);
        await Fade(_black, 1f, 2.8f);
        HideGuide();
        await Wait(0.8);
        await Banner("TRUE END — 근무 종료", new Color(0.72f, 0.96f, 0.88f));
        EndingState.Record(EndingState.Kind.True);
    }

    // ── 배드엔딩 ─────────────────────────────────────────────────────────
    private async Task BadEnd(EndingMonitorView left, EndingMonitorView right, float core)
    {
        // ① 숫자가 차오르다가 어느 순간 더 이상 오르지 않는다.
        float from = Mathf.Max(0f, core - 9f);
        const float climb = 2.4f;
        double t = 0;
        float lastTick = -1f;
        while (t < climb)
        {
            await NextFrame();
            t += GetProcessDeltaTime();
            float v = Mathf.Lerp(from, core, Mathf.Clamp((float)(t / climb), 0f, 1f));
            left?.SetBar(v);
            if (v - lastTick >= 1f) { lastTick = v; Sfx.Instance?.Play("gauge_tick", -12f); }
        }
        left?.SetBar(core);
        await Wait(1.8);   // 잠깐 멈춤 — 숫자가 안 올라간다

        left?.SetBar(core, failed: true);
        left?.Push("복구율 부족", EndingMonitorView.Tone.Bad, 24);
        left?.Push("REQUIRED ........ 100.0%", EndingMonitorView.Tone.Dim);
        left?.Push($"CURRENT ......... {core:0.0}%", EndingMonitorView.Tone.Bad);
        left?.Push("FINAL RECOVERY FAILED", EndingMonitorView.Tone.Bad, 26);
        await AwaitTyping(left, 0.5);

        // ② 조명이 한 번 꺼졌다 켜지고, 경고음.
        SetLights(0f, 0f);
        await Wait(0.16);
        SetLights(0.85f, 0.5f);
        await Wait(0.35);
        Sfx.Instance?.Play("alert_beep3", -3f);
        await Wait(0.7);

        // ③ 두 모니터가 한꺼번에 시뻘개진다.
        left?.SetAlarm(true);
        right?.SetAlarm(true);
        _ctl?.SetScreenTint(new Color(1f, 0.58f, 0.52f));
        if (_emergency != null) { _emergency.Visible = true; _emergency.LightColor = new Color(0.95f, 0.1f, 0.08f); _emergency.LightEnergy = 1.6f; }
        Sfx.Instance?.Play("relay_click", -6f);
        right?.Clear("SYSTEM WARNING");
        right?.Push("CORE ........ UNSTABLE", EndingMonitorView.Tone.Bad);
        right?.Push("CONTAINMENT . FAILED", EndingMonitorView.Tone.Bad);
        right?.Push("");
        right?.Push("복구 가능 시간을 초과했습니다.", EndingMonitorView.Tone.Normal, 20);
        await AwaitTyping(right, 1.4);

        // ④ CRT 와 시야의 노이즈가 한 단계 심해지고, 왼쪽에 GUIDE-0 이 비웃는 얼굴로 뜬다.
        Sfx.Instance?.Loop("alarm", -26f);
        var noiseUp = CreateTween().SetParallel(true);
        noiseUp.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenNoise(v)), 0.04f, 0.18f, 0.8);
        noiseUp.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenDistortion(v)), 0f, 0.10f, 0.8);
        NSP.Ui.AmbientOverlay.Instance?.PulseNoise(0.5f);
        await Wait(0.8);
        left?.Clear();
        Sfx.Instance?.Play("noise", -14f, 1.3f);
        GuideCornerFace.SetAlarmTint(true);
        ShowGuide("sneer");
        NSP.Ui.AmbientOverlay.Instance?.PulseNoise(0.4f);
        await Wait(1.1);

        // ⑤ 오른쪽 — GUIDE-0 이 말하는 동안 글자가 찍히고 입이 움직인다.
        right?.Clear();
        await GuideSpeak(right, "최종 복구에 실패했습니다.", "야간 근무 기록을 종료합니다.");
        await Wait(1.6);

        // ⑥ 경고음이 점점 커지면서 조명이 깜빡인다.
        _flicker = true;
        const float rise = 2.8f;
        t = 0;
        while (t < rise)
        {
            await NextFrame();
            t += GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / rise), 0f, 1f);
            Sfx.Instance?.SetLoopVolume("alarm", Mathf.Lerp(-26f, -3f, k));
            _ctl?.SetScreenNoise(Mathf.Lerp(0.18f, 0.5f, k * k));
            _ctl?.SetScreenDistortion(Mathf.Lerp(0.10f, 0.35f, k * k));
        }

        // ⑦ 팡! — 프롤로그에서 머리를 맞고 쓰러질 때와 같은 피격 · 흐림 · 먹먹한 소리.
        _flicker = false;
        HideGuide();
        Sfx.Instance?.Play("boom", -1f);
        Sfx.Instance?.Play("impact_blunt", -1f);
        _red.Color = _red.Color with { A = 0.85f };
        var flash = CreateTween();
        flash.TweenProperty(_red, "color:a", 0f, 0.55).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        _ctl?.ShakeCamera(7.5f, 0.5f);
        _dizzy?.SetAmount(1f);
        // 귀가 먹먹해진다 — 경보 · 기계음이 한순간에 멀어지고 이명만 남는다.
        foreach (var k in new[] { "alarm", "machinery_loop", "drone_loop" }) Sfx.Instance?.StopLoop(k);
        Sfx.Instance?.Play("tinnitus", -4f);
        SetLights(0.3f, 0.2f);
        await Wait(0.45);

        // 시야가 옆으로 기울며 책상 쪽으로 떨어진다 → 암전.
        Sfx.Instance?.Play("body_fall", -2f);
        _ctl?.CollapseCameraOntoDesk(0.34f);
        await Fade(_black, 1f, 0.55f);
        _dizzy?.Clear();
        _ctl?.SetScreenDistortion(0f);
        await Wait(1.6);
        await Banner("BAD END — 복구 실패", new Color(1f, 0.45f, 0.38f));
        EndingState.Record(EndingState.Kind.Bad);
    }

    // ── 5일간의 근무 기록 → [타이틀로] ──────────────────────────────────────
    private Task ShowRecord(bool success)
    {
        var tcs = new TaskCompletionSource();
        var rec = new EndingRecordOverlay();
        rec.TitleRequested += () =>
        {
            EndingState.PendingWake = success ? EndingState.Kind.True : EndingState.Kind.Bad;
            ControlRoom3DHorror.ExternalLightingOverride = false;
            _ctl?.SetScreenTint(Colors.White);
            GetTree().ChangeSceneToFile("res://scenes/main/MainScene3D_Test.tscn");
            tcs.TrySetResult();
        };
        _layer.AddChild(rec);
        return tcs.Task;
    }

    // ── GUIDE-0 얼굴창 ───────────────────────────────────────────────────

    // 교육 때 쓰던 얼굴창 그대로 — 엔딩에서는 왼쪽 CRT 한가운데에 크게 뜬다.
    private static void ShowGuide(string expression)
    {
        var tex = GuideArt.Portrait(expression, out bool mouthless);
        GuideCornerFace.SetEndingWindow(true);
        GuideCornerFace.SetPortraitAll(expression, tex, mouthless);
        GuideCornerFace.ShowAll(true);
    }

    private static void HideGuide()
    {
        GuideMouthAnimator.Reset();
        GuideCornerFace.ShowAll(false);
        GuideCornerFace.SetEndingWindow(false);
        GuideCornerFace.SetAlarmTint(false);
    }

    // GUIDE-0 이 말하는 속도 — 콘솔 출력보다 느리게, 사람이 말하듯 또박또박.
    private const double GuideCharTime = 0.075;
    private const int GuideFontSize = 30;

    // 글자가 찍히는 동안 GUIDE-0 이 말한다(입모양 + 보이스 블립).
    private async Task GuideSpeak(EndingMonitorView view, params string[] lines)
    {
        _guideTalking = true;
        GuideMouthAnimator.StartTalking();
        view?.SetBlockCenter(true);
        foreach (var text in lines)
            view?.Push(text, EndingMonitorView.Tone.Title, GuideFontSize, center: true, charTime: GuideCharTime);
        await AwaitTyping(view);
        GuideMouthAnimator.StopTalking();
        GuideCornerFace.NotifyMouthChangedAll();
        _guideTalking = false;
    }

    // 화면에 한 글자 찍힐 때마다 — 말하는 중이면 GUIDE-0 의 입/보이스, 아니면 콘솔 타건음.
    private void HookTyping(EndingMonitorView view)
    {
        if (view == null) return;
        view.CharTyped += OnCharTyped;
    }

    private void OnCharTyped(char c)
    {
        if (_guideTalking)
        {
            GuideMouthAnimator.NoticeCharacter(c);
            Sfx.Instance?.PlayVoiceBlip("guide0", c);
            return;
        }
        if (char.IsWhiteSpace(c)) return;
        // 콘솔 타건음 — 글자마다 울리면 시끄럽다.
        if (++_keyCount % 3 != 0) return;
        Sfx.Instance?.Play("key_single", -26f, _rng.RandfRange(0.92f, 1.12f));
    }

    // 밀어 넣은 줄이 다 찍힐 때까지 기다린다.
    private async Task AwaitTyping(EndingMonitorView view, double after = 0)
    {
        int guard = 0;
        while (view != null && view.Typing && guard++ < 2000) await NextFrame();
        if (after > 0) await Wait(after);
    }

    // ── 도우미 ──────────────────────────────────────────────────────────

    private void FindLights()
    {
        Node root = _ctl;
        _m1 = root?.GetNodeOrNull<SpotLight3D>("ControlRoom/Monitor01/M01_ScreenLight");
        _m2 = root?.GetNodeOrNull<SpotLight3D>("ControlRoom/Monitor02/M02_ScreenLight");
        _fill = root?.GetNodeOrNull<SpotLight3D>("ControlRoom/DeskFillLight");
        _emergency = root?.GetNodeOrNull<OmniLight3D>("ControlRoom/EmergencyLight");
        if (_m1 != null) { _m1E = _m1.LightEnergy; _m1C = _m1.LightColor; }
        if (_m2 != null) { _m2E = _m2.LightEnergy; _m2C = _m2.LightColor; }
        if (_fill != null) { _fillE = _fill.LightEnergy; _fillC = _fill.LightColor; }
    }

    // 모니터 두 대의 빛(= 방의 주광)과 책상 보조광을 평소 대비 배율로.
    private void SetLights(float monitors, float fill)
    {
        if (_m1 != null) _m1.LightEnergy = _m1E * monitors;
        if (_m2 != null) _m2.LightEnergy = _m2E * monitors;
        if (_fill != null) _fill.LightEnergy = _fillE * fill;
    }

    private async Task Banner(string text, Color col)
    {
        _banner.Text = text;
        _banner.AddThemeColorOverride("font_color", col);
        var t = CreateTween();
        t.TweenProperty(_banner, "modulate:a", 1f, 0.8);
        t.TweenInterval(2.2);
        t.TweenProperty(_banner, "modulate:a", 0f, 0.8);
        await Wait(3.9);
    }

    private async Task Fade(ColorRect r, float to, float seconds)
    {
        var t = CreateTween();
        t.TweenProperty(r, "color:a", to, Mathf.Max(0.01f, seconds));
        await Wait(seconds);
    }

    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
