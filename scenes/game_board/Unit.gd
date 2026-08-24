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

## 로스터 "tokens" 배열에서 온 토큰 유닛이면 true. 다이얼 메뉴에서
## 데미지 기록/유닛 되돌리기/유닛 이동 시작을 감춘다.
var is_token: bool = false

## 로스터 stat.spd가 null(이동 스탯 자체가 없는 유닛 — 수정탑 등)이면 false.
## 다이얼 메뉴에서 "유닛 이동 시작"을 감춘다. move_inch/coherency_inch가
## 0인 것과는 다른 개념이다 — 이동력이 0인 게 아니라 애초에 이동이라는
## 행위 자체가 없는 유닛이라는 뜻.
var can_move: bool = true
