shader_type canvas_item;
// NSP 심문 스탠딩 일러용 CRT + 중앙 발광 셰이더 (Godot 4)
// Sprite2D / TextureRect 의 Material 슬롯에 ShaderMaterial 로 걸고 이 셰이더를 지정.
// 원본 밝은 일러 1장이면 되고, uniform 슬라이더로 강도를 조절합니다.

// ── CRT 톤 ──
uniform float strength   : hint_range(0.0, 1.0) = 0.40; // 청록으로 물드는 정도
uniform float darkness   : hint_range(0.3, 1.0) = 0.84; // 전체 밝기 (낮을수록 어두움)
uniform float desat      : hint_range(0.0, 1.0) = 0.18; // 채도 빼기
uniform float scan_amt   : hint_range(0.0, 0.5) = 0.14; // 스캔라인 세기
uniform float scan_freq  : hint_range(100.0, 1200.0) = 600.0; // 스캔라인 촘촘함
uniform float grain_amt  : hint_range(0.0, 0.3) = 0.06; // 노이즈 세기
uniform float vignette   : hint_range(0.0, 1.5) = 0.6;  // 가장자리 어둡게
uniform vec3  tint  = vec3(0.80, 1.00, 1.16);
uniform vec3  cyan  = vec3(0.52, 0.82, 1.02);

// ── 중앙 발광 (CCTV 반사광) ──
uniform float glow_amt    : hint_range(0.0, 3.0) = 0.9;  // 발광 세기 (캐릭터별로 조절)
uniform vec2  glow_center = vec2(0.5, 0.46);             // 빛 중심 (0=위, 1=아래)
uniform float glow_radius : hint_range(0.05, 1.0) = 0.62;// 빛 퍼지는 반경
uniform float glow_soft   : hint_range(0.5, 6.0) = 1.4;  // 낮을수록 넓게 퍼짐
uniform vec3  glow_color  = vec3(0.86, 0.90, 0.96);       // 빛 색 (살짝 따뜻한 흰색)

float rand(vec2 co) {
    return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
    vec4 col = texture(TEXTURE, UV);
    vec3 rgb = col.rgb;

    // 1) 채도 빼기
    float lum = dot(rgb, vec3(0.299, 0.587, 0.114));
    rgb = mix(rgb, vec3(lum), desat);

    // 2) 청록 그레이딩 + 어둡게
    rgb *= tint;
    float l2 = (rgb.r + rgb.g + rgb.b) / 3.0;
    rgb = mix(rgb, l2 * cyan, strength * 0.4);
    rgb *= darkness;

    // 3) 움직이는 스캔라인
    float scan = 1.0 - scan_amt * (0.5 + 0.5 * sin(UV.y * scan_freq + TIME * 6.0));
    rgb *= scan;

    // 4) 움직이는 그레인
    float n = rand(UV + vec2(TIME * 0.71, TIME * 0.53));
    rgb += (n - 0.5) * grain_amt;

    // 5) 비네트
    vec2 d = UV - vec2(0.5);
    float vig = 1.0 - vignette * dot(d, d);
    rgb *= clamp(vig, 0.0, 1.0);

    // 6) 중앙 발광 (톤보정 위에 additive) — 세로로 긴 이미지도 원형 유지
    float aspect = TEXTURE_PIXEL_SIZE.x / TEXTURE_PIXEL_SIZE.y; // = height/width
    vec2 gd = vec2(UV.x - glow_center.x, (UV.y - glow_center.y) * aspect);
    float g = pow(clamp(1.0 - length(gd) / glow_radius, 0.0, 1.0), glow_soft);
    rgb += glow_color * g * glow_amt * col.a; // 캐릭터(알파) 영역에만

    COLOR = vec4(rgb, col.a); // 투명 배경 보존
}
