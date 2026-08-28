using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    public static class GameConstants
    {
        public const float MmPerInch = 25.4f;
        public const float DefaultMoveInch = 4f;
        public const float DefaultCoherencyInch = 3f;

        public const string DefaultMapSizePreset = "36x36";

        public const float ScoreboardHeight = 84f;

        public static readonly Dictionary<string, Vector2> MapSizePresets = new Dictionary<string, Vector2>
        {
            { "36x36", new Vector2(36f * MmPerInch, 36f * MmPerInch) },
            { "54x36", new Vector2(54f * MmPerInch, 36f * MmPerInch) },
        };

        public static readonly Dictionary<string, Color> TeamColors = new Dictionary<string, Color>
        {
            { "A", new Color(1f, 0.15f, 0.15f, 0.85f) },
            { "B", new Color(0.15f, 0.35f, 1f, 0.85f) },
            { "neutral", new Color(0.6f, 0.6f, 0.6f, 0.85f) },
        };

        // ── 미션 목표 마커 ───────────────────────────────────────────────
        public static readonly int[] MissionObjectiveNumbers = { 1, 2, 3, 4, 5 };
        public const float MissionObjectiveTokenDiameterMm = 32f;
        public const float MissionObjectiveCaptureMarginInch = 3f;
        public static readonly Dictionary<int, Color> MissionObjectiveTokenColors = new Dictionary<int, Color>
        {
            { 1, new Color(0.85f, 0.15f, 0.15f) },
            { 2, new Color(0.15f, 0.4f, 0.85f) },
            { 3, new Color(0.85f, 0.15f, 0.15f) },
            { 4, new Color(0.15f, 0.4f, 0.85f) },
            { 5, new Color(0.2f, 0.7f, 0.25f) },
        };
        // 유닛 모델과 겹쳤을 때 헷갈리지 않도록 원색보다 살짝 탁하게 보이도록
        // 조정한다(Muted() 참고) — 로스터 토큰(더 큰 폭으로 톤다운)보다는
        // 가볍게.
        public const float MissionObjectiveSaturationFactor = 0.65f;
        public const float MissionObjectiveValueFactor = 0.9f;

        /// <summary>채도를 낮추고 살짝만 어둡게 해서 원래 색보다 "탁하게"
        /// 보이도록 한다. 단순히 어둡게만 하면(RGB를 그대로 곱하면 채도는 안
        /// 바뀌고 명도만 낮아짐) 라벨 텍스트 등과의 대비가 부족해질 수 있어서
        /// HSV의 채도/명도만 따로 조정한다. 로스터 토큰 색(BoardManager.Roster)과
        /// 미션 목표 마커 색 둘 다 여기를 공유한다.</summary>
        public static Color Muted(Color c, float saturationFactor, float valueFactor)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var rgb = Color.HSVToRGB(h, Mathf.Clamp01(s * saturationFactor), Mathf.Clamp01(v * valueFactor));
            return new Color(rgb.r, rgb.g, rgb.b, c.a);
        }
    }
}
