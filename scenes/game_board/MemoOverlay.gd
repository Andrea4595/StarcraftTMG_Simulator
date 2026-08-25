extends Control

## 마우스를 올린 유닛에 메모가 있는 모델이 있으면, 그 모델들 각각 아래에
## 메모를 텍스트로 보여준다. GameBoard._update_memo_overlay()가 호버 상태가
## 바뀔 때마다 갱신한다.

var entries: Array = [] # Array[Dictionary]: {"text","pos"}


func _draw() -> void:
	var font := ThemeDB.fallback_font
	var font_size := 13
	for entry in entries:
		var text: String = entry["text"]
		if text == "":
			continue
		var text_width := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
		var pos: Vector2 = entry["pos"]
		var baseline := pos + Vector2(-text_width / 2.0, 0.0)
		draw_string(font, baseline + Vector2(1.0, 1.0), text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color(0, 0, 0, 0.75))
		draw_string(font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color(1.0, 0.95, 0.75, 0.95))
