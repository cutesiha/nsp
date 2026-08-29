using Godot;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// "시설이 살아 있다"는 환경음. BGM 없음. 환풍구 팬 루프 + CRT hum + 형광등 hum 을 실제
// 3D 위치에 둔다(AudioStreamPlayer3D, 리스너는 현재 Camera3D). 환기 고장 시 팬이 느려지다
// 멈춰 공간이 갑자기 조용해지는 것 자체가 공포 연출이 된다.
public partial class ControlRoomAtmosphere : Node3D
{
    [Export] public NodePath VentPath = "ControlRoom/Vent";
    [Export] public NodePath M01ScreenPath = "ControlRoom/Monitor01/M01_Screen";
    [Export] public NodePath M02ScreenPath = "ControlRoom/Monitor02/M02_Screen";
    [Export] public NodePath CeilingLightPath = "ControlRoom/Lights/CeilingFixture";
    [Export] public NodePath ChairPath = "ControlRoom/Chair/Chair_Seat";

    private AudioStreamPlayer3D _vent, _chair;
    private float _ventPitchTarget = 1f;
    private float _ventVolTarget = -6f;
    private bool _ventDown;
    private RoomDangerTier _ventTier = RoomDangerTier.None;
    private float _nextCreak = 25f;

    public override void _Ready()
    {
        _vent = MakeLoop("vent_loop", NodeAt(VentPath), -6f, unitSize: 3.5f, maxDist: 14f);
        MakeLoop("crt_hum", NodeAt(M01ScreenPath), -19f, 1.4f, 4.5f);
        MakeLoop("crt_hum", NodeAt(M02ScreenPath), -19f, 1.4f, 4.5f);
        MakeLoop("fluor_hum", NodeAt(CeilingLightPath), -21f, 2.2f, 9f);

        _chair = MakeOneShot(NodeAt(ChairPath), -8f, 2f, 6f);
        _nextCreak = (float)GD.RandRange(15.0, 35.0);
    }

    private Node3D NodeAt(NodePath p) => GetNodeOrNull<Node3D>(p);

    private AudioStreamPlayer3D MakeLoop(string key, Node3D at, float volumeDb, float unitSize, float maxDist)
    {
        var stream = Load(key);
        if (stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;

        var p = new AudioStreamPlayer3D
        {
            Stream = stream,
            VolumeDb = volumeDb,
            UnitSize = unitSize,
            MaxDistance = maxDist,
            Bus = "Master",
            Autoplay = false,
        };
        (at ?? this).AddChild(p);
        if (stream != null) p.Play();
        return p;
    }

    private AudioStreamPlayer3D MakeOneShot(Node3D at, float volumeDb, float unitSize, float maxDist)
    {
        var p = new AudioStreamPlayer3D { VolumeDb = volumeDb, UnitSize = unitSize, MaxDistance = maxDist, Bus = "Master" };
        (at ?? this).AddChild(p);
        return p;
    }

    private static AudioStream Load(string key)
    {
        string path = $"res://assets/audio/sfx_{key}.wav";
        return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        PollVent();

        if (_vent != null)
        {
            _vent.PitchScale = Mathf.MoveToward(_vent.PitchScale, _ventPitchTarget, d * (_ventDown ? 0.7f : 1.1f));
            _vent.VolumeDb = Mathf.MoveToward(_vent.VolumeDb, _ventVolTarget, d * 22f);
            if (_ventDown && _vent.PitchScale < 0.08f && _vent.Playing) _vent.StreamPaused = true;
        }

        _nextCreak -= d;
        if (_nextCreak <= 0f)
        {
            _nextCreak = (float)GD.RandRange(22.0, 55.0);
            CreakChair();
        }
    }

    private void PollVent()
    {
        if (FacilitySimulation.Instance == null) return;
        var tier = RoomStatusText.GetDangerTier("vent_room");
        if (tier == _ventTier) return;

        bool wasFailed = _ventTier == RoomDangerTier.Failure;
        bool nowFailed = tier == RoomDangerTier.Failure;
        _ventTier = tier;

        if (nowFailed && !wasFailed) KillVent();
        else if (wasFailed && !nowFailed) RestoreVent();
    }

    // --- 공개 (공포 연출 / 이벤트 훅) -----------------------------------

    public void KillVent()
    {
        _ventDown = true;
        _ventPitchTarget = 0.02f;
        _ventVolTarget = -40f;
        PlayOn(_vent?.GetParentNode3D(), "vent_stop", -4f);
    }

    public void RestoreVent()
    {
        _ventDown = false;
        _ventPitchTarget = 1f;
        _ventVolTarget = -6f;
        if (_vent != null) _vent.StreamPaused = false;
        PlayOn(_vent?.GetParentNode3D(), "vent_restart", -3f);
    }

    public void CreakChair()
    {
        if (_chair == null) return;
        _chair.Stream = Load("chair_creak");
        _chair.PitchScale = (float)GD.RandRange(0.9, 1.1);
        _chair.Play();
    }

    private void PlayOn(Node3D at, string key, float db)
    {
        var s = Load(key);
        if (s == null) return;
        var p = new AudioStreamPlayer3D { Stream = s, VolumeDb = db, UnitSize = 3.5f, MaxDistance = 14f };
        (at ?? this).AddChild(p);
        p.Play();
        p.Finished += () => p.QueueFree();
    }
}
