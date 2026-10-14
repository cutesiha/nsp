using Godot;

namespace NSP.Ui;

// 전역 아날로그 호러 필터(autoload CanvasLayer).
//
// 화면 전체를 한 번 더 읽어서 VHS 테이프처럼 다시 그린다 — 색수차 · 가로 번짐 · 블랙 크러시 ·
// 대비/채도 · 색 단계 줄이기 · 스캔라인 · 그레인. AmbientOverlay 는 이미지를 위에 덮기만 해서
// 이런 효과를 낼 수 없었다(화면 픽셀을 읽어야 한다).
//
// 레이어 119 — HUD(110) · 통화창(114) · 로그(115) · 단서(116) · 패드 힌트(117) 까지 같은 테이프
// 질감을 받고, ESC 메뉴(120) · 경보(122) · 설정(130) 은 필터 위에 또렷하게 뜬다.
//
// 값 조절: shaders/analog_vhs_material.tres 를 인스펙터에서 열면 슬라이더로 바로 바뀐다.
// 전/후 비교: F7 로 켜고 끈다.
public partial class AnalogFilter : CanvasLayer
{
    public static AnalogFilter Instance { get; private set; }

    private const string MaterialPath = "res://shaders/analog_vhs_material.tres";
    private const Key ToggleKey = Key.F7;

    private ColorRect _rect;
    private ShaderMaterial _mat;
    private float _tracking;

    public bool Enabled
    {
        get => _rect?.Visible ?? false;
        set { if (_rect != null) _rect.Visible = value; }
    }

    public override void _EnterTree() => Instance = this;

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public override void _Ready()
    {
        Layer = 119;
        ProcessMode = ProcessModeEnum.Always;   // 일시정지 중에도 그레인이 멈추지 않게

        _mat = GD.Load<ShaderMaterial>(MaterialPath);
        if (_mat == null) { GD.PushWarning($"AnalogFilter: {MaterialPath} 을 찾지 못해 필터를 끕니다."); return; }

        _rect = new ColorRect
        {
            Color = Colors.White,            // 색은 셰이더가 정한다
            Material = _mat,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    public override void _Input(InputEvent e)
    {
        if (_rect == null) return;
        if (e is InputEventKey { Pressed: true, Echo: false } k && k.Keycode == ToggleKey)
            Enabled = !Enabled;
    }

    public override void _Process(double delta)
    {
        if (_mat == null || !Enabled) return;

        // 공포 연출이 AmbientOverlay 의 노이즈를 끌어올리면(PulseNoise · SetGhostNoise · 정전)
        // 테이프도 같이 찢어진다. 올라갈 땐 빠르게, 내려갈 땐 천천히.
        float target = Mathf.Clamp((AmbientOverlay.Instance?.Disturbance ?? 0f) * 2.5f, 0f, 1f);
        float k = Mathf.Clamp((float)delta * (target > _tracking ? 12f : 1.5f), 0f, 1f);
        _tracking = Mathf.Lerp(_tracking, target, k);
        _mat.SetShaderParameter("tracking", _tracking);
    }
}
