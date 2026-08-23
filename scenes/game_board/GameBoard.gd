extends Control

## 게임 화면 - 베이스 생성 / 베이스 제어(충돌·경계) / 다이얼 메뉴
## (데미지 기록·모델 제거·모델 복제) 기능.
## "유닛 이동"(리딩 모델 + 코헤런시 재배치), 유닛 배치, 스코어보드는
## 이후 단계에서 추가된다.
##
## 지금은 미션 생성 화면과의 핸드오프(지형/배치구역/미션목표 전달)가 없어서
## 독립적인 기본 지도 크기로 동작한다.

const RADIAL_MENU_SCENE := preload("res://scenes/common/RadialMenu.tscn")
const BASE_CREATION_DIALOG_SCENE := preload("res://scenes/game_board/BaseCreationDialog.tscn")
const DAMAGE_INPUT_DIALOG_SCENE := preload("res://scenes/game_board/DamageInputDialog.tscn")
const BASE_SCRIPT := preload("res://scenes/game_board/Base.gd")
const GUIDELINE_SCRIPT := preload("res://scenes/game_board/UnitMoveGuideline.gd")

const MARGIN := 8.0
const COLLISION_ITERATIONS := 8
const DUPLICATE_GAP_MM := 4.0
const FOLLOWER_SNAP_THRESHOLD_MM := 6.0
const FOLLOWER_RING_FRACTION := 0.7
const COHERENCY_EPSILON_MM := 0.5 # 경계에 스냅됐을 때 부동소수점 오차로 오탐지되는 것 방지
const COHERENCY_WARNING_TEXT := "코헤런시를 이탈한 모델은 즉시 사상자로서 제거됩니다."

const TEAM_COLORS := {
	"A": Color(1.0, 0.15, 0.15, 0.85),
	"B": Color(0.15, 0.35, 1.0, 0.85),
	"neutral": Color(0.6, 0.6, 0.6, 0.85),
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


func _ready() -> void:
	_map_size = GameConstants.MAP_SIZE_PRESETS[GameConstants.DEFAULT_MAP_SIZE_PRESET]
	_build_map_area()
	_build_radial_menu()
	_build_creation_dialog()
	_build_damage_dialog()
	_build_unit_move_panel()
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


func _layout() -> void:
	if _map_area == null:
		return

	var pos := Vector2(MARGIN, MARGIN)
	var avail := Vector2(max(size.x - MARGIN * 2.0, 10.0), max(size.y - MARGIN * 2.0, 10.0))

	var scale_factor: float = min(avail.x / _map_size.x, avail.y / _map_size.y)
	scale_factor = min(scale_factor, 1.0)

	_map_area.scale = Vector2(scale_factor, scale_factor)
	var scaled := _map_size * scale_factor
	_map_area.position = pos + (avail - scaled) / 2.0

	if _unit_move_panel != null:
		_unit_move_panel.position = Vector2(size.x - _unit_move_panel.size.x - MARGIN, MARGIN)


func _on_map_background_gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
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
		if _unit_move_active and _unit_move_phase == "leading":
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
	## 퍼진 위치를 잡기 쉽게 돕는다. 경계를 넘어가는 것 자체는 막지 않는다
	## (완료 시 이탈한 모델은 사상자로 제거).
	var offset := desired_center - leading_center
	var dist := offset.length()
	if dist > 0.01 and absf(dist - max_center_distance) <= FOLLOWER_SNAP_THRESHOLD_MM:
		desired_center = leading_center + offset.normalized() * max_center_distance
	return _resolve_position(piece, desired_center)


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
	_auto_place_followers()
	_update_unit_move_guideline()
	_update_unit_move_warning()
	_unit_move_panel.visible = true


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

	_unit_move_panel.visible = false
	_unit_move_warning_label.visible = false

	_guideline_layer.radius_mm = 0.0
	_guideline_layer.queue_redraw()
