using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 괴물 위협의 **소리와 반응**을 맡는다. 판정은 하나도 하지 않는다 —
// MonsterThreatSystem 이 낸 신호를 받아 귀와 몸으로 옮길 뿐이다.
//
// 여기가 "공정한 위협" 을 실제로 보장하는 곳이다(지시서 §7).
//   · 출현 구역에서 나오면 **먼 기척** 이 한 번 난다
//   · 복도에 들어서면 그쪽 방향으로 치우친 발소리가 간격을 좁혀 가며 들린다
//   · CCTV 가 꺼져 있어도 이 소리는 그대로 난다 — 영상이 유일한 단서가 되지 않는다
//   · 문 앞에 닿으면 두드림 · 긁힘이 **문 쪽에서** 들린다
//
// 방향은 접근 복도가 중앙제어실의 어느 쪽인지로 정한다(북=가운데, 서=왼쪽, 동=오른쪽).
public partial class MonsterThreatPresenter : Node
{
    public static MonsterThreatPresenter Instance { get; private set; }

    private MonsterThreatSystem _threats;
    private bool _wired;

    // 발소리 간격 — 멀 때는 뜸하게, 가까울수록 촘촘하게.
    private const float StepFar = 1.5f;
    private const float StepNear = 0.62f;
    private double _nextStep;
    private double _nextDistant;

    public override void _Ready()
    {
        Instance = this;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        StopCorridorCry();
        Unwire();
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (!_wired) Wire();
        var t = _threats?.Current;
        if (t == null) { StopCorridorCry(); return; }

        double now = Time.GetTicksMsec() / 1000.0;

        // 외곽 구역에 있는 동안 — 멀리서 한 번씩. 어디로 올지는 아직 알 수 없다.
        if (t.Phase == ThreatPhase.Outer && now >= _nextDistant)
        {
            _nextDistant = now + 3.4;
            PlayAt(t.Def.SfxDistant, -26f, 0f, 0.92f);
        }

        // 복도를 걸어오는 소리 — **CCTV 가 꺼져 있어도 난다.**
        if (t.Phase is ThreatPhase.Approach or ThreatPhase.Retreat && !t.Paused && now >= _nextStep)
        {
            float close = t.Closeness;
            _nextStep = now + Mathf.Lerp(StepFar, StepNear, close) * (float)GD.RandRange(0.85, 1.15);
            PlayAt(t.Def.SfxStep, Mathf.Lerp(-28f, -11f, close), Pan(t), Mathf.Lerp(0.92f, 1.04f, close));
        }

        TickCorridorCry(t);
    }

    // ── 복도에 있는 동안 계속 나는 울음소리 ──────────────────────────────
    //
    // 복도에 들어선 뒤 문까지 오는 10초 동안, 발소리 사이사이가 조용했다. 그러면
    // "지금 복도에 뭔가 있다" 가 귀로 이어지지 않는다(1013 QA 12번).
    //
    //   · 복도 CCTV 를 **안 보고 있을 때** — 어디선가 거슬리게 들리는 정도(IdleDb)
    //   · 그 복도 CCTV 를 **띄웠을 때**   — 바로 앞에서 나는 것처럼 크게(WatchDb)
    //
    // 음원은 개체별로 다르다(아기 = babymonster, 나머지 = monster). 원음 그대로 쓰지
    // 않고 Warp 버스로 조금 일그러뜨린다 — 그대로 틀면 "녹음을 튼 것" 으로 들린다.
    private const string CryCh = "corridor_cry";
    private const float CryIdleDb = -17f;
    private const float CryWatchDb = -2f;
    private string _cryKey = "";

    private static string CryKeyFor(MonsterDef def) =>
        def?.MonsterId == "infant" ? "babymonster" : "monster";

    private void TickCorridorCry(MonsterThreat t)
    {
        bool inCorridor = t.Phase is ThreatPhase.Approach or ThreatPhase.AtDoor
            or ThreatPhase.Pounding or ThreatPhase.Breach or ThreatPhase.Retreat;
        if (!inCorridor || t.Paused) { StopCorridorCry(); return; }

        var sfx = Sfx.Instance;
        if (sfx == null) return;

        string key = CryKeyFor(t.Def);
        if (_cryKey != key || !sfx.WarpedPlaying(CryCh))
        {
            if (!string.IsNullOrEmpty(_cryKey)) sfx.StopWarped(CryCh);
            _cryKey = key;
            // 아기 쪽은 조금 높게, 형체 · 거미는 낮게 — 같은 버스를 써도 정체가 구분된다.
            sfx.PlayWarped(key, CryIdleDb, key == "babymonster" ? 1.04f : 0.88f, CryCh, loop: true);
        }

        var sim = FacilitySimulation.Instance;
        bool watching = sim != null && sim.SurveillanceCorridorId == t.SegmentId
                        && (GameState.Instance?.IsCctvOperational() ?? false) && sim.CctvFeedLive;
        sfx.FadeWarped(CryCh, watching ? CryWatchDb : CryIdleDb, 0.35f);
    }

    private void StopCorridorCry()
    {
        if (string.IsNullOrEmpty(_cryKey)) return;
        _cryKey = "";
        Sfx.Instance?.StopWarped(CryCh, 0.6f);
    }

    private void Wire()
    {
        _threats = FacilitySimulation.Instance?.Threats;
        if (_threats == null) return;
        _wired = true;
        _threats.Spawned += OnSpawned;
        _threats.EnteredCorridor += OnEntered;
        _threats.ReachedDoor += OnReachedDoor;
        _threats.Pounded += OnPounded;
        _threats.Retreated += OnRetreated;
        _threats.Breached += OnBreached;
    }

    private void Unwire()
    {
        if (!_wired || _threats == null) return;
        _wired = false;
        _threats.Spawned -= OnSpawned;
        _threats.EnteredCorridor -= OnEntered;
        _threats.ReachedDoor -= OnReachedDoor;
        _threats.Pounded -= OnPounded;
        _threats.Retreated -= OnRetreated;
        _threats.Breached -= OnBreached;
    }

    // 중앙제어실에서 본 방향 → 스테레오 좌우. 이어폰이면 어느 쪽인지 바로 안다.
    private static float Pan(MonsterThreat t)
    {
        var seg = FacilitySimulation.Instance?.Corridors.ById(t.SegmentId);
        return seg?.Side switch
        {
            CorridorSide.West => -0.75f,
            CorridorSide.East => 0.75f,
            _ => 0f,
        };
    }

    private static void PlayAt(string key, float db, float pan, float pitch)
    {
        if (string.IsNullOrEmpty(key)) return;
        // Horror 버스는 패닝이 걸려 있다 — 좌우를 만들 수 있는 유일한 길이다.
        if (Mathf.Abs(pan) > 0.05f) Sfx.Instance?.PlayHorror(key, db, pan, pitch);
        else Sfx.Instance?.Play(key, db, pitch);
    }

    // ── 신호 ────────────────────────────────────────────────────────

    private void OnSpawned(MonsterThreat t)
    {
        _nextDistant = 0;
        _nextStep = 0;
        PlayAt(t.Def.SfxDistant, -22f, 0f, 0.9f);
    }

    private void OnEntered(MonsterThreat t)
    {
        // 복도에 들어섰다. **여기서부터 10초다.**
        //
        // 전에는 발소리 한 번(-18dB)만 났다. 조용한 편이 무섭긴 하지만, 접근 시간이
        // 10초로 줄어든 지금은 그 순간을 놓치면 아무것도 해 볼 수 없다. 그래서 전조는
        // 놓칠 수 없게 만든다 — 다만 **어느 복도인지는 여전히 말하지 않는다.**
        // 방향은 소리의 좌우(Pan)와 CCTV 로 찾는 것이 이 게임의 조작이다.
        PlayAt(t.Def.SfxStep, -6f, Pan(t), 0.95f);
        PlayAt(t.Def.SfxDistant, -10f, Pan(t), 0.8f);

        // 제어실 조명이 한 번 흔들리고, 화면에 잡음이 지나가고, 숨이 가빠진다.
        // 셋 다 이미 있는 연출이다 — 새 UI 를 띄우지 않고 방 자체가 알려 준다.
        RoomEffectStats.LightFlickerRequested?.Invoke();
        CCTVMonitorView.Instance?.FlashGlitch(0.45f);
        ControlRoom3DController.Instance?.ShakeCamera(0.3f, 0.25f);
        AdminFearDirector.Instance?.Burst(0.55f, 2.2f);
    }

    private void OnReachedDoor(MonsterThreat t)
    {
        var seg = FacilitySimulation.Instance?.Corridors.ById(t.SegmentId);
        PlayAt(t.Def.SfxScratch, -8f, Pan(t), 1f);
        // 문 앞에 왔다는 것만은 반드시 전해져야 한다 — 열려 있으면 여기가 마지막 기회다.
        AdminFearDirector.Instance?.Burst(seg is { Sealed: true } ? 0.8f : 0.95f,
            Mathf.Max(2f, t.Def.BreachDelaySeconds));
    }

    private void OnPounded(MonsterThreat t)
    {
        PlayAt(t.Def.SfxPound, -4f, Pan(t), (float)GD.RandRange(0.92, 1.08));
        // 문짝이 그 충격에 짧게 밀렸다 돌아온다 — "막혀 있다" 가 눈으로 보여야 한다.
        FacilityCctvWorld.Instance?.PoundCorridorDoor(t.SegmentId);
        // 문과 장치가 미세하게 흔들린다. 과한 파괴 연출은 쓰지 않는다.
        ControlRoom3DController.Instance?.ShakeCamera(0.45f, 0.18f);
        CCTVMonitorView.Instance?.FlashGlitch(0.25f);
    }

    private void OnRetreated(MonsterThreat t)
    {
        PlayAt(t.Def.SfxScratch, -14f, Pan(t), 0.85f);
    }

    private void OnBreached(MonsterThreat t)
    {
        StopCorridorCry();
        PlayAt(t.Def.SfxBreach, -2f, Pan(t), 1f);
        AdminDeathDirector.Begin(t);
    }
}
