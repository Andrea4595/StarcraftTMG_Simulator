extends Control

## 유닛 이동 중 코헤런시 가이드라인(원)을 그리는 오버레이. radius_mm이 0이면 아무것도 안 그린다.

var center_point: Vector2 = Vector2.ZERO
var radius_mm: float = 0.0
var line_color: Color = Color(1.0, 0.9, 0.2, 0.85)


func _draw() -> void:
	if radius_mm <= 0.0:
		return
	draw_arc(center_point, radius_mm, 0.0, TAU, 64, line_color, 2.0, true)
