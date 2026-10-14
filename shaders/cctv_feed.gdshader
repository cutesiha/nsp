shader_type canvas_item;

// CCTV 영상 전용 아날로그 질감.
//
// **영상 사각형에만 걸린다.** 모니터 유리 전체(crt_screen)나 모니터 1 의 지도 · 배치표에는
// 닿지 않는다 — 거기에 곡률을 주면 화면 좌표와 마우스 좌표가 어긋나 클릭이 빗나간다.
// 여기 걸리는 TextureRect 는 아무도 클릭하지 않으므로 마음껏 휘어도 된다.
//
// 스캔라인은 이미 뷰가 따로 깔고 있다(CCTVMonitorView.BuildScanlines) — 여기서 또 그리면
// 두 겹이 되어 모아레가 생긴다. 이 셰이더가 맡는 것은 **렌즈와 테이프** 쪽이다.
//
// 가독성이 최우선이다(지시서 §3). 기본값은 전부 "말하지 않으면 눈치채지 못하지만
// 끄면 허전한" 세기로 맞춰 두었다. 직원이 무엇을 하는지는 언제나 보여야 한다.

// 렌즈 곡률 — 가장자리만 부푼다.
uniform float curvature : hint_range(0.0, 0.25) = 0.055;
// 네 귀퉁이 어두움.
uniform float vignette : hint_range(0.0, 1.5) = 0.40;
// 밝은 곳 · 붉은 조명 둘레의 색 번짐. 표본을 2개 더 뽑는다(저사양에서 0).
uniform float bleed : hint_range(0.0, 1.0) = 0.32;
// 낡은 영상 장비의 잔상. 표본을 1개 더 뽑는다(저사양에서 0).
uniform float ghosting : hint_range(0.0, 1.0) = 0.20;
// 테이프가 고르게 돌지 않아 생기는 미세한 흔들림.
uniform float jitter : hint_range(0.0, 0.02) = 0.0013;
// 전자 잡음. 어두운 화면일수록 거칠어진다(실제 카메라가 게인을 올리는 것과 같다).
uniform float grain : hint_range(0.0, 0.3) = 0.032;

// ── 코드가 매 프레임 넘겨 주는 값 ──────────────────────────────────
// 신호 불안정. 0 이어도 가로 띠는 아주 약하게 늘 지나간다.
uniform float tracking : hint_range(0.0, 1.0) = 0.0;
// 채널 전환 진행도. 1 에서 0 으로 떨어지며 화면이 흐르고 신호를 새로 잡는다.
uniform float switch_t : hint_range(0.0, 1.0) = 0.0;
// 신호가 들어오고 있는가. 0 이면 영상 자체를 내보내지 않는다.
uniform float signal : hint_range(0.0, 1.0) = 1.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	float t = TIME;
	vec2 uv = UV;

	// ① 렌즈 곡률 — 중앙은 그대로, 가장자리로 갈수록 바깥으로 밀린다.
	vec2 c = uv - 0.5;
	float r2 = dot(c, c);
	uv = 0.5 + c * (1.0 + curvature * r2);

	// ② 미세 흔들림 — 주기가 다른 두 흔들림을 겹쳐 규칙이 들리지 않게 한다.
	uv.x += sin(t * 2.3) * jitter + sin(t * 17.0) * jitter * 0.35;
	uv.y += sin(t * 1.7) * jitter * 0.5;

	// ③ 트래킹 오류 — 가로 띠 하나가 화면을 훑고 지나가며 그 줄만 옆으로 밀린다.
	//    평소에도 아주 약하게 지나간다. 신호가 불안정해지면 눈에 띄게 커진다.
	float sweep = fract(t * 0.19);
	float band = smoothstep(0.045, 0.0, abs(uv.y - sweep));
	uv.x += band * (0.0016 + tracking * 0.045)
	        * (hash(vec2(floor(t * 60.0), 0.0)) - 0.5) * 2.0;

	// ④ 채널 전환 — 화면이 위로 흐르고 수평 동기가 깨진다.
	if (switch_t > 0.0) {
		uv.y = fract(uv.y + switch_t * switch_t * 3.0);
		uv.x += (hash(vec2(floor(uv.y * 180.0), floor(t * 120.0))) - 0.5) * switch_t * 0.09;
	}

	vec2 lo = vec2(0.0015);
	vec2 hi = vec2(0.9985);
	vec3 col = texture(TEXTURE, clamp(uv, lo, hi)).rgb;

	// ⑤ 잔상 — 지나간 그림이 인광처럼 조금 남는다. 밝은 쪽만 남기므로(max)
	//    어두운 부분이 뭉개지지 않는다.
	if (ghosting > 0.0) {
		vec3 trail = texture(TEXTURE, clamp(uv - vec2(0.0045, 0.0), lo, hi)).rgb;
		col = max(col, trail * ghosting);
	}

	// ⑥ 색 번짐 — 붉은 경광등과 밝은 기기 둘레가 옆으로 샌다.
	if (bleed > 0.0) {
		float rr = texture(TEXTURE, clamp(uv + vec2(0.0024, 0.0), lo, hi)).r;
		float bb = texture(TEXTURE, clamp(uv - vec2(0.0024, 0.0), lo, hi)).b;
		col.r = mix(col.r, max(col.r, rr), bleed);
		col.b = mix(col.b, max(col.b, bb), bleed);
	}

	// ⑦ 전자 잡음 — 어두운 화면에서 거칠고, 밝은 화면에서 잦아든다.
	float lum = dot(col, vec3(0.299, 0.587, 0.114));
	float g = hash(uv * vec2(640.0, 400.0) + fract(t) * 31.0) - 0.5;
	col += g * grain * mix(1.6, 0.35, clamp(lum, 0.0, 1.0));

	// ⑧ 가장자리 어두움.
	col *= clamp(1.0 - r2 * vignette * 1.6, 0.0, 1.0);

	// ⑨ 곡률 때문에 바깥으로 벗어난 자리는 화면 밖이다 — 검정으로 둔다.
	vec2 inside = step(vec2(0.0), uv) * step(uv, vec2(1.0));
	col *= inside.x * inside.y;

	// ⑩ 신호 세기. 전환 중에는 화면이 어두워졌다가 다시 잡힌다.
	col *= signal * (1.0 - switch_t * 0.6);

	COLOR = vec4(col, 1.0);
}
