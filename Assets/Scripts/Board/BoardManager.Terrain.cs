using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 지형(순수 표시용) ────────────────────────────────────────
        // 미션 설정 화면에서 놓은 지형을 그대로 재현한다. 물리/충돌은 없다
        // (고지대 통행 규칙 등은 사람이 직접 판정 — 이 프로젝트의 방침).
        // TerrainPiece를 미션 설정 화면과 공유하지만, 여기서는
        // raycastTarget=false로 꺼서 클릭이 아예 안 먹게 한다. 전용
        // terrainLayer에 심는다(baseLayer가 아님 — 지도 배경 바로 위,
        // 유닛/범위 표시보다는 아래에 깔리도록 z-order를 분리).

        private void BuildTerrainVisuals()
        {
            if (!MapData.HasData || terrainLayer == null)
            {
                return;
            }

            foreach (var piece in MapData.TerrainPieces)
            {
                var module = TerrainCatalog.Get(piece.ModuleId);
                if (module == null)
                {
                    continue;
                }

                var go = new GameObject($"Terrain_{module.Id}", typeof(RectTransform));
                go.transform.SetParent(terrainLayer, false);
                var terrainPiece = go.AddComponent<TerrainPiece>();
                terrainPiece.RectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                terrainPiece.RectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                terrainPiece.Setup(module);
                terrainPiece.Center = CornerToCenterMm(piece.Position);
                terrainPiece.RotationDegrees = piece.RotationDeg;
                terrainPiece.raycastTarget = false;
            }
        }
    }
}
