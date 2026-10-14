shader_type canvas_item;
// 전역 아날로그 호러 필터 — 화면 전체(3D + HUD)를 "녹화된 테이프" 처럼 만든다.
// AnalogFilter(autoload CanvasLayer) 의 풀스크린 ColorRect 에 걸린다.
// 기본값은 shaders/analog_vhs_material.tres 에서 인스펙터 슬라이더로 조절한다.
//
// 순서: 트래킹 흔들림 → 색수차 · 가로 번짐 → 색보정(블랙 크러시 · 대비 · 채도 · 명암별 색조)
//      → 색 단계 줄이기 + 디더 → 스캔라인 · 그레인 · 비네트
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

// 색보정
uniform float black_crush : hint_range(0.0, 0.3) = 0.04;    // 이 밝기 이하는 완전 검정
uniform float contrast : hint_range(0.5, 2.0) = 1.25;
uniform float saturation : hint_range(0.0, 2.0) = 1.3;
uniform vec3 shadow_tint : source_color = vec3(0.85, 0.75, 0.95);    // 어두운 곳에 곱할 색 (보라빛)
uniform vec3 highlight_tint : source_color = vec3(1.0, 0.86, 0.68);  // 밝은 곳에 곱할 색 (앰버)
uniform float tint_amount : hint_range(0.0, 1.0) = 1.0;

// 아날로그 질감
uniform float color_levels : hint_range(4.0, 64.0) = 24.0;  // 채널당 색 단계. 낮을수록 거칠다
uniform float aberration : hint_range(0.0, 0.01) = 0.0012;  // 색수차(R/B 가로 어긋남, 화면 너비 비율)
uniform float bleed : hint_range(0.0, 0.01) = 0.0012;       // 빨강이 오른쪽으로 번지는 정도
uniform float grain : hint_range(0.0, 0.3) = 0.05;
uniform float scanline : hint_range(0.0, 0.5) = 0.08;
uniform float vignette : hint_range(0.0, 1.5) = 0.35;       // AmbientOverlay 비네트와 겹치므로 약하게

// 공포 연출 때 코드(AnalogFilter.cs)가 0 → 1 로 끌어올린다.
uniform float tracking : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	float t = TIME;

	// VHS 트래킹 — 가로줄마다 미세하게 흔들리고, tracking 이 오르면 굵은 띠가 지나간다.
	float row = floor(uv.y * 240.0);
	float jitter = (hash(vec2(row, floor(t * 24.0))) - 0.5) * 0.0012;
	float band = smoothstep(0.96, 1.0, sin(uv.y * 5.0 - t * 1.3) * 0.5 + 0.5);
	uv.x += jitter * (0.3 + tracking * 4.0) + band * tracking * 0.02;

	// 색수차 + 가로 번짐. tracking 이 오르면 어긋남도 커진다.
	float ab = aberration * (1.0 + tracking * 3.0);
	float r = (texture(screen_tex, uv + vec2(ab, 0.0)).r
			+ texture(screen_tex, uv + vec2(ab + bleed, 0.0)).r) * 0.5;
	float g = texture(screen_tex, uv).g;
	float b = texture(screen_tex, uv - vec2(ab, 0.0)).b;
	vec3 col = vec3(r, g, b);

	// 색보정
	col = max(col - vec3(black_crush), vec3(0.0)) / (1.0 - black_crush);
	col = (col - vec3(0.5)) * contrast + vec3(0.5);
	col = clamp(col, vec3(0.0), vec3(1.0));
	float luma = dot(col, vec3(0.299, 0.587, 0.114));
	col = mix(vec3(luma), col, saturation);
	vec3 tone = mix(shadow_tint, highlight_tint, smoothstep(0.05, 0.6, luma));
	col *= mix(vec3(1.0), tone, tint_amount);

	// 색 단계 줄이기 + 디더 — 저비트 아날로그 질감, 어두운 그라데이션에 계단이 생긴다.
	float d = hash(FRAGCOORD.xy) - 0.5;
	col = floor(clamp(col, vec3(0.0), vec3(1.0)) * color_levels + vec3(0.5 + d)) / color_levels;

	// 스캔라인(한 줄 걸러) · 그레인 · 비네트
	col *= 1.0 - scanline * (0.5 + 0.5 * sin(FRAGCOORD.y * 3.14159));
	col += (hash(uv * vec2(913.0, 541.0) + fract(t) * 37.0) - 0.5) * grain * (1.0 + tracking);
	vec2 v = SCREEN_UV - vec2(0.5);
	col *= clamp(1.0 - dot(v, v) * vignette * 2.2, 0.0, 1.0);

	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), 1.0);
}
