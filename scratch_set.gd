extends SceneTree
func _init():
	await process_frame
	await process_frame
	var main: Node = load("res://scenes/main/MainScene3D_Test.tscn").instantiate()
	root.add_child(main)
	for i in range(40):
		await process_frame
	var title = main.get_node("TitleOverlay")
	title.call("OpenSettings")
	for i in range(40):
		await process_frame
	root.get_texture().get_image().save_png("res://scratch_set.png")
	print("SET shot saved")
	quit()
