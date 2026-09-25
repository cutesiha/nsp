extends SceneTree
# 클립의 특정 시점 자세를 격자 한 장으로 찍는다(창 모드로 실행해야 그림이 나온다).
#   godot --path . -s res://tools/ghost_pose_sheet.gd -- out.png ghost_sheep_drop:0,0.14,0.48,0.9 ...
# 각 칸은 위 = CCTV 각도(대각선 위에서), 아래 = 옆모습.

const CELL := Vector2i(230, 290)
var SCENES := {
	"sheep": "res://scenes/cctv_characters/employees/SheepEmployee3D.tscn",
	"rabbit": "res://scenes/cctv_characters/employees/RabbitEmployee3D.tscn",
	"cat": "res://scenes/cctv_characters/employees/CatEmployee3D.tscn",
	"dog": "res://scenes/cctv_characters/employees/DogEmployee3D.tscn",
	"wolf": "res://scenes/cctv_characters/employees/WolfEmployee3D.tscn",
	"fox": "res://scenes/cctv_characters/employees/FoxEmployee3D.tscn",
}
var jobs := []      # [clip, t]
var cols := 0
var out_path := ""
var vp: SubViewport
var cam_a: Camera3D
var cam_b: Camera3D
var actors := {}
var sheet: Image
var i := 0
var phase := 0
var wait := 0

func _initialize():
	var args := OS.get_cmdline_user_args()
	out_path = args[0]
	for a in args.slice(1):
		var parts = a.split(":")
		var ts = parts[-1].split(",")
		cols = max(cols, ts.size())
		for t in ts: jobs.append([parts[0], float(t)])
	var rows := 0
	for a in args.slice(1): rows += 1
	sheet = Image.create(CELL.x * cols, CELL.y * 2 * rows, false, Image.FORMAT_RGBA8)
	sheet.fill(Color(0.05, 0.05, 0.06))

	vp = SubViewport.new()
	vp.size = CELL
	vp.own_world_3d = true
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	root.add_child(vp)
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.12, 0.13, 0.15)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.7, 0.72, 0.78)
	env.ambient_light_energy = 0.8
	var we := WorldEnvironment.new(); we.environment = env; vp.add_child(we)
	var sun := DirectionalLight3D.new(); vp.add_child(sun)
	sun.rotation_degrees = Vector3(-50, 30, 0)
	var floor := MeshInstance3D.new(); var pm := PlaneMesh.new(); pm.size = Vector2(6, 6); floor.mesh = pm
	var fm := StandardMaterial3D.new(); fm.albedo_color = Color(0.3, 0.31, 0.34); floor.material_override = fm
	vp.add_child(floor)
	# 바닥 격자(0.5m) — 발이 뜨거나 묻히는지 보기 쉽게.
	for k in range(-4, 5):
		for axis in 2:
			var ln := MeshInstance3D.new(); var bm := BoxMesh.new()
			bm.size = Vector3(4.0, 0.004, 0.01) if axis == 0 else Vector3(0.01, 0.004, 4.0)
			ln.mesh = bm; ln.position = Vector3(0, 0.002, k * 0.5) if axis == 0 else Vector3(k * 0.5, 0.002, 0)
			var lm := StandardMaterial3D.new(); lm.albedo_color = Color(0.45, 0.46, 0.5); ln.material_override = lm
			vp.add_child(ln)
	cam_a = Camera3D.new(); vp.add_child(cam_a); cam_a.fov = 40
	cam_a.look_at_from_position(Vector3(1.9, 2.5, -2.7), Vector3(0, 0.6, 0))
	cam_b = Camera3D.new(); vp.add_child(cam_b); cam_b.fov = 40
	cam_b.look_at_from_position(Vector3(3.3, 0.9, 0.0), Vector3(0, 0.75, 0))

var box: MeshInstance3D

func _actor(clip: String) -> Node3D:
	# "who/clip" 로 직원을 고를 수 있다(없으면 ghost_<who>_… 에서 읽고, 그 외는 여우).
	var who := "fox"
	if clip.contains("/"): who = clip.split("/")[0]
	elif clip.begins_with("ghost_"): who = clip.split("_")[1]
	if not actors.has(who):
		var a: Node3D = load(SCENES[who]).instantiate()
		vp.add_child(a)
		var acc = a.get_node_or_null("AccessoryAnchor")
		if acc: acc.reparent(a.get_node("VisualRoot/RigRoot/Hips/Torso/Chest"), true)
		actors[who] = a
	for k in actors: actors[k].visible = k == who
	return actors[who]

func _process(_d):
	if i >= jobs.size():
		sheet.save_png(out_path)
		print("saved ", out_path)
		quit()
		return false
	var job = jobs[i]
	var a := _actor(job[0])
	var clip_name: String = job[0].split("/")[-1]
	var ap: AnimationPlayer = a.get_node("AnimationPlayer")
	if box == null:
		box = MeshInstance3D.new(); var bm := BoxMesh.new(); bm.size = Vector3(0.34, 0.26, 0.28); box.mesh = bm
		var mat := StandardMaterial3D.new(); mat.albedo_color = Color(0.62, 0.45, 0.25); box.material_override = mat
		vp.add_child(box)
	if wait == 1 and clip_name.contains("box"):
		# 두 손바닥 사이(조금 위)에 실제 크기 박스 — 손이 옆면에 닿는지 본다.
		var hl: Node3D = a.get_node("VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderL/UpperArmL/LowerArmL/HandL")
		var hr: Node3D = a.get_node("VisualRoot/RigRoot/Hips/Torso/Chest/ShoulderR/UpperArmR/LowerArmR/HandR")
		var pl := hl.global_transform * Vector3(0, -0.06, 0)
		var pr := hr.global_transform * Vector3(0, -0.06, 0)
		var chest: Node3D = a.get_node("VisualRoot/RigRoot/Hips/Torso/Chest")
		var up := chest.global_transform.basis.y.normalized()
		box.global_position = (pl + pr) * 0.5 + up * 0.26 * 0.28
		box.global_basis = chest.global_transform.basis.orthonormalized()
		box.visible = true
	elif wait == 1:
		box.visible = false
	if wait == 0:
		ap.play(clip_name, 0.0)
		ap.seek(job[1], true)
		ap.pause()
		(cam_a if phase == 0 else cam_b).current = true
		wait = 3
		return false
	wait -= 1
	if wait > 0: return false
	var img := vp.get_texture().get_image()
	img.convert(Image.FORMAT_RGBA8)
	var row := 0
	var col := 0
	var n := 0
	# 몇 번째 인자(행)인지 다시 센다.
	var args := OS.get_cmdline_user_args().slice(1)
	var idx := i
	for r in args.size():
		var cnt = args[r].split(":")[-1].split(",").size()
		if idx < cnt: row = r; col = idx; break
		idx -= cnt
	sheet.blit_rect(img, Rect2i(Vector2i.ZERO, CELL), Vector2i(col * CELL.x, (row * 2 + phase) * CELL.y))
	if phase == 0: phase = 1
	else:
		phase = 0
		i += 1
	return false
