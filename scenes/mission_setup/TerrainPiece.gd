extends TextureRect

## 지도 위에 배치된 지형 한 조각. 실제 드래그 추적/경계 클램핑은
## 부모(MissionSetup)가 전역 입력으로 처리하고, 이 노드는 시작 신호만 보낸다.

signal drag_requested(piece: TextureRect)
signal menu_requested(piece: TextureRect, screen_pos: Vector2)

var module_id: String = ""
var size_value: int = 0

const ROTATE_STEP_DEG := 22.5


func setup(module: TerrainModuleDef) -> void:
	module_id = module.id
	size_value = module.size_value
	texture = module.texture
	stretch_mode = TextureRect.STRETCH_KEEP
	size = texture.get_size()
	pivot_offset = size / 2.0
	mouse_filter = Control.MOUSE_FILTER_STOP


func center() -> Vector2:
	return position + size / 2.0


func set_center(new_center: Vector2) -> void:
	position = new_center - size / 2.0


func rotate_step(direction: int = 1) -> void:
	rotation_degrees = fmod(rotation_degrees + ROTATE_STEP_DEG * direction + 360.0, 360.0)


func rotated_half_extent() -> Vector2:
	var half := size / 2.0
	var corners := [
		Vector2(-half.x, -half.y), Vector2(half.x, -half.y),
		Vector2(half.x, half.y), Vector2(-half.x, half.y),
	]
	var max_x := 0.0
	var max_y := 0.0
	for corner in corners:
		var rotated: Vector2 = corner.rotated(rotation)
		max_x = max(max_x, abs(rotated.x))
		max_y = max(max_y, abs(rotated.y))
	return Vector2(max_x, max_y)


func _gui_input(event: InputEvent) -> void:
	## 휠 회전은 여기서 처리하지 않는다 — 예전엔 여기서 wheel을 accept_event()
	## 했었는데, 그러면 같은 wheel 이벤트가 MissionSetup._input()까지 오지
	## 않아 화면 줌이 막혔다(줌 로직은 _input()에 있는데, GUI 쪽에서 먼저
	## 소비해버리면 순서상 절대 도달할 수 없었다). 그래서 휠 회전과 줌은
	## MissionSetup._input()에서 마우스 아래에 있는 조각을 직접 찾아 함께
	## 처리한다 (GameBoard의 모델 회전 처리 방식과 동일).
	if event is InputEventMouseButton and event.pressed:
		match event.button_index:
			MOUSE_BUTTON_LEFT:
				drag_requested.emit(self)
				accept_event()
			MOUSE_BUTTON_RIGHT:
				menu_requested.emit(self, event.global_position)
				accept_event()
