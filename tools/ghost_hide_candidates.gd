extends SceneTree
# 숨는 자리 후보: 설비 AABB 에서 0.45m 이상 떨어지고, CCTV 화면 안(발·머리 모두)이며,
# WorkSpot · 기본 슬롯과 0.7m 이상 떨어진 바닥 점을 방마다 지도로 찍는다.
#   godot --headless --path . -s res://tools/ghost_hide_candidates.gd
#   o = 후보 · , = 작업 자리/슬롯과 가까움 · ~ = CCTV 화면 밖 · - = 설비와 가까움 · # = 설비 · S = WorkSpot
# 방 씬의 GhostHideSpots 마커(괴물 반응 표현 전용)를 옮길 때 이 지도의 o 위에 둔다.
var rooms = {
	"power_room": "res://scenes/rooms/room_power.tscn",
	"vent_room": "res://scenes/rooms/room_vent.tscn",
	"maintenance_room": "res://scenes/rooms/room_maintenance.tscn",
	"medical_room": "res://scenes/rooms/room_medical.tscn",
	"guard_room": "res://scenes/rooms/room_guard.tscn",
	"core_room": "res://scenes/rooms/room_core.tscn",
	"storage_room": "res://scenes/rooms/room_storage.tscn",
}
var slots = [Vector3(-0.3,0,-0.6), Vector3(0.9,0,-1.2), Vector3(-1.3,0,0.2), Vector3(0.4,0,0.9), Vector3(1.4,0,0.1), Vector3(-1.0,0,1.3)]
func collect(n, xf, out):
	var t = xf
	if n is Node3D: t = xf * n.transform
	if n is MeshInstance3D and n.visible: out.append([n, t])
	for c in n.get_children(): collect(c, t, out)
func spots(n, xf, out):
	var t = xf
	if n is Node3D: t = xf * n.transform
	if n.get_script() != null and str(n.get_script().resource_path).ends_with("RoomWorkSpot.cs"): out.append(t.origin)
	for c in n.get_children(): spots(c, t, out)
func dist_aabb(x, z, bb: AABB) -> float:
	var dx = max(bb.position.x - x, 0.0, x - bb.end.x)
	var dz = max(bb.position.z - z, 0.0, z - bb.end.z)
	return sqrt(dx*dx + dz*dz)
func visible(cam: Transform3D, p: Vector3) -> bool:
	var v = cam.affine_inverse() * p
	if -v.z < 0.1: return false
	var tv = tan(deg_to_rad(58.0) * 0.5) * 0.92
	var th = tv * 4.0 / 3.0 * 0.95
	return abs(v.x / -v.z) < th and abs(v.y / -v.z) < tv
func _init():
	var cam = Transform3D(Basis.IDENTITY, Vector3(3.5, 3.05, 3.5)).looking_at(Vector3(-0.4, 0.5, -0.5), Vector3.UP)
	for id in rooms:
		var r = load(rooms[id]).instantiate()
		root.add_child(r)
		var ms = []
		collect(r, Transform3D.IDENTITY, ms)
		var boxes = []
		for mm in ms:
			var bb: AABB = mm[1] * mm[0].get_aabb()
			if bb.end.y < 0.05 or bb.position.y > 1.8: continue
			if bb.size.x > 5.5 and bb.size.z > 5.5: continue
			boxes.append(bb)
		var sp = []
		spots(r, Transform3D.IDENTITY, sp)
		print("=== ", id)
		var z = -3.0
		while z <= 3.01:
			var line = "%5.2f " % z
			var x = -3.0
			while x <= 3.01:
				var clear = 9.0
				for bb in boxes: clear = min(clear, dist_aabb(x, z, bb))
				var ch = "#" if clear < 0.01 else ("-" if clear < 0.45 else ".")
				if ch == ".":
					var near = 9.0
					for s in sp: near = min(near, Vector2(s.x - x, s.z - z).length())
					for s in slots: near = min(near, Vector2(s.x - x, s.z - z).length())
					var vis = visible(cam, Vector3(x, 0.05, z)) and visible(cam, Vector3(x, 1.3, z))
					if not vis: ch = "~"
					elif near < 0.7: ch = ","
					else: ch = "o"
				for s in sp:
					if abs(s.x - x) < 0.125 and abs(s.z - z) < 0.125: ch = "S"
				line += ch
				x += 0.25
			print(line)
			z += 0.25
		r.queue_free()
	quit()
