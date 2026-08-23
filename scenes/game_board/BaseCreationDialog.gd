extends Control

## 다이얼 메뉴 e("여기에 베이스 생성")에서 뜨는 입력창.
## 이름 / 가로(mm) / 세로(mm, 비우면 가로와 같아져 원형) / 팀을 입력받는다.
## 패널 바깥을 클릭하면 취소된다.

signal confirmed(data: Dictionary)
signal cancelled

var _name_edit: LineEdit
var _width_edit: LineEdit
var _height_edit: LineEdit
var _team_buttons: Dictionary = {}
var _selected_team: String = "A"
var _displacement_check: CheckBox


func _ready() -> void:
	visible = false
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	_build_ui()


func _build_ui() -> void:
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(center)

	var panel := PanelContainer.new()
	center.add_child(panel)

	var box := VBoxContainer.new()
	box.custom_minimum_size = Vector2(260.0, 0.0)
	panel.add_child(box)

	var title := Label.new()
	title.text = "베이스 생성"
	box.add_child(title)

	_name_edit = _add_field(box, "이름")
	_width_edit = _add_field(box, "가로(mm)")
	_height_edit = _add_field(box, "세로(mm)")
	_height_edit.placeholder_text = "비우면 원형"

	var team_row := HBoxContainer.new()
	box.add_child(team_row)
	var team_label := Label.new()
	team_label.text = "팀"
	team_label.custom_minimum_size = Vector2(70.0, 0.0)
	team_row.add_child(team_label)

	var group := ButtonGroup.new()
	for team_id in ["A", "B", "neutral"]:
		var btn := Button.new()
		btn.text = "중립" if team_id == "neutral" else team_id
		btn.toggle_mode = true
		btn.button_group = group
		btn.pressed.connect(_on_team_pressed.bind(team_id))
		team_row.add_child(btn)
		_team_buttons[team_id] = btn

	_displacement_check = CheckBox.new()
	_displacement_check.text = "변위"
	box.add_child(_displacement_check)

	var button_row := HBoxContainer.new()
	box.add_child(button_row)
	var confirm_btn := Button.new()
	confirm_btn.text = "생성"
	confirm_btn.pressed.connect(_on_confirm_pressed)
	button_row.add_child(confirm_btn)
	var cancel_btn := Button.new()
	cancel_btn.text = "취소"
	cancel_btn.pressed.connect(_on_cancel_pressed)
	button_row.add_child(cancel_btn)


func _add_field(box: VBoxContainer, label_text: String) -> LineEdit:
	var row := HBoxContainer.new()
	box.add_child(row)
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(70.0, 0.0)
	row.add_child(label)
	var edit := LineEdit.new()
	edit.custom_minimum_size = Vector2(150.0, 0.0)
	row.add_child(edit)
	return edit


func _on_team_pressed(team_id: String) -> void:
	_selected_team = team_id


func open() -> void:
	_name_edit.text = "베이스"
	_width_edit.text = "32"
	_height_edit.text = ""
	_selected_team = "A"
	_team_buttons["A"].button_pressed = true
	_displacement_check.button_pressed = false
	visible = true
	mouse_filter = Control.MOUSE_FILTER_STOP
	_name_edit.grab_focus()


func close() -> void:
	visible = false
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		_on_cancel_pressed()
		accept_event()


func _on_confirm_pressed() -> void:
	var width := 32.0
	if _width_edit.text.is_valid_float():
		width = float(_width_edit.text)
	var height := width
	if _height_edit.text.is_valid_float():
		height = float(_height_edit.text)

	var data := {
		"name": _name_edit.text,
		"width_mm": max(width, 1.0),
		"height_mm": max(height, 1.0),
		"team": _selected_team,
		"is_displacement": _displacement_check.button_pressed,
	}
	close()
	confirmed.emit(data)


func _on_cancel_pressed() -> void:
	close()
	cancelled.emit()
