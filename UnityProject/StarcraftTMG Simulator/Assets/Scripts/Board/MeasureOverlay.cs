using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// '거리 재기' 도구 오버레이 — 스페이스바를 누르고 있는 동안 시작점부터
    /// 지금 마우스 위치(또는 그 아래 베이스)까지 선을 긋고 거리를 인치로
    /// 표시한다. Godot판 MeasureOverlay.gd 포팅.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class MeasureOverlay : MaskableGraphic
    {
        private const float LineWidth = 2f;
        private const float CircleRadius = 3f;
        private const int CircleSides = 24;
        private static readonly Color LineColor = new Color(1f, 0.95f, 0.3f, 0.9f);

        private bool _lineVisible;
        private Vector2 _fromPoint;
        private Vector2 _toPoint;
        private TextMeshProUGUI _label;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
        }

        public void SetLine(Vector2 from, Vector2 to, string labelText)
        {
            _lineVisible = true;
            _fromPoint = from;
            _toPoint = to;

            EnsureLabel();
            _label.text = labelText ?? "";
            _label.gameObject.SetActive(!string.IsNullOrEmpty(labelText));
            ((RectTransform)_label.transform).anchoredPosition = (from + to) / 2f + new Vector2(0f, 14f);

            SetVerticesDirty();
        }

        public void Hide()
        {
            _lineVisible = false;
            if (_label != null)
            {
                _label.gameObject.SetActive(false);
            }
            SetVerticesDirty();
        }

        private void EnsureLabel()
        {
            if (_label != null)
            {
                return;
            }
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(80f, 24f);
            _label = go.AddComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 16f;
            _label.color = Color.white;
            _label.raycastTarget = false;
            go.SetActive(false);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (!_lineVisible)
            {
                return;
            }

            AddLineQuad(vh, _fromPoint, _toPoint, LineColor, LineWidth / 2f);
            AddCircle(vh, _fromPoint, CircleRadius, LineColor);
            AddCircle(vh, _toPoint, CircleRadius, LineColor);
        }

        private static void AddCircle(VertexHelper vh, Vector2 center, float radius, Color color)
        {
            int baseIndex = vh.currentVertCount;
            vh.AddVert(new UIVertex { color = color, position = center });
            for (int i = 0; i < CircleSides; i++)
            {
                float angle = i * Mathf.PI * 2f / CircleSides;
                Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                vh.AddVert(new UIVertex { color = color, position = p });
            }
            for (int i = 0; i < CircleSides; i++)
            {
                int next = (i + 1) % CircleSides;
                vh.AddTriangle(baseIndex, baseIndex + i + 1, baseIndex + next + 1);
            }
        }

        private static void AddLineQuad(VertexHelper vh, Vector2 a, Vector2 b, Color color, float halfWidth)
        {
            Vector2 seg = b - a;
            if (seg.sqrMagnitude < 0.0001f)
            {
                return;
            }
            Vector2 dir = seg.normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x) * halfWidth;
            int vi = vh.currentVertCount;
            vh.AddVert(new UIVertex { color = color, position = a - normal });
            vh.AddVert(new UIVertex { color = color, position = a + normal });
            vh.AddVert(new UIVertex { color = color, position = b - normal });
            vh.AddVert(new UIVertex { color = color, position = b + normal });
            vh.AddTriangle(vi, vi + 1, vi + 2);
            vh.AddTriangle(vi + 1, vi + 3, vi + 2);
        }
    }
}
