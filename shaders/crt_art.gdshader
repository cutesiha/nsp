shader_type canvas_item;

// CRT 화면에 올리는 **캐릭터 원화**를 전역 아날로그 LUT 에서 살아남게 만든다.
//
// 원화는 연한 하늘색 · 연보라 · 흰 바탕으로 그려져 있다. 전역 LUT(analog_vhs)는 그
// 좁은 밝은 영역을 거의 **한 가지 파랑으로 눌러** 버린다. 그래서 모니터에 올리면
// 눈 · 수염 · 선이 전부 사라지고 파란 실루엣 한 덩어리만 남았다.
//
// 그냥 어둡게(Modulate) 하는 것으로는 안 된다. 밝기를 똑같은 비율로 내리면 색 사이의
// 간격도 같이 줄어 **더 안 보인다.** 필요한 것은 밝기가 아니라 **간격** 이다.
//
//   ① 채도를 조금 낮춘다 — LUT 가 파랑을 뭉개는 구간에서 빠져나온다
//   ② 대비를 올린다 — 연한 면은 더 내려가고 선은 그대로 남아 간격이 벌어진다
//   ③ 그러고 나서 전체를 살짝만 누른다 — 번짐(Glow)에 날아가지 않을 만큼
//
// 색을 바꾸지는 않는다(채도를 0 으로 만들지 않는다) — 캐릭터 색은 알아볼 수 있어야 한다.

uniform float saturation : hint_range(0.0, 1.5) = 0.74;
uniform float contrast : hint_range(0.5, 2.5) = 1.42;
uniform float brightness : hint_range(0.3, 1.5) = 0.94;
// 대비를 돌리는 기준점. 원화가 전체적으로 밝아서 중앙(0.5)보다 위를 잡아야
// 연한 면만 내려가고 선은 제자리에 남는다.
uniform float pivot : hint_range(0.2, 0.9) = 0.62;

void fragment() {
	vec4 src = texture(TEXTURE, UV);
	vec3 col = src.rgb;

	// ① 채도
	float l = dot(col, vec3(0.299, 0.587, 0.114));
	col = mix(vec3(l), col, saturation);

	// ② 대비 — pivot 위는 끌어올리고 아래는 끌어내린다
	col = (col - vec3(pivot)) * contrast + vec3(pivot);

	// ③ 전체 밝기
	col *= brightness;

	// 투명도는 그대로. 바깥의 Modulate(화자/청자 밝기 등)도 그대로 곱해진다.
	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), src.a) * COLOR;
}
