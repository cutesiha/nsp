using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.View;

// 전력 스위치 패널 — LIGHTING / CCTV / SENSOR 3개의 물리 스위치. 플레이어가 직접 클릭해
// 켜고 끈다(GameState.TryTogglePower). 발전 용량이 부족하면 켜는 시도가 거부된다(다른
// 채널을 먼저 꺼야 함 — 자동으로 대신 꺼주지 않는다). 상태 표시는 GameState를 읽기만
// 한다 — 새 전력 상태를 따로 들고 있지 않는다. Telephone의 Area3D 클릭 방식과 동일.
public partial class PowerSwitchPanel : Node3D
{
    private static readonly (PowerConsumer Channel, string Label, float X)[] Switches =
    {
        (PowerConsumer.Lighting, "LIGHT", -0.09f),
        (PowerConsumer.CctvWatch, "CCTV", 0f),
        (PowerConsumer.Sensor, "SENSOR", 0.09f),
    };

    private readonly Dictionary<PowerConsumer, StandardMaterial3D> _ledMats = new();
    private Label3D _capacityLabel;

    public override void _Ready()
    {
        var bodyMat = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.10f), Roughness = 0.4f, Metallic = 0.2f };
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.09f, 0.22f) },
            MaterialOverride = bodyMat,
        };
        AddChild(body);

        foreach (var (channel, label, x) in Switches)
            BuildSwitch(channel, label, x);

        _capacityLabel = new Label3D
        {
            Text = "POWER 3 / 3",
            Position = new Vector3(0f, 0.052f, -0.08f),
            PixelSize = 0.0008f,
            FontSize = 24,
            OutlineSize = 0,
            Modulate = new Color(0.6f, 0.85f, 0.7f),
        };
        AddChild(_capacityLabel);
    }

    private void BuildSwitch(PowerConsumer channel, string label, float x)
    {
        var ledMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.05f),
            EmissionEnabled = true,
            Emission = new Color(0.15f, 0.95f, 0.3f),
            EmissionEnergyMultiplier = 2.2f,
        };
        _ledMats[channel] = ledMat;

        var led = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.02f, Height = 0.02f, RadialSegments = 10 },
            Position = new Vector3(x, 0.055f, 0.02f),
            MaterialOverride = ledMat,
        };
        AddChild(led);

        var lbl = new Label3D
        {
            Text = label,
            Position = new Vector3(x, 0.052f, 0.075f),
            PixelSize = 0.0006f,
            FontSize = 20,
            OutlineSize = 0,
            Modulate = new Color(0.75f, 0.78f, 0.8f),
        };
        AddChild(lbl);

        var area = new Area3D { InputRayPickable = true, Position = new Vector3(x, 0.05f, 0.02f) };
        var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.07f, 0.08f, 0.09f) } };
        area.AddChild(shape);
        area.InputEvent += (camera, ev, pos, normal, idx) => OnAreaInput(channel, ev);
        AddChild(area);
    }

    private void OnAreaInput(PowerConsumer channel, InputEvent ev)
    {
        if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
        bool ok = GameState.Instance?.TryTogglePower(channel) ?? false;
        Sfx.Instance?.Play("switch", ok ? -6f : -3f, ok ? 1f : 0.7f);
    }

    public override void _Process(double delta)
    {
        var gs = GameState.Instance;
        if (gs == null) return;

        foreach (var (channel, _, _) in Switches)
        {
            bool on = gs.IsConsumerPowered(channel);
            var mat = _ledMats[channel];
            mat.Emission = on ? new Color(0.15f, 0.95f, 0.3f) : new Color(0.5f, 0.05f, 0.03f);
            mat.EmissionEnergyMultiplier = on ? 2.2f : 0.6f;
        }

        _capacityLabel.Text = $"POWER {gs.PowerCapacity} / {Config.Instance.Data.PowerCapacityMax}";
    }
}
