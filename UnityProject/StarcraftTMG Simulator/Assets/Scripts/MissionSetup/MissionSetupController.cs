using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 미션 생성 화면 — 지도 크기 선택 + 배치구역(팀별 지도 가장자리 구간)
    /// 그리기 + 미션 목표(1~5번) 배치. Godot판
    /// scenes/mission_setup/MissionSetup.gd 포팅 — 지형 배치는 아직 없다
    /// (텍스처/마스크 이미지 에셋이 필요해서 다음 단계). "게임 시작"을 누르면
    /// MissionData에 스냅샷을 채우고
    /// StartGameRequested를 올린다 — 실제 화면 전환은 이 컴포넌트를 만든 쪽
    /// (부트스트랩)이 그 이벤트를 구독해서 처리한다.
    ///
    /// 이 컴포넌트가 붙은 GameObject 자체가 화면 전체를 덮는 RectTransform
    /// (Canvas의 직속 자식, anchors (0,0)-(1,1))이라고 가정한다 — BoardManager
    /// 처럼 별도 baseLayer 참조를 안 받고 자기 자신의 transform을 기준으로
    /// UI를 짓는다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MissionSetupController : MonoBehaviour
    {
        public event Action StartGameRequested;

        private const float EdgeSnapThresholdMm = 15f;
        private const float ZoneHitThicknessMm = 24f;
        private const float MarginPx = 8f;

        private static readonly Dictionary<string, Color> ZoneColors = new Dictionary<string, Color>
        {
            { "A", new Color(1f, 0.15f, 0.15f, 0.9f) },
            { "B", new Color(0.15f, 0.35f, 1f, 0.9f) },
        };

        private RectTransform Root => (RectTransform)transform;

        private RectTransform _mapArea;
        private RectTransform _mapBackgroundRect;
        private RectTransform _gridRect;
        private RectTransform _zoneLayer;
        private RectTransform _terrainLayer;
        private RectTransform _objectiveLayer;

        private readonly Dictionary<string, Button> _sizeButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _terrainButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _zoneButtons = new Dictionary<string, Button>();
        private readonly Dictionary<int, Button> _objectiveButtons = new Dictionary<int, Button>();
        private readonly Dictionary<int, MissionObjectivePiece> _objectivePieces = new Dictionary<int, MissionObjectivePiece>();

        private string _currentPreset = GameConstants.DefaultMapSizePreset;
        private string _placementModuleId = "";
        private string _activeZonePlayer = "";
        private int _activeObjectiveNumber;

        private TerrainPiece _draggingPiece;
        private Vector2 _dragPieceOffset;

        private MissionObjectivePiece _draggingObjective;
        private Vector2 _dragObjectiveOffset;

        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private DeploymentZonePiece _drawingZone;
        private float _zoneCurrentLength;
        private Vector2 _zoneDownLocal;
        private string _zoneLockedEdge = "";
        private string _zoneCandidateH = "";
        private string _zoneCandidateV = "";

        private Vector2 MapSize => GameConstants.MapSizePresets[_currentPreset];

        private void Start()
        {
            // 지도(_map_area)를 좌측/상단 메뉴들보다 먼저 추가해야 한다 —
            // 나중에 추가된 형제 노드가 위에 그려지므로, 순서가 반대면 지도가
            // 배경으로 버튼들을 덮어버린다(Godot판 MissionSetup.gd의 동일한
            // 주석 참고).
            BuildMapArea();
            BuildSizeRow();
            BuildPalette();
            ApplyPreset(_currentPreset);
        }

        private void Update()
        {
            if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight)
            {
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
                UpdateMapLayout();
            }

            // 이 화면엔 아직 팬/줌이 없어서(게임 보드와 다름), 지형 조각 위에서
            // 휠을 굴리면 그냥 무조건 회전한다 — Godot판처럼 "지형 위면 회전,
            // 아니면 줌"으로 분기할 필요가 없다.
            float scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f) && TryGetLocalMouse(out var wheelLocal))
            {
                var hovered = FindTerrainPieceAt(wheelLocal);
                if (hovered != null)
                {
                    hovered.RotateStep(scroll > 0f ? 1 : -1);
                }
            }

            if (_draggingPiece != null)
            {
                HandleDragInput();
                return;
            }

            if (_draggingObjective != null)
            {
                HandleObjectiveDragInput();
                return;
            }

            if (_drawingZone != null)
            {
                if (TryGetLocalMouse(out var local))
                {
                    UpdateZoneDrawing(local);
                }
                if (Input.GetMouseButtonUp(0))
                {
                    FinishZoneDrawing();
                }
                return;
            }

            if (_activeZonePlayer != "" && Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                if (TryGetLocalMouse(out var local))
                {
                    HandleZoneStartClick(local);
                }
                return;
            }

            if (_activeObjectiveNumber != 0 && Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                HandleObjectivePlacementClick();
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

        // ── UI 빌드 ──────────────────────────────────────────────────

        private void BuildSizeRow()
        {
            var rowGo = new GameObject("SizeRow", typeof(RectTransform));
            rowGo.transform.SetParent(Root, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(MarginPx, -MarginPx);
            rowRect.sizeDelta = new Vector2(600f, 32f);

            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            foreach (var presetName in GameConstants.MapSizePresets.Keys)
            {
                string captured = presetName;
                var btn = CreateButton(rowRect, PresetLabel(presetName), () => OnSizePresetPressed(captured));
                _sizeButtons[presetName] = btn;
            }

            CreateButton(rowRect, "게임 시작 ▶", OnStartGamePressed);
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

            var instructionLabel = CreateLabel(panelRect, "배치구역 (지도 가장자리에서 드래그, 우클릭으로 삭제)");
            var instructionLe = instructionLabel.gameObject.AddComponent<LayoutElement>();
            instructionLe.preferredHeight = 44f;

            foreach (var player in new[] { "A", "B" })
            {
                var btn = CreateZoneToggleButton(panelRect, player);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 32f;
                _zoneButtons[player] = btn;
            }

            var objectiveLabel = CreateLabel(panelRect, "미션 목표");
            var objectiveLabelLe = objectiveLabel.gameObject.AddComponent<LayoutElement>();
            objectiveLabelLe.preferredHeight = 20f;

            foreach (var number in GameConstants.MissionObjectiveNumbers)
            {
                int captured = number;
                var btn = CreateButton(panelRect, $"목표 {number}", () => OnObjectiveButtonClicked(captured));
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 32f;
                _objectiveButtons[number] = btn;
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
            gridGo.AddComponent<MapGridOverlay>();

            var zoneLayerGo = new GameObject("ZoneLayer", typeof(RectTransform));
            zoneLayerGo.transform.SetParent(_mapArea, false);
            _zoneLayer = (RectTransform)zoneLayerGo.transform;
            _zoneLayer.anchorMin = Vector2.zero;
            _zoneLayer.anchorMax = Vector2.zero;
            _zoneLayer.pivot = Vector2.zero;
            _zoneLayer.anchoredPosition = Vector2.zero;

            var terrainLayerGo = new GameObject("TerrainLayer", typeof(RectTransform));
            terrainLayerGo.transform.SetParent(_mapArea, false);
            _terrainLayer = (RectTransform)terrainLayerGo.transform;
            _terrainLayer.anchorMin = Vector2.zero;
            _terrainLayer.anchorMax = Vector2.zero;
            _terrainLayer.pivot = Vector2.zero;
            _terrainLayer.anchoredPosition = Vector2.zero;

            var objectiveLayerGo = new GameObject("ObjectiveLayer", typeof(RectTransform));
            objectiveLayerGo.transform.SetParent(_mapArea, false);
            _objectiveLayer = (RectTransform)objectiveLayerGo.transform;
            _objectiveLayer.anchorMin = Vector2.zero;
            _objectiveLayer.anchorMax = Vector2.zero;
            _objectiveLayer.pivot = Vector2.zero;
            _objectiveLayer.anchoredPosition = Vector2.zero;
        }

        private static string PresetLabel(string presetName)
        {
            var parts = presetName.Split('x');
            return $"{parts[0]} x {parts[1]}\"";
        }

        private static TextMeshProUGUI CreateLabel(RectTransform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 12f;
            t.color = Color.white;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
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

        private Button CreateZoneToggleButton(RectTransform parent, string player)
        {
            var btn = CreateButton(parent, $"{player} 배치구역", () => OnZoneButtonClicked(player));
            var text = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                var c = ZoneColors[player];
                c.a = 1f;
                text.color = c;
            }
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

        // ── 지도 크기 프리셋 ────────────────────────────────────────────

        private void OnSizePresetPressed(string presetName)
        {
            if (presetName == _currentPreset)
            {
                return;
            }
            ApplyPreset(presetName);
        }

        private void ApplyPreset(string presetName)
        {
            _currentPreset = presetName;
            foreach (var kv in _sizeButtons)
            {
                SetButtonHighlighted(kv.Value, kv.Key == presetName);
            }

            for (int i = _zoneLayer.childCount - 1; i >= 0; i--)
            {
                Destroy(_zoneLayer.GetChild(i).gameObject);
            }
            _drawingZone = null;

            for (int i = _terrainLayer.childCount - 1; i >= 0; i--)
            {
                Destroy(_terrainLayer.GetChild(i).gameObject);
            }
            _draggingPiece = null;
            _placementModuleId = "";
            foreach (var kv in _terrainButtons)
            {
                SetButtonHighlighted(kv.Value, false);
            }

            for (int i = _objectiveLayer.childCount - 1; i >= 0; i--)
            {
                Destroy(_objectiveLayer.GetChild(i).gameObject);
            }
            _objectivePieces.Clear();
            _draggingObjective = null;
            _activeObjectiveNumber = 0;
            foreach (var btn in _objectiveButtons.Values)
            {
                btn.interactable = true;
                SetButtonHighlighted(btn, false);
            }

            Vector2 mapSize = MapSize;
            _mapBackgroundRect.sizeDelta = mapSize;
            _gridRect.sizeDelta = mapSize;
            _zoneLayer.sizeDelta = mapSize;
            _terrainLayer.sizeDelta = mapSize;
            _objectiveLayer.sizeDelta = mapSize;
            UpdateMapLayout();
        }

        /// <summary>지도가 팔레트/크기 행 아래 남는 뷰포트 안에 들어오도록
        /// 축소해서 배치한다(원본 mm 크기가 뷰포트보다 작으면 1:1로 그대로).
        /// Godot판 MissionSetup.gd의 _layout()에서 스케일 계산 부분만 대응 —
        /// 패닝/줌 인터랙션은 아직 없다. RectTransformUtility의 화면→로컬
        /// 좌표 변환은 이 스케일을 자동으로 고려하므로, 좌표 계산 코드는
        /// 손댈 필요가 없다.</summary>
        private void UpdateMapLayout()
        {
            if (_mapArea == null)
            {
                return;
            }
            float left = MarginPx + 180f + MarginPx; // 팔레트 폭 + 여백
            float top = MarginPx + 32f + MarginPx; // 크기 선택 행 높이 + 여백
            float viewportW = Mathf.Max(Screen.width - left - MarginPx, 10f);
            float viewportH = Mathf.Max(Screen.height - top - MarginPx, 10f);

            Vector2 mapSize = MapSize;
            float scale = Mathf.Min(viewportW / mapSize.x, viewportH / mapSize.y);
            scale = Mathf.Min(scale, 1f);

            _mapArea.localScale = new Vector3(scale, scale, 1f);
            _mapArea.anchoredPosition = new Vector2(left, MarginPx);
        }

        // ── 지형 배치 ────────────────────────────────────────────────────
        // Godot판 TerrainPiece.gd/TerrainCatalog.gd + MissionSetup.gd의 지형
        // 관련 함수들 포팅. 물리/충돌은 없다 — 순수 시각 참고용으로만
        // 배치한다(고지대 경사로 통행 등은 사람이 직접 판정, 사용자와 합의된
        // 방침). 회전은 휠로만 하고, 우클릭은 배치구역/미션 목표와 동일하게
        // 바로 삭제한다(옵션이 하나뿐이면 즉시 실행하는 이 프로젝트 규칙).

        private const float TerrainGridSizeMm = GameConstants.MmPerInch / 2f; // 0.5인치

        private void OnTerrainButtonToggled(string moduleId)
        {
            bool nowActive = _placementModuleId != moduleId;
            _placementModuleId = nowActive ? moduleId : "";
            foreach (var kv in _terrainButtons)
            {
                SetButtonHighlighted(kv.Value, nowActive && kv.Key == moduleId);
            }
            _activeZonePlayer = "";
            foreach (var kv in _zoneButtons)
            {
                SetButtonHighlighted(kv.Value, false);
            }
            ClearObjectiveMode();
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

            PlacePiece(_placementModuleId, SnapToGrid(local));
            ClearTerrainPlacementMode();
        }

        private void PlacePiece(string moduleId, Vector2 localPoint)
        {
            var module = TerrainCatalog.Get(moduleId);
            if (module == null)
            {
                return;
            }

            var go = new GameObject($"Terrain_{module.Id}", typeof(RectTransform));
            go.transform.SetParent(_terrainLayer, false);
            var piece = go.AddComponent<TerrainPiece>();
            piece.RectTransform.anchorMin = Vector2.zero;
            piece.RectTransform.anchorMax = Vector2.zero;
            piece.Setup(module);
            piece.Center = localPoint;
            piece.DragRequested += OnPieceDragRequested;
            piece.DeleteRequested += OnPieceDeleteRequested;
        }

        private void HandleDragInput()
        {
            if (TryGetLocalMouse(out var local))
            {
                _draggingPiece.Center = SnapToGrid(local + _dragPieceOffset);
            }
            if (Input.GetMouseButtonUp(0))
            {
                _draggingPiece = null;
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
            Destroy(piece.gameObject);
        }

        /// <summary>마우스 아래(맨 위에 그려진 것부터)의 지형 조각을 찾는다 —
        /// 회전된 사각형 그대로 판정한다(Godot판 _find_terrain_piece_at_point
        /// 포팅). 휠을 굴렸을 때 회전할지 말지 정하는 데 쓴다.</summary>
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

        // ── 배치구역 그리기 ─────────────────────────────────────────────

        private void OnZoneButtonClicked(string player)
        {
            _activeZonePlayer = _activeZonePlayer == player ? "" : player;
            foreach (var kv in _zoneButtons)
            {
                SetButtonHighlighted(kv.Value, kv.Key == _activeZonePlayer);
            }
            ClearObjectiveMode();
            ClearTerrainPlacementMode();
        }

        private void HandleZoneStartClick(Vector2 local)
        {
            Vector2 mapSize = MapSize;
            if (local.x < 0f || local.x > mapSize.x || local.y < 0f || local.y > mapSize.y)
            {
                return;
            }

            float dLeft = local.x;
            float dRight = mapSize.x - local.x;
            float dTop = local.y;
            float dBottom = mapSize.y - local.y;

            string candidateV = "";
            if (Mathf.Min(dLeft, dRight) <= EdgeSnapThresholdMm)
            {
                candidateV = dLeft <= dRight ? "left" : "right";
            }

            string candidateH = "";
            if (Mathf.Min(dTop, dBottom) <= EdgeSnapThresholdMm)
            {
                candidateH = dTop <= dBottom ? "top" : "bottom";
            }

            if (candidateV == "" && candidateH == "")
            {
                return;
            }

            _zoneDownLocal = local;
            _zoneCandidateV = candidateV;
            _zoneCandidateH = candidateH;
            // 변 하나에서만 시작했다면 그 변으로 고정, 모서리라면 드래그 방향에
            // 따라 매 프레임 다시 판단한다(아래 CurrentDragEdge 참고).
            _zoneLockedEdge = candidateV == "" ? candidateH : (candidateH == "" ? candidateV : "");

            _drawingZone = CreateZonePiece(_activeZonePlayer);
            UpdateZoneDrawing(local);
        }

        private string CurrentDragEdge(Vector2 local)
        {
            if (_zoneLockedEdge != "")
            {
                return _zoneLockedEdge;
            }
            // 모서리에서 시작한 경우: 눌렀던 지점부터 지금까지의 누적 이동
            // 방향으로 매번 다시 판단한다. 놓기 전까지는 언제든 가로↔세로를
            // 바꿀 수 있다.
            Vector2 delta = local - _zoneDownLocal;
            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? _zoneCandidateH : _zoneCandidateV;
        }

        private static float SnapAlongEdge(Vector2 local, string edge, Vector2 mapSize)
        {
            float value = (edge == "left" || edge == "right") ? local.y : local.x;
            float maxValue = (edge == "left" || edge == "right") ? mapSize.y : mapSize.x;
            value = Mathf.Clamp(value, 0f, maxValue);
            return Mathf.Clamp(SnapToInch(value), 0f, maxValue);
        }

        private static float SnapToInch(float value)
        {
            return Mathf.Round(value / GameConstants.MmPerInch) * GameConstants.MmPerInch;
        }

        private void UpdateZoneDrawing(Vector2 local)
        {
            Vector2 mapSize = MapSize;
            string edge = CurrentDragEdge(local);
            float startAlong = SnapAlongEdge(_zoneDownLocal, edge, mapSize);
            float currentAlong = SnapAlongEdge(local, edge, mapSize);

            float a = Mathf.Min(startAlong, currentAlong);
            float b = Mathf.Max(startAlong, currentAlong);
            float length = b - a;
            _zoneCurrentLength = length;

            _drawingZone.Edge = edge;
            _drawingZone.StartAlong = a;
            _drawingZone.EndAlong = b;

            var rt = _drawingZone.RectTransform;
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

        private void FinishZoneDrawing()
        {
            if (_zoneCurrentLength < GameConstants.MmPerInch - 1f)
            {
                Destroy(_drawingZone.gameObject);
            }
            _drawingZone = null;
        }

        private DeploymentZonePiece CreateZonePiece(string player)
        {
            var go = new GameObject($"Zone_{player}", typeof(RectTransform));
            go.transform.SetParent(_zoneLayer, false);
            var piece = go.AddComponent<DeploymentZonePiece>();
            piece.OwnerPlayer = player;
            piece.LineColor = ZoneColors[player];
            return piece;
        }

        // ── 미션 목표 배치 ──────────────────────────────────────────────
        // Godot판 MissionSetup.gd의 objective 관련 함수들 포팅. 배치구역과
        // 마찬가지로 팔레트에서 번호를 고르고(한 번에 하나만 배치 모드) 지도를
        // 클릭하면 놓인다 — 1인치 격자에 스냅, 드래그로 재배치, 우클릭으로
        // 바로 삭제(메뉴 없음 — 선택지가 삭제 하나뿐이라서, 배치구역과 동일한
        // 원칙). 번호당 하나만 놓을 수 있어 놓으면 그 팔레트 버튼이
        // 비활성화되고, 지우면 다시 활성화된다.

        private void OnObjectiveButtonClicked(int number)
        {
            _activeObjectiveNumber = _activeObjectiveNumber == number ? 0 : number;
            _activeZonePlayer = "";
            foreach (var kv in _zoneButtons)
            {
                SetButtonHighlighted(kv.Value, false);
            }
            foreach (var kv in _objectiveButtons)
            {
                SetButtonHighlighted(kv.Value, kv.Key == _activeObjectiveNumber);
            }
            ClearTerrainPlacementMode();
        }

        private void ClearObjectiveMode()
        {
            int number = _activeObjectiveNumber;
            _activeObjectiveNumber = 0;
            if (_objectiveButtons.TryGetValue(number, out var btn))
            {
                SetButtonHighlighted(btn, false);
            }
        }

        private void HandleObjectivePlacementClick()
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

            PlaceObjective(_activeObjectiveNumber, SnapToInchGrid(local));
            ClearObjectiveMode();
        }

        private void PlaceObjective(int number, Vector2 localPoint)
        {
            float diameter = GameConstants.MissionObjectiveTokenDiameterMm
                    + 2f * GameConstants.MissionObjectiveCaptureMarginInch * GameConstants.MmPerInch;

            var go = new GameObject($"Objective_{number}", typeof(RectTransform));
            go.transform.SetParent(_objectiveLayer, false);
            var piece = go.AddComponent<MissionObjectivePiece>();
            piece.Number = number;
            var baseColor = GameConstants.MissionObjectiveTokenColors.TryGetValue(number, out var c)
                    ? c
                    : new Color(0.85f, 0.85f, 0.8f);
            piece.TokenColor = GameConstants.Muted(baseColor, GameConstants.MissionObjectiveSaturationFactor, GameConstants.MissionObjectiveValueFactor);
            piece.RectTransform.anchorMin = Vector2.zero;
            piece.RectTransform.anchorMax = Vector2.zero;
            piece.RectTransform.sizeDelta = new Vector2(diameter, diameter);
            piece.Center = localPoint;
            ClampObjectiveToBounds(piece);
            piece.DragRequested += OnObjectiveDragRequested;
            piece.DeleteRequested += OnObjectiveDeleteRequested;

            _objectivePieces[number] = piece;
            if (_objectiveButtons.TryGetValue(number, out var btn))
            {
                btn.interactable = false;
            }
        }

        private void ClampObjectiveToBounds(MissionObjectivePiece piece)
        {
            Vector2 mapSize = MapSize;
            float margin = GameConstants.MissionObjectiveTokenDiameterMm / 2f;
            Vector2 c = piece.Center;
            c.x = Mathf.Clamp(c.x, margin, Mathf.Max(margin, mapSize.x - margin));
            c.y = Mathf.Clamp(c.y, margin, Mathf.Max(margin, mapSize.y - margin));
            piece.Center = c;
        }

        private void HandleObjectiveDragInput()
        {
            if (TryGetLocalMouse(out var local))
            {
                _draggingObjective.Center = SnapToInchGrid(local + _dragObjectiveOffset);
                ClampObjectiveToBounds(_draggingObjective);
            }
            if (Input.GetMouseButtonUp(0))
            {
                _draggingObjective = null;
            }
        }

        private void OnObjectiveDragRequested(MissionObjectivePiece piece)
        {
            _draggingObjective = piece;
            TryGetLocalMouse(out var local);
            _dragObjectiveOffset = piece.Center - local;
            piece.transform.SetAsLastSibling();
        }

        private void OnObjectiveDeleteRequested(MissionObjectivePiece piece)
        {
            int number = piece.Number;
            Destroy(piece.gameObject);
            if (_objectivePieces.TryGetValue(number, out var existing) && existing == piece)
            {
                _objectivePieces.Remove(number);
            }
            if (_objectiveButtons.TryGetValue(number, out var btn))
            {
                btn.interactable = true;
            }
        }

        private static Vector2 SnapToInchGrid(Vector2 point)
        {
            float step = GameConstants.MmPerInch;
            return new Vector2(Mathf.Round(point.x / step) * step, Mathf.Round(point.y / step) * step);
        }

        // ── 게임 시작 ────────────────────────────────────────────────

        private void OnStartGamePressed()
        {
            MissionData.Clear();
            MissionData.HasData = true;
            MissionData.MapPreset = _currentPreset;

            for (int i = 0; i < _zoneLayer.childCount; i++)
            {
                var piece = _zoneLayer.GetChild(i).GetComponent<DeploymentZonePiece>();
                if (piece == null)
                {
                    continue;
                }
                MissionData.DeploymentZones.Add(new DeploymentZoneData
                {
                    Edge = piece.Edge,
                    Player = piece.OwnerPlayer,
                    StartAlong = piece.StartAlong,
                    EndAlong = piece.EndAlong,
                });
            }

            foreach (var kv in _objectivePieces)
            {
                MissionData.MissionObjectives.Add(new MissionObjectiveData
                {
                    Number = kv.Key,
                    Position = kv.Value.Center,
                });
            }

            for (int i = 0; i < _terrainLayer.childCount; i++)
            {
                var piece = _terrainLayer.GetChild(i).GetComponent<TerrainPiece>();
                if (piece == null)
                {
                    continue;
                }
                MissionData.TerrainPieces.Add(new TerrainPieceData
                {
                    ModuleId = piece.ModuleId,
                    Position = piece.Center,
                    RotationDeg = piece.RotationDegrees,
                });
            }

            StartGameRequested?.Invoke();
        }
    }
}
