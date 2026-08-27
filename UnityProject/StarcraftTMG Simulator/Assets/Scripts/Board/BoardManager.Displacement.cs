using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 변위 베이스 재배치 ──────────────────────────────────────────

        /// <summary>원형 근사 거리가 아니라, 실제 겹침 판정과 똑같은 (회전된)
        /// 타원 폴리곤+SAT 검사를 그대로 재사용한다.</summary>
        private List<Base> FindOverlappingDisplacementBases(Base movedPiece)
        {
            var result = new List<Base>();
            if (movedPiece == null)
            {
                return result;
            }
            var polyA = EllipseMath.EllipsePolygonAt(movedPiece.Center, movedPiece.SizeMm, movedPiece.RotationRadians);
            foreach (var other in _pieces)
            {
                if (other == movedPiece || !other.IsDisplacement)
                {
                    continue;
                }
                var polyB = EllipseMath.EllipsePolygonAt(other.Center, other.SizeMm, other.RotationRadians);
                if (EllipseMath.PolygonOverlapMtv(polyA, movedPiece.Center, polyB, other.Center).HasValue)
                {
                    result.Add(other);
                }
            }
            return result;
        }

        /// <summary>모델 메뉴얼 이동/리딩 모델 이동이 끝난 직후, 방금 통과한 변위
        /// 베이스(들)의 새 위치는 이동한 사람이 직접 정한다: 항상 anchor에 딱
        /// 붙은 채(원하는 거리 0") 마우스를 따라가다가, 클릭하면 확정된다.</summary>
        private void StartDisplacementPlacement(Base anchor, List<Base> queue, bool resumeLeadingFinish)
        {
            _displacementAnchor = anchor;
            _displacementQueue.Clear();
            _displacementQueue.AddRange(queue);
            _displacementResumeLeadingFinish = resumeLeadingFinish;

            if (TryGetLocalMouse(out var mouseLocal))
            {
                var piece = _displacementQueue[0];
                piece.Center = ResolveDisplacementDragPosition(piece, mouseLocal);
            }
        }

        private void HandleDisplacementPlacementInput()
        {
            if (TryGetLocalMouse(out var local))
            {
                var piece = _displacementQueue[0];
                piece.Center = ResolveDisplacementDragPosition(piece, local);
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                _displacementQueue.RemoveAt(0);
                if (_displacementQueue.Count == 0)
                {
                    bool resume = _displacementResumeLeadingFinish;
                    _displacementAnchor = null;
                    _displacementResumeLeadingFinish = false;
                    if (resume)
                    {
                        // 유닛 이동/배치 중에 변위를 통과한 경우 — 그 트랜잭션을 이어서 마무리한다.
                        FinishLeadingMove();
                    }
                    else
                    {
                        // 일반 드래그가 변위 베이스를 밀어낸 경우 — 원래 드래그부터 지금
                        // 이 배치까지를 한 트랜잭션으로 커밋한다.
                        CommitUndoTransaction();
                    }
                }
            }
        }

        /// <summary>변위 베이스는 anchor 테두리에 정확히 맞닿은 채(원하는 거리 0")
        /// 마우스를 따라 돈다 — 원형 근사(bounding radius) 합이 아니라, 그 방향의
        /// 실제 타원 반지름을 각각 재서 더해야 회전된 타원끼리도 정확히 맞닿는다.</summary>
        private Vector2 ResolveDisplacementDragPosition(Base piece, Vector2 desiredCenter)
        {
            Vector2 anchorCenter = _displacementAnchor.Center;
            Vector2 offset = desiredCenter - anchorCenter;
            if (offset.magnitude < 0.01f)
            {
                offset = Vector2.right;
            }
            Vector2 direction = offset.normalized;
            float minDist = EllipseMath.EllipseRadiusInDirection(_displacementAnchor.SizeMm, _displacementAnchor.RotationRadians, direction)
                    + EllipseMath.EllipseRadiusInDirection(piece.SizeMm, piece.RotationRadians, direction);
            Vector2 pos = anchorCenter + direction * minDist;
            return ResolvePosition(piece, pos, false);
        }

    }
}
