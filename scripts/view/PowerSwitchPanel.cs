using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 전력 배분용 물리 토글 스위치 박스 — LIGHTING / CCTV / SENSOR 3개의 레버 스위치.
// 플레이어가 레버를 클릭하면 손이 나와 툭 튕기고(GameState.TryTogglePower), 전력 용량이
// 부족하면 레버가 다시 내려가며 상태 LED가 붉게 깜빡이고 실패음이 난다. 새 전력 상태를
// 들고 있지 않고 GameState를 읽기만 한다. Telephone과 같은 Area3D 클릭 방식.
// [Tool] — 형상을 코드로 만들지만 에디터 뷰포트에도 보이게 한다.
[Tool]
public partial class PowerSwitchPanel : Node3D
{
    private static readonly (PowerConsumer Channel, string Label, float X)[] Switches =
    {
        (PowerConsumer.Lighting, "LIGHTING", -0.095f),
        (PowerConsumer.CctvWatch, "CCTV", 0f),
        (PowerConsumer.Sensor, "SENSOR", 0.095f),
    };

    private const float LeverOn = -28f;   // 앞/위로 젖혀짐
    private const float LeverOff = 26f;   // 뒤/아래로 젖혀짐

    private readonly Dictionary<PowerConsumer, Node3D> _levers = new();
    private readonly Dictionary<PowerConsumer, StandardMaterial3D> _ledMats = new();
    private readonly Dictionary<PowerConsumer, double> _rejectUntil = new();
    private Label3D _capacityLabel;
    private PlayerCharacter _arms;

    public override void _Ready()
    {
        if (GetChildCount() > 0) return; // 스크립트 리로드 시 중복 생성 방지
        if (!Engine.IsEditorHint())
            _arms = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;

        var bodyMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.13f, 0.13f, 0.12f),
            Roughness = 0.85f,
            Metallic = 0.25f,
        };
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.06f, 0.14f) },
            RotationDegrees = new Vector3(-14f, 0f, 0f),
            MaterialOverride = bodyMat,
        };
        AddChild(body);

        var faceMat = new StandardMaterial3D { AlbedoColor = new Color(0.09f, 0.09f, 0.09f), Roughness = 0.7f };
        var face = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.30f, 0.012f, 0.125f) },
            Position = new Vector3(0f, 0.035f, 0f),
            RotationDegrees = new Vector3(-14f, 0f, 0f),
            MaterialOverride = faceMat,
        };
        AddChild(face);

        foreach (var (channel, label, x) in Switches)
            BuildSwitch(channel, label, x);

        _capacityLabel = new Label3D
        {
            Text = "POWER 3 / 3",
            Position = new Vector3(0f, 0.052f, -0.052f),
            RotationDegrees = new Vector3(-14f, 0f, 0f),
            PixelSize = 0.00042f,
            FontSize = 40,
            OutlineSize = 0,
            Modulate = new Color(0.55f, 0.85f, 0.65f),
        };
        AddChild(_capacityLabel);
    }

    private void BuildSwitch(PowerConsumer channel, string label, float x)
    {
        // 레버 피벗(밑동) — 여기서 회전한다.
        var pivot = new Node3D
        {
            Position = new Vector3(x, 0.03f, 0.028f),
            RotationDegrees = new Vector3(LeverOn, 0f, 0f),
        };
        AddChild(pivot);
        _levers[channel] = pivot;

        var stalkMat = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.7f, 0.72f), Metallic = 0.8f, Roughness = 0.35f };
        var stalk = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.009f, Height = 0.05f, RadialSegments = 8 },
            Position = new Vector3(0f, 0.025f, 0f),
            MaterialOverride = stalkMat,
        };
        pivot.AddChild(stalk);

        var tipMat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.2f, 0.12f), Roughness = 0.5f };
        var tip = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.011f, Height = 0.022f, RadialSegments = 8, Rings = 5 },
            Position = new Vector3(0f, 0.052f, 0f),
            MaterialOverride = tipMat,
        };
        pivot.AddChild(tip);

        // 상태 LED
        var ledMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.04f, 0.04f, 0.04f),
            EmissionEnabled = true,
            Emission = new Color(0.15f, 0.95f, 0.3f),
            EmissionEnergyMultiplier = 2.2f,
        };
        _ledMats[channel] = ledMat;
        var led = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.007f, BottomRadius = 0.008f, Height = 0.006f, RadialSegments = 8 },
            Position = new Vector3(x + 0.028f, 0.042f, 0.03f),
            RotationDegrees = new Vector3(-14f, 0f, 0f),
            MaterialOverride = ledMat,
        };
        AddChild(led);

        var lbl = new Label3D
        {
            Text = label,
            Position = new Vector3(x, 0.05f, 0.062f),
            RotationDegrees = new Vector3(-14f, 0f, 0f),
            PixelSize = 0.00032f,
            FontSize = 40,
            OutlineSize = 0,
            Modulate = new Color(0.78f, 0.8f, 0.82f),
        };
        AddChild(lbl);

        var area = new Area3D { InputRayPickable = true, Position = new Vector3(x, 0.05f, 0.03f) };
        area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.07f, 0.09f, 0.08f) } });
        area.InputEvent += (camera, ev, pos, normal, idx) => OnAreaInput(channel, ev);
        AddChild(area);
    }

    private void OnAreaInput(PowerConsumer channel, InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        bool ok = GameState.Instance?.TryTogglePower(channel) ?? false;
        _arms?.PlaySwitchFlip(channel == PowerConsumer.Sensor ? 1 : channel == PowerConsumer.CctvWatch ? 0 : -1);

        if (ok)
        {
            Sfx.Instance?.Play("switch", -5f);
        }
        else
        {
            // 전력 용량 부족 — 레버가 잠깐 올라갔다 다시 내려가고 LED가 붉게 깜빡인다.
            Sfx.Instance?.Play("switch_fail", -4f);
            _rejectUntil[channel] = Time.GetTicksMsec() / 1000.0 + 0.6;
            if (_levers.TryGetValue(channel, out var lever))
            {
                var t = CreateTween();
                t.TweenProperty(lever, "rotation_degrees:x", LeverOn, 0.09);
                t.TweenProperty(lever, "rotation_degrees:x", LeverOff, 0.18).SetTrans(Tween.TransitionType.Back);
            }
        }
    }

    public override void _Process(double delta)
    {
        var gs = GameState.Instance;
        if (gs == null) return;
        double now = Time.GetTicksMsec() / 1000.0;

        foreach (var (channel, _, _) in Switches)
        {
            bool on = gs.IsConsumerPowered(channel);

            if (_levers.TryGetValue(channel, out var lever))
            {
                float target = on ? LeverOn : LeverOff;
                lever.RotationDegrees = lever.RotationDegrees with
                {
                    X = Mathf.Lerp(lever.RotationDegrees.X, target, (float)delta * 12f),
                };
            }

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

        int cap = gs.PowerCapacity;
        int max = Config.Instance.Data.PowerCapacityMax;
        _capacityLabel.Text = $"POWER {cap} / {max}";
        _capacityLabel.Modulate = cap >= max ? new Color(0.55f, 0.85f, 0.65f)
            : cap == 0 ? new Color(0.95f, 0.25f, 0.2f)
            : new Color(0.95f, 0.7f, 0.25f);
    }
}
