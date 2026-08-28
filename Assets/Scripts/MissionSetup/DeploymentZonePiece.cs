using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 지도 가장자리를 따라 그어진 배치구역 선(구간) 하나. Godot판
    /// scenes/mission_setup/DeploymentZonePiece.gd 포팅. 이동/회전은 지원하지
    /// 않는다(다시 그려서 대체) — 우클릭하면 바로 삭제된다.
    ///
    /// pivot=(0,0)으로 둬서 anchoredPosition/sizeDelta가 곧 Godot Control의
    /// position/size와 같은 의미가 되게 한다(Base.cs의 중심-피벗 트릭과 달리,
    /// 여기서는 좌상단 기준이 원본과 그대로 대응된다).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class DeploymentZonePiece : MaskableGraphic, IPointerDownHandler
    {
        public string OwnerPlayer = "";
        public Color LineColor = Color.white;
        public float VisualThicknessMm = 6f;

        /// <summary>게임 화면 핸드오프용 데이터: 어느 변의, 어느 구간인지.
        /// edge: "left"/"right"/"top"/"bottom". along 값은 그 변을 따라 잰
        /// 좌표(left/right는 y, top/bottom은 x), 지도 로컬 mm 기준.</summary>
        public string Edge = "";
        public float StartAlong;
        public float EndAlong;

        public RectTransform RectTransform => (RectTransform)transform;

        protected override void Awake()
        {
            base.Awake();
            // 기본 anchorMin/anchorMax는 (0.5,0.5)라서 안 건드리면
            // anchoredPosition이 부모 "중앙" 기준으로 해석된다 — 부모(ZoneLayer)
            // 좌하단(코너) 기준으로 쓰려면 반드시 (0,0)으로 명시해야 한다.
            RectTransform.anchorMin = Vector2.zero;
            RectTransform.anchorMax = Vector2.zero;
            RectTransform.pivot = Vector2.zero;
            color = Color.white; // 실제 색은 정점 색(LineColor)이 담당한다.
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 size = RectTransform.rect.size;
            Rect r;
            if (size.x >= size.y)
            {
                float y = (size.y - VisualThicknessMm) / 2f;
                r = new Rect(0f, y, size.x, VisualThicknessMm);
            }
            else
            {
                float x = (size.x - VisualThicknessMm) / 2f;
                r = new Rect(x, 0f, VisualThicknessMm, size.y);
            }
            AddQuad(vh, r, LineColor);
        }

        private static void AddQuad(VertexHelper vh, Rect r, Color c)
        {
            int vi = vh.currentVertCount;
            vh.AddVert(new UIVertex { color = c, position = new Vector3(r.xMin, r.yMin) });
            vh.AddVert(new UIVertex { color = c, position = new Vector3(r.xMin, r.yMax) });
            vh.AddVert(new UIVertex { color = c, position = new Vector3(r.xMax, r.yMax) });
            vh.AddVert(new UIVertex { color = c, position = new Vector3(r.xMax, r.yMin) });
            vh.AddTriangle(vi, vi + 1, vi + 2);
            vh.AddTriangle(vi, vi + 2, vi + 3);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                Destroy(gameObject);
                eventData.Use();
            }
        }
    }
}
