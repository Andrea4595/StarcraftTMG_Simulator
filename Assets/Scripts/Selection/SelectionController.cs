using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// "선택" 화면(2026-08-30 재구성으로 신설) — 실제 게임 시작 흐름의 첫
    /// 단계. Entry의 "게임 시작"에서 들어온다. 화면 위쪽에 STANDARD
    /// ENGAGEMENT/SKIRMISH LEVEL 탭이 있고(사용자 지정), 탭에 따라 지도
    /// 크기(STANDARD=54x36, SKIRMISH=36x36)와 미션 전투 규모가 같이
    /// 필터링된다 — 그 아래 배치 프리셋(Deployments/)과 미션 프리셋
    /// (Missions/) 목록을 한 화면에 나란히 보여주고, 각각 클릭 또는
    /// "무작위 선택" 버튼(지금 탭으로 필터링된 항목 중에서만 고른다)으로
    /// 원하는 순서대로 고르면 된다. 탭을 바꾸면 이전 선택은 새 필터와 안
    /// 맞을 수 있으므로 둘 다 초기화된다. 좌상단 "엔트리로" 버튼으로 언제든
    /// Entry로 돌아갈 수 있다. 배치/미션 둘 다 고른 순간 자동으로
    /// TerrainSetup 화면으로 넘어간다 — 예전 MapSetupController/
    /// MissionSetupController의 "완료" 버튼이 하던 MapData/MissionSettingsData
    /// 채우기를 이제 여기가 대신한다(단, 지형은 여기서 채우지 않는다 —
    /// TerrainSetup이 매 게임 새로 배치).
    ///
    /// 이 컴포넌트가 붙은 GameObject 자체가 화면 전체를 덮는 RectTransform
    /// (Canvas의 직속 자식, anchors (0,0)-(1,1))이라고 가정한다 — Map/Mission
    /// AuthoringController와 같은 방식.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SelectionController : MonoBehaviour
    {
        public event Action BothPicked;
        public event Action BackRequested;

        private const float MarginPx = 16f;
        private const float PanelGap = 16f;
        private const float ThumbnailSize = 72f;

        private const float TitleHeight = 32f;
        private const float TabRowHeight = 36f;
        private const float RowGap = 8f;
        private const float ConfirmButtonHeight = 40f;
        // 타이틀 + 탭 줄이 차지하는 위쪽 전체 높이 — 패널들이 이 아래부터
        // 시작해야 타이틀 문구를 가리지 않는다(사용자 지적 — 예전엔 패널이
        // MarginPx만큼만 내려와서 타이틀과 겹쳐 안 보였다).
        private const float TopReservedHeight = MarginPx + TitleHeight + RowGap + TabRowHeight + RowGap;
        // 아래쪽 "확인" 버튼 줄이 차지하는 높이 — 패널이 그 위에서 끝나야 한다.
        private const float BottomReservedHeight = MarginPx + ConfirmButtonHeight + RowGap;

        private RectTransform Root => (RectTransform)transform;

        private RectTransform _mapListContent;
        private RectTransform _missionListContent;
        private TextMeshProUGUI _mapStatusLabel;
        private TextMeshProUGUI _missionStatusLabel;
        private Button _confirmButton;

        // static인 이유: TerrainSetup에서 "돌아가기"로 이 화면에 다시 들어와도
        // 골랐던 내용이 유지돼야 한다(사용자 지정) — 이 컴포넌트는 씬을 새로
        // 로드할 때마다 통째로 다시 만들어지는 MonoBehaviour라서, 인스턴스
        // 필드로는 이전 상태가 남지 않는다. MapData/MissionSettingsData(실제
        // 게임 데이터)는 원래도 static이라 이미 유지되고 있었고, 여기 셋은
        // 그 화면 표시(하이라이트/상태 라벨/탭)만을 위한 값이다.
        private static string s_pickedMapName;
        private static string s_pickedMissionName;
        // 전투 규모 탭 — STANDARD면 54x36 지도 + STANDARD 미션만, SKIRMISH면
        // 36x36 지도 + SKIRMISH 미션만 보여준다(사용자 지정).
        private static string s_selectedScale = MissionSettingsData.EngagementScaleStandard;

        private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();

        private void Start()
        {
            BuildBackButton();
            BuildTitle();
            BuildTabRow();
            BuildMapPanel();
            BuildMissionPanel();
            BuildConfirmButton();
        }

        /// <summary>전투 규모 탭이 정하는 지도 크기 — STANDARD ENGAGEMENT는
        /// 54x36, SKIRMISH LEVEL은 36x36(사용자 지정).</summary>
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
            btn.onClick.AddListener(() => BackRequested?.Invoke());
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
            titleLabel.text = "배치 프리셋과 미션 프리셋을 골라주세요 (순서 무관)";
            titleLabel.fontSize = 18f;
            titleLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.raycastTarget = false;
        }

        /// <summary>STANDARD ENGAGEMENT / SKIRMISH LEVEL 탭 — 고르면 지도
        /// 크기(ScaleToMapPreset)와 미션 전투 규모 둘 다에 맞춰 양쪽 목록을
        /// 다시 필터링한다. 탭을 바꾸면 이전 선택은 더 이상 이 탭의 필터와
        /// 안 맞을 수 있으므로 둘 다 초기화한다.</summary>
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
                // preferredWidth를 명시적으로 준다 — CreateButton의 배경
                // Image는 스프라이트가 없어 ILayoutElement로서 폭 0을
                // 보고하는데, 이 행은 폭이 메인 축인 HorizontalLayoutGroup +
                // childForceExpandWidth=true라서 이 프로젝트에서 실제로
                // 겪은 "형제 버튼이 폭 0으로 접히는" 함정과 같은 조건이다.
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 200f;
                le.flexibleWidth = 1f;
                _tabButtons[scale] = btn;
            }
            RefreshTabHighlight();
        }

        private void OnTabSelected(string scale)
        {
            if (scale == s_selectedScale)
            {
                return;
            }
            s_selectedScale = scale;
            RefreshTabHighlight();

            // 이전 탭에서 고른 프리셋이 새 탭의 필터와 안 맞을 수 있으므로
            // 둘 다 선택 해제하고 다시 고르게 한다.
            s_pickedMapName = null;
            s_pickedMissionName = null;
            _mapStatusLabel.text = "선택되지 않음";
            _missionStatusLabel.text = "선택되지 않음";
            RefreshMapList();
            RefreshMissionList();
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

            var titleLabel = CreateLabel(panelRect, "배치 프리셋", 16f, FontStyles.Bold);
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _mapStatusLabel = CreateLabel(panelRect, string.IsNullOrEmpty(s_pickedMapName) ? "선택되지 않음" : $"선택됨: {s_pickedMapName}", 13f, FontStyles.Normal);
            _mapStatusLabel.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            _mapStatusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            _mapListContent = ScrollListUtil.Create(panelRect, 100f, new Color(0f, 0f, 0f, 0.15f), out _, out var scrollLe);
            scrollLe.flexibleHeight = 1f;

            var randomBtn = CreateButton(panelRect, "무작위 선택", PickRandomMap);
            randomBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            RefreshMapList();
            // TerrainSetup에서 "돌아가기"로 돌아온 경우 등 — 이전에 이미
            // 골랐던 항목이 지금 탭의 필터에도 남아있다면 다시 하이라이트.
            if (!string.IsNullOrEmpty(s_pickedMapName))
            {
                RefreshMapListHighlightByName(s_pickedMapName);
            }
        }

        private void RefreshMapList()
        {
            for (int i = _mapListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_mapListContent.GetChild(i).gameObject);
            }

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
            // childControlWidth=true라야 thumbGo/nameLabel의 LayoutElement.
            // preferredWidth/flexibleWidth가 실제로 반영된다(false면 각
            // 자식의 RectTransform 기본 크기를 그대로 쓴다 — 이 프로젝트의
            // 다른 프리셋 목록 항목들도 같은 이유로 true를 쓴다).
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandWidth = false;
            itemLayout.childControlHeight = true;
            itemLayout.childForceExpandHeight = false;
            var itemLe = itemGo.AddComponent<LayoutElement>();
            itemLe.preferredHeight = ThumbnailSize + 12f;

            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemBg;
            itemBtn.onClick.AddListener(() => PickMap(name, mapPreset, zones, objectives, itemBg));

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

        private void PickMap(string name, string mapPreset, List<DeploymentZoneData> zones, List<MissionObjectiveData> objectives, Image chosenBg)
        {
            MapData.Clear();
            MapData.HasData = true;
            MapData.MapPreset = mapPreset;
            MapData.DeploymentZones.AddRange(zones);
            MapData.MissionObjectives.AddRange(objectives);
            // TerrainPieces는 일부러 안 채운다 — TerrainSetup이 매 게임 새로 배치.

            s_pickedMapName = name;
            _mapStatusLabel.text = $"선택됨: {name}";
            HighlightChosen(_mapListContent, chosenBg);
            RefreshConfirmButtonState();
        }

        private void PickRandomMap()
        {
            string wantedPreset = ScaleToMapPreset(s_selectedScale);
            var candidates = new List<(string Path, string MapPreset, List<DeploymentZoneData> Zones, List<MissionObjectiveData> Objectives)>();
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
                if (MapPresetIO.TryLoad(jsonText, out var mapPreset, out var zones, out var objectives, out _, out _)
                        && mapPreset == wantedPreset)
                {
                    candidates.Add((path, mapPreset, zones, objectives));
                }
            }
            if (candidates.Count == 0)
            {
                Debug.LogWarning($"'{wantedPreset}' 배치 프리셋이 Deployments 폴더에 없습니다.");
                return;
            }
            var picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            // 목록의 해당 항목을 다시 찾아 하이라이트하기보다, 목록을 다시 그린
            // 뒤 이름으로 하이라이트를 맞춘다 — 순서가 항상 같아 간단하다.
            PickMap(Path.GetFileNameWithoutExtension(picked.Path), picked.MapPreset, picked.Zones, picked.Objectives, null);
            RefreshMapListHighlightByName(s_pickedMapName);
        }

        private void RefreshMapListHighlightByName(string name)
        {
            for (int i = 0; i < _mapListContent.childCount; i++)
            {
                var item = _mapListContent.GetChild(i);
                var label = item.GetComponentInChildren<TextMeshProUGUI>();
                var bg = item.GetComponent<Image>();
                if (label != null && bg != null && label.text == name)
                {
                    HighlightChosen(_mapListContent, bg);
                    return;
                }
            }
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

            var titleLabel = CreateLabel(panelRect, "미션 프리셋", 16f, FontStyles.Bold);
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            _missionStatusLabel = CreateLabel(panelRect, string.IsNullOrEmpty(s_pickedMissionName) ? "선택되지 않음" : $"선택됨: {s_pickedMissionName}", 13f, FontStyles.Normal);
            _missionStatusLabel.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            _missionStatusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            _missionListContent = ScrollListUtil.Create(panelRect, 100f, new Color(0f, 0f, 0f, 0.15f), out _, out var scrollLe);
            scrollLe.flexibleHeight = 1f;

            // 전투 규모 탭이 생겨서 STANDARD/SKIRMISH 무작위 버튼 두 개를
            // 따로 둘 필요가 없어졌다 — 지금 고른 탭 기준으로 하나만
            // (사용자 지정).
            var randomBtn = CreateButton(panelRect, "무작위 선택", PickRandomMission);
            randomBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 36f;

            RefreshMissionList();
            if (!string.IsNullOrEmpty(s_pickedMissionName))
            {
                RefreshMissionListHighlightByName(s_pickedMissionName);
            }
        }

        private void RefreshMissionList()
        {
            for (int i = _missionListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_missionListContent.GetChild(i).gameObject);
            }

            foreach (var path in ListPresetFiles(ResolveMissionPresetDirectory()))
            {
                if (!TryLoadMissionPreset(path, out var preset) || preset.EngagementScale != s_selectedScale)
                {
                    continue;
                }
                CreateMissionListItem(path, preset);
            }
        }

        private struct MissionPresetData
        {
            public string MissionName;
            public string MissionParameters;
            public string ScoringConditions;
            public string AdditionalConditions;
            public int BaseSupply;
            public int SupplyPerRound;
            public int RoundLength;
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
            if (!MissionSettingsPresetIO.TryLoad(jsonText, out var missionName, out var missionParameters, out var scoringConditions,
                    out var additionalConditions, out var baseSupply, out var supplyPerRound, out var roundLength,
                    out var engagementScale, out _))
            {
                return false;
            }
            preset = new MissionPresetData
            {
                MissionName = missionName,
                MissionParameters = missionParameters,
                ScoringConditions = scoringConditions,
                AdditionalConditions = additionalConditions,
                BaseSupply = baseSupply,
                SupplyPerRound = supplyPerRound,
                RoundLength = roundLength,
                EngagementScale = engagementScale,
            };
            return true;
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
            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemBg;
            itemBtn.onClick.AddListener(() => PickMission(displayName, preset, itemBg));

            var nameLabel = CreateLabel((RectTransform)itemGo.transform, displayName, 13f, FontStyles.Bold);
            nameLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

            var scaleLabel = CreateLabel((RectTransform)itemGo.transform, preset.EngagementScale, 11f, FontStyles.Normal);
            scaleLabel.color = new Color(0.7f, 0.75f, 0.85f, 1f);
            scaleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
        }

        private void PickMission(string name, MissionPresetData preset, Image chosenBg)
        {
            MissionSettingsData.Clear();
            MissionSettingsData.HasData = true;
            MissionSettingsData.MissionName = preset.MissionName;
            MissionSettingsData.MissionParameters = preset.MissionParameters;
            MissionSettingsData.ScoringConditions = preset.ScoringConditions;
            MissionSettingsData.AdditionalConditions = preset.AdditionalConditions;
            MissionSettingsData.BaseSupply = preset.BaseSupply;
            MissionSettingsData.SupplyPerRound = preset.SupplyPerRound;
            MissionSettingsData.RoundLength = preset.RoundLength;
            MissionSettingsData.EngagementScale = preset.EngagementScale;

            s_pickedMissionName = name;
            _missionStatusLabel.text = $"선택됨: {name}";
            HighlightChosen(_missionListContent, chosenBg);
            RefreshConfirmButtonState();
        }

        private void PickRandomMission()
        {
            var candidates = new List<string>();
            var loaded = new List<MissionPresetData>();
            foreach (var path in ListPresetFiles(ResolveMissionPresetDirectory()))
            {
                if (TryLoadMissionPreset(path, out var preset) && preset.EngagementScale == s_selectedScale)
                {
                    candidates.Add(path);
                    loaded.Add(preset);
                }
            }
            if (candidates.Count == 0)
            {
                Debug.LogWarning($"'{s_selectedScale}' 미션 프리셋이 Missions 폴더에 없습니다.");
                return;
            }
            int idx = UnityEngine.Random.Range(0, candidates.Count);
            var preset2 = loaded[idx];
            string displayName = string.IsNullOrEmpty(preset2.MissionName) ? Path.GetFileNameWithoutExtension(candidates[idx]) : preset2.MissionName;
            PickMission(displayName, preset2, null);
            RefreshMissionListHighlightByName(displayName);
        }

        private void RefreshMissionListHighlightByName(string name)
        {
            for (int i = 0; i < _missionListContent.childCount; i++)
            {
                var item = _missionListContent.GetChild(i);
                var label = item.GetComponentInChildren<TextMeshProUGUI>();
                var bg = item.GetComponent<Image>();
                if (label != null && bg != null && label.text == name)
                {
                    HighlightChosen(_missionListContent, bg);
                    return;
                }
            }
        }

        // ── 공용 ────────────────────────────────────────────────────

        /// <summary>목록 안의 항목 배경색을 전부 원래대로 되돌린 뒤, 골라진
        /// 항목만 밝게 칠한다 — chosenBg가 null이면(무작위 선택 경로) 호출한
        /// 쪽이 이름으로 다시 찾아 별도로 부른다.</summary>
        private static void HighlightChosen(RectTransform listContent, Image chosenBg)
        {
            for (int i = 0; i < listContent.childCount; i++)
            {
                var bg = listContent.GetChild(i).GetComponent<Image>();
                if (bg != null)
                {
                    bg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
                }
            }
            if (chosenBg != null)
            {
                chosenBg.color = new Color(0.25f, 0.45f, 0.3f, 1f);
            }
        }

        private void BuildConfirmButton()
        {
            var go = new GameObject("ConfirmButton", typeof(RectTransform));
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
            btn.onClick.AddListener(() => BothPicked?.Invoke());
            _confirmButton = btn;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "확인";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 16f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.raycastTarget = false;

            RefreshConfirmButtonState();
        }

        /// <summary>배치/미션 둘 다 골라야만 눌리는 "확인" 버튼 — 이 버튼을
        /// 눌러야 다음(TerrainSetup) 화면으로 넘어간다(사용자 지정, 예전엔
        /// 둘 다 고른 순간 자동으로 넘어갔었다).</summary>
        private void RefreshConfirmButtonState()
        {
            bool ready = !string.IsNullOrEmpty(s_pickedMapName) && !string.IsNullOrEmpty(s_pickedMissionName);
            _confirmButton.interactable = ready;
            _confirmButton.GetComponent<Image>().color = ready
                    ? new Color(0.25f, 0.55f, 0.3f, 1f)
                    : new Color(0.25f, 0.25f, 0.25f, 1f);
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
            // 위쪽은 타이틀+탭 줄, 아래쪽은 "확인" 버튼 줄 전체 높이만큼
            // 비워야 서로 가려지지 않는다 — 예전엔 위쪽 값이 반대로(아래쪽에
            // 큰 값, 위쪽에 작은 값) 들어가 있어서 패널이 타이틀을 덮어버렸다
            // (사용자 지적으로 발견).
            panelRect.offsetMin = new Vector2(left ? MarginPx : halfGap, BottomReservedHeight);
            panelRect.offsetMax = new Vector2(left ? -halfGap : -MarginPx, -TopReservedHeight);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);
            return panelRect;
        }

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

        /// <summary>배치 프리셋 하나를 boxSize 크기의 작은 지도로 그린다 —
        /// MapAuthoringController.DrawPresetThumbnail과 같은 방식(코너 원점
        /// mm → 박스 중심 로컬 좌표), 지형은 그리지 않는다(이 화면에서 다루는
        /// 프리셋엔 애초에 지형 데이터가 없다).</summary>
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
    }
}
