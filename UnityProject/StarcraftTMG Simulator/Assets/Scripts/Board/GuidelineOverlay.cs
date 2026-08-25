using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 유닛 이동 중 보여주는 참고용 시각 요소 — 이동거리 원, 코헤런시 경계
    /// 폴리곤, 이동 거리/경고 텍스트. Godot판 UnitMoveGuideline.gd 포팅.
    /// 전부 참고용일 뿐 실제 clamp에는 안 쓰인다(그 철학은 로직 쪽에 있다).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class GuidelineOverlay : MaskableGraphic
    {
        private const float LineWidth = 1.5f;
        private const int CircleSides = 64;
        private static readonly Color LineColor = new Color(0.9f, 0.9f, 0.5f, 0.7f);

        private Vector2 _centerMm;
        private float _radiusMm;
        private List<Vector2[]> _bandPolylines = new List<Vector2[]>();
        private TextMeshProUGUI _label;

        public Vector2 CenterMm { get => _centerMm; set { _centerMm = value; SetVerticesDirty(); } }
        public float RadiusMm { get => _radiusMm; set { _radiusMm = value; SetVerticesDirty(); } }

        public List<Vector2[]> BandPolylines
        {
            get => _bandPolylines;
            set { _bandPolylines = value ?? new List<Vector2[]>(); SetVerticesDirty(); }
        }

        public string LabelText
        {
            get => _label != null ? _label.text : "";
            set { EnsureLabel(); _label.text = value ?? ""; _label.gameObject.SetActive(!string.IsNullOrEmpty(value)); }
        }

        public Vector2 LabelPos
        {
            set { EnsureLabel(); ((RectTransform)_label.transform).anchoredPosition = value; }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
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
            rect.sizeDelta = new Vector2(120f, 24f);
            _label = go.AddComponent<TextMeshProUGUI>();
            _label.alignment = TextAlignmentOptions.Center;
            _label.fontSize = 16f;
            _label.color = new Color(1f, 1f, 0.6f, 0.95f);
            _label.raycastTarget = false;
            go.SetActive(false);
        }

        public void ClearBand()
        {
            _radiusMm = 0f;
            _bandPolylines = new List<Vector2[]>();
            LabelText = "";
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (_radiusMm > 0f)
            {
                var circle = new Vector2[CircleSides];
                for (int i = 0; i < CircleSides; i++)
                {
                    float angle = i * Mathf.PI * 2f / CircleSides;
                    circle[i] = _centerMm + new Vector2(Mathf.Cos(angle) * _radiusMm, Mathf.Sin(angle) * _radiusMm);
                }
                AddPolyline(vh, circle, true);
            }

            foreach (var band in _bandPolylines)
            {
                AddPolyline(vh, band, false);
            }
        }

        private static void AddPolyline(VertexHelper vh, Vector2[] points, bool closed)
        {
            int count = points.Length;
            if (count < 2)
            {
                return;
            }
            int segments = closed ? count : count - 1;
            float half = LineWidth / 2f;
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % count];
                Vector2 dir = (b - a).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x) * half;

                int vi = vh.currentVertCount;
                Vector3 a3 = a;
                Vector3 b3 = b;
                Vector3 normal3 = normal;
                vh.AddVert(new UIVertex { color = LineColor, position = a3 - normal3 });
                vh.AddVert(new UIVertex { color = LineColor, position = a3 + normal3 });
                vh.AddVert(new UIVertex { color = LineColor, position = b3 - normal3 });
                vh.AddVert(new UIVertex { color = LineColor, position = b3 + normal3 });
                vh.AddTriangle(vi, vi + 1, vi + 2);
                vh.AddTriangle(vi + 1, vi + 3, vi + 2);
            }
        }
    }
}
