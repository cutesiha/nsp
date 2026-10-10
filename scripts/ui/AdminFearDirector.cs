using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Ui;

// 관리자(플레이어)의 몸이 위협에 반응한다 — 심장박동 · 거친 호흡 · 화면 가장자리의 붉은 기운.
//
// **판정과 섞이지 않는다.** 여기 있는 값은 승패에 아무 영향이 없고, 거꾸로 게임 상태를
// 읽기만 한다(MonsterThreatSystem.Closeness). 연출이 판정을 만들지 않는다.
//
// ── 세 가지를 따로 쓴다 ─────────────────────────────────────────────
// 무슨 일이 터지든 셋이 한꺼번에 나면 금세 무뎌진다. 그래서 상황마다 조합이 다르다.
//   접근 시작  심박만(거리에 따라 BPM↑)
//   문 앞      심박 + 호흡 + 비네팅
//   정전       심박 + 비네팅 (**호흡은 켜지 않는다** — 숨은 '가까이 있다' 의 신호다)
//   문 두드림  심박 유지 + 화면 진동
//
// ── 소리 ────────────────────────────────────────────────────────────
//   심장박동  admin_heartbeat.wav — "둥-둥" 한 쌍만 들어 있다. BPM 은 **재생 간격**으로
//             만든다(PitchScale 을 올려 왜곡시키지 않는다). 파일이 없으면 조용히 쉰다.
//   호흡      man_breath — 사용자가 넣어 둔 남자 숨소리를 그대로 루프한다.
public partial class AdminFearDirector : CanvasLayer
{
    public static AdminFearDirector Instance { get; private set; }

    // ── 수치 ────────────────────────────────────────────────────────
    private const string HeartKey = "admin_heartbeat";
    private const string BreathKey = "man_breath";

    // 거리 구간별 BPM. 실제 간격은 60/BPM 초다.
    private const float BpmFar = 78f;
    private const float BpmMid = 104f;
    private const float BpmNear = 132f;
    private const float BpmAtDoor = 158f;

    private const float HeartDbFar = -22f;
    private const float HeartDbNear = -9f;
    private const float BreathDbMax = -8f;

    // 붉은 비네팅 — 가장자리에서만 번진다. 중앙은 끝까지 읽을 수 있어야 한다.
    private const float VignetteMaxEdge = 0.62f;   // 가장 셀 때의 알파
    private const float RiseSpeed = 1.5f;          // 올라가는 속도(1초에 이만큼)
    private const float FallSpeed = 0.55f;         // 내려가는 속도 — 올라갈 때보다 느리게

    // 셰이더: 화면 가장자리에서 안쪽으로 번지는 붉은 기운.
    //   edge   0 = 없음, 1 = 최대
    //   reach  붉은 영역이 중앙으로 얼마나 밀고 들어왔는가(귀신 관측용)
    private const string ShaderCode = @"
shader_type canvas_item;
uniform float edge : hint_range(0.0, 1.0) = 0.0;
uniform float reach : hint_range(0.0, 1.0) = 0.0;
uniform vec3 tint = vec3(0.62, 0.05, 0.04);
void fragment() {
    vec2 p = (UV - vec2(0.5)) * 2.0;
    // 화면비를 반영하지 않는다 — 가장자리를 고르게 덮는 쪽이 자연스럽다.
    float d = length(p * vec2(1.0, 0.92));
    // reach 가 커질수록 시작 지점이 중앙으로 당겨진다.
    float from = mix(0.95, 0.18, clamp(reach, 0.0, 1.0));
    float a = smoothstep(from, 1.45, d) * edge;
    COLOR = vec4(tint, a);
}
";

    private ColorRect _veil;
    private ShaderMaterial _mat;
    private AudioStreamPlayer _heart;

    // 지금 얼마나 세게 반응하고 있는가(0~1). 목표값으로 서서히 따라간다.
    private float _level;
    private float _target;
    // 중앙으로 밀고 들어오는 정도(귀신 관측 전용). 보통은 0.
    private float _reach;
    private float _reachTarget;
    private double _nextBeat;
    private float _bpm = BpmFar;
    private bool _breathing;

    // 일회성 반응(기괴한 전화 · 점프스케어 · 기절 발견 등)이 남은 시간.
    private float _burst;
    private float _burstLevel;
    // 정전 반응 — 숨소리를 켜지 않는 유일한 상태.
    private bool _suppressBreath;

    public override void _Ready()
    {
        Instance = this;
        // 경보 HUD(122) 위, 도전과제 팝업(150) 아래, 암전(160) 아래.
        Layer = 140;
        ProcessMode = ProcessModeEnum.Always;
        BuildUi();
        _heart = new AudioStreamPlayer { Bus = GameSettings.BusSfx, VolumeDb = HeartDbFar };
        AddChild(_heart);
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        StopBreath();
        if (Instance == this) Instance = null;
    }

    private void BuildUi()
    {
        _mat = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _mat.SetShaderParameter("edge", 0f);
        _mat.SetShaderParameter("reach", 0f);

        _veil = new ColorRect
        {
            Color = Colors.White,                       // 색은 셰이더가 정한다
            MouseFilter = Control.MouseFilterEnum.Ignore,  // 입력을 절대 가로채지 않는다
            Visible = false,
        };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _veil.Material = _mat;
        AddChild(_veil);
    }

    // ── 바깥에서 부르는 것 ──────────────────────────────────────────

    // 몇 초짜리 일회성 반응. 지속 위협이 더 세면 그쪽이 이긴다.
    //   strength 0~1 · seconds 유지 시간
    public void Burst(float strength, float seconds, bool breath = true)
    {
        _burstLevel = Mathf.Max(_burstLevel, Mathf.Clamp(strength, 0f, 1f));
        _burst = Mathf.Max(_burst, seconds);
        if (!breath) _suppressBreath = true;
    }

    // 정전 — 심박과 붉은 기운만. 숨소리는 켜지 않는다(지시서 §7-2).
    public void PowerOutage(float seconds)
    {
        _suppressBreath = true;
        Burst(0.62f, seconds, breath: false);
    }

    // CCTV 에서 귀신을 보고 있는 동안 — 붉은 영역이 바깥에서 중앙으로 **천천히** 조여 온다.
    // 깜빡이지 않는다. 0 을 주면 풀린다.
    public void GhostWatch(float amount) => _reachTarget = Mathf.Clamp(amount, 0f, 1f);

    public void ResetAll()
    {
        _level = _target = _reach = _reachTarget = 0f;
        _burst = _burstLevel = 0f;
        _suppressBreath = false;
        StopBreath();
        Apply();
    }

    // 검사용.
    public float Level => _level;
    public float Bpm => _bpm;
    public bool Breathing => _breathing;
    public float VignetteEdge => _mat == null ? 0f : (float)_mat.GetShaderParameter("edge");
    public float VignetteReach => _mat == null ? 0f : (float)_mat.GetShaderParameter("reach");

    // ── 매 프레임 ───────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        float d = (float)delta;
        var sim = FacilitySimulation.Instance;
        bool live = GameState.Instance?.CurrentPhase == GamePhase.Live;

        // 근무가 아니면 전부 걷는다 — 보고서 · 휴게시간 · 타이틀에 붉은 화면이 남지 않게.
        if (!live)
        {
            if (_level > 0f || _breathing || _burst > 0f) ResetAll();
            return;
        }

        // ① 지속 위협 — 복도의 괴물이 얼마나 가까운가.
        var t = sim?.Threats.Current;
        float threat = 0f;
        bool atDoor = false;
        if (t is { Active: true })
        {
            threat = t.Phase switch
            {
                ThreatPhase.Outer => 0.18f,
                ThreatPhase.Approach => 0.25f + t.Closeness * 0.5f,
                ThreatPhase.Retreat => 0.25f + t.Closeness * 0.35f,
                ThreatPhase.Dormant or ThreatPhase.Finished => 0f,
                _ => 1f,
            };
            atDoor = t.Phase is ThreatPhase.AtDoor or ThreatPhase.Pounding or ThreatPhase.Breach;
        }

        // ② 일회성 반응.
        if (_burst > 0f)
        {
            _burst -= d;
            if (_burst <= 0f) { _burstLevel = 0f; _suppressBreath = false; }
        }

        // 둘이 겹치면 센 쪽이 남는다. 지속 위협은 일회성이 끝나도 사라지지 않는다.
        _target = Mathf.Max(threat, _burstLevel);
        _level = Mathf.MoveToward(_level, _target, (_level < _target ? RiseSpeed : FallSpeed) * d);

        _reach = Mathf.MoveToward(_reach, _reachTarget, (_reach < _reachTarget ? 0.22f : 0.6f) * d);

        TickHeart(d);
        TickBreath(threat, atDoor);
        Apply();
    }

    private void TickHeart(float d)
    {
        if (_level <= 0.02f)
        {
            if (_heart.Playing) _heart.Stop();
            _nextBeat = 0;
            return;
        }

        // 거리 구간별 BPM 사이를 부드럽게 오간다.
        float want = _level switch
        {
            < 0.30f => Mathf.Lerp(BpmFar, BpmMid, _level / 0.30f),
            < 0.62f => Mathf.Lerp(BpmMid, BpmNear, (_level - 0.30f) / 0.32f),
            _ => Mathf.Lerp(BpmNear, BpmAtDoor, Mathf.Min(1f, (_level - 0.62f) / 0.38f)),
        };
        _bpm = Mathf.MoveToward(_bpm, want, 26f * d);

        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextBeat) return;
        // **간격**으로 BPM 을 만든다. 소리 자체는 한 번도 변형하지 않는다.
        _nextBeat = now + 60.0 / Mathf.Max(40f, _bpm);

        var stream = HeartStream();
        if (stream == null) return;
        _heart.Stream = stream;
        _heart.VolumeDb = Mathf.Lerp(HeartDbFar, HeartDbNear, _level);
        _heart.Play();
    }

    private AudioStream _heartStream;
    private bool _heartMissing;

    private AudioStream HeartStream()
    {
        if (_heartStream != null) return _heartStream;
        if (_heartMissing) return null;
        const string path = "res://assets/audio/sfx/" + HeartKey + ".wav";
        if (!ResourceLoader.Exists(path))
        {
            // 파일이 없어도 게임은 멈추지 않는다 — 심박만 쉰다.
            _heartMissing = true;
            GD.PushWarning($"AdminFearDirector: {path} 가 없어 심장박동을 쉽니다.");
            return null;
        }
        _heartStream = GD.Load<AudioStream>(path);
        return _heartStream;
    }

    // 숨소리는 **가까울 때만**. 평상시에 계속 몰아쉬면 피로하기만 하다.
    private void TickBreath(float threat, bool atDoor)
    {
        bool want = !_suppressBreath && (atDoor || threat >= 0.62f || _burstLevel >= 0.55f);
        if (want && !_breathing)
        {
            Sfx.Instance?.Loop(BreathKey, BreathDbMax - 6f);
            _breathing = Sfx.Instance?.IsLooping(BreathKey) ?? false;
        }
        else if (!want && _breathing) StopBreath();

        if (_breathing)
            Sfx.Instance?.SetLoopVolume(BreathKey, Mathf.Lerp(BreathDbMax - 10f, BreathDbMax, _level));
    }

    private void StopBreath()
    {
        if (!_breathing) return;
        _breathing = false;
        Sfx.Instance?.StopLoop(BreathKey);
    }

    private void Apply()
    {
        if (_mat == null) return;
        float edge = _level * VignetteMaxEdge;
        // 귀신 관측은 가장자리 세기와 **별도로** 중앙 침범만 만든다.
        if (_reach > 0.01f) edge = Mathf.Max(edge, 0.26f + _reach * 0.26f);
        _mat.SetShaderParameter("edge", edge);
        _mat.SetShaderParameter("reach", _reach);
        _veil.Visible = edge > 0.004f;
    }
}
