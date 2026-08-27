using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 마커(활성화/점령/아이콘) ───────────────────────────────────

        /// <summary>화면 하단 중앙에 마커 종류별 버튼을 모아 놓은 작은 패널을
        /// 띄운다 — 누르면 StartMarkerPlacement()로 배치 모드에 들어간다.
        /// Godot판 _build_marker_bar() 포팅. 버튼은 아이콘만 보여준다(글자
        /// 없음). 패널 자체가 ContentSizeFitter로 버튼 묶음 크기에 딱 맞게
        /// 줄어들고 화면 폭 전체가 아니라 중앙 한 곳에 뭉쳐 보이도록, 예전의
        /// "화면 전체 폭 바 + 그 안의 줄"(HorizontalLayoutGroup의
        /// childForceExpand 기본값이 true라서 버튼들이 그 넓은 폭에 흩뿌려져
        /// 보였다) 대신 바 자체를 레이아웃 그룹으로 두고 내용물 크기로
        /// 줄어들게 했다.</summary>
        private void BuildMarkerBar()
        {
            var canvasParent = GetCanvasParent();

            var barGo = new GameObject("MarkerBar", typeof(RectTransform));
            barGo.transform.SetParent(canvasParent, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 8f);

            var bg = barGo.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);

            var layout = barGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = barGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var entry in MarkerBarEntries)
            {
                Texture2D icon = entry.Kind switch
                {
                    "activation" => _activationTextureMovement,
                    "capture" => _captureTexture,
                    _ => _iconTextures != null && _iconTextures.TryGetValue(entry.Kind, out var t) ? t : null,
                };
                CreateMarkerBarButton(barRect, entry.Kind, icon);
            }
        }

        private void CreateMarkerBarButton(Transform parent, string kind, Texture2D icon)
        {
            var go = new GameObject($"MarkerBtn_{kind}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(MarkerBarHeight - 8f, MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => StartMarkerPlacement(kind));
        }

        private void StartMarkerPlacement(string kind)
        {
            if (_unitMoveActive)
            {
                return;
            }
            _placingMarkerKind = kind;
            ShowMarkerPlacementPreview(kind);
        }

        private void ShowMarkerPlacementPreview(string kind)
        {
            ClearMarkerPlacementPreview();
            var marker = CreateMarkerObject(kind, markerLayer);
            marker.raycastTarget = false;
            var c = marker.color;
            c.a *= 0.5f;
            marker.color = c;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                marker.Center = mouseLocal;
            }
            _markerPlacementPreview = marker;
        }

        private void ClearMarkerPlacementPreview()
        {
            if (_markerPlacementPreview != null)
            {
                Destroy(_markerPlacementPreview.gameObject);
                _markerPlacementPreview = null;
            }
        }

        /// <summary>kind에 맞는 마커 컴포넌트를 새로 만들어 parent 아래에 붙인다
        /// (아직 위치/이벤트 연결은 안 함 — 배치 미리보기/실제 배치 양쪽에서
        /// 공용으로 쓴다).</summary>
        private MarkerBase CreateMarkerObject(string kind, Transform parent)
        {
            switch (kind)
            {
                case "activation":
                {
                    var go = new GameObject("ActivationMarker", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    var marker = go.AddComponent<ActivationMarker>();
                    marker.Configure(_activationTextureMovement, _activationTextureAssault, _activationTextureDone);
                    return marker;
                }
                case "capture":
                {
                    var go = new GameObject("CaptureMarker", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    var marker = go.AddComponent<CaptureMarker>();
                    marker.Configure(_captureTexture);
                    return marker;
                }
                default:
                {
                    var go = new GameObject($"IconMarker_{kind}", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    var marker = go.AddComponent<IconMarker>();
                    _iconTextures.TryGetValue(kind, out var tex);
                    marker.Configure(kind, tex);
                    return marker;
                }
            }
        }

        /// <summary>배치 모드 중 마우스 클릭 처리 — 지도 위 왼쪽 버튼이면 실제로
        /// 배치하고, 그 외 버튼이면 배치를 취소한다. 어느 쪽이든 배치 모드는
        /// 끝난다. Godot판 스페이스바 처리 바로 아래의 _placing_marker_kind
        /// 분기 포팅. UI(마커바 버튼 등) 위 클릭은 여기서 손대지 않는다 — 예를
        /// 들어 다른 마커 버튼을 눌러 종류를 바꾸는 클릭은 그 버튼의
        /// onClick(StartMarkerPlacement)이 처리하는데, 여기서도 같은 클릭을
        /// "배치 모드 종료"로 취급해버리면 막 시작된 새 배치가 같은 프레임에
        /// 취소돼버린다.</summary>
        private void HandleMarkerPlacementInput()
        {
            if (!IsPointerOverUi() && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
            {
                if (Input.GetMouseButtonDown(0) && TryGetLocalMouse(out var mouseLocal))
                {
                    PlaceMarker(_placingMarkerKind, mouseLocal);
                }
                _placingMarkerKind = "";
                ClearMarkerPlacementPreview();
                return;
            }

            if (TryGetLocalMouse(out var hoverLocal) && _markerPlacementPreview != null)
            {
                _markerPlacementPreview.Center = hoverLocal;
            }
        }

        private void PlaceMarker(string kind, Vector2 point)
        {
            BeginUndoTransaction();
            var marker = CreateMarkerObject(kind, markerLayer);
            marker.Center = ClampMarkerToMap(marker, point);
            marker.DragRequested += OnMarkerDragRequested;
            switch (kind)
            {
                case "activation":
                    marker.RightClicked += OnActivationMarkerRightClicked;
                    break;
                case "capture":
                    marker.RightClicked += OnCaptureMarkerRightClicked;
                    break;
                default:
                    marker.RightClicked += OnIconMarkerRightClicked;
                    break;
            }
            CommitUndoTransaction();
        }

        /// <summary>markerLayer는 baseLayer와 같은 중심-원점 mm 좌표계이므로
        /// (mapSizeMm의 절반이 지도 중심) 그 기준으로 클램프한다.</summary>
        private Vector2 ClampMarkerToMap(MarkerBase marker, Vector2 desiredCenter)
        {
            Vector2 half = marker.RectTransform.sizeDelta / 2f;
            Vector2 mapHalf = mapSizeMm / 2f;
            return new Vector2(
                    Mathf.Clamp(desiredCenter.x, -mapHalf.x + half.x, mapHalf.x - half.x),
                    Mathf.Clamp(desiredCenter.y, -mapHalf.y + half.y, mapHalf.y - half.y));
        }

        private void OnMarkerDragRequested(MarkerBase piece)
        {
            BeginUndoTransaction();
            _draggingMarker = piece;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                _markerDragOffset = piece.Center - mouseLocal;
            }
            piece.transform.SetAsLastSibling();
        }

        private void HandleMarkerDragInput()
        {
            if (Input.GetMouseButtonUp(0))
            {
                _draggingMarker = null;
                CommitUndoTransaction();
                return;
            }
            if (TryGetLocalMouse(out var mouseLocal))
            {
                var desired = mouseLocal + _markerDragOffset;
                _draggingMarker.Center = ClampMarkerToMap(_draggingMarker, desired);
            }
        }

        private void OnActivationMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 우클릭은 이동 → 돌격 → 완료를 계속 순환한다. shift+우클릭이 삭제.
            BeginUndoTransaction();
            var marker = (ActivationMarker)piece;
            if (shiftHeld)
            {
                Destroy(marker.gameObject);
                CommitUndoTransaction();
                return;
            }
            int idx = System.Array.IndexOf(ActivationMarker.StateSequence, marker.State);
            marker.SetState(ActivationMarker.StateSequence[(idx + 1) % ActivationMarker.StateSequence.Length]);
            CommitUndoTransaction();
        }

        private void OnCaptureMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 우클릭은 흰색 → 빨간색 → 파란색을 계속 순환한다. shift+우클릭이 삭제 —
            // 색 순환에 종료 지점이 없어서(활성화 마커처럼 마지막에 사라지는 게
            // 아님) 삭제는 별도 입력으로 뺐다.
            BeginUndoTransaction();
            var marker = (CaptureMarker)piece;
            if (shiftHeld)
            {
                Destroy(marker.gameObject);
                CommitUndoTransaction();
                return;
            }
            int idx = System.Array.IndexOf(CaptureMarker.ColorSequence, marker.ColorState);
            marker.SetColorState(CaptureMarker.ColorSequence[(idx + 1) % CaptureMarker.ColorSequence.Length]);
            CommitUndoTransaction();
        }

        private void OnIconMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 순환 없이 우클릭 한 번으로 바로 삭제(shift 여부는 상관없다).
            BeginUndoTransaction();
            Destroy(piece.gameObject);
            CommitUndoTransaction();
        }

    }
}
