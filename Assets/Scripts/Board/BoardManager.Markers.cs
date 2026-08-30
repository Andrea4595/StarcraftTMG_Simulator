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

        /// <summary>화면 맨 아래를 가로지르는 바 — 마커 종류별 버튼은 중앙에
        /// 모아두고, 스크린샷 버튼은 우측 하단에 고정한다. 누르면
        /// StartMarkerPlacement()로 배치 모드에 들어간다. Godot판
        /// _build_marker_bar() 포팅이지만 배치는 새로 짰다(원래는 화면 하단
        /// 중앙에 붕 뜬 작은 패널이었다 — 스코어보드 바와 같은 "화면 끝까지
        /// 가로지르는 바 + 그 안에 독립적으로 앵커된 구역들" 방식으로
        /// 바꿨다).</summary>
        private void BuildMarkerBar()
        {
            var canvasParent = GetCanvasParent();

            var barGo = new GameObject("MarkerBar", typeof(RectTransform));
            barGo.transform.SetParent(canvasParent, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, GameConstants.MarkerBarHeight);

            var bg = barGo.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);

            // 마커 아이콘들 — 화면 정중앙에 모아둔다(예전과 같은 자리).
            var iconsGo = new GameObject("MarkerIcons", typeof(RectTransform));
            iconsGo.transform.SetParent(barRect, false);
            var iconsRect = (RectTransform)iconsGo.transform;
            iconsRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconsRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconsRect.pivot = new Vector2(0.5f, 0.5f);
            iconsRect.anchoredPosition = Vector2.zero;

            var layout = iconsGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = iconsGo.AddComponent<ContentSizeFitter>();
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
                CreateMarkerBarButton(iconsRect, entry.Kind, icon);
            }

            CreateMarkerHintLabel(barRect);
            CreateScreenshotButton(barRect);
            CreateFitButton(barRect);
            CreateDiceButton(barRect);
            CreateExitButton(barRect);
            CreateSaveButton(barRect);
        }

        /// <summary>바 맨 왼쪽 — 처음 화면(Entry)으로 돌아가기(사용자 요청).
        /// 클릭하면 바로 나가지 않고 확인 창을 먼저 띄운다(exitConfirmDialog,
        /// ConfigureExit로 주입됨) — 확인하면 진행 중이던 판을 버리고
        /// 처음 화면으로 돌아간다(BoardManager.cs의 OnExitConfirmed).</summary>
        private void CreateExitButton(Transform parent)
        {
            var go = new GameObject("ExitButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/ExitButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (exitConfirmDialog != null)
                {
                    exitConfirmDialog.Open("진행 중인 게임을 종료하고\n처음 화면으로 돌아갈까요?\n(현재 진행 상황은 저장되지 않습니다)");
                }
            });
        }

        /// <summary>"나가기" 버튼 바로 오른쪽 — 진행 중인 게임을 이름을 물어본
        /// 뒤 Saves/ 폴더에 저장한다(사용자 요청, BoardManager.Save.cs의
        /// SaveGame). 프리셋 저장(SaveButton.png)과 같은 아이콘을 재사용해
        /// "저장"이라는 시각 언어를 통일했다.</summary>
        private void CreateSaveButton(Transform parent)
        {
            var go = new GameObject("SaveButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f + GameConstants.MarkerBarHeight, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/SaveButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (saveNameDialog != null)
                {
                    saveNameDialog.Open("저장 이름", "");
                }
            });
        }

        /// <summary>마커 아이콘 오른쪽에 조작법을 띄워주는 라벨(사용자 요청) —
        /// iconsGo 자신의 ContentSizeFitter 안에 넣으면 라벨이 나타날 때마다
        /// 그 그룹 전체가 재중앙정렬되며 아이콘들이 화면에서 좌우로 밀리는
        /// 문제가 있어서, 독립적으로 배치하고 아이콘 그룹의 폭을 직접
        /// 계산해서(HorizontalLayoutGroup과 같은 식) 그 오른쪽 끝에 맞춘다 —
        /// 그래야 아이콘 위치는 항상 고정이다.</summary>
        private void CreateMarkerHintLabel(Transform parent)
        {
            float iconSize = GameConstants.MarkerBarHeight - 8f;
            int count = MarkerBarEntries.Length;
            float iconsGroupWidth = 20f /* padding L+R */ + count * iconSize + Mathf.Max(count - 1, 0) * 6f /* spacing */;

            var go = new GameObject("MarkerHintLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(iconsGroupWidth / 2f + 12f, 0f);
            rect.sizeDelta = new Vector2(420f, GameConstants.MarkerBarHeight - 12f);

            _markerHintLabel = go.AddComponent<TextMeshProUGUI>();
            _markerHintLabel.fontSize = 13f;
            _markerHintLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            _markerHintLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _markerHintLabel.enableWordWrapping = false;
            _markerHintLabel.raycastTarget = false;
            _markerHintLabel.text = "";
        }

        private void CreateScreenshotButton(Transform parent)
        {
            var go = new GameObject("ScreenshotButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/ScreenshotButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(TakeScreenshot);
        }

        /// <summary>스크린샷 버튼 바로 왼쪽 — 조작(팬/줌 상태)에 그대로 남는
        /// "지도 화면에 맞추기"(FitMapToView, BoardManager.PanZoom.cs). 예전
        /// 스크린샷이 찍기 직전에만 잠깐 적용하고 되돌리던 뷰를, 사용자
        /// 요청으로 일반 조작 버튼 하나로 분리한 것 — "고정"이 아니라 한 번
        /// 위치를 잡아줄 뿐이라 그 뒤엔 평소처럼 팬/줌할 수 있다.</summary>
        private void CreateFitButton(Transform parent)
        {
            var go = new GameObject("FitButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - (GameConstants.MarkerBarHeight - 8f) - 6f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/FitButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(FitMapToView);
        }

        /// <summary>Fit 버튼 바로 왼쪽 — 주사위 굴리기 툴(DiceRollDialog)을
        /// 연다. 보드 상태와 무관한 독립 창이라 여기선 그냥 여는 것만 한다.</summary>
        private void CreateDiceButton(Transform parent)
        {
            var go = new GameObject("DiceButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - 2f * (GameConstants.MarkerBarHeight - 8f) - 12f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/DiceButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (diceRollDialog != null)
                {
                    diceRollDialog.Open();
                }
            });
        }

        private void CreateMarkerBarButton(Transform parent, string kind, Texture2D icon)
        {
            var go = new GameObject($"MarkerBtn_{kind}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => StartMarkerPlacement(kind));

            var hover = go.AddComponent<MarkerButtonHoverHandler>();
            string hint = MarkerControlHints.TryGetValue(kind, out var h) ? h : "";
            hover.OnEnter = () =>
            {
                if (_markerHintLabel != null)
                {
                    _markerHintLabel.text = hint;
                }
            };
            hover.OnExit = () =>
            {
                if (_markerHintLabel != null)
                {
                    _markerHintLabel.text = "";
                }
            };
        }

        /// <summary>마커바 버튼 하나에만 붙어서 마우스 진입/이탈을 알려준다 —
        /// Button 자체는 클릭만 다루므로, 호버로 조작법을 보여주려면
        /// IPointerEnterHandler/IPointerExitHandler를 직접 구현해야 한다
        /// (TacticalCardClickHandler와 같은 패턴).</summary>
        private class MarkerButtonHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public System.Action OnEnter;
            public System.Action OnExit;

            public void OnPointerEnter(PointerEventData eventData)
            {
                OnEnter?.Invoke();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                OnExit?.Invoke();
            }
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
