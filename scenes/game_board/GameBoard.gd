extends Control

## 게임 화면 - 베이스 생성 / 베이스 제어(충돌·경계) / 다이얼 메뉴
## (데미지 기록·모델 제거·모델 복제·유닛 이동) / 유닛 배치 기능.
## 변위 베이스 예외, 스코어보드는 이후 단계에서 추가된다.
##
## MissionData(오토로드)에 핸드오프 데이터가 있으면(미션 생성 화면에서
## "게임 시작"을 눌러서 넘어온 경우) 그 지도 크기/지형/배치구역/미션목표를
## 그대로 재현하고, 유닛 배치도 그 유닛의 팀 배치구역 구간을 기준으로
## 삼는다. 핸드오프 데이터가 없으면(게임 화면으로 바로 들어온 경우) 기본
## 지도 크기로 동작하고, 배치는 지도 전체 가장자리를 기준으로 폴백한다.
## 로스터 앱이 없어서 "미배치 유닛"은 임시 "유닛 추가" 폼으로 직접 등록한다.

const RADIAL_MENU_SCENE := preload("res://scenes/common/RadialMenu.tscn")
const BASE_CREATION_DIALOG_SCENE := preload("res://scenes/game_board/BaseCreationDialog.tscn")
const DAMAGE_INPUT_DIALOG_SCENE := preload("res://scenes/game_board/DamageInputDialog.tscn")
const UNIT_ADD_DIALOG_SCENE := preload("res://scenes/game_board/UnitAddDialog.tscn")
const BASE_SCRIPT := preload("res://scenes/game_board/Base.gd")
const GUIDELINE_SCRIPT := preload("res://scenes/game_board/UnitMoveGuideline.gd")
const TERRAIN_PIECE_SCENE := preload("res://scenes/mission_setup/TerrainPiece.tscn")
const DEPLOYMENT_ZONE_SCRIPT := preload("res://scenes/mission_setup/DeploymentZonePiece.gd")
const OBJECTIVE_PIECE_SCRIPT := preload("res://scenes/mission_setup/MissionObjectivePiece.gd")

const MARGIN := 8.0
const PALETTE_WIDTH := 180.0
const COLLISION_ITERATIONS := 8
const DUPLICATE_GAP_MM := 4.0
const FOLLOWER_SNAP_THRESHOLD_MM := 6.0
const FOLLOWER_OUTWARD_SNAP_THRESHOLD_MM := 40.0 # 경계 밖으로는 훨씬 강하게 붙잡아둔다
const FOLLOWER_RING_FRACTION := 0.7
const COHERENCY_EPSILON_MM := 0.5 # 경계에 스냅됐을 때 부동소수점 오차로 오탐지되는 것 방지
const COHERENCY_WARNING_TEXT := "코헤런시를 이탈한 모델은 즉시 사상자로서 제거됩니다."

const TEAM_COLORS := {
	"A": Color(1.0, 0.15, 0.15, 0.85),
	"B": Color(0.15, 0.35, 1.0, 0.85),
	"neutral": Color(0.6, 0.6, 0.6, 0.85),
}

const OBJECTIVE_TOKEN_COLORS := {
	1: Color(0.85, 0.15, 0.15),
	2: Color(0.15, 0.4, 0.85),
	3: Color(0.85, 0.15, 0.15),
	4: Color(0.15, 0.4, 0.85),
	5: Color(0.2, 0.7, 0.25),
}

var _map_size: Vector2

var _map_area: Control
var _map_background: ColorRect
var _base_layer: Control

var _radial_menu: Control
var _creation_dialog: Control
var _damage_dialog: Control
var _pending_base_point: Vector2 = Vector2.ZERO
var _menu_target_base: Control = null

var _dragging_base: Control = null
var _drag_offset: Vector2 = Vector2.ZERO

var _guideline_layer: Control

var _unit_move_active: bool = false
var _unit_move_leading: Control = null
var _unit_move_unit: Unit = null
var _unit_move_phase: String = "" # "leading" / "followers"
var _unit_move_start_point: Vector2 = Vector2.ZERO
var _unit_move_original_positions: Dictionary = {} # piece -> Vector2

var _unit_move_panel: Control
var _unit_move_warning_label: Label

var _dragging_follower: Control = null

var _unit_move_is_deployment: bool = false
var _deployment_def_snapshot: Dictionary = {}
var _pending_follower_count: int = 0

var _pending_units: Array = [] # Array[Dictionary] (아직 배치되지 않은 유닛 정의)
var _pending_deployment_def: Dictionary = {} # 지금 배치 클릭을 기다리는 정의 (비었으면 없음)
var _pending_list_box: VBoxContainer
var _unit_add_dialog: Control


func _ready() -> void:
	var preset := MissionData.map_preset if MissionData.has_data else GameConstants.DEFAULT_MAP_SIZE_PRESET
	_map_size = GameConstants.MAP_SIZE_PRESETS[preset]
	_build_map_area()
	_build_radial_menu()
	_build_creation_dialog()
	_build_damage_dialog()
	_build_unit_move_panel()
	_build_pending_panel()
	_build_unit_add_dialog()
	_layout()
	resized.connect(_layout)


func _build_map_area() -> void:
	_map_area = Control.new()
	_map_area.name = "MapArea"
	_map_area.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_map_area)

	_map_background = ColorRect.new()
	_map_background.color = Color(0.15, 0.18, 0.15)
	_map_background.size = _map_size
	_map_background.mouse_filter = Control.MOUSE_FILTER_STOP
	_map_background.gui_input.connect(_on_map_background_gui_input)
	_map_area.add_child(_map_background)

	_build_mission_data_visuals()

	_base_layer = Control.new()
	_base_layer.name = "BaseLayer"
	_base_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_base_layer)

	_guideline_layer = Control.new()
	_guideline_layer.name = "GuidelineLayer"
	_guideline_layer.set_script(GUIDELINE_SCRIPT)
	_guideline_layer.size = _map_size
	_guideline_layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_map_area.add_child(_guideline_layer)


func _build_mission_data_visuals() -> void:
	## 미션 생성 화면에서 넘어온 지형/배치구역/미션목표를 그대로 재현한다.
	## 여기서는 순수 시각 참고용이다: 드래그/회전/삭제 등 조작은 지원하지
	## 않는다 (그 편집은 미션 생성 화면의 몫).
	if not MissionData.has_data:
		return

	for zone in MissionData.deployment_zones:
		var piece := Control.new()
		piece.set_script(DEPLOYMENT_ZONE_SCRIPT)
		piece.owner_player = zone["player"]
		piece.edge = zone["edge"]
		piece.start_along = zone["start_along"]
		piece.end_along = zone["end_along"]
		piece.line_color = TEAM_COLORS.get(zone["player"], TEAM_COLORS["neutral"])
		piece.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_map_area.add_child(piece)

		var thickness := 6.0
		match zone["edge"]:
			"left":
				piece.position = Vector2(-thickness / 2.0, zone["start_along"])
				piece.size = Vector2(thickness, zone["end_along"] - zone["start_along"])
			"right":
				piece.position = Vector2(_map_size.x - thickness / 2.0, zone["start_along"])
				piece.size = Vector2(thickness, zone["end_along"] - zone["start_along"])
			"top":
				piece.position = Vector2(zone["start_along"], -thickness / 2.0)
				piece.size = Vector2(zone["end_along"] - zone["start_along"], thickness)
			"bottom":
				piece.position = Vector2(zone["start_along"], _map_size.y - thickness / 2.0)
				piece.size = Vector2(zone["end_along"] - zone["start_along"], thickness)

	for terrain in MissionData.terrain_pieces:
		var module := TerrainCatalog.get_module(terrain["module_id"])
		if module == null:
			continue
		var piece: TextureRect = TERRAIN_PIECE_SCENE.instantiate()
		_map_area.add_child(piece)
		piece.setup(module)
		piece.rotation_degrees = terrain["rotation_deg"]
		piece.set_center(terrain["position"])
		piece.mouse_filter = Control.MOUSE_FILTER_IGNORE

	for objective in MissionData.mission_objectives:
		var diameter := 32.0 + 2.0 * 3.0 * GameConstants.MM_PER_INCH
		var piece := Control.new()
		piece.set_script(OBJECTIVE_PIECE_SCRIPT)
		piece.number = objective["number"]
		piece.token_color = OBJECTIVE_TOKEN_COLORS.get(objective["number"], Color(0.85, 0.85, 0.8))
		piece.size = Vector2(diameter, diameter)
		piece.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_map_area.add_child(piece)
		piece.set_center(objective["position"])


func _build_radial_menu() -> void:
	_radial_menu = RADIAL_MENU_SCENE.instantiate()
	add_child(_radial_menu)
	_radial_menu.action_chosen.connect(_on_menu_action_chosen)


func _build_creation_dialog() -> void:
	_creation_dialog = BASE_CREATION_DIALOG_SCENE.instantiate()
	add_child(_creation_dialog)
	_creation_dialog.confirmed.connect(_on_base_creation_confirmed)


func _build_damage_dialog() -> void:
	_damage_dialog = DAMAGE_INPUT_DIALOG_SCENE.instantiate()
	add_child(_damage_dialog)
	_damage_dialog.confirmed.connect(_on_damage_confirmed)


func _build_unit_move_panel() -> void:
	_unit_move_panel = PanelContainer.new()
	_unit_move_panel.visible = false
	add_child(_unit_move_panel)

	var box := VBoxContainer.new()
	box.custom_minimum_size = Vector2(220.0, 0.0)
	_unit_move_panel.add_child(box)

	_unit_move_warning_label = Label.new()
	_unit_move_warning_label.text = COHERENCY_WARNING_TEXT
	_unit_move_warning_label.autowrap_mode = TextServer.AUTOWRAP_WORD
	_unit_move_warning_label.add_theme_color_override("font_color", Color(1.0, 0.3, 0.3))
	_unit_move_warning_label.visible = false
	box.add_child(_unit_move_warning_label)

	var confirm_btn := Button.new()
	confirm_btn.text = "완료"
	confirm_btn.pressed.connect(_on_unit_move_complete_pressed)
	box.add_child(confirm_btn)


func _build_pending_panel() -> void:
	var panel := PanelContainer.new()
	panel.position = Vector2(MARGIN, MARGIN)
	add_child(panel)

	var box := VBoxContainer.new()
	box.custom_minimum_size = Vector2(PALETTE_WIDTH - 16.0, 0.0)
	panel.add_child(box)

	var title := Label.new()
	title.text = "미배치 유닛"
	box.add_child(title)

	var add_btn := Button.new()
	add_btn.text = "+ 유닛 추가"
	add_btn.pressed.connect(func(): _unit_add_dialog.open())
	box.add_child(add_btn)

	box.add_child(HSeparator.new())

	_pending_list_box = VBoxContainer.new()
	box.add_child(_pending_list_box)


func _build_unit_add_dialog() -> void:
	_unit_add_dialog = UNIT_ADD_DIALOG_SCENE.instantiate()
	add_child(_unit_add_dialog)
	_unit_add_dialog.confirmed.connect(_on_unit_add_confirmed)


func _layout() -> void:
	if _map_area == null:
		return

	var left := MARGIN + PALETTE_WIDTH + MARGIN
	var pos := Vector2(left, MARGIN)
	var avail := Vector2(max(size.x - left - MARGIN, 10.0), max(size.y - MARGIN * 2.0, 10.0))

	var scale_factor: float = min(avail.x / _map_size.x, avail.y / _map_size.y)
	scale_factor = min(scale_factor, 1.0)

	_map_area.scale = Vector2(scale_factor, scale_factor)
	var scaled := _map_size * scale_factor
	_map_area.position = pos + (avail - scaled) / 2.0

	if _unit_move_panel != null:
		_unit_move_panel.position = Vector2(size.x - _unit_move_panel.size.x - MARGIN, MARGIN)


func _on_map_background_gui_input(event: InputEvent) -> void:
	if not (event is InputEventMouseButton and event.pressed):
		return

	if event.button_index == MOUSE_BUTTON_LEFT:
		if not _pending_deployment_def.is_empty():
			_begin_deployment_drag(_map_area.get_local_mouse_position())
			accept_event()
		return

	if event.button_index == MOUSE_BUTTON_RIGHT:
		if not _pending_deployment_def.is_empty():
			_pending_units.append(_pending_deployment_def)
			_pending_deployment_def = {}
			_clear_deployment_band()
			_refresh_pending_list()
			accept_event()
			return

		_pending_base_point = _map_area.get_local_mouse_position()
		_radial_menu.open([
			{"label": "베이스 생성", "action": "create_base"},
		], event.global_position)
		accept_event()


func _on_menu_action_chosen(action: String) -> void:
	match action:
		"create_base":
			_creation_dialog.open()
		"damage":
			_damage_dialog.open(_menu_target_base.damage)
		"remove":
			_remove_base(_menu_target_base)
		"duplicate":
			_duplicate_base(_menu_target_base)
		"start_unit_move":
			_start_unit_move(_menu_target_base)


func _on_base_creation_confirmed(data: Dictionary) -> void:
	## 임시 "베이스 생성" 다이얼로 만드는 베이스는 항상 1모델짜리 새 유닛으로 취급한다.
	## 이렇게 해야 이후 복제/유닛 이동이 이 베이스에도 동일하게 동작한다.
	var unit := Unit.new()
	unit.unit_name = data["name"]
	unit.team = data["team"]
	unit.coherency_inch = GameConstants.DEFAULT_COHERENCY_INCH

	var piece := Control.new()
	piece.set_script(BASE_SCRIPT)
	piece.unit = unit
	piece.size_mm = Vector2(data["width_mm"], data["height_mm"])
	piece.fill_color = TEAM_COLORS.get(data["team"], TEAM_COLORS["neutral"])
	piece.size = piece.size_mm
	piece.mouse_filter = Control.MOUSE_FILTER_STOP
	_base_layer.add_child(piece)
	piece.set_center(_pending_base_point)
	piece.set_center(_resolve_position(piece, piece.center()))
	_connect_base_signals(piece)

	unit.models.append(piece)


func _connect_base_signals(piece: Control) -> void:
	piece.drag_requested.connect(_on_base_drag_requested)
	piece.menu_requested.connect(_on_base_menu_requested)


func _on_base_drag_requested(piece: Control) -> void:
	if _unit_move_active:
		if _unit_move_phase == "leading" and piece == _unit_move_leading:
			_dragging_base = piece
			_drag_offset = piece.center() - _map_area.get_local_mouse_position()
		elif _unit_move_phase == "followers" and piece != _unit_move_leading and piece.unit == _unit_move_unit:
			_dragging_follower = piece
			_drag_offset = piece.center() - _map_area.get_local_mouse_position()
		return

	_dragging_base = piece
	_drag_offset = piece.center() - _map_area.get_local_mouse_position()
	_base_layer.move_child(piece, _base_layer.get_child_count() - 1)


func _on_base_menu_requested(piece: Control, screen_pos: Vector2) -> void:
	_menu_target_base = piece
	_radial_menu.open([
		{"label": "데미지 기록", "action": "damage"},
		{"label": "모델 제거", "action": "remove"},
		{"label": "모델 복제", "action": "duplicate"},
		{"label": "유닛 이동 시작", "action": "start_unit_move"},
	], screen_pos)


func _on_damage_confirmed(value: int) -> void:
	if _menu_target_base == null:
		return
	_menu_target_base.damage = value
	_menu_target_base.queue_redraw()
	_menu_target_base = null


func _remove_base(piece: Control) -> void:
	if piece == null:
		return
	if piece.unit != null:
		piece.unit.models.erase(piece)
	if _dragging_base == piece:
		_dragging_base = null
	piece.queue_free()
	_menu_target_base = null


func _duplicate_base(piece: Control) -> void:
	if piece == null:
		return

	var new_piece := Control.new()
	new_piece.set_script(BASE_SCRIPT)
	new_piece.unit = piece.unit
	new_piece.size_mm = piece.size_mm
	new_piece.fill_color = piece.fill_color
	new_piece.size = new_piece.size_mm
	new_piece.mouse_filter = Control.MOUSE_FILTER_STOP
	_base_layer.add_child(new_piece)

	var desired: Vector2 = piece.center() + Vector2(piece.radius() + new_piece.radius() + DUPLICATE_GAP_MM, 0.0)
	new_piece.set_center(_resolve_position(new_piece, desired))
	_connect_base_signals(new_piece)

	if piece.unit != null:
		piece.unit.models.append(new_piece)

	_menu_target_base = null


func _input(event: InputEvent) -> void:
	if _unit_move_active and event is InputEventMouseButton and event.pressed \
			and event.button_index == MOUSE_BUTTON_RIGHT:
		_cancel_unit_move()
		get_viewport().set_input_as_handled()
		return

	if _dragging_base:
		_handle_base_drag_input(event)
		return

	if _dragging_follower:
		_handle_follower_drag_input(event)
		return


func _handle_base_drag_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		var local: Vector2 = _map_area.get_local_mouse_position()
		var desired: Vector2 = local + _drag_offset
		var resolved: Vector2
		if _unit_move_active and _unit_move_phase == "leading" and _unit_move_is_deployment:
			resolved = _resolve_deployment_leading_position(desired)
		elif _unit_move_active and _unit_move_phase == "leading":
			resolved = _resolve_leading_position(desired)
		else:
			resolved = _resolve_position(_dragging_base, desired)
		_dragging_base.set_center(resolved)
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
		var finished_leading := _unit_move_active and _unit_move_phase == "leading" \
				and _dragging_base == _unit_move_leading
		_dragging_base = null
		get_viewport().set_input_as_handled()
		if finished_leading:
			_finish_leading_move()


func _handle_follower_drag_input(event: InputEvent) -> void:
	if event is InputEventMouseMotion:
		var local: Vector2 = _map_area.get_local_mouse_position()
		var desired: Vector2 = local + _drag_offset
		var max_center_distance := _max_follower_center_distance(_dragging_follower)
		var resolved := _resolve_follower_position(
				_dragging_follower, desired, _unit_move_leading.center(), max_center_distance)
		_dragging_follower.set_center(resolved)
		_update_unit_move_warning()
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
		_dragging_follower = null
		get_viewport().set_input_as_handled()


func _resolve_position(piece: Control, desired_center: Vector2) -> Vector2:
	## 베이스끼리 절대 겹치지 않도록, 겹치는 다른 베이스로부터 밀어내는 것을
	## 여러 번 반복해서 가장 가까운 비충돌 위치를 근사한다. 이후 지도 경계로 clamp.
	var pos := desired_center
	var radius: float = piece.radius()

	for _iteration in range(COLLISION_ITERATIONS):
		var moved := false
		for other in _base_layer.get_children():
			if other == piece:
				continue
			var min_dist: float = radius + other.radius()
			var offset: Vector2 = pos - other.center()
			var dist: float = offset.length()
			if dist < min_dist:
				moved = true
				if dist < 0.01:
					offset = Vector2(1.0, 0.0)
					dist = 0.01
				pos = other.center() + offset.normalized() * min_dist
		if not moved:
			break

	pos.x = clamp(pos.x, radius, max(radius, _map_size.x - radius))
	pos.y = clamp(pos.y, radius, max(radius, _map_size.y - radius))
	return pos


func _resolve_leading_position(desired_center: Vector2) -> Vector2:
	## 충돌/경계 해소에 더해, 리딩 모델이 시작 지점으로부터 이동력(인치)을
	## 벗어나지 못하도록 한 번 더 잡아당긴다.
	var pos := _resolve_position(_unit_move_leading, desired_center)
	var max_dist := _unit_move_unit.move_inch * GameConstants.MM_PER_INCH
	var offset := pos - _unit_move_start_point
	var dist := offset.length()
	if dist > max_dist and dist > 0.01:
		pos = _unit_move_start_point + offset.normalized() * max_dist
		pos = _resolve_position(_unit_move_leading, pos)
	return pos


func _coherency_boundary_radius_mm() -> float:
	## 리딩 모델 "테두리"로부터 코헤런시 거리만큼 떨어진 절대 경계선의 반지름
	## (리딩 모델 중심 기준). 이 선을 넘으면(=선을 밟기만 해도) 이탈이다.
	return _unit_move_leading.radius() + _unit_move_unit.coherency_inch * GameConstants.MM_PER_INCH


func _max_follower_center_distance(follower: Control) -> float:
	## follower 베이스 전체가 코헤런시 경계 안에 완전히 들어오기 위한
	## 중심 간 최대 허용 거리. (경계 반지름에서 follower 자신의 반지름만큼 뺀다.)
	return _coherency_boundary_radius_mm() - follower.radius()


func _resolve_follower_position(piece: Control, desired_center: Vector2, leading_center: Vector2, max_center_distance: float) -> Vector2:
	## 코헤런시 경계 근처로 드래그하면 그 경계선에 스냅되도록 해서, 최대로
	## 퍼진 위치를 잡기 쉽게 돕는다. 안쪽에서 접근할 때보다 바깥으로
	## 넘어가려 할 때 훨씬 넓은 범위에서 붙잡아, 실수로 코헤런시를
	## 벗어나기 어렵게 한다. 그래도 완전히 막지는 않는다 (계속 세게
	## 끌면 벗어날 수 있고, 완료 시 이탈한 모델은 사상자로 제거된다).
	##
	## 충돌 회피(다른 베이스를 피해 밀려남)와 코헤런시 스냅은 서로를
	## 무효화시킬 수 있다 (스냅하면 다른 베이스와 겹치고, 그걸 피해 밀려나면
	## 다시 코헤런시를 벗어난다). 둘을 번갈아 여러 번 적용해서 수렴시키면,
	## 옆 베이스 테두리를 타고 돌면서 코헤런시 경계에 맞는 지점을 찾는
	## 효과를 낸다.
	var pos := desired_center
	for _iteration in range(COLLISION_ITERATIONS):
		var before := pos
		pos = _snap_to_coherency_boundary(pos, leading_center, max_center_distance)
		pos = _resolve_position(piece, pos)
		if pos.distance_to(before) < 0.01:
			break
	return pos


func _snap_to_coherency_boundary(pos: Vector2, leading_center: Vector2, max_center_distance: float) -> Vector2:
	var offset := pos - leading_center
	var dist := offset.length()
	if dist > 0.01:
		if dist >= max_center_distance and dist - max_center_distance <= FOLLOWER_OUTWARD_SNAP_THRESHOLD_MM:
			pos = leading_center + offset.normalized() * max_center_distance
		elif dist < max_center_distance and max_center_distance - dist <= FOLLOWER_SNAP_THRESHOLD_MM:
			pos = leading_center + offset.normalized() * max_center_distance
	return pos


func _start_unit_move(leading: Control) -> void:
	if leading == null or leading.unit == null or _unit_move_active:
		return

	_unit_move_active = true
	_unit_move_leading = leading
	_unit_move_unit = leading.unit
	_unit_move_phase = "leading"
	_unit_move_start_point = leading.center()

	_unit_move_original_positions.clear()
	for model in _unit_move_unit.models:
		_unit_move_original_positions[model] = model.center()

	## 원은 "베이스 테두리로부터 이동거리만큼"을 나타내야 하므로 리딩 모델
	## 반지름만큼 더해서 그린다. (실제 이동 가능 거리 자체는 같은 베이스가
	## 움직이는 것이라 반지름이 상쇄되어 move_inch 그대로 유지된다.)
	_guideline_layer.center_point = _unit_move_start_point
	_guideline_layer.radius_mm = leading.radius() + _unit_move_unit.move_inch * GameConstants.MM_PER_INCH
	_guideline_layer.queue_redraw()

	_menu_target_base = null


func _finish_leading_move() -> void:
	_unit_move_phase = "followers"
	if _unit_move_is_deployment and _pending_follower_count > 0:
		_spawn_deployment_followers()
	_auto_place_followers()
	_update_unit_move_guideline()
	_update_unit_move_warning()
	_unit_move_panel.visible = true


func _spawn_deployment_followers() -> void:
	for _i in range(_pending_follower_count):
		var follower := Control.new()
		follower.set_script(BASE_SCRIPT)
		follower.unit = _unit_move_unit
		follower.size_mm = _unit_move_leading.size_mm
		follower.fill_color = _unit_move_leading.fill_color
		follower.size = follower.size_mm
		follower.mouse_filter = Control.MOUSE_FILTER_STOP
		_base_layer.add_child(follower)
		follower.set_center(_unit_move_leading.center())
		_connect_base_signals(follower)
		_unit_move_unit.models.append(follower)
	_pending_follower_count = 0


func _auto_place_followers() -> void:
	var followers: Array = []
	for model in _unit_move_unit.models:
		if model != _unit_move_leading:
			followers.append(model)

	if followers.is_empty():
		return

	var boundary := _coherency_boundary_radius_mm()
	var leading_center: Vector2 = _unit_move_leading.center()

	for i in range(followers.size()):
		var follower: Control = followers[i]
		var ring_radius: float = (boundary - follower.radius()) * FOLLOWER_RING_FRACTION
		var angle := (TAU / followers.size()) * i - PI / 2.0
		var desired: Vector2 = leading_center + Vector2(cos(angle), sin(angle)) * ring_radius
		follower.set_center(_resolve_position(follower, desired))


func _update_unit_move_guideline() -> void:
	_guideline_layer.band_polylines = []
	_guideline_layer.center_point = _unit_move_leading.center()
	_guideline_layer.radius_mm = _coherency_boundary_radius_mm()
	_guideline_layer.queue_redraw()


func _update_unit_move_warning() -> void:
	var leading_center: Vector2 = _unit_move_leading.center()
	var any_out := false
	for model in _unit_move_unit.models:
		if model == _unit_move_leading:
			continue
		if model.center().distance_to(leading_center) > _max_follower_center_distance(model) + COHERENCY_EPSILON_MM:
			any_out = true
			break
	_unit_move_warning_label.visible = any_out


func _on_unit_move_complete_pressed() -> void:
	if not _unit_move_active or _unit_move_phase != "followers":
		return

	var leading_center: Vector2 = _unit_move_leading.center()
	var casualties: Array = []
	for model in _unit_move_unit.models:
		if model == _unit_move_leading:
			continue
		if model.center().distance_to(leading_center) > _max_follower_center_distance(model) + COHERENCY_EPSILON_MM:
			casualties.append(model)

	for model in casualties:
		_unit_move_unit.models.erase(model)
		model.queue_free()

	_end_unit_move()


func _cancel_unit_move() -> void:
	if _unit_move_is_deployment:
		## 배치 중 취소: 아직 게임에 존재한 적 없는 유닛이므로 되돌릴 위치가 없다.
		## 만든 모델을 전부 지우고, 정의를 미배치 목록에 되돌려놓는다.
		for model in _unit_move_unit.models.duplicate():
			model.queue_free()
		_pending_units.append(_deployment_def_snapshot)
		_refresh_pending_list()
	else:
		for piece in _unit_move_original_positions:
			if is_instance_valid(piece):
				piece.set_center(_unit_move_original_positions[piece])

	_dragging_base = null
	_dragging_follower = null
	_end_unit_move()


func _end_unit_move() -> void:
	_unit_move_active = false
	_unit_move_leading = null
	_unit_move_unit = null
	_unit_move_phase = ""
	_unit_move_original_positions.clear()
	_unit_move_is_deployment = false
	_deployment_def_snapshot = {}
	_pending_follower_count = 0

	_unit_move_panel.visible = false
	_unit_move_warning_label.visible = false

	_guideline_layer.radius_mm = 0.0
	_guideline_layer.band_polylines = []
	_guideline_layer.queue_redraw()


func _on_unit_add_confirmed(data: Dictionary) -> void:
	_pending_units.append(data)
	_refresh_pending_list()


func _refresh_pending_list() -> void:
	for child in _pending_list_box.get_children():
		child.queue_free()

	for i in range(_pending_units.size()):
		var def: Dictionary = _pending_units[i]
		var btn := Button.new()
		btn.text = "%s (%s, %d모델)" % [def["name"], def["team"], def["model_count"]]
		btn.custom_minimum_size = Vector2(PALETTE_WIDTH - 16.0, 40.0)
		btn.pressed.connect(_start_deployment.bind(i))
		_pending_list_box.add_child(btn)


func _start_deployment(index: int) -> void:
	if _unit_move_active or index < 0 or index >= _pending_units.size():
		return
	_pending_deployment_def = _pending_units[index]
	_pending_units.remove_at(index)
	_refresh_pending_list()
	_show_deployment_band(_pending_deployment_def)


func _team_zone_segments(team: String) -> Array:
	var segments: Array = []
	if not MissionData.has_data:
		return segments
	for zone in MissionData.deployment_zones:
		if zone["player"] == team:
			segments.append(zone)
	return segments


func _segment_endpoints_world(zone: Dictionary) -> Array:
	var a: float = zone["start_along"]
	var b: float = zone["end_along"]
	match zone["edge"]:
		"left":
			return [Vector2(0.0, a), Vector2(0.0, b)]
		"right":
			return [Vector2(_map_size.x, a), Vector2(_map_size.x, b)]
		"top":
			return [Vector2(a, 0.0), Vector2(b, 0.0)]
		"bottom":
			return [Vector2(a, _map_size.y), Vector2(b, _map_size.y)]
	return [Vector2.ZERO, Vector2.ZERO]


func _closest_point_on_segment(p: Vector2, a: Vector2, b: Vector2) -> Vector2:
	var ab := b - a
	var len_sq := ab.length_squared()
	if len_sq < 0.0001:
		return a
	var t: float = clamp((p - a).dot(ab) / len_sq, 0.0, 1.0)
	return a + ab * t


func _local_to_world(edge: String, p: Vector2) -> Vector2:
	## p = (along, depth-into-board) in a local frame for this edge.
	match edge:
		"left":
			return Vector2(p.y, p.x)
		"right":
			return Vector2(_map_size.x - p.y, p.x)
		"top":
			return Vector2(p.x, p.y)
		"bottom":
			return Vector2(p.x, _map_size.y - p.y)
	return Vector2.ZERO


func _build_capsule_polygon(edge: String, a: float, b: float, depth: float) -> PackedVector2Array:
	## 구간 [a,b]에서 depth만큼 보드 안쪽으로 뻗은 "약통" 모양(양 끝은
	## 컴퍼스로 그린 것처럼 둥글게) 외곽선. 지도 가장자리 쪽은 닫지 않아도
	## draw_polyline이 마지막 점을 첫 점과 이어주면 자연히 가장자리를 따라
	## 닫힌다.
	var steps := 16
	var points := PackedVector2Array()
	for i in range(steps + 1):
		var t: float = 180.0 - 90.0 * i / float(steps)
		var rad := deg_to_rad(t)
		points.append(_clamp_to_map(_local_to_world(edge, Vector2(a + depth * cos(rad), depth * sin(rad)))))
	for i in range(steps + 1):
		var t2: float = 90.0 - 90.0 * i / float(steps)
		var rad2 := deg_to_rad(t2)
		points.append(_clamp_to_map(_local_to_world(edge, Vector2(b + depth * cos(rad2), depth * sin(rad2)))))
	return points


func _clamp_to_map(p: Vector2) -> Vector2:
	return Vector2(clamp(p.x, 0.0, _map_size.x), clamp(p.y, 0.0, _map_size.y))


func _merge_all_polygons(polygons: Array) -> Array:
	if polygons.is_empty():
		return []
	var result: Array = [polygons[0]]
	for i in range(1, polygons.size()):
		var p: PackedVector2Array = polygons[i]
		var merged_into := false
		for j in range(result.size()):
			var merge_result: Array = Geometry2D.merge_polygons(result[j], p)
			if merge_result.size() == 1:
				result[j] = merge_result[0]
				merged_into = true
				break
		if not merged_into:
			result.append(p)
	return result


func _fallback_edge_band_polyline(radius: float, expand_for_visual: bool) -> PackedVector2Array:
	## 배치구역 데이터가 없을 때 쓰는 폴백: 지도 전체 가장자리 안쪽 테두리.
	var move_mm := GameConstants.DEFAULT_MOVE_INCH * GameConstants.MM_PER_INCH
	var inset: float = radius + move_mm + (radius if expand_for_visual else 0.0)
	var p := Vector2(inset, inset)
	var s := Vector2(max(_map_size.x - inset * 2.0, 0.0), max(_map_size.y - inset * 2.0, 0.0))
	return PackedVector2Array([p, p + Vector2(s.x, 0.0), p + s, p + Vector2(0.0, s.y), p])


func _show_deployment_band(def: Dictionary) -> void:
	var width: float = def["width_mm"]
	var height: float = def["height_mm"]
	var radius: float = max(width, height) / 2.0
	var move_mm := GameConstants.DEFAULT_MOVE_INCH * GameConstants.MM_PER_INCH
	var visual_depth: float = radius * 2.0 + move_mm

	var segments := _team_zone_segments(def["team"])
	var polylines: Array = []

	if segments.is_empty():
		polylines.append(_fallback_edge_band_polyline(radius, true))
	else:
		var polygons: Array = []
		for zone in segments:
			polygons.append(_build_capsule_polygon(zone["edge"], zone["start_along"], zone["end_along"], visual_depth))
		for merged in _merge_all_polygons(polygons):
			var closed := PackedVector2Array(merged)
			if closed.size() > 0:
				closed.append(closed[0])
			polylines.append(closed)

	_guideline_layer.band_polylines = polylines
	_guideline_layer.queue_redraw()


func _clear_deployment_band() -> void:
	_guideline_layer.band_polylines = []
	_guideline_layer.queue_redraw()


func _begin_deployment_drag(click_point: Vector2) -> void:
	var def := _pending_deployment_def
	var width: float = def["width_mm"]
	var height: float = def["height_mm"]

	var unit := Unit.new()
	unit.unit_name = def["name"]
	unit.team = def["team"]
	unit.coherency_inch = GameConstants.DEFAULT_COHERENCY_INCH
	unit.move_inch = GameConstants.DEFAULT_MOVE_INCH

	var leading := Control.new()
	leading.set_script(BASE_SCRIPT)
	leading.unit = unit
	leading.size_mm = Vector2(width, height)
	leading.fill_color = TEAM_COLORS.get(def["team"], TEAM_COLORS["neutral"])
	leading.size = leading.size_mm
	leading.mouse_filter = Control.MOUSE_FILTER_STOP
	_base_layer.add_child(leading)
	unit.models.append(leading)

	## 나머지 모델은 아직 만들지 않는다 (리딩 모델이 실제로 놓이기 전까지는
	## 클릭 지점에 겹쳐서 충돌 해소를 방해하게 된다). _finish_leading_move()에서
	## 리딩 모델 배치가 확정된 뒤에 만든다.
	_pending_follower_count = max(int(def["model_count"]) - 1, 0)

	_pending_deployment_def = {}
	_deployment_def_snapshot = def

	_unit_move_active = true
	_unit_move_is_deployment = true
	_unit_move_leading = leading
	_unit_move_unit = unit
	_unit_move_phase = "leading"
	_unit_move_original_positions.clear()

	leading.set_center(_resolve_deployment_leading_position(click_point))
	_connect_base_signals(leading)

	_dragging_base = leading
	_drag_offset = leading.center() - _map_area.get_local_mouse_position()
	_base_layer.move_child(leading, _base_layer.get_child_count() - 1)


func _resolve_deployment_leading_position(desired_center: Vector2) -> Vector2:
	## 배치 중인 리딩 모델은 지도 경계를 벗어날 수 없고, 그 팀의 배치구역
	## 구간(들) 중 하나로부터 이동거리(인치) 안쪽에 완전히 들어와 있어야
	## 한다 (구간의 양 끝에서는 컴퍼스로 그린 것처럼 옆으로도 퍼질 수 있다).
	## 배치구역 데이터가 없으면 지도 전체 가장자리로 폴백한다.
	var pos := _resolve_position(_unit_move_leading, desired_center)
	var radius: float = _unit_move_leading.radius()

	var segments := _team_zone_segments(_unit_move_unit.team)
	if segments.is_empty():
		pos = _clamp_to_nearest_map_edge(pos, radius)
	else:
		var move_mm := _unit_move_unit.move_inch * GameConstants.MM_PER_INCH
		var allowed: float = radius + move_mm

		var best_point: Vector2 = pos
		var best_dist: float = INF
		for zone in segments:
			var ends := _segment_endpoints_world(zone)
			var cp: Vector2 = _closest_point_on_segment(pos, ends[0], ends[1])
			var dist := cp.distance_to(pos)
			if dist < best_dist:
				best_dist = dist
				best_point = cp

		if best_dist > allowed:
			var dir := pos - best_point
			if dir.length() < 0.01:
				dir = Vector2(1.0, 0.0)
			pos = best_point + dir.normalized() * allowed
			pos = _resolve_position(_unit_move_leading, pos)

	return pos


func _clamp_to_nearest_map_edge(pos: Vector2, radius: float) -> Vector2:
	var move_mm := _unit_move_unit.move_inch * GameConstants.MM_PER_INCH
	var d_left := pos.x - radius
	var d_right := _map_size.x - pos.x - radius
	var d_top := pos.y - radius
	var d_bottom := _map_size.y - pos.y - radius
	var edge_dist: float = min(min(d_left, d_right), min(d_top, d_bottom))

	if edge_dist > move_mm:
		if d_left <= d_right and d_left <= d_top and d_left <= d_bottom:
			pos.x = radius + move_mm
		elif d_right <= d_top and d_right <= d_bottom:
			pos.x = _map_size.x - radius - move_mm
		elif d_top <= d_bottom:
			pos.y = radius + move_mm
		else:
			pos.y = _map_size.y - radius - move_mm
		pos = _resolve_position(_unit_move_leading, pos)

	return pos
