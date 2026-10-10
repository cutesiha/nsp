using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Prologue;

namespace NSP.View;

// DAY5 최종 격리 보고서를 제출한 다음 — 엔딩 연출 전체.
//
// **프롤로그는 "모니터 안의 기록 영상을 당신이 본다" 였다. 엔딩은 반대다 —
// 방 안에서 일이 벌어지고 당신이 그 안에 있다.**
// 그래서 엔딩은 두 모니터를 한 번 완전히 꺼 버리고 시작한다. 게임 시작 이래 두 화면이
// 동시에 꺼지는 것은 처음이고, 그 순간 플레이어는 5일 만에 처음으로 "방" 을 본다.
// 신원 조회도 큰 화면이 아니라 손 앞의 작은 패드에서 한다.
//
// 엔딩은 네 갈래다 — 코어 100% 복구 × 최종 보고서의 지목.
//
//   TRUE  (복구 O · 지목 O)  안도, 그리고 쓸쓸함.
//                            재난 당일의 회상(누가 당신을 쳤는가) → 말 없는 마지막 통화
//                            → 복구 완료 → 5일 내내 울리던 기계음이 처음으로 끊긴다.
//   LOOSE (복구 O · 지목 X)  ②③ 이 TRUE 와 한 프레임도 다르지 않다. 플레이어는 이겼다고 믿는다.
//                            명단 여섯 장이 전부 "확인" 으로 끝난 뒤, 완전한 무음 속에서
//                            한 장이 깨지고 **복구되지 않는다**.
//   LATE  (복구 X · 지목 O)  당신이 전임자가 된다. 영구 봉쇄 스위치를 당신 손으로 내린다.
//   BAD   (복구 X · 지목 X)  절망. 격벽이 아래에서 위로 열리고, 문 밖으로 그림자가 지나간다.
//
// 판정은 여기서 하지 않는다 — GameState.WasCaught(최종 보고서)와 CoreProgress 를 읽을 뿐이다.
public partial class EndingDirector : Node
{
    // 엔딩이 도는 동안(기록 화면 포함) — ESC 일시정지 메뉴를 막는다.
    public static bool IsPlaying { get; private set; }

    private ControlRoom3DController _ctl;
    private TitleOverlay _title;
    private SpotLight3D _m1, _m2, _fill;
    private OmniLight3D _emergency, _ceiling, _roomFill;
    private Node3D _lightGroup, _ceilFixture;
    private MeshInstance3D _emergencyMesh;
    private StandardMaterial3D _emergencyMat;
    private float _m1E = 1.6f, _m2E = 1.6f, _fillE = 0.2f;
    private float _ceilE = 2.9f, _roomFillE = 0.34f;
    private Color _m1C, _m2C, _fillC, _ceilC, _roomFillC;

    private CanvasLayer _layer;
    private ColorRect _red;
    private ColorRect _white;
    private ColorRect _black;
    private Label _banner;
    private DizzyOverlay _dizzy;
    private EndingProps _props;

    private bool _flicker;
    private float _flickerT;
    private readonly RandomNumberGenerator _rng = new();

    // GUIDE-0 이 말하는 중인가 — 화면에 글자가 찍힐 때마다 입과 보이스가 그 신호를 받는다.
    private bool _guideTalking;
    private int _keyCount;

    private EndingState.Kind _kind;
    private float _core;

    // 5일 내내 울리던 시설의 소리. 엔딩마다 끊기는 순간이 다르다 — 그게 각 엔딩의 끝이다.
    private const float MachineryDb = -16f, DroneDb = -21f, VentDb = -15f;

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

        _red = FullRect(new Color(0.85f, 0.05f, 0.03f, 0f));
        _white = FullRect(new Color(1f, 1f, 1f, 0f));
        _black = FullRect(new Color(0f, 0f, 0f, 0f));

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

    private ColorRect FullRect(Color c)
    {
        var r = new ColorRect { Color = c, MouseFilter = Control.MouseFilterEnum.Ignore };
        r.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(r);
        return r;
    }

    public override void _Process(double delta)
    {
        // GUIDE-0 입모양 — 평소에는 GuideHologramView 가 돌리지만 엔딩에는 그 화면이 없다.
        if (GuideMouthAnimator.Talking)
        {
            GuideMouthAnimator.Tick(delta);
            GuideCornerFace.NotifyMouthChangedAll();
        }

        TickSeal(delta);

        if (!_flicker) return;
        // 경고음이 커지는 동안 조명이 불규칙하게 깜빡인다(BAD).
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
        AdminPad3D.Instance?.SetEndingMode(false);
        EndingPadConsole.Instance?.Close();
        foreach (var k in new[] { "machinery_loop", "drone_loop", "vent_loop", "alarm", "siren" })
            Sfx.Instance?.StopLoop(k);
    }

    // ────────────────────────────────────────────────────────────────────

    private async Task RunAsync()
    {
        FindLights();
        var gs = GameState.Instance;
        _core = Mathf.Min(gs?.CoreProgress ?? 0f, 100f);

        // 엔딩 축 두 개 — 코어를 복구했는가, 최종 보고서의 지목이 맞았는가.
        // '복구했는가' 의 기준은 **그 모드의 마지막 날 목표치**다(기본 5일 = 100%).
        bool recovered = DayObjectives.CoreRecovered(_core);
        bool caught = gs?.WasCaught ?? false;
        _kind = recovered
            ? (caught ? EndingState.Kind.True : EndingState.Kind.Loose)
            : (caught ? EndingState.Kind.Late : EndingState.Kind.Bad);

        // 엔딩 종류가 **확정된** 지점 — 연출용 문구가 아니라 이 값이 기록에 남는다.
        // 관리자 평가 등급도 여기서 함께 판정한다(계산은 DayObjectives.Grade 그대로).
        // 팝업은 AchievementToast 가 연출이 끝날 때까지 붙잡아 둔다.
        AchievementManager.Instance?.NoteEnding(_kind);

        ControlRoom3DHorror.ExternalLightingOverride = true;
        gs?.SetPhase(NSP.Data.GamePhase.Result);
        _ctl?.SetInputLocked(true);
        _ctl?.SetFocusLocked(true);
        _ctl?.ClearFocus(0.2f);
        Sfx.Instance?.FadeOutMusic(0.8f);

        _props = new EndingProps();
        _props.Attach(_ctl);   // 스스로 HorrorProps 밑에 붙는다

        await CommonIntro();

        switch (_kind)
        {
            case EndingState.Kind.True: await TrueEnd(); break;
            case EndingState.Kind.Loose: await LooseEnd(); break;
            case EndingState.Kind.Late: await LateEnd(); break;
            default: await BadEnd(); break;
        }

        await ShowRecord(_kind);
    }

    // ── 공통 도입 (네 엔딩 전부, 약 15초) ─────────────────────────────────
    //
    // 두 모니터를 **차례로** 끈다. 게임 내내 이 방의 빛은 두 화면이었고, 그것이 꺼지는
    // 순간 플레이어는 처음으로 천장등 아래의 방을 본다. 그다음 눈이 가는 곳은
    // 큰 화면이 아니라 책상 위에서 혼자 켜지는 작은 패드 하나다.
    private async Task CommonIntro()
    {
        Sfx.Instance?.Loop("machinery_loop", MachineryDb);
        Sfx.Instance?.Loop("drone_loop", DroneDb);
        Sfx.Instance?.Loop("vent_loop", VentDb);

        // ① 두 모니터가 차례로 꺼진다 — 꺼질 때 화면이 가로선으로 수축한다.
        // 전역 밝기를 한 번 1 로 못 박고 시작한다 — 앞 단계(배치 · 보고서)의 밝기 트윈이
        // 아직 돌고 있으면 한 화면만 끈 것이 다음 프레임에 되살아난다.
        _ctl?.SetScreenBrightness(1f);
        _ctl?.SetScreenDistortion(0.8f);
        var warp = CreateTween();
        warp.TweenMethod(Callable.From<float>(v => _ctl?.SetScreenDistortion(v)), 0.8f, 0f, 0.5);
        _ctl?.SetScreenBrightnessFor("01", 0f);
        Sfx.Instance?.Play("power_down", -12f);
        await Wait(0.3);
        _ctl?.SetScreenBrightnessFor("02", 0f);
        Sfx.Instance?.Play("power_down", -14f);
        await Wait(0.45);
        // 두 화면이 다 꺼졌으니 전역 값도 0 으로 맞춰 둔다(이후 화면을 바꿔 껴도 켜지지 않게).
        _ctl?.SetScreenBrightness(0f);

        // ② 천장등만 남기고 방이 어두워진다.
        //    평소 이 그룹은 꺼져 있다(방의 빛은 두 화면이었다) — 여기서 처음 켠다.
        ShowRoomLights(0.62f);
        var lt = CreateTween().SetParallel(true);
        if (_ceiling != null) lt.TweenProperty(_ceiling, "light_energy", _ceilE * 0.62f * 0.62f, 1.2);
        SetLights(0f, 0f);

        // ③ 앰비언트 한 단계 하강.
        Sfx.Instance?.SetLoopVolume("machinery_loop", MachineryDb - 8f);
        Sfx.Instance?.SetLoopVolume("drone_loop", DroneDb - 8f);
        await Wait(1.3);

        // ④ 정적.
        await Wait(1.0);

        // ⑤ 책상 위 패드의 배지 표시등이 켜진다.
        var pad = AdminPad3D.Instance;
        pad?.SetEndingMode(true);
        pad?.SetBadgeLed(PadBlue, 2.6f);
        Sfx.Instance?.Play("sensor_beep", -16f);
        var console = EndingPadConsole.Instance;
        if (console != null)
        {
            console.Open("");
            console.CharTyped += OnCharTyped;
        }
        await Wait(0.5);

        // ⑥ 카메라가 패드로 내려간다 — 신원 조회는 손 앞 작은 화면에서 한다.
        if (pad?.BodyNode != null) _ctl?.FocusProp(pad.BodyNode, 1.4f);
        await Wait(1.7);
    }

    // 문을 돌아볼 때 눈을 이만큼 옮긴다 — 두 CRT 가 눈앞을 가려서, 고개만 돌리면 문이
    // 모니터 뒤에 완전히 숨는다. 의자에 기대 화면 위로 올려다보는 자세다.
    protected static readonly Vector3 DoorEyeOffset = new(0f, 0.45f, 0.25f);

    protected static readonly Color PadBlue = new(0.25f, 0.70f, 1f);
    protected static readonly Color PadRed = new(1f, 0.22f, 0.16f);
    protected static readonly Color PadGreen = new(0.3f, 1f, 0.55f);

    // ── 패드 신원 조회 ───────────────────────────────────────────────────

    private EndingPadConsole Pad => EndingPadConsole.Instance;

    private void PadOpen(string header) => Pad?.Open(header);

    private void PadPush(string text, EndingMonitorView.Tone tone = EndingMonitorView.Tone.Normal,
        int size = 30, double charTime = 0) => Pad?.Push(text, tone, size, charTime);

    private async Task PadWait(double after = 0)
    {
        int guard = 0;
        while (Pad is { Typing: true } && guard++ < 3000) await NextFrame();
        if (after > 0) await Wait(after);
    }

    // ── 복구 완료 (TRUE ⑤ · LOOSE ②) ───────────────────────────────────
    //
    // **두 엔딩이 한 프레임도 다르지 않아야 한다.** 그래서 한 메서드로 둔다 —
    // 다른 것은 SYSTEM STATUS 의 마지막 한 줄뿐이고, 그것이 LOOSE 의 유일한 단서다.
    private async Task RecoveryComplete(string isolationLine, EndingMonitorView.Tone isolationTone)
    {
        var left = EndingMonitorView.Left;
        var right = EndingMonitorView.Right;
        left?.Clear();
        right?.Clear();
        HookTyping(left);
        HookTyping(right);

        // ① 두 모니터가 다시 켜진다.
        _ctl?.ClearFocus(0.9f);
        await Wait(0.5);
        _ctl?.SetLeftScreen(_ctl.EndingLeftViewport);
        _ctl?.SetRightScreen(_ctl.EndingRightViewport);
        _ctl?.SetScreenNoise(0.05f);
        // 전역 밝기를 1 로 되돌려야 꺼져 있던 뷰포트가 다시 매 프레임 그린다
        // (UpdateActiveViewports 가 전역 값으로 Always/Once 를 고른다). 오른쪽은 곧바로 다시 끈다.
        _ctl?.SetScreenBrightness(1f);
        _ctl?.SetScreenBrightnessFor("02", 0f);
        Sfx.Instance?.Play("crt_on", -8f);
        await Wait(0.4);
        _ctl?.SetScreenBrightnessFor("02", 1f);
        Sfx.Instance?.Play("crt_on", -8f);
        SetLights(0.85f, 0.5f);
        await Wait(0.6);

        // ② 왼쪽 — 막대가 목표치까지 차오른다.
        float goal = DayObjectives.FinalCoreTarget;
        left?.Clear("CONTAINMENT CORE", "FINAL RECOVERY SEQUENCE");
        left?.SetBar(goal - 2.6f);
        await Wait(0.4);
        foreach (float d in new[] { -2.6f, -1.2f, -0.4f, 0f })
        {
            left?.SetBar(goal + d);
            Sfx.Instance?.Play("gauge_tick", -10f);
            await Wait(d >= 0f ? 0.2 : 0.75);
        }

        // ③ 0.9초 정적 → "복구 완료".
        await Wait(0.9);
        left?.Push("");
        left?.Push("복구 완료", EndingMonitorView.Tone.Good, 30);

        // ④ 5일 내내 울리던 소리가 처음으로 끊긴다. 이 정적이 이 엔딩의 보상이다.
        Sfx.Instance?.StopLoop("machinery_loop");
        Sfx.Instance?.StopLoop("drone_loop");
        Sfx.Instance?.Play("task_done", -8f);
        _ctl?.SetScreenNoise(0.012f);

        // ⑤ 조명이 밝고 따뜻해진다.
        WarmRoom(2.2f);
        await AwaitTyping(left, 1.0);

        // ⑥ 환풍기 소리만 정상 속도로 남는다.
        Sfx.Instance?.SetLoopVolume("vent_loop", VentDb);

        // ⑦ 오른쪽 — 점검 결과가 한 줄씩. 마지막 한 줄만 엔딩마다 다르다.
        right?.Clear("SYSTEM STATUS");
        right?.Push("CORE ........ STABLE", EndingMonitorView.Tone.Good);
        right?.Push("POWER ....... NORMAL", EndingMonitorView.Tone.Good);
        right?.Push("CONTAINMENT . RESTORED", EndingMonitorView.Tone.Good);
        right?.Push(isolationLine, isolationTone);
        for (int i = 0; i < 4; i++)
        {
            await Wait(0.45);
            Sfx.Instance?.Play("relay_click", -13f - i * 2f);
        }
        await AwaitTyping(right, 0.6);
    }

    // 조명이 밝고 따뜻해진다(복구 완료).
    private void WarmRoom(float seconds)
    {
        var warm = new Color(1f, 0.84f, 0.66f);
        var t = CreateTween().SetParallel(true);
        if (_m1 != null) t.TweenProperty(_m1, "light_color", _m1C.Lerp(warm, 0.55f), seconds);
        if (_m2 != null) t.TweenProperty(_m2, "light_color", _m2C.Lerp(warm, 0.55f), seconds);
        if (_fill != null) t.TweenProperty(_fill, "light_color", warm, seconds);
        if (_ceiling != null)
        {
            t.TweenProperty(_ceiling, "light_color", _ceilC.Lerp(warm, 0.55f), seconds);
            t.TweenProperty(_ceiling, "light_energy", _ceilE * 0.62f * 1.1f, seconds);
        }
        if (_roomFill != null)
        {
            t.TweenProperty(_roomFill, "light_color", _roomFillC.Lerp(warm, 0.55f), seconds);
            t.TweenProperty(_roomFill, "light_energy", _roomFillE * 1.45f, seconds);
        }
        if (_emergency != null) { _emergency.LightEnergy = 0f; _emergency.Visible = false; }
    }

    // ── 5일간의 근무 기록 → [타이틀로] ──────────────────────────────────────
    private Task ShowRecord(EndingState.Kind kind)
    {
        var tcs = new TaskCompletionSource();
        var rec = new EndingRecordOverlay { Kind = kind };
        rec.TitleRequested += () =>
        {
            EndingState.PendingWake = kind;
            ControlRoom3DHorror.ExternalLightingOverride = false;
            _ctl?.SetScreenTint(Colors.White);
            _ctl?.SetFocusLocked(false);
            GetTree().ChangeSceneToFile("res://scenes/main/MainScene3D_Test.tscn");
            tcs.TrySetResult();
        };
        _layer.AddChild(rec);
        return tcs.Task;
    }

    // ── GUIDE-0 얼굴창 ───────────────────────────────────────────────────

    // 교육 때 쓰던 얼굴창 그대로 — 엔딩에서는 CRT 한가운데에 크게 뜬다.
    // 기본은 왼쪽. 오른쪽이 글자를 맡고 왼쪽이 명단을 맡는 장면(LOOSE ④)에서만 오른쪽으로 옮긴다.
    protected void ShowGuide(string expression, bool onRight = false)
    {
        var tex = GuideArt.Portrait(expression, out bool mouthless);
        GuideCornerFace.SetEndingWindow(true);
        GuideCornerFace.SetPortraitAll(expression, tex, mouthless);
        GuideCornerFace.ShowAll(true);
        PlaceGuide(onRight);
    }

    // 얼굴창이 뜰 화면을 고른다. 나머지 화면에서는 접어 둔다.
    protected void PlaceGuide(bool onRight)
    {
        GuideCornerFace.MuteIn(_ctl?.EndingLeftViewport, onRight);
        GuideCornerFace.MuteIn(_ctl?.EndingRightViewport, !onRight);
        // 명단 화면 위에는 절대 올리지 않는다 — 카드를 가린다.
        GuideCornerFace.MuteIn(_ctl?.EndingStaffViewport, true);
        GuideCornerFace.MuteIn(_ctl?.VerdictViewport, true);
    }

    // 표정만 바꾼다(말하는 도중 한 프레임에 갈아 끼울 때).
    protected static void SwapGuideFace(string expression)
    {
        var tex = GuideArt.Portrait(expression, out bool mouthless);
        GuideCornerFace.SetPortraitAll(expression, tex, mouthless);
    }

    protected static void HideGuide()
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
    // speed 가 1 보다 크면 그만큼 느리게 말한다(LATE 는 10% 느리다).
    private async Task GuideSpeak(EndingMonitorView view, params string[] lines) =>
        await GuideSpeakAt(view, 1f, lines);

    private async Task GuideSpeakAt(EndingMonitorView view, float slow, params string[] lines)
    {
        _guideTalking = true;
        GuideMouthAnimator.StartTalking();
        view?.SetBlockCenter(true);
        foreach (var text in lines)
            view?.Push(text, EndingMonitorView.Tone.Title, GuideFontSize, center: true,
                charTime: GuideCharTime * slow);
        await AwaitTyping(view);
        GuideMouthAnimator.StopTalking();
        GuideCornerFace.NotifyMouthChangedAll();
        _guideTalking = false;
    }

    // 화면에 한 글자 찍힐 때마다 — 말하는 중이면 GUIDE-0 의 입/보이스, 아니면 콘솔 타건음.
    protected void HookTyping(EndingMonitorView view)
    {
        if (view == null) return;
        view.CharTyped -= OnCharTyped;
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
    protected async Task AwaitTyping(EndingMonitorView view, double after = 0)
    {
        int guard = 0;
        while (view != null && view.Typing && guard++ < 3000) await NextFrame();
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
        _lightGroup = root?.GetNodeOrNull<Node3D>("ControlRoom/Lights");
        _ceiling = root?.GetNodeOrNull<OmniLight3D>("ControlRoom/Lights/CeilingLight");
        _roomFill = root?.GetNodeOrNull<OmniLight3D>("ControlRoom/Lights/FillLight");
        _ceilFixture = root?.GetNodeOrNull<Node3D>("ControlRoom/Lights/CeilingFixture");
        _emergencyMesh = root?.GetNodeOrNull<MeshInstance3D>("ControlRoom/Lights/EmergencyLight_Mesh");
        if (_emergencyMesh?.GetSurfaceOverrideMaterial(0) is StandardMaterial3D em)
        {
            _emergencyMat = (StandardMaterial3D)em.Duplicate();
            _emergencyMesh.SetSurfaceOverrideMaterial(0, _emergencyMat);
        }
        if (_m1 != null) { _m1E = _m1.LightEnergy; _m1C = _m1.LightColor; }
        if (_m2 != null) { _m2E = _m2.LightEnergy; _m2C = _m2.LightColor; }
        if (_fill != null) { _fillE = _fill.LightEnergy; _fillC = _fill.LightColor; }
        if (_ceiling != null) { _ceilC = _ceiling.LightColor; }
        if (_roomFill != null) { _roomFillE = _roomFill.LightEnergy; _roomFillC = _roomFill.LightColor; }
    }

    // 천장등 그룹을 켠다. 평소 이 그룹은 꺼져 있다 — 방의 빛은 두 모니터였다.
    // 엔딩에서 처음 켜지고, 그래서 플레이어가 5일 만에 처음으로 "방" 을 본다.
    private void ShowRoomLights(float factor)
    {
        if (_lightGroup != null) _lightGroup.Visible = true;
        if (_ceiling != null) _ceiling.LightEnergy = _ceilE * factor;
        if (_roomFill != null) _roomFill.LightEnergy = _roomFillE * 1.6f;
        // 등 메쉬는 그대로 켜면 화면에서 하얗게 타 버린다(평소 숨겨져 있어 조정된 적이 없다).
        if (_ceilFixture is MeshInstance3D mi && mi.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
        {
            _fixtureMat = (StandardMaterial3D)mat.Duplicate();
            _fixtureMat.EmissionEnergyMultiplier = 0.32f;
            mi.SetSurfaceOverrideMaterial(0, _fixtureMat);
        }
    }

    private StandardMaterial3D _fixtureMat;

    // 모니터 두 대의 빛(= 평소 방의 주광)과 책상 보조광을 평소 대비 배율로.
    protected void SetLights(float monitors, float fill)
    {
        if (_m1 != null) _m1.LightEnergy = _m1E * monitors;
        if (_m2 != null) _m2.LightEnergy = _m2E * monitors;
        if (_fill != null) _fill.LightEnergy = _fillE * fill;
    }

    // 전기가 한 번 흔들린다. 엔딩 동안에는 ControlRoom3DHorror 의 조명 제어가 꺼져 있으므로
    // (ExternalLightingOverride) 그쪽 FlickerOnce 를 부르지 않고 여기서 직접 깜빡인다.
    protected async Task FlickerRoom()
    {
        float ceil = _ceiling?.LightEnergy ?? 0f;
        float fill = _roomFill?.LightEnergy ?? 0f;
        float m1 = _m1?.LightEnergy ?? 0f, m2 = _m2?.LightEnergy ?? 0f;
        if (_ceiling != null) _ceiling.LightEnergy = 0f;
        if (_roomFill != null) _roomFill.LightEnergy = 0f;
        if (_m1 != null) _m1.LightEnergy = 0f;
        if (_m2 != null) _m2.LightEnergy = 0f;
        Sfx.Instance?.Play("flicker", -14f);
        await Wait(0.12);
        if (_ceiling != null) _ceiling.LightEnergy = ceil;
        if (_roomFill != null) _roomFill.LightEnergy = fill;
        if (_m1 != null) _m1.LightEnergy = m1;
        if (_m2 != null) _m2.LightEnergy = m2;
    }

    // 벽에 붙은 비상등 덩어리(발광 메쉬). 완전한 어둠에서는 이것도 꺼야 한다 —
    // 빛이 아니라 재질이 스스로 빛나는 물건이라 OmniLight 를 꺼도 그대로 남는다.
    protected void SetEmergencyMesh(float energy, Color? color = null)
    {
        if (_emergencyMat == null) return;
        if (color.HasValue) _emergencyMat.Emission = color.Value;
        _emergencyMat.EmissionEnergyMultiplier = energy;
    }

    // 조명을 하나씩 끈다(LATE 의 소등). 끝에 천장등 · 보조광만 남는다.
    protected void SetCeiling(float energy, float seconds)
    {
        if (_ceiling == null) return;
        var t = CreateTween();
        t.TweenProperty(_ceiling, "light_energy", energy, seconds);
    }

    protected void HideCeilingFixture()
    {
        if (_fixtureMat != null) _fixtureMat.EmissionEnergyMultiplier = 0f;
        if (_ceiling != null) _ceiling.LightEnergy = 0f;
    }

    protected async Task Banner(string text, Color col, Color? fontOverride = null)
    {
        _banner.Text = text;
        _banner.AddThemeColorOverride("font_color", fontOverride ?? col);
        var t = CreateTween();
        t.TweenProperty(_banner, "modulate:a", 1f, 0.8);
        t.TweenInterval(2.2);
        t.TweenProperty(_banner, "modulate:a", 0f, 0.8);
        await Wait(3.9);
    }

    protected async Task Fade(ColorRect r, float to, float seconds)
    {
        var t = CreateTween();
        t.TweenProperty(r, "color:a", to, Mathf.Max(0.01f, seconds));
        await Wait(seconds);
    }

    // 화면 전체가 한 번 번쩍인다(흰색 · 붉은색).
    protected async Task Flash(ColorRect r, float peak, float up, float down)
    {
        var t = CreateTween();
        t.TweenProperty(r, "color:a", peak, up);
        t.TweenProperty(r, "color:a", 0f, down);
        await Wait(up + down);
    }

    protected async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    protected async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
