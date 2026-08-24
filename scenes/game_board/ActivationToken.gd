extends TextureRect

## 활성화 토큰 하나. 처음 배치되면 "이동" 면으로 시작하고, 우클릭할 때마다
## 이동 → 돌격 → 제거 순으로 순환한다. 실제 드래그 추적과 우클릭에 따른
## 상태 변경/제거는 부모(GameBoard)가 처리한다(되돌리기 대상이라 GameBoard가
## 알아야 하므로) — 이 노드는 좌클릭/우클릭 신호만 보낸다.

signal drag_requested(piece: Control)
signal right_clicked(piece: Control)

const TEXTURE_MOVEMENT := preload("res://Tokens/activated-movement.png")
const TEXTURE_ASSAULT := preload("res://Tokens/activated-assault.png")
const TOKEN_SIZE_MM := 25.4 # 1"

var state: String = "movement" # "movement" / "assault"


func _ready() -> void:
	## expand_mode를 IGNORE_SIZE로 두지 않으면 텍스처의 원본 픽셀 크기가
	## 최소 크기로 강제되어 size를 아무리 작게 줘도 원본 그대로 커 보인다.
	expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	custom_minimum_size = Vector2(TOKEN_SIZE_MM, TOKEN_SIZE_MM)
	size = Vector2(TOKEN_SIZE_MM, TOKEN_SIZE_MM)
	pivot_offset = size / 2.0
	mouse_filter = Control.MOUSE_FILTER_STOP
	_refresh_texture()


func center() -> Vector2:
	return position + size / 2.0


func set_center(new_center: Vector2) -> void:
	position = new_center - size / 2.0


func _refresh_texture() -> void:
	texture = TEXTURE_MOVEMENT if state == "movement" else TEXTURE_ASSAULT


func set_state(new_state: String) -> void:
	state = new_state
	_refresh_texture()


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT:
			drag_requested.emit(self)
			accept_event()
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			right_clicked.emit(self)
			accept_event()
