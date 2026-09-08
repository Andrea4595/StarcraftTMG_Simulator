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
    ///
    /// 배치구역 밴드(BandPolylines)가 여러 개고(같은 팀의 인접한 구역들)
    /// 서로 겹칠 때는 Godot의 Geometry2D.merge_polygons 같은 실제 다각형
    /// 합치기 대신, RangeOverlay의 범위 겹침 처리와 똑같은 트릭을 쓴다 —
    /// 각 밴드를 그리기 전에 "다른 밴드 다각형 안"에 들어가는 구간만 빼고
    /// 그린다(AddPolylineExcludingOverlap, 클리핑은 공용 유틸
    /// EllipseMath.TryClipSegmentToConvexPolygon). 실제로 합쳐진 다각형은
    /// 아니지만 겹치는 자리에 이중선이 안 보여서 시각적으로는 병합된 것
    /// 처럼 보인다.
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
        // 유닛 이동 웨이포인트 경로 전용 — BandPolylines와 달리 서로 겹침
        // 클리핑 없이(각 구간이 독립적인 선분) 그냥 그대로 그린다. 각 원소는
        // 2점짜리 배열(테두리에서 테두리로 다듬어진 한 구간).
        private List<Vector2[]> _pathSegments = new List<Vector2[]>();
        // 상대(원격) 플레이어가 지금 웨이포인트로 옮기고 있는 경로 — 로컬
        // PathSegments와 완전히 독립적인 별도 목록이다(둘 다 동시에 활성일
        // 수 있으므로 서로 덮어쓰면 안 된다, 사용자 요청: 상대 이동/가이드
        // 라인도 공유).
        private List<Vector2[]> _remotePathSegments = new List<Vector2[]>();
        // 상대의 남은 이동력 테두리 — 로컬 BandPolylines와 완전히 독립적인
        // 별도 목록(둘 다 동시에 활성일 수 있음, 사용자 요청: 이동력 링도
        // 공유).
        private List<Vector2[]> _remoteBandPolylines = new List<Vector2[]>();
        private TextMeshProUGUI _label;

        public Vector2 CenterMm { get => _centerMm; set { _centerMm = value; SetVerticesDirty(); } }
        public float RadiusMm { get => _radiusMm; set { _radiusMm = value; SetVerticesDirty(); } }

        public List<Vector2[]> BandPolylines
        {
            get => _bandPolylines;
            set { _bandPolylines = value ?? new List<Vector2[]>(); SetVerticesDirty(); }
        }

        public List<Vector2[]> PathSegments
        {
            get => _pathSegments;
            set { _pathSegments = value ?? new List<Vector2[]>(); SetVerticesDirty(); }
        }

        public List<Vector2[]> RemotePathSegments
        {
            get => _remotePathSegments;
            set { _remotePathSegments = value ?? new List<Vector2[]>(); SetVerticesDirty(); }
        }

        public List<Vector2[]> RemoteBandPolylines
        {
            get => _remoteBandPolylines;
            set { _remoteBandPolylines = value ?? new List<Vector2[]>(); SetVerticesDirty(); }
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
            _pathSegments = new List<Vector2[]>();
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

            int bandCount = _bandPolylines.Count;
            for (int bi = 0; bi < bandCount; bi++)
            {
                var band = _bandPolylines[bi];
                if (bandCount > 1)
                {
                    var others = new List<Vector2[]>(bandCount - 1);
                    for (int bj = 0; bj < bandCount; bj++)
                    {
                        if (bj != bi)
                        {
                            others.Add(_bandPolylines[bj]);
                        }
                    }
                    AddPolylineExcludingOverlap(vh, band, others);
                }
                else
                {
                    AddPolyline(vh, band, false);
                }
            }

            foreach (var seg in _pathSegments)
            {
                if (seg.Length >= 2)
                {
                    AddLineQuad(vh, seg[0], seg[1], LineWidth / 2f);
                }
            }

            foreach (var seg in _remotePathSegments)
            {
                if (seg.Length >= 2)
                {
                    AddLineQuad(vh, seg[0], seg[1], LineWidth / 2f);
                }
            }

            foreach (var band in _remoteBandPolylines)
            {
                AddPolyline(vh, band, false);
            }
        }

        /// <summary>ClearBand()와 별개다 — ClearBand()는 로컬 이동이 끝날 때
        /// (BandPolylines/LabelText/PathSegments) 쓰고, 이건 원격(상대)
        /// 이동이 끝났다는 방송을 받았을 때만 쓴다. 서로 독립적이어야
        /// 한쪽이 끝나도 다른 쪽 시각 요소가 안 지워진다.</summary>
        public void ClearRemoteBand()
        {
            _remotePathSegments = new List<Vector2[]>();
            _remoteBandPolylines = new List<Vector2[]>();
            SetVerticesDirty();
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
                AddLineQuad(vh, a, b, half);
            }
        }

        /// <summary>다른 밴드 다각형들 안쪽에 들어가는 구간은 빼고 그리는
        /// 폴리라인 — 여러 배치구역 밴드가 마치 하나로 병합된 것처럼 보이게
        /// 한다(RangeOverlay.AddDashedPolylineExcludingOverlap과 같은 원리,
        /// 여긴 점선이 아니라 실선이라 점선 리듬 계산만 뺐다). 마지막 구간
        /// (BoardManager.Roster.cs의 BuildCapsulePolygon이 만드는 두 호(arc)
        /// 끝점을 지도 가장자리를 따라 잇는, 밴드를 "닫는" 구간)만은 예외로
        /// 클리핑 없이 항상 그린다 — 인접한 밴드끼리 지도 테두리 쪽에서 서로
        /// 겹치면 이 구간이 서로를 지워버려서 정작 지도 테두리 선 자체가
        /// 안 보이는 문제가 있었다.</summary>
        private static void AddPolylineExcludingOverlap(VertexHelper vh, Vector2[] points, List<Vector2[]> otherPolygons)
        {
            if (points.Length < 2)
            {
                return;
            }
            int closingSegment = points.Length - 2;
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                if (i == closingSegment)
                {
                    AddLineQuad(vh, a, b, LineWidth / 2f);
                    continue;
                }
                float segLen = Vector2.Distance(a, b);
                if (segLen < 0.0001f)
                {
                    continue;
                }
                Vector2 dir = (b - a) / segLen;

                var insideIntervals = new List<(float From, float To)>();
                foreach (var other in otherPolygons)
                {
                    if (EllipseMath.TryClipSegmentToConvexPolygon(a, b, other, out float tEnter, out float tExit))
                    {
                        insideIntervals.Add((tEnter, tExit));
                    }
                }
                var outsideIntervals = EllipseMath.ComplementIntervals(insideIntervals);
                foreach (var (from, to) in outsideIntervals)
                {
                    if (to - from < 0.0001f)
                    {
                        continue;
                    }
                    AddLineQuad(vh, a + dir * (from * segLen), a + dir * (to * segLen), LineWidth / 2f);
                }
            }
        }

        private static void AddLineQuad(VertexHelper vh, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 dir = (b - a).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x) * halfWidth;
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
