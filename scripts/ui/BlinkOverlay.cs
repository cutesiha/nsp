using System.Threading.Tasks;
using Godot;

namespace NSP.Ui;

// "관리자가 잠시 눈을 감았다 뜬다" — 화면 가장자리부터 어두워져 중앙까지 덮고, 역순으로 열린다.
//
// 눈꺼풀 그림을 위아래에서 내리지 않는다(지시서 §2). 그런 연출은 1인칭 관제실 화면에
// 얹으면 만화가 된다. 여기서 하는 일은 하나뿐이다 — **비네트를 중앙까지 조여 들어간다.**
//
// 구현은 비네트 텍스처 한 장의 scale 이다. 어두운 고리가 바깥에 있을 때는 화면 밖이라
// 보이지 않고, scale 을 줄이면 그 고리가 중앙으로 밀려 들어오며 시야가 좁아진다.
// 마지막 구간만 검은 판으로 완전히 덮는다 — 고리만으로는 중앙에 작은 구멍이 남는다.
//
// **반드시 열린다.** 예외 · 씬 전환 · 강제 종료 어느 쪽으로 빠져나가도 OpenNow() 를
// 지나가거나 _ExitTree 가 치운다(§10 fail-safe). 닫힌 채로 남으면 그 뒤로 아무것도 안 보인다.
public partial class BlinkOverlay : CanvasLayer
{
    public static BlinkOverlay Instance { get; private set; }

    // 지금 눈이 감겨 있는가(검사 · fail-safe 판정용). 뜨는 동작이 끝나야 false 가 된다.
    public bool IsClosed { get; private set; }

    // 지금 뜨는 중인가. 감는 중과 뜨는 중은 ClosedAmount 만으로는 구분되지 않는다.
    public bool IsOpening { get; private set; }
    // 셰이더 한 장. 화면 가운데에서의 거리가 cutoff 를 넘는 픽셀부터 검게 칠한다.
    //
    // 예전에는 비네트 텍스처를 축소해서 조이려 했는데, TextureRect 는 **자기 사각형 안만**
    // 칠한다 — 줄이면 바깥이 아예 안 칠해져서 화면 가장자리가 오히려 멀쩡히 보였다.
    // 전체 화면을 덮는 사각형 하나에 셰이더를 걸어야 "바깥부터 안으로" 가 된다.
    private const string ShaderCode = @"
shader_type canvas_item;
// 2.1 = 완전히 열림(모서리까지 투명) · 0.0 = 완전히 닫힘(전부 검정)
uniform float cutoff : hint_range(0.0, 2.2) = 2.1;
uniform float softness : hint_range(0.05, 1.0) = 0.45;
void fragment() {
    vec2 p = (UV - vec2(0.5)) * 2.0;
    float d = length(p);
    float a = smoothstep(cutoff - softness, cutoff, d);
    COLOR = vec4(0.0, 0.0, 0.0, a);
}
";

    // 모서리까지 완전히 투명해지는 값(정규화 좌표에서 모서리 거리는 √2 ≈ 1.414).
    private const float OpenCutoff = 2.1f;
    private const float ShutCutoff = 0.0f;

    private ColorRect _veil;
    private ShaderMaterial _mat;
    private Tween _tween;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        // 공포 연출 오버레이(HorrorDirector = 128)보다 위. 눈을 감으면 그 위의 노이즈도 덮인다.
        Layer = 160;

        _mat = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        _mat.SetShaderParameter("cutoff", OpenCutoff);
        _mat.SetShaderParameter("softness", 0.45f);

        _veil = new ColorRect
        {
            Color = Colors.White,            // 색은 셰이더가 정한다
            Material = _mat,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _veil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_veil);
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    // 지금 눈꺼풀이 얼마나 닫혔는가(0 = 열림, 1 = 완전 암전). 검사가 읽는다.
    public float ClosedAmount =>
        _mat == null ? 0f
        : Mathf.Clamp(1f - (float)_mat.GetShaderParameter("cutoff") / OpenCutoff, 0f, 1f);

    // 눈을 감는다. 끝나면 화면은 완전한 검정이다.
    public async Task Close(double seconds = 0.45)
    {
        IsClosed = true;
        IsOpening = false;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(SetCutoff), Cutoff, ShutCutoff, seconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        await ToSignal(_tween, Tween.SignalName.Finished);
    }

    // 눈을 뜬다.
    public async Task Open(double seconds = 0.45)
    {
        IsOpening = true;
        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenMethod(Callable.From<float>(SetCutoff), Cutoff, OpenCutoff, seconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        await ToSignal(_tween, Tween.SignalName.Finished);
        IsClosed = false;
        IsOpening = false;
    }

    // 트윈을 기다리지 않고 즉시 전부 걷는다. fail-safe 전용 — 연출이 아니다.
    public void OpenNow()
    {
        _tween?.Kill();
        _tween = null;
        IsClosed = false;
        IsOpening = false;
        SetCutoff(OpenCutoff);
    }

    private float Cutoff => _mat == null ? OpenCutoff : (float)_mat.GetShaderParameter("cutoff");

    private void SetCutoff(float v) => _mat?.SetShaderParameter("cutoff", v);
}
