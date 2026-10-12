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
//   ② 돌진     **3D 모델이 실제로** 달려든다. 알아볼 만한 거리에서 한 번 **멈칫** 하고,
//              그 정적 끝에 한 호흡으로 **얼굴을 화면에 들이민다**
//              (정지 이미지를 한 프레임 붙이는 방식이 아니다)
//   ③ 여진     이명 · 붉게 물든 화면이 잠깐 남는다
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

    public override void _Ready()
    {
        Instance = this;
        // 즉사 연출의 배우를 미리 세워 둔다 — 쓰는 것은 죽는 순간이지만, 그때 만들면
        // 모델을 처음 읽느라 연출의 첫 프레임이 멈춘다(PERFORMANCE_BASELINE.md D).
        _actor = MakeActor();
    }

    private MonsterActor MakeActor()
    {
        var a = new MonsterActor();
        a.Attach(this);
        MonsterWarmup.Register(a);
        return a;
    }

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
        // 비명 · 충격음 · 흔들림은 Charge 안에서, **얼굴이 들이닥치는 바로 그 순간**에
        // 같이 터진다. 여기서 따로 울리면 소리가 그림보다 한 박자 늦는다.
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null) await Charge(cam);
        else await Wait(0.35);

        // ── ③ 여진 ─────────────────────────────────────────────────
        // 들이민 얼굴은 Charge 가 이미 한 호흡 붙들고 있었다. 여기서는 숨 한 번만 더.
        await Wait(0.12);

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
    //
    // 세 토막이다 — 달려든다 → 멈칫 → 쾅.
    // 가운데의 멈칫이 핵심이다. 쉬지 않고 밀려오면 "뭔가 커졌다" 로 끝나지만,
    // 알아볼 만한 거리에서 한 번 멈춰 서면 다음 한 호흡이 놀란다.
    private async Task Charge(Camera3D cam)
    {
        var def = _threat?.Def;
        if (def == null) { await Wait(0.35); return; }

        // 배우는 **사망하는 순간에 만들지 않는다.**
        //
        // 전에는 여기서 new 했다. 그래서 즉사 연출이 시작되는 바로 그 프레임에 FBX 를
        // 처음 읽었고(실측 88.8 ms), 가장 놀라야 할 장면이 멈춘 채 시작했다.
        // 이제 _Ready 에서 만들어 MonsterWarmup 에 등록해 두므로 여기서는 꽂기만 한다.
        _actor ??= MakeActor();
        if (!_actor.Use(def)) { await Wait(0.35); return; }

        // 발바닥에서 얼굴까지의 높이. 모델은 발이 원점이므로 이만큼 내려야 얼굴이
        // 화면 한가운데 온다 — 한 값으로 고정하면 큰 것은 가슴만, 작은 것은 화면
        // 밖으로 빠진다. **키울 때도 같은 비율로** 내려야 한다(안 그러면 몸집이
        // 커지는 만큼 얼굴이 화면 위로 빠져나가 어깨만 남는다).
        float eye = Mathf.Clamp(def.TargetHeight - 0.16f, 0.52f, 1.80f);
        // 들이미는 자세는 고개가 조금 내려가 있어 계산대로 세우면 얼굴이 화면 가운데보다
        // 아래에 걸린다. 그만큼 들어 올린다 — 키에 비례해야 작은 개체가 위로 뜨지 않는다.
        float faceLift = def.TargetHeight * 0.077f;

        // 관리자 자리에서 터지는 짧은 빛. 이게 없으면 "검은 덩어리가 화면을 덮었다"
        // 로만 보이고, 무엇에게 당했는지 끝내 알 수 없다. 가까워질수록 밝아진다.
        var flash = new OmniLight3D
        {
            // 윤곽만 드러낼 만큼. 세게 비추면 얼굴이 하얗게 날아가 무엇인지 더 안 보인다.
            LightColor = new Color(0.78f, 0.80f, 0.86f),
            OmniRange = 2.4f, LightEnergy = 0f, ShadowEnabled = false,
        };
        AddChild(flash);

        // 카메라 자리를 **매 프레임 다시 읽는다**. 마지막에 화면을 세게 흔드는데,
        // 처음 한 번 읽은 자리에 세워 두면 들이민 얼굴이 흔들림과 함께 화면 밖으로
        // 밀려나 어깨만 스쳐 지나간다. 괴물은 흔들려도 렌즈에 붙어 있어야 한다.
        void Pose(float dist, float s, float yawWobble = 0f)
        {
            Transform3D c = cam.GlobalTransform;
            Vector3 fwd = -c.Basis.Z.Normalized();
            // 관리자(카메라)를 똑바로 보게 세운다. 각도 계산은 MonsterActor 가 월드
            // 좌표로 처리한다 — 여기서 로컬 yaw 를 직접 넣으면 부모 노드가 놓인
            // 방향에 따라 앞뒤가 뒤집힌다.
            _actor.FaceViewer(c.Origin - Vector3.Up * (eye * s - faceLift) + fwd * dist,
                              c.Origin, s, yawWobble);
            // 빛은 관리자 쪽에 남는다 — 괴물에 바싹 붙이면 얼굴이 하얗게 날아간다.
            flash.GlobalPosition = c.Origin + fwd * 0.12f + c.Basis.X * 0.28f - Vector3.Up * 0.05f;
        }

        const float FarDist = 3.6f;      // 문간의 어둠에서 출발
        const float HoldDist = 1.45f;    // 무엇인지 알아볼 수 있는 거리
        // 얼굴이 화면을 채우는 거리. 키에 비례한다 — 작은 개체를 큰 것과 같은 거리에
        // 세우면 화면 아래쪽 귀퉁이에만 걸린다.
        //
        // **더 붙이면 안 된다.** 0.4m 쯤에서 카메라가 모델 안으로 들어가 버려
        // 뒷면이 잘려 나가고 화면에 아무것도 남지 않는다(가장 중요한 한 컷이 통째로
        // 비었던 원인이다). 0.6m 도 얼굴이 아니라 뺨 한 면만 가득 찬다.
        float faceDist = Mathf.Max(0.52f, 0.30f + def.TargetHeight * 0.30f);

        Pose(FarDist, 1f);
        // 달려드는 동안은 **달리기** 클립이다. 비명 클립을 처음부터 틀면 웅크렸다
        // 도는 준비 동작만 보여 주고 끝나 버린다(얼굴 대신 등이 들이닥친다).
        _actor.PlayRole(Approach(def), 1.35f);

        // ⓐ 달려든다 — 뒤로 갈수록 빨라진다(ease-in). 피할 수 없다는 감각.
        await Sweep(0.30, u =>
        {
            float e = u * u * u;
            Pose(Mathf.Lerp(FarDist, HoldDist, e), 1f);
            flash.LightEnergy = Mathf.Lerp(0.06f, 0.75f, e);
        });

        // ⓑ 멈칫 — 숨 한 번. 제자리에서 아주 조금 떨 뿐이다.
        //    이 정적이 있어야 바로 다음 한 호흡이 놀란다.
        await Sweep(0.16, u =>
        {
            Pose(HoldDist, 1f, Mathf.Sin(u * 26f) * 1.1f);
            flash.LightEnergy = 0.75f;
        });

        // ⓒ 쾅 — 한 호흡에 얼굴을 들이민다.
        // 소리 · 흔들림 · 붉은 섬광이 **이 순간에** 같이 터져야 "쾅" 으로 읽힌다.
        // 여기서 비명 클립으로 갈아타되 **앞을 보는 구간부터** 튼다.
        if (!string.IsNullOrEmpty(def.AnimJumpscare))
        {
            _actor.PlayRole(def.AnimJumpscare, 1f, 0d);
            if (def.AnimJumpscareAtSeconds > 0f) _actor.SeekRole(def.AnimJumpscareAtSeconds);
        }
        var ctl = ControlRoom3DController.Instance;
        Sfx.Instance?.PlayGhostScream(2f);
        Sfx.Instance?.Play("impact_blunt", 0f, 0.86f);
        Sfx.Instance?.Play("tinnitus", -6f);
        ctl?.ShakeCamera(9f, 0.55f);
        ctl?.SetScreenTint(new Color(1.6f, 0.25f, 0.2f));
        CCTVMonitorView.Instance?.FlashGlitch(1f);
        await Sweep(0.06, u =>
        {
            float s = Mathf.Lerp(1f, 1.12f, u);
            Pose(Mathf.Lerp(HoldDist, faceDist, u), s);
            flash.LightEnergy = Mathf.Lerp(0.75f, 0.95f, u);
        });

        // 얼굴이 박힌 채로 남는다 — 흔들리는 동안에도 계속 화면 한가운데에 붙여 둔다.
        await Sweep(0.42, _ =>
        {
            Pose(faceDist, 1.12f);
            flash.LightEnergy = 0.95f;
        });
    }

    // seconds 동안 매 프레임 step(0→1) 을 부른다. 돌진의 세 토막이 같은 방식으로 움직인다.
    private async Task Sweep(double seconds, System.Action<float> step)
    {
        double t = 0;
        while (t < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += GetProcessDeltaTime();
            step(Mathf.Clamp((float)(t / seconds), 0f, 1f));
        }
    }

    // 달려드는 동안 쓸 클립 — 달리기가 있으면 그것, 없으면 걷기, 그것도 없으면 비명.
    // 셋 다 없는 모델(거미)은 MonsterActor 가 코드 연출로 몸을 흔든다.
    private static string Approach(MonsterDef def) =>
        !string.IsNullOrEmpty(def.AnimRun) ? def.AnimRun
        : !string.IsNullOrEmpty(def.AnimWalk) ? def.AnimWalk
        : def.AnimJumpscare;

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

        col.AddChild(Lbl("당신은 사망했습니다.", 42, red));
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
