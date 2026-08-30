using Godot;
using NSP.Core;

namespace NSP.View;

// 책상 위 소형 경고 단말기 — 오래된 산업용 장비 느낌의 작은 화면 + 경고등. 판정 로직은
// 전혀 없다. AlertTerminalView(SENSOR 화면 내용)를 SubViewport에 넣어 작은 화면 쿼드에
// 그대로 투사한다(DeskScheduleBoard와 동일한 방식) — 항상 켜져 있고 근무 배치 단계에서도
// 치우지 않는다(휴게/배치 중에는 자연히 "정상" 상태만 보인다).
public partial class AlertTerminalProp : Node3D
{
    private AlertTerminalView _ui;
    private StandardMaterial3D _lampMat;
    private float _blinkPhase;

    public override void _Ready()
    {
        var bodyMat = new StandardMaterial3D { AlbedoColor = new Color(0.10f, 0.10f, 0.11f), Roughness = 0.55f, Metallic = 0.1f };
        var body = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.20f, 0.16f, 0.03f) },
            Position = new Vector3(0f, 0.09f, 0f),
            RotationDegrees = new Vector3(-20f, 0f, 0f),
            MaterialOverride = bodyMat,
        };
        AddChild(body);

        var vp = new SubViewport
        {
            Size = new Vector2I(240, 160),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            Disable3D = true,
            TransparentBg = false,
        };
        AddChild(vp);

        _ui = new AlertTerminalView();
        vp.AddChild(_ui);

        var screen = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.17f, 0.115f) },
            Position = new Vector3(0f, 0.105f, 0.0155f),
            RotationDegrees = new Vector3(-20f, 0f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoTexture = vp.GetTexture(),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            },
        };
        AddChild(screen);

        _lampMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.05f),
            EmissionEnabled = true,
            Emission = new Color(0.2f, 0.9f, 0.3f),
            EmissionEnergyMultiplier = 1.2f,
        };
        var lamp = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 10, Rings = 6 },
            Position = new Vector3(0.085f, 0.155f, 0.006f),
            MaterialOverride = _lampMat,
        };
        AddChild(lamp);
    }

    public override void _Process(double delta)
    {
        if (_ui == null || _lampMat == null) return;

        var sev = _ui.CurrentSeverity;
        bool failure = _ui.InFailureFlash;
        float freq = failure ? 10f : sev == AlertSeverity.Critical ? 6f : sev == AlertSeverity.Warning ? 2.5f : 0f;
        _blinkPhase += (float)delta * freq;
        float k = freq > 0f ? 0.5f + 0.5f * Mathf.Sin(_blinkPhase) : 1f;

        Color baseCol = failure ? new Color(0.95f, 0.1f, 0.08f)
            : sev == AlertSeverity.Critical ? new Color(0.95f, 0.55f, 0.1f)
            : sev == AlertSeverity.Warning ? new Color(0.9f, 0.75f, 0.15f)
            : new Color(0.2f, 0.9f, 0.3f);

        _lampMat.Emission = baseCol;
        _lampMat.EmissionEnergyMultiplier = freq > 0f ? 0.8f + 2.2f * k : 1.2f;
    }
}
