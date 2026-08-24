extends Control

## 게임 화면의 베이스(모델) 하나. 원형 또는 타원형이며, 이름/팀 색을 갖는다.
## 실제 드래그 추적/충돌 해소는 부모(GameBoard)가 전역 입력으로 처리하고,
## 이 노드는 좌클릭 시작 신호만 보낸다.

signal drag_requested(piece: Control)
signal menu_requested(piece: Control, screen_pos: Vector2)

var unit: Unit = null
var size_mm: Vector2 = Vector2(32.0, 32.0) # 가로, 세로 (지름)
var fill_color: Color = Color(0.6, 0.6, 0.6, 0.85)
var damage: int = 0

## 변위 베이스: 모델 메뉴얼 이동/리딩 모델 이동 중에는 이 베이스와 겹쳐서
## 지나갈 수 있다. 그 이동이 끝나면 GameBoard가 이 베이스를 겹치게 된
## 베이스로부터 원하는 거리 0"(딱 붙게) 위치로 밀어낸다.
var is_displacement: bool = false

const DAMAGE_BADGE_RADIUS_MM := 8.0

const OUTLINE_COLOR := Color(0.05, 0.05, 0.05, 0.9)
const DISPLACEMENT_OUTLINE_COLOR := Color(0.2, 0.9, 0.9, 0.95)
const ELLIPSE_RESOLUTION := 48


func _ready() -> void:
	## 회전이 중심을 기준으로 일어나도록. (size는 항상 add_child() 전에
	## 설정되므로 이 시점에는 올바른 값이 들어 있다.)
	pivot_offset = size / 2.0


func bounding_radius() -> float:
	## 지도 경계 clamp, 가이드라인 시각화, 초기 배치 근사 등 "대충 이 정도
	## 크기"면 충분한 곳에서만 쓰는 근사 반지름. 긴 쪽 반지름을 써서(원형으로
	## 근사) 보수적으로 잡는다.
	##
	## 이름이 radius()가 아니라 bounding_radius()인 이유: 실제 겹침/접촉
	## 판정(베이스끼리 물리 충돌, 변위 베이스 밀어내기 등)에 이 원형 근사를
	## 쓰면 타원 베이스에서 틀어진 결과가 나온다 — 이런 사고가 반복됐다.
	## 방향에 따라 달라지는 정확한 값이 필요하면 이 값 대신
	## GameBoard._ellipse_radius_in_direction()을, 폴리곤 전체 겹침 판정이
	## 필요하면 GameBoard._resolve_position()/_polygon_overlap_mtv()를 써라.
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

	var outline_color := DISPLACEMENT_OUTLINE_COLOR if is_displacement else OUTLINE_COLOR
	draw_colored_polygon(points, fill_color)
	draw_polyline(points + PackedVector2Array([points[0]]), outline_color, 1.5 if not is_displacement else 3.0, true)

	var label := unit.unit_name if unit != null else ""
	if label != "":
		var font := ThemeDB.fallback_font
		var font_size := 12
		var text_width := font.get_string_size(label, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
		var ascent := font.get_ascent(font_size)
		var descent := font.get_descent(font_size)
		var baseline := Vector2(c.x - text_width / 2.0, c.y + (ascent - descent) / 2.0)
		draw_string(font, baseline, label, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color.BLACK)

	if damage > 0:
		var badge_center := Vector2(size.x - DAMAGE_BADGE_RADIUS_MM, DAMAGE_BADGE_RADIUS_MM)
		draw_circle(badge_center, DAMAGE_BADGE_RADIUS_MM, Color(0.8, 0.1, 0.1))
		var font := ThemeDB.fallback_font
		var font_size := 10
		var text := str(damage)
		var text_width := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
		var ascent := font.get_ascent(font_size)
		var descent := font.get_descent(font_size)
		var baseline := Vector2(badge_center.x - text_width / 2.0, badge_center.y + (ascent - descent) / 2.0)
		draw_string(font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color.WHITE)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		if event.button_index == MOUSE_BUTTON_LEFT:
			drag_requested.emit(self)
			accept_event()
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			menu_requested.emit(self, event.global_position)
			accept_event()
