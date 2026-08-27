using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 점령 마커 하나. flag 텍스처를 높이 1"(가로세로 비율은 유지)로 배치한다.
    /// 우클릭할 때마다 흰색 → 빨간색 → 파란색 순으로 계속 순환한다(삭제 없이
    /// 영원히 반복) — 삭제는 별도로 shift+우클릭. Godot판 CaptureMarker.gd
    /// 포팅. Godot판은 검은 실루엣 텍스처를 가정하고 셰이더로 알파만 마스크로
    /// 써서 색을 새로 칠했지만, 실제 flag.png는 RGB가 이미 순백(255,255,255)
    /// + 알파 마스크라서 — 흰색 × 틴트색 = 그 틴트색 그대로이므로 — Unity
    /// RawImage의 기본 곱연산 color 틴트만으로 셰이더 없이 동일한 결과가
    /// 나온다(직접 픽셀 확인 후 결정, 별도 셰이더 포팅 불필요).
    /// </summary>
    public class CaptureMarker : MarkerBase
    {
        public const float MarkerHeightMm = 25.4f; // 1"
        public static readonly string[] ColorSequence = { "white", "red", "blue" };
        private static readonly System.Collections.Generic.Dictionary<string, Color> ColorValues = new System.Collections.Generic.Dictionary<string, Color>
        {
            { "white", new Color(1f, 1f, 1f, 1f) },
            { "red", new Color(0.9f, 0.15f, 0.15f, 1f) },
            { "blue", new Color(0.15f, 0.4f, 0.9f, 1f) },
        };

        public string ColorState { get; private set; } = "white";

        public void Configure(Texture2D flagTexture)
        {
            texture = flagTexture;
            float aspect = flagTexture != null && flagTexture.height > 0
                    ? (float)flagTexture.width / flagTexture.height
                    : 1f;
            RectTransform.sizeDelta = new Vector2(MarkerHeightMm * aspect, MarkerHeightMm);
            RefreshColor();
        }

        public void SetColorState(string state)
        {
            ColorState = state;
            RefreshColor();
        }

        private void RefreshColor()
        {
            color = ColorValues[ColorState];
        }
    }
}
