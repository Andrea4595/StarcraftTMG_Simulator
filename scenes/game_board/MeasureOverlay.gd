extends Control

## '거리 재기' 도구 오버레이. 스페이스바를 누르고 있는 동안 시작점부터
## 현재 마우스 위치까지 선을 긋고, 그 거리를 인치로 표시한다.

var line_visible: bool = false
var from_point: Vector2 = Vector2.ZERO
var to_point: Vector2 = Vector2.ZERO
var label_text: String = ""
var line_color: Color = Color(1.0, 0.95, 0.3, 0.9)


func _draw() -> void:
	if not line_visible:
		return
	draw_line(from_point, to_point, line_color, 2.0, true)
	draw_circle(from_point, 3.0, line_color)
	draw_circle(to_point, 3.0, line_color)

	if label_text != "":
		var font := ThemeDB.fallback_font
		var font_size := 16
		var text_width := font.get_string_size(label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
		var mid := (from_point + to_point) / 2.0
		var baseline := mid + Vector2(-text_width / 2.0, -10.0)
		draw_string(font, baseline + Vector2(1.0, 1.0), label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color(0, 0, 0, 0.8))
		draw_string(font, baseline, label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color.WHITE)
