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
    /// 배치 프리셋 제작 화면(2026-08-30 재구성 — 예전 이름 "맵 셋업", 그 전엔
    /// "미션 셋업") — 지도 크기 선택 + 배치구역(팀별 지도 가장자리 구간)
    /// 그리기 + 미션 목표(1~5번) 배치. Godot판 scenes/mission_setup/
    /// MissionSetup.gd 포팅에서 출발했지만, 이제 게임 시작 흐름에 끼는 라이브
    /// 셋업 화면이 아니라 나중에 Selection 화면에서 골라 쓸 "배치 프리셋"을
    /// 만들어두는 편집기 전용 화면이다 — Entry 한쪽 구석 버튼으로만 들어온다.
    /// 지형 배치는 여기 없다(별도 TerrainSetupController로 완전히 분리 —
    /// 지형은 매 게임 새로 배치하고 프리셋으로 저장하지 않는다는 사용자
    /// 지정에 따라, 애초에 "제작"할 대상이 아니게 됐다). "완료 ▶" 버튼은
    /// 이제 다음 셋업 단계로 넘어가는 게 아니라 그냥 Entry로 돌아가기다 —
    /// 프리셋으로 남기려면 명시적으로 "프리셋 저장" 버튼을 눌러야 한다
    /// (사용자 지정).
    ///
    /// 이 컴포넌트가 붙은 GameObject 자체가 화면 전체를 덮는 RectTransform
    /// (Canvas의 직속 자식, anchors (0,0)-(1,1))이라고 가정한다 — BoardManager
    /// 처럼 별도 baseLayer 참조를 안 받고 자기 자신의 transform을 기준으로
    /// UI를 짓는다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MapAuthoringController : MonoBehaviour
    {
        public event Action BackRequested;

        private const float EdgeSnapThresholdMm = 15f;
        private const float ZoneHitThicknessMm = 24f;
        private const float MarginPx = 8f;

        // 게임 보드(BoardManager.PanZoom.cs)와 같은 줌 범위/배율.
        private const float ZoomStep = 1.1f;
        private const float MinZoom = 0.3f;
        private const float MaxZoom = 4f;

        private static readonly Dictionary<string, Color> ZoneColors = new Dictionary<string, Color>
        {
            { "A", new Color(1f, 0.15f, 0.15f, 0.9f) },
            { "B", new Color(0.15f, 0.35f, 1f, 0.9f) },
        };

        /// <summary>프리셋 미리보기 전용 — 게임 보드의 미션 목표 마커는 이제
        /// 플레이어가 바꾼 팀 색(GameConstants.GetMissionObjectiveBaseColor)을
        /// 따라가지만, 이 미리보기는 아직 어느 팀 색도 모르는 맵 셋업
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
        private RectTransform _objectiveLayer;

        private readonly Dictionary<string, Button> _sizeButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _zoneButtons = new Dictionary<string, Button>();
        private readonly Dictionary<int, Button> _objectiveButtons = new Dictionary<int, Button>();
        private readonly Dictionary<int, MissionObjectivePiece> _objectivePieces = new Dictionary<int, MissionObjectivePiece>();

        private string _currentPreset = GameConstants.DefaultMapSizePreset;
        private string _activeZonePlayer = "";
        private int _activeObjectiveNumber;

        private MissionObjectivePiece _draggingObjective;
        private Vector2 _dragObjectiveOffset;

        private int _lastScreenWidth;
        private int _lastScreenHeight;

        // ── 지도 이동/확대축소(패닝/줌) ─────────────────────────────────
        // 게임 보드(BoardManager.PanZoom.cs)의 가운데 버튼 드래그 패닝 +
        // 마우스 휠 줌과 같은 방식.
        private bool _panning;
        private Vector2 _lastPanScreenPos;
        private float _zoomLevel = 1f;
        private float _baseScaleFactor = 1f;

        private DeploymentZonePiece _drawingZone;
        private float _zoneCurrentLength;
        private Vector2 _zoneDownLocal;
        private string _zoneLockedEdge = "";
        private string _zoneCandidateH = "";
        private string _zoneCandidateV = "";

        // ── 미션 프리셋 저장/불러오기 ──────────────────────────────────
        private InputDialog _presetNameDialog;
        private RectTransform _presetListContent;

        private Vector2 MapSize => GameConstants.MapSizePresets[_currentPreset];

        private void Start()
        {
            // 지도(_map_area)를 좌측/상단 메뉴들보다 먼저 추가해야 한다 —
            // 나중에 추가된 형제 노드가 위에 그려지므로, 순서가 반대면 지도가
            // 배경으로 버튼들을 덮어버린다(Godot판 MissionSetup.gd의 동일한
            // 주석 참고).
            BuildMapArea();
            BuildTopRow();
            BuildSizeRow();
            BuildPalette();
            BuildPresetDialogs();
            BuildPresetListPanel(MarginPx);
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

            HandlePanInput();

            // 휠은 커서 위치 기준 줌 — 지형이 없어졌으니 게임 보드(BoardManager.
            // PanZoom.cs)처럼 다른 UI 위가 아니면 항상 줌만 한다.
            float scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f) && !IsPointerOverUi())
            {
                ZoomAt(Input.mousePosition, scroll > 0f ? ZoomStep : 1f / ZoomStep);
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

        /// <summary>가운데 버튼 드래그로 지도를 움직인다(사용자 요청) —
        /// BoardManager.PanZoom.cs의 HandlePanAndZoom과 같은 방식. _mapArea의
        /// 부모(Root, 화면 전체)를 기준으로 스크린 좌표 차이를 로컬 좌표
        /// 차이로 바꿔 anchoredPosition에 누적한다 — 코너 원점(pivot=0,0)이든
        /// 중심 원점이든 상관없이 항상 성립하는 계산이라 _mapArea의 앵커
        /// 방식(BuildMapArea 참고)을 안 가린다.</summary>
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

        /// <summary>마우스가 가리키는 지도 위 지점이 화면상 같은 자리에 그대로
        /// 있도록 확대/축소하면서 위치를 함께 보정한다 — 실제 계산은
        /// 게임 보드(BoardManager.PanZoom.cs)도 함께 쓰는 공용 유틸리티
        /// (Board/MapZoomUtil.cs)로 뽑아냈다. 예전엔 여기 따로(부모 기준
        /// 로컬 좌표로) 구현했었는데, 그 방식은 부모(캔버스)의 pivot과
        /// _mapArea의 anchor가 우연히 일치해야만 맞는 결과가 나오는 취약한
        /// 수식이라 이 화면(코너 anchor)에서 줌 중심이 화면 좌하단으로
        /// 쏠리는 버그가 났다 — 공용 유틸리티는 그 문제 자체가 없다.</summary>
        private void ZoomAt(Vector2 screenPos, float factor)
        {
            if (_mapArea == null)
            {
                return;
            }
            _zoomLevel = MapZoomUtil.ApplyZoomAtScreenPoint(_mapArea, screenPos, _zoomLevel, _baseScaleFactor, factor, MinZoom, MaxZoom);
        }

        // ── UI 빌드 ──────────────────────────────────────────────────

        /// <summary>맨 위 줄 — "엔트리로"(BackButton.png)와 "프리셋 저장"
        /// 아이콘 버튼만 남긴다(사용자 지정 — 지도 크기 버튼은 아래 줄로).</summary>
        private void BuildTopRow()
        {
            var rowGo = new GameObject("TopRow", typeof(RectTransform));
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

            CreateIconButton(rowRect, Resources.Load<Texture2D>("UI/BackButton"), 32f, () => BackRequested?.Invoke());
            CreateIconButton(rowRect, Resources.Load<Texture2D>("UI/SaveButton"), 32f, OnSavePresetPressed);
        }

        /// <summary>맨 위 줄 바로 아래 — 지도 크기 선택 버튼들만.</summary>
        private void BuildSizeRow()
        {
            var rowGo = new GameObject("SizeRow", typeof(RectTransform));
            rowGo.transform.SetParent(Root, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(MarginPx, -(MarginPx + 32f + MarginPx));
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
        }

        /// <summary>미션 프리셋 저장용 다이얼로그 — 이름 입력은 기존
        /// InputDialog를 그대로 재사용한다(이 화면 전용 인스턴스).</summary>
        private void BuildPresetDialogs()
        {
            _presetNameDialog = new GameObject("PresetNameDialog").AddComponent<InputDialog>();
            _presetNameDialog.transform.SetParent(Root, false);
            _presetNameDialog.Confirmed += OnPresetNameConfirmed;
        }

        // ── 미션 프리셋 목록(우측 패널) ───────────────────────────────────
        // 예전엔 "프리셋 불러오기" 버튼 → 파일 탐색 다이얼로그 → 마우스
        // 올리면 미리보기 순서였는데, 사용자 요청으로 화면 우측에 모든
        // 프리셋의 미리보기를 항상 목록으로 띄워두고 클릭하면 바로
        // 불러오는 방식으로 바꿨다(다이얼로그/미리보기 패널 자체가 없어짐).

        private const float PresetThumbnailSize = 96f;
        private const float PresetListPanelWidth = 220f;

        /// <summary>화면 우측, 화면 위쪽 여백부터 화면 하단까지 채우는 스크롤
        /// 목록 — Deployments/ 폴더의 모든 *.json을 미리보기 썸네일+파일명으로
        /// 나열하고, 클릭하면 그 자리에서 바로 불러온다(LoadPresetFromFile).</summary>
        private void BuildPresetListPanel(float topOffset)
        {
            var panelGo = new GameObject("PresetList", typeof(RectTransform));
            panelGo.transform.SetParent(Root, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            float bottomInset = MarginPx;
            panelRect.anchoredPosition = new Vector2(-MarginPx, (bottomInset - topOffset) / 2f);
            panelRect.sizeDelta = new Vector2(PresetListPanelWidth, -(topOffset + bottomInset));

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var title = CreateLabel(panelRect, "프리셋 (클릭하면 바로 불러오기)");
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 20f;

            _presetListContent = ScrollListUtil.Create(panelRect, 100f, new Color(0f, 0f, 0f, 0.15f), out _, out var scrollLe);
            scrollLe.flexibleHeight = 1f; // 남는 세로 공간을 전부 목록이 차지한다.

            RefreshPresetList();
        }

        /// <summary>Deployments/ 폴더를 다시 스캔해서 목록을 처음부터 다시
        /// 그린다 — 프리셋 저장 직후에도 새로 저장된 파일이 바로 보이도록
        /// 다시 부른다(OnPresetNameConfirmed).</summary>
        private void RefreshPresetList()
        {
            for (int i = _presetListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_presetListContent.GetChild(i).gameObject);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(ResolvePresetDirectory(), "*.json");
            }
            catch (Exception)
            {
                return;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var path in files)
            {
                string jsonText;
                try
                {
                    jsonText = File.ReadAllText(path);
                }
                catch (Exception)
                {
                    continue;
                }
                if (!MapPresetIO.TryLoad(jsonText, out var mapPreset, out var zones, out var objectives, out var terrain, out _))
                {
                    continue;
                }
                CreatePresetListItem(path, mapPreset, zones, objectives, terrain);
            }
        }

        private void CreatePresetListItem(string path, string mapPreset, List<DeploymentZoneData> zones,
                List<MissionObjectiveData> objectives, List<TerrainPieceData> terrain)
        {
            var itemGo = new GameObject("PresetItem", typeof(RectTransform));
            itemGo.transform.SetParent(_presetListContent, false);
            var itemLayout = itemGo.AddComponent<VerticalLayoutGroup>();
            itemLayout.padding = new RectOffset(6, 6, 6, 6);
            itemLayout.spacing = 4f;
            itemLayout.childAlignment = TextAnchor.UpperCenter;
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandWidth = true;
            itemLayout.childControlHeight = true;
            itemLayout.childForceExpandHeight = false;

            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemBg;
            itemBtn.onClick.AddListener(() => LoadPresetFromFile(path));

            var thumbGo = new GameObject("Thumb", typeof(RectTransform));
            thumbGo.transform.SetParent(itemGo.transform, false);
            var thumbLe = thumbGo.AddComponent<LayoutElement>();
            thumbLe.preferredWidth = PresetThumbnailSize;
            thumbLe.preferredHeight = PresetThumbnailSize;
            var thumbRect = (RectTransform)thumbGo.transform;
            var thumbBg = thumbGo.AddComponent<Image>();
            thumbBg.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            thumbBg.raycastTarget = false;
            DrawPresetThumbnail(thumbRect, PresetThumbnailSize, mapPreset, zones, objectives, terrain);

            var nameLabel = CreateLabel((RectTransform)itemGo.transform, Path.GetFileNameWithoutExtension(path));
            nameLabel.fontSize = 11f;
            var nameLe = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameLe.preferredHeight = 28f;
        }

        /// <summary>배치구역/지형/미션 목표를 boxSize 크기의 작은 지도로 그려서
        /// previewMapArea 아래에 채운다 — 프리셋 목록 항목 하나하나가 이걸
        /// 부른다(항목 자체가 통째로 파괴될 때 자식도 같이 없어지므로 별도
        /// 정리 로직은 필요 없다).</summary>
        private void DrawPresetThumbnail(RectTransform previewMapArea, float boxSize, string mapPreset,
                List<DeploymentZoneData> zones, List<MissionObjectiveData> objectives, List<TerrainPieceData> terrain)
        {
            Vector2 mapSize = GameConstants.MapSizePresets.TryGetValue(mapPreset, out var sz)
                    ? sz
                    : GameConstants.MapSizePresets[GameConstants.DefaultMapSizePreset];
            float scale = Mathf.Min(boxSize / mapSize.x, boxSize / mapSize.y);
            Vector2 drawSize = mapSize * scale;

            // 코너 원점([0,mapSize]) mm 좌표를 미리보기 박스의 중심 원점
            // 로컬 좌표로 바꾼다(0~1로 정규화한 뒤 drawSize 기준으로 중앙 정렬).
            Vector2 ToPreviewLocal(Vector2 cornerPointMm)
            {
                Vector2 norm = new Vector2(cornerPointMm.x / mapSize.x, cornerPointMm.y / mapSize.y);
                return new Vector2((norm.x - 0.5f) * drawSize.x, (norm.y - 0.5f) * drawSize.y);
            }

            var mapBgGo = new GameObject("MapBg", typeof(RectTransform));
            mapBgGo.transform.SetParent(previewMapArea, false);
            var mapBgRect = (RectTransform)mapBgGo.transform;
            mapBgRect.anchorMin = new Vector2(0.5f, 0.5f);
            mapBgRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapBgRect.pivot = new Vector2(0.5f, 0.5f);
            mapBgRect.sizeDelta = drawSize;
            mapBgRect.anchoredPosition = Vector2.zero;
            var mapBgImg = mapBgGo.AddComponent<Image>();
            mapBgImg.color = new Color(0.15f, 0.18f, 0.15f, 1f);
            mapBgImg.raycastTarget = false;

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
                go.transform.SetParent(previewMapArea, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = horizontal ? new Vector2(Vector2.Distance(pa, pb), 3f) : new Vector2(3f, Vector2.Distance(pa, pb));
                rt.anchoredPosition = (pa + pb) / 2f;
                var img = go.AddComponent<Image>();
                img.color = ZoneColors.TryGetValue(z.Player, out var c) ? c : Color.white;
                img.raycastTarget = false;
            }

            foreach (var t in terrain)
            {
                var go = new GameObject("Terrain", typeof(RectTransform));
                go.transform.SetParent(previewMapArea, false);
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
            }

            foreach (var o in objectives)
            {
                var go = new GameObject($"Objective_{o.Number}", typeof(RectTransform));
                go.transform.SetParent(previewMapArea, false);
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
            // 위 두 줄(엔트리로/저장 아이콘 줄 + 지도 크기 줄) 아래로.
            panelRect.anchoredPosition = new Vector2(MarginPx, -(MarginPx + 32f + MarginPx + 32f + MarginPx));
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

        /// <summary>텍스트 대신 아이콘 하나로 된 정사각형 버튼(사용자 요청 —
        /// "프리셋 저장" 버튼을 SaveButton.png로) — CreateButton과 달리
        /// 배경색 없이 아이콘 자체만 보여준다(마커바의 아이콘 버튼들과
        /// 같은 스타일).</summary>
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
            _objectiveLayer.sizeDelta = mapSize;
            _zoomLevel = 1f; // 지도 크기를 바꾸면 줌/팬도 깨끗한 상태로 되돌린다.
            UpdateMapLayout();
        }

        /// <summary>지도가 팔레트/크기 행/우측 프리셋 목록을 뺀 남는 뷰포트
        /// 안에 들어오도록 축소해서 배치한다(원본 mm 크기가 뷰포트보다
        /// 작으면 1:1로 그대로),
        /// 그 뷰포트 안에서 정중앙에 오도록 놓는다(사용자 요청 — 예전엔
        /// 뷰포트의 좌하단 구석에 딱 붙어서 화면 왼쪽 아래로 치우쳐 보였다).
        /// 여기서 계산하는 scale은 "뷰포트에 꽉 맞추는" 기준 배율
        /// (_baseScaleFactor)일 뿐이고, 실제 적용 배율은 여기에 사용자가
        /// 휠로 조절한 _zoomLevel을 곱한 값이다 — 게임 보드(BoardManager.
        /// PanZoom.cs)와 완전히 같은 구조. 창 크기가 바뀔 때마다(Update())
        /// 다시 불려서 팬 오프셋도 이 정중앙 위치로 재설정된다.</summary>
        private void UpdateMapLayout()
        {
            if (_mapArea == null)
            {
                return;
            }
            float left = MarginPx + 180f + MarginPx; // 팔레트 폭 + 여백
            float right = MarginPx + PresetListPanelWidth + MarginPx; // 프리셋 목록 폭 + 여백
            float top = MarginPx + 32f + MarginPx + 32f + MarginPx; // 상단 두 줄(엔트리로/저장 + 지도 크기) 높이 + 여백
            float viewportW = Mathf.Max(Screen.width - left - right, 10f);
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

        // ── 배치구역 그리기 ─────────────────────────────────────────────

        private void OnZoneButtonClicked(string player)
        {
            _activeZonePlayer = _activeZonePlayer == player ? "" : player;
            foreach (var kv in _zoneButtons)
            {
                SetButtonHighlighted(kv.Value, kv.Key == _activeZonePlayer);
            }
            ClearObjectiveMode();
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

        // ── 완료 ────────────────────────────────────────────────────

        /// <summary>지금 화면에 놓여있는 배치구역/미션 목표를 그대로 읽어낸다
        /// — "프리셋 저장"이 이 스냅샷을 파일로 쓴다. 지형은 여기 없다 —
        /// 이 화면은 더 이상 지형을 다루지 않는다(TerrainSetupController가
        /// 매 게임 새로 배치, 프리셋으로 저장하지 않음).</summary>
        private void CollectCurrentState(out List<DeploymentZoneData> zones, out List<MissionObjectiveData> objectives)
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
        }

        // ── 배치 프리셋 저장/불러오기 ──────────────────────────────────
        // 배치구역/미션 목표 배치를 JSON 파일로 저장했다가 나중에 다시
        // 불러온다(사용자 요청). 저장 위치(Deployments/)는 우측 프리셋 목록
        // 패널(BuildPresetListPanel)이 스캔하는 폴더와 같아야 한다. 지형은
        // 여기서 다루지 않으므로 MapPresetIO.Save에는 항상 빈 목록을
        // 넘긴다 — 스키마 자체를 건드리지 않아 구버전 프리셋 파일(지형
        // 데이터가 남아있는)과도 읽기 호환이 유지된다(다만 이 화면은 그
        // 데이터를 더 이상 쓰지 않는다).

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

            CollectCurrentState(out var zones, out var objectives);
            try
            {
                MapPresetIO.Save(path, _currentPreset, zones, objectives, new List<TerrainPieceData>());
                RefreshPresetList(); // 방금 저장한 파일이 목록에 바로 보이도록.
            }
            catch (Exception e)
            {
                Debug.LogWarning($"프리셋을 저장하지 못했습니다: {path} ({e.Message})");
            }
        }

        /// <summary>프리셋 목록 항목을 클릭하면 바로 불러온다(예전엔 파일
        /// 탐색 다이얼로그의 FileSelected 이벤트가 이걸 불렀는데, 그
        /// 다이얼로그 자체가 없어져서 목록 항목의 클릭이 직접 부른다).</summary>
        private void LoadPresetFromFile(string path)
        {
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

            if (!MapPresetIO.TryLoad(jsonText, out var mapPreset, out var zones, out var objectives, out _, out var error))
            {
                Debug.LogWarning($"프리셋 파일 형식이 올바르지 않습니다: {path} — {error}");
                return;
            }

            ApplyLoadedPreset(mapPreset, zones, objectives);
        }

        /// <summary>불러온 프리셋 데이터를 실제 화면에 반영한다 —
        /// LoadPresetFromFile이 부른다. ApplyPreset이 기존 배치구역/목표를 전부 지우고
        /// 지도 크기를 다시 잡아준다 — "완료"와 달리 여기선 그 뒤에 곧바로
        /// 불러온 내용을 다시 채워 넣는다. 지형은 이 화면에 없으므로 파일에
        /// 남아있는 구버전 지형 데이터가 있어도 그냥 버려진다(사용 안 함).</summary>
        private void ApplyLoadedPreset(string mapPreset, List<DeploymentZoneData> zones, List<MissionObjectiveData> objectives)
        {
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
