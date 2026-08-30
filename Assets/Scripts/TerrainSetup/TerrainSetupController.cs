using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 지형 셋업 화면(2026-08-30 재구성으로 신설) — Selection에서 고른 배치
    /// 프리셋(MapData: 지도 크기/배치구역/미션 목표)을 읽기 전용 참고로
    /// 보여주면서, 지형만 이 게임 한 판을 위해 새로 배치한다(사용자 지정 —
    /// 지형은 프리셋으로 저장하지 않고 매 게임 새로). 지형 배치 코드 자체는
    /// 옛 MapSetupController(현 MapAuthoringController)에서 그대로 옮겨왔다
    /// — 팔레트/스냅/휠회전/드래그/우클릭삭제 전부 동일한 동작.
    ///
    /// 배치구역/미션 목표는 편집 불가능한 순수 참고용이다 — 우클릭으로
    /// 지워지거나 클릭에 반응하면 안 되므로, DeploymentZonePiece는
    /// raycastTarget=false로(그 컴포넌트 자신의 OnPointerDown이 우클릭 시
    /// 바로 Destroy를 호출하는 자체-삭제 로직이라 이렇게 막지 않으면 지형을
    /// 배치하다 실수로 지워질 수 있다), MissionObjectivePiece는 게임 보드가
    /// 이미 쓰는 것과 같은 패턴(AllowDrag=false, Drag/DeleteRequested를
    /// 구독하지 않음)으로 사실상 무해화한다.
    ///
    /// "게임 시작 ▶"을 누르면 지금 배치된 지형을 MapData.TerrainPieces에
    /// 채우고 Completed를 올린다 — 실제 씬 전환(GameBoard로)은 이 컴포넌트를
    /// 만든 쪽(부트스트랩)이 담당한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TerrainSetupController : MonoBehaviour
    {
        public event Action Completed;
        public event Action BackRequested;

        private const float MarginPx = 8f;
        private const float TerrainGridSizeMm = GameConstants.MmPerInch / 2f; // 0.5인치

        private const float ZoomStep = 1.1f;
        private const float MinZoom = 0.3f;
        private const float MaxZoom = 4f;

        private RectTransform Root => (RectTransform)transform;

        private RectTransform _mapArea;
        private RectTransform _mapBackgroundRect;
        private RectTransform _gridRect;
        private RectTransform _referenceLayer;
        private RectTransform _terrainLayer;

        private readonly Dictionary<string, Button> _terrainButtons = new Dictionary<string, Button>();

        // 멀티플레이어 중 네트워크로 배치된 지형만 등록됨(id는
        // BoardNetworkSync가 발급) — 이동/회전/삭제 방송이 도착했을 때 어느
        // GameObject인지 찾는 용도(마커의 _networkedMarkersById와 같은 패턴).
        private readonly Dictionary<int, TerrainPiece> _networkedTerrainById = new Dictionary<int, TerrainPiece>();

        private string _placementModuleId = "";
        private TerrainPiece _draggingPiece;
        private Vector2 _dragPieceOffset;

        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private bool _panning;
        private Vector2 _lastPanScreenPos;
        private float _zoomLevel = 1f;
        private float _baseScaleFactor = 1f;

        private Vector2 MapSize => GameConstants.MapSizePresets.TryGetValue(MapData.MapPreset, out var sz)
                ? sz
                : GameConstants.MapSizePresets[GameConstants.DefaultMapSizePreset];

        private void Start()
        {
            // 지도(_mapArea)를 팔레트/상단 버튼보다 먼저 추가해야 나중에 추가된
            // 형제가 위에 그려지는 순서상 지도가 배경으로 버튼들을 덮지 않는다
            // (MapAuthoringController와 동일한 이유).
            BuildMapArea();
            BuildTopRow();
            BuildPalette();
            DrawReference();
            UpdateMapLayout();
        }

        private void Update()
        {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
                UpdateMapLayout();
            }

            HandlePanInput();

            if (_pendingRotationPiece != null && Time.time >= _pendingRotationDeadline)
            {
                FlushPendingRotationBroadcast();
            }

            float scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f) && TryGetLocalMouse(out var wheelLocal))
            {
                var hovered = FindTerrainPieceAt(wheelLocal);
                if (hovered != null)
                {
                    hovered.RotateStep(scroll > 0f ? 1 : -1);
                    RequestNetworkTerrainRotateIfNeeded(hovered);
                }
                else if (!IsPointerOverUi())
                {
                    ZoomAt(Input.mousePosition, scroll > 0f ? ZoomStep : 1f / ZoomStep);
                }
            }

            if (_draggingPiece != null)
            {
                HandleDragInput();
                return;
            }

            if (_placementModuleId != "" && Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                HandlePlacementClick();
            }
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private bool TryGetLocalMouse(out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapArea, Input.mousePosition, null, out local);
        }

        private void HandlePanInput()
        {
            if (_mapArea == null)
            {
                return;
            }
            var parent = _mapArea.parent as RectTransform;
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
                    _mapArea.anchoredPosition += curLocal - prevLocal;
                }
                _lastPanScreenPos = currentScreenPos;
            }
        }

        private void ZoomAt(Vector2 screenPos, float factor)
        {
            if (_mapArea == null)
            {
                return;
            }
            _zoomLevel = MapZoomUtil.ApplyZoomAtScreenPoint(_mapArea, screenPos, _zoomLevel, _baseScaleFactor, factor, MinZoom, MaxZoom);
        }

        // ── UI 빌드 ──────────────────────────────────────────────────

        private void BuildTopRow()
        {
            var rowGo = new GameObject("TopRow", typeof(RectTransform));
            rowGo.transform.SetParent(Root, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(MarginPx, -MarginPx);
            rowRect.sizeDelta = new Vector2(440f, 32f);

            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            // "돌아가기" — 바로 이전 화면(Selection)으로 돌아간다(사용자
            // 지정). MapData/MissionSettingsData는 여기서 아무것도 지우지
            // 않으므로 Selection의 선택 상태가 그대로 유지된다 — 그 화면
            // 자신의 static 필드(s_pickedMapName 등)가 다시 화면을 그릴 때
            // 복원한다.
            CreateIconButton(rowRect, Resources.Load<Texture2D>("UI/BackButton"), 32f, () => BackRequested?.Invoke());

            var titleLabel = CreateLabel(rowRect, "지형을 배치한 뒤 게임을 시작하세요");
            var titleLe = titleLabel.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredWidth = 260f;
            titleLe.preferredHeight = 32f;

            CreateButton(rowRect, "게임 시작 >", OnStartGamePressed);
        }

        private void BuildPalette()
        {
            var panelGo = new GameObject("Palette", typeof(RectTransform));
            panelGo.transform.SetParent(Root, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(MarginPx, -(MarginPx + 32f + MarginPx));
            panelRect.sizeDelta = new Vector2(180f, 40f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var terrainLabel = CreateLabel(panelRect, "지형 (휠로 회전, 우클릭 메뉴로 삭제)");
            var terrainLabelLe = terrainLabel.gameObject.AddComponent<LayoutElement>();
            terrainLabelLe.preferredHeight = 32f;

            foreach (var module in TerrainCatalog.Modules)
            {
                string capturedId = module.Id;
                var btn = CreateButton(panelRect, module.DisplayName, () => OnTerrainButtonToggled(capturedId));
                var btnLe = btn.gameObject.AddComponent<LayoutElement>();
                btnLe.preferredHeight = 32f;
                _terrainButtons[module.Id] = btn;
            }
        }

        private void BuildMapArea()
        {
            var areaGo = new GameObject("MapArea", typeof(RectTransform));
            areaGo.transform.SetParent(Root, false);
            _mapArea = (RectTransform)areaGo.transform;
            _mapArea.anchorMin = Vector2.zero;
            _mapArea.anchorMax = Vector2.zero;
            _mapArea.pivot = Vector2.zero;

            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(_mapArea, false);
            _mapBackgroundRect = (RectTransform)bgGo.transform;
            _mapBackgroundRect.anchorMin = Vector2.zero;
            _mapBackgroundRect.anchorMax = Vector2.zero;
            _mapBackgroundRect.pivot = Vector2.zero;
            _mapBackgroundRect.anchoredPosition = Vector2.zero;
            _mapBackgroundRect.sizeDelta = MapSize;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.15f, 0.18f, 0.15f, 1f);
            bgImg.raycastTarget = false;

            var gridGo = new GameObject("Grid", typeof(RectTransform));
            gridGo.transform.SetParent(_mapArea, false);
            _gridRect = (RectTransform)gridGo.transform;
            _gridRect.anchorMin = Vector2.zero;
            _gridRect.anchorMax = Vector2.zero;
            _gridRect.pivot = Vector2.zero;
            _gridRect.anchoredPosition = Vector2.zero;
            _gridRect.sizeDelta = MapSize;
            gridGo.AddComponent<MapGridOverlay>();

            var referenceLayerGo = new GameObject("ReferenceLayer", typeof(RectTransform));
            referenceLayerGo.transform.SetParent(_mapArea, false);
            _referenceLayer = (RectTransform)referenceLayerGo.transform;
            _referenceLayer.anchorMin = Vector2.zero;
            _referenceLayer.anchorMax = Vector2.zero;
            _referenceLayer.pivot = Vector2.zero;
            _referenceLayer.anchoredPosition = Vector2.zero;
            _referenceLayer.sizeDelta = MapSize;

            var terrainLayerGo = new GameObject("TerrainLayer", typeof(RectTransform));
            terrainLayerGo.transform.SetParent(_mapArea, false);
            _terrainLayer = (RectTransform)terrainLayerGo.transform;
            _terrainLayer.anchorMin = Vector2.zero;
            _terrainLayer.anchorMax = Vector2.zero;
            _terrainLayer.pivot = Vector2.zero;
            _terrainLayer.anchoredPosition = Vector2.zero;
            _terrainLayer.sizeDelta = MapSize;
        }

        /// <summary>지도가 팔레트/상단 바를 뺀 남는 뷰포트 안에 정중앙으로
        /// 들어오도록 배치한다 — MapAuthoringController.UpdateMapLayout과
        /// 완전히 같은 계산(오른쪽엔 프리셋 목록 패널이 없으므로 그 항만 뺐다).</summary>
        private void UpdateMapLayout()
        {
            if (_mapArea == null)
            {
                return;
            }
            float left = MarginPx + 180f + MarginPx;
            float top = MarginPx + 32f + MarginPx;
            float viewportW = Mathf.Max(Screen.width - left - MarginPx, 10f);
            float viewportH = Mathf.Max(Screen.height - top - MarginPx, 10f);

            Vector2 mapSize = MapSize;
            float scale = Mathf.Min(viewportW / mapSize.x, viewportH / mapSize.y);
            scale = Mathf.Min(scale, 1f);
            _baseScaleFactor = scale;

            float totalScale = scale * _zoomLevel;
            _mapArea.localScale = new Vector3(totalScale, totalScale, 1f);
            float extraW = Mathf.Max(viewportW - mapSize.x * totalScale, 0f);
            float extraH = Mathf.Max(viewportH - mapSize.y * totalScale, 0f);
            _mapArea.anchoredPosition = new Vector2(left + extraW / 2f, MarginPx + extraH / 2f);
        }

        /// <summary>Selection에서 고른 배치 프리셋(MapData)의 배치구역/미션
        /// 목표를 읽기 전용 참고로 그린다 — 한 번만 부르면 된다(이 화면
        /// 안에서는 절대 바뀌지 않는 고정 데이터).</summary>
        private void DrawReference()
        {
            foreach (var z in MapData.DeploymentZones)
            {
                var piece = CreateZonePiece(z.Player);
                piece.Edge = z.Edge;
                piece.StartAlong = z.StartAlong;
                piece.EndAlong = z.EndAlong;
                // raycastTarget=false — DeploymentZonePiece 자신의 OnPointerDown이
                // 우클릭 시 곧바로 Destroy(gameObject)를 호출하는 자체-삭제
                // 로직이라, 그냥 두면 지형을 배치하다 실수로 참고선을 지울 수
                // 있다(이벤트 구독 여부와 무관하게 자체적으로 지워짐 — 다른
                // 조각들처럼 외부 이벤트에만 의존하지 않는다는 게 차이).
                piece.raycastTarget = false;
                ApplyZoneGeometry(piece, z.Edge, z.StartAlong, z.EndAlong - z.StartAlong, MapSize);
            }

            foreach (var o in MapData.MissionObjectives)
            {
                var go = new GameObject($"Objective_{o.Number}", typeof(RectTransform));
                go.transform.SetParent(_referenceLayer, false);
                var piece = go.AddComponent<MissionObjectivePiece>();
                piece.Number = o.Number;
                var baseColor = GameConstants.GetMissionObjectiveBaseColor(o.Number);
                piece.TokenColor = GameConstants.Muted(baseColor, GameConstants.MissionObjectiveSaturationFactor, GameConstants.MissionObjectiveValueFactor);
                piece.RectTransform.anchorMin = Vector2.zero;
                piece.RectTransform.anchorMax = Vector2.zero;
                float diameter = GameConstants.MissionObjectiveTokenDiameterMm
                        + 2f * GameConstants.MissionObjectiveCaptureMarginInch * GameConstants.MmPerInch;
                piece.RectTransform.sizeDelta = new Vector2(diameter, diameter);
                piece.Center = o.Position;
                // AllowDrag=false(기본 false 아님, 명시) + Drag/DeleteRequested를
                // 구독하지 않는다 — 왼쪽 클릭은 AllowDrag 가드에서 막히고,
                // 오른쪽 클릭은 이벤트를 "올리려는 시도"는 하지만 구독자가
                // 없어 실제로는 아무 일도 안 일어난다(게임 보드가 이미 쓰는
                // 것과 같은 무해화 패턴 — BoardManager.MissionObjectives.cs 참고).
                piece.AllowDrag = false;
            }
        }

        // ── 지형 배치 ────────────────────────────────────────────────────
        // MapAuthoringController(예전 MapSetupController)에서 그대로 옮겨온
        // 코드 — 물리/충돌 없는 순수 시각 참고용 배치, 휠로 회전, 우클릭으로
        // 바로 삭제.

        private void OnTerrainButtonToggled(string moduleId)
        {
            bool nowActive = _placementModuleId != moduleId;
            _placementModuleId = nowActive ? moduleId : "";
            foreach (var kv in _terrainButtons)
            {
                SetButtonHighlighted(kv.Value, nowActive && kv.Key == moduleId);
            }
        }

        private void ClearTerrainPlacementMode()
        {
            _placementModuleId = "";
            foreach (var kv in _terrainButtons)
            {
                SetButtonHighlighted(kv.Value, false);
            }
        }

        private void HandlePlacementClick()
        {
            if (!TryGetLocalMouse(out var local))
            {
                return;
            }
            Vector2 mapSize = MapSize;
            if (local.x < 0f || local.x > mapSize.x || local.y < 0f || local.y > mapSize.y)
            {
                return;
            }

            string moduleId = _placementModuleId;
            Vector2 snapped = SnapToGrid(local);
            ClearTerrainPlacementMode();

            // 멀티 연결 중이면 호스트에게 배치를 요청하고 끝 — 실제 생성은
            // 방송(BoardNetworkSync.PlaceTerrainRpc)이 도착했을 때
            // SpawnLocalTerrainVisual이 처리한다(호스트 자신도 이 경로를
            // 탄다, 마커 배치와 완전히 같은 패턴).
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[TerrainSetupController] BoardNetworkSync.Instance가 없음 — 지형 배치 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestPlaceTerrainServerRpc(moduleId, snapped);
                return;
            }

            PlacePiece(moduleId, snapped);
        }

        private TerrainPiece PlacePiece(string moduleId, Vector2 localPoint, int networkId = -1)
        {
            var module = TerrainCatalog.Get(moduleId);
            if (module == null)
            {
                return null;
            }

            var go = new GameObject($"Terrain_{module.Id}", typeof(RectTransform));
            go.transform.SetParent(_terrainLayer, false);
            var piece = go.AddComponent<TerrainPiece>();
            piece.RectTransform.anchorMin = Vector2.zero;
            piece.RectTransform.anchorMax = Vector2.zero;
            piece.Setup(module);
            piece.Center = localPoint;
            piece.NetworkTerrainId = networkId;
            if (networkId >= 0)
            {
                _networkedTerrainById[networkId] = piece;
            }
            piece.DragRequested += OnPieceDragRequested;
            piece.DeleteRequested += OnPieceDeleteRequested;
            return piece;
        }

        /// <summary>BoardNetworkSync.PlaceTerrainRpc가 방송을 받았을 때
        /// 호출한다(배치를 요청한 쪽 자신도 포함 — 마커와 동일하게, 요청자가
        /// 직접 만들지 않고 이 방송을 거쳐서 만든다).</summary>
        internal void SpawnLocalTerrainVisual(string moduleId, Vector2 point, int id)
        {
            if (PlacePiece(moduleId, point, id) == null)
            {
                Debug.LogError($"[TerrainSetupController] 지형 모듈을 못 찾음: {moduleId}");
            }
        }

        /// <summary>BoardNetworkSync.MoveTerrainRpc가 방송을 받았을 때
        /// 호출한다(옮긴 쪽 자신도 포함 — 이미 그 자리에 있으므로 사실상
        /// 아무 효과가 없다). 마커와 달리 부드러운 트윈 없이 즉시 반영한다 —
        /// 지형은 자주 옮기지 않는 배치 단계 전용이라 스냅으로 충분하다.</summary>
        internal void MoveLocalTerrainVisual(int id, Vector2 point)
        {
            if (!_networkedTerrainById.TryGetValue(id, out var piece))
            {
                Debug.LogError($"[TerrainSetupController] 이동시킬 지형을 못 찾음(id={id})");
                return;
            }
            piece.Center = point;
        }

        /// <summary>BoardNetworkSync.RotateTerrainRpc가 방송을 받았을 때
        /// 호출한다 — 상대 값(휠 한 칸)이 아니라 절대 각도를 그대로 반영해서
        /// 메시지 유실에도 어긋나지 않는다.</summary>
        internal void RotateLocalTerrainVisual(int id, float rotationDeg)
        {
            if (!_networkedTerrainById.TryGetValue(id, out var piece))
            {
                Debug.LogError($"[TerrainSetupController] 회전시킬 지형을 못 찾음(id={id})");
                return;
            }
            piece.RotationDegrees = rotationDeg;
        }

        /// <summary>BoardNetworkSync.DeleteTerrainRpc가 방송을 받았을 때
        /// 호출한다(삭제를 요청한 쪽도 포함 — 마커 삭제와 동일한 패턴).</summary>
        internal void DeleteLocalTerrainVisual(int id)
        {
            if (!_networkedTerrainById.TryGetValue(id, out var piece))
            {
                Debug.LogError($"[TerrainSetupController] 삭제할 지형을 못 찾음(id={id})");
                return;
            }
            _networkedTerrainById.Remove(id);
            Destroy(piece.gameObject);
        }

        // 휠을 빠르게 연속으로 굴리면 매 틱마다 방송 왕복이 겹쳐서, 뒤늦게
        // 도착한 예전 각도가 그 사이 더 진행된 로컬 회전을 덮어써 "드드득"
        // 떨리는 것처럼 보였다(사용자 발견). 그래서 회전 자체는 매 틱 즉시
        // 로컬 반영하되(반응성 유지), 방송은 마지막 조작 후 이 시간만큼
        // 조용해야만 한 번 나간다 — 그 사이 새 휠 조작이 들어오면 마감시각을
        // 다시 미룬다("연속된 조작이 끝나면 그때서야 공유", 사용자 지정).
        private const float RotationBroadcastDebounceSeconds = 0.5f;
        private TerrainPiece _pendingRotationPiece;
        private float _pendingRotationDeadline = -1f;

        /// <summary>휠로 회전시킨 지형이 네트워크로 배치된 것이면(멀티 연결
        /// 중) 방송을 예약한다 — 회전 자체는 이미 로컬에서 즉시 반영된
        /// 뒤이므로(반응성 우선, 마커 드래그와 같은 이유) 여기선 "언젠가
        /// 상대에게 알리기"만 디바운스해서 미룬다.</summary>
        private void RequestNetworkTerrainRotateIfNeeded(TerrainPiece piece)
        {
            if (piece.NetworkTerrainId < 0 || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            // 조작 중이던 조각이 바뀌면(다른 조각을 돌리기 시작하면) 이전
            // 조각 걸 무기한 미루지 않고 지금 바로 흘려보낸다.
            if (_pendingRotationPiece != null && _pendingRotationPiece != piece)
            {
                FlushPendingRotationBroadcast();
            }
            _pendingRotationPiece = piece;
            _pendingRotationDeadline = Time.time + RotationBroadcastDebounceSeconds;
        }

        /// <summary>예약된 회전 방송이 있으면 지금 바로 내보낸다 — 디바운스
        /// 마감시각이 됐을 때(Update()) 또는 다른 조각으로 조작이 옮겨갈
        /// 때, 그리고 "게임 시작"으로 이 화면을 떠나기 직전(마지막 회전이
        /// 아직 대기 중일 때 놓치지 않도록) 호출한다.</summary>
        private void FlushPendingRotationBroadcast()
        {
            var piece = _pendingRotationPiece;
            _pendingRotationPiece = null;
            _pendingRotationDeadline = -1f;
            if (piece == null) // 대기 중 우클릭 삭제 등으로 이미 없어졌을 수 있다.
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[TerrainSetupController] BoardNetworkSync.Instance가 없음 — 지형 회전 요청을 못 보냄");
                return;
            }
            BoardNetworkSync.Instance.RequestRotateTerrainServerRpc(piece.NetworkTerrainId, piece.RotationDegrees);
        }

        private void HandleDragInput()
        {
            if (TryGetLocalMouse(out var local))
            {
                _draggingPiece.Center = SnapToGrid(local + _dragPieceOffset);
            }
            if (Input.GetMouseButtonUp(0))
            {
                var piece = _draggingPiece;
                _draggingPiece = null;
                // 드래그 자체는 반응성 때문에 항상 로컬 실시간 진행 —
                // 여기선 드래그를 끝낸 최종 위치만 상대에게 알린다(마커
                // 드래그-종료 방송과 같은 이유).
                if (piece.NetworkTerrainId >= 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                {
                    if (BoardNetworkSync.Instance == null)
                    {
                        Debug.LogError("[TerrainSetupController] BoardNetworkSync.Instance가 없음 — 지형 이동 요청을 못 보냄");
                    }
                    else
                    {
                        BoardNetworkSync.Instance.RequestMoveTerrainServerRpc(piece.NetworkTerrainId, piece.Center);
                    }
                }
            }
        }

        private void OnPieceDragRequested(TerrainPiece piece)
        {
            _draggingPiece = piece;
            TryGetLocalMouse(out var local);
            _dragPieceOffset = piece.Center - local;
            piece.transform.SetAsLastSibling();
        }

        private void OnPieceDeleteRequested(TerrainPiece piece)
        {
            // 멀티 연결 중이고 이 지형이 네트워크로 배치된 것이면 삭제
            // 방송을 요청하고 끝 — 실제 삭제는 이 요청이 되돌아오는 방송
            // (DeleteLocalTerrainVisual)을 거쳐서 일어난다(마커 삭제와 동일).
            if (piece.NetworkTerrainId >= 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[TerrainSetupController] BoardNetworkSync.Instance가 없음 — 지형 삭제 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestDeleteTerrainServerRpc(piece.NetworkTerrainId);
                return;
            }
            Destroy(piece.gameObject);
        }

        private TerrainPiece FindTerrainPieceAt(Vector2 point)
        {
            for (int i = _terrainLayer.childCount - 1; i >= 0; i--)
            {
                var piece = _terrainLayer.GetChild(i).GetComponent<TerrainPiece>();
                if (piece == null)
                {
                    continue;
                }
                float rad = -piece.RotationDegrees * Mathf.Deg2Rad;
                Vector2 offset = point - piece.Center;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);
                Vector2 local = new Vector2(offset.x * cos - offset.y * sin, offset.x * sin + offset.y * cos);
                Vector2 half = piece.RectTransform.sizeDelta / 2f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y)
                {
                    return piece;
                }
            }
            return null;
        }

        private static Vector2 SnapToGrid(Vector2 point)
        {
            return new Vector2(
                    Mathf.Round(point.x / TerrainGridSizeMm) * TerrainGridSizeMm,
                    Mathf.Round(point.y / TerrainGridSizeMm) * TerrainGridSizeMm);
        }

        // ── 배치구역 참고선 지오메트리 ───────────────────────────────────
        // MapAuthoringController.ApplyZoneGeometry/CreateZonePiece와 완전히
        // 같은 계산 — edge/along 구간(mm)을 실제 RectTransform으로 바꾼다.

        private const float ZoneHitThicknessMm = 24f;

        private static readonly Dictionary<string, Color> ZoneColors = new Dictionary<string, Color>
        {
            { "A", new Color(1f, 0.15f, 0.15f, 0.9f) },
            { "B", new Color(0.15f, 0.35f, 1f, 0.9f) },
        };

        private DeploymentZonePiece CreateZonePiece(string player)
        {
            var go = new GameObject($"Zone_{player}", typeof(RectTransform));
            go.transform.SetParent(_referenceLayer, false);
            var piece = go.AddComponent<DeploymentZonePiece>();
            piece.OwnerPlayer = player;
            piece.LineColor = ZoneColors.TryGetValue(player, out var c) ? c : Color.white;
            return piece;
        }

        private static void ApplyZoneGeometry(DeploymentZonePiece piece, string edge, float a, float length, Vector2 mapSize)
        {
            var rt = piece.RectTransform;
            switch (edge)
            {
                case "left":
                    rt.anchoredPosition = new Vector2(-ZoneHitThicknessMm / 2f, a);
                    rt.sizeDelta = new Vector2(ZoneHitThicknessMm, length);
                    break;
                case "right":
                    rt.anchoredPosition = new Vector2(mapSize.x - ZoneHitThicknessMm / 2f, a);
                    rt.sizeDelta = new Vector2(ZoneHitThicknessMm, length);
                    break;
                case "top":
                    rt.anchoredPosition = new Vector2(a, -ZoneHitThicknessMm / 2f);
                    rt.sizeDelta = new Vector2(length, ZoneHitThicknessMm);
                    break;
                case "bottom":
                    rt.anchoredPosition = new Vector2(a, mapSize.y - ZoneHitThicknessMm / 2f);
                    rt.sizeDelta = new Vector2(length, ZoneHitThicknessMm);
                    break;
            }
        }

        // ── 완료 ────────────────────────────────────────────────────

        private void OnStartGamePressed()
        {
            // 디바운스 중이던 회전이 있으면 지금 흘려보낸다 — 안 그러면 막
            // 돌린 지형의 마지막 각도가 상대에게 영영 전달되지 않을 수 있다.
            if (_pendingRotationPiece != null)
            {
                FlushPendingRotationBroadcast();
            }

            // 멀티 연결 중이면 양쪽 다 같은 순간에 GameBoard로 넘어가야
            // 하므로 신호를 방송한다 — 지형은 이미 실시간 동기화돼 있으므로
            // 스냅샷을 따로 보내지 않고, 방송을 받은 각자가 자기 화면의
            // 지형으로 채운다(CompleteFromNetwork).
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[TerrainSetupController] BoardNetworkSync.Instance가 없음 — 게임 시작 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestStartGameServerRpc();
                return;
            }
            FillMapDataAndComplete();
        }

        /// <summary>BoardNetworkSync.StartGameRpc가 방송을 받았을 때(누른
        /// 쪽 자신도 포함) 호출한다.</summary>
        internal void CompleteFromNetwork()
        {
            FillMapDataAndComplete();
        }

        private void FillMapDataAndComplete()
        {
            MapData.TerrainPieces.Clear();
            for (int i = 0; i < _terrainLayer.childCount; i++)
            {
                var piece = _terrainLayer.GetChild(i).GetComponent<TerrainPiece>();
                if (piece == null)
                {
                    continue;
                }
                MapData.TerrainPieces.Add(new TerrainPieceData
                {
                    ModuleId = piece.ModuleId,
                    Position = piece.Center,
                    RotationDeg = piece.RotationDegrees,
                });
            }
            Completed?.Invoke();
        }

        // ── 공용 위젯 ────────────────────────────────────────────────

        private static TextMeshProUGUI CreateLabel(RectTransform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 12f;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        private static Button CreateButton(RectTransform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(140f, 32f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 14f;
            text.color = Color.white;
            text.raycastTarget = false;

            return btn;
        }

        /// <summary>텍스트 대신 아이콘 하나로 된 정사각형 버튼 — MapAuthoringController/
        /// MissionAuthoringController의 "엔트리로"/"프리셋 저장" 버튼과 같은 스타일
        /// (배경색 없이 아이콘 자체만).</summary>
        private static Button CreateIconButton(RectTransform parent, Texture2D icon, float size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("IconBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(size, size);

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            return btn;
        }

        private static void SetButtonHighlighted(Button btn, bool highlighted)
        {
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = highlighted ? new Color(0.5f, 0.5f, 0.5f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
            }
        }
    }
}
