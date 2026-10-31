extends SceneTree

# [일회용 도구] 모니터 외곽선 잉크 텍스처에서 **모서리 밖으로 삐져나온 획**을 지운다.
#
#   godot --headless --path . --script res://scripts/debug/trim_ink.gd
#
# 손으로 그은 선이라 네 모서리마다 획이 테두리를 넘어 조금씩 튀어나와 있다. 화면에서는
# 그게 모니터 밖으로 삐죽 솟은 선으로 보인다.
#
# 테두리 자체가 어디까지인지는 **그림에게 물어본다** — 가운데 가로줄 · 세로줄을 훑어
# 테두리의 바깥 끝을 찾고, 그 바깥으로 나간 픽셀만 지운다. 선 굵기나 위치를 손으로
# 적어 넣지 않으므로 두 장(M01 · M02)에 그대로 쓸 수 있다.

const ALPHA_MIN := 8      # 이 이상이면 "그려진 픽셀" 로 친다(가장자리 흐림 무시)

func _init() -> void:
	for name in ["analog_ink_m01", "analog_ink_m02"]:
		_trim("res://shaders/%s.png" % name)
	quit()

func _trim(res_path: String) -> void:
	var abs_path := ProjectSettings.globalize_path(res_path)
	var img := Image.load_from_file(abs_path)
	if img == null:
		print("[trim] 못 읽음: ", abs_path)
		return

	var w := img.get_width()
	var h := img.get_height()

	# 테두리의 바깥 끝 — 가운데 줄에서 찾는다(모서리의 삐침이 섞이지 않는 자리).
	var top := _first_opaque_y(img, w / 2, h, false)
	var bottom := _first_opaque_y(img, w / 2, h, true)
	var left := _first_opaque_x(img, h / 2, w, false)
	var right := _first_opaque_x(img, h / 2, w, true)

	if top < 0 or bottom < 0 or left < 0 or right < 0:
		print("[trim] 테두리를 찾지 못함: ", res_path)
		return

	var cleared := 0
	for y in range(h):
		for x in range(w):
			if y >= top and y <= bottom and x >= left and x <= right:
				continue
			var c := img.get_pixel(x, y)
			if c.a <= 0.0:
				continue
			img.set_pixel(x, y, Color(c.r, c.g, c.b, 0.0))
			cleared += 1

	img.save_png(abs_path)
	print("[trim] %s  테두리 x %d~%d · y %d~%d  지운 픽셀 %d" % [res_path, left, right, top, bottom, cleared])

# 가운데 세로줄을 위(또는 아래)에서 훑어 처음 만나는 그려진 픽셀의 y.
func _first_opaque_y(img: Image, x: int, h: int, from_bottom: bool) -> int:
	var ys := range(h - 1, -1, -1) if from_bottom else range(h)
	for y in ys:
		if img.get_pixel(x, y).a * 255.0 >= ALPHA_MIN:
			return y
	return -1

func _first_opaque_x(img: Image, y: int, w: int, from_right: bool) -> int:
	var xs := range(w - 1, -1, -1) if from_right else range(w)
	for x in xs:
		if img.get_pixel(x, y).a * 255.0 >= ALPHA_MIN:
			return x
	return -1
