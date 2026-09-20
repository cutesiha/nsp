shader_type canvas_item;

// 기절했다 일어나는 몇 초 동안 화면 전체에 씌우는 효과.
// (기립성 저혈압으로 눈앞이 도는 그 느낌 — 시야가 출렁이고, 상이 겹쳐 보이고,
//  가장자리부터 까맣게 좁아졌다가 서서히 풀린다.)
//
// amount 0 이면 화면을 그대로 통과시킨다 — 코드에서는 0 일 때 아예 숨긴다.

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float time_s = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	float a = clamp(amount, 0.0, 1.0);
	float t = time_s;
	vec2 uv = SCREEN_UV;

	// 1) 화면이 통째로 느리게 출렁인다.
	vec2 swim;
	swim.x = sin(uv.y * 5.0 + t * 1.7) * 0.030 + sin(uv.y * 13.0 - t * 2.9) * 0.009;
	swim.y = cos(uv.x * 4.0 - t * 1.3) * 0.022;
	uv += swim * a;

	// 2) 가로로 찢어지는 밴드 — 가끔 한 줄씩 어긋난다.
	float band_id = floor(uv.y * 18.0 + t * 1.5);
	float band = step(0.87, hash(vec2(band_id, floor(t * 7.0))));
	uv.x += band * a * 0.055 * sin(t * 40.0 + band_id);

	uv = clamp(uv, vec2(0.002), vec2(0.998));

	// 3) 초점이 안 맞아 상이 겹쳐 보인다(색까지 어긋난다).
	float sep = a * 0.015;
	vec3 col;
	col.r = texture(screen_tex, clamp(uv + vec2(sep, 0.0), vec2(0.0), vec2(1.0))).r;
	col.g = texture(screen_tex, uv).g;
	col.b = texture(screen_tex, clamp(uv - vec2(sep, sep * 0.5), vec2(0.0), vec2(1.0))).b;
	vec3 ghost = texture(screen_tex, clamp(uv + vec2(-sep * 2.2, sep * 1.6), vec2(0.0), vec2(1.0))).rgb;
	col = mix(col, max(col, ghost), a * 0.55);

	// 4) 거친 노이즈.
	float n = hash(uv * vec2(719.3, 421.7) + fract(t) * 31.0);
	col += (n - 0.5) * a * 0.40;

	// 5) 가장자리부터 시야가 좁아진다.
	vec2 c = SCREEN_UV - vec2(0.5);
	float d = length(c) * 1.42;
	col *= 1.0 - smoothstep(0.40 - a * 0.30, 1.06 - a * 0.44, d) * a;

	COLOR = vec4(col, 1.0);
}
