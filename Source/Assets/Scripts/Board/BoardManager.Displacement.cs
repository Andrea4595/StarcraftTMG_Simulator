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

        internal void HandleDisplacementPlacementInput()
        {
            if (TryGetLocalMouse(out var local))
            {
                var piece = _displacementQueue[0];
                piece.Center = ResolveDisplacementDragPosition(piece, local);
            }

            // FindBaseAtPoint/IsOverMissionObjective 예외: 다른 유닛/토큰이나
            // 미션 마커 위에서 클릭해도 지금 따라가는 위치에 정상적으로
            // 배치되게 한다(사용자 보고, 2026-09-03/04 — 유닛/토큰은 처음
            // 보고에서, 미션 마커는 뒤이어 추가 보고). 새 드래그 자체는
            // OnDragRequested의 HasDisplacementQueue 가드로 이미 막혔지만
            // (BoardManager.cs), 그 가드만으론 이 아래 클릭이 IsPointerOverUi()에
            // 걸려 그냥 씹힌다(조각/마커도 raycastTarget이라 그 위 클릭은 전부
            // "UI 위"로 잡힘) — HandlePendingDeploymentInput의
            // IsOverMissionObjective() 예외와 같은 패턴.
            bool overExistingPiece = TryGetLocalMouse(out var hitPoint) && FindBaseAtPoint(hitPoint) != null;
            if (Input.GetMouseButtonDown(0) && (!IsPointerOverUi() || overExistingPiece || IsOverMissionObjective()))
            {
                // 방금 확정된 변위 베이스의 새 위치를 공유한다(사용자 요청,
                // 2026-08-30) — 그 베이스가 속한 유닛은 지금 옮기는 중인
                // 유닛(_unitMoveUnit)과 다를 수 있으므로 따로 방송한다.
                var placedPiece = _displacementQueue[0];
                _displacementQueue.RemoveAt(0);
                BroadcastUnitIfNetworked(placedPiece.Unit);
                if (_displacementQueue.Count == 0)
                {
                    bool resume = _displacementResumeLeadingFinish;
                    var anchor = _displacementAnchor;
                    _displacementAnchor = null;
                    _displacementResumeLeadingFinish = false;
                    if (resume)
                    {
                        // 배치(신규 유닛을 처음 놓는 것)는 예전처럼 여기서 바로
                        // 끝난다. 이미 배치된 유닛을 다시 옮기는 중이었다면, 이
                        // 지점을 웨이포인트로 확정하고 리딩 단계를 계속한다
                        // (사용자 요청 범위 — EndPieceDrag의 같은 분기 참고).
                        if (_unitMoveIsDeployment)
                        {
                            FinishLeadingMove();
                        }
                        else
                        {
                            CommitLeadingWaypoint();
                        }
                    }
                    else
                    {
                        // 일반 드래그가 변위 베이스를 밀어낸 경우 — 원래 드래그부터 지금
                        // 이 배치까지를 한 트랜잭션으로 커밋한다. 이 경로는 EndPieceDrag의
                        // "일반 드래그" 분기를 안 타서(변위 처리로 새 지점) anchor 자신의
                        // 최종 위치가 아직 공유된 적 없다 — 여기서 같이 공유한다.
                        CommitUndoTransaction();
                        BroadcastUnitIfNetworked(anchor.Unit);
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
