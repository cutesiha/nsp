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
//   A 반사   전원이 꺼진 듯 어두운 모니터 표면에 뒤에 선 무언가가 0.35초 비친다
//   B 문창   뒤쪽 출입문의 작은 창을 사람 같은 그림자가 지나간다(0.6초, 좌→우)
//   C 복귀   조명이 다시 켜지는 한두 프레임 동안 모니터 사이에 형체가 있다
//   D 카메라 아무도 배치하지 않은 작업실 CCTV 구석에 형체가 한 프레임 잡힌다
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
    private PresenceOverlay _overlay;

    public override void _Ready()
    {
        Instance = this;
        _rng.Randomize();
        _overlay = new PresenceOverlay();
        AddChild(_overlay);
        // 조명이 돌아오는 순간의 형체(C)는 깜빡임 신호에 얹는다.
        RoomEffectStats.LightFlickerRequested += OnFlicker;
    }

    public override void _ExitTree()
    {
        RoomEffectStats.LightFlickerRequested -= OnFlicker;
        if (Instance == this) Instance = null;
    }

    public override void _Process(double delta) => Tick();

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
            case Kind.Reflection: _overlay?.ShowReflection(); break;
            case Kind.DoorWindow: _overlay?.ShowDoorWindow(); break;
            case Kind.LightReturn: _overlay?.ShowLightReturn(); break;
            case Kind.Cctv: FacilityCctvWorld.Instance?.FlashEntity(0.05f); break;
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
