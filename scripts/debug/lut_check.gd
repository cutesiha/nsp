extends SceneTree

# [일회용 도구] 캐릭터 원화가 **전역 LUT 를 지나고도 보이는가**를 게임을 띄우지 않고 본다.
#
#   godot --headless --path . --script res://scripts/debug/lut_check.gd -- <저장 폴더>
#
# 세로로 세 칸을 붙여 한 장으로 낸다.
#   ① 원본
#   ② crt_art.gdshader 의 곡선을 통과한 것
#   ③ 거기에 analog_lut.png 까지 통과한 것  ← 실제로 화면에 보이는 색
#
# ②를 건너뛴 ①→③ 도 같이 내서, 보정이 있을 때와 없을 때를 나란히 본다.

const LUT := "res://shaders/analog_lut.png"
const LUT_SIZE := 33

# crt_art.gdshader 와 **같은 값**이어야 한다. 바꾸면 양쪽 다 바꾼다.
const SATURATION := 1.0
const SHADOW_LIFT := 0.78
const HIGHLIGHT_KNEE := 0.45
const MAX_BOOST := 1.45

var _lut: Image

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	var out_dir := args[0] if args.size() > 0 else ProjectSettings.globalize_path("user://")

	_lut = Image.load_from_file(ProjectSettings.globalize_path(LUT))
	if _lut == null:
		print("[lut] LUT 를 못 읽음")
		quit()
		return

	for who in ["wolf", "dog", "rabbit"]:
		_strip(who, out_dir)
	quit()

func _strip(who: String, out_dir: String) -> void:
	var path := "res://assets/characters/standing_v2/%s/%s_%s_bigmouth.png" % [
		who, who, "bad" if who in ["wolf", "cat", "sheep"] else "smile"]
	var src := Image.load_from_file(ProjectSettings.globalize_path(path))
	if src == null:
		print("[lut] 못 읽음: ", path)
		return

	# 얼굴만 잘라 크게 본다 — 가면 · 입이 거기 있다.
	var w := src.get_width()
	var h := src.get_height()
	var face := src.get_region(Rect2i(int(w * 0.25), int(h * 0.08), int(w * 0.5), int(h * 0.34)))
	var fw := face.get_width()
	var fh := face.get_height()

	var out := Image.create_empty(fw * 3, fh, false, Image.FORMAT_RGBA8)
	for y in range(fh):
		for x in range(fw):
			var c := face.get_pixel(x, y)
			var curved := _curve(c)
			var a: Vector3 = _lut_look(Vector3(c.r, c.g, c.b))
			var d: Vector3 = _lut_look(curved)
			out.set_pixel(x, y, Color(a.x, a.y, a.z, c.a))
			out.set_pixel(fw + x, y, Color(curved.x, curved.y, curved.z, c.a))
			out.set_pixel(fw * 2 + x, y, Color(d.x, d.y, d.z, c.a))
	out.save_png(out_dir + "/lut_" + who + ".png")

	# 가면 한가운데와 입 자리의 값을 숫자로도 남긴다.
	var mc := face.get_pixel(int(fw * 0.5), int(fh * 0.28))
	var oc := face.get_pixel(int(fw * 0.5), int(fh * 0.72))
	var mask := Vector3(mc.r, mc.g, mc.b)
	var mouth := Vector3(oc.r, oc.g, oc.b)
	print("[lut] %s  가면   원본 %.2f → LUT만 %.2f → 보정+LUT %.2f" % [
		who, _lum(mask), _lum(_lut_look(mask)), _lum(_lut_look(_curve(mc)))])
	print("[lut] %s  입자리 원본 %.2f → LUT만 %.2f → 보정+LUT %.2f" % [
		who, _lum(mouth), _lum(_lut_look(mouth)), _lum(_lut_look(_curve(oc)))])

func _lum(v: Vector3) -> float:
	return v.x * 0.299 + v.y * 0.587 + v.z * 0.114

# crt_art.gdshader 의 fragment() 와 같은 계산.
func _curve(c: Color) -> Vector3:
	var v := Vector3(c.r, c.g, c.b)
	var l: float = _lum(v)
	var lifted: float = pow(maxf(l, 0.0), SHADOW_LIFT)
	var toned: float = lifted / (lifted + HIGHLIGHT_KNEE)
	v *= minf(toned / maxf(l, 1e-4), MAX_BOOST)
	v = Vector3(toned, toned, toned).lerp(v, SATURATION)
	return Vector3(clampf(v.x, 0.0, 1.0), clampf(v.y, 0.0, 1.0), clampf(v.z, 0.0, 1.0))

# analog_vhs.gdshader 의 lut_lookup 과 같은 계산(선형 보간 포함).
func _lut_look(v: Vector3) -> Vector3:
	var n: float = float(LUT_SIZE)
	var r: float = clampf(v.x, 0.0, 1.0) * (n - 1.0)
	var g: float = clampf(v.y, 0.0, 1.0) * (n - 1.0)
	var b: float = clampf(v.z, 0.0, 1.0) * (n - 1.0)
	var b0: float = floor(b)
	var b1: float = minf(b0 + 1.0, n - 1.0)
	var t: float = b - b0
	return _lut_fetch(r, g, b0).lerp(_lut_fetch(r, g, b1), t)

func _lut_fetch(r: float, g: float, b: float) -> Vector3:
	var px := int(round(b * LUT_SIZE + r))
	var py := int(round(g))
	px = clamp(px, 0, _lut.get_width() - 1)
	py = clamp(py, 0, _lut.get_height() - 1)
	var c := _lut.get_pixel(px, py)
	return Vector3(c.r, c.g, c.b)
