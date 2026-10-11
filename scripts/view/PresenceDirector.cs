using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// '존재' 연출 — 설계 문서 16절.
//
// 정체를 보여 주는 것이 아니라 "뭔가 있었다" 는 느낌만 남긴다. 네 가지다.
//   A 구석   모니터 오른쪽 어둠에 무언가가 0.35초 서 있다
//   B 횡단   중앙제어실 뒤쪽을 가로질러 **빠르게** 지나간다(0.6초)
//   C 복귀   조명이 다시 켜지는 한두 프레임 동안 모니터 사이에 형체가 있다
//   D 카메라 보고 있는 작업실 CCTV 를 무언가가 가로질러 **휙** 지나간다
//
// 넷 다 **실제 개체 모델 셋**(형체 · 작은 것 · 다리 많은 것) 중 하나를 쓴다.
// 예전에는 화면 위에 원 + 사다리꼴을 직접 그렸는데, 그게 "검은 도형이 지나간다" 로
// 보여서 무엇을 봤는지 짐작할 수조차 없었다. 어차피 어둡고 짧아 정체는 드러나지
// 않으므로, 같은 모델을 쓰는 편이 "아까 그거였나" 라는 의심을 남긴다.
//
// 지켜야 할 것(이게 이 연출의 전부다)
//   · 로그에 남지 않는다. EventLog · 증거 카드 · 근무 기억 어디에도 적지 않는다.
//     인터뷰에서 물어도 아무도 모른다 — "나만 본 건가?" 가 핵심이다.
//   · 따라서 방해자 추리에 아무 영향이 없다.
//   · 하루 2~3회, 서로 최소 40초. 근무 중에만, 교육일(DAY0)에는 나오지 않는다.
//
// 카메라 앞으로 튀어나오지 않는다(16절 마지막 문장). 전부 시야 가장자리에서 조용히 지나간다.
public partial class PresenceDirector : Node
{
    public enum Kind { Reflection, DoorWindow, LightReturn, Cctv }

    public static PresenceDirector Instance { get; private set; }

    // 하루 상한(둘 중 하나를 그날 무작위로 고른다) · 사이 간격.
    private const int MinPerDay = 2;
    private const int MaxPerDay = 3;
    private const float MinGapSeconds = 40f;
    // 첫 연출은 근무가 조금 흐른 뒤부터.
    private const float FirstAfterSeconds = 20f;

    // 검사용 — 오늘 몇 번 나왔고 무엇이 나왔는가.
    public int FiredToday { get; private set; }
    public int TargetToday { get; private set; }
    public readonly List<Kind> FiredKinds = new();
    public event Action<Kind> Fired;

    private int _day = -1;
    private float _lastAt = -999f;
    private float _nextAt;
    private readonly RandomNumberGenerator _rng = new();

    // ── 몸 ──────────────────────────────────────────────────────────
    //
    // 중앙제어실 안에 세운다. 방은 x ∈ [-2.2, 2.2] · 뒷벽 z = -2.6 이고 관리자는
    // 원점에서 -Z 를 본다 — 그래서 뒷벽 앞(z ≈ -2.3)이 "모니터 너머 어둠" 이다.
    private MonsterActor _body;
    private OmniLight3D _rim;
    private double _showUntil;
    private double _showFrom;
    private Vector3 _slideA, _slideB;

    private const float BackZ = -2.30f;
    private const float StandY = 0f;

    public override void _Ready()
    {
        Instance = this;
        _rng.Randomize();
        // 조명이 돌아오는 순간의 형체(C)는 깜빡임 신호에 얹는다.
        RoomEffectStats.LightFlickerRequested += OnFlicker;
    }

    // 중앙제어실 3D 노드. PresenceDirector 는 그 형제로 놓여 있다.
    private Node3D Room() => GetParent()?.GetNodeOrNull<Node3D>("ControlRoom");

    private bool EnsureBody()
    {
        if (_body != null) return true;
        var room = Room();
        if (room == null) return false;
        _body = new MonsterActor();
        _body.Attach(room);
        // 아주 약한 뒷빛. 없으면 어두운 뒷벽 앞에서 **아무것도 보이지 않는다.**
        // 얼굴을 드러낼 만큼 비추면 안 된다 — 정체가 보이면 무섭지 않다.
        _rim = new OmniLight3D
        {
            LightColor = new Color(0.55f, 0.60f, 0.72f),
            OmniRange = 2.6f, LightEnergy = 0.35f, ShadowEnabled = false,
        };
        room.AddChild(_rim);
        _rim.Visible = false;
        return true;
    }

    // seconds 동안 a → b 로 지나간다(a == b 면 제자리).
    private void ShowBody(Vector3 a, Vector3 b, float seconds)
    {
        if (!EnsureBody()) return;
        var def = MonsterCast.Pick(GameState.Instance?.CurrentDay ?? 1, _rng);
        if (def == null || !_body.Use(def)) return;

        _slideA = a;
        _slideB = b;
        _showFrom = Time.GetTicksMsec() / 1000.0;
        _showUntil = _showFrom + seconds;
        _body.PlayRole(MonsterCast.MoveClip(def), a.IsEqualApprox(b) ? 1f : 1.6f);
        PlaceBody(0f);
        if (_rim != null) _rim.Visible = true;
    }

    private void PlaceBody(float t)
    {
        Vector3 p = _slideA.Lerp(_slideB, Mathf.Clamp(t, 0f, 1f));
        // 늘 관리자 쪽을 본다 — 지나가는 중에도 고개만은 이쪽이다.
        _body.FaceViewer(p, new Vector3(0f, StandY, 0.2f));
        if (_rim != null) _rim.Position = p + new Vector3(0f, 1.3f, -0.55f);
    }

    private void HideBody()
    {
        _body?.Hide();
        if (_rim != null) _rim.Visible = false;
        _showUntil = 0;
    }

    public override void _ExitTree()
    {
        RoomEffectStats.LightFlickerRequested -= OnFlicker;
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta)
    {
        // 보여 주는 중이면 자리를 잇는다. 끝나면 조용히 지운다.
        if (_showUntil > 0)
        {
            double now = Time.GetTicksMsec() / 1000.0;
            if (now >= _showUntil) HideBody();
            else PlaceBody((float)((now - _showFrom) / Mathf.Max(0.01f, (float)(_showUntil - _showFrom))));
        }
        Tick();
    }

    private void Tick()
    {
        var gs = GameState.Instance;
        if (gs == null) return;
        if (gs.CurrentDay != _day) NewDay(gs.CurrentDay);
        if (!Allowed()) return;

        float now = gs.DayTimeSeconds;
        if (now < _nextAt || FiredToday >= TargetToday) return;
        if (now - _lastAt < MinGapSeconds) return;

        Fire(PickKind());
        _lastAt = now;
        _nextAt = now + MinGapSeconds + _rng.RandfRange(6f, 26f);
    }

    private void NewDay(int day)
    {
        _day = day;
        FiredToday = 0;
        FiredKinds.Clear();
        _lastAt = -999f;
        _nextAt = FirstAfterSeconds + _rng.RandfRange(0f, 18f);
        TargetToday = _rng.RandiRange(MinPerDay, MaxPerDay);
    }

    // 근무 중에만, 교육일에는 나오지 않는다.
    // 강한 공포 연출(§6-4) 직후의 전역 정숙 구간에도 비켜선다 — 큰 것 뒤에 바로
    // 작은 것이 겹치면 둘 다 값이 떨어진다(§6-5 상호 배제).
    public static bool Allowed()
    {
        var gs = GameState.Instance;
        return gs != null && gs.CurrentPhase == GamePhase.Live && !DayFeatures.IsTutorialDay
               && !NSP.Ui.HorrorEventDirector.QuietNow;
    }

    // 지금 상황에서 쓸 수 있는 연출 중 하나.
    private Kind PickKind()
    {
        var pool = new List<Kind> { Kind.Reflection, Kind.DoorWindow };
        // 아무도 없는 방을 보고 있을 때만 카메라 연출이 말이 된다.
        if (WatchingEmptyRoom()) pool.Add(Kind.Cctv);
        // 같은 것이 연달아 나오지 않게.
        if (FiredKinds.Count > 0 && pool.Count > 1) pool.RemoveAll(k => k == FiredKinds[^1]);
        return pool[_rng.RandiRange(0, pool.Count - 1)];
    }

    private static bool WatchingEmptyRoom()
    {
        var sim = FacilitySimulation.Instance;
        string room = sim?.SurveillanceTargetRoomId ?? "";
        if (sim == null || string.IsNullOrEmpty(room)) return false;
        if (CCTVMonitorView.Instance?.FeedVisible != true) return false;
        return sim.OnDutyCount(room) == 0;
    }

    // 조명이 돌아오는 순간 — 낮은 확률로만 형체가 끼어든다.
    private void OnFlicker()
    {
        if (!Allowed() || FiredToday >= TargetToday) return;
        var gs = GameState.Instance;
        if (gs.DayTimeSeconds - _lastAt < MinGapSeconds) return;
        if (_rng.Randf() >= 0.22f) return;
        Fire(Kind.LightReturn);
        _lastAt = gs.DayTimeSeconds;
    }

    // 실제로 한 번 보여 준다. **여기서 아무것도 기록하지 않는다** — 그게 이 연출의 규칙이다.
    public void Fire(Kind kind)
    {
        FiredToday++;
        FiredKinds.Add(kind);
        switch (kind)
        {
            // 오른쪽 어둠 — 문 쪽에 가만히 서 있다가 사라진다.
            case Kind.Reflection:
                ShowBody(new Vector3(1.62f, StandY, BackZ), new Vector3(1.62f, StandY, BackZ), 0.35f);
                break;
            // 뒤쪽을 가로지른다. 빠르다 — 고개를 돌렸을 때는 이미 없다.
            case Kind.DoorWindow:
                ShowBody(new Vector3(-2.05f, StandY, BackZ - 0.1f),
                         new Vector3(2.05f, StandY, BackZ - 0.1f), 0.60f);
                break;
            // 불이 돌아오는 한두 프레임 — 모니터 사이에 있다.
            case Kind.LightReturn:
                ShowBody(new Vector3(0f, StandY, BackZ), new Vector3(0f, StandY, BackZ), 0.06f);
                break;
            case Kind.Cctv: FacilityCctvWorld.Instance?.FlashEntity(0.45f); break;
        }
        Fired?.Invoke(kind);
    }

    // ── 검사용 ────────────────────────────────────────────────────────
    // 하루를 새로 세운다(목표 횟수를 직접 지정).
    public void ResetForTest(int day, int target)
    {
        NewDay(day);
        TargetToday = target;
    }

    // 하루를 새로 세운다(목표 횟수는 평소처럼 뽑는다).
    public void ResetForTestRandomTarget(int day) => NewDay(day);

    // 화면 없이 판정만 한 틱 굴린다.
    public void TickForTest(float delta) => Tick();
}
