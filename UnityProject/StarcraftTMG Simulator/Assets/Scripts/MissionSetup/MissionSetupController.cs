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
    /// 그리기. Godot판 scenes/mission_setup/MissionSetup.gd 포팅 — 지형 배치와
    /// 미션 목표 배치는 아직 없다(텍스처/마스크 이미지 에셋이 필요해서 다음
    /// 단계). "게임 시작"을 누르면 MissionData에 스냅샷을 채우고
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

        private readonly Dictionary<string, Button> _sizeButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, Button> _zoneButtons = new Dictionary<string, Button>();

        private string _currentPreset = GameConstants.DefaultMapSizePreset;
        private string _activeZonePlayer = "";

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

            Vector2 mapSize = MapSize;
            _mapBackgroundRect.sizeDelta = mapSize;
            _gridRect.sizeDelta = mapSize;
            _zoneLayer.sizeDelta = mapSize;
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

        // ── 배치구역 그리기 ─────────────────────────────────────────────

        private void OnZoneButtonClicked(string player)
        {
            _activeZonePlayer = _activeZonePlayer == player ? "" : player;
            foreach (var kv in _zoneButtons)
            {
                SetButtonHighlighted(kv.Value, kv.Key == _activeZonePlayer);
            }
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

            StartGameRequested?.Invoke();
        }
    }
}
