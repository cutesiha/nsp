extends SceneTree
# 사람 이송 전용 CCTV 애니메이션 생성기 — employee_common.tres 에 넣는다.
#
#   godot --headless --script res://tools/gen_carry_clips.gd
#
# tools/gen_cctv_clips.py 와 같은 자세 모델(FK + 팔 IK)을 쓴다. 이 환경에는 파이썬이
# 없어서 GDScript 로 옮겼다 — 관절 규칙 · 트랙 포맷 · .tres 끼워넣기 방식은 그쪽과 같다.
# 여러 번 돌려도 된다(같은 이름의 클립은 지우고 다시 넣는다). 다른 클립은 건드리지 않는다.
#
# 박스 운반 클립(pickup_box / carry_box_*)은 사람을 드는 데 쓰지 않는다. 팔 각도도 손 높이도
# 사람 몸이 아니라 34cm 상자에 맞춰져 있기 때문이다. 여기 클립은 전부 "사람 몸"을 기준으로
# 손 위치를 IK 로 풀어 맞춘다.
#
# 관절 규칙(캐릭터는 자기 -Z 를 본다, +X 가 오른쪽):
#   · 팔/다리 x+ = 앞으로 든다   · 아래팔 x+ = 팔꿈치 굽힘   · 아래다리 x- = 무릎 굽힘
#   · 몸통/가슴 x- = 앞으로 숙임 · 머리 x+ = 고개 젖힘(위)   · 머리 y+ = 왼쪽을 봄
#   · 발 x+ = 발끝 들기           · out + = 바깥쪽

const LIB := "res://scenes/cctv_characters/anim/employee_common.tres"

const H := "VisualRoot/RigRoot/Hips"
const C := "VisualRoot/RigRoot/Hips/Torso/Chest"

# 트랙 순서(파이썬 생성기의 ORDER 와 같아야 한다).
const ORDER := ["VR", "HIPS", "TORSO", "CHEST", "HEAD", "SHL", "SHR", "UAL", "UAR",
	"LAL", "LAR", "HNDL", "HNDR", "ULL", "ULR", "LLL", "LLR", "FL", "FR"]

var PATHS := {
	"VR": "VisualRoot:position",
	"HIPS": H + ":rotation",
	"TORSO": H + "/Torso:rotation",
	"CHEST": C + ":rotation",
	"HEAD": C + "/Neck/Head:rotation",
	"SHL": C + "/ShoulderL:rotation",
	"SHR": C + "/ShoulderR:rotation",
	"UAL": C + "/ShoulderL/UpperArmL:rotation",
	"UAR": C + "/ShoulderR/UpperArmR:rotation",
	"LAL": C + "/ShoulderL/UpperArmL/LowerArmL:rotation",
	"LAR": C + "/ShoulderR/UpperArmR/LowerArmR:rotation",
	"HNDL": C + "/ShoulderL/UpperArmL/LowerArmL/HandL:rotation",
	"HNDR": C + "/ShoulderR/UpperArmR/LowerArmR/HandR:rotation",
	"ULL": H + "/UpperLegL:rotation",
	"ULR": H + "/UpperLegR:rotation",
	"LLL": H + "/UpperLegL/LowerLegL:rotation",
	"LLR": H + "/UpperLegR/LowerLegR:rotation",
	"FL": H + "/UpperLegL/LowerLegL/FootL:rotation",
	"FR": H + "/UpperLegR/LowerLegR/FootR:rotation",
}

# 체형 치수(EmployeeCctvBase_F / _M 의 실제 노드 오프셋).
var DIMS := {
	"F": {"hip": 0.825, "torso": 0.09, "chest": 0.19, "neck": 0.19, "head": 0.09,
		"sh": Vector2(0.1165, 0.145), "ua": Vector2(0.044, -0.02), "la": 0.28, "hand": 0.26,
		"ul": Vector2(0.09, -0.02), "ll": 0.40, "foot": 0.345},
	"M": {"hip": 1.00, "torso": 0.115, "chest": 0.245, "neck": 0.21, "head": 0.09,
		"sh": Vector2(0.128, 0.165), "ua": Vector2(0.053, -0.02), "la": 0.315, "hand": 0.29,
		"ul": Vector2(0.085, -0.02), "ll": 0.50, "foot": 0.42},
}
const PALM := 0.06   # 손목에서 손바닥 중심까지

var _warns: Array[String] = []


# ── 자세 도우미 ───────────────────────────────────────────────────────────

func pose(kw: Dictionary = {}) -> Dictionary:
	var p := {}
	for k in ORDER:
		p[k] = Vector3.ZERO
	for k in kw:
		p[k] = kw[k]
	return p


func merge(parts: Array) -> Dictionary:
	var out := pose()
	for part in parts:
		for k in part:
			out[k] = part[k]
	return out


func add(p: Dictionary, kw: Dictionary) -> Dictionary:
	var q := p.duplicate()
	for k in kw:
		q[k] = q[k] + kw[k]
	return q


# 양팔. out + = 바깥으로 벌림, shrug + = 어깨 으쓱.
func arms(ua_x: float, out_: float, la: float, shrug := 0.0, hand := 0.0,
		ua_x_r = null, out_r = null, la_r = null, hand_r = null) -> Dictionary:
	var axr: float = ua_x if ua_x_r == null else ua_x_r
	var orr: float = out_ if out_r == null else out_r
	var lar: float = la if la_r == null else la_r
	var hnr: float = hand if hand_r == null else hand_r
	return {
		"SHL": Vector3(0, 0, -shrug), "SHR": Vector3(0, 0, shrug),
		"UAL": Vector3(ua_x, 0, -out_), "UAR": Vector3(axr, 0, orr),
		"LAL": Vector3(la, 0, 0), "LAR": Vector3(lar, 0, 0),
		"HNDL": Vector3(hand, 0, 0), "HNDR": Vector3(hnr, 0, 0),
	}


# 골반을 drop 만큼 내렸을 때 발이 바닥에 닿는 무릎 각도와 발 각도.
func _solve_leg(body: String, drop: float, thigh: float, out_ := 0.0) -> Vector2:
	var d: Dictionary = DIMS[body]
	var need: float = (d.ll + d.foot - drop) / max(0.2, cos(deg_to_rad(out_)))
	var a := deg_to_rad(thigh)
	var c: float = (need - d.ll * cos(a)) / d.foot
	c = clampf(c, -1.0, 1.0)
	var s := -acos(c)
	if s > a:
		s = a
	return Vector2(rad_to_deg(s - a), -rad_to_deg(s))


# 두 발을 바닥에 붙인 채 골반을 drop 만큼 낮춘다. lift = 들어 올린 발(무릎 추가 굽힘).
func legs(body: String, drop: float, thigh_l: float, thigh_r = null, out_l := 0.0, out_r = null,
		lift_l := 0.0, lift_r := 0.0, foot_l := 0.0, foot_r := 0.0) -> Dictionary:
	var tr: float = thigh_l if thigh_r == null else thigh_r
	var orr: float = out_l if out_r == null else out_r
	var l := _solve_leg(body, drop, thigh_l, out_l)
	var r := _solve_leg(body, drop, tr, orr)
	return {
		"VR": Vector3(0.0, -drop, 0.0),
		"ULL": Vector3(thigh_l, 0, -out_l), "ULR": Vector3(tr, 0, orr),
		"LLL": Vector3(l.x - lift_l, 0, 0), "LLR": Vector3(r.x - lift_r, 0, 0),
		"FL": Vector3(l.y + lift_l * 0.5 + foot_l, 0, 0),
		"FR": Vector3(r.y + lift_r * 0.5 + foot_r, 0, 0),
	}


func stand(body: String, drop := 0.0, extra: Dictionary = {}) -> Dictionary:
	return merge([legs(body, drop, 2.0), arms(4, 3, 10), extra])


# 한쪽 무릎을 바닥에 대고 다른 발은 앞에 딛는다(바닥에 누운 사람을 잡을 때).
func kneel_one(body: String, knee := "R") -> Dictionary:
	var d: Dictionary = DIMS[body]
	var knee_h := 0.075
	var hip_joint: float = knee_h + d.ll * cos(deg_to_rad(8))
	var drop: float = (d.hip - 0.02) - hip_joint
	var need: float = hip_joint - 0.06
	var c: float = (need - d.foot * cos(deg_to_rad(-6))) / d.ll
	var a := rad_to_deg(acos(clampf(c, -1.0, 1.0)))
	var down := {"thigh": 8.0, "knee": -94.0, "foot": -86.0}
	var up := {"thigh": a, "knee": -6.0 - a, "foot": 6.0}
	var kl: Dictionary = down if knee == "L" else up
	var kr: Dictionary = up if knee == "L" else down
	return {
		"VR": Vector3(0.0, -drop, 0.0),
		"ULL": Vector3(kl.thigh, 0, -5), "ULR": Vector3(kr.thigh, 0, 5),
		"LLL": Vector3(kl.knee, 0, 0), "LLR": Vector3(kr.knee, 0, 0),
		"FL": Vector3(kl.foot, 0, 0), "FR": Vector3(kr.foot, 0, 0),
	}


# 걷는 다리 4키. 팔·몸통은 호출한 쪽이 덮는다.
func walk_keys(body: String, T: float, stride: float, drop := 0.02, lift := 40.0, bob := 0.012) -> Array:
	return [
		[0.0, legs(body, drop, stride, -stride * 0.7)],
		[T * 0.25, legs(body, drop - bob, 6, 6, 0.0, null, 0.0, lift)],
		[T * 0.5, legs(body, drop, -stride * 0.7, stride)],
		[T * 0.75, legs(body, drop - bob, 6, 6, 0.0, null, lift, 0.0)],
	]


# ── FK ────────────────────────────────────────────────────────────────────

func _child(parent: Transform3D, off: Vector3, rot_deg: Vector3) -> Transform3D:
	var b := Basis.from_euler(Vector3(deg_to_rad(rot_deg.x), deg_to_rad(rot_deg.y),
		deg_to_rad(rot_deg.z)), EULER_ORDER_YXZ)
	return parent * Transform3D(b, off)


# 자세 p 의 손바닥 위치(캐릭터 루트 기준).
func _palm(body: String, p: Dictionary, side: String) -> Vector3:
	var d: Dictionary = DIMS[body]
	var root := Transform3D(Basis.IDENTITY, p["VR"])
	var hips := _child(root, Vector3(0, d.hip, 0), p["HIPS"])
	var torso := _child(hips, Vector3(0, d.torso, 0), p["TORSO"])
	var chest := _child(torso, Vector3(0, d.chest, 0), p["CHEST"])
	var sx := -1.0 if side == "L" else 1.0
	var sh := _child(chest, Vector3(sx * d.sh.x, d.sh.y, 0), p["SH" + side])
	var ua := _child(sh, Vector3(sx * d.ua.x, d.ua.y, 0), p["UA" + side])
	var la := _child(ua, Vector3(0, -d.la, 0), p["LA" + side])
	var hand := _child(la, Vector3(0, -d.hand, 0), p["HND" + side])
	return hand * Vector3(0, -PALM, 0)


# ── IK — 손바닥을 목표점에 ────────────────────────────────────────────────

func _arm_cost(body: String, p: Dictionary, side: String, target: Vector3, v: Vector3) -> float:
	var q := p.duplicate()
	q["UA" + side] = Vector3(v.x, p["UA" + side].y, -v.y if side == "L" else v.y)
	q["LA" + side] = Vector3(v.z, p["LA" + side].y, p["LA" + side].z)
	var e: float = _palm(body, q, side).distance_squared_to(target)
	# 팔을 옆으로 벌리는 것보다 앞으로 굽히는 쪽을 조금 더 좋게 본다.
	e += 2e-7 * v.y * v.y
	if v.z < 0.0:
		e += 1e-3 * v.z * v.z
	elif v.z > 158.0:
		e += 1e-3 * (v.z - 158.0) * (v.z - 158.0)
	return e


func _solve_arm(body: String, p: Dictionary, side: String, target: Vector3, seed_: Vector3) -> Array:
	var v := seed_
	var best := _arm_cost(body, p, side, target, v)
	var step := 16.0
	while step > 0.05:
		var moved := false
		for i in 3:
			for s in [step, -step]:
				var w := v
				w[i] += s
				var c := _arm_cost(body, p, side, target, w)
				if c < best:
					best = c
					v = w
					moved = true
		if not moved:
			step *= 0.5
	return [v, sqrt(max(0.0, best - 2e-7 * v.y * v.y))]


# p 의 팔을 풀어 손바닥이 L/R(몸 기준 좌표)에 오게 한다.
func reach(body: String, p: Dictionary, L = null, R = null, name_ := "") -> Dictionary:
	var q := p.duplicate()
	for pair in [["L", L], ["R", R]]:
		var side: String = pair[0]
		if pair[1] == null:
			continue
		var target: Vector3 = pair[1]
		var best = null
		var best_err := 1e9
		for sd in [Vector3(60, 8, 50), Vector3(100, 10, 90), Vector3(30, 4, 20),
				Vector3(140, 20, 110), Vector3(20, -8, 100), Vector3(-40, 10, 100),
				Vector3(-70, 14, 80)]:
			var res := _solve_arm(body, q, side, target, sd)
			if res[1] < best_err:
				best_err = res[1]
				best = res[0]
		var v: Vector3 = best
		q["UA" + side] = Vector3(v.x, q["UA" + side].y, -v.y if side == "L" else v.y)
		q["LA" + side] = Vector3(v.z, q["LA" + side].y, q["LA" + side].z)
		if best_err > 0.035:
			_warns.append("  ! %s %s%s: 손이 목표에서 %.1fcm 떨어짐" % [name_, body, side, best_err * 100.0])
	return q


# ── 클립 ──────────────────────────────────────────────────────────────────

class Clip:
	var name: String
	var length: float
	var loop: bool
	var keys: Array = []        # [time, pose]
	var rigroot = null          # Vector3(도) — 몸 전체를 눕히는 클립만

	func _init(n: String, l: float, lp: bool) -> void:
		name = n
		length = l
		loop = lp

	func key(t: float, p: Dictionary) -> Clip:
		keys.append([snappedf(t, 0.0001), p])
		return self


func fmt(v: float) -> String:
	var s := "%.4f" % v
	if s.contains("."):
		s = s.rstrip("0").rstrip(".")
	if s == "-0" or s == "":
		s = "0"
	return s


func emit(c: Clip) -> String:
	var keys: Array = c.keys.duplicate()
	keys.sort_custom(func(a, b): return a[0] < b[0])
	if c.loop:
		var kept := []
		for k in keys:
			if k[0] < c.length - 1e-4:
				kept.append(k)
		kept.append([c.length, keys[0][1]])
		keys = kept
	var times := PackedStringArray()
	for k in keys:
		times.append(fmt(k[0]))
	var trans := PackedStringArray()
	for _k in keys:
		trans.append("1")

	var lines := PackedStringArray()
	lines.append('[sub_resource type="Animation" id="Anim_%s"]' % c.name)
	lines.append('resource_name = "%s"' % c.name)
	lines.append("length = %s" % fmt(c.length))
	if c.loop:
		lines.append("loop_mode = 1")
	for i in ORDER.size():
		var joint: String = ORDER[i]
		var vals := PackedStringArray()
		for k in keys:
			var val: Vector3 = k[1][joint]
			if joint != "VR":
				val = Vector3(deg_to_rad(val.x), deg_to_rad(val.y), deg_to_rad(val.z))
			vals.append("Vector3(%s, %s, %s)" % [fmt(val.x), fmt(val.y), fmt(val.z)])
		lines.append('tracks/%d/type = "value"' % i)
		lines.append("tracks/%d/imported = false" % i)
		lines.append("tracks/%d/enabled = true" % i)
		lines.append('tracks/%d/path = NodePath("%s")' % [i, PATHS[joint]])
		lines.append("tracks/%d/interp = 2" % i)
		lines.append("tracks/%d/loop_wrap = true" % i)
		lines.append("tracks/%d/keys = {" % i)
		lines.append('"times": PackedFloat32Array(%s),' % ", ".join(times))
		lines.append('"transitions": PackedFloat32Array(%s),' % ", ".join(trans))
		lines.append('"update": 0,')
		lines.append('"values": [%s]' % ", ".join(vals))
		lines.append("}")
	if c.rigroot != null:
		var n := ORDER.size()
		var rr: Vector3 = c.rigroot
		lines.append('tracks/%d/type = "value"' % n)
		lines.append("tracks/%d/imported = false" % n)
		lines.append("tracks/%d/enabled = true" % n)
		lines.append('tracks/%d/path = NodePath("VisualRoot/RigRoot:rotation")' % n)
		lines.append("tracks/%d/interp = 1" % n)
		lines.append("tracks/%d/loop_wrap = true" % n)
		lines.append("tracks/%d/keys = {" % n)
		lines.append('"times": PackedFloat32Array(0),')
		lines.append('"transitions": PackedFloat32Array(1),')
		lines.append('"update": 0,')
		lines.append('"values": [Vector3(%s, %s, %s)]'
			% [fmt(deg_to_rad(rr.x)), fmt(deg_to_rad(rr.y)), fmt(deg_to_rad(rr.z))])
		lines.append("}")
	return "\n".join(lines) + "\n"


# ── 자세 설계 ─────────────────────────────────────────────────────────────
#
# 손 목표점은 "환자 몸의 어디를 잡는가"로 정한다. 운반자 몸 기준 좌표(+X 오른쪽, -Z 앞).

# 업기 — 손은 환자 허벅지 뒤를 받친다(골반 옆, 조금 뒤).
func pig_grip(body: String, lift := 0.0) -> Array:
	var d: Dictionary = DIMS[body]
	var y: float = d.hip * 0.80 + lift
	var x: float = 0.235 if body == "M" else 0.205
	return [Vector3(-x, y, 0.10), Vector3(x, y, 0.10)]


# 공주님 안기 — 왼팔은 환자 등 아래, 오른팔은 무릎 뒤. 둘 다 가슴 앞 높이.
func bri_grip(body: String, y_off := 0.0, fwd := 0.0) -> Array:
	var d: Dictionary = DIMS[body]
	var y: float = d.hip * 1.02 + y_off
	var x: float = 0.33 if body == "M" else 0.29
	var z: float = -0.21 - fwd
	return [Vector3(-x, y + 0.02, z), Vector3(x, y, z - 0.01)]


# 어깨 부축 — 왼손은 환자 허리(왼쪽 옆), 오른손은 어깨에 걸친 환자 손목(오른쪽 위).
func sho_grip(body: String, drop := 0.0) -> Array:
	var d: Dictionary = DIMS[body]
	var waist: float = d.hip * 0.95 - drop
	var wrist: float = d.hip * 1.30 - drop
	var x: float = 0.30 if body == "M" else 0.26
	return [Vector3(-x, waist, -0.02), Vector3(x * 0.6, wrist, -0.12)]


func build() -> Array:
	var clips: Array = []
	for body in ["M", "F"]:
		var suf := "_m" if body == "M" else "_f"
		var d: Dictionary = DIMS[body]
		clips.append_array(_piggyback(body, suf))
		clips.append_array(_bridal(body, suf))
		clips.append_array(_shoulder(body, suf))
		clips.append_array(_recover(body, suf))
		clips.append_array(_victims(body, suf, d))
	return clips


# ═══ 업기(늑대 · 고양이) ══════════════════════════════════════════════════
func _piggyback(body: String, suf: String) -> Array:
	var out: Array = []
	var g := pig_grip(body)
	# 최종 업은 자세 — 무릎 조금 굽히고 상체를 앞으로, 두 손은 뒤로 돌려 허벅지를 받친다.
	var hold := merge([legs(body, 0.085, 13),
		{"TORSO": Vector3(-19, 0, 0), "CHEST": Vector3(-4, 0, 0), "HEAD": Vector3(9, 0, 0)},
		arms(-38, 13, 92, 6.0)])
	hold = reach(body, hold, g[0], g[1], "piggyback_hold")

	# ── 픽업 1.5초: 환자를 보고 → 등을 돌려 깊게 앉고 → 상체를 끌어올려 업고 → 일어선다
	var c := Clip.new("faint_pickup_piggyback" + suf, 1.5, false)
	var look := merge([legs(body, 0.02, 3),
		{"TORSO": Vector3(-10, 0, 0), "HEAD": Vector3(-24, 0, 0)}, arms(8, 5, 16)])
	var squat_lo := pig_grip(body, -0.42)
	var crouch := merge([legs(body, 0.44, 76),
		{"TORSO": Vector3(-26, 0, 0), "CHEST": Vector3(-6, 0, 0), "HEAD": Vector3(4, 0, 0)},
		arms(-52, 18, 70, 10.0)])
	crouch = reach(body, crouch, squat_lo[0], squat_lo[1], "piggyback_crouch")
	var grab_mid := pig_grip(body, -0.30)
	var grab := merge([legs(body, 0.41, 72),
		{"TORSO": Vector3(-30, 0, 0), "CHEST": Vector3(-8, 0, 0), "HEAD": Vector3(2, 0, 0)},
		arms(-48, 16, 76, 12.0)])
	grab = reach(body, grab, grab_mid[0], grab_mid[1], "piggyback_grab")
	var mid_lo := pig_grip(body, -0.16)
	var rising := merge([legs(body, 0.24, 46),
		{"TORSO": Vector3(-26, 0, 0), "CHEST": Vector3(-6, 0, 0), "HEAD": Vector3(8, 0, 0)},
		arms(-42, 14, 86, 10.0)])
	rising = reach(body, rising, mid_lo[0], mid_lo[1], "piggyback_rise")

	c.key(0.0, stand(body))
	c.key(0.25, look)
	c.key(0.55, crouch)
	c.key(0.90, grab)
	c.key(1.20, rising)
	c.key(1.50, hold)
	out.append(c)

	# ── 운반 걷기 — 보폭 짧고 무릎 더 굽은 채, 팔은 계속 허벅지를 받친다(팔 스윙 없음)
	var T := 0.95
	c = Clip.new("faint_carry_walk_piggyback" + suf, T, true)
	var i := 0
	for kv in walk_keys(body, T, 17, 0.085, 28.0, 0.010):
		var q: Dictionary = merge([kv[1],
			{"TORSO": Vector3(-19, 0, 0), "CHEST": Vector3(-4, 0, 0), "HEAD": Vector3(9, 0, 0)},
			arms(-38, 13, 92, 6.0)])
		# 걸음에 맞춰 상체가 아주 조금 좌우로 흔들린다.
		q = add(q, {"TORSO": Vector3(0, 0, [1.8, 0, -1.8, 0][i])})
		var gg := pig_grip(body, [0.0, 0.012, 0.0, 0.012][i])
		q = reach(body, q, gg[0], gg[1], c.name)
		c.key(kv[0], q)
		i += 1
	out.append(c)

	# ── 침대에 내려놓기 1.8초: 몸을 낮추고 → 손을 등으로 옮겨 → 상체부터 내려놓는다
	c = Clip.new("faint_bed_place_piggyback" + suf, 1.8, false)
	var pl_a := pig_grip(body, -0.10)
	var low := merge([legs(body, 0.30, 54),
		{"TORSO": Vector3(-24, 0, 0), "CHEST": Vector3(-6, 0, 0), "HEAD": Vector3(6, 0, 0)},
		arms(-40, 15, 84, 10.0)])
	low = reach(body, low, pl_a[0], pl_a[1], "pig_place_low")
	# 손이 허벅지에서 등으로 — 팔이 몸 옆을 지나 앞쪽 위로 올라온다.
	var d: Dictionary = DIMS[body]
	var ps := 1.0 if body == "M" else 0.84
	var back_l := Vector3(-0.26 * ps, d.hip * 0.96, -0.06 * ps)
	var back_r := Vector3(0.26 * ps, d.hip * 0.96, -0.06 * ps)
	var shift := merge([legs(body, 0.30, 54),
		{"TORSO": Vector3(-22, 0, 0), "CHEST": Vector3(-4, 0, 0), "HEAD": Vector3(2, 0, 0)},
		arms(-20, 20, 80, 8.0)])
	shift = reach(body, shift, back_l, back_r, "pig_place_shift")
	# 환자 상체를 침대 쪽(앞)으로 내려 준다 — 두 손이 앞·아래로 뻗는다.
	var lay_l := Vector3(-0.26, d.hip * 0.76, -0.34)
	var lay_r := Vector3(0.26, d.hip * 0.74, -0.36)
	var laying := merge([legs(body, 0.34, 58),
		{"TORSO": Vector3(-34, 0, 0), "CHEST": Vector3(-8, 0, 0), "HEAD": Vector3(-6, 0, 0)},
		arms(30, 14, 60, 6.0)])
	laying = reach(body, laying, lay_l, lay_r, "pig_place_lay")
	var release := merge([legs(body, 0.26, 48),
		{"TORSO": Vector3(-26, 0, 0), "CHEST": Vector3(-6, 0, 0), "HEAD": Vector3(-10, 0, 0)},
		arms(26, 20, 34, 2.0)])
	c.key(0.0, hold)
	c.key(0.40, low)
	c.key(0.80, shift)
	c.key(1.25, laying)
	c.key(1.60, add(laying, {"TORSO": Vector3(2, 0, 0)}))
	c.key(1.80, release)
	out.append(c)
	return out


# ═══ 공주님 안기(강아지 · 여우) ═══════════════════════════════════════════
func _bridal(body: String, suf: String) -> Array:
	var out: Array = []
	var g := bri_grip(body)
	# 최종 안은 자세 — 팔꿈치 확실히 굽고, 환자를 가슴 가까이 당긴다.
	var hold := merge([legs(body, 0.07, 11),
		{"TORSO": Vector3(5, 0, 0), "CHEST": Vector3(2, 0, 0), "HEAD": Vector3(-8, 0, 0)},
		arms(18, 26, 108, 8.0)])
	hold = reach(body, hold, g[0], g[1], "bridal_hold")

	# ── 픽업 1.6초: 옆에 서서 → 무릎 굽혀 두 팔을 몸 아래로 → 들어 올려 가슴에 붙인다
	var c := Clip.new("faint_pickup_bridal" + suf, 1.6, false)
	var d: Dictionary = DIMS[body]
	var look := merge([legs(body, 0.02, 3),
		{"TORSO": Vector3(-12, 0, 0), "HEAD": Vector3(-26, 0, 0)}, arms(10, 6, 18)])
	# 바닥의 환자 — 등/무릎 아래로 팔을 넣는다(바닥에서 10cm 남짓).
	# 팔 길이가 어깨~바닥 거리보다 짧다. 상체를 깊게 숙여 어깨를 내려야 손이 실제로 닿는다
	# (여성 베이스는 팔이 6cm 더 짧아 목표를 몸 쪽으로 더 당긴다).
	var s := 1.0 if body == "M" else 0.84
	var floor_l := Vector3(-0.26 * s, 0.30, -0.22 * s)
	var floor_r := Vector3(0.23 * s, 0.28, -0.25 * s)
	var kneel_p: Dictionary = kneel_one(body, "R")
	var dig := merge([kneel_p,
		{"TORSO": Vector3(-52, 0, 0), "CHEST": Vector3(-14, 0, 0), "HEAD": Vector3(8, 0, 0)},
		arms(46, 16, 44, 10.0)])
	dig = reach(body, dig, floor_l, floor_r, "bridal_dig")
	# 팔이 몸 아래로 완전히 들어갔다 — 아직 들지 않았다.
	var under := merge([kneel_p,
		{"TORSO": Vector3(-48, 0, 0), "CHEST": Vector3(-12, 0, 0), "HEAD": Vector3(6, 0, 0)},
		arms(44, 22, 56, 10.0)])
	under = reach(body, under, floor_l + Vector3(0.02, 0.04, 0.02),
		floor_r + Vector3(-0.02, 0.04, 0.02), "bridal_under")
	var mid := bri_grip(body, -0.26, 0.05)
	var lifting := merge([legs(body, 0.26, 50),
		{"TORSO": Vector3(-14, 0, 0), "CHEST": Vector3(-2, 0, 0), "HEAD": Vector3(-8, 0, 0)},
		arms(30, 24, 86, 12.0)])
	lifting = reach(body, lifting, mid[0], mid[1], "bridal_lift")
	c.key(0.0, stand(body))
	c.key(0.25, look)
	c.key(0.62, dig)
	c.key(0.92, under)
	c.key(1.28, lifting)
	c.key(1.60, hold)
	out.append(c)

	# ── 운반 걷기 — 상체 거의 안정, 팔은 환자를 고정(스윙 없음)
	var T := 0.92
	c = Clip.new("faint_carry_walk_bridal" + suf, T, true)
	var i := 0
	for kv in walk_keys(body, T, 21, 0.07, 34.0, 0.011):
		var q: Dictionary = merge([kv[1],
			{"TORSO": Vector3(5, 0, 0), "CHEST": Vector3(2, 0, 0), "HEAD": Vector3(-8, 0, 0)},
			arms(18, 26, 108, 8.0)])
		q = add(q, {"TORSO": Vector3(0, 0, [1.2, 0, -1.2, 0][i])})
		var gg := bri_grip(body, [0.0, 0.010, 0.0, 0.010][i])
		q = reach(body, q, gg[0], gg[1], c.name)
		c.key(kv[0], q)
		i += 1
	out.append(c)

	# ── 침대에 내려놓기 1.8초: 무릎 굽혀 몸을 낮추고 → 환자를 매트리스에 대고 → 팔을 뺀다
	c = Clip.new("faint_bed_place_bridal" + suf, 1.8, false)
	var low_t := bri_grip(body, -0.22, 0.10)
	var low := merge([legs(body, 0.24, 46),
		{"TORSO": Vector3(-6, 0, 0), "CHEST": Vector3(0, 0, 0), "HEAD": Vector3(-14, 0, 0)},
		arms(26, 24, 92, 8.0)])
	low = reach(body, low, low_t[0], low_t[1], "bri_place_low")
	var touch_t := bri_grip(body, -0.34, 0.18)
	var touch := merge([legs(body, 0.30, 54),
		{"TORSO": Vector3(-16, 0, 0), "CHEST": Vector3(-4, 0, 0), "HEAD": Vector3(-18, 0, 0)},
		arms(34, 22, 74, 6.0)])
	touch = reach(body, touch, touch_t[0], touch_t[1], "bri_place_touch")
	# 팔을 환자 몸 아래에서 천천히 뺀다 — 손이 바깥쪽으로 빠진다.
	var slide := merge([legs(body, 0.28, 52),
		{"TORSO": Vector3(-16, 0, 0), "CHEST": Vector3(-4, 0, 0), "HEAD": Vector3(-16, 0, 0)},
		arms(32, 34, 52, 4.0)])
	slide = reach(body, slide, touch_t[0] + Vector3(-0.10, 0.02, 0.0),
		touch_t[1] + Vector3(0.10, 0.02, 0.0), "bri_place_slide")
	var off := merge([legs(body, 0.22, 42),
		{"TORSO": Vector3(-12, 0, 0), "HEAD": Vector3(-14, 0, 0)}, arms(22, 22, 30, 2.0)])
	c.key(0.0, hold)
	c.key(0.45, low)
	c.key(1.05, touch)
	c.key(1.45, slide)
	c.key(1.80, off)
	out.append(c)
	return out


# ═══ 어깨 부축(양 · 토끼) ═════════════════════════════════════════════════
func _shoulder(body: String, suf: String) -> Array:
	var out: Array = []
	var g := sho_grip(body)
	# 최종 부축 자세 — 환자는 왼쪽. 운반자는 오른쪽으로 몸을 기울여 균형을 잡는다.
	var hold := merge([legs(body, 0.06, 9, 7, 4.0, 10.0),
		{"TORSO": Vector3(-6, 0, 11), "CHEST": Vector3(-2, 0, 5), "HEAD": Vector3(-6, -14, -4)},
		arms(-12, 34, 46, 4.0, 0.0, 86, 10, 118, 0)])
	hold = reach(body, hold, g[0], g[1], "shoulder_hold")

	# ── 픽업 1.8초: 옆에 무릎 꿇고 → 상체를 일으켜 → 팔을 어깨에 걸고 → 같이 일어선다
	var c := Clip.new("faint_pickup_shoulder" + suf, 1.8, false)
	var d: Dictionary = DIMS[body]
	var look := merge([legs(body, 0.02, 3),
		{"TORSO": Vector3(-12, 0, 0), "HEAD": Vector3(-26, 0, 0)}, arms(10, 6, 18)])
	var kneel_p: Dictionary = kneel_one(body, "L")
	var s := 1.0 if body == "M" else 0.84
	# 바닥의 환자 상체를 양손으로 잡는다(어깨가 닿는 높이까지 상체를 깊게 숙인다).
	var torso_l := Vector3(-0.23 * s, 0.32, -0.22 * s)
	var torso_r := Vector3(-0.03, 0.36, -0.25 * s)
	var grab := merge([kneel_p,
		{"TORSO": Vector3(-46, 0, 6), "CHEST": Vector3(-12, 0, 2), "HEAD": Vector3(6, -10, 0)},
		arms(50, 18, 50, 10.0)])
	grab = reach(body, grab, torso_l, torso_r, "shoulder_grab")
	# 상체를 일으킨다 — 손이 올라온다.
	var up_l := Vector3(-0.28, d.hip * 0.55, -0.20)
	var up_r := Vector3(-0.02, d.hip * 0.70, -0.24)
	var raise := merge([kneel_p,
		{"TORSO": Vector3(-14, 0, 8), "CHEST": Vector3(-4, 0, 3), "HEAD": Vector3(-10, -12, 0)},
		arms(36, 22, 66, 12.0)])
	raise = reach(body, raise, up_l, up_r, "shoulder_raise")
	# 환자 팔을 자기 어깨 뒤로 넘기고 손목을 잡는다.
	var sling := sho_grip(body, 0.34)
	var sling_p := merge([legs(body, 0.34, 58, 52, 4.0, 10.0),
		{"TORSO": Vector3(-18, 0, 9), "CHEST": Vector3(-4, 0, 4), "HEAD": Vector3(-8, -14, -2)},
		arms(-8, 30, 44, 6.0, 0.0, 78, 12, 110, 0)])
	sling_p = reach(body, sling_p, sling[0], sling[1], "shoulder_sling")
	# 힘을 주다 한 번 내려앉는다(양·토끼 공통 — 속도로 성격을 가른다).
	var strain := sho_grip(body, 0.20)
	var strain_p := merge([legs(body, 0.22, 44, 40, 4.0, 10.0),
		{"TORSO": Vector3(-14, 0, 10), "CHEST": Vector3(-4, 0, 5), "HEAD": Vector3(-4, -14, -3)},
		arms(-10, 32, 45, 6.0, 0.0, 82, 11, 114, 0)])
	strain_p = reach(body, strain_p, strain[0], strain[1], "shoulder_strain")
	var sag := sho_grip(body, 0.26)
	var sag_p := merge([legs(body, 0.27, 50, 46, 4.0, 10.0),
		{"TORSO": Vector3(-16, 0, 10), "CHEST": Vector3(-5, 0, 5), "HEAD": Vector3(-6, -14, -3)},
		arms(-10, 32, 45, 7.0, 0.0, 80, 11, 112, 0)])
	sag_p = reach(body, sag_p, sag[0], sag[1], "shoulder_sag")

	c.key(0.0, stand(body))
	c.key(0.22, look)
	c.key(0.58, grab)
	c.key(0.92, raise)
	c.key(1.20, sling_p)
	c.key(1.42, strain_p)
	c.key(1.56, sag_p)     # 한 번 내려앉고
	c.key(1.80, hold)      # 다시 힘을 줘 완전히 세운다
	out.append(c)

	# ── 운반 걷기 — 보폭 가장 작고 둘이 함께 절뚝인다
	var T := 1.10
	c = Clip.new("faint_carry_walk_shoulder" + suf, T, true)
	var i := 0
	for kv in walk_keys(body, T, 12, 0.07, 22.0, 0.014):
		var q: Dictionary = merge([kv[1],
			{"TORSO": Vector3(-6, 0, 11), "CHEST": Vector3(-2, 0, 5), "HEAD": Vector3(-6, -14, -4)},
			arms(-12, 34, 46, 4.0, 0.0, 86, 10, 118, 0)])
		# 환자 쪽(왼쪽)으로 한 번씩 더 기울었다 돌아온다 — 절뚝이는 느낌.
		q = add(q, {"TORSO": Vector3(0, 0, [3.5, 0.5, -1.0, 0.5][i]),
			"HEAD": Vector3([2.0, 0, -1.0, 0][i], 0, 0)})
		var gg := sho_grip(body, [0.0, -0.012, 0.0, -0.012][i])
		q = reach(body, q, gg[0], gg[1], c.name)
		c.key(kv[0], q)
		i += 1
	out.append(c)

	# ── 침대에 내려놓기 1.8초: 가장자리에 앉히고 → 상체를 뒤로 눕히고 → 손을 뺀다
	c = Clip.new("faint_bed_place_shoulder" + suf, 1.8, false)
	var seat := sho_grip(body, 0.22)
	var seating := merge([legs(body, 0.24, 46, 42, 4.0, 10.0),
		{"TORSO": Vector3(-14, 0, 9), "CHEST": Vector3(-4, 0, 4), "HEAD": Vector3(-12, -12, -2)},
		arms(-10, 32, 46, 6.0, 0.0, 80, 12, 112, 0)])
	seating = reach(body, seating, seat[0], seat[1], "sho_place_seat")
	# 어깨에 걸린 팔을 풀고 두 손으로 등·허리를 받친다.
	var sup_l := Vector3(-0.30 * s, d.hip * 0.80, -0.16 * s)
	var sup_r := Vector3(-0.02, d.hip * 0.84, -0.26 * s)
	var support := merge([legs(body, 0.26, 48, 44, 4.0, 10.0),
		{"TORSO": Vector3(-18, 0, 6), "CHEST": Vector3(-6, 0, 2), "HEAD": Vector3(-16, -10, 0)},
		arms(40, 24, 70, 8.0)])
	support = reach(body, support, sup_l, sup_r, "sho_place_support")
	# 상체를 침대 뒤쪽으로 천천히 눕힌다.
	var lay_l := Vector3(-0.32 * s, d.hip * 0.62, -0.34 * s)
	var lay_r := Vector3(-0.06, d.hip * 0.66, -0.38 * s)
	var laying := merge([legs(body, 0.30, 54, 50, 4.0, 10.0),
		{"TORSO": Vector3(-28, 0, 4), "CHEST": Vector3(-8, 0, 2), "HEAD": Vector3(-20, -8, 0)},
		arms(46, 22, 56, 6.0)])
	laying = reach(body, laying, lay_l, lay_r, "sho_place_lay")
	var off := merge([legs(body, 0.20, 40),
		{"TORSO": Vector3(-16, 0, 0), "HEAD": Vector3(-16, 0, 0)}, arms(26, 20, 32, 2.0)])
	c.key(0.0, hold)
	c.key(0.42, seating)
	c.key(0.86, support)
	c.key(1.34, laying)
	c.key(1.58, add(laying, {"TORSO": Vector3(2, 0, 0)}))
	c.key(1.80, off)
	out.append(c)
	return out


# ═══ 내려놓은 뒤 — 손을 빼고 허리를 펴고 환자를 한 번 내려다본다 ═══════════
func _recover(body: String, suf: String) -> Array:
	var c := Clip.new("faint_carrier_recover" + suf, 1.0, false)
	var bent := merge([legs(body, 0.22, 42),
		{"TORSO": Vector3(-18, 0, 0), "CHEST": Vector3(-4, 0, 0), "HEAD": Vector3(-14, 0, 0)},
		arms(24, 20, 32, 2.0)])
	var half := merge([legs(body, 0.10, 20),
		{"TORSO": Vector3(-10, 0, 0), "CHEST": Vector3(-2, 0, 0), "HEAD": Vector3(-18, 0, 0)},
		arms(12, 12, 22, 0.0)])
	# 완전히 서서 환자를 한 번 내려다본다.
	var look := merge([legs(body, 0.01, 2),
		{"TORSO": Vector3(-3, 0, 0), "HEAD": Vector3(-22, 0, 0)}, arms(6, 4, 14)])
	# 숨 한 번 — 가슴이 올라왔다 내려간다.
	var breath := merge([legs(body, 0.0, 2),
		{"TORSO": Vector3(3, 0, 0), "CHEST": Vector3(4, 0, 0), "HEAD": Vector3(-6, 0, 0)},
		arms(5, 6, 12, -4.0)])
	c.key(0.0, bent)
	c.key(0.30, half)
	c.key(0.58, look)
	c.key(0.80, breath)
	c.key(1.00, stand(body))
	return [c]


# ═══ 환자 — 의식이 없는 몸. 스타일별로 실려 있는 모양이 다르다 ════════════
func _victims(body: String, suf: String, d: Dictionary) -> Array:
	var out: Array = []

	# 업힌 환자 — 상체가 운반자 등에 붙어 앞으로 축 처지고, 두 팔이 어깨 앞으로 늘어진다.
	# 다리는 운반자 양옆으로 내려와 무릎이 약간 굽는다.
	var pig := merge([
		{"VR": Vector3.ZERO,
		"HIPS": Vector3(-6, 0, 0), "TORSO": Vector3(-24, 0, 0), "CHEST": Vector3(-12, 0, 0),
		"HEAD": Vector3(-30, 26, 8),
		"ULL": Vector3(44, 0, -16), "ULR": Vector3(46, 0, 16),
		"LLL": Vector3(-58, 0, 0), "LLR": Vector3(-54, 0, 0),
		"FL": Vector3(14, 0, 0), "FR": Vector3(12, 0, 0)},
		arms(96, 15, 84, -12.0, -22.0, 93, 13, 88, -18)])
	var c := Clip.new("faint_victim_piggyback" + suf, 2.6, true)
	c.key(0.0, pig)
	c.key(0.65, add(pig, {"HEAD": Vector3(3, -5, -2), "CHEST": Vector3(-2, 0, 0),
		"UAL": Vector3(-3, 0, 2), "UAR": Vector3(-3, 0, -2)}))
	c.key(1.30, add(pig, {"HEAD": Vector3(-2, 4, 3), "TORSO": Vector3(2, 0, 0)}))
	c.key(1.95, add(pig, {"HEAD": Vector3(2, -3, -1), "UAL": Vector3(3, 0, -2),
		"UAR": Vector3(3, 0, 2)}))
	out.append(c)

	# 안긴 환자 — 몸이 가로로 눕는다(RigRoot 90도). 머리는 뒤로 젖혀지고 한 팔이 늘어진다.
	var bri := merge([
		{"VR": Vector3.ZERO,
		"HIPS": Vector3(4, 0, 0), "TORSO": Vector3(8, 0, 0), "CHEST": Vector3(6, 0, 0),
		"HEAD": Vector3(22, -18, 0),
		"ULL": Vector3(-14, 0, -8), "ULR": Vector3(-12, 0, 8),
		"LLL": Vector3(-32, 0, 0), "LLR": Vector3(-28, 0, 0),
		"FL": Vector3(-14, 0, 0), "FR": Vector3(-12, 0, 0)},
		arms(-26, 30, 18, -12.0, -20.0, 36, 6, 64, -16)])
	c = Clip.new("faint_victim_bridal" + suf, 2.6, true)
	c.rigroot = Vector3(90, 0, 0)
	c.key(0.0, bri)
	c.key(0.65, add(bri, {"HEAD": Vector3(-3, 3, 0), "UAL": Vector3(4, 0, -3)}))
	c.key(1.30, add(bri, {"HEAD": Vector3(2, -2, 0), "LLL": Vector3(3, 0, 0),
		"LLR": Vector3(3, 0, 0)}))
	c.key(1.95, add(bri, {"HEAD": Vector3(-2, 2, 0), "UAL": Vector3(-3, 0, 2)}))
	out.append(c)

	# 부축받는 환자 — 몸이 운반자 쪽(오른쪽)으로 크게 기울고, 왼팔이 운반자 어깨 위로 올라간다.
	# 다리는 힘이 빠져 무릎이 굽고 발끝이 바닥에 끌린다.
	var sho := merge([
		{"VR": Vector3(0, -0.14, 0),
		"HIPS": Vector3(0, 0, -6), "TORSO": Vector3(-10, 0, -20), "CHEST": Vector3(-6, 0, -8),
		"HEAD": Vector3(-26, 10, -14),
		"ULL": Vector3(24, 0, -6), "ULR": Vector3(16, 0, 6),
		"LLL": Vector3(-46, 0, 0), "LLR": Vector3(-38, 0, 0),
		"FL": Vector3(18, 0, 0), "FR": Vector3(14, 0, 0)},
		arms(128, 26, 40, 18.0, -16.0, 14, 8, 16, -22)])
	c = Clip.new("faint_victim_shoulder" + suf, 2.6, true)
	c.key(0.0, sho)
	c.key(0.65, add(sho, {"HEAD": Vector3(3, -4, 2), "TORSO": Vector3(0, 0, 2),
		"ULR": Vector3(6, 0, 0), "LLR": Vector3(-5, 0, 0)}))
	c.key(1.30, add(sho, {"HEAD": Vector3(-2, 3, -2), "TORSO": Vector3(0, 0, -2),
		"ULL": Vector3(6, 0, 0), "LLL": Vector3(-5, 0, 0)}))
	c.key(1.95, add(sho, {"HEAD": Vector3(2, -2, 1), "ULR": Vector3(4, 0, 0),
		"LLR": Vector3(-4, 0, 0)}))
	out.append(c)
	return out


# ── 쓰기 ──────────────────────────────────────────────────────────────────

func _initialize() -> void:
	var clips := build()
	var path := ProjectSettings.globalize_path(LIB)
	var f := FileAccess.open(LIB, FileAccess.READ)
	if f == null:
		printerr("라이브러리를 못 열었다: ", LIB)
		quit(1)
		return
	var src := f.get_as_text()
	f.close()

	# 같은 이름의 예전 클립을 지운다(여러 번 돌려도 한 벌만 남게).
	for c in clips:
		var re := RegEx.create_from_string(
			'(?s)\\[sub_resource type="Animation" id="Anim_%s"\\]\\n.*?\\n(?=\\[)' % c.name)
		src = re.sub(src, "", true)
		var re2 := RegEx.create_from_string('&"%s": SubResource\\("Anim_%s"\\),?\\n' % [c.name, c.name])
		src = re2.sub(src, "", true)

	var at := src.find("[resource]")
	if at < 0:
		printerr("[resource] 섹션을 못 찾았다")
		quit(1)
		return
	var head := src.substr(0, at)
	var tail := src.substr(at + "[resource]".length())

	var body := ""
	for c in clips:
		body += emit(c) + "\n"
	head = head.rstrip("\n") + "\n\n" + body

	var dre := RegEx.create_from_string("(?s)_data = \\{\\n(.*?)\\n\\}")
	var m := dre.search(tail)
	if m == null:
		printerr("_data 블록을 못 찾았다")
		quit(1)
		return
	var entries: Array[String] = []
	for line in m.get_string(1).split("\n"):
		var s := line.strip_edges()
		if s.is_empty():
			continue
		entries.append(s.rstrip(","))
	for c in clips:
		entries.append('&"%s": SubResource("Anim_%s")' % [c.name, c.name])
	entries.sort()
	tail = tail.substr(0, m.get_start()) + "_data = {\n" + ",\n".join(entries) + "\n}" \
		+ tail.substr(m.get_end())

	var w := FileAccess.open(LIB, FileAccess.WRITE)
	w.store_string(head + "[resource]" + tail)
	w.close()

	for warn in _warns:
		print(warn)
	print("%d clips → %s" % [clips.size(), path])
	for c in clips:
		print("  %-38s %4.2fs %s  keys=%d" % [c.name, c.length,
			"loop" if c.loop else "once", c.keys.size()])
	quit(0)
