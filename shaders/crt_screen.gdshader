shader_type spatial;
// 화면 색을 자기발광(EMISSION)으로 내보낸다 — WorldEnvironment Glow 가 화면 사각형 전체에 걸리게.
// 방 조명은 받지 않는다(ALBEDO 0, 반사 0) → emission_energy 1.0 이면 예전 unshaded 와 같은 밝기.
render_mode cull_back, shadows_disabled, specular_disabled, ambient_light_disabled;

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
// 발광 세기. 1.0 = 예전 밝기. 1 을 넘는 만큼만 Glow(번짐)가 생긴다 — 올리면 흰 글자부터 번지므로 조금씩.
uniform float emission_energy : hint_range(0.0, 4.0) = 1.0;
// 켜진 CRT 의 형광 바탕빛 — 검정 UI 배경도 "전원이 들어온 유리"처럼 은은하게 빛나게. 가운데가 조금 더 밝다.
uniform vec3 phosphor_base = vec3(0.0, 0.0, 0.0);   // 0 = 끔. 켜진 유리 느낌은 화면 UI 쪽 바탕(CrtGlass)이 담당한다
uniform float phosphor_center : hint_range(0.0, 1.0) = 0.35;
// 바탕빛은 UI 가 없는 검정 바탕에만 깐다 — 이 밝기(휘도)를 넘는 패널 · 선 · 글자는 원래 색 그대로 위에 올라간다.
uniform float phosphor_cut : hint_range(0.0, 0.3) = 0.045;
// 노이즈 배율 — 코드가 넣는 noise_strength(평소 0.02~0.035, 공포 연출 때 크게)에 곱한다.
uniform float noise_scale : hint_range(0.0, 1.0) = 0.35;

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
	// 바탕 판정은 원본 픽셀로(노이즈 · 스캔라인이 섞이기 전) — 경계가 흔들리지 않게.
	float raw_lum = dot(col, vec3(0.299, 0.587, 0.114));

	// 스캔라인
	float sl = sin(uv.y * 900.0) * 0.5 + 0.5;
	col *= 1.0 - scanline_strength * sl;

	// 정적 노이즈
	float n = rand(uv * vec2(680.0, 480.0) + fract(t) * 17.0);
	col += (n - 0.5) * noise_strength * noise_scale;

	// 미세 플리커
	col *= 1.0 - flicker_strength * rand(vec2(floor(t * 30.0), 0.0));

	// 형광 바탕빛 — 검정 바탕에만. UI(패널 · 선 · 글자)는 건드리지 않아 바탕빛 위에 또렷하게 얹힌다.
	vec2 vc = uv - 0.5;
	float center = 1.0 + phosphor_center * (1.0 - clamp(dot(vc, vc) * 4.0, 0.0, 1.0));
	float bg_mask = 1.0 - smoothstep(phosphor_cut, phosphor_cut + 0.012, raw_lum);
	col += phosphor_base * center * bg_mask;

	// 가장자리 어두움
	float vig = clamp(1.0 - dot(vc, vc) * vignette_strength * 4.0, 0.0, 1.0);
	col *= vig;

	col *= vec3(tint_r, tint_g, tint_b) * brightness;

	ALBEDO = vec3(0.0);
	ROUGHNESS = 1.0;
	EMISSION = col * emission_energy;
}
