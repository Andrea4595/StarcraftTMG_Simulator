using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 멀티플레이어 전용 "카드 준비" 화면(2026-08-31 신설) — 호스트와
    /// 클라이언트가 연결되는 즉시(RelayConnectionTest) 양쪽 다 자동으로
    /// 여기로 들어온다. 각자 자기 화면에서 배치 프리셋 2장 + 미션 프리셋
    /// 2장을 고른다(SelectionController와 같은 목록/탭/썸네일 코드를 재사용
    /// 패턴으로 복제 — 이 프로젝트가 MapAuthoringController/
    /// SelectionController처럼 비슷한 목록 코드를 이미 여러 화면에 중복시켜
    /// 온 것과 같은 방식, 공용 유틸로 뽑지 않는다). SelectionController와의
    /// 차이는 클릭이 "토글"이고 카테고리당 최대 2개까지만 고를 수 있다는 것.
    ///
    /// "준비 완료"를 누르면 고른 4개 프리셋 파일의 원본 JSON 텍스트를(상대
    /// 컴퓨터엔 그 파일이 없을 수 있으므로 파싱 결과가 아니라 원본을,
    /// 로스터/유닛 동기화와 같은 이유) DraftState.Local*에 저장하고
    /// BoardNetworkSync로 방송한 뒤, 상대를 기다리지 않고 곧바로 CardDraft로
    /// 넘어간다(2026-08-31 사용자 지정) — 상대의 4장은 아직 안 왔으면
    /// 자리표시자로 보이다가, 나중에 도착하는 순간 그 화면에서 채워진다
    /// (DraftState.ApplyRemoteCardPrepJson, CardDraftController.RefreshRemoteCards).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CardPrepController : MonoBehaviour
    {
        public event Action BackRequested;

        private const float MarginPx = 16f;
        private const float PanelGap = 16f;
        private const float ThumbnailSize = 72f;

        private const float TitleHeight = 32f;
        private const float TabRowHeight = 36f;
        private const float RowGap = 8f;
        private const float ConfirmButtonHeight = 40f;
        private const float TopReservedHeight = MarginPx + TitleHeight + RowGap + TabRowHeight + RowGap;
        private const float BottomReservedHeight = MarginPx + ConfirmButtonHeight + RowGap;

        private RectTransform Root => (RectTransform)transform;

        private RectTransform _mapListContent;
        private RectTransform _missionListContent;
        private TextMeshProUGUI _mapStatusLabel;
        private TextMeshProUGUI _missionStatusLabel;
        private Button _readyButton;
        private TextMeshProUGUI _readyButtonLabel;

        private static string s_selectedScale = MissionSettingsData.EngagementScaleStandard;

        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();

        // 카테고리당 최대 2개 — 순서는 고른 순서(선입선출은 아니고, 다시
        // 클릭하면 그 자리에서 빠진다).
        private readonly List<(string Path, string Name)> _pickedDeploymentPaths = new List<(string, string)>();
        private readonly List<(string Path, string Name)> _pickedMissionPaths = new List<(string, string)>();
        private readonly Dictionary<string, Image> _mapItemBgByPath = new Dictionary<string, Image>();
        private readonly Dictionary<string, Image> _missionItemBgByPath = new Dictionary<string, Image>();

        // "준비 완료" 버튼 클릭 처리 자체는 즉시 씬을 넘기므로 이 화면이
        // 다시 클릭을 받을 일이 없지만, 혹시 모를 중복 호출을 막는 방어용 플래그.
        private bool _readySent;

        private void Start()
        {
            // 카드 준비 화면에 들어올 때마다 새로 시작 — 직전 판(멀티 게임을
            // 한 번 마치고 다시 시작하는 경우 등)의 드래프트 상태가 남아있으면
            // 안 되므로, UI를 짓기 전에 먼저 비운다.
            DraftState.Clear();

            BuildBackButton();
            BuildTitle();
            BuildTabRow();
            BuildMapPanel();
            BuildMissionPanel();
            BuildReadyButton();
        }

        private static string ScaleToMapPreset(string scale)
        {
            return scale == MissionSettingsData.EngagementScaleStandard ? "54x36" : "36x36";
        }

        private void BuildBackButton()
        {
            var go = new GameObject("BackButton", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(MarginPx, -MarginPx);
            rect.sizeDelta = new Vector2(32f, 32f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/BackButton");
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                DraftState.Clear();
                BackRequested?.Invoke();
            });
        }

        private void BuildTitle()
        {
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(Root, false);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -MarginPx);
            titleRect.sizeDelta = new Vector2(700f, TitleHeight);
            var titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            titleLabel.text = "배치 프리셋 2장, 미션 프리셋 2장을 골라주세요";
            titleLabel.fontSize = 18f;
            titleLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.raycastTarget = false;
        }

        private void BuildTabRow()
        {
            var rowGo = new GameObject("TabRow", typeof(RectTransform));
            rowGo.transform.SetParent(Root, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = new Vector2(0.5f, 1f);
            rowRect.anchorMax = new Vector2(0.5f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -(MarginPx + TitleHeight + RowGap));
            rowRect.sizeDelta = new Vector2(420f, TabRowHeight);

            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            foreach (var scale in new[] { MissionSettingsData.EngagementScaleStandard, MissionSettingsData.EngagementScaleSkirmish })
            {
                string captured = scale;
                var btn = CreateButton(rowRect, scale, () => OnTabSelected(captured));
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 200f;
                le.flexibleWidth = 1f;
                _tabButtons[scale] = btn;
            }
            RefreshTabHighlight();
        }

        private void OnTabSelected(string scale)
        {
            if (scale == s_selectedScale || DraftState.LocalReady)
            {
                return;
            }
            s_selectedScale = scale;
            RefreshTabHighlight();

            _pickedDeploymentPaths.Clear();
            _pickedMissionPaths.Clear();
            RefreshMapList();
            RefreshMissionList();
            RefreshMapStatusLabel();
            RefreshMissionStatusLabel();
            RefreshReadyButtonState();
        }

        private void RefreshTabHighlight()
        {
            foreach (var kv in _tabButtons)
            {
                bool selected = kv.Key == s_selectedScale;
                var img = kv.Value.GetComponent<Image>();
                if (img != null)
                {
                    img.color = selected ? new Color(0.25f, 0.45f, 0.75f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
                }
                var label = kv.Value.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        // ── 배치 프리셋 패널(왼쪽 절반) ──────────────────────────────────

        private void BuildMapPanel()
        {
            var panelRect = CreateHalfPanel(left: true);

            var layout = panelRect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var titleLabel = CreateLabel(panelRect, "배치 프리셋 (2장)", 16f, FontStyles.Bold);
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _mapStatusLabel = CreateLabel(panelRect, "선택됨 (0/2)", 13f, FontStyles.Normal);
            _mapStatusLabel.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            _mapStatusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            _mapListContent = ScrollListUtil.Create(panelRect, 100f, new Color(0f, 0f, 0f, 0.15f), out _, out var scrollLe);
            scrollLe.flexibleHeight = 1f;

            RefreshMapList();
        }

        private void RefreshMapList()
        {
            for (int i = _mapListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_mapListContent.GetChild(i).gameObject);
            }
            _mapItemBgByPath.Clear();

            string wantedPreset = ScaleToMapPreset(s_selectedScale);
            foreach (var path in ListPresetFiles(ResolveMapPresetDirectory()))
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
                if (!MapPresetIO.TryLoad(jsonText, out var mapPreset, out var zones, out var objectives, out _, out _)
                        || mapPreset != wantedPreset)
                {
                    continue;
                }
                CreateMapListItem(path, mapPreset, zones, objectives);
            }
        }

        private void CreateMapListItem(string path, string mapPreset, List<DeploymentZoneData> zones, List<MissionObjectiveData> objectives)
        {
            string name = Path.GetFileNameWithoutExtension(path);

            var itemGo = new GameObject("Item", typeof(RectTransform));
            itemGo.transform.SetParent(_mapListContent, false);
            var itemLayout = itemGo.AddComponent<HorizontalLayoutGroup>();
            itemLayout.padding = new RectOffset(6, 6, 6, 6);
            itemLayout.spacing = 8f;
            itemLayout.childAlignment = TextAnchor.MiddleLeft;
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandWidth = false;
            itemLayout.childControlHeight = true;
            itemLayout.childForceExpandHeight = false;
            var itemLe = itemGo.AddComponent<LayoutElement>();
            itemLe.preferredHeight = ThumbnailSize + 12f;

            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            _mapItemBgByPath[path] = itemBg;
            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemBg;
            itemBtn.onClick.AddListener(() => ToggleMapPick(path, name));

            var thumbGo = new GameObject("Thumb", typeof(RectTransform));
            thumbGo.transform.SetParent(itemGo.transform, false);
            var thumbLe = thumbGo.AddComponent<LayoutElement>();
            thumbLe.preferredWidth = ThumbnailSize;
            thumbLe.preferredHeight = ThumbnailSize;
            var thumbRect = (RectTransform)thumbGo.transform;
            var thumbBg = thumbGo.AddComponent<Image>();
            thumbBg.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            thumbBg.raycastTarget = false;
            DrawMapThumbnail(thumbRect, ThumbnailSize, mapPreset, zones, objectives);

            var nameLabel = CreateLabel((RectTransform)itemGo.transform, name, 13f, FontStyles.Normal);
            var nameLe = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameLe.preferredWidth = 200f - ThumbnailSize;
            nameLe.flexibleWidth = 1f;
        }

        private void ToggleMapPick(string path, string name)
        {
            if (DraftState.LocalReady)
            {
                return;
            }
            int idx = _pickedDeploymentPaths.FindIndex(e => e.Path == path);
            if (idx >= 0)
            {
                _pickedDeploymentPaths.RemoveAt(idx);
            }
            else
            {
                if (_pickedDeploymentPaths.Count >= 2)
                {
                    return;
                }
                _pickedDeploymentPaths.Add((path, name));
            }
            RefreshMapHighlights();
            RefreshMapStatusLabel();
            RefreshReadyButtonState();
        }

        private void RefreshMapHighlights()
        {
            foreach (var kv in _mapItemBgByPath)
            {
                bool picked = _pickedDeploymentPaths.Exists(e => e.Path == kv.Key);
                kv.Value.color = picked ? new Color(0.25f, 0.45f, 0.3f, 1f) : new Color(0.22f, 0.22f, 0.22f, 1f);
            }
        }

        private void RefreshMapStatusLabel()
        {
            _mapStatusLabel.text = $"선택됨 ({_pickedDeploymentPaths.Count}/2)";
        }

        // ── 미션 프리셋 패널(오른쪽 절반) ──────────────────────────────────

        private void BuildMissionPanel()
        {
            var panelRect = CreateHalfPanel(left: false);

            var layout = panelRect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var titleLabel = CreateLabel(panelRect, "미션 프리셋 (2장)", 16f, FontStyles.Bold);
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _missionStatusLabel = CreateLabel(panelRect, "선택됨 (0/2)", 13f, FontStyles.Normal);
            _missionStatusLabel.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            _missionStatusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            _missionListContent = ScrollListUtil.Create(panelRect, 100f, new Color(0f, 0f, 0f, 0.15f), out _, out var scrollLe);
            scrollLe.flexibleHeight = 1f;

            RefreshMissionList();
        }

        private struct MissionPresetData
        {
            public string MissionName;
            public string EngagementScale;
        }

        private static bool TryLoadMissionPreset(string path, out MissionPresetData preset)
        {
            preset = default;
            string jsonText;
            try
            {
                jsonText = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return false;
            }
            if (!MissionSettingsPresetIO.TryLoad(jsonText, out var missionName, out _, out _,
                    out _, out _, out _, out _, out var engagementScale, out _))
            {
                return false;
            }
            preset = new MissionPresetData { MissionName = missionName, EngagementScale = engagementScale };
            return true;
        }

        private void RefreshMissionList()
        {
            for (int i = _missionListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_missionListContent.GetChild(i).gameObject);
            }
            _missionItemBgByPath.Clear();

            foreach (var path in ListPresetFiles(ResolveMissionPresetDirectory()))
            {
                if (!TryLoadMissionPreset(path, out var preset) || preset.EngagementScale != s_selectedScale)
                {
                    continue;
                }
                CreateMissionListItem(path, preset);
            }
        }

        private void CreateMissionListItem(string path, MissionPresetData preset)
        {
            string displayName = string.IsNullOrEmpty(preset.MissionName) ? Path.GetFileNameWithoutExtension(path) : preset.MissionName;

            var itemGo = new GameObject("Item", typeof(RectTransform));
            itemGo.transform.SetParent(_missionListContent, false);
            var itemLayout = itemGo.AddComponent<VerticalLayoutGroup>();
            itemLayout.padding = new RectOffset(6, 6, 6, 6);
            itemLayout.spacing = 2f;
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandWidth = true;
            itemLayout.childControlHeight = true;
            itemLayout.childForceExpandHeight = false;

            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            _missionItemBgByPath[path] = itemBg;
            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemBg;
            itemBtn.onClick.AddListener(() => ToggleMissionPick(path, displayName));

            var nameLabel = CreateLabel((RectTransform)itemGo.transform, displayName, 13f, FontStyles.Bold);
            nameLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var scaleLabel = CreateLabel((RectTransform)itemGo.transform, preset.EngagementScale, 11f, FontStyles.Normal);
            scaleLabel.color = new Color(0.7f, 0.75f, 0.85f, 1f);
            scaleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
        }

        private void ToggleMissionPick(string path, string name)
        {
            if (DraftState.LocalReady)
            {
                return;
            }
            int idx = _pickedMissionPaths.FindIndex(e => e.Path == path);
            if (idx >= 0)
            {
                _pickedMissionPaths.RemoveAt(idx);
            }
            else
            {
                if (_pickedMissionPaths.Count >= 2)
                {
                    return;
                }
                _pickedMissionPaths.Add((path, name));
            }
            RefreshMissionHighlights();
            RefreshMissionStatusLabel();
            RefreshReadyButtonState();
        }

        private void RefreshMissionHighlights()
        {
            foreach (var kv in _missionItemBgByPath)
            {
                bool picked = _pickedMissionPaths.Exists(e => e.Path == kv.Key);
                kv.Value.color = picked ? new Color(0.25f, 0.45f, 0.3f, 1f) : new Color(0.22f, 0.22f, 0.22f, 1f);
            }
        }

        private void RefreshMissionStatusLabel()
        {
            _missionStatusLabel.text = $"선택됨 ({_pickedMissionPaths.Count}/2)";
        }

        // ── "준비 완료" ──────────────────────────────────────────────────

        private void BuildReadyButton()
        {
            var go = new GameObject("ReadyButton", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, MarginPx);
            rect.sizeDelta = new Vector2(240f, ConfirmButtonHeight);

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnReadyButtonClicked);
            _readyButton = btn;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            _readyButtonLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _readyButtonLabel.text = "준비 완료";
            _readyButtonLabel.alignment = TextAlignmentOptions.Center;
            _readyButtonLabel.fontSize = 16f;
            _readyButtonLabel.fontStyle = FontStyles.Bold;
            _readyButtonLabel.color = Color.white;
            _readyButtonLabel.raycastTarget = false;

            RefreshReadyButtonState();
        }

        private void RefreshReadyButtonState()
        {
            if (_readySent)
            {
                _readyButton.interactable = false;
                _readyButtonLabel.text = "준비 완료";
                _readyButton.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.25f, 1f);
                return;
            }
            bool ready = _pickedDeploymentPaths.Count == 2 && _pickedMissionPaths.Count == 2;
            _readyButton.interactable = ready;
            _readyButtonLabel.text = "준비 완료";
            _readyButton.GetComponent<Image>().color = ready
                    ? new Color(0.25f, 0.55f, 0.3f, 1f)
                    : new Color(0.25f, 0.25f, 0.25f, 1f);
        }

        /// <summary>상대를 기다리지 않고, 누르는 즉시 CardDraft로 넘어간다
        /// (2026-08-31 사용자 지정 — 상대가 아직 안 끝났으면 그쪽 카드
        /// 자리는 CardDraft 화면에서 자리표시자로 보이다가, 상대가 나중에
        /// 끝내는 순간 방송으로 채워진다).</summary>
        private void OnReadyButtonClicked()
        {
            if (_readySent || _pickedDeploymentPaths.Count != 2 || _pickedMissionPaths.Count != 2)
            {
                return;
            }
            _readySent = true;
            RefreshReadyButtonState();

            DraftState.LocalDeployment.Clear();
            DraftState.LocalMission.Clear();
            foreach (var (path, name) in _pickedDeploymentPaths)
            {
                DraftState.LocalDeployment.Add(new DraftState.PresetEntry { DisplayName = name, JsonText = ReadFileSafely(path) });
            }
            foreach (var (path, name) in _pickedMissionPaths)
            {
                DraftState.LocalMission.Add(new DraftState.PresetEntry { DisplayName = name, JsonText = ReadFileSafely(path) });
            }
            DraftState.LocalReady = true;
            DraftState.BuildLocalPool();

            var root = new Dictionary<string, object>
            {
                ["deployment"] = ToWireList(DraftState.LocalDeployment),
                ["mission"] = ToWireList(DraftState.LocalMission),
            };
            string wrapperJson = MiniJson.Write(root);
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[CardPrepController] BoardNetworkSync.Instance가 없음 — 카드 준비를 못 보냄");
            }
            else
            {
                BoardNetworkSync.Instance.RequestBroadcastCardPrep(NetworkTeam.LocalTeam(), wrapperJson);
            }

            UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.CardDraftSceneName);
        }

        private static string ReadFileSafely(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static List<object> ToWireList(List<DraftState.PresetEntry> entries)
        {
            var list = new List<object>();
            foreach (var e in entries)
            {
                list.Add(new Dictionary<string, object> { ["name"] = e.DisplayName, ["json"] = e.JsonText });
            }
            return list;
        }

        // ── 공용 위젯(SelectionController와 동일한 코드) ───────────────────

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = Color.white;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(160f, 36f);

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
            text.fontSize = 13f;
            text.color = Color.white;
            text.raycastTarget = false;

            return btn;
        }

        private static void DrawMapThumbnail(RectTransform previewMapArea, float boxSize, string mapPreset,
                List<DeploymentZoneData> zones, List<MissionObjectiveData> objectives)
        {
            Vector2 mapSize = GameConstants.MapSizePresets.TryGetValue(mapPreset, out var sz)
                    ? sz
                    : GameConstants.MapSizePresets[GameConstants.DefaultMapSizePreset];
            float scale = Mathf.Min(boxSize / mapSize.x, boxSize / mapSize.y);
            Vector2 drawSize = mapSize * scale;

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
                rt.sizeDelta = horizontal ? new Vector2(Vector2.Distance(pa, pb), 2f) : new Vector2(2f, Vector2.Distance(pa, pb));
                rt.anchoredPosition = (pa + pb) / 2f;
                var img = go.AddComponent<Image>();
                img.color = z.Player == "A" ? new Color(1f, 0.15f, 0.15f, 0.9f) : new Color(0.15f, 0.35f, 1f, 0.9f);
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
                rt.sizeDelta = new Vector2(8f, 8f);
                rt.anchoredPosition = ToPreviewLocal(o.Position);
                var img = go.AddComponent<Image>();
                img.color = new Color(0.85f, 0.85f, 0.8f);
                img.raycastTarget = false;
            }
        }

        private static string[] ListPresetFiles(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        private static string ResolveMapPresetDirectory()
        {
            string dir = Path.Combine(AppPaths.ExeDirectory(), "Deployments");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string ResolveMissionPresetDirectory()
        {
            string dir = Path.Combine(AppPaths.ExeDirectory(), "Missions");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private RectTransform CreateHalfPanel(bool left)
        {
            var panelGo = new GameObject(left ? "MapPanel" : "MissionPanel", typeof(RectTransform));
            panelGo.transform.SetParent(Root, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(left ? 0f : 0.5f, 0f);
            panelRect.anchorMax = new Vector2(left ? 0.5f : 1f, 1f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            float halfGap = PanelGap / 2f;
            panelRect.offsetMin = new Vector2(left ? MarginPx : halfGap, BottomReservedHeight);
            panelRect.offsetMax = new Vector2(left ? -halfGap : -MarginPx, -TopReservedHeight);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            return panelRect;
        }
    }
}
