extends Control

## 게임 화면의 베이스(모델) 하나. 원형 또는 타원형이며, 이름/팀 색을 갖는다.
## 실제 드래그 추적/충돌 해소는 부모(GameBoard)가 전역 입력으로 처리하고,
## 이 노드는 좌클릭 시작 신호만 보낸다.

signal drag_requested(piece: Control)

var unit: Unit = null
var size_mm: Vector2 = Vector2(32.0, 32.0) # 가로, 세로 (지름)
var fill_color: Color = Color(0.6, 0.6, 0.6, 0.85)

const OUTLINE_COLOR := Color(0.05, 0.05, 0.05, 0.9)
const ELLIPSE_RESOLUTION := 48


func radius() -> float:
	## 충돌 판정에 쓰이는 근사 반지름. 타원의 경우 긴 쪽 반지름을 써서
	## (원형으로 근사) 절대 시각적으로 겹치지 않도록 보수적으로 잡는다.
	return max(size_mm.x, size_mm.y) / 2.0


func center() -> Vector2:
	return position + size / 2.0


func set_center(new_center: Vector2) -> void:
	position = new_center - size / 2.0


func _draw() -> void:
	var c := size / 2.0
	var rx := size_mm.x / 2.0
	var ry := size_mm.y / 2.0

	var points := PackedVector2Array()
	for i in range(ELLIPSE_RESOLUTION):
		var angle := i * TAU / ELLIPSE_RESOLUTION
		points.append(c + Vector2(cos(angle) * rx, sin(angle) * ry))

	draw_colored_polygon(points, fill_color)
	draw_polyline(points + PackedVector2Array([points[0]]), OUTLINE_COLOR, 1.5, true)

	var label := unit.unit_name if unit != null else ""
	if label != "":
		var font := ThemeDB.fallback_font
		var font_size := 12
		var text_width := font.get_string_size(label, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
		var ascent := font.get_ascent(font_size)
		var descent := font.get_descent(font_size)
		var baseline := Vector2(c.x - text_width / 2.0, c.y + (ascent - descent) / 2.0)
		draw_string(font, baseline, label, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color.BLACK)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT:
		drag_requested.emit(self)
		accept_event()
