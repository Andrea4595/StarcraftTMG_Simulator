using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 회전된 타원 베이스끼리의 겹침 판정/밀어내기(SAT+MTV), 방향별 정확한
    /// 타원 반지름, 코헤런시 판정에 쓰는 오프셋 폴리곤 등 순수 수학 유틸리티.
    /// Godot 버전 GameBoard.gd의 동명 함수들을 그대로 포팅한 것 — 1mm = 1
    /// world unit 규칙도 그대로 유지한다(Vector2 값은 전부 mm 기준).
    /// </summary>
    public interface IEllipseBody
    {
        Vector2 SizeMm { get; }
        float RotationRadians { get; }
        bool IsDisplacement { get; }
    }

    public static class EllipseMath
    {
        public const int CollisionSides = 64;
        public const int CollisionIterations = 8;

        public static float BoundingRadius(Vector2 sizeMm)
        {
            return Mathf.Max(sizeMm.x, sizeMm.y) / 2f;
        }

        private static Vector2 Rotate(Vector2 v, float radians)
        {
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        /// <summary>centerPt를 중심으로 하는, rot(라디안)만큼 회전된 타원의 다각형 근사.</summary>
        public static Vector2[] EllipsePolygonAt(Vector2 centerPt, Vector2 sizeMm, float rot)
        {
            float rx = sizeMm.x / 2f;
            float ry = sizeMm.y / 2f;
            var points = new Vector2[CollisionSides];
            for (int i = 0; i < CollisionSides; i++)
            {
                float angle = i * Mathf.PI * 2f / CollisionSides;
                var local = Rotate(new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry), rot);
                points[i] = centerPt + local;
            }
            return points;
        }

        /// <summary>
        /// 타원 테두리에서 바깥으로 offsetMm만큼 고르게 떨어진 곡선의 다각형 근사.
        /// 반지름을 단순히 늘리는 게 아니라 각 점의 실제 바깥 법선 방향으로
        /// 밀어야 "테두리로부터 X만큼"이 방향과 무관하게 일정해진다.
        /// </summary>
        public static Vector2[] EllipseOffsetPolygonAt(Vector2 centerPt, Vector2 sizeMm, float rot, float offsetMm)
        {
            float rx = sizeMm.x / 2f;
            float ry = sizeMm.y / 2f;
            var points = new Vector2[CollisionSides];
            for (int i = 0; i < CollisionSides; i++)
            {
                float angle = i * Mathf.PI * 2f / CollisionSides;
                var localPoint = new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry);
                var normal = new Vector2(Mathf.Cos(angle) / rx, Mathf.Sin(angle) / ry).normalized;
                var offsetPoint = localPoint + normal * offsetMm;
                points[i] = centerPt + Rotate(offsetPoint, rot);
            }
            return points;
        }

        /// <summary>
        /// (회전된) 타원의 중심에서 worldDir 방향으로 잰, 그 방향의 테두리까지의
        /// 정확한 거리. 타원은 중심 대칭이라 worldDir과 -worldDir의 결과가 같다.
        /// </summary>
        public static float EllipseRadiusInDirection(Vector2 sizeMm, float rot, Vector2 worldDir)
        {
            float rx = sizeMm.x / 2f;
            float ry = sizeMm.y / 2f;
            var localDir = Rotate(worldDir, -rot);
            float denom = Mathf.Sqrt(Mathf.Pow(localDir.x / rx, 2f) + Mathf.Pow(localDir.y / ry, 2f));
            if (denom < 0.0001f)
            {
                return Mathf.Max(rx, ry);
            }
            return 1f / denom;
        }

        /// <summary>
        /// (회전된) 타원의 중심에서, 법선이 worldDir인 직선에 접하는 지점까지의
        /// 거리(지지함수/support function) — "이 타원이 worldDir 방향의 직선을
        /// 넘지 않으려면 중심이 그 선에서 최소 얼마나 떨어져야 하는가"에 대한
        /// 정확한 답. EllipseRadiusInDirection(중심에서 worldDir 방향으로 그은
        /// 반직선이 타원 테두리와 만나는 점까지의 거리)과는 다른 값이다 — 두
        /// 함수는 worldDir이 타원의 주축(장축/단축)과 정확히 일치할 때만 같고,
        /// 그 사이 각도에서는 지지함수가 항상 더 크다(반직선 위의 점에서의
        /// 접선은 일반적으로 worldDir에 수직이 아니기 때문). 직선 경계(배치
        /// 밴드/이동거리 곡선)에 스냅/클램프할 땐 반드시 이 함수를 써야
        /// 한다 — 반직선 거리를 쓰면 회전된 타원이 대각선 방향에서 실제보다
        /// 짧게 잡혀 경계 밖으로 살짝 튀어나간다(첫 시도에서 확인된 버그).
        /// 공식: 타원 로컬 좌표계에서 방향 (dx,dy)(단위벡터)의 지지함수는
        /// sqrt((rx*dx)² + (ry*dy)²) — a·cosθ+b·sinθ의 최댓값이 sqrt(a²+b²)라는
        /// 항등식에서 바로 나온다.
        /// </summary>
        public static float EllipseSupportInDirection(Vector2 sizeMm, float rot, Vector2 worldDir)
        {
            float rx = sizeMm.x / 2f;
            float ry = sizeMm.y / 2f;
            var localDir = Rotate(worldDir, -rot).normalized;
            return Mathf.Sqrt(Mathf.Pow(rx * localDir.x, 2f) + Mathf.Pow(ry * localDir.y, 2f));
        }

        public static bool PointInConvexPolygon(Vector2 point, Vector2[] polygon)
        {
            float signRef = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                Vector2 edge = b - a;
                Vector2 toPoint = point - a;
                float cross = edge.x * toPoint.y - edge.y * toPoint.x;
                if (i == 0)
                {
                    signRef = cross;
                }
                else if (cross * signRef < -0.0001f)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>선분(a→b)이 볼록 다각형 polygon 내부에 들어가는 매개변수
        /// t구간 [tEnter,tExit](0~1 기준)을 구한다 — Cyrus-Beck 볼록 다각형
        /// 선분 클리핑. 다각형의 감김 방향(CW/CCW)에 무관하게 동작한다(부호
        /// 있는 넓이로 자동 판별) — 겹치는 다각형 무리를 실제로 하나로 합치는
        /// 대신, 다른 다각형 안쪽에 들어가는 구간만 그리지 않아서 "병합된
        /// 것처럼" 보이게 하는 트릭에 쓴다(범위 표시 겹침, 배치구역 밴드
        /// 겹침 양쪽에서 재사용).</summary>
        public static bool TryClipSegmentToConvexPolygon(Vector2 a, Vector2 b, Vector2[] polygon, out float tEnter, out float tExit)
        {
            tEnter = 0f;
            tExit = 1f;
            if (polygon.Length < 3)
            {
                return false;
            }
            float windingSign = SignedArea(polygon) >= 0f ? 1f : -1f;
            Vector2 ab = b - a;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 e0 = polygon[i];
                Vector2 e1 = polygon[(i + 1) % polygon.Length];
                Vector2 edge = e1 - e0;
                Vector2 outwardNormal = new Vector2(edge.y, -edge.x) * windingSign;

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

        private static float SignedArea(Vector2[] polygon)
        {
            float sum = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum;
        }

        /// <summary>[0,1] 구간에서 insides(다른 다각형 안쪽 구간들, 겹치거나
        /// 순서 없어도 됨)를 뺀 나머지("바깥쪽") 구간들을 돌려준다.</summary>
        public static List<(float From, float To)> ComplementIntervals(List<(float From, float To)> insides)
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

        /// <summary>
        /// 분리축 정리(SAT)로 두 볼록 다각형이 겹치는지 확인하고, 겹친다면 a를
        /// b로부터 밀어낼 최소 이동 벡터(MTV)를 돌려준다. 안 겹치면 null.
        /// </summary>
        public static Vector2? PolygonOverlapMtv(Vector2[] polyA, Vector2 centerA, Vector2[] polyB, Vector2 centerB)
        {
            float minOverlap = float.PositiveInfinity;
            Vector2 minAxis = Vector2.zero;

            foreach (var poly in new[] { polyA, polyB })
            {
                int count = poly.Length;
                for (int i = 0; i < count; i++)
                {
                    Vector2 p1 = poly[i];
                    Vector2 p2 = poly[(i + 1) % count];
                    Vector2 edge = p2 - p1;
                    if (edge.sqrMagnitude < 0.0001f)
                    {
                        continue;
                    }
                    var axis = new Vector2(-edge.y, edge.x).normalized;

                    float minA = float.PositiveInfinity, maxA = float.NegativeInfinity;
                    foreach (var p in polyA)
                    {
                        float proj = Vector2.Dot(p, axis);
                        minA = Mathf.Min(minA, proj);
                        maxA = Mathf.Max(maxA, proj);
                    }

                    float minB = float.PositiveInfinity, maxB = float.NegativeInfinity;
                    foreach (var p in polyB)
                    {
                        float proj = Vector2.Dot(p, axis);
                        minB = Mathf.Min(minB, proj);
                        maxB = Mathf.Max(maxB, proj);
                    }

                    if (maxA <= minB || maxB <= minA)
                    {
                        return null;
                    }

                    float overlap = Mathf.Min(maxA, maxB) - Mathf.Max(minA, minB);
                    if (overlap < minOverlap)
                    {
                        minOverlap = overlap;
                        minAxis = axis;
                    }
                }
            }

            if (Vector2.Dot(centerA - centerB, minAxis) < 0f)
            {
                minAxis = -minAxis;
            }

            return minAxis * minOverlap;
        }

        /// <summary>
        /// 베이스끼리 절대 겹치지 않도록, 겹치는 다른 베이스로부터 밀어내는 것을
        /// 여러 번 반복해서 가장 가까운 비충돌 위치를 근사한다. 이후 지도
        /// 경계로 clamp. allowDisplacementOverlap이면(모델 메뉴얼 이동/리딩
        /// 모델 이동 중) 변위 베이스는 장애물로 치지 않고 통과할 수 있다.
        /// </summary>
        public static Vector2 ResolvePosition(
            IEllipseBody self,
            Vector2 desiredCenter,
            IEnumerable<(IEllipseBody body, Vector2 center)> others,
            Vector2 mapSize,
            bool allowDisplacementOverlap)
        {
            Vector2 pos = desiredCenter;
            float boundingRadius = BoundingRadius(self.SizeMm);

            for (int iteration = 0; iteration < CollisionIterations; iteration++)
            {
                bool moved = false;
                var polyA = EllipsePolygonAt(pos, self.SizeMm, self.RotationRadians);
                foreach (var (other, otherCenter) in others)
                {
                    if (allowDisplacementOverlap && other.IsDisplacement)
                    {
                        continue;
                    }

                    float otherBoundingRadius = BoundingRadius(other.SizeMm);
                    if (Vector2.Distance(pos, otherCenter) > boundingRadius + otherBoundingRadius)
                    {
                        continue;
                    }

                    var polyB = EllipsePolygonAt(otherCenter, other.SizeMm, other.RotationRadians);
                    var mtv = PolygonOverlapMtv(polyA, pos, polyB, otherCenter);
                    if (mtv.HasValue)
                    {
                        moved = true;
                        pos += mtv.Value;
                        polyA = EllipsePolygonAt(pos, self.SizeMm, self.RotationRadians);
                    }
                }
                if (!moved)
                {
                    break;
                }
            }

            // baseLayer는 중심-원점 mm 좌표계다(지도 중심이 (0,0), 범위는
            // [-mapSize/2, +mapSize/2]) — Base 조각의 anchoredPosition이 그대로
            // 이 좌표계를 따른다. 예전엔 여기가 [0, mapSize] 모서리-원점으로
            // 잘못 클램프돼 있어서, 지도 왼쪽 절반/아래쪽 절반으로는 드래그가
            // 전혀 안 먹혔다(그쪽으로 옮기려 하면 전부 중심 쪽 모서리 근처로
            // 튕겨 나갔다) — 지도 배경을 추가하고 나서야 실제로 드러난 버그.
            float halfX = mapSize.x / 2f;
            float halfY = mapSize.y / 2f;
            pos.x = Mathf.Clamp(pos.x, -halfX + boundingRadius, Mathf.Max(-halfX + boundingRadius, halfX - boundingRadius));
            pos.y = Mathf.Clamp(pos.y, -halfY + boundingRadius, Mathf.Max(-halfY + boundingRadius, halfY - boundingRadius));
            return pos;
        }

        /// <summary>Eberly의 robust point-to-ellipse 최근접점 알고리즘에서 쓰는
        /// 이분법 루트 찾기. 뉴턴법과 달리 중심 근처 등에서도 항상 안정적으로
        /// 수렴한다(측정 도구의 거리 재기에 씀).</summary>
        private static float EllipseDistanceRoot(float r0, float z0, float z1, float g0)
        {
            float n0 = r0 * z0;
            float s0 = z1 - 1f;
            float s1 = g0 < 0f ? 0f : Mathf.Sqrt(n0 * n0 + z1 * z1) - 1f;
            float s = 0f;
            for (int i = 0; i < 64; i++)
            {
                s = (s0 + s1) / 2f;
                if (s == s0 || s == s1)
                {
                    break;
                }
                float ratio0 = n0 / (s + r0);
                float ratio1 = z1 / (s + 1f);
                float g = ratio0 * ratio0 + ratio1 * ratio1 - 1f;
                if (g > 0f)
                {
                    s0 = s;
                }
                else if (g < 0f)
                {
                    s1 = s;
                }
                else
                {
                    break;
                }
            }
            return s;
        }

        /// <summary>
        /// 중심이 원점이고 회전되지 않은 타원(반지름 rx,ry) 테두리에서
        /// localTarget에 실제로 가장 가까운 점 — 단순히 중심 방향으로 투사하는
        /// 것과 달리 진짜 최근접점(Eberly의 강건한 point-to-ellipse 알고리즘 포팅).
        /// 타원은 중심에서 본 방향과 실제 최근접점 방향이(원과 달리) 대체로 다르다.
        /// </summary>
        public static Vector2 ClosestPointOnEllipseLocal(Vector2 localTarget, float rx, float ry)
        {
            if (rx < 0.0001f || ry < 0.0001f)
            {
                return Vector2.zero;
            }

            // 알고리즘은 e0 >= e1을 가정하므로 필요하면 축을 맞바꿔 풀고 되돌린다.
            bool swapped = rx < ry;
            float e0 = swapped ? ry : rx;
            float e1 = swapped ? rx : ry;
            float in0 = swapped ? localTarget.y : localTarget.x;
            float in1 = swapped ? localTarget.x : localTarget.y;

            float sx = in0 >= 0f ? 1f : -1f;
            float sy = in1 >= 0f ? 1f : -1f;
            float y0 = Mathf.Abs(in0);
            float y1 = Mathf.Abs(in1);

            float x0, x1;

            if (y1 > 0.0001f)
            {
                if (y0 > 0.0001f)
                {
                    float z0 = y0 / e0;
                    float z1 = y1 / e1;
                    float g = z0 * z0 + z1 * z1 - 1f;
                    if (Mathf.Abs(g) > 0.000001f)
                    {
                        float r0 = (e0 / e1) * (e0 / e1);
                        float s = EllipseDistanceRoot(r0, z0, z1, g);
                        x0 = r0 * y0 / (s + r0);
                        x1 = y1 / (s + 1f);
                    }
                    else
                    {
                        x0 = y0;
                        x1 = y1;
                    }
                }
                else
                {
                    x0 = 0f;
                    x1 = e1;
                }
            }
            else
            {
                float numer0 = e0 * y0;
                float denom0 = e0 * e0 - e1 * e1;
                if (numer0 < denom0)
                {
                    float xde0 = numer0 / denom0;
                    x0 = e0 * xde0;
                    x1 = e1 * Mathf.Sqrt(Mathf.Max(1f - xde0 * xde0, 0f));
                }
                else
                {
                    x0 = e0;
                    x1 = 0f;
                }
            }

            x0 *= sx;
            x1 *= sy;

            return swapped ? new Vector2(x1, x0) : new Vector2(x0, x1);
        }

        /// <summary>회전+이동된 타원(center, rot)의 테두리에서 world-space target에
        /// 가장 가까운 점(world 좌표).</summary>
        public static Vector2 ClosestPointOnEllipseWorld(Vector2 center, Vector2 sizeMm, float rot, Vector2 target)
        {
            Vector2 localTarget = Rotate(target - center, -rot);
            Vector2 localPoint = ClosestPointOnEllipseLocal(localTarget, sizeMm.x / 2f, sizeMm.y / 2f);
            return center + Rotate(localPoint, rot);
        }

        /// <summary>point에서 가장 가까운, polylines(각각 이어진 점들의 배열 —
        /// 닫힌 폴리곤이면 마지막 점이 첫 점과 같은 값으로 이미 중복되어 있다고
        /// 가정) 위의 점과, 그 지점이 속한 변의 정확한 안쪽 법선(수직) 단위
        /// 벡터를 함께 찾는다. 감김 방향(CW/CCW)에 무관하게 동작한다 — 부호
        /// 있는 넓이로 바깥쪽 법선을 구하는 TryClipSegmentToConvexPolygon과
        /// 같은 기법(SignedArea)을 재사용하고, 안쪽은 그 반대 방향이다. (첫
        /// 시도는 "폴리곤 중심 방향"으로 근사했는데, 직선 변에서도 위치에
        /// 따라 그 방향이 진짜 법선에서 벗어나 스냅 결과가 선을 넘나드는
        /// 문제가 있었다 — 변마다 정확한 법선을 써야 직선 구간 어디서든
        /// 일관되게 정확히 radius만큼 떨어진 지점에 스냅된다.) 모든 폴리라인의
        /// 모든 변을 순회해 최근접 세그먼트를 찾는 단순한 방식.</summary>
        public static Vector2 ClosestPointOnPolylines(Vector2 point, List<Vector2[]> polylines, out Vector2 inwardDirection, out float distance)
        {
            Vector2 best = point;
            Vector2 bestInward = Vector2.zero;
            float bestDistSq = float.MaxValue;
            foreach (var polyline in polylines)
            {
                float windingSign = SignedArea(polyline) >= 0f ? 1f : -1f;
                for (int i = 0; i < polyline.Length - 1; i++)
                {
                    Vector2 a = polyline[i];
                    Vector2 b = polyline[i + 1];
                    Vector2 edge = b - a;
                    float lenSq = edge.sqrMagnitude;
                    float t = lenSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / lenSq) : 0f;
                    Vector2 candidate = a + edge * t;
                    float distSq = (candidate - point).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = candidate;
                        Vector2 outwardNormal = new Vector2(edge.y, -edge.x) * windingSign;
                        bestInward = outwardNormal.sqrMagnitude > 0.0001f ? -outwardNormal.normalized : Vector2.zero;
                    }
                }
            }
            distance = Mathf.Sqrt(bestDistSq);
            inwardDirection = bestInward;
            return best;
        }

        /// <summary>반직선(origin에서 dir 방향)이 세그먼트(a→b)와 만나는 지점의
        /// t(≥0, origin으로부터의 거리)를 구한다. 표준 2D 반직선-선분 교차
        /// (연립방정식 origin+t·dir = a+s·(b-a)를 크라메르 공식으로 풂, s는
        /// [0,1] 안에 있어야 함).</summary>
        public static bool TryRaySegmentIntersect(Vector2 origin, Vector2 dir, Vector2 a, Vector2 b, out float t)
        {
            t = 0f;
            Vector2 e = b - a;
            float det = -dir.x * e.y + e.x * dir.y;
            if (Mathf.Abs(det) < 1e-8f)
            {
                return false;
            }
            Vector2 ap = a - origin;
            float tt = (-ap.x * e.y + e.x * ap.y) / det;
            float s = (dir.x * ap.y - ap.x * dir.y) / det;
            if (tt < 0f || s < 0f || s > 1f)
            {
                return false;
            }
            t = tt;
            return true;
        }

        /// <summary>origin에서 dir 방향 반직선이 polylines의 어느 변과든 처음
        /// 만나는 지점까지의 거리 — 아무 것도 안 만나면 null.</summary>
        public static float? RayDistanceToPolylines(Vector2 origin, Vector2 dir, List<Vector2[]> polylines)
        {
            float? best = null;
            foreach (var polyline in polylines)
            {
                for (int i = 0; i < polyline.Length - 1; i++)
                {
                    if (TryRaySegmentIntersect(origin, dir, polyline[i], polyline[i + 1], out float t) && (best == null || t < best.Value))
                    {
                        best = t;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// center를 고정 기준점 삼아 "그 방향(dir)으로 (sizeMm/rot로 주어진)
        /// 타원의 중심이 최대 얼마나 멀어질 수 있는가"(그 타원의 테두리가
        /// boundaryPolygon을 넘지 않는 한도)를 구하되, 지지함수 한 방향만
        /// 보는 대신 훨씬 엄격하게 검증한다 — 이 타원이 (기준이 된 다른
        /// 타원, 예컨대 리딩 모델과) 다르게 회전돼 있으면 실제로 가장 많이
        /// 튀어나오는 방향이 dir이 아닐 수 있어서, 지지함수만으로는 통과
        /// 시켜도 실제로는 다른 방향의 테두리가 경계를 넘어가는 버그가
        /// 있었다(사용자가 실제로 겪음). 대신 후보 위치마다 타원 테두리
        /// 64점 전부가 boundaryPolygon 안에 있는지(PointInConvexPolygon,
        /// IsFollowerWithinCoherency가 쓰는 것과 같은 검증)를 직접 확인하며
        /// 이분 탐색으로 한계 지점을 찾는다 — "테두리 한 점"이 아니라 "타원
        /// 전체가 완전히 안에 들어가는" 조건을 정확히 만족시킨다.
        /// boundaryPolygon 하나만 받는다(이동/코헤런시는 리딩 모델 하나의
        /// 오프셋 곡선뿐이라 폴리곤이 항상 하나 — 배치처럼 구역 여러 개인
        /// 경우엔 이 함수를 쓰지 않는다).
        /// </summary>
        public static float MaxRadialDistanceFullyInside(Vector2 center, Vector2 dir, Vector2[] boundaryPolygon, Vector2 sizeMm, float rot, float upperBound, int iterations = 18)
        {
            bool FullyInside(float t)
            {
                var poly = EllipsePolygonAt(center + dir * t, sizeMm, rot);
                foreach (var p in poly)
                {
                    if (!PointInConvexPolygon(p, boundaryPolygon))
                    {
                        return false;
                    }
                }
                return true;
            }

            if (upperBound <= 0f || !FullyInside(0f))
            {
                return 0f;
            }
            if (FullyInside(upperBound))
            {
                return upperBound;
            }
            float lo = 0f;
            float hi = upperBound;
            for (int i = 0; i < iterations; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (FullyInside(mid))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }
    }
}
