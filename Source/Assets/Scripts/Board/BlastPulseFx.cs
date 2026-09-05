using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 블라스트 템플릿 위에서 반복 재생되는 펄스 원 하나(2026-09-06 신설,
    /// 사용자 요청 — "실사 이미지 대신 도형적인 연출로"). 채워진 원 하나만
    /// 그리는 아주 단순한 MaskableGraphic — MissionObjectivePiece.AddFilledCircle과
    /// 같은 팬(fan) 삼각분할 기법이지만, 이쪽은 테두리/라벨 없이 원 하나뿐이라
    /// 별도 헬퍼 없이 이 컴포넌트 자체에 그린다. 크기/투명도는
    /// BoardManager.BlastTemplate.cs가 매 프레임 SetSizeAndAlpha로 갱신한다
    /// (스폰부터 소멸까지 스스로 애니메이션하지 않음 — 순수 그리기 전용).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class BlastPulseFx : MaskableGraphic
    {
        private const int VisualSides = 48;

        public RectTransform RectTransform => (RectTransform)transform;

        protected override void Awake()
        {
            base.Awake();
            RectTransform.pivot = new Vector2(0.5f, 0.5f);
            raycastTarget = false;
        }

        /// <summary>지름(mm)과 알파값을 한 번에 반영한다 — 크기는
        /// RectTransform.sizeDelta(자동으로 OnRectTransformDimensionsChange를
        /// 거쳐 다시 그려짐)로, 알파는 색(다시 그리기를 직접 요청함)으로.</summary>
        public void SetSizeAndAlpha(float diameterMm, float alpha)
        {
            RectTransform.sizeDelta = new Vector2(diameterMm, diameterMm);
            var c = color;
            c.a = alpha;
            color = c;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            float radius = RectTransform.sizeDelta.x / 2f;
            if (radius <= 0f)
            {
                return;
            }

            vh.AddVert(new UIVertex { color = color, position = Vector3.zero });
            for (int i = 0; i < VisualSides; i++)
            {
                float angle = i * Mathf.PI * 2f / VisualSides;
                vh.AddVert(new UIVertex { color = color, position = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f) });
            }
            for (int i = 0; i < VisualSides; i++)
            {
                int next = (i + 1) % VisualSides;
                vh.AddTriangle(0, i + 1, next + 1);
            }
        }
    }
}
