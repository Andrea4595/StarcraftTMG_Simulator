using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>지도 배경에 1인치 간격 참고용 격자선을 그린다. 게임 규칙과는
    /// 무관한 시각 보조선. Godot판 scenes/mission_setup/MapGrid.gd 포팅.</summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class MapGridOverlay : MaskableGraphic
    {
        private const float LineSpacingMm = GameConstants.MmPerInch;
        private const float MajorLineSpacingMm = GameConstants.MmPerInch * 9f;
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color MajorLineColor = new Color(1f, 1f, 1f, 0.35f);

        public RectTransform RectTransform => (RectTransform)transform;

        protected override void Awake()
        {
            base.Awake();
            // 기본 anchorMin/anchorMax는 (0.5,0.5)라서 명시하지 않으면
            // anchoredPosition이 부모 중앙 기준으로 해석된다.
            RectTransform.anchorMin = Vector2.zero;
            RectTransform.anchorMax = Vector2.zero;
            RectTransform.pivot = Vector2.zero;
            raycastTarget = false;
            color = Color.white;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 size = RectTransform.rect.size;
            DrawGrid(vh, size, LineSpacingMm, LineColor, 1f);
            DrawGrid(vh, size, MajorLineSpacingMm, MajorLineColor, 2f);
        }

        private static void DrawGrid(VertexHelper vh, Vector2 size, float spacing, Color color, float width)
        {
            for (float x = 0f; x <= size.x; x += spacing)
            {
                AddLine(vh, new Vector2(x, 0f), new Vector2(x, size.y), color, width);
            }
            for (float y = 0f; y <= size.y; y += spacing)
            {
                AddLine(vh, new Vector2(0f, y), new Vector2(size.x, y), color, width);
            }
        }

        private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 dir = (b - a).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x) * (width / 2f);
            int vi = vh.currentVertCount;
            Vector3 a3 = a;
            Vector3 b3 = b;
            Vector3 normal3 = normal;
            vh.AddVert(new UIVertex { color = color, position = a3 - normal3 });
            vh.AddVert(new UIVertex { color = color, position = a3 + normal3 });
            vh.AddVert(new UIVertex { color = color, position = b3 - normal3 });
            vh.AddVert(new UIVertex { color = color, position = b3 + normal3 });
            vh.AddTriangle(vi, vi + 1, vi + 2);
            vh.AddTriangle(vi + 1, vi + 3, vi + 2);
        }
    }
}
