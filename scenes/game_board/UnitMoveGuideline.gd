extends Control

## 유닛 이동/배치 가이드라인 오버레이.
## radius_mm > 0이면 원(코헤런시/이동거리 경계)을 그리고,
## band_polylines에 담긴 선(배치 가능 구역 외곽선, 여러 개일 수 있음)을
## 그린다. 필요 없는 쪽은 0/빈 배열로 둔다.

var center_point: Vector2 = Vector2.ZERO
var radius_mm: float = 0.0
var band_polylines: Array = [] # Array[PackedVector2Array]
var line_color: Color = Color(1.0, 0.9, 0.2, 0.85)

## 리딩 모델 이동 중 "시작 지점으로부터 이동한 거리"를 보여주는 텍스트.
## label_text가 빈 문자열이면 그리지 않는다.
var label_text: String = ""
var label_pos: Vector2 = Vector2.ZERO


func _draw() -> void:
	if radius_mm > 0.0:
		draw_arc(center_point, radius_mm, 0.0, TAU, 64, line_color, 2.0, true)
	for points in band_polylines:
		if points.size() >= 2:
			draw_polyline(points, line_color, 2.0, true)
	if label_text != "":
		var font := ThemeDB.fallback_font
		var font_size := 16
		var text_width := font.get_string_size(label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
		var baseline := label_pos + Vector2(-text_width / 2.0, 0.0)
		draw_string(font, baseline + Vector2(1.0, 1.0), label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color(0, 0, 0, 0.8))
		draw_string(font, baseline, label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color.WHITE)
