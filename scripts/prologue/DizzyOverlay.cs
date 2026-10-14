using Godot;

namespace NSP.Prologue;

// 기절했다 일어날 때 화면 전체를 덮는 어지러움 오버레이.
//
// 기존 SetScreenNoise/SetScreenDistortion 은 CRT 모니터 두 대의 표면에만 걸리는 값이라
// "화면이 일그러진다"가 실제 시야에는 거의 보이지 않았다. 여기서는 화면 텍스처를
// 통째로 다시 그려 시야 자체를 흔든다.
//
// Amount 0 이면 노드 자체를 숨긴다 — 평소에는 전체화면 블렌드 비용이 전혀 없다.
public partial class DizzyOverlay : CanvasLayer
{
    public static DizzyOverlay Instance { get; private set; }

    private const string ShaderPath = "res://shaders/dizzy.gdshader";

    private ColorRect _rect;
    private ShaderMaterial _mat;
    private float _amount;
    private float _clock;

    public override void _Ready()
    {
        Instance = this;
        // 분위기 오버레이(100)·HUD(110~115) 위. 시야 자체가 도는 연출이라 전부 같이 돈다.
        Layer = 118;
        Visible = false;

        var shader = GD.Load<Shader>(ShaderPath);
        if (shader == null)
        {
            GD.PushWarning($"DizzyOverlay: {ShaderPath} 를 찾지 못했습니다. 어지러움 연출을 건너뜁니다.");
            SetProcess(false);
            return;
        }

        _mat = new ShaderMaterial { Shader = shader };
        _rect = new ColorRect
        {
            Color = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = _mat,
        };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 0 = 평소, 1 = 눈앞이 완전히 도는 상태.
    public void SetAmount(float amount)
    {
        _amount = Mathf.Clamp(amount, 0f, 1f);
        bool on = _mat != null && _amount > 0.004f;
        if (Visible != on) Visible = on;
        _mat?.SetShaderParameter("amount", _amount);
    }

    public void Clear() => SetAmount(0f);

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _clock += (float)delta;
        _mat?.SetShaderParameter("time_s", _clock);
    }
}
