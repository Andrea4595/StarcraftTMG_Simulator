extends Node

## 배치 가능한 지형 모듈 목록.
## texture/mask_texture는 1mm = 1px로 제작된 이미지.
## size_value는 룰북 상의 "지형 사이즈"에 대응하지만, 이 커스텀 지형 세트에는
## 아직 실제 값이 정해지지 않아 임시값을 넣어두었다. (TODO: 실제 값으로 교체)

var modules: Array[TerrainModuleDef] = []

const _DEFS := [
	{"id": "straight", "name": "직선 지형", "file": "직선 지형", "size_value": 2},
	{"id": "l_shape", "name": "L 지형", "file": "L 지형", "size_value": 2},
	{"id": "slope", "name": "경사 지형", "file": "경사 지형", "size_value": 2},
	{"id": "hill", "name": "언덕 지형", "file": "언덕 지형", "size_value": 2},
	{"id": "bush", "name": "부쉬", "file": "부쉬", "size_value": 0},
]


func _ready() -> void:
	for def in _DEFS:
		var module := TerrainModuleDef.new()
		module.id = def["id"]
		module.display_name = def["name"]
		module.texture = load("res://Terrains/%s.png" % def["file"])
		module.mask_texture = load("res://Terrains/%s 마스크.png" % def["file"])
		module.size_value = def["size_value"]
		modules.append(module)


func get_module(id: String) -> TerrainModuleDef:
	for module in modules:
		if module.id == id:
			return module
	return null
