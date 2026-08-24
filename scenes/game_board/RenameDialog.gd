extends Control

## 다이얼 메뉴 "이름 변경"에서 뜨는 입력창. 텍스트 하나만 받는다.
## 패널 바깥을 클릭하면 취소된다.

signal confirmed(value: String)
signal cancelled

var _value_edit: LineEdit


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
	box.custom_minimum_size = Vector2(200.0, 0.0)
	panel.add_child(box)

	var title := Label.new()
	title.text = "유닛 이름 변경"
	box.add_child(title)

	var row := HBoxContainer.new()
	box.add_child(row)
	var label := Label.new()
	label.text = "이름"
	label.custom_minimum_size = Vector2(70.0, 0.0)
	row.add_child(label)
	_value_edit = LineEdit.new()
	_value_edit.custom_minimum_size = Vector2(100.0, 0.0)
	_value_edit.text_submitted.connect(func(_t: String) -> void: _on_confirm_pressed())
	row.add_child(_value_edit)

	var button_row := HBoxContainer.new()
	box.add_child(button_row)
	var confirm_btn := Button.new()
	confirm_btn.text = "확인"
	confirm_btn.pressed.connect(_on_confirm_pressed)
	button_row.add_child(confirm_btn)
	var cancel_btn := Button.new()
	cancel_btn.text = "취소"
	cancel_btn.pressed.connect(_on_cancel_pressed)
	button_row.add_child(cancel_btn)


func open(initial_value: String) -> void:
	_value_edit.text = initial_value
	visible = true
	mouse_filter = Control.MOUSE_FILTER_STOP
	_value_edit.grab_focus()
	_value_edit.select_all()


func close() -> void:
	visible = false
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		_on_cancel_pressed()
		accept_event()


func _on_confirm_pressed() -> void:
	var value := _value_edit.text.strip_edges()
	close()
	confirmed.emit(value)


func _on_cancel_pressed() -> void:
	close()
	cancelled.emit()
