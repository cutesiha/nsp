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
// 연출 순서 — **뜸을 길게, 손은 짧게.**
//   ① 정적     3초. 숨소리도 복도의 울음도 끊기고 **아무 일도 일어나지 않는다.**
//              방 소리만 평소대로 돈다. "갔나?" 하고 생각하게 되는 자리다
//   ② 팍       0.07초. 어둠 속 3.6m 에서 **렌즈 앞까지 한 번에.** 비명 · 충격음 ·
//              흔들림 · 붉은 조명이 전부 이 순간에 같이 터진다
//              (정지 이미지를 한 프레임 붙이는 방식이 아니라 3D 모델이 실제로 온다)
//   ③ 여진     이명 · 붉게 물든 화면이 잠깐 남는다
//   ④ 암전     BlinkOverlay 로 화면 전체가 닫힌다
//   ⑤ 기록     사망 화면 → 타이틀
//
// ①의 3초가 이 연출의 전부다. 전에는 여기에 숨소리와 붉은 조명을 깔아 "온다" 를
// 미리 알려 줬는데, 그러면 눈이 따라갈 준비를 해 버려 정작 들이닥칠 때 놀랄 것이 없었다.
//
// 모니터를 확대해 보고 있었더라도 괴물이 **카메라 바로 앞**에 서므로 무엇에 당했는지 보인다.
public partial class AdminDeathDirector : Node
{
    public static AdminDeathDirector Instance { get; private set; }
    public static bool IsPlaying { get; private set; }

    private MonsterThreat _threat;
    private MonsterActor _actor;

    // 차폐가 뚫린 뒤 **아무 일도 일어나지 않는** 시간. 길면 "고장인가?" 가 되어 놀랄 틈을 놓친다 —
    // 짧게 숨만 죽였다가 갑자기 들이민다(2026-10-29).
    private const double SilenceSeconds = 1.2;

    // 얼굴이 꽂히는 순간의 배율. 1 이면 실제 키 그대로라 "멀리 서 있는 사람" 으로 보인다 —
    // 조금 키워야 렌즈를 밀고 들어온 느낌이 난다.
    private const float FaceScale = 1.28f;

    // 그것이 내던 소리를 전부 끊는다. 방 소리(기계 · 환기)는 남긴다 —
    // 완전한 무음은 "소리가 고장 났다" 로 들리고, 평소 소리가 그대로 도는 쪽이
    // **아무 일도 없는 것처럼** 느껴져서 더 방심하게 된다.
    private static void SilenceTheRoom()
    {
        var sfx = Sfx.Instance;
        if (sfx == null) return;
        sfx.StopLoop("man_breath_real");
        sfx.StopLoop("breath_faint");
        foreach (string ch in new[] { "corridor_cry" }) sfx.StopWarped(ch, 0.25f);
        AdminFearDirector.Instance?.ResetAll();
        AmbientOverlay.Instance?.SetGhostNoise(0f);
    }

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

    // ── 엔딩(BAD)의 마지막 한 컷 — 같은 들이닥침을 **판정 없이** 빌려 쓴다 ─────────
    //
    // 문이 열리고 아무도 없는 줄 알았던 그때, 그것이 렌즈 앞에 박힌다(2026-10-31 사용자 요청).
    // Begin 은 근무를 닫고 사망 기록을 띄우므로 쓰지 않는다 — 여기서는 Charge 만 돈다.
    // 배우는 미리 세워 모델까지 읽어 둔다. 들이닥치는 그 프레임에 읽으면 첫 컷이 멈춘다.
    public static AdminDeathDirector PrepareScare(Node host, string monsterId)
    {
        MonsterDef def = null;
        foreach (var m in MonsterThreatSystem.Monsters)
            if (m.MonsterId == monsterId) { def = m; break; }
        if (def == null && MonsterThreatSystem.Monsters.Count > 0) def = MonsterThreatSystem.Monsters[0];
        if (host == null || def == null) return null;
        var d = new AdminDeathDirector { _threat = new MonsterThreat { Def = def } };
        host.AddChild(d);
        if (d._actor.Use(def)) d._actor.Hide();
        return d;
    }

    public async Task ScareAsync()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null) await Charge(cam);
        else { Sfx.Instance?.PlayGhostScreamChorus(); await Wait(0.8); }
    }

    // 들이닥친 얼굴 · 붉은 조명을 거둔다. 암전은 부른 쪽이 먼저 쳐 둔다.
    public void EndScare()
    {
        _redOn = false;
        _actor?.Hide();
        EndFaceOverlay();
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

        // 조작만 닫는다. **단계는 아직 Live 그대로 둔다** —
        // Result 로 내리면 HUD 가 전부 "근무가 끝났다" 로 보고 사라진다. 3초 동안
        // 화면이 텅 비면 그건 정적이 아니라 고장으로 보인다. 평소 화면 그대로
        // 아무 일도 안 일어나는 것이 이 연출의 전부다.
        ctl?.SetInputLocked(true);
        ctl?.SetFocusLocked(true);

        // ── ① 정적 ─────────────────────────────────────────────────
        //
        // **3초 동안 아무 소리도 나지 않는다.**
        //
        // 전에는 여기서 숨소리를 깔고 방을 붉게 물들인 뒤 곧바로 달려들게 했다.
        // 그러면 "온다" 를 미리 알려 주는 꼴이라 정작 들이닥칠 때 놀랄 것이 없다.
        // 숨소리도 복도의 울음도 끊고 가만히 둔다 — 플레이어가 **갔나?** 하고
        // 생각할 때쯤이 가장 무방비한 순간이고, 그 자리를 노린다.
        //
        // 화면은 그대로 두고 **소리만** 끊는다(SilenceTheRoom).
        SilenceTheRoom();
        await Wait(SilenceSeconds);

        // 여기서부터 근무를 닫는다 — 정적이 끝나고 들이닥치는 순간이다.
        gs?.SetPhase(GamePhase.Result);
        RepairApprovalSystem.AbortForShiftEnd();

        // ── ② 팍 ───────────────────────────────────────────────────
        // 비명 · 충격음 · 흔들림 · 붉은 조명이 **얼굴이 들이닥치는 그 한 프레임**에
        // 전부 같이 터진다. 조금이라도 먼저 울리면 그게 예고가 된다.
        // 들이밈 → 고개를 뒤로 젖힘 → 박치기 → 기괴한 비명 여러 개(Charge 안에서 전부).
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null) await Charge(cam);
        else { Sfx.Instance?.PlayGhostScreamChorus(); await Wait(0.8); }

        // ── ④ 암전 ─────────────────────────────────────────────────
        // 비명이 울리는 동안 화면이 어두워지며 닫히고, 그 뒤 사망 기록이 뜬다.
        var blink = BlinkOverlay.Instance;
        if (blink != null) await blink.Close(0.9);
        else await Wait(0.9);
        await Wait(0.5);

        _actor?.Hide();
        EndFaceOverlay();
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
        // 모델은 FitToHeight 가 **실제로 재서** 키를 TargetHeight 에 맞춰 둔다. 즉
        // 머리 꼭대기가 발에서 정확히 TargetHeight 다. 얼굴 한가운데는 그 92% 쯤이다.
        // 이 높이를 카메라와 같게 두면 얼굴이 화면 한가운데에 온다.
        //
        // 전에는 `TargetHeight - 0.16` 에 보정값(faceLift)을 더하는 식이었다. 키가
        // 다른 셋에 같은 뺄셈을 쓰니 작은 개체(아기 0.78m · 거미 0.62m)와 큰 개체
        // (결번자 1.82m)의 결과가 달라져서, 어떤 것은 얼굴이 뜨고 어떤 것은 가슴만 찼다.
        // 비율로 두면 셋이 같은 자리에 온다.
        // 아래에서 실제 자세를 재어 다시 넣는다(PosedTopY). 여기 값은 임시다.
        float eye = def.TargetHeight * 0.88f;
        const float faceLift = 0f;

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
        Vector3? face = null;
        Vector3 hips = Vector3.Zero;
        void Pose(float dist, float s, float yawWobble = 0f, float leanBack = 0f, float lift = 0f)
        {
            Transform3D c = cam.GlobalTransform;
            _poseView = c;
            Vector3 fwd = -c.Basis.Z.Normalized();
            // 관리자(카메라)를 똑바로 보게 세운다. 각도 계산은 MonsterActor 가 월드
            // 좌표로 처리한다 — 여기서 로컬 yaw 를 직접 넣으면 부모 노드가 놓인
            // 방향에 따라 앞뒤가 뒤집힌다.
            if (face is Vector3 f)
            {
                // 머리 뼈가 있으면 얼굴 한가운데를 렌즈 축 위 dist 지점에 못 박는다 — 박든 떨든
                // 얼굴이 화면 한가운데를 벗어나지 않는다. 젖힐 때는 바로 선 자세의 골반을 축으로
                // 상체째 뒤로 젖힌다(박치기 전의 뜸). 목을 축으로 돌렸더니 아래의 커다란 몸통이
                // 렌즈 쪽으로 쏠려 박치기 직전 화면을 시커멓게 가렸다.
                Vector3 at = c.Origin + fwd * dist + Vector3.Up * lift;
                _actor.FaceViewer(at, c.Origin, s, yawWobble);
                _actor.PinLocalPoint(f, at);
                if (Mathf.Abs(leanBack) > 0.01f)
                {
                    Vector3 hipsAt = _actor.ToGlobal(hips);
                    _actor.FaceViewer(at, c.Origin, s, yawWobble, leanBack);
                    _actor.PinLocalPoint(hips, hipsAt);
                }
            }
            else
                _actor.FaceViewer(c.Origin - Vector3.Up * (eye * s - faceLift - lift) + fwd * dist,
                                  c.Origin, s, yawWobble, leanBack, eye * 0.85f);
            // 빛은 관리자 쪽에 남는다 — 괴물에 바싹 붙이면 얼굴이 하얗게 날아간다.
            flash.GlobalPosition = c.Origin + fwd * 0.12f + c.Basis.X * 0.28f - Vector3.Up * 0.05f;
        }

        // 출발 거리. 3초 동안 아무것도 없다가 **여기서 렌즈까지 한 번에** 온다.
        const float FarDist = 3.6f;

        // 보이지 않는 자리에 세워 두기만 한다 — 어두운 3.6m 지점이라 화면에 걸리지 않는다.
        Pose(FarDist, 1f);
        flash.LightEnergy = 0f;
        // 비명 클립을 **앞을 보는 구간부터** 튼다. 처음부터 틀면 웅크렸다 도는 준비
        // 동작만 보여 주고 끝나 버린다(얼굴 대신 등이 들이닥친다).
        if (!string.IsNullOrEmpty(def.AnimJumpscare))
        {
            _actor.PlayRole(def.AnimJumpscare, 1f, 0d);
            if (def.AnimJumpscareAtSeconds > 0f) _actor.SeekRole(def.AnimJumpscareAtSeconds);
        }
        else _actor.PlayRole(Approach(def), 1.6f);

        // **그 자세의 키를 직접 잰다.** 두 프레임을 흘려 스켈레톤이 그 자세로 자리를
        // 잡게 한 다음에 재야 한다(세운 프레임에는 아직 차렷 자세다).
        //
        // TargetHeight 를 쓰면 안 된다 — 그건 차렷 자세의 키이고, 비명 클립은 몸을
        // 숙이거나 웅크리게 만들어 그 순간의 머리 높이가 전혀 다르다. 실제로 키 기준으로
        // 계산했을 때 결번자의 얼굴이 화면 아래로 빠져 어깨만 찍혔다.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        float top = _actor.PosedTopY();
        // 잰 키의 이 지점을 **카메라 높이에 맞춘다.** 값이 작을수록 괴물이 위로 올라온다
        // (발이 원점이라, 적게 내릴수록 몸 전체가 올라간다).
        //
        // 0.88(= 머리 꼭대기 바로 아래)로 뒀더니 셋 다 얼굴이 화면 아래쪽에 걸렸다.
        // 서 있는 자세를 기준으로 하면 맞는 비율이지만, 들이닥치는 자세는 하나같이
        // 몸을 앞으로 숙이고 있어서 **얼굴이 잰 높이보다 훨씬 아래**에 있다.
        // 그만큼 더 올린다 — 조금 높게 걸리는 쪽이 낫다. 화면을 내려다보는 각이 되고,
        // 낮게 걸리면 책상 뒤에서 올라오는 것처럼 보인다.
        eye = top * 0.72f;

        // 얼굴이 화면을 채우는 거리. 잰 키에 비례한다 — 작은 개체를 큰 것과 같은
        // 거리에 세우면 화면 귀퉁이에만 걸린다.
        //
        // **더 붙이면 안 된다.** 0.4m 쯤에서 카메라가 모델 안으로 들어가 버려
        // 뒷면이 잘려 나가고 화면에 아무것도 남지 않는다(가장 중요한 한 컷이 통째로
        // 비었던 원인이다). 전에는 0.84m 였는데, 그 거리에서는 몸통까지 다 들어와
        // **책상 뒤에 서 있는 것**처럼 보였다.
        float faceDist = Mathf.Max(0.55f, 0.20f + top * 0.17f);
        if (_actor.FacePoints(out Vector3 fc, out hips)) face = fc;
        // 머리 뼈로 맞출 때는 dist 가 **렌즈에서 얼굴까지**다(위 값은 발 기준이라 몸통이 먼저 닿는다).
        // 0.40 은 들이밀어도 얼굴이 화면 높이의 4할밖에 안 돼 덜 무서웠다.
        if (face != null) faceDist = 0.30f;
        Pose(FarDist, 1f);
        // 괴물만 따로 찍어 화면 맨 앞에 겹친다 — 얼굴이 서는 자리(카메라 앞 0.5~0.8m)보다 모니터 · 책상이
        // 가까워서, 그냥 두면 얼굴이 통째로 모니터 뒤에 가려졌다(2026-10-29 — "깜놀을 안 시킨다").
        BeginFaceOverlay(cam);

        // 다섯 박자(2026-10-29 사용자 지시) —
        //   ⓐ 갑자기 얼굴을 들이민다  ⓑ 고개를 뒤로 젖힌다  ⓒ 빠르게 얼굴로 박치기
        //   ⓓ 기괴한 비명 여러 개가 한꺼번에  (ⓔ 암전 · 사망 표시는 Run 이 잇는다)
        var ctl = ControlRoom3DController.Instance;
        float leanDist = faceDist * 1.35f;     // 들이민 자리 — 얼굴이 화면을 거의 채운다
        float backDist = leanDist + 0.32f;     // 젖히며 조금 물러난 자리
        // 박치기 — 렌즈에 거의 닿는다. 0.72배로 뒀더니 카메라가 머리 속으로 들어가 화면에서 통째로 사라졌다.
        float hitDist = faceDist * 0.92f;

        // ⓐ 들이밈 — 0.08초 만에 어둠 속에서 렌즈 앞으로. 충격음 · 흔들림 · 붉은 조명이 이 순간 같이 터진다.
        BeginRedAlert();
        Sfx.Instance?.Play("impact_blunt", 0f, 0.86f);
        Sfx.Instance?.Play("tinnitus", -8f);
        ctl?.ShakeCamera(7f, 0.4f);
        ctl?.SetScreenTint(new Color(1.6f, 0.25f, 0.2f));
        AmbientOverlay.Instance?.PulseNoise(0.5f);
        CCTVMonitorView.Instance?.FlashGlitch(1f);
        await Sweep(0.08, u =>
        {
            float e = u * u;
            Pose(Mathf.Lerp(FarDist, leanDist, e), Mathf.Lerp(1f, FaceScale, e));
            flash.LightEnergy = Mathf.Lerp(0.2f, 0.9f, e);
        });
        // 얼굴이 딱 보이게 잠깐 멈춘다 — 미세하게 떨면서.
        await Sweep(0.40, u =>
        {
            Pose(leanDist, FaceScale, Mathf.Sin(u * 40f) * 1.2f);
            flash.LightEnergy = 0.9f;
        });

        // ⓑ 고개를 뒤로 젖힌다 — 박치기 전의 뜸. 숨을 들이켜는 듯한 소리.
        Sfx.Instance?.Play("whisper_reverse", -2f, 0.78f);
        Sfx.Instance?.Play("breath_close", -4f, 0.8f);
        await Sweep(0.45, u =>
        {
            float e = 1f - (1f - u) * (1f - u);
            Pose(Mathf.Lerp(leanDist, backDist, e), FaceScale, 0f, Mathf.Lerp(0f, 34f, e));
            flash.LightEnergy = Mathf.Lerp(0.9f, 0.6f, e);
        });
        await Sweep(0.14, _ => Pose(backDist, FaceScale, 0f, 34f));

        // ⓒ 박치기 — 0.06초. 젖혔던 몸이 앞으로 꺾이며 얼굴이 렌즈에 박힌다.
        Sfx.Instance?.Play("head_impact", 2f, 0.82f);
        Sfx.Instance?.Play("impact_blunt", 2f, 0.62f);
        ctl?.ShakeCamera(15f, 0.75f);
        ctl?.SetScreenTint(new Color(2.0f, 0.18f, 0.14f));
        AmbientOverlay.Instance?.PulseNoise(1f);
        CCTVMonitorView.Instance?.FlashGlitch(1f);
        await Sweep(0.06, u =>
        {
            float e = u * u;
            Pose(Mathf.Lerp(backDist, hitDist, e), Mathf.Lerp(FaceScale, FaceScale * 1.1f, e), 0f,
                Mathf.Lerp(34f, 0f, e));
            flash.LightEnergy = 1.1f;
        });

        // ⓓ 기괴한 비명 여러 개 — 얼굴이 박힌 그 자리에서. 화면에 붙인 채 떨게 둔다.
        Sfx.Instance?.PlayGhostScreamChorus();
        await Sweep(0.55, u =>
        {
            Pose(hitDist, FaceScale * 1.1f, Mathf.Sin(u * 55f) * 2.5f);
            flash.LightEnergy = Mathf.Lerp(1.1f, 0.5f, u);
        });
    }

    // ── 얼굴 오버레이 ───────────────────────────────────────────────
    //
    // 괴물 메시만 전용 레이어(20)로 옮기고, 같은 월드를 보는 투명 배경 SubViewport 카메라가 그
    // 레이어만 찍어 화면 위(CanvasLayer 105 — 아날로그 필터 119 · 암전 160 아래)에 겹친다.
    // 카메라는 매 프레임 관리자 카메라와 같은 자리 · 같은 화각으로 맞춘다(흔들림까지 따라간다).
    private const uint FaceLayer = 1u << 19;
    private SubViewport _faceVp;
    private Camera3D _faceCam;
    private Camera3D _mainCam;
    private Transform3D? _poseView;   // Pose 가 괴물을 세울 때 기준으로 삼은 카메라 자리
    private CanvasLayer _faceLayer;
    private OmniLight3D _faceLight;

    private void BeginFaceOverlay(Camera3D cam)
    {
        if (_faceVp != null || cam == null) return;
        _mainCam = cam;
        _actor.SetRenderLayers(FaceLayer);
        var main = GetViewport();
        var size = (Vector2I)main.GetVisibleRect().Size;
        _faceVp = new SubViewport
        {
            TransparentBg = true,
            Size = new Vector2I(Mathf.Max(64, size.X), Mathf.Max(64, size.Y)),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            World3D = main.World3D,
        };
        _faceCam = new Camera3D
        {
            CullMask = FaceLayer,
            Current = true,
            // 배경은 비우고, 어둠 속에서도 얼굴 윤곽이 읽힐 만큼만 은은한 빛을 준다.
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.62f, 0.56f, 0.60f),
                AmbientLightEnergy = 1.6f,
            },
        };
        _faceVp.AddChild(_faceCam);
        // 얼굴만 비추는 빛 — 괴물 레이어에만 닿는다(방 조명은 그대로). 위에서 비스듬히 내려와 윤곽이 선다.
        _faceLight = new OmniLight3D
        {
            LightCullMask = FaceLayer, LightColor = new Color(0.86f, 0.84f, 0.92f),
            OmniRange = 3.2f, LightEnergy = 2.0f, ShadowEnabled = false,
        };
        AddChild(_faceLight);
        AddChild(_faceVp);
        _faceLayer = new CanvasLayer { Layer = 105 };
        var rect = new TextureRect
        {
            Texture = _faceVp.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _faceLayer.AddChild(rect);
        AddChild(_faceLayer);
        SyncFaceCamera();
    }

    private void SyncFaceCamera()
    {
        if (_faceCam == null || _mainCam == null || !IsInstanceValid(_mainCam)) return;
        // 괴물을 세운 그 카메라 자리로 찍는다. 흔들림(최대 15°)은 매 프레임 새로 뽑혀서, 지금 자리로
        // 찍으면 한 프레임 전 기준으로 선 얼굴이 화면 밖으로 튀었다 — 얼굴은 렌즈에 붙고 방만 흔들린다.
        _faceCam.GlobalTransform = _poseView ?? _mainCam.GlobalTransform;
        _faceCam.Fov = _mainCam.Fov;
        _faceCam.Near = Mathf.Min(_mainCam.Near, 0.02f);   // 렌즈에 붙은 얼굴이 잘리지 않게
        _faceCam.Far = _mainCam.Far;
        _faceCam.HOffset = _mainCam.HOffset;
        _faceCam.VOffset = _mainCam.VOffset;
        if (_faceLight != null)
        {
            var c = _poseView ?? _mainCam.GlobalTransform;
            _faceLight.GlobalPosition = c.Origin + Vector3.Up * 0.45f + c.Basis.X * 0.22f - c.Basis.Z * 0.05f;
        }
    }

    private void EndFaceOverlay()
    {
        _faceLayer?.QueueFree();
        _faceVp?.QueueFree();
        _faceLight?.QueueFree();
        _faceLight = null;
        _faceLayer = null;
        _faceVp = null;
        _faceCam = null;
        _poseView = null;
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

    // ── 붉은 비상 조명 ──────────────────────────────────────────────
    //
    // 차폐가 뚫려 **이 방 안에** 괴물이 들어온 동안, 중앙제어실이 붉게 물든다.
    //
    // 이 방에서 실제로 보이는 광원은 모니터 두 대의 화면빛(주광)과 벽의 비상등뿐이다 —
    // 천장등 묶음은 평소 숨겨져 있다(ControlRoom3DHorror 주석). 그래서 둘을 같이 돌린다:
    // 파란 화면빛을 붉은쪽으로 꺾고, 꺼져 있던 비상등을 방 전체에 닿게 켠다.
    // 화면빛만 붉히면 "모니터가 붉어졌다" 로 읽히고, 비상등만 켜면 벽 한 귀퉁이만 붉다.
    //
    // 평소 조명 제어(ControlRoom3DHorror.TickRoomLighting)는 매 프레임 파란색으로
    // 되돌리므로 먼저 손을 떼게 한다 — 엔딩 연출이 쓰는 것과 같은 스위치다.
    private static readonly Color AlertRed = new(1f, 0.10f, 0.06f);

    private Light3D[] _roomLights = System.Array.Empty<Light3D>();
    private float[] _roomEnergy = System.Array.Empty<float>();
    private Color[] _roomColor = System.Array.Empty<Color>();
    private OmniLight3D _emergency;
    private bool _redOn;
    private float _red;      // 0 → 1, 붉게 물든 정도
    private double _redT;

    private void BeginRedAlert()
    {
        var ctl = ControlRoom3DController.Instance;
        if (ctl == null) return;
        ControlRoom3DHorror.ExternalLightingOverride = true;

        _roomLights = new[]
        {
            ctl.GetNodeOrNull<Light3D>("ControlRoom/Monitor01/M01_ScreenLight"),
            ctl.GetNodeOrNull<Light3D>("ControlRoom/Monitor02/M02_ScreenLight"),
            ctl.GetNodeOrNull<Light3D>("ControlRoom/DeskFillLight"),
        };
        _roomEnergy = new float[_roomLights.Length];
        _roomColor = new Color[_roomLights.Length];
        for (int i = 0; i < _roomLights.Length; i++)
        {
            if (_roomLights[i] == null) continue;
            _roomEnergy[i] = _roomLights[i].LightEnergy;
            _roomColor[i] = _roomLights[i].LightColor;
        }

        _emergency = ctl.GetNodeOrNull<OmniLight3D>("ControlRoom/EmergencyLight");
        if (_emergency != null)
        {
            _emergency.LightColor = AlertRed;
            // 기본 반경(4)은 등이 붙은 벽만 적신다. 책상과 관리자 자리까지 닿게 넓힌다 —
            // 이 판은 여기서 끝나고 씬이 통째로 새로 열리므로 되돌릴 필요가 없다.
            _emergency.OmniRange = 7.6f;
            _emergency.LightEnergy = 0f;
            _emergency.Visible = true;
        }
        _redOn = true;
    }

    public override void _Process(double delta)
    {
        SyncFaceCamera();
        if (!_redOn) return;
        _redT += delta;
        // 0.10초 안에 물든다 — 얼굴이 꽂히는 것과 **같은 순간**이어야 한다.
        // (전에는 0.26초였다. 그때는 돌진보다 먼저 켜 두는 전조였기 때문이다.)
        _red = Mathf.Min(1f, _red + (float)delta / 0.10f);

        // 비상등은 숨을 쉰다. 고정값으로 켜 두면 조명이 아니라 색필터처럼 보인다.
        if (_emergency != null)
        {
            float pulse = 0.76f + 0.24f * Mathf.Sin((float)_redT * 7.4f);
            _emergency.LightEnergy = 2.9f * _red * pulse;
        }

        for (int i = 0; i < _roomLights.Length; i++)
        {
            var l = _roomLights[i];
            if (l == null || !IsInstanceValid(l)) continue;
            l.LightColor = _roomColor[i].Lerp(AlertRed, _red);
            // 붉은 빛은 같은 세기로도 더 번져 보인다. 조금 낮춰 들이민 얼굴이
            // 하얗게 날아가지 않게 둔다(무엇에게 당했는지 보여야 한다).
            l.LightEnergy = _roomEnergy[i] * Mathf.Lerp(1f, 0.74f, _red);
        }
    }

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
        col.AddChild(Lbl($"DAY {GameState.Instance?.CurrentDay ?? 1}, " +
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
        _redOn = false;
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
