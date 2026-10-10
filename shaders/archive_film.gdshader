shader_type canvas_item;

// 낡은 기록영상 질감.
//
// 프롤로그 · 엔딩 · 스토리 3D 컷씬은 "지금 돌아가는 게임 화면" 이 아니라 **시설이 남긴
// 오래된 기록물**로 보여야 한다. 그래서 선명한 실시간 렌더 위에 한 겹 얹는다 —
// 주사선 · 필름 그레인 · 비네트 · 미세한 색수차 · 신호 흔들림.
//
// 과하면 장면이 안 보인다. 기본값은 "자세히 보면 알아채는" 정도로 맞춰져 있고,
// 재난 구간에서만 instability 를 올려 화면이 불안정해지게 한다.

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

uniform float grain = 0.055;        // 필름 그레인
uniform float scan = 0.085;         // 주사선 깊이
uniform float scan_lines = 190.0;   // 주사선 개수
uniform float vignette = 0.36;      // 가장자리 눌림
uniform float chroma = 0.0011;      // 색수차
uniform float desat = 0.16;         // 채도 빼기
uniform float lift = 0.028;         // 검정을 살짝 들어 올린다(완전한 검정은 디지털처럼 보인다)
uniform float instability = 0.0;    // 0 = 안정된 기록, 1 = 신호가 무너지는 중
uniform float t = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(41.3, 289.1))) * 43758.5453); }

void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 c = uv - 0.5;

    // 렌즈가 가장자리에서 아주 살짝 휜다.
    uv += c * dot(c, c) * 0.028;

    // 수평 동기가 흔들린다. 평소엔 거의 안 보이고, 불안정할수록 커진다.
    float jitter = sin(uv.y * 150.0 + t * 6.0) * 0.0005
                 + sin(uv.y * 23.0 - t * 1.7) * 0.0004 * instability * 6.0;
    uv.x += jitter;

    // 아주 가끔 한 줄이 통째로 밀린다(신호 끊김).
    float band = step(0.995 - instability * 0.06, hash(vec2(floor(uv.y * 48.0), floor(t * 7.0))));
    uv.x += band * (hash(vec2(floor(t * 7.0), 3.0)) - 0.5) * 0.045 * instability;

    vec3 col;
    float ca = chroma * (1.0 + instability * 3.0);
    col.r = texture(screen_tex, uv + vec2(ca, 0.0)).r;
    col.g = texture(screen_tex, uv).g;
    col.b = texture(screen_tex, uv - vec2(ca, 0.0)).b;

    // 주사선.
    float s = sin(uv.y * scan_lines * 3.14159265);
    col *= 1.0 - scan * (0.5 + 0.5 * s);

    // 화면 전체가 미세하게 깜빡인다.
    col *= 1.0 - 0.025 * (0.5 + 0.5 * sin(t * 11.3)) - 0.06 * instability
                * (0.5 + 0.5 * sin(t * 31.0));

    // 필름 그레인.
    float n = hash(uv * vec2(720.0, 405.0) + vec2(t * 37.0, t * 17.0));
    col += (n - 0.5) * (grain + instability * 0.07);

    // 비네트.
    col *= clamp(1.0 - vignette * dot(c, c) * 2.6, 0.0, 1.0);

    // 색감 — 채도를 조금 빼고 검정을 살짝 들어 올린다.
    float g = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(col, vec3(g), desat);
    col += vec3(lift, lift * 0.86, lift * 0.72);

    COLOR = vec4(col, 1.0);
}
