extends SceneTree
func _init():
	await process_frame
	await process_frame
	var main: Node = load("res://scenes/main/MainScene3D_Test.tscn").instantiate()
	root.add_child(main)
	for i in range(30):
		await process_frame
	main.get_node("TitleOverlay").emit_signal("StartRequested")
	for i in range(120):
		await process_frame

	# 코어실에 배치해 "근무 시작" 버튼을 활성화한 뒤 실제로 누른다.
	var sim = root.get_node("/root/FacilitySimulation")
	sim.call("AssignToRoom", "cat", "core_room")
	var board = main.get_node("ControlRoom/DeskScheduleBoard")
	board.call("Refresh")
	await process_frame
	var vp: SubViewport = null
	for c in board.get_children():
		if c is SubViewport: vp = c; break
	var p = Vector2(768 - 214 + 95, 560 - 58 + 23) * 1.3
	for b in [true, false]:
		var mb = InputEventMouseButton.new()
		mb.button_index = MOUSE_BUTTON_LEFT
		mb.pressed = b
		mb.position = p; mb.global_position = p
		vp.push_input(mb, true)
		await process_frame
	for i in range(200):
		await process_frame
	print("ZOOM shift started")

	for pair in [[KEY_3, "sensor"], [KEY_4, "power"]]:
		var k = InputEventKey.new()
		k.keycode = pair[0]
		k.pressed = true
		main.call("_Input", k)
		for i in range(45):
			await process_frame
		root.get_texture().get_image().save_png("res://scratch_zoom_%s.png" % pair[1])
		print("ZOOM shot ", pair[1])
		var esc = InputEventKey.new()
		esc.keycode = KEY_ESCAPE
		esc.pressed = true
		main.call("_Input", esc)
		for i in range(40):
			await process_frame
	quit()
