extends SceneTree

func _dump(n: Node, depth: int) -> void:
	var pad := ""
	for i in depth:
		pad += "  "
	var extra := ""
	if n is MeshInstance3D:
		var mi := n as MeshInstance3D
		var ab := mi.get_aabb()
		extra = " AABB pos=%s size=%s  surfaces=%d" % [ab.position, ab.size, mi.get_surface_override_material_count()]
		if mi.mesh:
			for s in mi.mesh.get_surface_count():
				extra += "  mat[%d]=%s" % [s, str(mi.mesh.surface_get_material(s))]
	if n is Node3D:
		extra += "  xform_origin=%s scale=%s" % [(n as Node3D).transform.origin, (n as Node3D).scale]
	print(pad, n.name, " [", n.get_class(), "]", extra)
	for c in n.get_children():
		_dump(c, depth + 1)

func _init() -> void:
	for p in ["res://assets/models/NSP_Quarantine_room.glb", "res://assets/models/entity.glb"]:
		print("\n=================== ", p, " ===================")
		var ps: PackedScene = load(p)
		if ps == null:
			print("  LOAD FAILED")
			continue
		var inst := ps.instantiate()
		_dump(inst, 1)
		inst.free()
	quit()
