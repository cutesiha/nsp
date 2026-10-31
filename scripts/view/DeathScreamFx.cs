using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 직원 사망 순간(2026-10-31 사용자 요청 — 직원 사망의 임팩트와 공포를 키운다).
//
//   · 비명 — 남자 직원은 boy_scream, 여자 직원은 girl_scream 을 일그러뜨려 튼다(Sfx.PlayDeathScream).
//   · 비명이 울리는 동안 **화면 전체가 울리듯 떨린다** — 카메라가 고주파로 떨리고(소리가 클수록 세게),
//     두 모니터 화면도 가로로 찢기고 지직거린다.
//   · 중앙제어실 천장에서 **새빨간 등이 껌뻑인다**. 소리가 멎으면 떨림도 등도 그 자리에서 꺼진다.
//
// 판정에는 손대지 않는다 — 소리 · 카메라 · 조명 · 모니터 표면만 만진다.
public partial class DeathScreamFx : Node
{
    // 음원을 못 찾았거나(합성 비명) 소리가 멎지 않을 때의 안전장치.
    private const float MaxSeconds = 14f;
    private const float SynthSeconds = 2.4f;
    private const float MinSeconds = 0.6f;
    // 가장 클 때 카메라 떨림(도). 모니터 글자를 못 읽을 만큼은 아니고, 몸이 움찔하는 만큼.
    private const float TremorDeg = 0.85f;

    private static readonly Color StrobeRed = new(1f, 0.03f, 0.02f);
    private static DeathScreamFx _running;

    private bool _real;
    private float _t;
    private float _amp;
    private float _baseNoise, _baseDistortion;
    private OmniLight3D _strobe, _strobeFill;
    private bool _on;
    private float _phaseLeft;
    private readonly RandomNumberGenerator _rng = new();
    private int _screamBus = -1;

    public static bool Running => _running != null && IsInstanceValid(_running);

    // 그 직원의 비명과 함께 연출을 시작한다. 이미 돌고 있으면 처음부터 다시 잰다(연달아 죽었을 때).
    public static void Play(string employeeId)
    {
        bool female = FacilitySimulation.Instance?.GetEmployeeDef(employeeId)?.Gender?.Contains("여") == true;
        bool real = Sfx.Instance?.PlayDeathScream(female, PanOf(employeeId)) == true;
        if (!real) Sfx.Instance?.PlayScream(employeeId, -1.5f);   // 음원이 없으면 예전 합성 비명

        var ctl = ControlRoom3DController.Instance;
        if (ctl == null) return;
        if (Running) { _running._real = real; _running._t = 0f; return; }
        var fx = new DeathScreamFx { _real = real, Name = "DeathScreamFx" };
        ctl.AddChild(fx);
        _running = fx;
    }

    // 비명이 들리는 방향 — 그 직원이 있던 방이 중앙제어실(지도 가운데)의 왼쪽인가 오른쪽인가.
    // "어디서 죽었지?" 가 귀로 먼저 온다(추리 단서). 끝까지 치우치지는 않는다(±0.7).
    private static float PanOf(string employeeId)
    {
        var sim = FacilitySimulation.Instance;
        string room = sim?.GetEmployeeState(employeeId)?.CurrentRoomId ?? "";
        if (sim == null || string.IsNullOrEmpty(room)) return 0f;
        float dx = sim.RoomMapPosition(room).X - sim.RoomMapPosition("central_office").X;
        return Mathf.Clamp(dx / 180f * 0.7f, -0.7f, 0.7f);
    }

    public override void _Ready()
    {
        _rng.Randomize();
        var ctl = ControlRoom3DController.Instance;
        _baseNoise = ctl?.ScreenNoise ?? 0.02f;
        _baseDistortion = ctl?.ScreenDistortion ?? 0f;
        _screamBus = AudioServer.GetBusIndex(GameSettings.BusScream);

        // 천장 등 자리(ControlRoom/Lights/CeilingFixture 바로 아래)에 붉은 점멸등을 따로 단다.
        // 기존 비상등은 ControlRoom3DHorror 가 매 프레임 제 값으로 되돌리므로 빌려 쓰지 않는다.
        var room = ctl?.GetNodeOrNull<Node3D>("ControlRoom");
        if (room != null)
        {
            _strobe = new OmniLight3D
            {
                Name = "DeathStrobe",
                Position = new Vector3(0f, 2.48f, -0.7f),
                LightColor = StrobeRed,
                LightEnergy = 0f,
                OmniRange = 6.5f,
                OmniAttenuation = 0.8f,
                ShadowEnabled = false,
            };
            // 책상 앞쪽을 적시는 낮은 보조 — 관리자 손과 키보드까지 붉게 물든다.
            _strobeFill = new OmniLight3D
            {
                Name = "DeathStrobeFill",
                Position = new Vector3(0f, 1.9f, 0.4f),
                LightColor = StrobeRed,
                LightEnergy = 0f,
                OmniRange = 3.2f,
                ShadowEnabled = false,
            };
            room.AddChild(_strobe);
            room.AddChild(_strobeFill);
        }
        _on = true;
        _phaseLeft = 0.12f;
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _t += d;

        // 소리가 얼마나 큰가 — 비명 버스의 현재 크기를 그대로 떨림 · 점멸 세기로 쓴다.
        float target = 1f;
        if (_real && _screamBus >= 0)
        {
            float db = Mathf.Max(AudioServer.GetBusPeakVolumeLeftDb(_screamBus, 0),
                                 AudioServer.GetBusPeakVolumeRightDb(_screamBus, 0));
            target = Mathf.Clamp((db + 34f) / 28f, 0f, 1f);
        }
        _amp = Mathf.Lerp(_amp, target, Mathf.Clamp(d * 14f, 0f, 1f));

        bool done = _t >= MaxSeconds
                    || (_t >= MinSeconds && (_real ? Sfx.Instance?.DeathScreamPlaying != true : _t >= SynthSeconds));
        if (done) { Finish(); return; }

        var ctl = ControlRoom3DController.Instance;
        float k = 0.35f + 0.65f * _amp;
        float calm = GameSettings.FlashScale;   // "깜빡임 · 흔들림 줄이기" 를 켜면 0.3
        if (ctl?.Rig != null) ctl.Rig.Tremor = TremorDeg * k * calm;
        ctl?.SetScreenDistortion(_baseDistortion + 0.45f * k * calm);
        ctl?.SetScreenNoise(_baseNoise + 0.20f * k * calm);

        // 줄이기를 켰으면 점멸 대신 **느린 맥박**(초당 한 번 남짓) — 밝기가 급히 바뀌지 않는다(광과민성 배려).
        if (GameSettings.ReduceFlashing)
        {
            float pulse = 0.55f + 0.45f * Mathf.Sin(_t * 5.5f);
            if (_strobe != null) _strobe.LightEnergy = (1.6f + 1.8f * k) * pulse;
            if (_strobeFill != null) _strobeFill.LightEnergy = (1.6f + 1.8f * k) * pulse * 0.45f;
            return;
        }

        // 껌뻑임 — 켜짐 · 꺼짐 길이가 매번 다르다. 일정한 박자는 경광등이고, 고르지 않아야 고장 난 등이다.
        _phaseLeft -= d;
        if (_phaseLeft <= 0f)
        {
            _on = !_on;
            _phaseLeft = _on ? _rng.RandfRange(0.06f, 0.17f) : _rng.RandfRange(0.04f, 0.13f);
        }
        float flash = _on ? _rng.RandfRange(0.85f, 1.1f) * (3.6f + 4.2f * k) : 0f;
        if (_strobe != null) _strobe.LightEnergy = flash;
        if (_strobeFill != null) _strobeFill.LightEnergy = flash * 0.45f;
    }

    // 소리가 멎었다 — 떨림도 등도 그 자리에서 꺼진다.
    private void Finish()
    {
        var ctl = ControlRoom3DController.Instance;
        if (ctl?.Rig != null) ctl.Rig.Tremor = 0f;
        ctl?.SetScreenDistortion(_baseDistortion);
        ctl?.SetScreenNoise(_baseNoise);
        _strobe?.QueueFree();
        _strobeFill?.QueueFree();
        _strobe = _strobeFill = null;
        if (_running == this) _running = null;
        QueueFree();
    }

    public override void _ExitTree()
    {
        // 씬이 통째로 바뀌는 중에도 떨림이 남지 않게.
        var rig = ControlRoom3DController.Instance?.Rig;
        if (rig != null && IsInstanceValid(rig)) rig.Tremor = 0f;
        if (_running == this) _running = null;
    }
}
