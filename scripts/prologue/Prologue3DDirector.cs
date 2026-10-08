using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그의 3D 컷씬 연출기.
//
// 예전 프롤로그는 정지 이미지를 켄 번스 · 노이즈로 넘기는 구조였다. 그 **콘티는 그대로 두고**
// (`docs/NSP_PROLOGUE_RUNTIME.md` 의 슬라이드 순서 · 대사 · 경보가 전부 살아 있다),
// 슬라이드에 `scene3d:` 가 붙어 있으면 그 자리에서 실제 3D 장면을 돌린다.
//
// 화면 구조는 셋으로 나뉜다(지시서 §J).
//   ① 기록   : 중앙제어실 모니터1 안에서 재생된다 — CRT 테두리 · 노이즈 그대로.
//   ② 재난   : SIGNAL LOST 뒤 모니터 틀을 깨고 화면 전체를 쓴다.
//   ③ 플레이어: 다시 1인칭 중앙제어실(여기는 PrologueDirector 가 맡는다).
//
// 무대는 트루엔딩이 쓰는 EndingCutsceneStage 를 그대로 쓴다 — **거대한 봉쇄 코어실이
// 같은 공간이어야** 트루엔딩에서 "내가 복구한 게 그때 터진 그 코어구나" 가 성립한다(§K).
public partial class Prologue3DDirector : Node
{
    public static Prologue3DDirector Instance { get; private set; }

    private EndingCutsceneStage _stage;
    private CanvasLayer _layer;
    private readonly List<CutsceneActor> _actors = new();
    private int _gen;
    private string _current = "";

    // 아는 장면만 3D 로 돈다. 모르는 id 는 조용히 기존 이미지 슬라이드로 돌아간다(§42).
    private static readonly string[] Known =
    {
        "archive_facility", "archive_director", "archive_habitat", "archive_core", "core_warning",
        "disaster_lab", "disaster_run", "disaster_bulkhead", "disaster_cctv",
        "core_drop", "core_explode", "disaster_after", "director_last",
        "surface_wide", "surface_road", "surface_last",
    };

    public static bool Has(string id) => !string.IsNullOrEmpty(id) && System.Array.IndexOf(Known, id) >= 0;

    // 모니터1 안에 띄울 때 쓰는 질감.
    public Texture2D Texture => _stage?.Screen?.Texture;

    public override void _Ready()
    {
        Instance = this;
        _rng.Randomize();
    }

    public override void _ExitTree()
    {
        RestoreBus();
        if (Instance == this) Instance = null;
    }

    private readonly RandomNumberGenerator _rng = new();

    // ── 무대 ────────────────────────────────────────────────────────────

    public void EnsureStage()
    {
        if (_stage != null) return;
        _layer = new CanvasLayer { Layer = 118 };   // 통화창(114) 위, 엔딩(130) 아래
        AddChild(_layer);
        _stage = new EndingCutsceneStage();
        AddChild(_stage);
        _stage.Attach(_layer);
        Flash = DoFlash;
        Muffle = DoMuffle;
        Fade = DoFade;
        // 처음에는 화면 전체 그림을 숨겨 둔다 — 기록 구간에서는 모니터1 안에서만 보인다.
        _stage.Screen.Modulate = new Color(1, 1, 1, 0);
        _stage.Screen.Visible = false;
    }

    // ── 화면 구조 전환 ───────────────────────────────────────────────────
    //
    //   모니터1 : 3D 가 CRT 안에서만 보인다(기록 영상). 화면 전체 레이어는 꺼져 있다.
    //   전체 화면 : 3D 를 화면 전체에 깔고, **그 위에** 컷씬 화면(경보 · 게이지 · 무전)을
    //              배경 없이 얹는다. 기존 시설 UI 를 그대로 쓰면서 틀만 사라진다(§D · §E).
    private TextureRect _uiScreen;

    public PrologueScript.SlideView View { get; private set; } = PrologueScript.SlideView.Monitor;
    public bool Fullscreen => View == PrologueScript.SlideView.Full;

    public void RequestView(PrologueScript.SlideView view)
    {
        if (view == View) return;
        _ = SetView(view, 0.4f);
    }

    public async Task SetFullscreen(bool on, float seconds) =>
        await SetView(on ? PrologueScript.SlideView.Full : PrologueScript.SlideView.Monitor, seconds);

    // monitor : CRT 안 / full : 3D + UI 전체 화면 / room : 현재 제어실 + UI 전체 화면
    public async Task SetView(PrologueScript.SlideView view, float seconds)
    {
        EnsureStage();
        if (_stage?.Screen == null) return;
        View = view;
        BuildUiScreen();

        var player = CutscenePlayer.Instance;
        var ctl = ControlRoom3DController.Instance;
        bool uiFull = view != PrologueScript.SlideView.Monitor;
        bool stageOn = view == PrologueScript.SlideView.Full;

        if (uiFull)
        {
            // 컷씬 뷰포트를 투명하게 만들어야 그 뒤(3D 무대 또는 제어실)가 비친다.
            if (ctl?.CutsceneViewport != null) ctl.CutsceneViewport.TransparentBg = true;
            player?.SetFullscreenMode(true);
            if (_uiScreen != null) _uiScreen.Visible = true;
        }
        if (stageOn) _stage.Screen.Visible = true;
        // room 으로 돌아오는 순간 모니터 확대를 푼다 — 여기서부터 플레이어 자신의 시점이다(§H).
        if (view == PrologueScript.SlideView.Room) ctl?.ClearFocus(0.6f);

        var t = CreateTween().SetParallel(true);
        t.TweenProperty(_stage.Screen, "modulate:a", stageOn ? 1f : 0f, Mathf.Max(0.01f, seconds));
        if (_uiScreen != null) t.TweenProperty(_uiScreen, "modulate:a", uiFull ? 1f : 0f, Mathf.Max(0.01f, seconds));
        await Wait(seconds);

        if (!stageOn)
        {
            _stage.Screen.Visible = false;
            _stage.ShowPrologueSet(EndingCutsceneStage.PSet.None);
            _stage.ShowSet(EndingCutsceneStage.Set.None);
            _current = "";
        }
        if (!uiFull)
        {
            if (_uiScreen != null) _uiScreen.Visible = false;
            player?.SetFullscreenMode(false);
            if (ctl?.CutsceneViewport != null) ctl.CutsceneViewport.TransparentBg = false;
        }
    }

    // ── 섬광 · 먹먹함 ────────────────────────────────────────────────────
    //
    // 폭발의 흰 섬광과 그 직후의 이명은 3D 안이 아니라 화면 전체에 걸린다.
    // 섬광은 **짧아야 한다** — 계속 하얗게 두면 화면이 죽는다(§24).
    private ColorRect _flash;

    private void BuildFlash()
    {
        if (_flash != null || _layer == null) return;
        _flash = new ColorRect
        {
            Color = new Color(1, 1, 1, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_flash);   // 3D · UI 위
    }

    private void DoFlash(Color color, float up, float down)
    {
        BuildFlash();
        if (_flash == null) return;
        _flash.Color = color with { A = 0f };
        var t = CreateTween();
        t.TweenProperty(_flash, "color:a", 1f, Mathf.Max(0.01f, up));
        t.TweenProperty(_flash, "color:a", 0f, Mathf.Max(0.01f, down));
    }

    // 귀가 먹먹해진다 — 효과음 버스를 한순간 눌렀다가 서서히 되돌린다(§30).
    private float _busRest = float.NaN;

    // 검은 장막 — 섬광과 달리 지정한 알파에 도달한 뒤 그대로 머문다.
    private ColorRect _fadeRect;

    private void DoFade(float alpha, float seconds)
    {
        if (_layer == null) return;
        if (_fadeRect == null)
        {
            _fadeRect = new ColorRect
            {
                Color = new Color(0, 0, 0, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _layer.AddChild(_fadeRect);
        }
        _fadeRect.Visible = true;
        CreateTween().TweenProperty(_fadeRect, "color:a", Mathf.Clamp(alpha, 0f, 1f),
            Mathf.Max(0.01f, seconds));
    }

    private void DoMuffle(float seconds)
    {
        int bus = AudioServer.GetBusIndex(GameSettings.BusSfx);
        if (bus < 0) return;
        if (float.IsNaN(_busRest)) _busRest = AudioServer.GetBusVolumeDb(bus);
        AudioServer.SetBusVolumeDb(bus, _busRest - 15f);
        var t = CreateTween();
        t.TweenMethod(Callable.From<float>(v => AudioServer.SetBusVolumeDb(bus, v)),
            _busRest - 15f, _busRest, Mathf.Max(0.05f, seconds)).SetDelay(0.35);
    }

    private void RestoreBus()
    {
        if (float.IsNaN(_busRest)) return;
        int bus = AudioServer.GetBusIndex(GameSettings.BusSfx);
        if (bus >= 0) AudioServer.SetBusVolumeDb(bus, _busRest);
        _busRest = float.NaN;
    }

    private void BuildUiScreen()
    {
        if (_uiScreen != null || _layer == null) return;
        var vp = ControlRoom3DController.Instance?.CutsceneViewport;
        if (vp == null) return;
        _uiScreen = new TextureRect
        {
            Texture = vp.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
            Visible = false,
        };
        _uiScreen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_uiScreen);   // 3D 화면(index 0) 위에 온다
    }

    // ── 재생 ────────────────────────────────────────────────────────────

    // 슬라이드가 떠 있는 동안 이 장면을 돌린다. 기다리지 않는다 — 슬라이드의 hold 가 길이를 정하고,
    // 플레이어가 먼저 넘기면 다음 Begin 이 이 장면을 끊는다(§49).
    public void Begin(string id)
    {
        if (!Has(id)) return;
        EnsureStage();
        if (_current == id) return;
        _current = id;
        int gen = ++_gen;
        ClearActors();
        // 앞 컷이 띄워 둔 카메라 이동 · 흔들림을 먼저 끊는다. 안 그러면 이 컷의
        // SetCam 을 앞 컷의 이동이 계속 덮어쓴다.
        _stage?.CancelCamMove();
        _shake = 0f;
        _stage?.ShakeOffset(Vector3.Zero);
        // 장막은 컷마다 걷는다 — 안 그러면 다음 컷이 검은 화면으로 시작한다.
        if (_fadeRect != null && !id.StartsWith("surface"))
        {
            _fadeRect.Color = _fadeRect.Color with { A = 0f };
            _fadeRect.Visible = false;
        }
        if (!id.StartsWith("surface")) SurfaceAmbienceStop();
        _ = RunScene(id, gen);
    }

    public void End()
    {
        _gen++;
        _current = "";
        ClearActors();
        _stage?.ShowPrologueSet(EndingCutsceneStage.PSet.None);
        _stage?.ShowSet(EndingCutsceneStage.Set.None);
        if (_stage?.Screen != null) { _stage.Screen.Visible = false; _stage.Screen.Modulate = new Color(1, 1, 1, 0); }
        if (_uiScreen != null) { _uiScreen.Visible = false; _uiScreen.Modulate = new Color(1, 1, 1, 0); }
        View = PrologueScript.SlideView.Monitor;
        CutscenePlayer.Instance?.SetFullscreenMode(false);
        var ctl0 = ControlRoom3DController.Instance;
        if (ctl0?.CutsceneViewport != null) ctl0.CutsceneViewport.TransparentBg = false;
        _shake = 0f;
        _shards.Clear();
        _stage?.CancelCamMove();      // 앞 컷이 띄워 둔 카메라 이동을 끊는다
        _stage?.ShakeOffset(Vector3.Zero);
        if (_flash != null) _flash.Color = _flash.Color with { A = 0f };
        if (_fadeRect != null) { _fadeRect.Color = _fadeRect.Color with { A = 0f }; _fadeRect.Visible = false; }
        SurfaceAmbienceStop();
        RestoreBus();
    }

    private bool Alive(int gen) => gen == _gen && IsInstanceValid(this);

    private void ClearActors()
    {
        foreach (var a in _actors) if (IsInstanceValid(a)) a.QueueFree();
        _actors.Clear();
    }

    private CutsceneActor Spawn(Node3D parent, string id, Vector3 pos, float yaw = 0f)
    {
        var a = new CutsceneActor();
        parent.AddChild(a);
        a.Spawn(id, pos, yaw);
        _actors.Add(a);
        return a;
    }

    private async Task RunScene(string id, int gen)
    {
        switch (id)
        {
            case "archive_facility": await ArchiveFacility(gen); break;
            case "archive_director": await ArchiveDirector(gen); break;
            case "archive_habitat": await ArchiveHabitat(gen); break;
            case "archive_core": await ArchiveCore(gen); break;
            case "core_warning": await CoreWarning(gen); break;
            case "disaster_lab": await DisasterLab(gen); break;
            case "disaster_run": await DisasterRun(gen); break;
            case "disaster_bulkhead": await DisasterBulkhead(gen); break;
            case "disaster_cctv": await DisasterCctv(gen); break;
            case "core_drop": await CoreDrop(gen); break;
            case "core_explode": await CoreExplode(gen); break;
            case "disaster_after": await DisasterAfter(gen); break;
            case "director_last": await DirectorLast(gen); break;
            case "surface_wide": await SurfaceWide(gen); break;
            case "surface_road": await SurfaceRoad(gen); break;
            case "surface_last": await SurfaceLast(gen); break;
        }
    }

    // ── ① 기록 : 시설 전경 ───────────────────────────────────────────────
    //
    // archive_01_facility.png 의 구도 — 긴 연구 구역을 안쪽으로 바라본다.
    // 다만 정지 그림이 아니라 **사람이 실제로 걸어 다니고** 카메라가 천천히 밀고 들어간다.
    private async Task ArchiveFacility(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.ArchiveHall);
        _stage.PrologueLights(1f);
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.ArchiveHall);
        _stage.SetCam(new Vector3(0f, 2.5f, 20f), new Vector3(0f, 2.3f, -14f), 52f);

        // 직원 다섯 — 전부 다른 일을 한다(§5 "전부 같은 idle 금지").
        var a = Spawn(root, "fox", new Vector3(-1.9f, 0f, 12f));
        var b = Spawn(root, "cat", new Vector3(2.4f, 0f, 0f));
        var c = Spawn(root, "dog", new Vector3(-6.4f, 0f, 5f), 90f);
        var d = Spawn(root, "sheep", new Vector3(6.6f, 0f, -4f), -90f);
        var e = Spawn(root, "rabbit", new Vector3(0.6f, 0f, -9f));

        // 걷는 둘도 걸음 위상 · 보폭이 다르다 — 같은 프레임에 같은 발이 나가면 눈에 띈다.
        a.SetRunStyle(CutsceneActor.RunStyle.Loping, 0.21f);
        b.SetRunStyle(CutsceneActor.RunStyle.Tired, 0.74f);
        a.MoveTo(new Vector3(-1.9f, 0f, -16f), 1.32f, CutsceneActor.Gait.Walk);
        b.MoveTo(new Vector3(2.4f, 0f, 18f), 1.05f, CutsceneActor.Gait.Walk);
        c.PlayClip("panel_press_m");
        d.PlayClip("console_operate_f");
        e.PlayClip("clipboard_check_m");

        await _stage.MoveCamCut(new Vector3(0f, 2.35f, 7.5f), new Vector3(0f, 2.2f, -16f), 7.0);
        if (!Alive(gen)) return;
        // 끝까지 간 직원은 돌아서 다시 걷는다 — 컷이 길어져도 멈춰 서 있지 않게.
        a.MoveTo(new Vector3(-1.2f, 0f, 14f), 1.2f, CutsceneActor.Gait.Walk);
    }

    // ── ② 기록 : 총괄 관리자 안전교육 ────────────────────────────────────
    //
    // archive_02_director.png — 카메라 정면. 녹화된 영상이므로 카메라는 거의 고정이고,
    // 관리자만 숨 쉬고 고개를 조금 움직이며 말한다(§6).
    private async Task ArchiveDirector(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Briefing);
        _stage.PrologueLights(1f);
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.Briefing);
        _stage.SetCam(new Vector3(0.1f, 1.62f, 2.65f), new Vector3(0f, 1.5f, -0.6f), 44f);

        var dir = Spawn(root, "wolf", new Vector3(0f, 0f, -1.05f), 180f);
        dir.PlayClip("talk");

        // 아주 느린 밀기 — 고정 카메라로 보이되 영상이 죽지 않을 만큼만.
        await _stage.MoveCamCut(new Vector3(0.05f, 1.6f, 2.25f), new Vector3(0f, 1.5f, -0.6f), 9.0);
    }

    // ── ③ 기록 : 생활 구역 ───────────────────────────────────────────────
    private async Task ArchiveHabitat(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Habitat);
        _stage.PrologueLights(1f);
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.Habitat);
        _stage.SetCam(new Vector3(3.4f, 1.9f, 6.2f), new Vector3(-0.5f, 1.2f, -3f), 50f);

        var a = Spawn(root, "rabbit", new Vector3(-3.2f, 0f, -0.4f), 180f);
        var b = Spawn(root, "sheep", new Vector3(4.5f, 0f, 2.5f), -40f);
        a.PlayClip("sit_typing");
        b.SetRunStyle(CutsceneActor.RunStyle.Normal, 0.58f);
        b.MoveTo(new Vector3(-1.5f, 0f, -5.5f), 1.0f, CutsceneActor.Gait.Walk);

        await _stage.MoveCamCut(new Vector3(1.6f, 1.75f, 4.4f), new Vector3(-0.8f, 1.2f, -4f), 6.0);
        if (!Alive(gen)) return;
        b.Stop();
        b.PlayClip("idle");
    }

    // ── ④ 기록 : 정상 가동 중인 거대 봉쇄 코어 ───────────────────────────
    //
    // **엔딩이 쓰는 그 코어실이다**(§8 · §K). 프롤로그에서는 이미 정상 가동 중 —
    // 링이 정렬돼 있고, 빛이 안정적이고, 푸른 계열이다.
    private async Task ArchiveCore(int gen)
    {
        _stage.ShowCoreHallForPrologue();
        PrologueCoreStable();
        // 난간 앞에 사람을 하나 세운다 — 비교 대상이 없으면 아무리 커도 크게 안 보인다.
        var root = _stage.CoreHallRoot;
        var obs = Spawn(root, "sheep", new Vector3(-2.6f, 0f, 6.4f), 170f);
        obs.PlayClip("clipboard_check_f");
        _stage.SetCam(new Vector3(4.2f, 2.2f, 15.0f), new Vector3(-0.4f, 4.6f, -3f), 62f);
        await _stage.MoveCamCut(new Vector3(1.6f, 2.4f, 10.0f), new Vector3(0f, 6.4f, -3f), 7.5);
    }

    // 정상 가동 상태 — 링 정렬 · 고정장치 물림 · 안정된 푸른 빛 · 바닥등 전부 켜짐.
    private void PrologueCoreStable()
    {
        _stage.CoreReset();
        _stage.CoreAlignRings(0.01);
        _stage.CoreClamp(0.01);
        _stage.PrologueCoreRestoreIntact();
        // 2.6 은 화면이 타 버린다 — 구(球)의 형태가 보이는 선까지만 올린다.
        // 밝기는 코어 자체가 아니라 둘레 설비(헤일로 · 인레이 · 입자)가 만든다.
        _stage.CoreCharge(1.0f, 0.01);
        for (int i = 0; i < 10; i++) _stage.CoreFloorLamp(i, 2.0f);
        // 정상 가동 중이므로 홀 자체도 켜져 있다 — 그래야 크기가 읽힌다.
        _stage.CoreHallFill(3.6f, new Color(0.62f, 0.70f, 0.85f));
        _stage.CoreFloorFill(3.0f, new Color(0.66f, 0.74f, 0.88f));
        _coreOutput = 100f;
    }

    private float _coreOutput = 100f;

    // ── ⑤ 기록의 끝 : 코어에 이상이 생긴다 ───────────────────────────────
    //
    // 아직 모니터1 안이다. 여기서 영상이 흔들리기 시작하고 SIGNAL LOST 로 이어진다(§B).
    private async Task CoreWarning(int gen)
    {
        _stage.ShowCoreHallForPrologue();
        _stage.SetCam(new Vector3(0.2f, 2.6f, 9.0f), new Vector3(0f, 8.2f, -3f), 58f);

        Sfx.Instance?.Play("machinery_loop", -14f, 0.86f);
        // 빛이 한 번 출렁인다 → 두 번째는 더 크게 → 경고음.
        for (int i = 0; i < 3 && Alive(gen); i++)
        {
            _stage.CoreCharge(0.5f, 0.12);
            await Wait(0.14);
            _stage.CoreCharge(2.6f, 0.2);
            await Wait(0.5 + i * 0.25);
        }
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("alert_beep3", -8f);
        _shake = 0.5f;
        _stage.CoreCharge(1.1f, 0.6);
        await Wait(0.8);
    }

    // ── 공용 : 화면 흔들림 ───────────────────────────────────────────────
    private float _shake;
    private float _shakeDecay = 1.4f;
    private Vector3 _camBase, _camLookBase;

    public void Shake(float amount, float decay = 1.4f)
    {
        _shake = Mathf.Max(_shake, amount);
        _shakeDecay = decay;
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        TickShards(d);
        _stage?.TickCore(d);
        _stage?.TickSurface(d);
        if (_shake <= 0f || _stage == null) return;
        _shake = Mathf.Max(0f, _shake - d * _shakeDecay);
        // 카메라 자체를 흔든다 — 화면만 흔드는 것과 달리 공간이 같이 흔들린다.
        _stage.ShakeOffset(new Vector3(
            _rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-0.4f, 0.4f)) * _shake * 0.22f);
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task NextFrame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
}
