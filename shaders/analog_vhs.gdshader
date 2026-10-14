shader_type canvas_item;
// 전역 아날로그 호러 필터 — 화면 전체(3D + HUD)를 다시 그린다.
// AnalogFilter(autoload CanvasLayer) 의 풀스크린 ColorRect 에 걸린다.
// 기본값은 shaders/analog_vhs_material.tres 에서 인스펙터 슬라이더로 조절한다.
//
// 색은 3D LUT(shaders/analog_lut.png) 한 장이 정한다. 기획 레퍼런스(원본 스크린샷과
// 직접 보정한 스크린샷 한 쌍)를 픽셀 단위로 비교해 뽑은 표다:
//   밝은 파랑 → 진파랑 · 청록 · 흰색, 어두운 회색 기기 몸체 → 살짝 밝은 청회색(#17262f 계열),
//   더 어두운 곳 → 검정. 빨강 · 노랑 · 초록 같은 강한 색과 진한 남색(모니터 외곽선)은 그대로.
// 손으로 칠한 그림자 · 남색 선은 이 필터가 아니라 3D 쪽(AnalogPaint)이 그린다.
//
// 순서: 트래킹 흔들림 → 색수차 → LUT → 입자 노이즈 → 스캔라인 · 비네트
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

// LUT — 가로 (lut_size × lut_size), 세로 lut_size 의 띠. x = 파랑 칸 × size + 빨강, y = 초록.
// 눈금은 제곱근 간격이다(칸 i = 색 (i / (size-1))²) — 어두운 쪽을 촘촘하게 나눠야
// "아주 어두운 회색은 완전 검정, 조금 밝은 회색은 청회색" 처럼 좁은 구간에서 갈리는 보정을 담을 수 있다.
uniform sampler2D lut_tex : filter_linear, repeat_disable;
uniform float lut_size = 33.0;
uniform float lut_amount : hint_range(0.0, 1.0) = 1.0;

// 입자 노이즈 — 픽셀을 확률적으로 0 / 1 로 튀겨 촘촘한 입자를 만든다(평균 색은 유지).
// 레퍼런스를 부위별로 재 보면 입자는 두 곳에만 낀다:
//   - 조명 받은 밝은 색(파란 책상 · 다이얼 · 화면 속 영상) — 밝고 채도 높을수록 거칠다
//   - 거의 검정인 순수한 파랑(전화기 몸체) — 검정 위에 파란 점이 튄다
// 청회색 기기 · 남색 손그림 선(초록이 섞인 파랑) · 검정 면은 깨끗하다.
uniform float grain : hint_range(0.0, 1.0) = 0.6;              // 입자 양
uniform float grain_bright_lo : hint_range(0.0, 1.0) = 0.25;   // 이 밝기(가장 밝은 채널)부터 입자가 생겨
uniform float grain_bright_hi : hint_range(0.0, 1.0) = 0.55;   // 여기서 최대
uniform float grain_dark_blue : hint_range(0.0, 1.0) = 1.0;    // 검정에 가까운 순수한 파랑의 입자(전화기 몸체)
uniform float grain_px : hint_range(1.0, 6.0) = 1.0;           // 입자 한 알의 크기(화면 픽셀) — 작을수록 촘촘
uniform float grain_fps : hint_range(1.0, 60.0) = 24.0;        // 입자가 바뀌는 속도
uniform float grain_color_split : hint_range(0.0, 1.0) = 0.1;  // 채널마다 따로 튀는 정도(높으면 무지개 점)
// 채널별 입자 세기 — 레퍼런스의 입자는 파랑 · 초록에만 있고 빨강은 거의 고르다(흰 점이 없다).
uniform vec3 grain_rgb = vec3(0.1, 1.0, 1.0);

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
	c = sqrt(clamp(c, vec3(0.0), vec3(1.0))) * (n - 1.0);
	float b0 = floor(c.b);
	float b1 = min(b0 + 1.0, n - 1.0);
	float y = (c.g + 0.5) / n;
	vec3 s0 = texture(lut_tex, vec2((b0 * n + c.r + 0.5) / (n * n), y)).rgb;
	vec3 s1 = texture(lut_tex, vec2((b1 * n + c.r + 0.5) / (n * n), y)).rgb;
	return mix(s0, s1, c.b - b0);
}

float grain_weight(vec3 c) {
	float mx = max(max(c.r, c.g), c.b);
	float mn = min(min(c.r, c.g), c.b);
	float sat = (mx - mn) / max(mx, 0.001);
	float lit = smoothstep(grain_bright_lo, grain_bright_hi, mx) * sat;
	// 파랑이 가장 크고 빨강 · 초록이 거의 없는 어두운 파랑
	float rg = max(c.r, c.g);
	float pure = (1.0 - smoothstep(0.08, 0.2, rg / max(c.b, 0.001))) * step(0.004, c.b) * (1.0 - smoothstep(0.2, 0.35, c.b));
	return max(lit, pure * grain_dark_blue);
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
	float gw = clamp(grain * grain_weight(m) + tracking * 0.3, 0.0, 1.0);
	vec3 col = mix(m, step(r, m), gw * grain_rgb);

	// 스캔라인 · 비네트
	col *= 1.0 - scanline * (0.5 + 0.5 * sin(FRAGCOORD.y * 3.14159));
	vec2 v = SCREEN_UV - vec2(0.5);
	col *= clamp(1.0 - dot(v, v) * vignette * 2.2, 0.0, 1.0);

	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), 1.0);
}
