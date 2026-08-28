using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 미션 목표 마커(순수 표시용) ────────────────────────────────
        // 미션 설정 화면에서 놓은 목표를 그대로 재현한다. Godot판
        // GameBoard.gd의 _build_mission_data_visuals() 중 목표 부분 포팅 —
        // 여기서는 드래그/삭제 등 조작을 지원하지 않는 순수 시각 참고용이다
        // (그 편집은 미션 설정 화면의 몫). MissionObjectivePiece를 미션
        // 설정 화면과 공유하지만, 여기서는 raycastTarget=false로 꺼서
        // 클릭이 아예 안 먹게 한다.

        private void BuildMissionObjectiveVisuals()
        {
            if (!MissionData.HasData)
            {
                return;
            }

            float diameter = GameConstants.MissionObjectiveTokenDiameterMm
                    + 2f * GameConstants.MissionObjectiveCaptureMarginInch * GameConstants.MmPerInch;

            foreach (var objective in MissionData.MissionObjectives)
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
                piece.raycastTarget = false;
            }
        }
    }
}
