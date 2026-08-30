shader_type spatial;
render_mode diffuse_burley, specular_disabled, cull_back;

// entity.glb(귀신) 전용. 회색빛 도는 흰색 + 매트하고 노이즈 낀 기괴한 질감,
// 손끝/발끝(모델 아래쪽·좌우 바깥)은 검붉은색으로 물든다.
// 모델 로컬 바운드: X[-0.46,0.46] Y[-0.50,0.50] Z[-0.22,0.22] (Y 위, 원점 중심).

uniform vec3 base_color : source_color = vec3(0.82, 0.83, 0.85);
uniform vec3 blood_color : source_color = vec3(0.19, 0.028, 0.03);
uniform float noise_scale = 20.0;
uniform float mottle : hint_range(0.0, 1.0) = 0.34;   // 얼룩덜룩 정도
uniform float grime : hint_range(0.0, 1.0) = 0.24;    // 거뭇한 때
uniform float tip_reach : hint_range(0.0, 0.5) = 0.30; // 끝부분 물드는 범위
uniform float rim_strength : hint_range(0.0, 1.0) = 0.12;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.11, 0.27, 0.43));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i + vec3(0, 0, 0)), hash(i + vec3(1, 0, 0)), f.x),
				   mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
			   mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x),
				   mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	float s = 0.0;
	float a = 0.5;
	for (int k = 0; k < 4; k++) {
		s += a * vnoise(p);
		p *= 2.03;
		a *= 0.5;
	}
	return s;
}

varying vec3 v_model;

void vertex() {
	v_model = VERTEX;
}

void fragment() {
	// 손끝/발끝 — 아래쪽(발) + 좌우 바깥이면서 몸통 아래(손).
	float feet = smoothstep(-0.50 + tip_reach, -0.50, v_model.y);
	float hands = smoothstep(0.46 - tip_reach, 0.46, abs(v_model.x))
				* smoothstep(0.14, -0.34, v_model.y);
	float tips = pow(clamp(feet + hands, 0.0, 1.0), 1.4);

	// 얼룩덜룩 + 거뭇한 때
	float n = fbm(v_model * noise_scale);
	float mott = mix(1.0 - mottle, 1.0 + mottle * 0.25, n);
	float dirt = smoothstep(0.34, 0.72, fbm(v_model * 6.3)) * grime;

	vec3 col = base_color * mott;
	col = mix(col, col * vec3(0.46, 0.4, 0.4), dirt);
	col = mix(col, blood_color, tips);

	ALBEDO = clamp(col, vec3(0.0), vec3(1.0));
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;

	// 살짝 비쳐 보이는 창백한 림(끝부분엔 안 준다)
	float fres = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 3.0);
	EMISSION = base_color * fres * rim_strength * (1.0 - tips);
}
