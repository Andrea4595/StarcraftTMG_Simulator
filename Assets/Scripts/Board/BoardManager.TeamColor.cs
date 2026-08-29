using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 플레이어 색상 변경 ───────────────────────────────────────────
        // 스코어보드 상단의 플레이어 이름을 클릭하면 팔레트가 뜨고, 색을
        // 고르면 여기로 들어온다(ScoreboardPanel). GameConstants.TeamColors는
        // Dictionary라 값만 바꾸면 이후의 모든 조회(로스터 임포트, 토큰 배치
        // 미리보기 등)가 자동으로 새 색을 쓰지만, 이미 보드 위에 나와있는
        // 것들(배치된 유닛, 예비대 목록, 마커, 미션 목표 마커)은 소급해서
        // 직접 갱신해줘야 한다.

        /// <summary>team("A"/"B")의 색을 baseColor로 바꾸고, 이미 존재하는
        /// 모든 관련 시각 요소를 즉시 다시 칠한다.</summary>
        public void SetTeamColor(string team, Color baseColor)
        {
            if (!GameConstants.TeamColors.ContainsKey(team))
            {
                return;
            }

            var stored = baseColor;
            stored.a = 0.85f; // 기존 팀 색과 같은 반투명도를 유지한다(유닛 채우기용).
            GameConstants.TeamColors[team] = stored;

            RepaintPlacedUnitsForTeam(team, stored);
            RepaintPendingUnitsForTeam(team, stored);
            RepaintCaptureMarkers();
            RepaintMissionObjectivesForTeam(team);
            RepaintDeploymentZonesForTeam(team, stored);
        }

        private void RepaintPlacedUnitsForTeam(string team, Color rawColor)
        {
            var mutedColor = GameConstants.Muted(rawColor, TokenColorSaturationFactor, TokenColorValueFactor);
            foreach (var piece in _pieces)
            {
                if (piece == null || piece.Unit == null || piece.Unit.Team != team)
                {
                    continue;
                }
                piece.FillColor = piece.Unit.IsToken ? mutedColor : rawColor;
            }
        }

        private void RepaintPendingUnitsForTeam(string team, Color rawColor)
        {
            foreach (var def in _pendingUnits)
            {
                if (def.Team == team)
                {
                    def.FillColor = rawColor;
                }
            }
        }

        private void RepaintCaptureMarkers()
        {
            if (markerLayer == null)
            {
                return;
            }
            foreach (var marker in markerLayer.GetComponentsInChildren<CaptureMarker>(true))
            {
                marker.RefreshColor();
            }
        }

        /// <summary>1/3/2/4번은 이번에 바뀐 team의 색을 그대로 따르므로 그
        /// 팀에 속한 것만 TokenColor를 다시 계산하면 되지만, 5번(중립)은
        /// "어느 팀이든 초록에 가까운 색을 고르면 회색으로" 규칙이라 A/B 어느
        /// 쪽 색이 바뀌든 항상 다시 계산해야 한다(GameConstants.
        /// GetMissionObjectiveBaseColor). 점령 링(RingColorState)은 아예 별개
        /// 개념이라 — 우클릭으로 순환된 링 색이 "빨강"/"파랑"이면 그 목표
        /// 번호가 어느 팀 소속이든 상관없이 A/B 어느 팀 색이 바뀌든 다시
        /// 칠해야 하므로, Refresh()는 팀 소속과 무관하게 매번 호출한다.</summary>
        private void RepaintMissionObjectivesForTeam(string team)
        {
            if (baseLayer == null)
            {
                return;
            }
            foreach (var piece in baseLayer.GetComponentsInChildren<MissionObjectivePiece>(true))
            {
                var pieceTeam = GameConstants.ObjectiveNumberToTeam(piece.Number);
                if (pieceTeam == team || pieceTeam == null)
                {
                    var baseColor = GameConstants.GetMissionObjectiveBaseColor(piece.Number);
                    piece.TokenColor = GameConstants.Muted(baseColor, GameConstants.MissionObjectiveSaturationFactor, GameConstants.MissionObjectiveValueFactor);
                }
                piece.Refresh();
            }
        }
    }
}
