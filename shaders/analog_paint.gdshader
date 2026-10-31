shader_type spatial;
// 손그림 투영기 — 기획 레퍼런스(직접 보정한 스크린샷)에 손으로 칠한 그림자 · 어두운 기기 몸체를
// 3D 장면에 그대로 붙인다. AnalogPaint.cs 가 붙은 풀스크린 사각형에 걸린다.
//
// 레퍼런스는 "자리에 앉은 기본 시점" 한 장이다. 화면 픽셀마다 깊이로 월드 좌표를 되살리고,
// 그 점을 기본 시점 카메라로 다시 찍어 레퍼런스의 어느 픽셀인지 찾는다. 그래서
//   - 기본 시점에서는 레퍼런스와 픽셀 단위로 같고,
//   - 확대 · 숨쉬기 · 고개 돌리기로 카메라가 움직여도 칠은 책상 · 전화기에 붙어서 따라간다.
//   - 기본 시점에서 가려져 있던 면(들어 올린 패드, 수화기 아래 등)은 깊이가 안 맞으니 칠하지 않는다.
//
// 칠은 두 장의 표(레퍼런스 해상도)로 들어 있다.
//   paint_mul : rgb = 원래 밝기에 곱할 값(채널별, sqrt(배율 / 4) 로 저장), a = 칠의 세기
//   paint_add : rgb = 더할 색(조명이 거의 없는 곳을 청회색으로 띄울 때만). 방 조명 세기(light_scale)를 따른다.
//   paint_depth : 기본 시점에서 본 거리(rg = 상위 · 하위 바이트, 0~8m)
render_mode unshaded, fog_disabled, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

uniform sampler2D paint_mul : filter_linear, repeat_disable;
uniform sampler2D paint_add : source_color, filter_linear, repeat_disable;
uniform sampler2D paint_depth : filter_nearest, repeat_disable;

// 기본 시점 카메라 — AnalogPaint.cs 가 채운다.
uniform mat4 ref_world_to_view;
uniform float ref_tan_half_fov = 0.6249;
uniform float ref_aspect = 1.9318;

uniform float amount : hint_range(0.0, 1.0) = 1.0;
uniform float light_scale = 1.0;
// 깊이가 이만큼(거리 대비 비율) 넘게 다르면 "기본 시점에서 본 그 면이 아니다" 로 보고 칠하지 않는다.
uniform float depth_tolerance = 0.025;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 world = INV_VIEW_MATRIX * INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	world.xyz /= world.w;

	vec3 rv = (ref_world_to_view * vec4(world.xyz, 1.0)).xyz;
	vec2 uv = vec2(rv.x / (-rv.z * ref_tan_half_fov * ref_aspect), rv.y / (-rv.z * ref_tan_half_fov));
	uv = vec2(uv.x * 0.5 + 0.5, 0.5 - uv.y * 0.5);

	vec2 edge = min(uv, vec2(1.0) - uv);
	float w = step(0.0, -rv.z) * smoothstep(0.0, 0.015, min(edge.x, edge.y));

	vec2 dd = textureLod(paint_depth, uv, 0.0).rg * 255.0;
	float ref_dist = (dd.r * 256.0 + dd.g) / 65535.0 * 8.0;
	float dist = length(rv);
	w *= step(abs(dist - ref_dist), depth_tolerance * ref_dist + 0.004) * step(0.01, ref_dist);

	vec4 pm = textureLod(paint_mul, uv, 0.0);
	vec3 mul = 4.0 * pm.rgb * pm.rgb;
	vec3 add = textureLod(paint_add, uv, 0.0).rgb * light_scale;
	w *= pm.a * amount;

	vec3 src = textureLod(screen_tex, SCREEN_UV, 0.0).rgb;
	ALBEDO = src * mul + add;
	ALPHA = w;
}
