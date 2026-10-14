shader_type canvas_item;
// 전역 아날로그 호러 필터 — 화면 전체(3D + HUD)를 다시 그린다.
// AnalogFilter(autoload CanvasLayer) 의 풀스크린 ColorRect 에 걸린다.
// 기본값은 shaders/analog_vhs_material.tres 에서 인스펙터 슬라이더로 조절한다.
//
// 색은 3D LUT(shaders/analog_lut.png) 한 장이 정한다. 기획 레퍼런스(원본 스크린샷과
// 직접 보정한 스크린샷 한 쌍)를 픽셀 단위로 비교해 뽑은 표다:
//   검정 → 진파랑 → 청록 → 흰색으로 뭉개지고, 빨강 · 노랑 · 초록 같은 강한 색은 살아남는다.
//
// 순서: 트래킹 흔들림 → 색수차 → LUT → 디더 노이즈 → 손그림 잉크 선 → 스캔라인 · 비네트
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
// 잉크 선 판정용 — 밉맵으로 흐린 화면. 글자 같은 잔디테일은 빼고 큰 윤곽만 잡는다.
uniform sampler2D screen_blur : hint_screen_texture, filter_linear_mipmap;

// LUT — 가로 (lut_size × lut_size), 세로 lut_size 의 띠. x = 파랑 칸 × size + 빨강, y = 초록.
uniform sampler2D lut_tex : filter_linear, repeat_disable;
uniform float lut_size = 17.0;
uniform float lut_amount : hint_range(0.0, 1.0) = 1.0;

// 디더 노이즈 — 각 픽셀을 확률적으로 0 / 1 로 밀어 거친 입자를 만든다(평균 색은 유지).
uniform float dither : hint_range(0.0, 1.0) = 0.85;
uniform float grain_px : hint_range(1.0, 6.0) = 2.0;        // 입자 한 알의 크기(화면 픽셀)
uniform float grain_fps : hint_range(1.0, 60.0) = 24.0;     // 입자가 바뀌는 속도
uniform float grain_color_split : hint_range(0.0, 1.0) = 0.05; // 채널마다 따로 튀는 정도(높으면 무지개 점)

// 손그림 잉크 선 — 큰 윤곽을 따라 흔들리는 남색 선. 밝은 곳(글자 · UI)은 덮지 않는다.
uniform float ink_amount : hint_range(0.0, 1.0) = 0.8;
uniform vec3 ink_color : source_color = vec3(0.05, 0.07, 0.32);
uniform float ink_width_px : hint_range(1.0, 12.0) = 5.0;
uniform float ink_threshold : hint_range(0.0, 0.5) = 0.05;
uniform float ink_wobble_px : hint_range(0.0, 20.0) = 6.0;
uniform float ink_fps : hint_range(1.0, 24.0) = 8.0;         // 선이 다시 그려지는 속도(손그림 '끓는' 느낌)
uniform float ink_blur_lod : hint_range(0.0, 5.0) = 3.0;

uniform float aberration : hint_range(0.0, 0.01) = 0.0008;
uniform float scanline : hint_range(0.0, 0.5) = 0.0;
uniform float vignette : hint_range(0.0, 1.5) = 0.2;

// 공포 연출 때 코드(AnalogFilter.cs)가 0 → 1 로 끌어올린다.
uniform float tracking : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash(i);
	float b = hash(i + vec2(1.0, 0.0));
	float c = hash(i + vec2(0.0, 1.0));
	float d = hash(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
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

float blur_luma(vec2 uv) {
	return dot(textureLod(screen_blur, uv, ink_blur_lod).rgb, vec3(0.299, 0.587, 0.114));
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 px = SCREEN_PIXEL_SIZE;
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

	// 디더 노이즈 — 채널이 거의 같은 난수를 써서, 어두운 곳엔 파란 점만 드문드문 튄다.
	vec2 cell = floor(FRAGCOORD.xy / grain_px);
	float frame = floor(t * grain_fps);
	vec2 seed = cell + vec2(frame * 17.13, frame * 7.71);
	float shared_r = hash(seed);
	vec3 own_r = vec3(hash(seed + 31.7), hash(seed + 57.3), hash(seed + 91.1));
	vec3 r = mix(vec3(shared_r), own_r, grain_color_split);
	vec3 col = mix(m, step(r, m), dither * (1.0 + tracking * 0.2));

	// 손그림 잉크 선 — 흐린 화면의 큰 윤곽을, 천천히 끓는 흔들림으로 비틀어서 따라 그린다.
	if (ink_amount > 0.0) {
		float iframe = floor(t * ink_fps);
		vec2 wob = vec2(vnoise(uv * vec2(28.0, 16.0) + iframe * 0.37),
						vnoise(uv * vec2(28.0, 16.0) + 13.0 + iframe * 0.53)) - 0.5;
		vec2 iuv = uv + wob * ink_wobble_px * 2.0 * px;
		vec2 w = ink_width_px * px;
		float gx = blur_luma(iuv - vec2(w.x, 0.0)) - blur_luma(iuv + vec2(w.x, 0.0));
		float gy = blur_luma(iuv - vec2(0.0, w.y)) - blur_luma(iuv + vec2(0.0, w.y));
		float edge = sqrt(gx * gx + gy * gy);
		float a = clamp((edge - ink_threshold) / 0.06, 0.0, 1.0) * ink_amount;
		// 글자 · UI 처럼 밝은 픽셀은 덮지 않는다(읽혀야 한다).
		float bright = max(max(src.r, src.g), src.b);
		a *= 1.0 - clamp((bright - 0.25) / 0.25, 0.0, 1.0);
		col = mix(col, ink_color, a);
	}

	// 스캔라인 · 비네트
	col *= 1.0 - scanline * (0.5 + 0.5 * sin(FRAGCOORD.y * 3.14159));
	vec2 v = SCREEN_UV - vec2(0.5);
	col *= clamp(1.0 - dot(v, v) * vignette * 2.2, 0.0, 1.0);

	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), 1.0);
}
