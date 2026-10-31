shader_type canvas_item;

// CRT 화면에 올리는 **캐릭터 원화**를 전역 아날로그 LUT 에서 살아남게 만든다.
//
// 원화는 한쪽으로 치우쳐 있지 않다. 한 장 안에 둘 다 있다 —
//   · 거의 흰 제복 · 연한 피부 (밝은 쪽)
//   · 어두운 회색 가면 · 검은 선화  (어두운 쪽)
// 그 위에 CRT 발광 → 번짐(Glow) → 전역 LUT 가 차례로 얹힌다. 양쪽 끝이 각각 다른
// 방식으로 죽는다. 밝은 쪽은 흰색으로 날아가고(토끼 얼굴 · 입 모양), 어두운 쪽은
// 검정으로 뭉개진다(늑대 · 강아지 가면이 통째로 까맣게 되던 것).
//
// **그래서 양쪽 끝을 다르게 다뤄야 한다.** 예전에는 가운데를 기준으로 대비를 곧게
// 늘렸는데(linear), 그러면 밝은 쪽은 1 로 잘리고 어두운 쪽은 0 으로 잘린다 —
// 고치려던 두 가지를 동시에 더 망가뜨리는 식이었다.
//
//   ① 어두운 쪽을 **들어올린다**(감마 < 1) — 가면과 선화가 검정에 붙지 않는다
//   ② 밝은 쪽을 **눌러 담는다**(무릎 곡선) — 흰색에 닿지 않아 입 · 눈 · 선이 남는다
//
// 둘 다 끝에서 완만해지는 곡선이라 **어느 쪽도 잘리지 않는다.** 결과는 좁은 띠
// 안에 모이지만, 그 띠가 바로 LUT 가 색을 구분해 주는 구간이다.

uniform float saturation : hint_range(0.0, 1.5) = 1.0;
// 1 보다 작을수록 어두운 쪽이 올라온다. 0.78 이면 회색 가면이 검정에서 빠져나온다.
uniform float shadow_lift : hint_range(0.3, 1.0) = 0.78;
// 밝은 쪽을 눌러 담는 세기. 작을수록 천장이 낮아진다(흰색에서 멀어진다).
uniform float highlight_knee : hint_range(0.1, 2.0) = 0.45;
// 어두운 픽셀을 들어올리는 배율의 상한. 낮출수록 검은 선이 더 검게 남는다.
uniform float max_boost : hint_range(1.0, 4.0) = 1.45;
// 마지막 미세 조정. 전체가 어둡거나 밝으면 이 한 줄만 움직인다.
uniform float gain : hint_range(0.5, 1.6) = 1.0;

// ── 가면 구역만 덜 어둡게(직원별, 기본 0 = 끔) ──────────────────────────────
// 늑대 가면은 회색(밝기 0.4 언저리)인데 화면에서는 거의 검정이 되어 그 위의 검은 눈 선이
// 묻혀 버렸다. 원인은 맨 아래 `* COLOR` 다 — Godot 4 의 fragment 에서 COLOR 는 이미
// **원화 색이 곱해진 값**이라, 여기서 원화를 다시 곱하면 중간 밝기가 제곱으로 떨어진다
// (0.4 → 0.16). 전 직원 공통의 기존 톤이므로 그대로 두고, **가면 칸에서만** 그 두 번째
// 곱을 mask_lift 만큼 되돌린다(1 = 톤 곡선만 거친 원래 회색).
// 칸(mask_rect = UV x0, y0, x1, y1) 안의 **채도 낮은 중간 회색**만 고친다 —
// 선화 · 눈(아주 어두운 쪽)과 머리카락 · 피부(밝은 쪽)는 그대로라 눈 선이 또렷해진다.
uniform float mask_lift : hint_range(0.0, 1.0) = 0.0;
uniform vec4 mask_rect = vec4(0.0, 0.0, 0.0, 0.0);
uniform float mask_lum_lo = 0.20;   // 이보다 어두운 픽셀(선 · 눈)은 건드리지 않는다
uniform float mask_lum_hi = 0.52;   // 이보다 밝은 픽셀(머리카락 · 피부)도 그대로

void fragment() {
	vec4 src = texture(TEXTURE, UV);
	vec3 col = max(src.rgb, vec3(0.0));

	// 가면 칸 보정 배율(1 = 그대로). 맨 아래에서 COLOR 에 곱한다.
	float mask_undo = 1.0;
	if (mask_lift > 0.0) {
		vec2 lo = mask_rect.xy, hi = mask_rect.zw;
		float edge = 0.015;
		float inside = smoothstep(lo.x - edge, lo.x + edge, UV.x) * (1.0 - smoothstep(hi.x - edge, hi.x + edge, UV.x))
		             * smoothstep(lo.y - edge, lo.y + edge, UV.y) * (1.0 - smoothstep(hi.y - edge, hi.y + edge, UV.y));
		float ml = dot(col, vec3(0.299, 0.587, 0.114));
		float chroma = max(col.r, max(col.g, col.b)) - min(col.r, min(col.g, col.b));
		float w = inside
		        * smoothstep(mask_lum_lo, mask_lum_lo + 0.06, ml)
		        * (1.0 - smoothstep(mask_lum_hi - 0.06, mask_lum_hi, ml))
		        * (1.0 - smoothstep(0.08, 0.16, chroma));
		mask_undo = mix(1.0, 1.0 / max(ml, 0.05), w * mask_lift);
	}

	// 곡선은 **밝기에만** 건다.
	//
	// 세 채널에 따로 걸면 채널 사이의 비율이 같이 눌려 **색이 빠진다** — 분홍 토끼가
	// 파랗게, 갈색 강아지가 파랗게 변했다. 밝기만 옮기고 색의 비율은 그대로 두면
	// 명암만 고쳐지고 캐릭터 색은 지켜진다.
	float l = dot(col, vec3(0.299, 0.587, 0.114));

	// ① 어두운 쪽 들어올리기 → ② 밝은 쪽 눌러 담기(0 은 0, 1 은 1/(1+k) 로).
	float lifted = pow(max(l, 0.0), shadow_lift);
	float toned = lifted / (lifted + highlight_knee) * gain;

	// 새 밝기로 옮긴다. 비율을 쓰므로 색조 · 채도는 건드리지 않는다.
	//
	// 다만 **아주 어두운 픽셀에서는 배율에 상한을 둔다.** 검은 선화는 밝기가 0 에
	// 가까워서 나누면 배율이 몇 배로 튀고, 선에 희미하게 섞여 있던 색(분홍 토끼의
	// 번진 테두리)이 그대로 끌려 올라와 **검은 선이 분홍 선이 된다.**
	// 가면을 들어올리는 데 필요한 배율은 1.3 언저리라, 상한을 둬도 거기엔 닿지 않는다.
	float scale = min(toned / max(l, 1e-4), max_boost);
	col *= scale;

	// 필요하면 여기서만 채도를 손본다(기본 1.0 = 그대로).
	col = mix(vec3(toned), col, saturation);

	// 투명도는 그대로. 바깥의 Modulate(화자/청자 밝기)도 그대로 곱해진다.
	COLOR = vec4(clamp(col, vec3(0.0), vec3(1.0)), src.a) * COLOR;
	COLOR.rgb *= mask_undo;
}
