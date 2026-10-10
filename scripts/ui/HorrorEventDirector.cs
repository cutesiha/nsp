using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Ui;

// 공포 이벤트 오케스트레이터(지시서 §6-5).
//
// 이미 있는 것을 대체하지 않는다. PresenceDirector(무해한 형체) · HorrorAudioDirector
// (랜덤 큐) · GhostHauntSystem(작업실 귀신) · HorrorDirector(L2/L3) 는 그대로 돈다.
// 여기서 맡는 것은 **그들이 다루지 않는 세 가지**와, 서로 겹치지 않게 하는 규칙이다.
//
//   · CCTV 점프스케어(§6-4)   — 죽지 않는 강한 충격형
//   · 방향이 있는 기척(§6-3)  — 왼쪽 속삭임 · 문 밖 금속 긁힘 · 복도 끝 아기 웃음
//   · 비정상 전화 3종(§6-2)   — 신호 왜곡 · 발신자 미상 · 무응답
//
// 불변 규칙
//   ① 실제 판정에 손대지 않는다. 코어 · 직원 · 증거 · 사고 어디에도 쓰지 않는다.
//   ② 진짜 위협(복도 괴물)이 돌고 있으면 전부 입을 닫는다 — 연출이 판정을 가린다.
//   ③ 강한 연출 뒤에는 긴 전역 쿨다운. 연달아 두 번 터지지 않는다.
//   ④ 입력을 가로챌 수 있는 순간(전화 · 심문 · 스토리 · 수리 미로 · 근무 종료)에는
//      아무것도 하지 않는다.
//   ⑤ DAY0(가상 시뮬레이션)에는 하나도 나오지 않는다.
public partial class HorrorEventDirector : Node
{
    public static HorrorEventDirector Instance { get; private set; }

    public enum Kind
    {
        CctvScare,      // 충격형 — CCTV 를 괴물 얼굴이 채운다
        Whisper,        // 환경형 — 왼쪽 귓가의 속삭임
        DoorScratch,    // 환경형 — 문 바로 밖의 금속 긁힘
        BabyLaugh,      // 환경형 — 복도 끝의 웃음
        CallWarp,       // 전화 ① — 통화 중인 직원 목소리가 서서히 비틀린다
        CallGhost,      // 전화 ② — 발신자 미상
        CallSilent,     // 전화 ③ — 무응답
    }

    private sealed class Def
    {
        public Kind Kind;
        public float Weight = 1f;
        public int FromDay = 1;
        public int MaxPerShift = 1;
        public float SelfCooldown = 90f;   // 같은 종류를 다시 쓰기까지
        public bool Strong;                // 강한 연출 — 전역 긴 쿨다운을 건다
    }

    // 초기 데이터. 전부 보수적이다 — 한 근무(약 2분)에 많아야 한두 번이다.
    private static readonly Def[] Table =
    {
        new() { Kind = Kind.CctvScare,   Weight = 1.0f, FromDay = 2, MaxPerShift = 1, SelfCooldown = 999f, Strong = true },
        new() { Kind = Kind.Whisper,     Weight = 1.6f, FromDay = 1, MaxPerShift = 2, SelfCooldown = 55f },
        new() { Kind = Kind.DoorScratch, Weight = 1.2f, FromDay = 1, MaxPerShift = 2, SelfCooldown = 55f },
        new() { Kind = Kind.BabyLaugh,   Weight = 0.9f, FromDay = 2, MaxPerShift = 1, SelfCooldown = 999f },
        new() { Kind = Kind.CallWarp,    Weight = 0.8f, FromDay = 2, MaxPerShift = 1, SelfCooldown = 999f },
        new() { Kind = Kind.CallGhost,   Weight = 0.7f, FromDay = 2, MaxPerShift = 1, SelfCooldown = 999f, Strong = true },
        new() { Kind = Kind.CallSilent,  Weight = 0.7f, FromDay = 1, MaxPerShift = 1, SelfCooldown = 999f },
    };

    // 근무 시작 후 이만큼은 조용하다 — 배치를 확인할 시간을 준다.
    private const float FirstEventAfter = 34f;
    // 어떤 이벤트든 사이 간격. 강한 것 뒤에는 StrongGap 이 추가로 붙는다.
    private const float MinGap = 38f;
    private const float StrongGap = 70f;

    // 끄기(테스트 · 접근성). 켜 두면 자동 발동이 멈추고 ForceFire 만 듣는다.
    public bool AutoDisabled { get; set; }

    // ── 전역 정숙 구간(§6-5 상호 배제) ──────────────────────────────
    //
    // 강한 연출 하나가 끝난 직후에는 **다른 공포 계통도** 잠깐 입을 닫는다.
    // PresenceDirector(무해한 형체) · HorrorAudioDirector(랜덤 큐) 가 이 값을 읽는다.
    // 기존 기능을 지우지 않고 "지금은 비켜" 한마디만 거는 방식이다.
    private static double _quietUntilMsec;

    public static bool QuietNow => Time.GetTicksMsec() < _quietUntilMsec;

    public static void Hush(double seconds)
        => _quietUntilMsec = Mathf.Max(_quietUntilMsec, Time.GetTicksMsec() + seconds * 1000.0);

    public static void ClearHushForTest() => _quietUntilMsec = 0;

    public event Action<Kind> Fired;
    public int FiredThisShift { get; private set; }
    public Kind? LastFired { get; private set; }

    private readonly Dictionary<Kind, int> _count = new();
    private readonly Dictionary<Kind, float> _lastAt = new();
    private int _day = -1;
    private float _clock;
    private float _nextAt;
    private bool _busy;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Pausable;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 근무가 바뀌면 하루치 집계를 비운다.
    public void ResetShift()
    {
        _count.Clear();
        _lastAt.Clear();
        FiredThisShift = 0;
        LastFired = null;
        _clock = 0f;
        _nextAt = FirstEventAfter;
        _busy = false;
        _warp = 0f;
        _warpArmed = false;
        if (Sfx.Instance != null) Sfx.Instance.VoiceWarp = 0f;
    }

    public override void _Process(double delta)
    {
        var gs = GameState.Instance;
        if (gs == null) return;

        if (gs.CurrentDay != _day)
        {
            _day = gs.CurrentDay;
            ResetShift();
        }

        if (gs.CurrentPhase != GamePhase.Live)
        {
            _clock = 0f;
            _nextAt = FirstEventAfter;
            // 근무가 끝나는 순간 왜곡이 걸려 있었다면 그대로 굳지 않게 지운다.
            if (_warp > 0f || _warpArmed)
            {
                _warp = 0f;
                _warpArmed = false;
                if (Sfx.Instance != null) Sfx.Instance.VoiceWarp = 0f;
            }
            return;
        }

        _clock += (float)delta;
        TickWarp((float)delta);

        if (AutoDisabled || _busy || _clock < _nextAt) return;
        if (!Safe()) return;

        var def = Pick();
        if (def == null) { _nextAt = _clock + MinGap; return; }
        Fire(def);
    }

    // ── 발동 가능한가 ────────────────────────────────────────────────

    // 여기서 걸러지는 순간에는 **아무 소리도, 아무 그림도** 내지 않는다.
    public bool Safe()
    {
        var gs = GameState.Instance;
        if (gs == null || gs.CurrentPhase != GamePhase.Live) return false;
        // DAY0 교육일은 전부 제외(§8 "기본 모드/DAY0 진행이 막히지 않을 것").
        if (DayFeatures.IsTutorialDay) return false;

        // 진짜 위협이 돌고 있으면 연출이 판정을 가린다 — 복도 괴물이 우선이다.
        var t = FacilitySimulation.Instance?.Threats?.Current;
        if (t != null && t.Active) return false;
        if (AdminDeathDirector.IsPlaying) return false;

        // 전화 · 스토리 · 컷신 · 암전 · 수리 미로 — 입력을 가로챌 수 있는 순간.
        if (Phone3D.Instance?.IsBusy == true) return false;
        if (PhoneCallHud.Instance?.IsOpen == true) return false;
        if (NSP.Prologue.CutscenePlayer.Instance?.IsPlaying == true) return false;
        if (NSP.Prologue.PrologueDirector.Instance?.IsRunning == true) return false;
        if (EndingDirector.IsPlaying) return false;
        if (BlinkOverlay.Instance?.IsClosed == true) return false;
        if (StoryTransition.Active) return false;
        if (StoryCutinDirector.PausesGameplay) return false;
        if (RepairApprovalSystem.Busy) return false;

        // 지금 **진짜 전화가 올 수 있는 상황**이면 전화기를 비워 둔다. 가짜 벨이
        // 울리는 동안 사고 신고 전화가 큐에서 밀리면 연출이 판정을 가린 셈이 된다
        // (§6-2 "중요한 직원의 구조 요청/기절 전화를 덮어 쓰지 말 것").
        if (FacilitySimulation.Instance?.Warnings.Active.Count > 0) return false;
        if (IncidentTracker.ActiveCount > 0) return false;

        // 다른 공포 디렉터가 화면이나 귀를 쥐고 있는 동안은 비켜선다(§6-5 상호 배제).
        if (HorrorDirector.Instance?.CustomEventActive == true) return false;
        if (Sfx.Instance is { HorrorPlaying: true }) return false;
        if (FacilitySimulation.Instance?.Ghost is { Active: true }) return false;
        if (QuietNow) return false;
        return true;
    }

    // ── 검사용 ──────────────────────────────────────────────────────
    // 자동 발동을 기다리지 않고 지금 뽑히는 것을 확인한다(§6-5 "재현 가능하게").
    public Kind? PickForTest() => Pick()?.Kind;
    public void SetClockForTest(float seconds) => _clock = seconds;
    public int FiredCountForTest(Kind k) => _count.GetValueOrDefault(k);
    public bool BusyForTest => _busy;
    public static IEnumerable<Kind> AllKinds => Table.Select(d => d.Kind);
    public static bool IsStrong(Kind k) => Table.First(d => d.Kind == k).Strong;
    public static int FromDayOf(Kind k) => Table.First(d => d.Kind == k).FromDay;
    public static int MaxPerShiftOf(Kind k) => Table.First(d => d.Kind == k).MaxPerShift;

    private Def Pick()
    {
        int day = GameState.Instance?.CurrentDay ?? 1;
        var pool = Table.Where(d =>
            day >= d.FromDay
            && _count.GetValueOrDefault(d.Kind) < d.MaxPerShift
            && _clock - _lastAt.GetValueOrDefault(d.Kind, -9999f) >= d.SelfCooldown
            && Possible(d.Kind)).ToList();
        if (pool.Count == 0) return null;

        float total = pool.Sum(d => d.Weight);
        float r = GD.Randf() * total;
        foreach (var d in pool)
        {
            r -= d.Weight;
            if (r <= 0f) return d;
        }
        return pool[^1];
    }

    // 종류마다 "지금 이것이 말이 되는가".
    private static bool Possible(Kind k) => k switch
    {
        // 괴물 모델이 하나도 없으면 점프스케어도 없다.
        Kind.CctvScare => MonsterThreatSystem.Monsters.Count > 0,
        // 통화가 없으면 왜곡할 목소리도 없다 — 통화 중에만 걸리므로 자동 발동에서는 뺀다.
        Kind.CallWarp => false,
        Kind.CallGhost or Kind.CallSilent => Phone3D.Instance?.IsBusy == false,
        _ => true,
    };

    // ── 실제 연출 ────────────────────────────────────────────────────

    public bool ForceFire(Kind kind)
    {
        var def = Table.FirstOrDefault(d => d.Kind == kind);
        if (def == null || _busy) return false;
        Fire(def);
        return true;
    }

    private void Fire(Def def)
    {
        _count[def.Kind] = _count.GetValueOrDefault(def.Kind) + 1;
        _lastAt[def.Kind] = _clock;
        FiredThisShift++;
        LastFired = def.Kind;
        _nextAt = _clock + MinGap + (def.Strong ? StrongGap : 0f);

        switch (def.Kind)
        {
            case Kind.CctvScare: _ = RunCctvScare(); break;
            case Kind.Whisper: Cue("whisper_short", -21f, -0.78f, 0.98f); break;
            case Kind.DoorScratch: Cue("door_scratch", -19f, 0.34f, 0.92f); break;
            case Kind.BabyLaugh: Cue("whisper_reverse", -22f, 0.62f, 1.22f); break;
            case Kind.CallWarp: BeginWarp(); break;
            case Kind.CallGhost: Hush(10.0); Phone3D.Instance?.RingAnomaly(AnomalyCallDirector.KindGhost); break;
            case Kind.CallSilent: Phone3D.Instance?.RingAnomaly(AnomalyCallDirector.KindSilent); break;
        }
        Fired?.Invoke(def.Kind);
    }

    // 방향이 있는 기척 — 전용 공포 버스로만 나간다(게임 정보음과 섞이지 않게).
    private static void Cue(string key, float db, float pan, float pitch)
        => Sfx.Instance?.PlayHorror(key, db, pan, pitch);

    // CCTV 점프스케어 — 0.2~0.7초 + 1~2초 노이즈 잔향. 셋 다 모델 · 각도 · 거리 ·
    // 노이즈 세기가 다르다. 같은 그림을 하루에 두 번 쓰지 않는다(MaxPerShift = 1).
    private sealed record ScareShot(string MonsterId, float Distance, float Sideways,
        float Yaw, float HoldSeconds, float Glitch, float Tail);

    private static readonly ScareShot[] Shots =
    {
        new("absentee", 0.82f, 0.00f, 4f, 0.26f, 1.00f, 1.7f),
        new("infant", 0.58f, 0.16f, -22f, 0.42f, 0.78f, 1.2f),
        new("spider", 0.74f, -0.20f, 196f, 0.64f, 0.90f, 2.0f),
    };

    private async Task RunCctvScare()
    {
        _busy = true;
        var world = FacilityCctvWorld.Instance;
        var shot = Shots[(int)(GD.Randi() % (uint)Shots.Length)];
        var def = MonsterThreatSystem.Monsters.FirstOrDefault(m => m.MonsterId == shot.MonsterId);

        if (world != null && def != null)
        {
            // 얼굴이 화면 한가운데 오도록 — 키의 0.9 지점을 시선 축에 맞춘다.
            const float headRatio = 0.90f;
            string clip = !string.IsNullOrEmpty(def.AnimJumpscare) ? def.AnimJumpscare
                : !string.IsNullOrEmpty(def.AnimRun) ? def.AnimRun : def.AnimIdle;

            CCTVMonitorView.Instance?.FlashGlitch(shot.Glitch);
            Sfx.Instance?.PlayHorror("cctv_cut", -8f, 0f, 0.92f);
            world.ShowScare(def, clip, shot.Distance, shot.Sideways, shot.Yaw, headRatio);
            Sfx.Instance?.PlayGhostScreamDistant(-12f);

            await Wait(shot.HoldSeconds);
            world.HideScare();
            // 잔향 — 화면이 바로 멀쩡해지면 "봤나?" 가 아니라 "버그인가?" 가 된다.
            CCTVMonitorView.Instance?.FlashGlitch(shot.Tail * 0.4f);
            Hush(13.0);   // 큰 것 뒤에 작은 연출이 겹치지 않게(§6-5)
            AdminFearDirector.Instance?.Burst(0.75f, 4f);
            await Wait(shot.Tail);
        }
        _busy = false;
    }

    // ── 전화 ① 신호 왜곡 ─────────────────────────────────────────────
    //
    // 통화 **중에만** 의미가 있다. 걸려 있는 동안 서서히 0 → 1 로 올라가고,
    // 통화가 끝나면 즉시 0 으로 돌아간다. 소리만 바뀐다(Sfx.VoiceWarp 주석 참고).
    private float _warp;
    private bool _warpArmed;

    private void BeginWarp() => _warpArmed = true;

    public float WarpForTest => _warp;

    private void TickWarp(float delta)
    {
        var sfx = Sfx.Instance;
        if (sfx == null) return;

        bool onCall = PhoneCallHud.Instance?.IsOpen == true
                      && Phone3D.Instance?.AnomalyActive == false;
        if (_warpArmed && onCall) _warp = Mathf.Min(1f, _warp + delta * 0.11f);
        else _warp = Mathf.MoveToward(_warp, 0f, delta * 2.5f);

        if (!onCall && _warp <= 0.001f) _warpArmed = false;
        sfx.VoiceWarp = _warp;
    }

    private async Task Wait(double seconds)
        => await ToSignal(GetTree().CreateTimer(seconds, true, false, true), SceneTreeTimer.SignalName.Timeout);
}
