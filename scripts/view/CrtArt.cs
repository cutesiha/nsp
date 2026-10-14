using Godot;

namespace NSP.View;

// CRT 화면에 올리는 **캐릭터 원화**를 한 곳에서 손본다.
//
// 원화는 밝은 파스텔(하늘색 · 연보라 · 흰 옷 · 흰 바탕)로 그려져 있다. 모니터 화면은
// 자기발광(crt_screen 의 EMISSION)이라 그 위에 번짐(Glow)과 전역 아날로그 LUT 가
// 차례로 얹힌다. 그대로 올리면 두 가지가 같이 일어난다.
//
//   · 밝은 쪽은 실루엣 둘레에 흰 테가 생기며 날아간다(번짐)
//   · 연한 면은 전부 **같은 파랑 한 덩어리**가 된다(LUT) — 눈 · 수염 · 선이 사라진다
//
// 처음에는 Modulate 로 어둡게만 눌렀는데, 그것만으로는 두 번째가 안 고쳐진다.
// 밝기를 같은 비율로 내리면 색 사이의 **간격도 같이** 줄어 오히려 더 안 보인다.
// 그래서 셰이더로 채도를 낮추고 대비를 올려 간격을 벌린 뒤, 마지막에만 살짝 누른다.
//
// 세기를 바꾸려면 shaders/crt_art.gdshader 의 uniform 기본값을 고친다.
public static class CrtArt
{
    private const string ShaderPath = "res://shaders/crt_art.gdshader";

    // 셰이더를 걸 수 없는 자리(_Draw 로 직접 그리는 화면)가 쓰는 색.
    // 거기서는 간격을 벌릴 수 없으니 **거의 누르지 않는다** — 누를수록 더 안 보인다.
    public static readonly Color Tint = new(0.88f, 0.91f, 0.94f);

    // 원화 뒤에 까는 어두운 받침 — 대비를 받쳐 준다.
    public static readonly Color Backing = new(0.04f, 0.05f, 0.07f, 0.85f);

    private static ShaderMaterial _shared;

    // TextureRect 에 걸 머티리얼. 한 장을 모두가 나눠 쓴다(값이 전부 같다).
    // 셰이더가 없으면 null — 부르는 쪽은 그냥 Modulate 만 걸고 넘어간다.
    public static ShaderMaterial Material()
    {
        if (_shared != null && GodotObject.IsInstanceValid(_shared)) return _shared;
        if (!ResourceLoader.Exists(ShaderPath)) return null;
        var shader = GD.Load<Shader>(ShaderPath);
        if (shader == null) return null;
        _shared = new ShaderMaterial { Shader = shader };
        return _shared;
    }

    // 원화를 올리는 TextureRect 하나를 CRT 용으로 맞춘다.
    public static void Apply(TextureRect rect)
    {
        if (rect == null) return;
        var mat = Material();
        if (mat != null) rect.Material = mat;
        else rect.Modulate = Tint;   // 셰이더가 없을 때의 대비책
    }
}
