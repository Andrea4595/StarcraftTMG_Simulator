extends Control

## 유닛 이동/배치 가이드라인 오버레이.
## radius_mm > 0이면 원(코헤런시/이동거리 경계), band_rect가 비어있지 않으면
## 사각형(배치 가능 구역 경계선)을 그린다. 필요 없는 쪽은 0/빈 Rect2로 둔다.

var center_point: Vector2 = Vector2.ZERO
var radius_mm: float = 0.0
var band_rect: Rect2 = Rect2()
var line_color: Color = Color(1.0, 0.9, 0.2, 0.85)


func _draw() -> void:
	if radius_mm > 0.0:
		draw_arc(center_point, radius_mm, 0.0, TAU, 64, line_color, 2.0, true)
	if band_rect.size.x > 0.0 and band_rect.size.y > 0.0:
		draw_rect(band_rect, line_color, false, 2.0)
