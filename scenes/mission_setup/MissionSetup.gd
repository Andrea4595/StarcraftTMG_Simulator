extends Control

## 미션 생성 화면 - 지형 배치 / 배치구역 설정 기능.
## 미션 목표 배치는 이후 단계에서 추가된다.

const TERRAIN_PIECE_SCENE := preload("res://scenes/mission_setup/TerrainPiece.tscn")
const RADIAL_MENU_SCENE := preload("res://scenes/common/RadialMenu.tscn")
const MAP_GRID_SCRIPT := preload("res://scenes/mission_setup/MapGrid.gd")
const DEPLOYMENT_ZONE_SCRIPT := preload("res://scenes/mission_setup/DeploymentZonePiece.gd")

const MARGIN := 8.0
const TOP_ROW_HEIGHT := 32.0
const PALETTE_WIDTH := 180.0
const GRID_SIZE_MM := 12.7 # 0.5인치
const EDGE_SNAP_THRESHOLD_MM := 15.0
const ZONE_VISUAL_THICKNESS_MM := 6.0
const ZONE_HIT_THICKNESS_MM := 24.0

const ZONE_COLORS := {
	"A": Color(1.0, 0.15, 0.15, 0.9),
	"B": Color(0.15, 0.35, 1.0, 0.9),
}

var _map_area: Control
var _map_background: ColorRect
var _map_grid: Control
var _zone_layer: Control
var _terrain_layer: Control

var _radial_menu: Control
var _menu_target: Control = null

var _palette_buttons: Array[Button] = []
var _zone_buttons: Array[Button] = []
var _size_buttons: Array[Button] = []

var _placement_module_id: String = ""
var _active_zone_player: String = ""
var _current_preset: String = GameConstants.DEFAULT_MAP_SIZE_PRESET

var _dragging_piece: TextureRect = null
var _drag_offset: Vector2 = Vector2.ZERO

var _drawing_zone: Control = null
var _zone_current_length: float = 0.0
var _zone_down_local: Vector2 = Vector2.ZERO
## 모서리가 아닌 변에서 시작하면 처음부터 고정, 모서리에서 시작하면 ""로 두고
## 드래그 방향에 따라 매 프레임 h/v 후보 중에서 다시 고른다.
var _zone_locked_edge: String = ""
var _zone_candidate_h: String = "" # "top" / "bottom" / ""
var _zone_candidate_v: String = "" # "left" / "right" / ""


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

	box.add_child(HSeparator.new())

	var zone_label := Label.new()
	zone_label.text = "배치구역 (지도 가장자리에서 드래그)"
	zone_label.autowrap_mode = TextServer.AUTOWRAP_WORD
	box.add_child(zone_label)

	for player in ["A", "B"]:
		var btn := Button.new()
		btn.text = "%s 배치구역" % player
		btn.toggle_mode = true
		btn.custom_minimum_size = Vector2(PALETTE_WIDTH - 16.0, 40.0)
		var text_color: Color = ZONE_COLORS[player]
		text_color.a = 1.0
		btn.add_theme_color_override("font_color", text_color)
		btn.toggled.connect(_on_zone_button_toggled.bind(player, btn))
		box.add_child(btn)
		_zone_buttons.append(btn)


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

	_zone_layer = Control.new()
	_zone_layer.name = "ZoneLayer"
	_zone_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_zone_layer)

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
	for child in _zone_layer.get_children():
		child.queue_free()
	_drawing_zone = null

	var map_size: Vector2 = GameConstants.MAP_SIZE_PRESETS[preset_name]
	_map_background.size = map_size
	_map_grid.size = map_size
	_map_grid.queue_redraw()
	_zone_layer.size = map_size
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
		_deactivate_other_mode_buttons(button)
		_active_zone_player = ""
		_placement_module_id = module_id
	elif _placement_module_id == module_id:
		_placement_module_id = ""


func _on_zone_button_toggled(pressed: bool, player: String, button: Button) -> void:
	if pressed:
		_deactivate_other_mode_buttons(button)
		_placement_module_id = ""
		_active_zone_player = player
	elif _active_zone_player == player:
		_active_zone_player = ""


func _deactivate_other_mode_buttons(except: Button) -> void:
	for other in _palette_buttons:
		if other != except:
			other.button_pressed = false
	for other in _zone_buttons:
		if other != except:
			other.button_pressed = false


func _clear_placement_mode() -> void:
	_placement_module_id = ""
	for btn in _palette_buttons:
		btn.button_pressed = false


func _input(event: InputEvent) -> void:
	if _dragging_piece:
		_handle_drag_input(event)
		return

	if _drawing_zone:
		_handle_zone_drawing_input(event)
		return

	if _placement_module_id != "" and event is InputEventMouseButton \
			and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_handle_placement_click()
		return

	if _active_zone_player != "" and event is InputEventMouseButton \
			and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		_handle_zone_start_click()


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


func _snap_along_edge(local: Vector2, edge: String, map_size: Vector2) -> float:
	var value: float = local.y if (edge == "left" or edge == "right") else local.x
	var max_value: float = map_size.y if (edge == "left" or edge == "right") else map_size.x
	value = clamp(value, 0.0, max_value)
	return clamp(_snap_to_inch(value), 0.0, max_value)


func _snap_to_inch(value: float) -> float:
	return round(value / GameConstants.MM_PER_INCH) * GameConstants.MM_PER_INCH


func _handle_zone_start_click() -> void:
	var local: Vector2 = _map_area.get_local_mouse_position()
	var map_size: Vector2 = GameConstants.MAP_SIZE_PRESETS[_current_preset]
	if local.x < 0.0 or local.x > map_size.x or local.y < 0.0 or local.y > map_size.y:
		return

	var d_left := local.x
	var d_right := map_size.x - local.x
	var d_top := local.y
	var d_bottom := map_size.y - local.y

	var candidate_v := ""
	if min(d_left, d_right) <= EDGE_SNAP_THRESHOLD_MM:
		candidate_v = "left" if d_left <= d_right else "right"

	var candidate_h := ""
	if min(d_top, d_bottom) <= EDGE_SNAP_THRESHOLD_MM:
		candidate_h = "top" if d_top <= d_bottom else "bottom"

	if candidate_v == "" and candidate_h == "":
		return

	_zone_down_local = local
	_zone_candidate_v = candidate_v
	_zone_candidate_h = candidate_h
	# 변 하나에서만 시작했다면 그 변으로 고정, 모서리라면 드래그 방향에 따라
	# 매 프레임 다시 판단한다 (아래 _update_zone_drawing 참고).
	_zone_locked_edge = candidate_h if candidate_v == "" else (candidate_v if candidate_h == "" else "")

	_drawing_zone = _create_zone_piece(_active_zone_player)
	_update_zone_drawing(local, map_size)
	get_viewport().set_input_as_handled()


func _handle_zone_drawing_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		var local: Vector2 = _map_area.get_local_mouse_position()
		var map_size: Vector2 = GameConstants.MAP_SIZE_PRESETS[_current_preset]
		_update_zone_drawing(local, map_size)
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
		_finish_zone_drawing()
		get_viewport().set_input_as_handled()


func _current_drag_edge(local: Vector2) -> String:
	if _zone_locked_edge != "":
		return _zone_locked_edge
	# 모서리에서 시작한 경우: 눌렀던 지점부터 지금까지의 누적 이동 방향으로
	# 매번 다시 판단한다. 놓기 전까지는 언제든 가로↔세로를 바꿀 수 있다.
	var delta: Vector2 = local - _zone_down_local
	if absf(delta.x) >= absf(delta.y):
		return _zone_candidate_h
	return _zone_candidate_v


func _update_zone_drawing(local: Vector2, map_size: Vector2) -> void:
	var edge := _current_drag_edge(local)
	var start_along := _snap_along_edge(_zone_down_local, edge, map_size)
	var current_along := _snap_along_edge(local, edge, map_size)

	var a: float = min(start_along, current_along)
	var b: float = max(start_along, current_along)
	var length: float = b - a
	_zone_current_length = length

	match edge:
		"left":
			_drawing_zone.position = Vector2(-ZONE_HIT_THICKNESS_MM / 2.0, a)
			_drawing_zone.size = Vector2(ZONE_HIT_THICKNESS_MM, length)
		"right":
			_drawing_zone.position = Vector2(map_size.x - ZONE_HIT_THICKNESS_MM / 2.0, a)
			_drawing_zone.size = Vector2(ZONE_HIT_THICKNESS_MM, length)
		"top":
			_drawing_zone.position = Vector2(a, -ZONE_HIT_THICKNESS_MM / 2.0)
			_drawing_zone.size = Vector2(length, ZONE_HIT_THICKNESS_MM)
		"bottom":
			_drawing_zone.position = Vector2(a, map_size.y - ZONE_HIT_THICKNESS_MM / 2.0)
			_drawing_zone.size = Vector2(length, ZONE_HIT_THICKNESS_MM)
	_drawing_zone.queue_redraw()


func _finish_zone_drawing() -> void:
	if _zone_current_length < GameConstants.MM_PER_INCH - 1.0:
		_drawing_zone.queue_free()
	_drawing_zone = null


func _create_zone_piece(player: String) -> Control:
	var piece := Control.new()
	piece.set_script(DEPLOYMENT_ZONE_SCRIPT)
	piece.owner_player = player
	piece.line_color = ZONE_COLORS[player]
	piece.visual_thickness = ZONE_VISUAL_THICKNESS_MM
	piece.mouse_filter = Control.MOUSE_FILTER_STOP
	_zone_layer.add_child(piece)
	return piece


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
			if _menu_target is TextureRect:
				var terrain_piece := _menu_target as TextureRect
				terrain_piece.rotate_step()
				_clamp_piece_to_bounds(terrain_piece)
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
