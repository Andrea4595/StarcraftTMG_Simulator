extends Node

## 전역 단위 환산 및 기본 규칙 상수.
## 내부 좌표계는 1 world unit = 1 mm로 통일한다.
## 이동거리/코헤런시 등 규칙 값은 인치(") 단위이므로 계산 시 mm로 환산해서 쓴다.

const MM_PER_INCH := 25.4

const DEFAULT_COHERENCY_INCH := 3.0
const DEFAULT_COHERENCY_MM := DEFAULT_COHERENCY_INCH * MM_PER_INCH

## 보드 크기 프리셋 (mm 단위). 미션 생성 화면에서 선택한다.
const MAP_SIZE_PRESETS := {
	"36x36": Vector2(36.0 * MM_PER_INCH, 36.0 * MM_PER_INCH),
	"54x36": Vector2(54.0 * MM_PER_INCH, 36.0 * MM_PER_INCH),
}
const DEFAULT_MAP_SIZE_PRESET := "36x36"


func inch_to_mm(value_inch: float) -> float:
	return value_inch * MM_PER_INCH


func mm_to_inch(value_mm: float) -> float:
	return value_mm / MM_PER_INCH
