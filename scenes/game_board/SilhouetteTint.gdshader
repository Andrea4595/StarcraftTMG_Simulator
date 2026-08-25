shader_type canvas_item;

// flag.png가 검은색 실루엣이라 일반 modulate(곱연산)로는 다른 색이 안
// 나온다(검은색 × 무슨 색 = 검은색). 텍스처의 알파만 모양 마스크로 쓰고
// 색은 여기서 직접 칠한다.
//
// fragment() 진입 시점의 COLOR는 이미 "텍스처 알파 × node.modulate"가 곱해진
// 값이다 — 그 알파를 그대로 이어받아야 배치 미리보기의 modulate.a(반투명
// 고스트) 같은 게 계속 먹힌다. COLOR를 통째로 새로 만들면서 이 알파를
// 빼먹으면 modulate가 무시되고 항상 불투명하게 나온다.

uniform vec4 tint_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
	COLOR = vec4(tint_color.rgb, tint_color.a * COLOR.a);
}
