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

        // 씬 이름 — GameFlowBootstrap의 씬 전환 스위치와 BoardManager의
        // "나가기" 버튼(진행 중인 판을 버리고 처음으로 돌아가기) 둘 다 같은
        // 이름을 써야 하므로 여기 하나로 모아둔다.
        //
        // 실제 게임 흐름(싱글플레이, 2026-08-30 재구성): Entry → Selection
        // (배치/미션 프리셋을 원하는 순서로 골라 MapData/MissionSettingsData를
        // 채움, 둘 다 고르면 자동 진행) → TerrainSetup(그 배치 프리셋의 지도
        // 크기/배치구역/미션 마커를 읽기 전용 참고로 보여주며 지형만 매 게임
        // 새로 배치 — 지형은 프리셋으로 저장하지 않는다) → GameBoard.
        //
        // MapAuthoring/MissionAuthoring은 이 흐름에 안 낀다 — Entry 한쪽
        // 구석의 별도 버튼으로 들어가는 "프리셋 제작" 전용 화면(옛 MapSetup/
        // MissionSetup, 예전엔 라이브 셋업 화면이었다가 지금은 편집기로만
        // 쓰인다)이고, 완료하면 그냥 Entry로 돌아간다. LoadGame도 같은
        // 자리(Entry 구석)에서 들어가는 별도 화면(2026-08-31 신설, 저장된
        // 게임 불러오기) — 고르면 GameLoadRequest에 파싱된 내용을 담아두고
        // 곧바로 GameBoard로 간다(Selection/TerrainSetup을 거치지 않는다 —
        // 저장 파일 자체가 지도/미션/라이브 상태를 전부 담고 있어서 필요 없음).
        public const string EntrySceneName = "Entry";
        public const string MapAuthoringSceneName = "MapAuthoring";
        public const string MissionAuthoringSceneName = "MissionAuthoring";
        public const string SelectionSceneName = "Selection";
        // 멀티플레이어 전용 미션/배치 드래프트 흐름(2026-08-31 신설) — Entry의
        // "게임 시작"을 눌렀을 때 NetworkManager가 연결(호스트/클라이언트)
        // 중이면 Selection 대신 이 둘을 거친다: CardPrep(각자 화면에서 배치
        // 프리셋 2장+미션 프리셋 2장을 고름) → CardDraft(양쪽 8장을 모아
        // 보여주고 롤오프/자유 밴·픽) → TerrainSetup(이후는 솔로와 합류,
        // 다만 지형 배치가 실시간으로 동기화된다는 점만 다르다).
        public const string CardPrepSceneName = "CardPrep";
        public const string CardDraftSceneName = "CardDraft";
        public const string TerrainSetupSceneName = "TerrainSetup";
        public const string LoadGameSceneName = "LoadGame";
        public const string GameBoardSceneName = "GameBoard";

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
        private static readonly Color DefaultTeamColorA = new Color(1f, 0.15f, 0.15f, 0.85f);
        private static readonly Color DefaultTeamColorB = new Color(0.15f, 0.35f, 1f, 0.85f);
        private static readonly Color DefaultTeamColorNeutral = new Color(0.6f, 0.6f, 0.6f, 0.85f);

        public static readonly Dictionary<string, Color> TeamColors = new Dictionary<string, Color>
        {
            { "A", DefaultTeamColorA },
            { "B", DefaultTeamColorB },
            { "neutral", DefaultTeamColorNeutral },
        };

        /// <summary>진행 중이던 판을 버리고 Entry로 나갈 때(BoardManager.
        /// OnExitConfirmed) 부른다 — TeamColors는 static Dictionary라 값이
        /// 그대로 남기 때문에, 스코어보드에서 바꾼 플레이어 색이 다음 판의
        /// 지형 배치 화면(미션 마커 미리보기 등)에 잘못 이어지는 실제 버그가
        /// 있었다(사용자 발견). 딕셔너리 참조 자체는 그대로 두고 값만
        /// 기본값으로 되돌린다 — 이미 이 딕셔너리를 참조 중인 모든 코드가
        /// 자동으로 반영된다.
        ///
        /// **2026-09-09 버그 수정**: "나갈 때"만 리셋하는 걸로는 부족했다 —
        /// 앱을 계속 켜둔 채 "새 게임"을 시작하면(Entry로 나간 적 없이)
        /// 지난 판 색이 그대로 새 판에 이어졌고, 특히 멀티에서는 호스트/
        /// 참가자 각자의 로컬 상태가 서로 다르게 남아있어 양쪽 색이 어긋나
        /// 보이는 문제로 이어졌다. 그래서 "새 판이 시작되는" 진짜 시점에도
        /// 부른다 — 솔로는 SelectionController.Start(), 멀티는 양쪽 다
        /// 반드시 거치는 CardPrepController.Start()(게임 도중 합류는 이
        /// 화면을 안 거치므로 해당 없음 — 그쪽은 기존 색을 그대로 넘겨받는
        /// 것이 맞다).</summary>
        public static void ResetTeamColors()
        {
            TeamColors["A"] = DefaultTeamColorA;
            TeamColors["B"] = DefaultTeamColorB;
            TeamColors["neutral"] = DefaultTeamColorNeutral;
        }

        // 텍스트용으로 보정할 때 강제할 최소 명도(V, HSV) — 플레이어가
        // 스코어보드에서 팀 색을 아주 어둡게 고르면(검정에 가깝게) 그 색
        // 그대로는 어두운 배경(채팅/로그 창 등) 위에서 텍스트가 거의 안
        // 보이게 된다(사용자 보고, 2026-09-09). 이미 밝은 색은 이 값보다
        // 명도가 높으므로 전혀 안 바뀐다 — "너무 어두울 때만" 끌어올리는
        // 효과. 처음엔 0.85로 했는데, 채도가 낮은(거의 검정에 가까운) 색은
        // 그 정도로 밝히면 사실상 회색이 돼 흰색(팀 없음) 메시지와 구분이
        // 안 됐다(사용자 재보고, 같은 날) — "적당히 어둡게"로 낮췄다.
        private const float MinTextColorValue = 0.5f;

        /// <summary>팀 코드("A"/"B" 등)로 텍스트에 칠할 색을 찾는다 — 되돌리기
        /// 토스트/되돌리기 목록 텍스트를 행위자 팀 색으로 칠하기 위해 쓴다
        /// (2026-09-04, 사용자 요청). team이 비어있거나 TeamColors에 없는
        /// 값(마커처럼 팀 소유가 뚜렷하지 않은 행동)이면 흰색으로 대체한다.
        /// TeamColors 자체는 유닛 채우기용으로 알파 0.85가 섞여 있어(반투명),
        /// 텍스트 가독성을 위해 항상 알파 1로 강제한다. 명도(HSV의 V)도
        /// MinTextColorValue 밑으로는 못 내려가게 끌어올린다 — 색상(Hue)/
        /// 채도는 그대로 두고 명도만 보정하므로 팀 색의 정체성은 유지하면서
        /// 어두운 색을 골랐을 때만 텍스트가 실제로 보이게 한다.</summary>
        public static Color ResolveTeamTextColor(string team)
        {
            if (!string.IsNullOrEmpty(team) && TeamColors.TryGetValue(team, out var c))
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                var boosted = Color.HSVToRGB(h, s, Mathf.Max(v, MinTextColorValue));
                return new Color(boosted.r, boosted.g, boosted.b, 1f);
            }
            return Color.white;
        }

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
