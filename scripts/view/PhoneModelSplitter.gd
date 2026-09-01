extends Node

# The imported rotary phone is one MeshInstance, but its receiver is a distinct
# upper mesh island.  Split faces by height once so Phone3D can move only the
# receiver while retaining the model's original material and UVs.
const RECEIVER_MIN_Y := 0.40

func _ready() -> void:
	var phone := get_parent() as Node3D
	var model := phone.get_node_or_null("PhoneModel") as Node3D
	var handset := phone.get_node_or_null("Handset") as Node3D
	if model == null or handset == null:
		return
	var source := _find_mesh(model)
	if source == null or source.mesh == null or source.mesh.get_surface_count() != 1:
		push_warning("PhoneModelSplitter: imported phone mesh could not be separated.")
		return

	var arrays := source.mesh.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	if vertices.is_empty():
		return
	if indices.is_empty():
		for i in vertices.size():
			indices.append(i)

	var body_indices := PackedInt32Array()
	var receiver_indices := PackedInt32Array()
	for i in range(0, indices.size() - 2, 3):
		var average_y := (vertices[indices[i]].y + vertices[indices[i + 1]].y + vertices[indices[i + 2]].y) / 3.0
		if average_y >= RECEIVER_MIN_Y:
			receiver_indices.append(indices[i])
			receiver_indices.append(indices[i + 1])
			receiver_indices.append(indices[i + 2])
		else:
			body_indices.append(indices[i])
			body_indices.append(indices[i + 1])
			body_indices.append(indices[i + 2])

	if body_indices.is_empty() or receiver_indices.is_empty():
		push_warning("PhoneModelSplitter: receiver faces were not found.")
		return

	var material := source.get_active_material(0)
	source.mesh = _mesh_from(arrays, body_indices, material)

	var receiver_mesh := MeshInstance3D.new()
	receiver_mesh.name = "ImportedReceiverMesh"
	receiver_mesh.mesh = _mesh_from(arrays, receiver_indices, material)
	receiver_mesh.transform = source.transform
	receiver_mesh.scale = model.scale
	handset.add_child(receiver_mesh)

	# The receiver's mesh now lives beside PhoneModel, so the original model root
	# no longer supplies its scale.  Keep the grip/rest markers at its real centre.
	var receiver_center := Vector3(0.0, 0.55 * model.scale.y, 0.0)
	var grip := handset.get_node_or_null("ReceiverGripPoint") as Marker3D
	if grip != null:
		grip.position = receiver_center
	var rest := phone.get_node_or_null("ReceiverRestPoint") as Marker3D
	if rest != null:
		rest.position = receiver_center

func _mesh_from(source_arrays: Array, kept_indices: PackedInt32Array, material: Material) -> ArrayMesh:
	var arrays := source_arrays.duplicate(true)
	arrays[Mesh.ARRAY_INDEX] = kept_indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	mesh.surface_set_material(0, material)
	return mesh

func _find_mesh(node: Node) -> MeshInstance3D:
	if node is MeshInstance3D:
		return node
	for child in node.get_children():
		var result := _find_mesh(child)
		if result != null:
			return result
	return null
