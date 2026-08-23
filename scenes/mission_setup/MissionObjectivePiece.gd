extends Control

## 미션 목표 마커 하나 (1~5번). 32mm 토큰 + 그 바깥으로 3" 점령 범위 링을 그린다.
## 이동은 지원하지만 회전은 의미가 없다 (원형). 우클릭하면 바로 삭제된다.

signal drag_requested(piece: Control)
signal delete_requested(piece: Control)

var number: int = 1
var token_color: Color = Color(0.85, 0.85, 0.8)

const TOKEN_DIAMETER_MM := 32.0
const CAPTURE_MARGIN_INCH := 3.0


func center() -> Vector2:
	return position + size / 2.0


func set_center(new_center: Vector2) -> void:
	position = new_center - size / 2.0


func _draw() -> void:
	var c := size / 2.0
	var token_radius := TOKEN_DIAMETER_MM / 2.0
	var capture_radius := size.x / 2.0

	draw_circle(c, capture_radius, Color(1.0, 1.0, 1.0, 0.10))
	draw_arc(c, capture_radius, 0.0, TAU, 48, Color(1.0, 1.0, 1.0, 0.6), 1.5, true)

	draw_circle(c, token_radius, token_color)
	draw_arc(c, token_radius, 0.0, TAU, 32, Color(0.1, 0.1, 0.1, 0.8), 1.5, true)

	var font := ThemeDB.fallback_font
	var font_size := 18
	var text := str(number)
	var text_width := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
	var ascent := font.get_ascent(font_size)
	var descent := font.get_descent(font_size)
	var baseline := Vector2(c.x - text_width / 2.0, c.y + (ascent - descent) / 2.0)
	draw_string(font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color.WHITE)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT:
			drag_requested.emit(self)
			accept_event()
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			delete_requested.emit(self)
			accept_event()
