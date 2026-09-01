using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 마커(활성화/점령/아이콘) ───────────────────────────────────

        // 멀티플레이어 중 배치된 마커만 등록됨(id는 BoardNetworkSync가
        // 발급) — 삭제/이동 방송이 도착했을 때 어느 GameObject인지 찾는 용도.
        private readonly Dictionary<int, MarkerBase> _networkedMarkersById = new();

        // 드래그를 끝낸 쪽이 아닌 다른 클라이언트가 이동 방송을 받았을 때,
        // 순간이동 대신 부드럽게 그 자리로 움직이게 하는 진행 중 트윈 목록.
        private const float MarkerMoveTweenDuration = 0.2f;
        private struct MarkerMoveTween
        {
            public MarkerBase Marker;
            public Vector2 From;
            public Vector2 To;
            public float StartTime;
        }
        private readonly List<MarkerMoveTween> _markerMoveTweens = new();

        /// <summary>되돌리기 조작 리스트에 보여줄 마커 종류 이름 — MarkerBarEntries에
        /// 이미 있는 한글 이름을 그대로 재사용한다.</summary>
        private static string GetMarkerKindLabel(string kind)
        {
            foreach (var entry in MarkerBarEntries)
            {
                if (entry.Kind == kind)
                {
                    return entry.Label;
                }
            }
            return "마커";
        }

        /// <summary>구체 타입(ActivationMarker/CaptureMarker/IconMarker)만 보고
        /// 종류 이름을 알아낸다 — MarkerBase 자체엔 종류 문자열이 없다.</summary>
        private static string DescribeMarkerKind(MarkerBase marker)
        {
            switch (marker)
            {
                case ActivationMarker:
                    return GetMarkerKindLabel("activation");
                case CaptureMarker:
                    return GetMarkerKindLabel("capture");
                case IconMarker icon:
                    return GetMarkerKindLabel(icon.Kind);
                default:
                    return "마커";
            }
        }

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
                Texture icon = entry.Kind switch
                {
                    "activation" => _activationMarkerPrefab != null ? _activationMarkerPrefab.texture : null,
                    "capture" => _captureMarkerPrefab != null ? _captureMarkerPrefab.texture : null,
                    _ => _iconMarkerPrefabsByKind != null && _iconMarkerPrefabsByKind.TryGetValue(entry.Kind, out var p) ? p.texture : null,
                };
                CreateMarkerBarButton(iconsRect, entry.Kind, icon);
            }

            CreateMarkerHintLabel(barRect);
            CreateScreenshotButton(barRect);
            CreateFitButton(barRect);
            CreateDiceButton(barRect);
            CreateRolloffButton(barRect);
            CreateUndoHistoryButton(barRect);
            CreateExitButton(barRect);
            CreateSaveButton(barRect);
            CreateMultiplayerButton(barRect);
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

        /// <summary>"저장" 버튼 바로 오른쪽(사용자 요청, 2026-09-02 — 처음엔
        /// 맨 왼쪽에 뒀다가 이 자리로 재배치) — 진행 중인 게임을 그대로 둔 채
        /// (씬 이동 없이) Entry의 "같이 하기"와 같은 모달을 연다
        /// (MultiplayerConnectDialog.Instance, GameFlowBootstrap이 영구
        /// 컴포넌트로 승격해둔 것 — 그 인스턴스 하나를 어느 씬에서든 그대로
        /// 재사용한다). 상대가 접속해 2명이 되면 CardPrep으로 보내지 않고
        /// 지금 보드 상태를 그대로 넘겨받아 둘 다 GameBoard에서 합류한다
        /// (MultiplayerConnectDialog.OnClientConnected/BoardManager.
        /// BroadcastFullStateForMidGameJoin 참고).</summary>
        private void CreateMultiplayerButton(Transform parent)
        {
            var go = new GameObject("MultiplayerButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f + GameConstants.MarkerBarHeight * 2f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/MultiplayButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => MultiplayerConnectDialog.Instance?.Open());
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
            _markerHintLabel.textWrappingMode = TextWrappingModes.NoWrap;
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

        /// <summary>다이스 버튼 바로 왼쪽(사용자 지정) — 롤 오프 모달(RolloffDialog)을
        /// 연다. 보드 상태와 무관한 독립 창이라 여기선 그냥 여는 것만 한다.</summary>
        private void CreateRolloffButton(Transform parent)
        {
            var go = new GameObject("RolloffButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - 3f * (GameConstants.MarkerBarHeight - 8f) - 18f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/RolloffButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (rolloffDialog != null)
                {
                    rolloffDialog.Open();
                }
            });
        }

        /// <summary>롤 오프 버튼 바로 왼쪽 — 되돌리기 모달(UndoHistoryDialog)을
        /// 연다(2026-09-01 신설 — 예전 Ctrl+Z 단축키를 대체). 보드 상태와
        /// 무관한 독립 창이라 여기선 그냥 여는 것만 한다.</summary>
        private void CreateUndoHistoryButton(Transform parent)
        {
            var go = new GameObject("UndoHistoryButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-10f - 4f * (GameConstants.MarkerBarHeight - 8f) - 24f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/UndoButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                if (undoHistoryDialog != null)
                {
                    undoHistoryDialog.Open(this);
                }
            });
        }

        private void CreateMarkerBarButton(Transform parent, string kind, Texture icon)
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

        /// <summary>kind에 맞는 마커 프리팹을 parent 아래에 인스턴스화한다
        /// (아직 위치/이벤트 연결은 안 함 — 배치 미리보기/실제 배치 양쪽에서
        /// 공용으로 쓴다). 텍스처/크기는 프리팹에 이미 채워져 있다.</summary>
        private MarkerBase CreateMarkerObject(string kind, Transform parent)
        {
            switch (kind)
            {
                case "activation":
                    return _activationMarkerPrefab != null ? Instantiate(_activationMarkerPrefab, parent, false) : null;
                case "capture":
                    return _captureMarkerPrefab != null ? Instantiate(_captureMarkerPrefab, parent, false) : null;
                default:
                    return _iconMarkerPrefabsByKind != null && _iconMarkerPrefabsByKind.TryGetValue(kind, out var prefab)
                            ? Instantiate(prefab, parent, false)
                            : null;
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
            // IsOverMissionObjective() 예외: 미션 목표 마커의 3인치 점령 링
            // 전체가 raycastTarget이라 그 위 클릭이 전부 "UI 위"로 잡혀서,
            // 마커가 놓인 자리에는 다른 마커를 배치할 수조차 없던 버그(사용자
            // 보고, 2026-09-02) — HandlePendingDeploymentInput의 같은 수정과
            // 동일한 패턴.
            if ((!IsPointerOverUi() || IsOverMissionObjective()) && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
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
            // 멀티플레이어 연결 중이면 호스트에게 배치를 요청하고 끝 —
            // 실제 마커 생성은 방송(PlaceMarkerRpc)이 도착했을 때
            // SpawnLocalMarkerVisual이 처리한다(호스트 자신도 이 경로를
            // 탄다). 되돌리기는 여기서 먼저 커밋해둔다 — CommitUndoTransaction이
            // 상대에게도 같은 항목을 방송하므로(BoardManager.UndoRedo.cs),
            // 마커가 실제로 화면에 나타나기 살짝 전에 스택에 먼저 올라가는
            // 셈이지만 그 시차는 무시할 수준이다.
            BeginUndoTransaction($"{GetMarkerKindLabel(kind)} 배치");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 마커 배치 요청을 못 보냄");
                    DiscardUndoTransaction();
                    return;
                }
                BoardNetworkSync.Instance.RequestPlaceMarkerServerRpc(kind, point);
                CommitUndoTransaction();
                return;
            }

            var marker = CreateMarkerObject(kind, markerLayer);
            marker.Center = ClampMarkerToMap(marker, point);
            WireMarkerEvents(marker, kind);
            CommitUndoTransaction();
        }

        private void WireMarkerEvents(MarkerBase marker, string kind)
        {
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
        }

        /// <summary>BoardNetworkSync.PlaceMarkerRpc가 방송을 받았을 때
        /// 호출한다(호스트 자신도 포함) — 완전히 로컬로(1인용 PlaceMarker와
        /// 같은 경로) 마커를 만든다. NetworkObject로 마커 자체를 스폰하지
        /// 않는 이유는 BoardNetworkSync.cs 클래스 주석 참고(NGO가
        /// NetworkObject를 일반 UI Transform 밑으로 재부모화하는 걸 막음).
        /// id는 호스트가 발급한 것 — 나중에 삭제 방송이 도착했을 때 이
        /// GameObject를 다시 찾는 데 쓴다.</summary>
        internal void SpawnLocalMarkerVisual(string kind, Vector2 point, int id)
        {
            var marker = CreateMarkerObject(kind, markerLayer);
            if (marker == null)
            {
                Debug.LogError($"[BoardManager] 마커 프리팹을 못 찾음: {kind}");
                return;
            }
            marker.Center = ClampMarkerToMap(marker, point);
            marker.NetworkMarkerId = id;
            _networkedMarkersById[id] = marker;
            WireMarkerEvents(marker, kind);
        }

        /// <summary>BoardNetworkSync.DeleteMarkerRpc가 방송을 받았을 때
        /// 호출한다(호스트 자신도 포함, 삭제를 요청한 쪽도 포함 — 요청자가
        /// 직접 지우지 않고 이 방송을 거쳐서 지운다).</summary>
        internal void DeleteLocalMarkerVisual(int id)
        {
            if (!_networkedMarkersById.TryGetValue(id, out var marker))
            {
                Debug.LogError($"[BoardManager] 삭제할 마커를 못 찾음(id={id})");
                return;
            }
            _networkedMarkersById.Remove(id);
            _markerMoveTweens.RemoveAll(t => t.Marker == marker);
            Destroy(marker.gameObject);
        }

        /// <summary>BoardNetworkSync.SetMarkerStateRpc가 방송을 받았을 때
        /// 호출한다(호스트 자신도 포함) — 활성화 마커(이동→돌격→완료)와
        /// 점령 마커(색 순환) 양쪽 다 이걸로 처리한다. 마커 자체가
        /// NetworkObject가 아니라 타입 정보를 따로 안 보내므로, 여기서
        /// 실제 타입을 보고 판별한다.</summary>
        internal void SetLocalMarkerState(int id, string state)
        {
            if (!_networkedMarkersById.TryGetValue(id, out var marker))
            {
                Debug.LogError($"[BoardManager] 상태를 바꿀 마커를 못 찾음(id={id})");
                return;
            }
            switch (marker)
            {
                case ActivationMarker activationMarker:
                    activationMarker.SetState(state);
                    break;
                case CaptureMarker captureMarker:
                    captureMarker.SetColorState(state);
                    break;
                default:
                    Debug.LogError($"[BoardManager] 상태 순환을 지원하지 않는 마커 타입(id={id})");
                    break;
            }
        }

        /// <summary>BoardNetworkSync.MoveMarkerRpc가 방송을 받았을 때
        /// 호출한다(드래그를 끝낸 쪽 자신도 포함 — 이미 그 자리에 있으므로
        /// 트윈이 사실상 아무 효과가 없다). 실시간 방송이 아니라 드래그
        /// 종료 시점 최종 위치 하나만 오므로(2026-08-30 사용자 요청 — 실시간일
        /// 필요 없다), 순간이동 대신 짧게 트윈해서 부드럽게 도착시킨다.</summary>
        internal void AnimateLocalMarkerMove(int id, Vector2 to)
        {
            if (!_networkedMarkersById.TryGetValue(id, out var marker))
            {
                Debug.LogError($"[BoardManager] 이동시킬 마커를 못 찾음(id={id})");
                return;
            }
            _markerMoveTweens.RemoveAll(t => t.Marker == marker);
            _markerMoveTweens.Add(new MarkerMoveTween
            {
                Marker = marker,
                From = marker.Center,
                To = to,
                StartTime = Time.time,
            });
        }

        /// <summary>매 프레임 BoardManager.Update()에서 호출 — 진행 중인 마커
        /// 이동 트윈을 전진시킨다. 삭제된 마커(Destroy됨)는 Unity의 null
        /// 비교 오버로드 덕에 여기서 그냥 걸러진다.</summary>
        private void UpdateMarkerMoveTweens()
        {
            for (int i = _markerMoveTweens.Count - 1; i >= 0; i--)
            {
                var tween = _markerMoveTweens[i];
                if (tween.Marker == null)
                {
                    _markerMoveTweens.RemoveAt(i);
                    continue;
                }
                float t = Mathf.Clamp01((Time.time - tween.StartTime) / MarkerMoveTweenDuration);
                float eased = t * t * (3f - 2f * t); // smoothstep
                tween.Marker.Center = Vector2.LerpUnclamped(tween.From, tween.To, eased);
                if (t >= 1f)
                {
                    _markerMoveTweens.RemoveAt(i);
                }
            }
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
            BeginUndoTransaction($"{DescribeMarkerKind(piece)} 이동");
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
                var marker = _draggingMarker;
                _draggingMarker = null;
                // 드래그 자체는(반응성 때문에) 언제나 로컬로 실시간 진행됐다 —
                // 여기선 그 최종 위치만 상대에게 알린다. 매 프레임 방송하지
                // 않는 이유는 실시간일 필요가 없다는 사용자 판단(2026-08-30) —
                // 대신 받는 쪽은 AnimateLocalMarkerMove로 부드럽게 그 자리로
                // 움직여준다.
                if (marker.NetworkMarkerId >= 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    if (BoardNetworkSync.Instance == null)
                    {
                        Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 마커 이동 요청을 못 보냄");
                    }
                    else
                    {
                        BoardNetworkSync.Instance.RequestMoveMarkerServerRpc(marker.NetworkMarkerId, marker.Center);
                    }
                }
                CommitUndoTransaction();
                return;
            }
            if (TryGetLocalMouse(out var mouseLocal))
            {
                var desired = mouseLocal + _markerDragOffset;
                _draggingMarker.Center = ClampMarkerToMap(_draggingMarker, desired);
            }
        }

        /// <summary>멀티 연결 중이고 이 마커가 네트워크로 배치된 것이면
        /// 삭제 방송을 요청하고 true를 반환한다 — 호출자는 이때 로컬
        /// Destroy를 건너뛰어야 한다(삭제는 이 요청이 되돌아오는 방송,
        /// BoardNetworkSync.DeleteMarkerRpc → DeleteLocalMarkerVisual을
        /// 거쳐서 일어난다). 미연결이면 false(호출자가 기존처럼 로컬로
        /// 바로 처리).</summary>
        private bool TryRequestNetworkMarkerDelete(MarkerBase marker)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return false;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 마커 삭제 요청을 못 보냄");
                return true; // 로컬 삭제도 막는다 — 안 그러면 다른 클라이언트와 화면이 어긋난다
            }
            BoardNetworkSync.Instance.RequestDeleteMarkerServerRpc(marker.NetworkMarkerId);
            return true;
        }

        /// <summary>TryRequestNetworkMarkerDelete와 같은 모양 — 활성화/점령
        /// 마커의 우클릭 상태 순환(이동→돌격→완료, 색 순환)용.</summary>
        private bool TryRequestNetworkMarkerStateChange(MarkerBase marker, string state)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return false;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 마커 상태 변경 요청을 못 보냄");
                return true;
            }
            BoardNetworkSync.Instance.RequestSetMarkerStateServerRpc(marker.NetworkMarkerId, state);
            return true;
        }

        private void OnActivationMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            var marker = (ActivationMarker)piece;
            if (shiftHeld)
            {
                BeginUndoTransaction($"{DescribeMarkerKind(marker)} 삭제");
                if (TryRequestNetworkMarkerDelete(marker))
                {
                    CommitUndoTransaction();
                    return;
                }
                Destroy(marker.gameObject);
                CommitUndoTransaction();
                return;
            }
            // 우클릭은 이동 → 돌격 → 완료를 계속 순환한다.
            int idx = System.Array.IndexOf(ActivationMarker.StateSequence, marker.State);
            string nextState = ActivationMarker.StateSequence[(idx + 1) % ActivationMarker.StateSequence.Length];
            BeginUndoTransaction($"{DescribeMarkerKind(marker)} 상태 변경");
            if (TryRequestNetworkMarkerStateChange(marker, nextState))
            {
                CommitUndoTransaction();
                return;
            }
            marker.SetState(nextState);
            CommitUndoTransaction();
        }

        private void OnCaptureMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            var marker = (CaptureMarker)piece;
            if (shiftHeld)
            {
                BeginUndoTransaction($"{DescribeMarkerKind(marker)} 삭제");
                if (TryRequestNetworkMarkerDelete(marker))
                {
                    CommitUndoTransaction();
                    return;
                }
                Destroy(marker.gameObject);
                CommitUndoTransaction();
                return;
            }
            // 우클릭은 흰색 → 빨간색 → 파란색을 계속 순환한다. 색 순환에
            // 종료 지점이 없어서(활성화 마커처럼 마지막에 사라지는 게 아님)
            // 삭제는 별도 입력으로 뺐다.
            int idx = System.Array.IndexOf(CaptureMarker.ColorSequence, marker.ColorState);
            string nextState = CaptureMarker.ColorSequence[(idx + 1) % CaptureMarker.ColorSequence.Length];
            BeginUndoTransaction($"{DescribeMarkerKind(marker)} 색 변경");
            if (TryRequestNetworkMarkerStateChange(marker, nextState))
            {
                CommitUndoTransaction();
                return;
            }
            marker.SetColorState(nextState);
            CommitUndoTransaction();
        }

        private void OnIconMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 순환 없이 우클릭 한 번으로 바로 삭제(shift 여부는 상관없다).
            BeginUndoTransaction($"{DescribeMarkerKind(piece)} 삭제");
            if (TryRequestNetworkMarkerDelete(piece))
            {
                CommitUndoTransaction();
                return;
            }
            Destroy(piece.gameObject);
            CommitUndoTransaction();
        }

    }
}
