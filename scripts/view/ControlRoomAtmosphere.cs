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

        _chair = new AudioStreamPlayer3D { VolumeDb = -8f, UnitSize = 2f, MaxDistance = 6f, Bus = "Master" };
        (NodeAt(ChairPath) ?? (Node3D)this).AddChild(_chair);

        _alarm = MakeLoop2D("alarm", Silent);

        _nextOneShot = (float)GD.RandRange(6.0, 14.0);
    }

    private Node3D NodeAt(NodePath p) => GetNodeOrNull<Node3D>(p);

    private Layer MakeLoop3D(string key, Node3D at, float normalDb, float startDb, float unit, float maxDist, float offDelay, float onDelay)
    {
        var stream = Load(key);
        if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        var p = new AudioStreamPlayer3D
        {
            Stream = stream, VolumeDb = Silent, UnitSize = unit, MaxDistance = maxDist, Bus = "Master",
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
        var p = new AudioStreamPlayer { Stream = stream, VolumeDb = startDb, Bus = "Master" };
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

        TickOneShots(d);
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
                Stream = s, VolumeDb = db, UnitSize = 3f, MaxDistance = 16f, Bus = "Master",
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
        var p = new AudioStreamPlayer3D { Stream = s, VolumeDb = db, UnitSize = 3f, MaxDistance = 14f, Bus = "Master" };
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
