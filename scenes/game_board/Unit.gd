class_name Unit
extends RefCounted

## 하나의 유닛(모델 그룹). 다이얼 메뉴의 "복제"는 같은 유닛에 모델을 추가하고,
## "유닛 이동"은 이 유닛에 속한 모든 모델을 함께 움직인다.
## 모델들은 유닛 이름을 공유한다 (개별 모델 이름은 없음).

var unit_name: String = ""
var team: String = "neutral"
var coherency_inch: float = GameConstants.DEFAULT_COHERENCY_INCH
var move_inch: float = GameConstants.DEFAULT_MOVE_INCH
var models: Array = [] # Array[Control] (Base 조각들)
