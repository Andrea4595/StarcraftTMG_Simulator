extends Control

## 재사용 가능한 우클릭 다이얼 메뉴.
## open()으로 액션 목록을 원형으로 배치해서 보여주고, 버튼을 누르면
## action_chosen 신호를 보낸 뒤 스스로 닫힌다. 바깥을 클릭해도 닫힌다.

signal action_chosen(action: String)

const BUTTON_SIZE := Vector2(96, 36)
const RADIUS := 64.0

@onready var button_holder: Control = $ButtonHolder


func _ready() -> void:
	visible = false
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func open(actions: Array, screen_pos: Vector2) -> void:
	for child in button_holder.get_children():
		child.queue_free()

	var count := actions.size()
	var start_angle := -PI / 2.0
	for i in range(count):
		var action: Dictionary = actions[i]
		var angle := start_angle + (TAU / count) * i
		var center := screen_pos + Vector2(cos(angle), sin(angle)) * (RADIUS if count > 1 else 0.0)

		var button := Button.new()
		button.text = action.get("label", "")
		button.custom_minimum_size = BUTTON_SIZE
		button.size = BUTTON_SIZE
		button.position = center - BUTTON_SIZE / 2.0
		button.pressed.connect(_choose.bind(action.get("action", "")))
		button_holder.add_child(button)

	visible = true
	mouse_filter = Control.MOUSE_FILTER_STOP


func close() -> void:
	visible = false
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func _choose(action: String) -> void:
	## close()를 emit()보다 먼저 해야 한다 — 핸들러가 새 목록으로 메뉴를
	## 다시 열 수도 있는데(예: 범위 입력기 삭제 시 하위 목록), emit() 이후에
	## close()를 부르면 방금 다시 연 메뉴를 즉시 닫아버리게 된다.
	close()
	action_chosen.emit(action)


func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		close()
		accept_event()
