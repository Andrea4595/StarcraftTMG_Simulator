using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 화면 이동/확대축소(패닝/줌) ─────────────────────────────────
        // mapArea가 baseLayer/guideline/memoOverlay를 감싸고, 이 하나의
        // 트랜스폼(localScale/anchoredPosition)만 조작해서 지도 전체를
        // 함께 움직인다. Godot판 GameBoard.gd의 _map_area/_layout/_zoom_at
        // 포팅. 관련 필드(ZoomStep/MinZoom/MaxZoom, _zoomLevel 등)는
        // BoardManager.cs 상단에 선언돼 있다.

        /// <summary>지도가 뷰포트 안에 들어오도록 기본 축소 비율을 다시 계산하고,
        /// 현재 줌 레벨을 반영해 mapArea를 중앙에 배치한다. 창 크기가 바뀌거나
        /// 지도 크기가 바뀔 때 호출된다 — Godot판 _layout()과 같은 지점에서
        /// 패닝 오프셋을 초기화(재중앙)한다는 것도 동일하다.</summary>
        private void UpdateMapLayout()
        {
            if (mapArea == null)
            {
                return;
            }

            var parent = mapArea.parent as RectTransform;
            Vector2 avail = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);

            float scaleFactor = Mathf.Min(avail.x / mapSizeMm.x, avail.y / mapSizeMm.y);
            scaleFactor = Mathf.Min(scaleFactor, 1f);
            _baseScaleFactor = scaleFactor;

            mapArea.sizeDelta = mapSizeMm;
            float totalScale = scaleFactor * _zoomLevel;
            mapArea.localScale = new Vector3(totalScale, totalScale, 1f);
            mapArea.anchoredPosition = Vector2.zero;
            _lastViewportSize = avail;
        }

        /// <summary>마우스가 가리키는 지도 위 지점이 화면상 같은 자리에 그대로
        /// 있도록 확대/축소하면서 위치를 함께 보정한다. Godot판 _zoom_at() 포팅
        /// — 실제 계산은 MissionSetupController도 함께 쓰는 공용 유틸리티
        /// (Board/MapZoomUtil.cs)로 뽑아냈다.</summary>
        private void ZoomAt(Vector2 screenPos, float factor)
        {
            if (mapArea == null)
            {
                return;
            }
            _zoomLevel = MapZoomUtil.ApplyZoomAtScreenPoint(mapArea, screenPos, _zoomLevel, _baseScaleFactor, factor, MinZoom, MaxZoom);
        }

        // "지도가 화면에 다 보이게 맞추기" 버튼(FitButton, 마커바)이 캡처
        // 직전 스크린샷 전용 뷰로 잠깐 바꿨다가 되돌리던 걸 대체한다 — 사용자
        // 요청으로 "고정"이 아니라 그냥 한 번 위치/배율을 잡아주는 일반
        // 조작으로 바뀌었다(BoardManager.Screenshot.cs 참고, 이제 스크린샷은
        // 현재 보이는 그대로만 찍는다). 위아래 공백을 이만큼 두고, 상단
        // 바(스코어보드)/하단 바(마커바) 사이 구간의 중앙에 높이 기준으로
        // 꽉 채운다.
        private const float FitViewMarginPx = 60f;

        /// <summary>지도 폭이 화면을 넘치든 말든 상관없이, 지도 높이가 상단
        /// 바(스코어보드)와 하단 바(마커바) 사이 구간에 FitViewMarginPx만큼
        /// 여백을 두고 그 구간 중앙을 채우도록 mapArea의 스케일/위치를 맞춘다.
        /// 두 바는 지도 위에 그대로 겹쳐 그려지므로(공간을 밀어내지 않음)
        /// 화면 전체 높이를 기준으로 중앙 정렬하면 안 되고, 이 둘을 뺀
        /// "실제로 보이는" 구간을 기준으로 잡아야 한다. 36x36"(정사각형)과
        /// 54x36"(가로가 긴) 둘 다 이 규칙 하나로 동일하게 처리된다. 평소의
        /// 줌 범위(MinZoom~MaxZoom)와 무관하게 필요한 배율을 그대로 쓰되,
        /// 이후 스크롤 줌이 여기서부터 자연스럽게 이어지도록 _zoomLevel도
        /// 그 배율에 맞춰 갱신한다(ZoomAt이 _baseScaleFactor*_zoomLevel로
        /// localScale을 다시 계산하므로, 안 맞추면 다음 스크롤 때 이 뷰가
        /// 사라지고 예전 줌으로 순간이동해버린다).</summary>
        private void FitMapToView()
        {
            if (mapArea == null)
            {
                return;
            }
            var parent = mapArea.parent as RectTransform;
            Vector2 avail = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);

            float usableHeight = avail.y - GameConstants.TopBarHeight - GameConstants.MarkerBarHeight;
            float fitScale = Mathf.Max(usableHeight - FitViewMarginPx * 2f, 1f) / mapSizeMm.y;
            mapArea.localScale = new Vector3(fitScale, fitScale, 1f);

            // 화면 정중앙이 아니라 두 바 사이 구간의 중앙으로 피봇을 옮긴다 —
            // 위 바(스코어보드+페이즈바, TopBarHeight)가 아래 바(마커바)보다
            // 크면 그만큼 지도를 아래로 내려야 두 바 사이에서 시각적으로
            // 가운데에 온다.
            float pivotY = (GameConstants.MarkerBarHeight - GameConstants.TopBarHeight) / 2f;
            mapArea.anchoredPosition = new Vector2(0f, pivotY);

            if (_baseScaleFactor > 0.0001f)
            {
                _zoomLevel = fitScale / _baseScaleFactor;
            }
        }

        /// <summary>가운데 버튼 드래그로 화면 이동, 마우스 휠로 커서 위치 기준
        /// 확대/축소. UI 패널 위에서 시작한 경우는 무시한다.</summary>
        private void HandlePanAndZoom()
        {
            if (mapArea == null)
            {
                return;
            }
            var parent = mapArea.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (Input.GetMouseButtonDown(2) && !IsPointerOverUi())
            {
                _panning = true;
                _lastPanScreenPos = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(2))
            {
                _panning = false;
            }

            if (_panning)
            {
                Vector2 currentScreenPos = Input.mousePosition;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, currentScreenPos, null, out var curLocal)
                        && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, _lastPanScreenPos, null, out var prevLocal))
                {
                    mapArea.anchoredPosition += curLocal - prevLocal;
                }
                _lastPanScreenPos = currentScreenPos;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                // 베이스를 드래그하는 중(리딩 모델 이동/배치, 팔로워 재배치)
                // 이거나 배치 고스트가 마우스를 따라다니는 중이면, 휠은 줌
                // 대신 그 조각의 회전에 쓴다 — 타원형 베이스는 방향이 실제
                // 판정에 영향을 주므로, 옮기는 동안 바로 돌려볼 수 있어야
                // 한다(사용자 요청). 이 분기는 일부러 !IsPointerOverUi() 밖에
                // 둔다 — 드래그 중인 베이스 자체가 레이캐스트 가능한 UI
                // 요소라서, 마우스가 그 위에 있으면(드래그 중엔 거의 항상)
                // IsPointerOverUi()가 true가 돼 회전 자체가 막혀버렸었다
                // (실제로 한 번도 작동하지 않았던 버그).
                var rotateTarget = _draggingPiece != null ? _draggingPiece : (_draggingFollower != null ? _draggingFollower : _placementPreview);
                if (rotateTarget != null)
                {
                    rotateTarget.RotateStep(scroll > 0f ? 1 : -1);
                }
                else
                {
                    // 드래그/회전 중이 아니라면, 마우스가 베이스 위에 있어도
                    // 줌은 평소처럼 동작해야 한다(사용자 요청) — 베이스도
                    // 레이캐스트 가능한 UI라 IsPointerOverUi()가 true로 잡히지만,
                    // 그게 "실제 UI 패널 위"인지 "그냥 지도 위 베이스 위"인지는
                    // 구분해야 한다. FindBaseAtPoint로 직접 다시 확인한다 —
                    // _hoveredBase는 이 함수가 UpdateHoveredUnit()보다 먼저
                    // 불려서 한 프레임 지난 값이라 여기선 못 믿는다.
                    bool overBase = TryGetLocalMouse(out var hoverLocal) && FindBaseAtPoint(hoverLocal) != null;
                    if (!IsPointerOverUi() || overBase)
                    {
                        float factor = scroll > 0f ? ZoomStep : 1f / ZoomStep;
                        ZoomAt(Input.mousePosition, factor);
                    }
                }
            }

            Vector2 avail = parent.rect.size;
            if (avail != _lastViewportSize)
            {
                UpdateMapLayout();
            }
        }

    }
}
