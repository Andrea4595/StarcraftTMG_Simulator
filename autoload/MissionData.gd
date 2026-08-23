extends Node

## 미션 생성 화면에서 만든 상태를 게임 화면으로 넘겨주는 핸드오프 저장소.
## "게임 시작" 버튼을 누르는 순간 스냅샷이 여기 채워진다.

var has_data: bool = false

var map_preset: String = GameConstants.DEFAULT_MAP_SIZE_PRESET

var terrain_pieces: Array = [] # [{module_id: String, position: Vector2, rotation_deg: float}]
var deployment_zones: Array = [] # [{edge: String, player: String, start_along: float, end_along: float}]
var mission_objectives: Array = [] # [{number: int, position: Vector2}]


func clear() -> void:
	has_data = false
	map_preset = GameConstants.DEFAULT_MAP_SIZE_PRESET
	terrain_pieces.clear()
	deployment_zones.clear()
	mission_objectives.clear()
