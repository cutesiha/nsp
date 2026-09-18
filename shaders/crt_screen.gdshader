shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;

// 왼쪽/오른쪽 CRT 모니터가 같은 SubViewport(전체 게임 화면)를 각자 다른 영역만
// 잘라서 보여준다. region_min/max 는 0~1 정규화된 잘라낼 사각형.
uniform sampler2D screen_tex : source_color, filter_linear, hint_default_black;
uniform vec2 region_min = vec2(0.0, 0.0);
uniform vec2 region_max = vec2(1.0, 1.0);

uniform float scanline_strength : hint_range(0.0, 1.0) = 0.06;
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.20;
uniform float noise_strength : hint_range(0.0, 0.5) = 0.016;
uniform float flicker_strength : hint_range(0.0, 1.0) = 0.02;
uniform float h_distortion : hint_range(0.0, 1.0) = 0.0;   // 공포 연출 시 코드에서 끌어올림
uniform float brightness : hint_range(0.0, 2.0) = 1.0;      // 정전/암전 시 0 쪽으로
uniform float tint_r : hint_range(0.0, 2.0) = 1.0;
uniform float tint_g : hint_range(0.0, 2.0) = 1.0;
uniform float tint_b : hint_range(0.0, 2.0) = 1.0;

float rand(vec2 co) {
	return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 uv = clamp(UV, 0.0, 1.0);

	// 간헐적 가로 왜곡 밴드
	float t = TIME;
	float band = smoothstep(0.965, 1.0, sin(uv.y * 8.0 - t * 3.0) * 0.5 + 0.5);
	uv.x += band * h_distortion * 0.04 * sin(t * 40.0);

	vec2 src = mix(region_min, region_max, uv);
	vec3 col = texture(screen_tex, src).rgb;

	// 스캔라인
	float sl = sin(uv.y * 900.0) * 0.5 + 0.5;
	col *= 1.0 - scanline_strength * sl;

	// 정적 노이즈
	float n = rand(uv * vec2(680.0, 480.0) + fract(t) * 17.0);
	col += (n - 0.5) * noise_strength;

	// 미세 플리커
	col *= 1.0 - flicker_strength * rand(vec2(floor(t * 30.0), 0.0));

	// 가장자리 어두움
	vec2 vc = uv - 0.5;
	float vig = clamp(1.0 - dot(vc, vc) * vignette_strength * 4.0, 0.0, 1.0);
	col *= vig;

	col *= vec3(tint_r, tint_g, tint_b) * brightness;

	ALBEDO = col;
}
