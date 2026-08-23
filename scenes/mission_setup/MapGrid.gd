extends Control

## 지도 배경에 1인치 간격 참고용 격자선을 그린다. 게임 규칙과는 무관한 시각 보조선.

var line_spacing_mm: float = GameConstants.MM_PER_INCH
var line_color: Color = Color(1.0, 1.0, 1.0, 0.15)

var major_line_spacing_mm: float = GameConstants.MM_PER_INCH * 9.0
var major_line_color: Color = Color(1.0, 1.0, 1.0, 0.35)


func _draw() -> void:
	_draw_grid(line_spacing_mm, line_color, 1.0)
	_draw_grid(major_line_spacing_mm, major_line_color, 2.0)


func _draw_grid(spacing_mm: float, color: Color, width: float) -> void:
	var x := 0.0
	while x <= size.x:
		draw_line(Vector2(x, 0.0), Vector2(x, size.y), color, width)
		x += spacing_mm

	var y := 0.0
	while y <= size.y:
		draw_line(Vector2(0.0, y), Vector2(size.x, y), color, width)
		y += spacing_mm
