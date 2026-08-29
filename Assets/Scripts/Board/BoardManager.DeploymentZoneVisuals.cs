using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 배치구역 가장자리 표시(항상 표시) ─────────────────────────────
        // 미션 설정 화면에서 그은 양 팀 배치구역 선을 게임판에도 항상 그려준다
        // (사용자 요청) — MissionObjectivePiece와 같은 원칙으로 순수 표시용
        // (클릭/드래그 불가, 편집은 미션 설정 화면의 몫)이라 raycastTarget을
        // 끈, 색만 있는 단순 Image 사각형으로 그린다(BuildCapsulePolygon 같은
        // "약통" 모양 배치 밴드와 달리, 여기서는 실제 그은 선 구간 그대로만
        // 보여준다 — 배치 중 임시로 보이는 이동 여유 반영 밴드와는 다른 것).

        private const float DeploymentZoneLineThicknessMm = 6f;

        private readonly Dictionary<string, List<Image>> _deploymentZoneImages = new Dictionary<string, List<Image>>();

        private void BuildDeploymentZoneVisuals()
        {
            if (!MissionData.HasData)
            {
                return;
            }
            foreach (var zone in MissionData.DeploymentZones)
            {
                Vector2 a, b;
                switch (zone.Edge)
                {
                    case "left": a = new Vector2(0f, zone.StartAlong); b = new Vector2(0f, zone.EndAlong); break;
                    case "right": a = new Vector2(mapSizeMm.x, zone.StartAlong); b = new Vector2(mapSizeMm.x, zone.EndAlong); break;
                    case "top": a = new Vector2(zone.StartAlong, 0f); b = new Vector2(zone.EndAlong, 0f); break;
                    default: a = new Vector2(zone.StartAlong, mapSizeMm.y); b = new Vector2(zone.EndAlong, mapSizeMm.y); break;
                }
                Vector2 pa = CornerToCenterMm(a);
                Vector2 pb = CornerToCenterMm(b);
                bool horizontal = zone.Edge == "top" || zone.Edge == "bottom";

                var go = new GameObject($"DeploymentZoneEdge_{zone.Player}", typeof(RectTransform));
                go.transform.SetParent(baseLayer, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = horizontal
                        ? new Vector2(Vector2.Distance(pa, pb), DeploymentZoneLineThicknessMm)
                        : new Vector2(DeploymentZoneLineThicknessMm, Vector2.Distance(pa, pb));
                rt.anchoredPosition = (pa + pb) / 2f;

                var img = go.AddComponent<Image>();
                img.color = GameConstants.TeamColors.TryGetValue(zone.Player, out var c) ? c : Color.white;
                img.raycastTarget = false;

                if (!_deploymentZoneImages.TryGetValue(zone.Player, out var list))
                {
                    list = new List<Image>();
                    _deploymentZoneImages[zone.Player] = list;
                }
                list.Add(img);
            }
        }

        /// <summary>SetTeamColor(BoardManager.TeamColor.cs)가 팀 색을 바꿀 때
        /// 이미 그려둔 배치구역 선도 같이 다시 칠한다 — 색이 안 맞아 어긋나
        /// 보이지 않도록.</summary>
        private void RepaintDeploymentZonesForTeam(string team, Color rawColor)
        {
            if (!_deploymentZoneImages.TryGetValue(team, out var list))
            {
                return;
            }
            foreach (var img in list)
            {
                if (img != null)
                {
                    img.color = rawColor;
                }
            }
        }
    }
}
