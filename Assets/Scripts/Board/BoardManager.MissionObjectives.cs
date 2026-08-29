using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 미션 목표 마커(순수 표시용 + 점령 링 색상 순환) ──────────────
        // 미션 설정 화면에서 놓은 목표를 그대로 재현한다. Godot판
        // GameBoard.gd의 _build_mission_data_visuals() 중 목표 부분 포팅 —
        // 드래그/삭제는 지원하지 않는다(그 편집은 미션 설정 화면의 몫).
        // 유일한 조작은 우클릭 → 점령 범위 링 색상을 흰→빨→파로 순환시키는
        // 것뿐(CaptureMarker와 같은 인터랙션, 삭제는 절대 없음) — 그래서
        // raycastTarget은 켜두되(우클릭을 받으려면 필요) AllowDrag=false로
        // 드래그만 막는다.

        private void BuildMissionObjectiveVisuals()
        {
            if (!MapData.HasData)
            {
                return;
            }

            float diameter = GameConstants.MissionObjectiveTokenDiameterMm
                    + 2f * GameConstants.MissionObjectiveCaptureMarginInch * GameConstants.MmPerInch;

            foreach (var objective in MapData.MissionObjectives)
            {
                var go = new GameObject($"MissionObjective_{objective.Number}", typeof(RectTransform));
                go.transform.SetParent(baseLayer, false);
                var piece = go.AddComponent<MissionObjectivePiece>();
                piece.Number = objective.Number;
                var baseColor = GameConstants.GetMissionObjectiveBaseColor(objective.Number);
                piece.TokenColor = GameConstants.Muted(baseColor, GameConstants.MissionObjectiveSaturationFactor, GameConstants.MissionObjectiveValueFactor);
                piece.RectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                piece.RectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                piece.RectTransform.sizeDelta = new Vector2(diameter, diameter);
                piece.Center = CornerToCenterMm(objective.Position);
                piece.AllowDrag = false;
                piece.RightClickCyclesColor = true;
            }
        }
    }
}
