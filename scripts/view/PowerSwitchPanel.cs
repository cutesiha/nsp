using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Ui;

namespace NSP.View;

// 전력 배분용 물리 토글 스위치 박스 — LIGHTING / CCTV / SENSOR 3개의 레버 스위치.
// 레버를 클릭하면 손이 나와 검지로 튕기고, 그 순간 "딱!" 소리 + 레버가 위/아래로 넘어가며
// 해당 기기 전원이 on/off 된다. 전력 포인트가 깎이면 스위치 기기에서 지지직 스파크가 튀고,
// 전력이 0이 되면 SHUT DOWN — 방 조명/센서/CCTV 전부 꺼지고 계속 파지직거린다.
// GameState 를 읽고 TryTogglePower 만 호출한다(전력 상태를 여기서 들고 있지 않는다).
[Tool]
public partial class PowerSwitchPanel : Node3D
{
    private static readonly (PowerConsumer Channel, string Label, float X)[] Switches =
    {
        (PowerConsumer.Lighting, "LIGHTING", -0.095f),
        (PowerConsumer.CctvWatch, "CCTV", 0f),
        (PowerConsumer.Sensor, "SENSOR", 0.095f),
    };

    private const float LeverOn = -28f;   // 앞/위로 젖혀짐(ON)
    private const float LeverOff = 26f;   // 뒤/아래로 젖혀짐(OFF)

    private readonly Dictionary<PowerConsumer, Node3D> _levers = new();
    private readonly Dictionary<PowerConsumer, Node3D> _tips = new();
    private readonly Dictionary<PowerConsumer, StandardMaterial3D> _ledMats = new();
    private readonly Dictionary<PowerConsumer, double> _rejectUntil = new();
    private readonly Dictionary<PowerConsumer, bool> _shownOn = new();
    private readonly Dictionary<PowerConsumer, double> _flipUntil = new();
    private Label3D _capacityLabel;
    private PlayerCharacter _arms;

    private AudioStreamPlayer3D _zap, _crackle;
    private MeshInstance3D _spark;
    private StandardMaterial3D _sparkMat;
    private int _lastCapacity = -1;
    private double _zapSparkUntil;
    private bool _shutdown;
    private bool _built;

    public override void _Ready()
    {
        if (_built) return;
        _built = true;
        if (!Engine.IsEditorHint())
            _arms = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;

        // 본체는 제공된 switch.glb. 아래의 세 레버/LED만 실제 전력 채널과 연결되는
        // 조작 부품으로 덧붙인다.
        foreach (var (channel, label, x) in Switches)
            BuildSwitch(channel, label, x);

        // 스파크 — 스위치들 위에서 번쩍이는 가산 발광구(평소 꺼짐).
        _sparkMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.85f, 1f), EmissionEnabled = true,
            Emission = new Color(0.75f, 0.9f, 1f), EmissionEnergyMultiplier = 0f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _spark = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.03f, Height = 0.06f, RadialSegments = 6, Rings = 4 },
            Position = new Vector3(0f, 0.075f, 0.02f), MaterialOverride = _sparkMat,
        };
        AddChild(_spark);
        var sparkLight = new OmniLight3D
        {
            Position = new Vector3(0f, 0.08f, 0.03f), LightColor = new Color(0.7f, 0.85f, 1f),
            LightEnergy = 0f, OmniRange = 0.8f, Name = "SparkLight",
        };
        _spark.AddChild(sparkLight);

        if (!Engine.IsEditorHint())
        {
            _zap = MakePlayer("electric_arc", loop: false, db: -3f);
            _crackle = MakePlayer("electric_crackle_loop", loop: true, db: -8f);
            foreach (var (channel, _, _) in Switches)
            {
                bool on = GameState.Instance?.IsConsumerPowered(channel) ?? true;
                _shownOn[channel] = on;
                if (_levers.TryGetValue(channel, out var lv))
                    lv.RotationDegrees = lv.RotationDegrees with { X = on ? LeverOn : LeverOff };
            }
            _lastCapacity = GameState.Instance?.PowerCapacity ?? -1;
        }

        _capacityLabel = new Label3D
        {
            Text = "POWER 3 / 3",
            Position = new Vector3(0f, 0.06f, -0.06f),
            RotationDegrees = new Vector3(-14f, 0f, 0f),
            PixelSize = 0.00042f, FontSize = 40, OutlineSize = 0,
            Modulate = new Color(0.55f, 0.85f, 0.65f),
        };
        AddChild(_capacityLabel);
    }

    private AudioStreamPlayer3D MakePlayer(string key, bool loop, float db)
    {
        string path = $"res://assets/audio/sfx/{key}.wav";
        var p = new AudioStreamPlayer3D { VolumeDb = db, UnitSize = 2.2f, MaxDistance = 10f };
        if (ResourceLoader.Exists(path)) p.Stream = GD.Load<AudioStream>(path);
        AddChild(p);
        if (loop) p.Finished += () => { if (IsInstanceValid(p) && p.Stream != null && _wantCrackle) p.Play(); };
        return p;
    }
    private bool _wantCrackle;

    private void BuildSwitch(PowerConsumer channel, string label, float x)
    {
        var pivot = new Node3D
        {
            Position = new Vector3(x, 0.03f, 0.028f),
            RotationDegrees = new Vector3(LeverOn, 0f, 0f),
        };
        AddChild(pivot);
        _levers[channel] = pivot;

        var stalkMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.72f), Metallic = 0.8f, Roughness = 0.35f };
        pivot.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.009f, Height = 0.05f, RadialSegments = 8 },
            Position = new Vector3(0f, 0.025f, 0f), MaterialOverride = stalkMat,
        });

        var tipMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.2f, 0.12f), Roughness = 0.5f };
        var tip = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.011f, Height = 0.022f, RadialSegments = 8, Rings = 5 },
            Position = new Vector3(0f, 0.052f, 0f), MaterialOverride = tipMat,
        };
        pivot.AddChild(tip);
        _tips[channel] = tip;

        var ledMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.04f, 0.04f, 0.04f), EmissionEnabled = true,
            Emission = new Color(0.15f, 0.95f, 0.3f), EmissionEnergyMultiplier = 2.2f,
        };
        _ledMats[channel] = ledMat;
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.007f, BottomRadius = 0.008f, Height = 0.006f, RadialSegments = 8 },
            Position = new Vector3(x + 0.028f, 0.042f, 0.03f),
            RotationDegrees = new Vector3(-14f, 0f, 0f), MaterialOverride = ledMat,
        });

        // 채널 라벨 — 스위치와 겹치지 않게 아래·앞(면판 위)으로 내린다.
        AddChild(new Label3D
        {
            Text = label,
            Position = new Vector3(x, 0.012f, 0.088f),
            RotationDegrees = new Vector3(-76f, 0f, 0f),
            PixelSize = 0.00030f, FontSize = 40, OutlineSize = 0,
            Modulate = new Color(0.78f, 0.8f, 0.82f),
        });

        var area = new Area3D { InputRayPickable = true, Position = new Vector3(x, 0.05f, 0.03f) };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.07f, 0.1f, 0.09f) } });
        area.InputEvent += (camera, ev, pos, normal, idx) => OnAreaInput(channel, ev);
        AddChild(area);
    }

    private void OnAreaInput(PowerConsumer channel, InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        if (GameState.Instance == null) return;
        if (GameState.Instance.CurrentPhase is not (GamePhase.Live or GamePhase.Rest)) return;
        if (_flipUntil.GetValueOrDefault(channel) > Time.GetTicksMsec() / 1000.0) return; // 연타 방지

        bool turningOn = !GameState.Instance.IsConsumerPowered(channel);
        Vector3 tipW = _tips.TryGetValue(channel, out var tip) ? tip.GlobalPosition : GlobalPosition;
        _flipUntil[channel] = Time.GetTicksMsec() / 1000.0 + 1.0;

        // 손 애니메이션은 연출. 실제 토글은 손이 레버에 닿는 타이밍(≈0.45s)에 확실히 실행한다.
        _arms?.PlaySwitchFlip(turningOn, tipW, default);
        double delay = _arms != null ? 0.45 : 0.0;
        var timer = GetTree().CreateTimer(delay);
        timer.Timeout += () => DoToggle(channel);
    }

    // 검지가 레버에 닿는 순간.
    private void DoToggle(PowerConsumer channel)
    {
        var gs = GameState.Instance;
        if (gs == null) return;

        bool ok = gs.TryTogglePower(channel);
        if (ok)
        {
            bool on = gs.IsConsumerPowered(channel);
            AnimateLever(channel, on);
            Sfx.Instance?.Play("relay_click", -3f);           // 딱!
            Sfx.Instance?.Play("switch", -8f, on ? 1.05f : 0.9f);
        }
        else
        {
            // 용량 부족 — 레버가 튕겨 돌아오고 LED가 붉게 깜빡, 지지직.
            Sfx.Instance?.Play("switch_fail", -4f);
            _zap?.Play();
            _zapSparkUntil = Time.GetTicksMsec() / 1000.0 + 0.35;
            _rejectUntil[channel] = Time.GetTicksMsec() / 1000.0 + 0.6;
            if (_levers.TryGetValue(channel, out var lever))
            {
                float rest = _shownOn.GetValueOrDefault(channel, true) ? LeverOn : LeverOff;
                float nudge = rest + (LeverOff - LeverOn) * 0.28f;
                var t = CreateTween();
                t.TweenProperty(lever, "rotation_degrees:x", nudge, 0.08);
                t.TweenProperty(lever, "rotation_degrees:x", rest, 0.16).SetTrans(Tween.TransitionType.Back);
            }
        }
    }

    private void AnimateLever(PowerConsumer channel, bool on)
    {
        _shownOn[channel] = on;
        if (!_levers.TryGetValue(channel, out var lever)) return;
        var t = CreateTween();
        t.TweenProperty(lever, "rotation_degrees:x", on ? LeverOn : LeverOff, 0.1)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;
        var gs = GameState.Instance;
        if (gs == null) return;
        double now = Time.GetTicksMsec() / 1000.0;

        // ── 전력 포인트 차감 감지 → 지지직 스파크 + 하강음 ──
        int cap = gs.PowerCapacity;
        if (_lastCapacity >= 0 && cap < _lastCapacity)
        {
            _zap?.Play();
            _zapSparkUntil = now + 0.5;
            Sfx.Instance?.Play("power_point_lost", -3f);
            AmbientOverlay.Instance?.Flash(0.35f);
        }
        _lastCapacity = cap;

        // ── SHUT DOWN (전력 0) — 계속 파지직 ──
        bool blackout = cap == 0 && gs.CurrentPhase == GamePhase.Live;
        if (blackout != _shutdown)
        {
            _shutdown = blackout;
            AmbientOverlay.Instance?.SetShutdown(blackout);
            if (blackout) Sfx.Instance?.Play("power_down", -2f);
        }
        _wantCrackle = blackout;
        if (blackout && _crackle != null && !_crackle.Playing && _crackle.Stream != null) _crackle.Play();
        if (!blackout && _crackle != null && _crackle.Playing) _crackle.Stop();

        // ── 스파크 발광 ──
        float sparkE;
        if (blackout)
            sparkE = 0.4f + 2.2f * Mathf.Abs(Mathf.Sin((float)(now * 17.0)) * (0.4f + 0.6f * GD.Randf()));
        else if (now < _zapSparkUntil)
            sparkE = 1.5f + 4f * GD.Randf();
        else
            sparkE = Mathf.Lerp(_sparkMat.EmissionEnergyMultiplier, 0f, (float)delta * 12f);
        _sparkMat.EmissionEnergyMultiplier = sparkE;
        _sparkMat.AlbedoColor = _sparkMat.AlbedoColor with { A = Mathf.Clamp(sparkE * 0.25f, 0f, 0.9f) };
        if (_spark.GetNodeOrNull<OmniLight3D>("SparkLight") is { } sl) sl.LightEnergy = Mathf.Min(sparkE, 4f);

        // ── 레버 자동 반영(외부 차단) + LED ──
        foreach (var (channel, _, _) in Switches)
        {
            bool on = gs.IsConsumerPowered(channel);
            if (now >= _flipUntil.GetValueOrDefault(channel) && _shownOn.GetValueOrDefault(channel, on) != on)
                AnimateLever(channel, on);

            var mat = _ledMats[channel];
            bool rejecting = _rejectUntil.GetValueOrDefault(channel) > now;
            if (rejecting)
            {
                float k = 0.5f + 0.5f * Mathf.Sin((float)(now * 40.0));
                mat.Emission = new Color(1f, 0.1f, 0.05f);
                mat.EmissionEnergyMultiplier = 1f + 3f * k;
            }
            else
            {
                mat.Emission = on ? new Color(0.15f, 0.95f, 0.3f) : new Color(0.45f, 0.05f, 0.03f);
                mat.EmissionEnergyMultiplier = on ? 2.4f : 0.7f;
            }
        }

        int max = Config.Instance.Data.PowerCapacityMax;
        _capacityLabel.Text = blackout ? "SHUT DOWN" : $"POWER {cap} / {max}";
        _capacityLabel.Modulate = cap >= max ? new Color(0.55f, 0.85f, 0.65f)
            : cap == 0 ? new Color(0.95f, 0.2f, 0.16f)
            : new Color(0.95f, 0.7f, 0.25f);
    }
}
