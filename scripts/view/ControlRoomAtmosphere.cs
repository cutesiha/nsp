using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 메인 근무화면 전용 Dynamic Ambience. BGM 없음 — 중앙제어실 자체의 기계 소음이 음악처럼
// 느껴지게 한다. 각 Loop는 독립 AudioStreamPlayer이고 게임 상태에 따라 볼륨/피치가 변한다.
//   정상       : 환경음 위주, Dark Drone 매우 작게
//   사고 경고  : 저주파 Drone / 불협 레이어가 커짐
//   금기 전조  : 기계음 Pitch 살짝 down + 형광등/CRT 노이즈 불안정
//   정전       : machinery→fluor→vent→crt→drone 순차 소등, 거의 무음 + 비상 경고음만
//   전력 복구  : machinery→fluor→crt→vent→drone 순으로 하나씩 복귀
// 멜로디/리듬 없음. 음산한 지하 연구시설 기계 소음.
public partial class ControlRoomAtmosphere : Node3D
{
    // 밖에서 상시음을 잠깐 비우거나(Hush) 관리자 호흡을 흔들 때 쓴다.
    // 씬이 내려가면 비워진다 — 다음 씬의 환경음을 엉뚱하게 건드리지 않게.
    public static ControlRoomAtmosphere Instance { get; private set; }

    [Export] public NodePath VentPath = "../ControlRoom/Vent";
    [Export] public NodePath M01ScreenPath = "../ControlRoom/Monitor01/M01_Screen";
    [Export] public NodePath M02ScreenPath = "../ControlRoom/Monitor02/M02_Screen";
    [Export] public NodePath CeilingLightPath = "../ControlRoom/Lights/CeilingFixture";
    [Export] public NodePath ChairPath = "../ControlRoom/Chair/Chair_Seat";
    [Export] public NodePath WallPath = "../ControlRoom/Wall_Back";
    // 신규 환경음 소스 위치.
    [Export] public NodePath ControlPanelPath = "../ControlRoom/ControlPanel";
    // 경고음 · 기기 험이 나는 자리 — 예전 경고 단말기 자리의 관리자 패드.
    [Export] public NodePath AlertTerminalPath = "../ControlRoom/AdminPad";

    // 플레이어 숨소리 기본 볼륨(dB). 긴장 상태에서 이보다 커진다.
    [Export] public float BreathBaseDb = -25f;

    private enum Amb { Off, Normal, Warning, TabooPrecursor, Blackout }

    private class Layer
    {
        public AudioStreamPlayer3D P3;
        public AudioStreamPlayer P2;
        public float NormalDb;
        public float OffDelay;   // 정전 시 이 시간(초) 후 소등
        public float OnDelay;    // 복구 시 이 시간(초) 후 복귀
        public float TgtDb, TgtPitch = 1f;

        public void Apply(float d)
        {
            if (P3 != null)
            {
                P3.VolumeDb = Mathf.MoveToward(P3.VolumeDb, TgtDb, d * 24f);
                P3.PitchScale = Mathf.Lerp(P3.PitchScale, TgtPitch, d * 2.5f);
            }
            if (P2 != null)
            {
                P2.VolumeDb = Mathf.MoveToward(P2.VolumeDb, TgtDb, d * 24f);
                P2.PitchScale = Mathf.Lerp(P2.PitchScale, TgtPitch, d * 2.5f);
            }
        }
    }

    private const float Silent = -60f;

    private Layer _vent, _fluor, _machinery, _drone;
    private readonly List<Layer> _crt = new();
    private readonly List<Layer> _all = new();

    private AudioStreamPlayer3D _chair;
    private AudioStreamPlayer _alarm;

    // Layer 시스템 밖에서 직접 관리하는 신규 상시음.
    //   _electric  : 배전/케이블 계통의 전기 치치직 (electric_crackle_loop)
    //   _sensorWhir: 책상 위 패드 거치대의 상시 구동음 (crt_hum 을 올려 얇은 회전음처럼)
    //   _breath    : 플레이어 본인의 숨소리 (에셋 없음 — 런타임에 필터드 노이즈로 생성)
    private AudioStreamPlayer3D _electric;
    private AudioStreamPlayer3D _sensorWhir;
    private AudioStreamPlayer _breath;
    private float _nextSensorPing = 5f;
    private float _nextElecPop = 3f;

    private Amb _amb = Amb.Off;
    private float _stateT;          // 현재 상태 진입 후 경과
    private float _restoreT = 999f; // 정전 → 복구 전환 후 경과
    private bool _wasBlackout;

    private float _nextOneShot = 12f;
    private float _nextFlicker = 1.5f;
    private bool _ventFaultDown;
    private RoomDangerTier _ventTier = RoomDangerTier.None;

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (NSP.Core.EventLog.Instance != null)
            NSP.Core.EventLog.Instance.EntryLogged -= OnEntryLogged;
        if (_wiredSim != null) _wiredSim.EmployeeKilled -= OnEmployeeKilled;
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        _vent = MakeLoop3D("vent_loop", NodeAt(VentPath), -6f, 0f, 3.5f, 14f, offDelay: 1.2f, onDelay: 2.0f);
        _fluor = MakeLoop3D("fluor_hum", NodeAt(CeilingLightPath), -21f, 0f, 2.2f, 9f, offDelay: 0.5f, onDelay: 0.8f);
        // 천장광 비활성 상태 — 방의 광원은 두 모니터뿐이라 형광등 웅웅 소리도 끈다(레이어는 남겨 둔다).
        _fluor.NormalDb = Silent;
        _machinery = MakeLoop3D("machinery_loop", NodeAt(WallPath), -20f, 0f, 6f, 22f, offDelay: 0.0f, onDelay: 0.0f);
        _crt.Add(MakeLoop3D("crt_hum", NodeAt(M01ScreenPath), -19f, 0f, 1.4f, 4.5f, offDelay: 2.0f, onDelay: 1.6f));
        _crt.Add(MakeLoop3D("crt_hum", NodeAt(M02ScreenPath), -19f, 0f, 1.4f, 4.5f, offDelay: 2.0f, onDelay: 1.6f));

        _drone = new Layer { NormalDb = -34f, OffDelay = 2.8f, OnDelay = 2.6f, TgtDb = Silent };
        _drone.P2 = MakeLoop2D("drone_loop", Silent);
        _all.Add(_drone); // MakeLoop3D 로 만든 레이어는 _all 에 자동 추가됨 — drone 만 수동.

        _chair = new AudioStreamPlayer3D { VolumeDb = -8f, UnitSize = 2f, MaxDistance = 6f, Bus = NSP.Core.GameSettings.BusSfx };
        (NodeAt(ChairPath) ?? (Node3D)this).AddChild(_chair);

        _alarm = MakeLoop2D("alarm", Silent);

        _electric = MakeSimpleLoop3D("electric_crackle_loop", NodeAt(ControlPanelPath), 2.4f, 9f);
        _sensorWhir = MakeSimpleLoop3D("crt_hum", NodeAt(AlertTerminalPath), 1.1f, 3.5f);
        if (_sensorWhir != null) _sensorWhir.PitchScale = 1.5f;
        BuildBreath();

        _nextOneShot = (float)GD.RandRange(6.0, 14.0);
        WireBreathEvents();
    }

    // Layer 목록(_all)에 넣지 않는 단순 3D 루프 — 상태별 볼륨/피치는 _Process 에서 직접 몬다.
    private AudioStreamPlayer3D MakeSimpleLoop3D(string key, Node3D at, float unit, float maxDist)
    {
        var stream = Load(key);
        if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        var p = new AudioStreamPlayer3D
        {
            Stream = stream, VolumeDb = Silent, UnitSize = unit, MaxDistance = maxDist, Bus = NSP.Core.GameSettings.BusSfx,
        };
        (at ?? (Node3D)this).AddChild(p);
        if (stream != null) p.Play();
        return p;
    }

    // 플레이어 숨소리. 녹음 에셋이 없어 런타임에 만든다 — 저역 통과시킨 노이즈에 들숨/날숨
    // 엔벨로프를 씌운 5.4초 루프. 볼륨은 아주 낮게 깔고(_Process 에서 상태별로 조절), 긴장
    // 상황(사고 경고 / 금기 전조 / 정전)에서 조금 커지고 빨라진다.
    private void BuildBreath()
    {
        _breath = new AudioStreamPlayer { Bus = NSP.Core.GameSettings.BusSfx, VolumeDb = Silent, Stream = MakeBreathStream() };
        AddChild(_breath);
        if (_breath.Stream != null) _breath.Play();
    }

    private static AudioStream MakeBreathStream()
    {
        const int rate = 22050;
        const float dur = 5.4f;
        int n = (int)(rate * dur);
        var pcm = new byte[n * 2];
        var rng = new RandomNumberGenerator();
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = BreathEnvelope(t);
            float white = rng.RandfRange(-1f, 1f);
            // 들숨은 조금 밝게(컷오프 높게), 날숨은 어둡게.
            float k = t < 2.2f ? 0.055f : 0.03f;
            lp += k * (white - lp);
            short v = (short)Mathf.Clamp(lp * env * 26000f, -32767f, 32767f);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Stereo = false,
            Data = pcm,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = n,
        };
    }

    // 들숨 0.15~1.5s, 날숨 2.7~4.6s(조금 더 길고 약하게), 나머지는 정적.
    private static float BreathEnvelope(float t)
    {
        float inhale = Bump(t, 0.15f, 1.5f);
        float exhale = Bump(t, 2.7f, 4.6f) * 0.8f;
        return Mathf.Clamp(inhale + exhale, 0f, 1f);
    }

    private static float Bump(float t, float a, float b)
    {
        if (t <= a || t >= b) return 0f;
        return Mathf.Sin((t - a) / (b - a) * Mathf.Pi);
    }

    private Node3D NodeAt(NodePath p) => GetNodeOrNull<Node3D>(p);

    private Layer MakeLoop3D(string key, Node3D at, float normalDb, float startDb, float unit, float maxDist, float offDelay, float onDelay)
    {
        var stream = Load(key);
        if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        var p = new AudioStreamPlayer3D
        {
            Stream = stream, VolumeDb = Silent, UnitSize = unit, MaxDistance = maxDist, Bus = NSP.Core.GameSettings.BusSfx,
        };
        (at ?? (Node3D)this).AddChild(p);
        if (stream != null) p.Play();
        var l = new Layer { P3 = p, NormalDb = normalDb, OffDelay = offDelay, OnDelay = onDelay, TgtDb = Silent };
        _all.Add(l);
        return l;
    }

    private AudioStreamPlayer MakeLoop2D(string key, float startDb)
    {
        var stream = Load(key);
        if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        var p = new AudioStreamPlayer { Stream = stream, VolumeDb = startDb, Bus = NSP.Core.GameSettings.BusSfx };
        AddChild(p);
        if (stream != null) p.Play();
        return p;
    }

    private static AudioStream Load(string key)
    {
        string path = $"res://assets/audio/sfx/{key}.wav";
        return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        var amb = DetermineState();
        if (amb != _amb)
        {
            if (_amb == Amb.Blackout && amb != Amb.Blackout) _restoreT = 0f;
            _amb = amb;
            _stateT = 0f;
        }
        _stateT += d;
        if (_restoreT < 999f) _restoreT += d;

        bool blackout = _amb == Amb.Blackout;
        _wasBlackout = blackout;

        foreach (var l in _all) ComputeTarget(l, blackout);

        // FAIL-02 환기 고장: 정전이 아니어도 환기가 죽어 있으면 vent 를 눌러둔다.
        PollVentFault();
        TickVentStop(d, blackout);

        foreach (var l in _all) l.Apply(d);

        // 비상 경고음: 정전 중에만.
        _alarm.VolumeDb = Mathf.MoveToward(_alarm.VolumeDb, blackout ? -22f : Silent, d * 20f);

        TickNewAmbience(d, blackout);
        TickOneShots(d);
        TickBreathStress(d);
    }

    // 배전 치치직 / 패드 구동음 / 숨소리. Layer 시스템(_all) 밖에서 상태별로 직접 몬다.
    private void TickNewAmbience(float d, bool blackout)
    {
        bool live = _amb != Amb.Off;
        bool tense = _amb is Amb.Warning or Amb.TabooPrecursor;

        // 전기 치치직 — 정전이면 계통이 죽으니 무음. 긴장 상황에서 조금 커진다.
        if (_electric != null)
        {
            float tgt = !live || blackout ? Silent
                      : _amb == Amb.Warning ? -18f
                      : _amb == Amb.TabooPrecursor ? -19f : -25f;
            _electric.VolumeDb = Mathf.MoveToward(_electric.VolumeDb, tgt, d * 14f);

            _nextElecPop -= d;
            if (_nextElecPop <= 0f && live && !blackout)
            {
                _nextElecPop = (float)GD.RandRange(tense ? 1.5 : 3.5, tense ? 5.0 : 10.0);
                PlayOn(NodeAt(ControlPanelPath), "electric_arc", tense ? -16f : -22f);
            }
        }

        // 패드 상시 구동음 — 정전이면 패드도 꺼진다. 금기 전조에는 피치가 살짝 불안정.
        if (_sensorWhir != null)
        {
            float tgt = !live || blackout ? Silent
                      : _amb == Amb.TabooPrecursor ? -22f : -28f;
            _sensorWhir.VolumeDb = Mathf.MoveToward(_sensorWhir.VolumeDb, tgt, d * 14f);
            float pTgt = _amb == Amb.TabooPrecursor ? 1.28f : 1.5f;
            _sensorWhir.PitchScale = Mathf.Lerp(_sensorWhir.PitchScale, pTgt, d * 2f);

            _nextSensorPing -= d;
            if (_nextSensorPing <= 0f && live && !blackout)
            {
                _nextSensorPing = (float)GD.RandRange(4.5, 8.5);
                PlayOn(NodeAt(AlertTerminalPath), "sensor_beep", -30f);
            }
        }

        // 숨소리 — 근무 중에는 계속. 정전에도 죽지 않고 오히려 또렷해진다(고립감).
        //
        // 여기에 **사건 후 잔여 반응**(_breathStress)이 더해진다. 평상시에는 거의 들리지
        // 않고, 기절 · 사망 같은 일이 있은 뒤 몇 초 동안만 또렷해진다(§15 · §16).
        if (_breath != null)
        {
            float tgt = _amb switch
            {
                Amb.Off => Silent,
                Amb.Warning => BreathBaseDb + 5f,
                Amb.TabooPrecursor => BreathBaseDb + 8f,
                Amb.Blackout => BreathBaseDb + 7f,
                _ => BreathBaseDb,
            };
            // 가쁜 호흡은 최대 +9dB 까지. 그 위로 올리면 숨소리가 대사를 덮는다.
            if (_amb != Amb.Off) tgt += _breathStress * 9f;
            _breath.VolumeDb = Mathf.MoveToward(_breath.VolumeDb, tgt, d * 8f);
            float pTgt = _amb is Amb.TabooPrecursor or Amb.Blackout ? 1.18f : _amb == Amb.Warning ? 1.08f : 1f;
            // 빨라지는 쪽도 상한을 둔다 — 1.45 를 넘으면 사람 숨이 아니라 과호흡 효과음이 된다.
            pTgt = Mathf.Min(1.45f, pTgt + _breathStress * 0.3f);
            _breath.PitchScale = Mathf.Lerp(_breath.PitchScale, pTgt, d * 1.5f);
        }
    }

    // ── 관리자 호흡 (지시서 §15 · §16) ────────────────────────────────
    //
    // 호흡은 **지속 상태가 아니라 사건 후 잔여 반응**이다. 계속 헐떡거리면 귀찮아지므로
    // 사건이 나면 한 번 올라갔다가 스스로 가라앉는다.
    //
    //   작은 사고 · 방해공작   : 짧은 숨 한 번      (5~10초)
    //   직원 기절             : 조금 빨라짐         (5~8초)
    //   직원 사망 · 치명적 실패 : 명확한 가쁜 호흡   (10~15초)
    //
    // 새 Stress HUD 를 만들지 않는다(§15) — 기존 _breath 플레이어의 볼륨과 피치만 흔든다.
    // 연속 사건이면 쌓이되 상한이 있다(§16).
    private float _breathStress;          // 0 = 평상시, 1 = 가쁜 호흡
    private float _breathDecayPerSec = 0.1f;
    private const float BreathStressMax = 1f;

    // 사건 하나가 호흡에 남기는 반응. amount 는 0~1, seconds 는 가라앉는 데 걸리는 시간.
    public void AddBreathStress(float amount, float seconds)
    {
        if (amount <= 0f) return;
        _breathStress = Mathf.Min(BreathStressMax, _breathStress + amount);
        // 더 긴 반응이 들어오면 그쪽에 맞춘다 — 짧은 사건이 긴 사건의 여운을 끊지 않게.
        _breathDecayPerSec = Mathf.Min(_breathDecayPerSec, 1f / Mathf.Max(1f, seconds));
    }

    // 큰 충격 직후 — 숨이 짧게 멎었다가 급하게 들이마신다(§15).
    public void GaspNow()
    {
        AddBreathStress(0.75f, 13f);
        NSP.Core.Sfx.Instance?.Play("admin_gasp", -17f);
    }

    // 지금 호흡이 얼마나 가쁜가(검사용).
    public float BreathStress => _breathStress;

    private void TickBreathStress(float d)
    {
        if (_breathStress <= 0f) return;
        _breathStress = Mathf.Max(0f, _breathStress - _breathDecayPerSec * d);
        // 다 가라앉으면 감쇠 속도도 기본으로 돌린다.
        if (_breathStress <= 0f) _breathDecayPerSec = 0.1f;
    }


    // ── 사건 → 호흡 배선 (§15) ───────────────────────────────────────
    //
    // 새 판정을 만들지 않는다 — 이미 화면에 뜬 사건(EventLog)과 사망 신호만 듣는다.
    // 관리자 상태는 실제 게임 상황에만 반응한다.
    private void WireBreathEvents()
    {
        if (_wired) return;
        _wired = true;
        if (NSP.Core.EventLog.Instance != null)
            NSP.Core.EventLog.Instance.EntryLogged += OnEntryLogged;
        _wiredSim = FacilitySimulation.Instance;
        if (_wiredSim != null) _wiredSim.EmployeeKilled += OnEmployeeKilled;
    }

    private bool _wired;
    private FacilitySimulation _wiredSim;

    private void OnEntryLogged()
    {
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        var e = NSP.Core.EventLog.Instance?.GetAllEntries();
        if (e == null || e.Count == 0) return;
        var last = e[^1];
        switch (last.EventType)
        {
            // 직원이 기절했다 — 조금 빨라지고 5~8초 만에 가라앉는다(§16).
            case LogEventType.Neglect when last.Detail == LogDetail.Fainted:
                AddBreathStress(0.42f, 7f);
                break;
            // 작은 사고 · 정전 · CCTV 단절 — 짧은 숨 한 번.
            case LogEventType.TaskFailed:
            case LogEventType.PowerOutage:
            case LogEventType.CctvDisconnect:
                AddBreathStress(0.2f, 6f);
                break;
            // 대형 방해공작 — 5~10초(§16).
            case LogEventType.Sabotage:
                AddBreathStress(0.35f, 9f);
                break;
            // 금기 위반 — 치명적 실패에 가깝다.
            case LogEventType.TabooViolation:
                GaspNow();
                break;
        }
    }

    // 직원 사망 — 10~15초 동안 명확히 가쁜 호흡. 숨이 한 번 멎었다가 들이마신다(§15).
    private void OnEmployeeKilled(string employeeId) => GaspNow();
    private Amb DetermineState()
    {
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return Amb.Off;
        if (GameState.Instance.PowerCapacity == 0) return Amb.Blackout;

        var sim = FacilitySimulation.Instance;
        if (sim != null)
            foreach (var id in sim.GetRoomIds())
            {
                var rs = sim.GetRoomState(id);
                if (rs != null && rs.TabooHoldTimers.Values.Any(v => v > 0f))
                    return Amb.TabooPrecursor;
            }

        var alerts = AlertSystem.Instance?.GetActiveAlerts();
        if (alerts != null && alerts.Count > 0 && alerts[0].Severity != AlertSeverity.Notice)
            return Amb.Warning;

        return Amb.Normal;
    }

    private void ComputeTarget(Layer l, bool blackout)
    {
        if (_amb == Amb.Off)
        {
            l.TgtDb = Silent; l.TgtPitch = 1f;
            return;
        }

        if (blackout)
        {
            l.TgtDb = _stateT > l.OffDelay ? Silent : StateDb(l);
            l.TgtPitch = Mathf.Lerp(1f, 0.6f, Mathf.Clamp(_stateT / 3f, 0f, 1f));
            return;
        }

        // 정전에서 막 복구된 직후: 레이어별 딜레이 후 다시 켜진다.
        if (_restoreT < 4f)
        {
            l.TgtDb = _restoreT > l.OnDelay ? StateDb(l) : Silent;
            l.TgtPitch = StatePitch(l);
            return;
        }

        l.TgtDb = StateDb(l);
        l.TgtPitch = StatePitch(l);
    }

    private float StateDb(Layer l) => _amb switch
    {
        Amb.Warning => l == _drone ? -20f : l == _machinery ? -16f : IsCrt(l) ? -18f : l.NormalDb,
        Amb.TabooPrecursor => l == _drone ? -24f : l == _machinery ? -17f : l == _fluor ? l.NormalDb : IsCrt(l) ? -16f : l.NormalDb,
        _ => l.NormalDb,
    };

    private float StatePitch(Layer l) => _amb switch
    {
        Amb.TabooPrecursor => l == _machinery ? 0.90f : l == _vent ? 0.94f : l == _drone ? 0.95f : 1f,
        Amb.Warning => l == _drone ? 0.98f : 1f,
        _ => 1f,
    };

    private bool IsCrt(Layer l) => _crt.Contains(l);

    private void PollVentFault()
    {
        if (FacilitySimulation.Instance == null) return;
        var tier = RoomStatusText.GetDangerTier("vent_room");
        if (tier == _ventTier) return;
        _ventTier = tier;
        if (tier == RoomDangerTier.Failure) BeginVentStop();
        else BeginVentRestart();
    }

    // ── 환풍기 정지 연출 ──────────────────────────────────────────────
    //
    // 설계 문서 13절: 팬이 느려짐 → 피치 감소 → 정지 → **갑자기 공간이 너무 조용해짐**.
    // 예전에는 볼륨만 -42dB 로 깎아, 소리가 그냥 작아질 뿐 "멎었다" 는 느낌이 없었다.
    // 마지막 정적이 이 연출의 핵심이다 — 환풍기가 멎은 자리를 다른 상시음까지 함께 비워 준다.
    private const float VentSpinDownSeconds = 3.0f;   // 다 느려지는 데 걸리는 시간
    private const float VentStoppedPitch = 0.35f;     // 멎기 직전의 회전(피치)
    private const float HushSeconds = 2.5f;           // 멎은 뒤 공간이 조용해져 있는 시간
    private const float HushDb = 6f;                  // 그동안 다른 상시음을 낮추는 폭

    private float _ventDownT = -1f;   // 정지 연출 경과(초). 음수 = 진행 중 아님
    private float _hushT = -1f;       // 정적 경과(초). 음수 = 진행 중 아님
    private float _hushSeconds = HushSeconds;   // 이번 정적의 길이(정전은 더 짧다)
    private bool _hushDone;           // 이번 정지에서 정적을 이미 한 번 썼는가

    private void BeginVentStop()
    {
        if (_ventFaultDown) return;
        _ventFaultDown = true;
        _ventDownT = 0f;
        _hushT = -1f;
        _hushDone = false;
        PlayOn(_vent.P3?.GetParentNode3D(), "vent_stop", -4f);
    }

    private void BeginVentRestart()
    {
        if (!_ventFaultDown) return;
        _ventFaultDown = false;
        _ventDownT = -1f;
        _hushT = -1f;
        _hushDone = false;
        _vent.TgtPitch = 1f;
        PlayOn(_vent.P3?.GetParentNode3D(), "vent_restart", -3f);
    }

    // 밖에서 정적만 따로 걸고 싶을 때(내 방 정전 등). 환풍기 정지와 같은 연출을 쓴다.
    public void Hush(float seconds)
    {
        _hushT = 0f;
        _hushSeconds = Mathf.Max(0.2f, seconds);
    }

    // 지금 정적이 걸려 있는가(검사용).
    public bool VentHushActive => _hushT >= 0f;
    public bool VentStopping => _ventFaultDown;
    public float VentPitchTarget => _vent?.TgtPitch ?? 1f;

    private void TickVentStop(float d, bool blackout)
    {
        if (_ventDownT >= 0f) _ventDownT += d;
        if (_hushT >= 0f) _hushT += d;

        if (_ventFaultDown && !blackout)
        {
            float u = _ventDownT < 0f ? 1f : Mathf.Clamp(_ventDownT / VentSpinDownSeconds, 0f, 1f);
            // ① 회전이 느려진다 — 피치가 1.0 에서 0.35 로.
            _vent.TgtPitch = Mathf.Lerp(1f, VentStoppedPitch, u);
            // ② 다 느려지면 소리가 완전히 사라진다.
            _vent.TgtDb = Mathf.Min(_vent.TgtDb, Mathf.Lerp(_vent.NormalDb, Silent, u));
            // ③ 멎는 순간부터 정적이 시작된다(이번 정지에 한 번만).
            if (u >= 1f && !_hushDone) { _hushDone = true; _hushT = 0f; _hushSeconds = HushSeconds; }
        }

        // ④ 갑자기 공간이 너무 조용해진다 — 다른 상시음도 함께 눌렀다가 서서히 되돌린다.
        if (_hushT < 0f) return;
        if (_hushT >= _hushSeconds) { _hushT = -1f; return; }
        ApplyHush(HushDb * (1f - Mathf.Clamp((_hushT - (_hushSeconds - 0.8f)) / 0.8f, 0f, 1f)));
    }

    // 상시음을 cut(dB) 만큼 눌러 둔다. 목표값은 매 프레임 다시 계산되므로 여기서 덮어써야 한다.
    private void ApplyHush(float cut)
    {
        if (cut <= 0f) return;
        foreach (var l in new[] { _machinery, _drone, _fluor })
            if (l != null) l.TgtDb -= cut;
        foreach (var l in _crt) l.TgtDb -= cut * 0.5f;
    }

    // --- 랜덤 One-shot / 깜빡임 -----------------------------------------
    private static readonly (string key, float db)[] OneShots =
    {
        ("metal_clang", -10f), ("pipe_knock", -13f), ("steam_hiss", -12f),
        ("relay_click", -14f), ("chair_creak", -9f),
    };

    private void TickOneShots(float d)
    {
        if (_amb == Amb.Off) return;

        _nextOneShot -= d;
        if (_nextOneShot <= 0f)
        {
            bool tense = _amb is Amb.Warning or Amb.TabooPrecursor;
            _nextOneShot = (float)GD.RandRange(tense ? 4.0 : 9.0, tense ? 11.0 : 24.0);
            var (key, db) = OneShots[GD.Randi() % OneShots.Length];
            var s = Load(key);
            if (s == null) return;
            var p = new AudioStreamPlayer3D
            {
                Stream = s, VolumeDb = db, UnitSize = 3f, MaxDistance = 16f, Bus = NSP.Core.GameSettings.BusSfx,
                PitchScale = (float)GD.RandRange(0.9, 1.1),
            };
            AddChild(p);
            p.Play();
            p.Finished += () => p.QueueFree();
        }

        // 금기 전조: 형광등/CRT 불안정 — 주기적 깜빡임 + 순간 볼륨 딥.
        if (_amb == Amb.TabooPrecursor)
        {
            _nextFlicker -= d;
            if (_nextFlicker <= 0f)
            {
                _nextFlicker = (float)GD.RandRange(0.7, 2.2);
                PlayOn(_fluor.P3?.GetParentNode3D(), "flicker", -14f);
                if (_fluor.P3 != null) _fluor.P3.VolumeDb -= 8f;
                foreach (var c in _crt) if (c.P3 != null) c.P3.VolumeDb -= 6f;
            }
        }
    }

    private void PlayOn(Node3D at, string key, float db)
    {
        var s = Load(key);
        if (s == null) return;
        var p = new AudioStreamPlayer3D { Stream = s, VolumeDb = db, UnitSize = 3f, MaxDistance = 14f, Bus = NSP.Core.GameSettings.BusSfx };
        (at ?? (Node3D)this).AddChild(p);
        p.Play();
        p.Finished += () => p.QueueFree();
    }

    // --- 외부 훅(공포 연출 등) — 기존 이름 유지 ---------------------------
    // 밖에서 부르는 입구. 고장 감지(PollVentFault)와 같은 길을 타야 연출이 갈리지 않는다.
    public void KillVent() => BeginVentStop();

    public void RestoreVent() => BeginVentRestart();

    public void CreakChair()
    {
        if (_chair == null) return;
        _chair.Stream = Load("chair_creak");
        _chair.PitchScale = (float)GD.RandRange(0.9, 1.1);
        _chair.Play();
    }
}
