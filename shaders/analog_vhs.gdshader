shader_type canvas_item;
// 전역 아날로그 호러 필터 — 화면 전체(3D + HUD)를 다시 그린다.
// AnalogFilter(autoload CanvasLayer) 의 풀스크린 ColorRect 에 걸린다.
// 기본값은 shaders/analog_vhs_material.tres 에서 인스펙터 슬라이더로 조절한다.
//
// 색은 3D LUT(shaders/analog_lut.png) 한 장이 정한다. 기획 레퍼런스(원본 스크린샷과
// 직접 보정한 스크린샷 한 쌍)를 픽셀 단위로 비교해 뽑은 표다:
//   밝은 파랑 → 진파랑 · 청록 · 흰색, 어두운 회색 기기 몸체 → 살짝 밝은 청회색(#17262f 계열),
//   더 어두운 곳 → 검정. 빨강 · 노랑 · 초록 같은 강한 색과 진한 남색(모니터 외곽선)은 그대로.
//
// 순서: 트래킹 흔들림 → 색수차 → LUT → 입자 노이즈 → 스캔라인 · 비네트
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

// LUT — 가로 (lut_size × lut_size), 세로 lut_size 의 띠. x = 파랑 칸 × size + 빨강, y = 초록.
uniform sampler2D lut_tex : filter_linear, repeat_disable;
uniform float lut_size = 17.0;
uniform float lut_amount : hint_range(0.0, 1.0) = 1.0;

// 입자 노이즈 — 픽셀을 확률적으로 0 / 1 로 튀겨 촘촘한 입자를 만든다(평균 색은 유지).
// 밝고 채도 높은 곳(조명 받은 파란 책상 · 다이얼)에만 생기고, 검정 · 청회색 기기 몸체는 깨끗하다.
uniform float grain : hint_range(0.0, 1.0) = 0.6;              // 입자 양
uniform float grain_px : hint_range(1.0, 6.0) = 1.0;           // 입자 한 알의 크기(화면 픽셀) — 작을수록 촘촘
uniform float grain_fps : hint_range(1.0, 60.0) = 24.0;        // 입자가 바뀌는 속도
uniform float grain_color_split : hint_range(0.0, 1.0) = 0.1;  // 채널마다 따로 튀는 정도(높으면 무지개 점)
uniform float grain_min : hint_range(0.0, 0.2) = 0.01;         // 이 '채도 × 밝기' 부터 입자가 생기기 시작
uniform float grain_full : hint_range(0.01, 0.5) = 0.08;       // 이 값에서 입자 양이 최대

uniform float aberration : hint_range(0.0, 0.01) = 0.0008;
uniform float scanline : hint_range(0.0, 0.5) = 0.0;
uniform float vignette : hint_range(0.0, 1.5) = 0.2;

// 공포 연출 때 코드(AnalogFilter.cs)가 0 → 1 로 끌어올린다.
uniform float tracking : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

vec3 lut_lookup(vec3 c) {
	float n = lut_size;
	c = clamp(c, vec3(0.0), vec3(1.0)) * (n - 1.0);
	float b0 = floor(c.b);
	float b1 = min(b0 + 1.0, n - 1.0);
	float y = (c.g + 0.5) / n;
	vec3 s0 = texture(lut_tex, vec2((b0 * n + c.r + 0.5) / (n * n), y)).rgb;
	vec3 s1 = texture(lut_tex, vec2((b1 * n + c.r + 0.5) / (n * n), y)).rgb;
	return mix(s0, s1, c.b - b0);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	float t = TIME;

	// VHS 트래킹 — 평소엔 거의 없고, tracking 이 오르면 가로줄이 흔들리고 굵은 띠가 지나간다.
	float row = floor(uv.y * 240.0);
	float jitter = (hash(vec2(row, floor(t * 24.0))) - 0.5) * 0.0012;
	float band = smoothstep(0.96, 1.0, sin(uv.y * 5.0 - t * 1.3) * 0.5 + 0.5);
	uv.x += jitter * tracking * 4.0 + band * tracking * 0.02;

	// 색수차
	float ab = aberration * (1.0 + tracking * 3.0);
	vec3 src = vec3(
		texture(screen_tex, uv + vec2(ab, 0.0)).r,
		texture(screen_tex, uv).g,
		texture(screen_tex, uv - vec2(ab, 0.0)).b);

	// 색 — LUT
	vec3 m = mix(src, lut_lookup(src), lut_amount);

	// 입자 노이즈
	vec2 cell = floor(FRAGCOORD.xy / grain_px);
	float frame = floor(t * grain_fps);
	vec2 seed = cell + vec2(frame * 17.13, frame * 7.71);
	vec3 r = mix(vec3(hash(seed)), vec3(hash(seed + 31.7), hash(seed + 57.3), hash(seed + 91.1)), grain_color_split);
	float mx = max(max(m.r, m.g), m.b);
	float sl = (mx - min(min(m.r, m.g), m.b)) * mx;
	float gw = grain * clamp((sl - grain_min) / max(grain_full - grain_min, 0.001), 0.0, 1.0);
	gw = clamp(gw + tracking * 0.3, 0.0, 1.0);
	vec3 col = mix(m, step(r, m), gw);

	// 스캔라인 · 비네트
	col *= 1.0 - scanline * (0.5 + 0.5 * sin(FRAGCOORD.y * 3.14159));
	vec2 v = SCREEN_UV - vec2(0.5);
	col *= clamp(1.0 - dot(v, v) * vignette * 2.2, 0.0, 1.0);

	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), 1.0);
}
