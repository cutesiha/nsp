using Godot;
using NSP.Ui;

namespace NSP.View;

// 손그림 투영기(shaders/analog_paint.gdshader)를 구동한다. 메인 씬의 ControlRoom/AnalogPaint 에 붙는다.
//
// 기획 레퍼런스에서 손으로 칠한 것들을 장면에 붙인다(붓 자국은 따라 그리지 않고 깔끔하게 다듬었다).
//   - 이 노드(풀스크린 사각형) : 전화기 뒤 · 옆 책상의 검은 그림자, 어두운 베젤 · 패드 받침, 청회색 책상 뒤쪽
//   - "analog_ink" 그룹(모니터 앞면 사각형) : 두 모니터 베젤 모서리를 따라 그은 남색 외곽선
// 둘 다 AnalogFilter 와 함께 F8 로 켜고 꺼진다. 방 조명(모니터 빛 · 책상 보조등)이 꺼지면 같이 사라진다
// — 정전 연출에서 칠만 혼자 빛나면 안 된다.
//
// 칠 텍스처는 레퍼런스와 기본 시점 렌더를 비교해 만든다(scripts/debug/AnalogLookShot.cs 로 렌더).
// 기본 시점이 바뀌면(의자 · 카메라 위치) ReferenceCamera 와 텍스처를 다시 뽑아야 한다.
public partial class AnalogPaint : MeshInstance3D
{
    public static AnalogPaint Instance { get; private set; }

    // 레퍼런스를 찍은 카메라(숨쉬기 · 확대 없이 자리에 앉은 기본 시점)의 월드 자세.
    [Export] public Transform3D ReferenceCamera = Transform3D.Identity;
    [Export] public float ReferenceFov = 64f;
    [Export] public float ReferenceAspect = 1700f / 880f;

    // 방 조명 — 이 셋의 세기 합이 레퍼런스 때(ReferenceLightEnergy)보다 줄면 칠의 밝은 부분 · 남색 선도 줄인다.
    [Export] public NodePath[] Lights = { "../Monitor01/M01_ScreenLight", "../Monitor02/M02_ScreenLight", "../DeskFillLight" };
    [Export] public float ReferenceLightEnergy = 6.08f;

    private ShaderMaterial _mat;
    private Light3D[] _lights = System.Array.Empty<Light3D>();
    private bool _enabled = true;

    // F8 과 별개로 칠만 끄고 켤 때(비교 촬영용).
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        _mat = MaterialOverride as ShaderMaterial ?? GetSurfaceOverrideMaterial(0) as ShaderMaterial;
        if (_mat == null) { GD.PushWarning("AnalogPaint: ShaderMaterial 이 없어 손그림을 끕니다."); Visible = false; return; }

        _mat.SetShaderParameter("ref_world_to_view", new Projection(ReferenceCamera.AffineInverse()));
        _mat.SetShaderParameter("ref_tan_half_fov", Mathf.Tan(Mathf.DegToRad(ReferenceFov) * 0.5f));
        _mat.SetShaderParameter("ref_aspect", ReferenceAspect);

        var list = new System.Collections.Generic.List<Light3D>();
        foreach (var p in Lights)
            if (GetNodeOrNull<Light3D>(p) is { } l) list.Add(l);
        _lights = list.ToArray();
    }

    public override void _Process(double delta)
    {
        if (_mat == null) return;
        bool on = _enabled && (AnalogFilter.Instance?.Enabled ?? true);
        Visible = on;

        float energy = 0f;
        foreach (var l in _lights)
            if (IsInstanceValid(l) && l.IsVisibleInTree()) energy += l.LightEnergy;
        float lightScale = _lights.Length == 0 ? 1f : Mathf.Clamp(energy / Mathf.Max(ReferenceLightEnergy, 0.001f), 0f, 1.5f);
        if (on) _mat.SetShaderParameter("light_scale", lightScale);

        foreach (var n in GetTree().GetNodesInGroup("analog_ink"))
        {
            if (n is not GeometryInstance3D ink) continue;
            ink.Visible = on;
            if (on && ink is MeshInstance3D { MaterialOverride: StandardMaterial3D m })
                m.AlbedoColor = new Color(1f, 1f, 1f, Mathf.Clamp(lightScale, 0f, 1f));
        }
    }
}
