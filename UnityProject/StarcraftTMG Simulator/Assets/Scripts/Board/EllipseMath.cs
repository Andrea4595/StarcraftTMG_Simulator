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

            pos.x = Mathf.Clamp(pos.x, boundingRadius, Mathf.Max(boundingRadius, mapSize.x - boundingRadius));
            pos.y = Mathf.Clamp(pos.y, boundingRadius, Mathf.Max(boundingRadius, mapSize.y - boundingRadius));
            return pos;
        }
    }
}
