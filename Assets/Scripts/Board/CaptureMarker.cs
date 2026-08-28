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

        public string ColorState { get; private set; } = "white";

        /// <summary>"red"/"blue"는 고정 색이 아니라 A/B팀의 현재 색을 그대로
        /// 따라간다(GameConstants.TeamColors, 플레이어가 스코어보드에서 바꿀 수
        /// 있음) — 항상 완전 불투명해야 하므로 알파는 1로 강제한다.</summary>
        private static Color ResolveColor(string state)
        {
            switch (state)
            {
                case "red":
                    return WithFullAlpha(GameConstants.TeamColors.TryGetValue("A", out var a) ? a : new Color(0.9f, 0.15f, 0.15f));
                case "blue":
                    return WithFullAlpha(GameConstants.TeamColors.TryGetValue("B", out var b) ? b : new Color(0.15f, 0.4f, 0.9f));
                default:
                    return Color.white;
            }
        }

        private static Color WithFullAlpha(Color c)
        {
            c.a = 1f;
            return c;
        }

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

        /// <summary>팀 색이 바뀐 뒤 이미 배치된 마커의 색을 다시 계산시킨다
        /// (BoardManager.SetTeamColor가 보드 위 모든 CaptureMarker에 대해 부른다).</summary>
        public void RefreshColor()
        {
            color = ResolveColor(ColorState);
        }
    }
}
