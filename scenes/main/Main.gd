extends Control

const MISSION_SETUP_SCENE := preload("res://scenes/mission_setup/MissionSetup.tscn")
const GAME_BOARD_SCENE := preload("res://scenes/game_board/GameBoard.tscn")

@onready var screen_host: Control = $ScreenHost

var _current_screen: Node = null


func _ready() -> void:
	_show_screen(MISSION_SETUP_SCENE)


func _show_screen(scene: PackedScene) -> void:
	if _current_screen:
		_current_screen.queue_free()
	_current_screen = scene.instantiate()
	screen_host.add_child(_current_screen)
	if _current_screen.has_signal("start_game_requested"):
		_current_screen.start_game_requested.connect(_on_game_board_button_pressed)


func _on_mission_setup_button_pressed() -> void:
	_show_screen(MISSION_SETUP_SCENE)


func _on_game_board_button_pressed() -> void:
	_show_screen(GAME_BOARD_SCENE)
