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

        public const float ScoreboardHeight = 56f;
        public const float PhaseBarHeight = 32f;
        public const float MarkerBarHeight = 44f;
        public const float PendingPanelWidth = 280f;

        // 스코어보드와 페이즈 바를 합친, 화면 위쪽에서 실제로 고정 UI가 차지하는
        // 총 높이 — 맵 뷰포트/팀 패널 등이 "화면 맨 위 고정 바 아래"를 계산할 때
        // ScoreboardHeight 대신 이걸 써야 페이즈 바 추가분만큼 자동으로 반영된다.
        public const float TopBarHeight = ScoreboardHeight + PhaseBarHeight;

        public static readonly Dictionary<string, Vector2> MapSizePresets = new Dictionary<string, Vector2>
        {
            { "36x36", new Vector2(36f * MmPerInch, 36f * MmPerInch) },
            { "54x36", new Vector2(54f * MmPerInch, 36f * MmPerInch) },
        };

        // "A"/"B"는 플레이어가 스코어보드에서 색을 바꿀 수 있다(BoardManager.
        // SetTeamColor) — Dictionary 참조 자체는 readonly지만 내용물(값)은
        // 자유롭게 갱신되므로, 이미 이 딕셔너리를 참조하는 모든 코드(로스터
        // 임포트, 마커, 미션 목표 마커 등)가 자동으로 새 색을 따라간다.
        public static readonly Dictionary<string, Color> TeamColors = new Dictionary<string, Color>
        {
            { "A", new Color(1f, 0.15f, 0.15f, 0.85f) },
            { "B", new Color(0.15f, 0.35f, 1f, 0.85f) },
            { "neutral", new Color(0.6f, 0.6f, 0.6f, 0.85f) },
        };

        /// <summary>플레이어 색상 선택 팝업에 보여줄 미리 정해둔 팔레트.</summary>
        public static readonly Color[] TeamColorPalette =
        {
            new Color(0.85f, 0.15f, 0.15f), // 빨강
            new Color(0.15f, 0.35f, 1f),    // 파랑
            new Color(0.2f, 0.7f, 0.25f),   // 초록
            new Color(0.95f, 0.55f, 0.1f),  // 주황
            new Color(0.55f, 0.25f, 0.85f), // 보라
            new Color(0.9f, 0.85f, 0.15f),  // 노랑
            new Color(0.15f, 0.75f, 0.85f), // 하늘
            new Color(0.9f, 0.25f, 0.6f),   // 분홍
            new Color(0.1f, 0.55f, 0.5f),   // 청록
            new Color(0.55f, 0.35f, 0.2f),  // 갈색
            new Color(0.85f, 0.85f, 0.85f), // 흰색
            new Color(0.15f, 0.15f, 0.15f), // 검정
        };

        // ── 미션 목표 마커 ───────────────────────────────────────────────
        public static readonly int[] MissionObjectiveNumbers = { 1, 2, 3, 4, 5 };
        public const float MissionObjectiveTokenDiameterMm = 32f;
        public const float MissionObjectiveCaptureMarginInch = 3f;
        // 5번(중앙, 중립) 목표 마커의 기본색. 어느 팀이 이 색과 가까운 색을
        // 고르면(GetMissionObjectiveBaseColor) 팔레트에 없는(=플레이어가 절대
        // 고를 수 없는) TeamColors["neutral"] 회색으로 자동 전환해 항상
        // 구분되게 한다.
        private static readonly Color NeutralObjectiveDefaultColor = new Color(0.2f, 0.7f, 0.25f);

        /// <summary>목표 번호 1/3은 A팀, 2/4는 B팀 색을 그대로 따라간다(룰북상
        /// 홀수/짝수 목표가 각 팀 쪽에 가깝다는 관례를 반영) — 5번(중앙)은
        /// 특정 팀에 속하지 않는다.</summary>
        public static string ObjectiveNumberToTeam(int number)
        {
            switch (number)
            {
                case 1:
                case 3:
                    return "A";
                case 2:
                case 4:
                    return "B";
                default:
                    return null;
            }
        }

        /// <summary>미션 목표 마커의 Muted() 이전 원색. 팀 색이 알파 0.85로
        /// 저장돼 있어도(반투명 유닛 채우기용) 목표 마커는 항상 완전 불투명해야
        /// 하므로 알파를 1로 강제한다.</summary>
        public static Color GetMissionObjectiveBaseColor(int number)
        {
            var team = ObjectiveNumberToTeam(number);
            Color c;
            if (team != null)
            {
                c = TeamColors.TryGetValue(team, out var tc) ? tc : Color.white;
            }
            else
            {
                bool collidesWithTeam = ColorsAreClose(NeutralObjectiveDefaultColor, TeamColors["A"])
                        || ColorsAreClose(NeutralObjectiveDefaultColor, TeamColors["B"]);
                c = collidesWithTeam ? TeamColors["neutral"] : NeutralObjectiveDefaultColor;
            }
            c.a = 1f;
            return c;
        }

        /// <summary>RGB 채널 차이의 절댓값 합이 임계값 미만이면 "같은 계열의
        /// 색"으로 본다 — 5번 목표 마커가 어느 팀의 현재 색과 헷갈릴 만큼
        /// 가까운지 판정하는 데만 쓴다.</summary>
        private static bool ColorsAreClose(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.3f;
        }
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
