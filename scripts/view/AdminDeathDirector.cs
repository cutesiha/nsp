using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 차폐에 실패했다 — 괴물이 중앙제어실로 넘어와 관리자를 죽인다.
//
// 직원의 죽음이 아니다. 시설 로그에도 추리 자료에도 남지 않고, 엔딩 네 갈래와도 무관하다.
// 근무는 그 자리에서 끝나고 **다음 날로 넘어가지 않는다**(지시서 §7 · §5-3B).
//
// 연출 순서 — 전부 합쳐 2초 남짓이다.
//   ① 인기척   아주 짧은 정적 + 바로 뒤의 숨소리. 화면이 한 번 꺾인다
//   ② 돌진     **3D 모델이 실제로** 카메라 앞까지 0.35초 만에 밀려 들어온다
//              (정지 이미지를 한 프레임 붙이는 방식이 아니다)
//   ③ 충격     비명 · 충격음 · 이명 · 강한 흔들림 · 붉은 섬광
//   ④ 암전     BlinkOverlay 로 화면 전체가 닫힌다
//   ⑤ 기록     사망 화면 → 타이틀
//
// 모니터를 확대해 보고 있었더라도 괴물이 **카메라 바로 앞**에 서므로 무엇에 당했는지 보인다.
public partial class AdminDeathDirector : Node
{
    public static AdminDeathDirector Instance { get; private set; }
    public static bool IsPlaying { get; private set; }

    private MonsterThreat _threat;
    private MonsterActor _actor;

    // 한 판에 한 번만. 이미 죽었으면 두 번째 신호는 무시한다.
    public static void Begin(MonsterThreat t)
    {
        if (IsPlaying) return;
        // 가상 시뮬레이션(DAY0)에서는 죽지 않는다. 조작을 배우는 자리에서 실패 한 번에
        // 판이 끝나면 배울 기회가 사라진다(지시서 §7). MonsterThreatSystem.NoBreach 가
        // 애초에 여기까지 오지 못하게 막지만, 마지막 빗장을 하나 더 건다.
        if (NSP.Core.DayFeatures.IsTutorialDay) return;
        var ctl = ControlRoom3DController.Instance;
        if (ctl == null) return;
        var d = new AdminDeathDirector { _threat = t };
        ctl.AddChild(d);
        d.Run();
    }

    public override void _Ready() => Instance = this;

    public override void _ExitTree()
    {
        IsPlaying = false;
        if (Instance == this) Instance = null;
    }

    private async void Run()
    {
        IsPlaying = true;
        var ctl = ControlRoom3DController.Instance;
        var gs = GameState.Instance;

        // 근무를 그 자리에서 멈춘다. 시뮬레이션 틱 · 패드 · 전화 · 근무 종료 전부 닫힌다.
        gs?.SetPhase(GamePhase.Result);
        RepairApprovalSystem.AbortForShiftEnd();
        ctl?.SetInputLocked(true);
        ctl?.SetFocusLocked(true);

        // ── ① 인기척 ───────────────────────────────────────────────
        // 소리가 먼저다. 보기 전에 **등 뒤에 있다는 것**부터 안다.
        Sfx.Instance?.Play("breath_close", -3f, 0.92f);
        AdminFearDirector.Instance?.Burst(1f, 3.5f);
        ctl?.ShakeCamera(0.9f, 0.35f);
        await Wait(0.42);

        // ── ② 돌진 ─────────────────────────────────────────────────
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null) await Charge(cam);
        else await Wait(0.35);

        // ── ③ 충격 ─────────────────────────────────────────────────
        Sfx.Instance?.PlayGhostScream(2f);
        Sfx.Instance?.Play("impact_blunt", 0f, 0.86f);
        Sfx.Instance?.Play("tinnitus", -6f);
        ctl?.ShakeCamera(9f, 0.55f);
        ctl?.SetScreenTint(new Color(1.6f, 0.25f, 0.2f));
        CCTVMonitorView.Instance?.FlashGlitch(1f);
        await Wait(0.55);

        // ── ④ 암전 ─────────────────────────────────────────────────
        var blink = BlinkOverlay.Instance;
        if (blink != null) await blink.Close(0.45);
        else await Wait(0.45);

        _actor?.Hide();
        AdminFearDirector.Instance?.ResetAll();
        foreach (string k in new[] { "machinery_loop", "drone_loop", "vent_loop", "alarm", "siren" })
            Sfx.Instance?.StopLoop(k);
        Sfx.Instance?.FadeOutMusic(0.4f);

        // ── ⑤ 기록 ─────────────────────────────────────────────────
        ShowRecord();
    }

    // 괴물이 **실제로** 카메라 쪽으로 밀려 들어온다. 모델 · 애니메이션은 데이터가 정한다.
    private async Task Charge(Camera3D cam)
    {
        var def = _threat?.Def;
        if (def == null) { await Wait(0.35); return; }

        _actor = new MonsterActor();
        _actor.Attach(this);
        if (!_actor.Use(def)) { await Wait(0.35); return; }

        Transform3D c = cam.GlobalTransform;
        Vector3 fwd = -c.Basis.Z.Normalized();
        Vector3 up = Vector3.Up;
        // 모델은 발이 원점에 있다. 발밑을 **그 개체의 키만큼** 내려야 마지막 한
        // 프레임에 얼굴이 화면 한가운데 온다 — 한 값으로 고정하면 큰 것은 가슴만,
        // 작은 것은 화면 밖으로 빠진다.
        Vector3 floor = c.Origin - up * Mathf.Clamp(def.TargetHeight - 0.16f, 0.52f, 1.80f);

        // 카메라를 바라보게 돌린다. 개체마다 모델의 앞이 어느 축인지 다르므로
        // 여기서 뒤집지 않는다 — 어긋나는 모델은 그 개체의 YawOffsetDegrees 로 맞춘다
        // (복도 CCTV 와 이 돌진이 같은 값을 쓰게 하려면 한 곳에서만 고쳐야 한다).
        float yaw = Mathf.RadToDeg(Mathf.Atan2(-fwd.X, -fwd.Z));
        Vector3 far = floor + fwd * 4.2f;
        // 코앞까지 들어온다. 다만 **키에 따라** 멈추는 거리가 달라야 한다 — 같은 거리면
        // 큰 개체는 가슴만 화면을 덮어 무엇인지 알 수 없고, 작은 개체는 멀어 보인다.
        Vector3 near = floor + fwd * (0.38f + def.TargetHeight * 0.18f);

        _actor.SetPose(far, yaw);
        _actor.PlayRole(PickCharge(def), 1.35f);

        // 관리자 자리에서 터지는 짧은 빛. 이게 없으면 "검은 덩어리가 화면을 덮었다"
        // 로만 보이고, 무엇에게 당했는지 끝내 알 수 없다. 가까워질수록 밝아진다.
        var flash = new OmniLight3D
        {
            // 윤곽만 드러낼 만큼. 세게 비추면 얼굴이 하얗게 날아가 무엇인지 더 안 보인다.
            LightColor = new Color(0.78f, 0.80f, 0.86f),
            OmniRange = 2.2f, LightEnergy = 0f, ShadowEnabled = false,
            Position = c.Origin + fwd * 0.25f + up * 0.10f,
        };
        AddChild(flash);

        // 0.35초 만에 코앞까지. 뒤로 갈수록 빨라진다(ease-in) — 피할 수 없다는 감각.
        const double dur = 0.35;
        double t = 0;
        while (t < dur)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
            float u = Mathf.Clamp((float)(t / dur), 0f, 1f);
            float e = u * u * u;
            _actor.SetPose(far.Lerp(near, e), yaw, Mathf.Lerp(1f, 1.18f, e));
            flash.LightEnergy = Mathf.Lerp(0.08f, 0.85f, e);
        }
    }

    // 점프스케어 클립이 있으면 그것, 없으면 달리기, 그것도 없으면 걷기.
    // 셋 다 없는 모델(거미)은 MonsterActor 가 코드 연출로 몸을 흔든다.
    private static string PickCharge(MonsterDef def) =>
        !string.IsNullOrEmpty(def.AnimJumpscare) ? def.AnimJumpscare
        : !string.IsNullOrEmpty(def.AnimRun) ? def.AnimRun
        : def.AnimWalk;

    // ── 사망 기록 화면 ──────────────────────────────────────────────

    private void ShowRecord()
    {
        var layer = new CanvasLayer { Layer = 165, ProcessMode = ProcessModeEnum.Always };
        AddChild(layer);

        var bg = new ColorRect { Color = new Color(0.02f, 0.01f, 0.012f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(center);

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        center.AddChild(col);

        var red = new Color(0.86f, 0.26f, 0.21f);
        var ink = new Color(0.80f, 0.82f, 0.80f);
        var dim = new Color(0.46f, 0.50f, 0.49f);

        col.AddChild(Lbl("관리자 사망", 42, red));
        col.AddChild(Lbl("ADMINISTRATOR LOST", 16, red with { A = 0.75f }));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });

        var seg = FacilitySimulation.Instance?.Corridors.ById(_threat?.SegmentId ?? "");
        string where = seg?.DisplayName ?? "접근 복도";
        col.AddChild(Lbl($"{where} 차폐 실패", 20, ink));
        col.AddChild(Lbl($"DAY {GameState.Instance?.CurrentDay ?? 1} · " +
                         $"{NSP.Dialogue.DialogueClock.Text(GameState.Instance?.DayTimeSeconds ?? 0f)}", 15, dim));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        col.AddChild(Lbl("중앙제어실 접근을 막지 못했습니다.", 16, dim));
        col.AddChild(Lbl("이 근무 기록은 여기서 끝납니다.", 16, dim));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 26) });

        var btn = MonitorUi.Button("[ 타이틀로 ]", red, ViewFont.Default, GoToTitle, ViewFont.FS(18));
        btn.CustomMinimumSize = new Vector2(260, 52);
        btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        col.AddChild(btn);

        // 화면이 뜬 뒤 암전을 걷는다 — 검은 화면 위에 글자만 남는다.
        BlinkOverlay.Instance?.OpenNow();
    }

    private static Label Lbl(string text, int size, Color col)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontOverride("font", ViewFont.Default);
        l.AddThemeFontSizeOverride("font_size", ViewFont.FS(size));
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    // 다음 날로 넘어가지 않는다. 판을 통째로 비우고 시작 화면으로 돌아간다.
    private void GoToTitle()
    {
        IsPlaying = false;
        ControlRoom3DHorror.ExternalLightingOverride = false;
        ControlRoom3DController.Instance?.SetScreenTint(Colors.White);
        ControlRoom3DController.Instance?.SetFocusLocked(false);
        GameState.Instance?.ResetRun();
        FacilitySimulation.Instance?.ResetRun();
        EventLog.Instance?.ClearAll();
        DialogueHistory.Instance?.ClearAll();
        NSP.Taboo.TabooRuleSystem.Instance?.ActivateDailyTaboos(System.Array.Empty<string>());
        GetTree().ChangeSceneToFile("res://scenes/main/MainScene3D_Test.tscn");
    }

    private async Task Wait(double seconds)
        => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
