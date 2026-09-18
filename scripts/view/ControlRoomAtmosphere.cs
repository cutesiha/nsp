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
    [Export] public NodePath VentPath = "../ControlRoom/Vent";
    [Export] public NodePath M01ScreenPath = "../ControlRoom/Monitor01/M01_Screen";
    [Export] public NodePath M02ScreenPath = "../ControlRoom/Monitor02/M02_Screen";
    [Export] public NodePath CeilingLightPath = "../ControlRoom/Lights/CeilingFixture";
    [Export] public NodePath ChairPath = "../ControlRoom/Chair/Chair_Seat";
    [Export] public NodePath WallPath = "../ControlRoom/Wall_Back";
    // 신규 환경음 소스 위치.
    [Export] public NodePath ControlPanelPath = "../ControlRoom/ControlPanel";
    [Export] public NodePath AlertTerminalPath = "../ControlRoom/AlertTerminal";

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
    //   _sensorWhir: 책상 위 센서 단말이 계속 도는 소리 (crt_hum 을 올려 얇은 회전음처럼)
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

    public override void _Ready()
    {
        _vent = MakeLoop3D("vent_loop", NodeAt(VentPath), -6f, 0f, 3.5f, 14f, offDelay: 1.2f, onDelay: 2.0f);
        _fluor = MakeLoop3D("fluor_hum", NodeAt(CeilingLightPath), -21f, 0f, 2.2f, 9f, offDelay: 0.5f, onDelay: 0.8f);
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
        if (_ventFaultDown && !blackout) _vent.TgtDb = Mathf.Min(_vent.TgtDb, -42f);

        foreach (var l in _all) l.Apply(d);

        // 비상 경고음: 정전 중에만.
        _alarm.VolumeDb = Mathf.MoveToward(_alarm.VolumeDb, blackout ? -22f : Silent, d * 20f);

        TickNewAmbience(d, blackout);
        TickOneShots(d);
    }

    // 배전 치치직 / 센서 회전음 / 숨소리. Layer 시스템(_all) 밖에서 상태별로 직접 몬다.
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

        // 센서 상시 회전음 — 정전이면 센서도 꺼진다. 금기 전조에는 피치가 살짝 불안정.
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
            _breath.VolumeDb = Mathf.MoveToward(_breath.VolumeDb, tgt, d * 8f);
            float pTgt = _amb is Amb.TabooPrecursor or Amb.Blackout ? 1.18f : _amb == Amb.Warning ? 1.08f : 1f;
            _breath.PitchScale = Mathf.Lerp(_breath.PitchScale, pTgt, d * 1.5f);
        }
    }

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
        Amb.TabooPrecursor => l == _drone ? -24f : l == _machinery ? -17f : l == _fluor ? -18f : IsCrt(l) ? -16f : l.NormalDb,
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
        bool nowFailed = tier == RoomDangerTier.Failure;
        if (nowFailed && !_ventFaultDown)
        {
            _ventFaultDown = true;
            PlayOn(_vent.P3?.GetParentNode3D(), "vent_stop", -4f);
        }
        else if (!nowFailed && _ventFaultDown)
        {
            _ventFaultDown = false;
            PlayOn(_vent.P3?.GetParentNode3D(), "vent_restart", -3f);
        }
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
    public void KillVent()
    {
        _ventFaultDown = true;
        PlayOn(_vent.P3?.GetParentNode3D(), "vent_stop", -4f);
    }

    public void RestoreVent()
    {
        _ventFaultDown = false;
        PlayOn(_vent.P3?.GetParentNode3D(), "vent_restart", -3f);
    }

    public void CreakChair()
    {
        if (_chair == null) return;
        _chair.Stream = Load("chair_creak");
        _chair.PitchScale = (float)GD.RandRange(0.9, 1.1);
        _chair.Play();
    }
}
