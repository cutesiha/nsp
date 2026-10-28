extends SceneTree
# 트루엔딩 — 봉쇄 코어가 다시 물리는 순간의 효과음 합성기. assets/audio/sfx/ 에 쓴다.
#
#   godot --headless --path . --script res://tools/gen_ending_sfx.gd
#
# 규격은 기존 효과음과 같다: 22050Hz · 16bit · 모노(HorrorSfxGen · VentSfxGen 과 동일).
# 이 환경에는 파이썬이 없어서 GDScript 로 짠다. 여러 번 돌려도 된다(같은 파일을 덮어쓴다).
#
#   core_gather   빛이 사방에서 코어로 빨려 들어간다. 흩어진 음들이 한 화음으로 모이고,
#                 쓸려 들어가는 바람 소리와 올라가는 충전음이 끝에서 한 점으로 조여든다.
#   core_bloom    다 모인 빛이 터지는 순간 — 둥근 저음 한 번과 길게 남는 밝은 화음.
#   core_shimmer  복구 완료의 "샤랄라" — 종소리 같은 높은 음이 빠르게 올라가며 반짝인다.

const RATE := 22050
const DIR := "res://assets/audio/sfx"

# 모일 화음 — A 장조(라 · 도# · 미). 끝에서 이 음들로 수렴한다.
const CHORD := [220.0, 277.18, 329.63, 440.0, 554.37, 659.26, 880.0]

var _rng := RandomNumberGenerator.new()


func _initialize() -> void:
	_rng.seed = 20261028
	print("\n################ 엔딩 코어 효과음 합성 ################")
	_save("core_gather", _gather(3.2), 0.62)
	_save("core_bloom", _bloom(3.0), 0.66)
	_save("core_shimmer", _shimmer(3.4), 0.58)
	print("################ 완료 ################")
	quit(0)


# ── 빛이 모인다 ────────────────────────────────────────────────────────
func _gather(sec: float) -> PackedFloat32Array:
	var n := int(sec * RATE)
	var out := PackedFloat32Array()
	out.resize(n)

	# ① 흩어진 음 → 화음. 각 음은 제자리에서 ±14% 어긋난 높이에서 출발해 끝에서 정확히 맞는다.
	var starts := []
	for f in CHORD:
		starts.append(f * _rng.randf_range(0.86, 1.14))
	var phases := []
	phases.resize(CHORD.size())
	phases.fill(0.0)

	# ② 쓸려 들어가는 바람 — 백색소음을 공진 필터로 거르고, 중심 주파수를 위로 끌어올린다.
	var bp := _Sv.new()
	var bp2 := _Sv.new()
	# ③ 충전음 — 낮게 시작해 올라가는 사인 두 겹.
	var sweep_ph := 0.0
	var sub_ph := 0.0

	for i in n:
		var t := float(i) / RATE
		var k := t / sec                      # 0 → 1
		var swell := pow(k, 2.2)              # 끝으로 갈수록 급하게 커진다(거꾸로 감은 심벌)
		var conv := 1.0 - pow(1.0 - k, 2.6)   # 음높이가 맞아 가는 정도

		var s := 0.0
		for j in CHORD.size():
			var f: float = lerp(float(starts[j]), float(CHORD[j]), conv)
			# 모일수록 떨림이 줄어든다.
			f *= 1.0 + 0.012 * (1.0 - conv) * sin(TAU * (5.0 + j) * t)
			phases[j] = fmod(phases[j] + TAU * f / RATE, TAU)
			var amp := (0.10 + 0.9 * swell) * (1.0 if j < 4 else 0.6)
			s += sin(phases[j]) * amp * 0.16

		var noise := _rng.randf_range(-1.0, 1.0)
		var center: float = lerp(350.0, 7200.0, pow(k, 1.6))
		var wind := bp.run(noise, center, 2.2) + 0.5 * bp2.run(noise, center * 1.9, 3.5)
		s += wind * (0.04 + 0.62 * swell)

		var sweep_f: float = lerp(90.0, 1250.0, pow(k, 1.8))
		sweep_ph = fmod(sweep_ph + TAU * sweep_f / RATE, TAU)
		s += (sin(sweep_ph) + 0.35 * sin(sweep_ph * 2.0)) * 0.22 * swell

		var sub_f: float = lerp(38.0, 74.0, k)
		sub_ph = fmod(sub_ph + TAU * sub_f / RATE, TAU)
		s += sin(sub_ph) * 0.28 * pow(k, 1.3)

		out[i] = s

	# 끝은 칼같이 끊지 않고 30ms 만에 조인다 — 다음 소리(core_bloom)가 그 자리를 받는다.
	return _fade(out, 0.25, 0.03)


# ── 터지는 순간 ────────────────────────────────────────────────────────
func _bloom(sec: float) -> PackedFloat32Array:
	var n := int(sec * RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	var lp := _OnePole.new()
	var thump_ph := 0.0
	var ph := []
	ph.resize(CHORD.size())
	ph.fill(0.0)
	for i in n:
		var t := float(i) / RATE
		# 둥근 저음 — 95Hz 에서 42Hz 로 떨어지며 사라진다.
		var tf: float = lerp(95.0, 42.0, minf(1.0, t / 0.6))
		thump_ph = fmod(thump_ph + TAU * tf / RATE, TAU)
		var s := sin(thump_ph) * exp(-t * 3.2) * 0.9
		# 공기가 한 번 밀려난다 — 짧은 저역 소음.
		s += lp.low(_rng.randf_range(-1.0, 1.0), 0.08) * exp(-t * 7.0) * 1.6
		# 밝은 화음 — 천천히 피어나 길게 남는다.
		var pad_env := (1.0 - exp(-t * 9.0)) * exp(-t * 0.9)
		for j in CHORD.size():
			var f: float = CHORD[j] * (1.0 + 0.002 * sin(TAU * (0.7 + 0.3 * j) * t))
			ph[j] = fmod(ph[j] + TAU * f / RATE, TAU)
			s += sin(ph[j]) * pad_env * 0.09 * (1.0 if j < 5 else 0.55)
		out[i] = s
	return _fade(out, 0.004, 0.5)


# ── 샤랄라 ────────────────────────────────────────────────────────────
# 종소리 하나 = 기음 + 어긋난 배음(2.0 · 2.76 · 5.4) — 금속 막대가 울리는 비율이다.
# A 장조 5음계를 세 옥타브 빠르게 올라갔다가, 맨 위에서 몇 알이 흩어지며 반짝인다.
func _shimmer(sec: float) -> PackedFloat32Array:
	var n := int(sec * RATE)
	var out := PackedFloat32Array()
	out.resize(n)
	var scale := [880.0, 987.77, 1108.73, 1318.51, 1479.98]   # A5 B5 C#6 E6 F#6
	var notes := []   # [시작(초), 주파수, 세기]
	var at := 0.02
	for o in 3:
		for f in scale:
			var freq: float = f * pow(2.0, o)
			if freq > 3000.0:
				break
			notes.append([at, freq, 0.55 + 0.1 * o])
			at += 0.055 * (1.0 - 0.18 * o)
	# 맨 위에서 흩어지는 반짝임.
	for _i in 14:
		var f2: float = scale[_rng.randi_range(0, scale.size() - 1)] * pow(2.0, _rng.randi_range(1, 2))
		notes.append([at + 0.05 + _rng.randf_range(0.0, 1.3), minf(f2, 3000.0), _rng.randf_range(0.18, 0.4)])

	for note in notes:
		var t0: float = note[0]
		var f: float = note[1]
		var a: float = note[2]
		var i0 := int(t0 * RATE)
		var count := int(minf(1.6, sec - t0) * RATE)
		for i in count:
			var idx := i0 + i
			if idx >= n:
				break
			var t := float(i) / RATE
			var atk := minf(1.0, t / 0.0025)
			var s := sin(TAU * f * t) * exp(-t * 2.6)
			s += 0.45 * sin(TAU * f * 2.0 * t) * exp(-t * 4.5)
			s += 0.28 * sin(TAU * f * 2.76 * t) * exp(-t * 7.0)
			if f * 5.4 < RATE * 0.45:
				s += 0.12 * sin(TAU * f * 5.4 * t) * exp(-t * 12.0)
			# 아주 조금 어긋난 쌍둥이 — 반짝임이 한 점이 아니라 넓게 퍼진다.
			s += 0.35 * sin(TAU * f * 1.004 * t + 1.3) * exp(-t * 2.9)
			out[idx] += s * a * atk * 0.22

	# 바닥에 깔리는 아주 옅은 공기(하이햇 같은 쉬익) — 종소리만 있으면 장난감 같다.
	var hp := _OnePole.new()
	for i in n:
		var t := float(i) / RATE
		var env := (1.0 - exp(-t * 6.0)) * exp(-t * 1.5)
		out[i] += hp.high(_rng.randf_range(-1.0, 1.0), 0.6) * env * 0.05
	return _fade(out, 0.003, 0.6)


# ── 도구 ──────────────────────────────────────────────────────────────

func _fade(s: PackedFloat32Array, in_sec: float, out_sec: float) -> PackedFloat32Array:
	var fi := maxi(1, int(in_sec * RATE))
	var fo := maxi(1, int(out_sec * RATE))
	for i in min(fi, s.size()):
		s[i] *= float(i) / fi
	for i in min(fo, s.size()):
		s[s.size() - 1 - i] *= float(i) / fo
	return s


# 피크를 맞춰 16bit 모노 WAV 로 쓴다.
func _save(name: String, s: PackedFloat32Array, peak: float) -> void:
	var hi := 1e-9
	for v in s:
		hi = maxf(hi, absf(v))
	var k := peak / hi
	var data := PackedByteArray()
	data.resize(s.size() * 2)
	for i in s.size():
		data.encode_s16(i * 2, int(clamp(s[i] * k, -1.0, 1.0) * 32767.0))

	var f := FileAccess.open("%s/%s.wav" % [DIR, name], FileAccess.WRITE)
	if f == null:
		push_error("열 수 없음: %s" % name)
		return
	f.store_buffer("RIFF".to_ascii_buffer())
	f.store_32(36 + data.size())
	f.store_buffer("WAVEfmt ".to_ascii_buffer())
	f.store_32(16)
	f.store_16(1)
	f.store_16(1)
	f.store_32(RATE)
	f.store_32(RATE * 2)
	f.store_16(2)
	f.store_16(16)
	f.store_buffer("data".to_ascii_buffer())
	f.store_32(data.size())
	f.store_buffer(data)
	f.close()
	print("   %-14s %5.2f초  피크 %.2f" % [name, s.size() / float(RATE), peak])


# 상태 변수 필터(대역 통과).
class _Sv:
	const R := 22050.0
	var low := 0.0
	var band := 0.0

	func run(x: float, freq: float, q: float) -> float:
		var f := 2.0 * sin(PI * clampf(freq, 20.0, R * 0.45) / R)
		var damp := 1.0 / maxf(0.5, q)
		var high := x - low - damp * band
		band += f * high
		low += f * band
		return band


class _OnePole:
	var z := 0.0

	func low(x: float, k: float) -> float:
		z += k * (x - z)
		return z

	func high(x: float, k: float) -> float:
		z += k * (x - z)
		return x - z
