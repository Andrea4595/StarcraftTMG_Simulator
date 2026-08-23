extends Control

## 미션 생성 화면 - 지형 배치 기능.
## 배치구역 설정 / 미션 목표 배치는 이후 단계에서 추가된다.

const TERRAIN_PIECE_SCENE := preload("res://scenes/mission_setup/TerrainPiece.tscn")
const RADIAL_MENU_SCENE := preload("res://scenes/common/RadialMenu.tscn")
const MAP_GRID_SCRIPT := preload("res://scenes/mission_setup/MapGrid.gd")

const MARGIN := 8.0
const TOP_ROW_HEIGHT := 32.0
const PALETTE_WIDTH := 180.0
const GRID_SIZE_MM := 12.7 # 0.5인치

var _map_area: Control
var _map_background: ColorRect
var _map_grid: Control
var _terrain_layer: Control

var _radial_menu: Control
var _menu_target: TextureRect = null

var _palette_buttons: Array[Button] = []
var _size_buttons: Array[Button] = []

var _placement_module_id: String = ""
var _current_preset: String = GameConstants.DEFAULT_MAP_SIZE_PRESET

var _dragging_piece: TextureRect = null
var _drag_offset: Vector2 = Vector2.ZERO


func _ready() -> void:
	_build_size_row()
	_build_palette()
	_build_map_area()
	_build_radial_menu()
	_apply_preset(_current_preset)
	resized.connect(_layout)


func _build_size_row() -> void:
	var row := HBoxContainer.new()
	row.position = Vector2(MARGIN, MARGIN)
	add_child(row)

	var group := ButtonGroup.new()
	for preset_name in GameConstants.MAP_SIZE_PRESETS.keys():
		var btn := Button.new()
		btn.text = _preset_label(preset_name)
		btn.toggle_mode = true
		btn.button_group = group
		btn.set_meta("preset", preset_name)
		btn.pressed.connect(_on_size_preset_pressed.bind(preset_name))
		row.add_child(btn)
		_size_buttons.append(btn)


func _build_palette() -> void:
	var panel := PanelContainer.new()
	panel.position = Vector2(MARGIN, MARGIN + TOP_ROW_HEIGHT + MARGIN)
	add_child(panel)

	var box := VBoxContainer.new()
	panel.add_child(box)

	for module in TerrainCatalog.modules:
		var btn := Button.new()
		btn.text = module.display_name
		btn.toggle_mode = true
		btn.custom_minimum_size = Vector2(PALETTE_WIDTH - 16.0, 40.0)
		btn.toggled.connect(_on_palette_button_toggled.bind(module.id, btn))
		box.add_child(btn)
		_palette_buttons.append(btn)


func _build_map_area() -> void:
	_map_area = Control.new()
	_map_area.name = "MapArea"
	_map_area.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_map_area)

	_map_background = ColorRect.new()
	_map_background.color = Color(0.15, 0.18, 0.15)
	_map_background.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_map_background)

	_map_grid = Control.new()
	_map_grid.set_script(MAP_GRID_SCRIPT)
	_map_grid.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_map_grid)

	_terrain_layer = Control.new()
	_terrain_layer.name = "TerrainLayer"
	_terrain_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_terrain_layer)


func _build_radial_menu() -> void:
	_radial_menu = RADIAL_MENU_SCENE.instantiate()
	add_child(_radial_menu)
	_radial_menu.action_chosen.connect(_on_menu_action_chosen)


func _preset_label(preset_name: String) -> String:
	var parts := preset_name.split("x")
	return "%s x %s\"" % [parts[0], parts[1]]


func _on_size_preset_pressed(preset_name: String) -> void:
	if preset_name == _current_preset:
		return
	_apply_preset(preset_name)


func _apply_preset(preset_name: String) -> void:
	_current_preset = preset_name
	for btn in _size_buttons:
		btn.button_pressed = (btn.get_meta("preset") == preset_name)

	for child in _terrain_layer.get_children():
		child.queue_free()

	var map_size: Vector2 = GameConstants.MAP_SIZE_PRESETS[preset_name]
	_map_background.size = map_size
	_map_grid.size = map_size
	_map_grid.queue_redraw()
	_terrain_layer.size = map_size
	_layout()


func _layout() -> void:
	if _map_area == null:
		return

	var top := MARGIN + TOP_ROW_HEIGHT + MARGIN
	var left := MARGIN + PALETTE_WIDTH + MARGIN
	var viewport_pos := Vector2(left, top)
	var viewport_size := Vector2(max(size.x - left - MARGIN, 10.0), max(size.y - top - MARGIN, 10.0))

	var map_size: Vector2 = GameConstants.MAP_SIZE_PRESETS[_current_preset]
	var scale_factor: float = min(viewport_size.x / map_size.x, viewport_size.y / map_size.y)
	scale_factor = min(scale_factor, 1.0)

	_map_area.scale = Vector2(scale_factor, scale_factor)
	var scaled_size := map_size * scale_factor
	_map_area.position = viewport_pos + (viewport_size - scaled_size) / 2.0


func _on_palette_button_toggled(pressed: bool, module_id: String, button: Button) -> void:
	if pressed:
		for other in _palette_buttons:
			if other != button:
				other.button_pressed = false
		_placement_module_id = module_id
	elif _placement_module_id == module_id:
		_placement_module_id = ""


func _clear_placement_mode() -> void:
	_placement_module_id = ""
	for btn in _palette_buttons:
		btn.button_pressed = false


func _input(event: InputEvent) -> void:
	if _dragging_piece:
		_handle_drag_input(event)
		return

	if _placement_module_id != "" and event is InputEventMouseButton \
			and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_handle_placement_click()


func _snap_to_grid(point: Vector2) -> Vector2:
	return (point / GRID_SIZE_MM).round() * GRID_SIZE_MM


func _handle_drag_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		var local: Vector2 = _map_area.get_local_mouse_position()
		_dragging_piece.set_center(_snap_to_grid(local + _drag_offset))
		_clamp_piece_to_bounds(_dragging_piece)
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
		_dragging_piece = null
		get_viewport().set_input_as_handled()


func _handle_placement_click() -> void:
	var local: Vector2 = _map_area.get_local_mouse_position()
	var map_size: Vector2 = GameConstants.MAP_SIZE_PRESETS[_current_preset]
	if local.x < 0.0 or local.x > map_size.x or local.y < 0.0 or local.y > map_size.y:
		return

	_place_piece(_placement_module_id, _snap_to_grid(local))
	_clear_placement_mode()
	get_viewport().set_input_as_handled()


func _place_piece(module_id: String, local_point: Vector2) -> void:
	var module := TerrainCatalog.get_module(module_id)
	if module == null:
		return

	var piece: TextureRect = TERRAIN_PIECE_SCENE.instantiate()
	_terrain_layer.add_child(piece)
	piece.setup(module)
	piece.set_center(local_point)
	_clamp_piece_to_bounds(piece)
	piece.drag_requested.connect(_on_piece_drag_requested)
	piece.menu_requested.connect(_on_piece_menu_requested)
	piece.rotate_requested.connect(_on_piece_rotate_requested)


func _on_piece_drag_requested(piece: TextureRect) -> void:
	_dragging_piece = piece
	_drag_offset = piece.center() - _map_area.get_local_mouse_position()
	_terrain_layer.move_child(piece, _terrain_layer.get_child_count() - 1)


func _on_piece_rotate_requested(piece: TextureRect, direction: int) -> void:
	piece.rotate_step(direction)
	_clamp_piece_to_bounds(piece)


func _on_piece_menu_requested(piece: TextureRect, screen_pos: Vector2) -> void:
	_menu_target = piece
	_radial_menu.open([
		{"label": "회전", "action": "rotate"},
		{"label": "삭제", "action": "delete"},
	], screen_pos)


func _on_menu_action_chosen(action: String) -> void:
	if _menu_target == null:
		return
	match action:
		"rotate":
			_menu_target.rotate_step()
			_clamp_piece_to_bounds(_menu_target)
		"delete":
			_menu_target.queue_free()
	_menu_target = null


func _clamp_piece_to_bounds(piece: TextureRect) -> void:
	var map_size: Vector2 = GameConstants.MAP_SIZE_PRESETS[_current_preset]
	var half: Vector2 = piece.rotated_half_extent()
	var c: Vector2 = piece.center()
	c.x = clamp(c.x, half.x, max(half.x, map_size.x - half.x))
	c.y = clamp(c.y, half.y, max(half.y, map_size.y - half.y))
	piece.set_center(c)
