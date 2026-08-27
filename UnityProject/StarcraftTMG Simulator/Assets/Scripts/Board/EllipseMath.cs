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
    }
}
