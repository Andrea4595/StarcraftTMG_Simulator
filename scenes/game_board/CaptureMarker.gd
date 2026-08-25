extends TextureRect

## 점령 마커 하나. flag.png를 높이 1"(가로세로 비율은 유지)로 배치한다.
## 우클릭할 때마다 흰색 → 빨간색 → 파란색 순으로 계속 순환한다(삭제 없이
## 영원히 반복) — 삭제는 별도로 shift+우클릭. 실제 드래그 추적/색 순환/삭제는
## 부모(GameBoard)가 처리한다(되돌리기 대상이므로) — 이 노드는 좌클릭/우클릭
## 신호만 보낸다.

signal drag_requested(piece: Control)
signal right_clicked(piece: Control, shift_held: bool)

const TEXTURE := preload("res://Tokens/flag.png")
const TINT_SHADER := preload("res://scenes/game_board/SilhouetteTint.gdshader")
const MARKER_HEIGHT_MM := 25.4 # 1"

const COLOR_SEQUENCE := ["white", "red", "blue"]
const COLOR_VALUES := {
	"white": Color(1.0, 1.0, 1.0, 1.0),
	"red": Color(0.9, 0.15, 0.15, 1.0),
	"blue": Color(0.15, 0.4, 0.9, 1.0),
}

var color_state: String = "white"


func _ready() -> void:
	texture = TEXTURE
	expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	var aspect: float = float(TEXTURE.get_width()) / float(TEXTURE.get_height())
	size = Vector2(MARKER_HEIGHT_MM * aspect, MARKER_HEIGHT_MM)
	custom_minimum_size = size
	pivot_offset = size / 2.0
	mouse_filter = Control.MOUSE_FILTER_STOP

	var mat := ShaderMaterial.new()
	mat.shader = TINT_SHADER
	material = mat
	_refresh_color()


func center() -> Vector2:
	return position + size / 2.0


func set_center(new_center: Vector2) -> void:
	position = new_center - size / 2.0


func set_color_state(new_state: String) -> void:
	color_state = new_state
	_refresh_color()


func _refresh_color() -> void:
	material.set_shader_parameter("tint_color", COLOR_VALUES[color_state])


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT:
			drag_requested.emit(self)
			accept_event()
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			right_clicked.emit(self, event.shift_pressed)
			accept_event()
