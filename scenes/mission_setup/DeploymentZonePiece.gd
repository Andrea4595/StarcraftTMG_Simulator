extends Control

## 지도 가장자리를 따라 그어진 배치구역 선(구간) 하나.
## 클릭 판정 영역(size)은 실제 보이는 선보다 넉넉하게 잡아서,
## 얇은 선이라도 우클릭 삭제가 쉽도록 한다.
## 이동/회전은 지원하지 않는다 (다시 그려서 대체). 우클릭하면 바로 삭제된다.

var owner_player: String = ""
var line_color: Color = Color.WHITE
var visual_thickness: float = 6.0


func _draw() -> void:
	if size.x >= size.y:
		var y := (size.y - visual_thickness) / 2.0
		draw_rect(Rect2(0.0, y, size.x, visual_thickness), line_color)
	else:
		var x := (size.x - visual_thickness) / 2.0
		draw_rect(Rect2(x, 0.0, visual_thickness, size.y), line_color)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
		queue_free()
		accept_event()
