extends Control

## '범위 표시 → 추가'에서 뜨는 입력창. 인치 단위 거리(소수 가능)와 "상시 표시"
## 여부를 받는다. 이 옵션은 한 번 정해지면 나중에 다시 바꿀 수 없다 —
## 마음에 안 들면 지우고 새로 추가한다. 패널 바깥을 클릭하면 취소된다.

signal confirmed(value: float, always_show: bool)
signal cancelled

var _value_edit: LineEdit
var _always_show_check: CheckBox


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
	box.custom_minimum_size = Vector2(220.0, 0.0)
	panel.add_child(box)

	var title := Label.new()
	title.text = "범위 입력 (인치)"
	box.add_child(title)

	var row := HBoxContainer.new()
	box.add_child(row)
	var label := Label.new()
	label.text = "거리(\")"
	label.custom_minimum_size = Vector2(70.0, 0.0)
	row.add_child(label)
	_value_edit = LineEdit.new()
	_value_edit.custom_minimum_size = Vector2(100.0, 0.0)
	_value_edit.text_submitted.connect(func(_t: String) -> void: _on_confirm_pressed())
	row.add_child(_value_edit)

	_always_show_check = CheckBox.new()
	_always_show_check.text = "상시 표시"
	_always_show_check.button_pressed = true
	box.add_child(_always_show_check)

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


func open(initial_value: float) -> void:
	_value_edit.text = ("%.1f" % initial_value) if initial_value > 0.0 else ""
	_always_show_check.button_pressed = true
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
	## IME(한글 등) 조합 중이던 마지막 글자가 있으면 강제로 확정한다 —
	## Godot의 LineEdit는 조합 중 텍스트가 포커스를 잃을 때만 자동으로
	## 확정되고, 버튼 클릭 등으로 바로 값을 읽으면 그 전에 유실될 수 있다.
	if _value_edit.has_ime_text():
		_value_edit.apply_ime()
	var value := 0.0
	if _value_edit.text.is_valid_float():
		value = float(_value_edit.text)
	var always_show := _always_show_check.button_pressed
	close()
	if value > 0.0:
		confirmed.emit(value, always_show)
	else:
		cancelled.emit()


func _on_cancel_pressed() -> void:
	close()
	cancelled.emit()
