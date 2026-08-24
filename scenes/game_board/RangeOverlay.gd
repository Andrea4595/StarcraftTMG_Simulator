extends Control

## '범위 표시' 오버레이. 유닛에 설정된 각 사거리(인치)마다, 그 유닛의
## 모든 모델 베이스 테두리로부터 해당 거리 안의 영역(항상 병합해서 하나의
## 모양으로)을 그린다.
##
## fill과 outline을 서로 다른 레이어(서로 다른 형제 Control, mode로 구분)에
## 나눠 그린다 — 이 스크립트 하나를 두 인스턴스에 붙이고 mode만 다르게
## 설정해서 쓴다. 둘 다 GameBoard.gd에서 base_layer/token_layer보다 먼저
## (=아래에) 추가돼서, 지도/지형보다는 앞이지만 모든 유닛/토큰보다는
## 뒤에 보인다.

var entries: Array = [] # Array[Dictionary]: {"polygons","label_text","label_pos","hovered"}
var highlight_polygons: Array = [] # Array[PackedVector2Array], "fill" 모드에서만 쓰는 모델 단위 강조 채우기
var mode: String = "fill" # "fill" 또는 "outline"

const FILL_COLOR := Color(0.85, 0.85, 0.85, 0.02)
const HIGHLIGHT_FILL_COLOR := Color(0.85, 0.85, 0.85, 0.15) # 마우스가 올라간 모델 하나만의 범위 강조
const OUTLINE_COLOR := Color(0.85, 0.85, 0.85, 0.45)
const HOVER_OUTLINE_COLOR := Color(1.0, 0.85, 0.15, 0.9) # 마우스가 올라간 유닛의 범위 강조(다른 범위들 사이에서 구분되게 노란색)
const LABEL_COLOR := Color(0.95, 0.95, 0.95, 0.8)
const HOVER_LABEL_COLOR := Color(1.0, 0.9, 0.3, 0.95)
const DASH_LENGTH := 6.0
const GAP_LENGTH := 5.0


func _draw() -> void:
	if mode == "fill":
		for entry in entries:
			for polygon in entry["polygons"]:
				if polygon.size() >= 3:
					draw_colored_polygon(polygon, FILL_COLOR)
		for polygon in highlight_polygons:
			if polygon.size() >= 3:
				draw_colored_polygon(polygon, HIGHLIGHT_FILL_COLOR)
		return

	for entry in entries:
		var hovered: bool = entry.get("hovered", false)
		var outline_color := HOVER_OUTLINE_COLOR if hovered else OUTLINE_COLOR
		for polygon in entry["polygons"]:
			if polygon.size() < 2:
				continue
			var closed := PackedVector2Array(polygon)
			closed.append(polygon[0])
			_draw_dashed_polyline(closed, outline_color)

		var label_text: String = entry.get("label_text", "")
		if label_text != "":
			var label_color := HOVER_LABEL_COLOR if hovered else LABEL_COLOR
			var font := ThemeDB.fallback_font
			var font_size := 14
			var text_width := font.get_string_size(label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
			var pos: Vector2 = entry["label_pos"]
			var baseline := pos + Vector2(-text_width / 2.0, 0.0)
			draw_string(font, baseline + Vector2(1.0, 1.0), label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color(0, 0, 0, 0.7))
			draw_string(font, baseline, label_text, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, label_color)


func _draw_dashed_polyline(points: PackedVector2Array, color: Color) -> void:
	## 점 하나하나를 잇는 여러 선분에 걸쳐, 폴리라인 전체를 따라 누적된
	## 거리를 기준으로 점선 on/off 구간을 정하므로 선분 경계에서도 점선
	## 리듬이 끊기지 않는다.
	var period := DASH_LENGTH + GAP_LENGTH
	var cumulative := 0.0
	for i in range(points.size() - 1):
		var a: Vector2 = points[i]
		var b: Vector2 = points[i + 1]
		var seg_len := a.distance_to(b)
		if seg_len < 0.0001:
			continue
		var dir: Vector2 = (b - a) / seg_len
		var traveled := 0.0
		while traveled < seg_len:
			var phase := fmod(cumulative + traveled, period)
			var dash_on := phase < DASH_LENGTH
			var step: float = (DASH_LENGTH - phase) if dash_on else (period - phase)
			step = min(step, seg_len - traveled)
			if dash_on:
				draw_line(a + dir * traveled, a + dir * (traveled + step), color, 1.5, true)
			traveled += step
		cumulative += seg_len
