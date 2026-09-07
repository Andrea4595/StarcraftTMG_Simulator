using UnityEngine;

namespace TmgBoard
{
    /// <summary>BoardManager.Update()가 매 프레임 무엇을 처리할지 정하는
    /// 우선순위 캐스케이드 — 예전엔 BoardManager.cs의 Update() 안에 그대로
    /// 있었다(2026-09-03, BoardManager 리팩토링 Phase 5에서 분리). 상태를
    /// 전혀 갖지 않는다(static) — 모든 모드 플래그는 여전히 BoardManager가
    /// 소유하고, 이 클래스는 board의 internal 프로퍼티/메서드를 통해서만
    /// 읽고 부른다. 실제 드래그 계산(ResolvePosition 등)은 BoardManager
    /// 쪽에 남아있다 — 조각/보드 상태에 너무 깊이 의존해서 분리하면
    /// 필드 접근만 전부 board.를 거치게 될 뿐 실질적인 결합도가 안
    /// 줄어들기 때문(BoardManager.UndoRedo.cs의 CaptureBoardSnapshot/
    /// IsUndoBlocked이 UndoRedoService 대신 BoardManager에 남은 것과
    /// 같은 이유).</summary>
    internal static class BoardInputController
    {
        internal static void RunFrame(BoardManager board)
        {
            board.HandlePanAndZoom();
            board.HandleMeasureInput();
            board.UpdateHoveredUnit();
            board.UpdateUnitDetailPanel();
            board.UpdateScreenshotToast();
            board.UpdateMultiplayerButtonState();
            board.UpdateMarkerMoveTweens();
            board.RefreshBlastTemplateHighlights();
            board.UpdateBlastTemplateFx();
            board.UpdatePieceMoveTweens();
            board.UpdateEmoteFades();
            board.UpdateGifExport();

            board.UpdateEngageWarningForCurrentDrag();

            if (board.IsUnitMoveActive && Input.GetMouseButtonDown(1))
            {
                board.CancelUnitMove();
                return;
            }

            if (board.IsDraggingPiece)
            {
                board.HandlePieceDragInput();
                return;
            }

            if (board.IsClickCandidatePending)
            {
                board.HandleClickCandidateInput();
                return;
            }

            if (board.IsDraggingFollower)
            {
                board.HandleFollowerDragInput();
                return;
            }

            if (board.HasDisplacementQueue)
            {
                board.HandleDisplacementPlacementInput();
                return;
            }

            if (board.HasPendingDeployment)
            {
                board.HandlePendingDeploymentInput();
                return;
            }

            if (board.HasPendingRosterToken)
            {
                board.HandlePendingRosterTokenInput();
                return;
            }

            if (board.IsDraggingMarker)
            {
                board.HandleMarkerDragInput();
                return;
            }

            if (board.IsPlacingMarker)
            {
                board.HandleMarkerPlacementInput();
                return;
            }

            board.HandleEmptyClickFallthrough();
        }
    }
}
