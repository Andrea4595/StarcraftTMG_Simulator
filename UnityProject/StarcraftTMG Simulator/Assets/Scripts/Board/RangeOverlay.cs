using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>범위 표시 오버레이에 그릴 항목 하나 — 어떤 유닛의 한 사거리에
    /// 대응하는, 그 유닛 모델들 각각의 오프셋 다각형(들) + 라벨.</summary>
    public class RangeOverlayEntry
    {
        public readonly List<Vector2[]> Polygons = new List<Vector2[]>();
        public string LabelText = "";
        public Vector2 LabelPos;
        public bool Hovered;
    }

    public enum RangeOverlayMode
    {
        Fill,
        Outline,
    }

    /// <summary>
    /// '범위 표시' 오버레이 — 유닛에 등록된 각 사거리(인치)마다, 그 유닛의 모든
    /// 모델 베이스 테두리로부터 그 거리만큼 떨어진 다각형을 그린다. Godot판
    /// RangeOverlay.gd 포팅 — 단, 상시 배경 채우기는 Godot판과 달리 뺐다(사용자
    /// 요청: 배경색이 늘 켜져 있는 대신, 마우스가 올라간 모델의 범위만 강조
    /// 채우기로 보여준다). Fill 인스턴스는 이제 그 강조 채우기 전용이고,
    /// Outline 인스턴스는 여전히 점선 테두리+거리 라벨(상시 표시 항목은
    /// 계속 항상 보인다 — 이건 배경색이 아니라 테두리라 그대로 둔다).
    ///
    /// Godot의 Geometry2D.merge_polygons(같은 유닛의 여러 모델 다각형을 실제
    /// 다각형 하나로 합치는 것)는 포팅하지 않았다 — Unity에 다각형 불리언
    /// 유틸이 없어서(미션설정 배치구역 포팅 때와 같은 이유). 대신 Outline은
    /// 각 변을 그리기 전에 "같은 항목의 다른 모델 다각형 안"에 들어가는
    /// 구간을 골라내 그 부분만 빼고 그린다(AddDashedPolylineExcludingOverlap)
    /// — 실제로 하나의 다각형으로 합친 건 아니지만, 겹치는 자리에는 선이
    /// 안 보이므로 시각적으로는 병합된 것처럼 보인다. 이 클리핑은 각
    /// 다각형이 볼록(convex)이라고 가정한다 — 타원 오프셋 다각형은 거의
    /// 항상 볼록하지만, 아주 길쭉한 타원을 아주 큰 거리로 오프셋하면 끝
    /// 부분이 살짝 오목해질 수 있다(그런 극단적인 경우는 클리핑이 살짝
    /// 부정확할 수 있음 — 감수할 만한 시각적 근사).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class RangeOverlay : MaskableGraphic
    {
        private static readonly Color HighlightFillColor = new Color(0.85f, 0.85f, 0.85f, 0.15f);
        private static readonly Color OutlineColor = new Color(0.85f, 0.85f, 0.85f, 0.45f);
        private static readonly Color HoverOutlineColor = new Color(1f, 0.85f, 0.15f, 0.9f);
        private static readonly Color LabelColor = new Color(0.95f, 0.95f, 0.95f, 0.8f);
        private static readonly Color HoverLabelColor = new Color(1f, 0.9f, 0.3f, 0.95f);
        private const float DashLength = 6f;
        private const float GapLength = 5f;
        private const float LineWidth = 1.5f;

        public RangeOverlayMode Mode = RangeOverlayMode.Fill;

        private List<RangeOverlayEntry> _entries = new List<RangeOverlayEntry>();
        private List<Vector2[]> _highlightPolygons = new List<Vector2[]>();
        private readonly List<TextMeshProUGUI> _labelPool = new List<TextMeshProUGUI>();

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            color = Color.white;
        }

        public void SetEntries(List<RangeOverlayEntry> entries)
        {
            _entries = entries ?? new List<RangeOverlayEntry>();
            if (Mode == RangeOverlayMode.Outline)
            {
                RefreshLabels();
            }
            SetVerticesDirty();
        }

        /// <summary>Fill 모드에서만 쓰는, 지금 마우스가 올라간 모델 하나만의
        /// (병합 없는) 범위 강조 채우기.</summary>
        public void SetHighlightPolygons(List<Vector2[]> polygons)
        {
            _highlightPolygons = polygons ?? new List<Vector2[]>();
            SetVerticesDirty();
        }

        private void RefreshLabels()
        {
            EnsureLabelPoolSize(_entries.Count);
            for (int i = 0; i < _labelPool.Count; i++)
            {
                if (i < _entries.Count && !string.IsNullOrEmpty(_entries[i].LabelText))
                {
                    var entry = _entries[i];
                    _labelPool[i].gameObject.SetActive(true);
                    _labelPool[i].text = entry.LabelText;
                    _labelPool[i].color = entry.Hovered ? HoverLabelColor : LabelColor;
                    ((RectTransform)_labelPool[i].transform).anchoredPosition = entry.LabelPos;
                }
                else
                {
                    _labelPool[i].gameObject.SetActive(false);
                }
            }
        }

        private void EnsureLabelPoolSize(int count)
        {
            while (_labelPool.Count < count)
            {
                var go = new GameObject("RangeLabel", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(80f, 20f);
                var label = go.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = 14f;
                label.raycastTarget = false;
                _labelPool.Add(label);
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (Mode == RangeOverlayMode.Fill)
            {
                foreach (var polygon in _highlightPolygons)
                {
                    AddFilledPolygon(vh, polygon, HighlightFillColor);
                }
                return;
            }

            foreach (var entry in _entries)
            {
                var outlineColor = entry.Hovered ? HoverOutlineColor : OutlineColor;
                int count = entry.Polygons.Count;
                for (int pi = 0; pi < count; pi++)
                {
                    var polygon = entry.Polygons[pi];
                    if (polygon.Length < 2)
                    {
                        continue;
                    }
                    var closed = new Vector2[polygon.Length + 1];
                    polygon.CopyTo(closed, 0);
                    closed[polygon.Length] = polygon[0];

                    if (count > 1)
                    {
                        var others = new List<Vector2[]>(count - 1);
                        for (int pj = 0; pj < count; pj++)
                        {
                            if (pj != pi)
                            {
                                others.Add(entry.Polygons[pj]);
                            }
                        }
                        AddDashedPolylineExcludingOverlap(vh, closed, outlineColor, others);
                    }
                    else
                    {
                        AddDashedPolyline(vh, closed, outlineColor);
                    }
                }
            }
        }

        /// <summary>임의의 별모양(중심 기준 star-convex) 다각형을 중심점에서
        /// 팬(fan) 삼각분할로 채운다 — Base.cs가 자기 타원을 채우는 것과 같은
        /// 기법. 타원 오프셋 다각형은 항상 이 조건을 만족한다.</summary>
        private static void AddFilledPolygon(VertexHelper vh, Vector2[] polygon, Color color)
        {
            if (polygon.Length < 3)
            {
                return;
            }
            Vector2 centroid = Vector2.zero;
            foreach (var p in polygon)
            {
                centroid += p;
            }
            centroid /= polygon.Length;

            int baseIndex = vh.currentVertCount;
            vh.AddVert(new UIVertex { color = color, position = centroid });
            for (int i = 0; i < polygon.Length; i++)
            {
                vh.AddVert(new UIVertex { color = color, position = polygon[i] });
            }
            for (int i = 0; i < polygon.Length; i++)
            {
                int next = (i + 1) % polygon.Length;
                vh.AddTriangle(baseIndex, baseIndex + i + 1, baseIndex + next + 1);
            }
        }

        /// <summary>폴리라인 전체를 따라 누적된 거리를 기준으로 점선 on/off
        /// 구간을 정하므로, 선분 경계에서도 점선 리듬이 끊기지 않는다.</summary>
        private static void AddDashedPolyline(VertexHelper vh, Vector2[] points, Color color)
        {
            const float period = DashLength + GapLength;
            float half = LineWidth / 2f;
            float cumulative = 0f;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                float segLen = Vector2.Distance(a, b);
                if (segLen < 0.0001f)
                {
                    continue;
                }
                Vector2 dir = (b - a) / segLen;
                float traveled = 0f;
                while (traveled < segLen)
                {
                    float phase = (cumulative + traveled) % period;
                    bool dashOn = phase < DashLength;
                    float step = dashOn ? (DashLength - phase) : (period - phase);
                    step = Mathf.Min(step, segLen - traveled);
                    if (dashOn)
                    {
                        AddLineQuad(vh, a + dir * traveled, a + dir * (traveled + step), color, half);
                    }
                    traveled += step;
                }
                cumulative += segLen;
            }
        }

        /// <summary>같은 항목(entry)의 다른 모델 다각형들과 겹치는 구간은 빼고
        /// 그리는 점선 폴리라인 — 여러 모델의 범위가 마치 하나로 병합된 것처럼
        /// 보이게 한다. 점선의 리듬(누적 거리 기준 on/off)은 그대로 유지한 채,
        /// 각 "켜짐" 구간을 그리기 직전에 다른 다각형들 안쪽 부분만 잘라낸다.</summary>
        private static void AddDashedPolylineExcludingOverlap(VertexHelper vh, Vector2[] points, Color color, List<Vector2[]> otherPolygons)
        {
            const float period = DashLength + GapLength;
            float half = LineWidth / 2f;
            float cumulative = 0f;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                float segLen = Vector2.Distance(a, b);
                if (segLen < 0.0001f)
                {
                    continue;
                }
                Vector2 dir = (b - a) / segLen;

                var insideIntervals = new List<(float From, float To)>();
                foreach (var other in otherPolygons)
                {
                    if (TryClipSegmentToConvexPolygon(a, b, other, out float tEnter, out float tExit))
                    {
                        insideIntervals.Add((tEnter, tExit));
                    }
                }
                var outsideIntervals = ComplementIntervals(insideIntervals);

                float traveled = 0f;
                while (traveled < segLen)
                {
                    float phase = (cumulative + traveled) % period;
                    bool dashOn = phase < DashLength;
                    float step = dashOn ? (DashLength - phase) : (period - phase);
                    step = Mathf.Min(step, segLen - traveled);
                    if (dashOn)
                    {
                        float t0 = traveled / segLen;
                        float t1 = (traveled + step) / segLen;
                        foreach (var (from, to) in outsideIntervals)
                        {
                            float lo = Mathf.Max(t0, from);
                            float hi = Mathf.Min(t1, to);
                            if (lo < hi)
                            {
                                AddLineQuad(vh, a + dir * (lo * segLen), a + dir * (hi * segLen), color, half);
                            }
                        }
                    }
                    traveled += step;
                }
                cumulative += segLen;
            }
        }

        /// <summary>선분(a→b)이 볼록 다각형 polygon 내부에 들어가는 매개변수
        /// t구간 [tEnter,tExit](0~1 기준)을 구한다 — Cyrus-Beck 볼록 다각형
        /// 선분 클리핑. polygon은 반시계 방향(CCW)이라고 가정한다(타원 오프셋
        /// 다각형 생성 순서가 항상 CCW).</summary>
        private static bool TryClipSegmentToConvexPolygon(Vector2 a, Vector2 b, Vector2[] polygon, out float tEnter, out float tExit)
        {
            tEnter = 0f;
            tExit = 1f;
            Vector2 ab = b - a;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 e0 = polygon[i];
                Vector2 e1 = polygon[(i + 1) % polygon.Length];
                Vector2 edge = e1 - e0;
                Vector2 outwardNormal = new Vector2(edge.y, -edge.x);

                float w = Vector2.Dot(a - e0, outwardNormal);
                float d = Vector2.Dot(ab, outwardNormal);
                if (Mathf.Abs(d) < 1e-8f)
                {
                    if (w > 0f)
                    {
                        return false; // 이 변과 평행하면서 완전히 바깥쪽 — 다각형과 만나지 않는다.
                    }
                    continue;
                }
                float t = -w / d;
                if (d < 0f)
                {
                    if (t > tEnter)
                    {
                        tEnter = t;
                    }
                }
                else
                {
                    if (t < tExit)
                    {
                        tExit = t;
                    }
                }
                if (tEnter > tExit)
                {
                    return false;
                }
            }
            return tEnter < tExit;
        }

        /// <summary>[0,1] 구간에서 insides(다른 다각형 안쪽 구간들, 겹치거나
        /// 순서 없어도 됨)를 뺀 나머지("바깥쪽") 구간들을 돌려준다.</summary>
        private static List<(float From, float To)> ComplementIntervals(List<(float From, float To)> insides)
        {
            if (insides.Count == 0)
            {
                return new List<(float, float)> { (0f, 1f) };
            }
            insides.Sort((x, y) => x.From.CompareTo(y.From));
            var merged = new List<(float From, float To)>();
            foreach (var iv in insides)
            {
                if (merged.Count > 0 && iv.From <= merged[merged.Count - 1].To)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.From, Mathf.Max(last.To, iv.To));
                }
                else
                {
                    merged.Add(iv);
                }
            }

            var outside = new List<(float From, float To)>();
            float cursor = 0f;
            foreach (var iv in merged)
            {
                if (iv.From > cursor)
                {
                    outside.Add((cursor, iv.From));
                }
                cursor = Mathf.Max(cursor, iv.To);
            }
            if (cursor < 1f)
            {
                outside.Add((cursor, 1f));
            }
            return outside;
        }

        private static void AddLineQuad(VertexHelper vh, Vector2 a, Vector2 b, Color color, float halfWidth)
        {
            Vector2 dir = (b - a).normalized;
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
