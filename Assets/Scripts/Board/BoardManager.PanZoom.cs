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
        /// 있도록 확대/축소하면서 위치를 함께 보정한다. Godot판 _zoom_at() 포팅.</summary>
        private void ZoomAt(Vector2 screenPos, float factor)
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

            float newZoom = Mathf.Clamp(_zoomLevel * factor, MinZoom, MaxZoom);
            if (Mathf.Approximately(newZoom, _zoomLevel))
            {
                return;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, null, out var mouseLocal))
            {
                return;
            }

            float oldScale = mapArea.localScale.x;
            Vector2 mapPoint = (mouseLocal - mapArea.anchoredPosition) / oldScale;

            _zoomLevel = newZoom;
            float newScale = _baseScaleFactor * _zoomLevel;
            mapArea.localScale = new Vector3(newScale, newScale, 1f);
            mapArea.anchoredPosition = mouseLocal - mapPoint * newScale;
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
            if (!Mathf.Approximately(scroll, 0f) && !IsPointerOverUi())
            {
                float factor = scroll > 0f ? ZoomStep : 1f / ZoomStep;
                ZoomAt(Input.mousePosition, factor);
            }

            Vector2 avail = parent.rect.size;
            if (avail != _lastViewportSize)
            {
                UpdateMapLayout();
            }
        }

    }
}
