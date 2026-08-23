extends Control

## 게임 화면 - 베이스 생성 / 베이스 제어(충돌·경계) 기능.
## 다이얼 메뉴의 나머지 항목(데미지 기록/제거/복제/유닛 이동), 유닛 배치,
## 스코어보드는 이후 단계에서 추가된다.
##
## 지금은 미션 생성 화면과의 핸드오프(지형/배치구역/미션목표 전달)가 없어서
## 독립적인 기본 지도 크기로 동작한다.

const RADIAL_MENU_SCENE := preload("res://scenes/common/RadialMenu.tscn")
const BASE_CREATION_DIALOG_SCENE := preload("res://scenes/game_board/BaseCreationDialog.tscn")
const BASE_SCRIPT := preload("res://scenes/game_board/Base.gd")

const MARGIN := 8.0
const COLLISION_ITERATIONS := 8

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
var _pending_base_point: Vector2 = Vector2.ZERO

var _dragging_base: Control = null
var _drag_offset: Vector2 = Vector2.ZERO


func _ready() -> void:
	_map_size = GameConstants.MAP_SIZE_PRESETS[GameConstants.DEFAULT_MAP_SIZE_PRESET]
	_build_map_area()
	_build_radial_menu()
	_build_creation_dialog()
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


func _build_radial_menu() -> void:
	_radial_menu = RADIAL_MENU_SCENE.instantiate()
	add_child(_radial_menu)
	_radial_menu.action_chosen.connect(_on_menu_action_chosen)


func _build_creation_dialog() -> void:
	_creation_dialog = BASE_CREATION_DIALOG_SCENE.instantiate()
	add_child(_creation_dialog)
	_creation_dialog.confirmed.connect(_on_base_creation_confirmed)


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


func _on_base_creation_confirmed(data: Dictionary) -> void:
	var piece := Control.new()
	piece.set_script(BASE_SCRIPT)
	piece.base_name = data["name"]
	piece.size_mm = Vector2(data["width_mm"], data["height_mm"])
	piece.team = data["team"]
	piece.fill_color = TEAM_COLORS.get(data["team"], TEAM_COLORS["neutral"])
	piece.size = piece.size_mm
	piece.mouse_filter = Control.MOUSE_FILTER_STOP
	_base_layer.add_child(piece)
	piece.set_center(_pending_base_point)
	piece.set_center(_resolve_position(piece, piece.center()))
	piece.drag_requested.connect(_on_base_drag_requested)


func _on_base_drag_requested(piece: Control) -> void:
	_dragging_base = piece
	_drag_offset = piece.center() - _map_area.get_local_mouse_position()
	_base_layer.move_child(piece, _base_layer.get_child_count() - 1)


func _input(event: InputEvent) -> void:
	if not _dragging_base:
		return

	if event is InputEventMouseMotion:
		var local: Vector2 = _map_area.get_local_mouse_position()
		var resolved := _resolve_position(_dragging_base, local + _drag_offset)
		_dragging_base.set_center(resolved)
		get_viewport().set_input_as_handled()
	elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and not event.pressed:
		_dragging_base = null
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
