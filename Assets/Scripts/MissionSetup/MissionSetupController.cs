using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>프리셋 미리보기 전용 — 게임 보드의 미션 목표 마커는 이제
        /// 플레이어가 바꾼 팀 색(GameConstants.GetMissionObjectiveBaseColor)을
        /// 따라가지만, 이 미리보기는 아직 어느 팀 색도 모르는 미션 설정
        /// 화면에서 쓰이므로 항상 고정된 1/3=빨강, 2/4=파랑, 5=초록으로
        /// 보여준다(사용자 요청) — ZoneColors와 같은 이유로 이 화면만의
        /// 고정 팔레트다.</summary>
        private static readonly Dictionary<int, Color> PreviewObjectiveColors = new Dictionary<int, Color>
        {
            { 1, new Color(0.85f, 0.15f, 0.15f) },
            { 2, new Color(0.15f, 0.4f, 0.85f) },
            { 3, new Color(0.85f, 0.15f, 0.15f) },
            { 4, new Color(0.15f, 0.4f, 0.85f) },
            { 5, new Color(0.2f, 0.7f, 0.25f) },
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

        // ── 미션 프리셋 저장/불러오기 ──────────────────────────────────
        private InputDialog _presetNameDialog;
        private RosterFileDialog _presetLoadDialog;

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
            BuildPresetDialogs();
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
            rowRect.sizeDelta = new Vector2(900f, 32f);

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
            CreateButton(rowRect, "프리셋 저장", OnSavePresetPressed);
            CreateButton(rowRect, "프리셋 불러오기", OnLoadPresetPressed);
        }

        /// <summary>미션 프리셋 저장/불러오기용 다이얼로그 두 개 — 이름 입력은
        /// 기존 InputDialog를, 파일 선택은 로스터 임포트가 쓰던
        /// RosterFileDialog를(제목만 바꿔) 그대로 재사용한다. 둘 다 이
        /// 화면(MissionSetupController) 전용 인스턴스라 로스터 쪽 파일
        /// 다이얼로그(게임 화면에만 있음)와는 완전히 별개다.</summary>
        private void BuildPresetDialogs()
        {
            _presetNameDialog = new GameObject("PresetNameDialog").AddComponent<InputDialog>();
            _presetNameDialog.transform.SetParent(Root, false);
            _presetNameDialog.Confirmed += OnPresetNameConfirmed;

            _presetLoadDialog = new GameObject("PresetLoadDialog").AddComponent<RosterFileDialog>();
            _presetLoadDialog.transform.SetParent(Root, false);
            _presetLoadDialog.FileSelected += OnPresetFileSelected;
            _presetLoadDialog.Cancelled += () => _presetPreviewGo.SetActive(false);
            _presetLoadDialog.FileHighlighted += OnPresetFileHighlighted;
            _presetLoadDialog.FileHighlightCleared += ClearPresetPreviewContent;

            BuildPresetPreviewPanel();
        }

        // ── 미션 프리셋 미리보기 ─────────────────────────────────────────
        // "프리셋 불러오기" 목록에서 파일 위에 마우스를 올리면 그 배치구역/
        // 지형/미션 목표를 작은 지도로 그려 보여준다(사용자 요청). 실제로
        // 화면에 배치하기 전에 미리 볼 수 있게 — RosterFileDialog 패널
        // (560 폭, 화면 중앙 앵커) 오른쪽에 별도 패널로 붙인다.

        private const float PresetPreviewBoxSize = 220f;
        private const float PresetPreviewPanelWidth = 260f;

        private GameObject _presetPreviewGo;
        private RectTransform _presetPreviewMapArea;
        private TextMeshProUGUI _presetPreviewHintLabel;
        private readonly List<GameObject> _presetPreviewSpawned = new List<GameObject>();

        private void BuildPresetPreviewPanel()
        {
            _presetPreviewGo = new GameObject("PresetPreview", typeof(RectTransform));
            _presetPreviewGo.transform.SetParent(Root, false);
            var rect = (RectTransform)_presetPreviewGo.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            // RosterFileDialog의 패널은 화면 중앙 앵커에 폭 560 고정이라, 그
            // 오른쪽 절반 끝(중앙+280)에서 여백 12를 두고 이어붙인다.
            rect.anchoredPosition = new Vector2(280f + 12f, 0f);
            rect.sizeDelta = new Vector2(PresetPreviewPanelWidth, 480f);

            var bg = _presetPreviewGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            // 미리보기 패널이 RosterFileDialog의 전체화면 반투명 배경 위에
            // 얹혀 있으므로(같은 화면 영역), raycastTarget을 켜서 이 패널
            // 위에서의 클릭이 그 아래 배경까지 뚫고 내려가 "바깥 클릭 =
            // 취소"로 잘못 처리되지 않게 막는다.
            bg.raycastTarget = true;

            var layout = _presetPreviewGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            float contentWidth = PresetPreviewPanelWidth - 24f;

            var title = CreateLabel((RectTransform)_presetPreviewGo.transform, "미리보기");
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredWidth = contentWidth;
            titleLe.preferredHeight = 20f;

            var mapAreaGo = new GameObject("PreviewMap", typeof(RectTransform));
            mapAreaGo.transform.SetParent(_presetPreviewGo.transform, false);
            var mapAreaLe = mapAreaGo.AddComponent<LayoutElement>();
            mapAreaLe.preferredWidth = PresetPreviewBoxSize;
            mapAreaLe.preferredHeight = PresetPreviewBoxSize;
            _presetPreviewMapArea = (RectTransform)mapAreaGo.transform;
            _presetPreviewMapArea.sizeDelta = new Vector2(PresetPreviewBoxSize, PresetPreviewBoxSize);
            var mapAreaBg = mapAreaGo.AddComponent<Image>();
            mapAreaBg.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            mapAreaBg.raycastTarget = false;

            _presetPreviewHintLabel = CreateLabel((RectTransform)_presetPreviewGo.transform, "파일에 마우스를 올리면\n미리보기가 나타납니다");
            _presetPreviewHintLabel.fontSize = 11f;
            _presetPreviewHintLabel.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            var hintLe = _presetPreviewHintLabel.gameObject.AddComponent<LayoutElement>();
            hintLe.preferredWidth = contentWidth;
            hintLe.preferredHeight = 40f;

            _presetPreviewGo.SetActive(false);
        }

        private void OnPresetFileHighlighted(string path)
        {
            string jsonText;
            try
            {
                jsonText = File.ReadAllText(path);
            }
            catch (Exception)
            {
                ClearPresetPreviewContent();
                return;
            }

            if (!MissionPresetIO.TryLoad(jsonText, out var mapPreset, out var zones, out var objectives, out var terrain, out _))
            {
                ClearPresetPreviewContent();
                return;
            }

            RenderPresetPreview(mapPreset, zones, objectives, terrain);
        }

        private void ClearPresetPreviewContent()
        {
            foreach (var go in _presetPreviewSpawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _presetPreviewSpawned.Clear();
            _presetPreviewHintLabel.gameObject.SetActive(true);
        }

        private void RenderPresetPreview(string mapPreset, List<DeploymentZoneData> zones,
                List<MissionObjectiveData> objectives, List<TerrainPieceData> terrain)
        {
            ClearPresetPreviewContent();
            _presetPreviewHintLabel.gameObject.SetActive(false);

            Vector2 mapSize = GameConstants.MapSizePresets.TryGetValue(mapPreset, out var sz)
                    ? sz
                    : GameConstants.MapSizePresets[GameConstants.DefaultMapSizePreset];
            float scale = Mathf.Min(PresetPreviewBoxSize / mapSize.x, PresetPreviewBoxSize / mapSize.y);
            Vector2 drawSize = mapSize * scale;

            // 코너 원점([0,mapSize]) mm 좌표를 미리보기 박스의 중심 원점
            // 로컬 좌표로 바꾼다(0~1로 정규화한 뒤 drawSize 기준으로 중앙 정렬).
            Vector2 ToPreviewLocal(Vector2 cornerPointMm)
            {
                Vector2 norm = new Vector2(cornerPointMm.x / mapSize.x, cornerPointMm.y / mapSize.y);
                return new Vector2((norm.x - 0.5f) * drawSize.x, (norm.y - 0.5f) * drawSize.y);
            }

            var mapBgGo = new GameObject("MapBg", typeof(RectTransform));
            mapBgGo.transform.SetParent(_presetPreviewMapArea, false);
            var mapBgRect = (RectTransform)mapBgGo.transform;
            mapBgRect.anchorMin = new Vector2(0.5f, 0.5f);
            mapBgRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapBgRect.pivot = new Vector2(0.5f, 0.5f);
            mapBgRect.sizeDelta = drawSize;
            mapBgRect.anchoredPosition = Vector2.zero;
            var mapBgImg = mapBgGo.AddComponent<Image>();
            mapBgImg.color = new Color(0.15f, 0.18f, 0.15f, 1f);
            mapBgImg.raycastTarget = false;
            _presetPreviewSpawned.Add(mapBgGo);

            foreach (var z in zones)
            {
                Vector2 a, b;
                switch (z.Edge)
                {
                    case "left": a = new Vector2(0f, z.StartAlong); b = new Vector2(0f, z.EndAlong); break;
                    case "right": a = new Vector2(mapSize.x, z.StartAlong); b = new Vector2(mapSize.x, z.EndAlong); break;
                    case "top": a = new Vector2(z.StartAlong, 0f); b = new Vector2(z.EndAlong, 0f); break;
                    default: a = new Vector2(z.StartAlong, mapSize.y); b = new Vector2(z.EndAlong, mapSize.y); break;
                }
                Vector2 pa = ToPreviewLocal(a);
                Vector2 pb = ToPreviewLocal(b);
                bool horizontal = z.Edge == "top" || z.Edge == "bottom";

                var go = new GameObject("Zone", typeof(RectTransform));
                go.transform.SetParent(_presetPreviewMapArea, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = horizontal ? new Vector2(Vector2.Distance(pa, pb), 3f) : new Vector2(3f, Vector2.Distance(pa, pb));
                rt.anchoredPosition = (pa + pb) / 2f;
                var img = go.AddComponent<Image>();
                img.color = ZoneColors.TryGetValue(z.Player, out var c) ? c : Color.white;
                img.raycastTarget = false;
                _presetPreviewSpawned.Add(go);
            }

            foreach (var t in terrain)
            {
                var go = new GameObject("Terrain", typeof(RectTransform));
                go.transform.SetParent(_presetPreviewMapArea, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(10f, 10f);
                rt.anchoredPosition = ToPreviewLocal(t.Position);
                rt.localEulerAngles = new Vector3(0f, 0f, -t.RotationDeg);
                var img = go.AddComponent<Image>();
                img.color = new Color(0.55f, 0.45f, 0.3f, 0.9f);
                img.raycastTarget = false;
                _presetPreviewSpawned.Add(go);
            }

            foreach (var o in objectives)
            {
                var go = new GameObject($"Objective_{o.Number}", typeof(RectTransform));
                go.transform.SetParent(_presetPreviewMapArea, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(12f, 12f);
                rt.anchoredPosition = ToPreviewLocal(o.Position);
                var img = go.AddComponent<Image>();
                img.color = PreviewObjectiveColors.TryGetValue(o.Number, out var objColor) ? objColor : new Color(0.85f, 0.85f, 0.8f);
                img.raycastTarget = false;

                var labelGo = new GameObject("Num", typeof(RectTransform));
                labelGo.transform.SetParent(go.transform, false);
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                var label = labelGo.AddComponent<TextMeshProUGUI>();
                label.text = o.Number.ToString();
                label.fontSize = 8f;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.raycastTarget = false;
                _presetPreviewSpawned.Add(go);
            }
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

        private TerrainPiece PlacePiece(string moduleId, Vector2 localPoint)
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
            piece.DragRequested += OnPieceDragRequested;
            piece.DeleteRequested += OnPieceDeleteRequested;
            return piece;
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
            ApplyZoneGeometry(_drawingZone, edge, a, length, mapSize);
        }

        /// <summary>edge/along 구간(mm)을 실제 RectTransform 위치/크기로
        /// 변환한다 — 그리는 중(UpdateZoneDrawing)과 프리셋 불러오기(한 번에
        /// 다시 세팅) 둘 다 이 계산이 필요해서 공유한다.</summary>
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
            var baseColor = GameConstants.GetMissionObjectiveBaseColor(number);
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

        /// <summary>지금 화면에 놓여있는 배치구역/미션 목표/지형을 그대로
        /// 읽어낸다 — "게임 시작"(MissionData로)과 "프리셋 저장"(파일로) 둘
        /// 다 결국 같은 스냅샷이 필요해서 공유한다.</summary>
        private void CollectCurrentState(out List<DeploymentZoneData> zones,
                out List<MissionObjectiveData> objectives, out List<TerrainPieceData> terrain)
        {
            zones = new List<DeploymentZoneData>();
            for (int i = 0; i < _zoneLayer.childCount; i++)
            {
                var piece = _zoneLayer.GetChild(i).GetComponent<DeploymentZonePiece>();
                if (piece == null)
                {
                    continue;
                }
                zones.Add(new DeploymentZoneData
                {
                    Edge = piece.Edge,
                    Player = piece.OwnerPlayer,
                    StartAlong = piece.StartAlong,
                    EndAlong = piece.EndAlong,
                });
            }

            objectives = new List<MissionObjectiveData>();
            foreach (var kv in _objectivePieces)
            {
                objectives.Add(new MissionObjectiveData
                {
                    Number = kv.Key,
                    Position = kv.Value.Center,
                });
            }

            terrain = new List<TerrainPieceData>();
            for (int i = 0; i < _terrainLayer.childCount; i++)
            {
                var piece = _terrainLayer.GetChild(i).GetComponent<TerrainPiece>();
                if (piece == null)
                {
                    continue;
                }
                terrain.Add(new TerrainPieceData
                {
                    ModuleId = piece.ModuleId,
                    Position = piece.Center,
                    RotationDeg = piece.RotationDegrees,
                });
            }
        }

        private void OnStartGamePressed()
        {
            MissionData.Clear();
            MissionData.HasData = true;
            MissionData.MapPreset = _currentPreset;
            CollectCurrentState(out var zones, out var objectives, out var terrain);
            MissionData.DeploymentZones.AddRange(zones);
            MissionData.MissionObjectives.AddRange(objectives);
            MissionData.TerrainPieces.AddRange(terrain);
            StartGameRequested?.Invoke();
        }

        // ── 미션 프리셋 저장/불러오기 ──────────────────────────────────
        // 배치구역/지형/미션 목표 배치를 JSON 파일로 저장했다가 나중에 다시
        // 불러온다(사용자 요청). 저장 위치는 로스터 JSON과 같은 Document/
        // 폴더 — 불러오기 브라우저(RosterFileDialog 재사용)의 시작 폴더와
        // 맞춰야 하므로.

        private void OnSavePresetPressed()
        {
            _presetNameDialog.Open("프리셋 이름", "");
        }

        private void OnPresetNameConfirmed(string name)
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string path;
            try
            {
                path = Path.Combine(ResolvePresetDirectory(), $"{SanitizeFileName(name)}.json");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"프리셋 저장 경로를 만들 수 없습니다: {e.Message}");
                return;
            }

            CollectCurrentState(out var zones, out var objectives, out var terrain);
            try
            {
                MissionPresetIO.Save(path, _currentPreset, zones, objectives, terrain);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"프리셋을 저장하지 못했습니다: {path} ({e.Message})");
            }
        }

        private void OnLoadPresetPressed()
        {
            _presetLoadDialog.SetStartDirectory(ResolvePresetDirectory());
            _presetLoadDialog.Open("미션 프리셋 선택");
            ClearPresetPreviewContent();
            _presetPreviewGo.SetActive(true);
            _presetPreviewGo.transform.SetAsLastSibling(); // 다이얼로그 자체의 반투명 배경보다 위에 그려져야 한다.
        }

        private void OnPresetFileSelected(string path)
        {
            _presetPreviewGo.SetActive(false);

            string jsonText;
            try
            {
                jsonText = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"프리셋 파일을 열 수 없습니다: {path} ({e.Message})");
                return;
            }

            if (!MissionPresetIO.TryLoad(jsonText, out var mapPreset, out var zones, out var objectives, out var terrain, out var error))
            {
                Debug.LogWarning($"프리셋 파일 형식이 올바르지 않습니다: {path} — {error}");
                return;
            }

            // ApplyPreset이 기존 배치구역/지형/목표를 전부 지우고 지도 크기를
            // 다시 잡아준다 — "게임 시작"과 달리 여기선 그 뒤에 곧바로
            // 불러온 내용을 다시 채워 넣는다.
            ApplyPreset(mapPreset);

            Vector2 mapSize = MapSize;
            foreach (var z in zones)
            {
                var piece = CreateZonePiece(z.Player);
                piece.Edge = z.Edge;
                piece.StartAlong = z.StartAlong;
                piece.EndAlong = z.EndAlong;
                ApplyZoneGeometry(piece, z.Edge, z.StartAlong, z.EndAlong - z.StartAlong, mapSize);
            }

            foreach (var o in objectives)
            {
                PlaceObjective(o.Number, o.Position);
            }

            foreach (var t in terrain)
            {
                var piece = PlacePiece(t.ModuleId, t.Position);
                if (piece != null)
                {
                    piece.RotationDegrees = t.RotationDeg;
                }
            }
        }

        /// <summary>미션 프리셋은 로스터 JSON(Document/)과 다른, 전용
        /// Deployments/ 폴더에 저장한다(사용자가 직접 만들어둔 폴더) —
        /// 스크린샷 기능과 같은 AppPaths.ExeDirectory() 계산을 쓰므로 에디터
        /// 에서도 실제 빌드에서도 항상 실행 위치 기준으로 맞게 찾는다. 폴더가
        /// 없으면(첫 저장이거나 다른 컴퓨터) 만들어준다.</summary>
        private static string ResolvePresetDirectory()
        {
            string dir = Path.Combine(AppPaths.ExeDirectory(), "Deployments");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
