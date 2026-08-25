extends TextureRect

## 아이콘 하나로만 표시되는 간단한 1인치 마커(이동/돌격/전투/버프/디버프 등).
## 상태 순환 없음 — 좌클릭 드래그, 우클릭 한 번으로 즉시 삭제. GameBoard가
## 생성할 때 texture와 kind(어떤 종류인지, 되돌리기 스냅샷에 씀)를 지정한다.

signal drag_requested(piece: Control)
signal right_clicked(piece: Control)

const MARKER_SIZE_MM := 25.4 # 1"

var kind: String = ""


func _ready() -> void:
	expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	custom_minimum_size = Vector2(MARKER_SIZE_MM, MARKER_SIZE_MM)
	size = Vector2(MARKER_SIZE_MM, MARKER_SIZE_MM)
	pivot_offset = size / 2.0
	mouse_filter = Control.MOUSE_FILTER_STOP


func center() -> Vector2:
	return position + size / 2.0


func set_center(new_center: Vector2) -> void:
	position = new_center - size / 2.0


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT:
			drag_requested.emit(self)
			accept_event()
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			right_clicked.emit(self)
			accept_event()
