extends Node

## 진행 중인 한 판의 상태를 담는 뼈대.
## 목표 1에서는 이 값들을 화면 상단 전광판에서 직접 수정하는 정도만 지원할 예정이다.
## (필드 정의만 해두고, UI 연결은 이후 단계에서 진행)

var round_number: int = 1
var supply: int = 0

var mission_vp: Dictionary = {"A": 0, "B": 0}
var kill_vp: Dictionary = {"A": 0, "B": 0}


func total_vp(player: String) -> int:
	return mission_vp.get(player, 0) + kill_vp.get(player, 0)
