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
        Unwire();
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        if (!_wired) Wire();
        var t = _threats?.Current;
        if (t == null) return;

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
        // 복도에 들어섰다 — 이제 어느 쪽인지 들린다. 경고 한 줄은 띄우지 않는다
        // (알려 주는 순간 CCTV 를 돌려 찾을 이유가 사라진다).
        PlayAt(t.Def.SfxStep, -18f, Pan(t), 0.95f);
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
        PlayAt(t.Def.SfxBreach, -2f, Pan(t), 1f);
        AdminDeathDirector.Begin(t);
    }
}
