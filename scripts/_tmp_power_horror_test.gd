extends SceneTree

func alive_count(sim) -> int:
	var count := 0
	for id in sim.GetEmployeeIds():
		if sim.GetEmployeeState(id).Alive:
			count += 1
	return count

func _initialize() -> void:
	await process_frame
	await process_frame
	var gs = root.get_node("GameState")
	var sim = root.get_node("FacilitySimulation")
	gs.ResetRun()
	sim.ResetRun()
	gs.SetSaboteur("fox")
	gs.SetPhase(2)
	var before := alive_count(sim)
	gs.TryTogglePower(0)
	sim.Tick(0.1)
	var after_first := alive_count(sim)
	for i in 10:
		sim.Tick(0.1)
	gs.TryTogglePower(2)
	for i in 10:
		sim.Tick(0.1)
	print("[TEST] power_murder_counts=", before, ">", after_first, ">", alive_count(sim))

	# 후속 점프스케어 및 물리 적색 조명용 최소 3D 트리.
	gs.ResetRun()
	sim.ResetRun()
	var stage := Node3D.new()
	stage.name = "Stage"
	root.add_child(stage)
	var control_room := Node3D.new()
	control_room.name = "ControlRoom"
	stage.add_child(control_room)
	var lights := Node3D.new()
	lights.name = "Lights"
	control_room.add_child(lights)
	var ceiling := OmniLight3D.new()
	ceiling.name = "CeilingLight"
	ceiling.light_energy = 2.9
	lights.add_child(ceiling)
	var fill := OmniLight3D.new()
	fill.name = "FillLight"
	fill.light_energy = 0.34
	lights.add_child(fill)
	var emergency := OmniLight3D.new()
	emergency.name = "EmergencyLight"
	emergency.visible = false
	emergency.light_energy = 0.0
	lights.add_child(emergency)
	var rig := Node3D.new()
	rig.name = "PlayerSeatRig"
	stage.add_child(rig)
	var camera := Camera3D.new()
	camera.name = "Camera3D"
	rig.add_child(camera)
	var room_horror = load("res://scripts/view/ControlRoom3DHorror.cs").new()
	room_horror.name = "ControlRoom3DHorror"
	stage.add_child(room_horror)
	var director = load("res://scripts/ui/HorrorDirector.cs").new()
	director.name = "HorrorDirector"
	stage.add_child(director)
	await process_frame
	gs.SetPhase(2)
	await process_frame
	room_horror.ActivateTabooAlert()
	await create_timer(1.0).timeout
	print("[TEST] taboo_lights=ceiling:", "%.2f" % ceiling.light_energy,
		" emergency:", "%.2f" % emergency.light_energy,
		" red:", "%.2f" % ceiling.light_color.r, ",", "%.2f" % ceiling.light_color.g)

	gs.ResetDayClock()
	gs.AdvanceDayTime(179.4)
	director.SchedulePostTabooJumpscare()
	var saw_face := false
	for i in 90:
		await process_frame
		var face = camera.find_child("EntityFaceJumpscare", true, false)
		if face != null and face.visible:
			saw_face = true
	print("[TEST] post_taboo_face_seen=", saw_face)
	quit()
