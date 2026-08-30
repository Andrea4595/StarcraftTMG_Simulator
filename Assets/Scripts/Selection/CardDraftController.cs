using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 멀티플레이어 전용 "카드 드래프트" 화면(2026-08-31 신설, 같은 날
    /// 재구성) — CardPrep에서 "준비 완료"를 누른 쪽은 상대를 기다리지 않고
    /// 곧바로 여기로 넘어온다(사용자 지정). DraftState.Pool은 내 4장은 항상
    /// 실제 데이터로, 상대 4장은 아직 안 왔으면 자리표시자(어두운 회색
    /// 네모, IsPlaceholder)로 채워져 있다가 상대가 나중에 끝내면 방송으로
    /// 채워진다(RefreshRemoteCards).
    ///
    /// 공식 룰의 엄격한 순서(롤오프 → 승자가 종류 선택 → 밴·밴·픽·픽)를
    /// 강제하지 않고 "꽤 자율적으로" 구현한다(사용자 지정) — 롤오프 도구만
    /// 제공하고, 실제 순서는 두 사람이 알아서 지킨다: 양쪽 누구나 실제
    /// 카드가 도착한 것에 한해 자유롭게 우클릭(밴 토글)하거나 좌클릭(선택,
    /// 미션·배치 각 1장까지만)할 수 있다. 밴/선택 상태는 DraftState의 공유
    /// 필드에 저장되고 BoardNetworkSync를 통해 양쪽에 즉시 반영된다.
    ///
    /// 미션 1장 + 배치 1장이 모두 선택되면 "다음" 버튼이 나타나고, 누르면
    /// SelectionController.PickMap/PickMission과 같은 방식으로 MapData/
    /// MissionSettingsData를 채운 뒤 TerrainSetup으로 넘어간다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CardDraftController : MonoBehaviour
    {
        private const float MarginPx = 16f;
        private const float CardWidth = 170f;
        private const float MissionCardHeight = 156f; // 팀 라벨+이름+"정보" 버튼+상태 라벨 4단
        private const float DeploymentCardHeight = 190f;
        private const float ThumbnailSize = 96f;

        private RectTransform Root => (RectTransform)transform;

        private class CardVisual
        {
            public Image Background;
            public TextMeshProUGUI NameLabel;
            public TextMeshProUGUI StateLabel;
            public RectTransform ThumbnailRect; // 배치 카드만(미션 카드는 null)
            public Button InfoButton; // 미션 카드만(배치 카드는 null)
        }

        private readonly Dictionary<string, CardVisual> _cardVisuals = new Dictionary<string, CardVisual>();

        private RolloffDialog _rolloffDialog;
        private MissionInfoDialog _missionInfoDialog;
        private GameObject _nextButtonGo;
        private Button _rolloffButton;

        private void Start()
        {
            BuildTitle();

            if (DraftState.Pool.Count == 0)
            {
                BuildEmptyStateLabel();
                return;
            }

            BuildRolloffButton();
            BuildRow("mission", "미션 카드", MissionCardHeight, isDeployment: false, anchorTopOffset: 96f);
            BuildRow("deployment", "배치 카드", DeploymentCardHeight, isDeployment: true, anchorTopOffset: 96f + MissionCardHeight + 48f);
            BuildNextButton();

            // RolloffDialog는 반드시 별도 자식 오브젝트에 붙여야 한다 —
            // Awake()가 자기 GameObject에 화면 전체를 덮는 반투명 배경
            // Image를 직접 추가하므로(그 컴포넌트 자신의 SetActive(false)로
            // 평소엔 숨김), 이 화면의 루트에 바로 붙이면 그 배경이 루트
            // 자체가 돼 카드 클릭을 항상 가로막는다(GameFlowBootstrap의
            // BuildGameBoard가 RolloffDialog를 붙이는 방식과 동일).
            var rolloffGo = new GameObject("RolloffDialog", typeof(RectTransform));
            rolloffGo.transform.SetParent(Root, false);
            _rolloffDialog = rolloffGo.AddComponent<RolloffDialog>();

            // MissionInfoDialog도 같은 이유로 별도 자식 오브젝트에 붙인다.
            var missionInfoGo = new GameObject("MissionInfoDialog", typeof(RectTransform));
            missionInfoGo.transform.SetParent(Root, false);
            _missionInfoDialog = missionInfoGo.AddComponent<MissionInfoDialog>();

            RefreshAllCardVisuals();
            RefreshNextButtonState();
            RefreshRolloffButtonState();
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
            titleRect.sizeDelta = new Vector2(700f, 32f);
            var titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            titleLabel.text = "좌클릭: 선택(미션·배치 각 1장)   우클릭: 밴 — 자유롭게 진행하세요";
            titleLabel.fontSize = 16f;
            titleLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.raycastTarget = false;
        }

        private void BuildEmptyStateLabel()
        {
            var go = new GameObject("EmptyState", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(500f, 60f);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = "카드 데이터가 없습니다 — Entry에서 다시 시작해주세요.";
            label.fontSize = 16f;
            label.color = new Color(0.8f, 0.5f, 0.5f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }

        /// <summary>양쪽 카드의 정체가 다 밝혀졌을 때만(사용자 지정) 눌린다
        /// — DraftState.RemoteReady가 정확히 그 순간이다: 상대의 4장 원본이
        /// 도착해 자리표시자가 실제 카드로 채워진 시점과 같기 때문에(같은
        /// 값을 또 추적하는 새 신호가 필요 없다).</summary>
        private void BuildRolloffButton()
        {
            var go = new GameObject("Btn_롤오프", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-MarginPx, -MarginPx);
            rect.sizeDelta = new Vector2(120f, 36f);

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => _rolloffDialog?.Open());
            _rolloffButton = btn;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = "롤 오프";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 14f;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private void BuildRow(string category, string rowLabel, float cardHeight, bool isDeployment, float anchorTopOffset)
        {
            var labelGo = new GameObject($"{category}_Label", typeof(RectTransform));
            labelGo.transform.SetParent(Root, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0.5f, 1f);
            labelRect.anchorMax = new Vector2(0.5f, 1f);
            labelRect.pivot = new Vector2(0.5f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, -anchorTopOffset);
            labelRect.sizeDelta = new Vector2(300f, 24f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = rowLabel;
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;

            var rowGo = new GameObject($"{category}_Row", typeof(RectTransform));
            rowGo.transform.SetParent(Root, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = new Vector2(0.5f, 1f);
            rowRect.anchorMax = new Vector2(0.5f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -(anchorTopOffset + 28f));
            rowRect.sizeDelta = new Vector2(4f * CardWidth + 3f * 16f, cardHeight);

            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            foreach (var card in DraftState.Pool)
            {
                if (card.Category != category)
                {
                    continue;
                }
                BuildCardVisual(rowRect, card, cardHeight, isDeployment);
            }
        }

        private void BuildCardVisual(Transform parent, DraftState.DraftCard card, float cardHeight, bool isDeployment)
        {
            var go = new GameObject($"Card_{card.Id}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(CardWidth, cardHeight);

            var bg = go.AddComponent<Image>();
            var handler = go.AddComponent<CardClickHandler>();
            handler.LeftClicked = () => OnCardLeftClicked(card);
            handler.RightClicked = () => OnCardRightClicked(card);

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            // childControlHeight는 반드시 true여야 한다 — false로 두면
            // 레이아웃 그룹이 커서만 LayoutElement.preferredHeight만큼
            // 이동시키고 그 자식의 실제 RectTransform 높이는 그대로 두는데,
            // 새로 만든 RectTransform의 기본 높이가 preferredHeight와 달라서
            // (특히 배경 Image가 있는 "정보" 버튼에서) 카드 안에서 세로로
            // 길게 삐져나와 보였다(사용자 발견 — ScrollListUtil.cs에 이미
            // 적혀 있는 것과 같은 함정).
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var teamLabel = CreateLabel(go.transform, card.OwnerTeam == "A" ? "팀 A" : "팀 B", 11f, FontStyles.Bold);
            teamLabel.color = GameConstants.TeamColors.TryGetValue(card.OwnerTeam, out var tc) ? tc : Color.white;
            teamLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            RectTransform thumbnailRect = null;
            if (isDeployment)
            {
                var thumbGo = new GameObject("Thumb", typeof(RectTransform));
                thumbGo.transform.SetParent(go.transform, false);
                var thumbLe = thumbGo.AddComponent<LayoutElement>();
                thumbLe.preferredWidth = ThumbnailSize;
                thumbLe.preferredHeight = ThumbnailSize;
                var thumbBg = thumbGo.AddComponent<Image>();
                thumbBg.color = new Color(0.05f, 0.05f, 0.05f, 1f);
                thumbBg.raycastTarget = false;
                thumbnailRect = (RectTransform)thumbGo.transform;
                if (!card.IsPlaceholder && card.Zones != null && card.Objectives != null)
                {
                    DrawMapThumbnail(thumbnailRect, ThumbnailSize, card.MapPreset, card.Zones, card.Objectives);
                }
            }

            var nameLabel = CreateLabel(go.transform, card.IsPlaceholder ? "???" : card.DisplayName, 13f, FontStyles.Normal);
            var nameLe = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameLe.preferredHeight = 32f;
            nameLabel.textWrappingMode = TextWrappingModes.Normal;

            Button infoButton = null;
            if (!isDeployment)
            {
                infoButton = CreateInfoButton(go.transform, card);
            }

            var stateLabel = CreateLabel(go.transform, "", 11f, FontStyles.Bold);
            stateLabel.color = new Color(1f, 0.55f, 0.55f, 1f);
            stateLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            _cardVisuals[card.Id] = new CardVisual
            {
                Background = bg,
                NameLabel = nameLabel,
                StateLabel = stateLabel,
                ThumbnailRect = thumbnailRect,
                InfoButton = infoButton,
            };
        }

        /// <summary>미션 카드에만 붙는 작은 "정보" 버튼 — 좌클릭(선택)/
        /// 우클릭(밴)과 겹치지 않도록 별도 버튼으로 뺐다. 카드 배경의
        /// CardClickHandler보다 위에 그려져야 클릭이 먼저 잡히므로, 카드
        /// 배경보다 나중에(레이아웃 순서상 아래 자식으로) 추가한다.</summary>
        private Button CreateInfoButton(Transform parent, DraftState.DraftCard card)
        {
            var go = new GameObject("InfoButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 22f;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.35f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = !card.IsPlaceholder;
            btn.onClick.AddListener(() => OnInfoButtonClicked(card));

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = "정보";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 12f;
            text.color = Color.white;
            text.raycastTarget = false;

            return btn;
        }

        private void OnInfoButtonClicked(DraftState.DraftCard card)
        {
            if (card.IsPlaceholder || _missionInfoDialog == null)
            {
                return;
            }
            _missionInfoDialog.Open(card.DisplayName, card.MissionParameters, card.ScoringConditions,
                    card.AdditionalConditions, card.BaseSupply, card.SupplyPerRound, card.RoundLength, card.EngagementScale);
        }

        private void OnCardLeftClicked(DraftState.DraftCard card)
        {
            // 아직 상대 데이터가 안 온 자리표시자는 선택할 게 없다.
            if (card.IsPlaceholder || DraftState.BannedIds.Contains(card.Id))
            {
                return;
            }
            string current = card.Category == "mission" ? DraftState.SelectedMissionId : DraftState.SelectedDeploymentId;
            string newId = current == card.Id ? "" : card.Id;
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[CardDraftController] BoardNetworkSync.Instance가 없음 — 카드 선택 요청을 못 보냄");
                return;
            }
            BoardNetworkSync.Instance.RequestSetDraftSelectionServerRpc(card.Category, newId);
        }

        private void OnCardRightClicked(DraftState.DraftCard card)
        {
            if (card.IsPlaceholder)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[CardDraftController] BoardNetworkSync.Instance가 없음 — 밴 요청을 못 보냄");
                return;
            }
            BoardNetworkSync.Instance.RequestToggleDraftBanServerRpc(card.Id);
        }

        /// <summary>BoardNetworkSync가 상대 카드 데이터 방송을 완성했을 때
        /// 부른다 — DraftState.Pool의 해당 자리표시자는 이미 실제 내용으로
        /// 채워져 있으므로(DraftState.ApplyRemoteCardPrepJson), 여기선 화면
        /// (이름/썸네일/배경색)만 다시 그린다.</summary>
        public void RefreshRemoteCards()
        {
            RefreshRolloffButtonState();
            if (_cardVisuals.Count == 0)
            {
                return; // 아직 이 화면의 카드 UI가 지어지기 전 — Start()가 최신 Pool을 그대로 읽어간다.
            }
            foreach (var card in DraftState.Pool)
            {
                RefreshCardContent(card);
                RefreshCardVisual(card.Id);
            }
        }

        /// <summary>양쪽 카드가 다 밝혀졌을 때만(DraftState.RemoteReady) 롤
        /// 오프를 누를 수 있게 한다(사용자 지정) — 이 값이 상대 4장이
        /// 도착해 자리표시자가 실제 카드로 바뀐 바로 그 순간이라, 별도
        /// "상대가 이 화면에 도착했는지" 신호를 새로 만들 필요가 없다.</summary>
        private void RefreshRolloffButtonState()
        {
            if (_rolloffButton == null)
            {
                return;
            }
            bool ready = DraftState.RemoteReady;
            _rolloffButton.interactable = ready;
            _rolloffButton.GetComponent<Image>().color = ready
                    ? new Color(0.3f, 0.3f, 0.3f, 1f)
                    : new Color(0.2f, 0.2f, 0.2f, 1f);
        }

        /// <summary>카드 이름/(배치면) 썸네일을 지금 card 내용대로 다시
        /// 그린다 — 자리표시자가 실제 카드로 바뀌었을 때 부른다.</summary>
        private void RefreshCardContent(DraftState.DraftCard card)
        {
            if (!_cardVisuals.TryGetValue(card.Id, out var visual))
            {
                return;
            }
            visual.NameLabel.text = card.IsPlaceholder ? "???" : card.DisplayName;
            if (visual.InfoButton != null)
            {
                visual.InfoButton.interactable = !card.IsPlaceholder;
            }
            if (visual.ThumbnailRect != null)
            {
                for (int i = visual.ThumbnailRect.childCount - 1; i >= 0; i--)
                {
                    Destroy(visual.ThumbnailRect.GetChild(i).gameObject);
                }
                if (!card.IsPlaceholder && card.Zones != null && card.Objectives != null)
                {
                    DrawMapThumbnail(visual.ThumbnailRect, ThumbnailSize, card.MapPreset, card.Zones, card.Objectives);
                }
            }
        }

        /// <summary>BoardNetworkSync.SetDraftSelectionRpc가 방송을 받았을 때
        /// 호출한다(요청한 쪽 자신도 포함).</summary>
        public void ApplySetSelection(string category, string cardId)
        {
            string resolved = string.IsNullOrEmpty(cardId) ? null : cardId;
            if (category == "mission")
            {
                DraftState.SelectedMissionId = resolved;
            }
            else
            {
                DraftState.SelectedDeploymentId = resolved;
            }
            RefreshAllCardVisuals();
            RefreshNextButtonState();
        }

        /// <summary>BoardNetworkSync.ToggleDraftBanRpc가 방송을 받았을 때
        /// 호출한다 — 밴하는 순간 그 카드가 선택돼 있었으면 선택도 같이
        /// 해제한다(양쪽 클라이언트가 결정적으로 동일하게 처리하므로 추가
        /// 메시지 없이도 어긋나지 않는다).</summary>
        public void ApplyToggleBan(string cardId)
        {
            if (!DraftState.BannedIds.Add(cardId))
            {
                DraftState.BannedIds.Remove(cardId);
            }
            if (DraftState.BannedIds.Contains(cardId))
            {
                if (DraftState.SelectedMissionId == cardId)
                {
                    DraftState.SelectedMissionId = null;
                }
                if (DraftState.SelectedDeploymentId == cardId)
                {
                    DraftState.SelectedDeploymentId = null;
                }
            }
            RefreshAllCardVisuals();
            RefreshNextButtonState();
        }

        private void RefreshAllCardVisuals()
        {
            foreach (var card in DraftState.Pool)
            {
                RefreshCardVisual(card.Id);
            }
        }

        private void RefreshCardVisual(string id)
        {
            if (!_cardVisuals.TryGetValue(id, out var visual))
            {
                return;
            }
            var card = DraftState.FindCard(id);
            // 아직 상대 데이터가 안 온 자리표시자는 어두운 회색 네모로만
            // 보여준다(사용자 지정) — 밴/선택 상태와 무관하게 항상 이 모습.
            if (card == null || card.IsPlaceholder)
            {
                visual.Background.color = new Color(0.12f, 0.12f, 0.12f, 1f);
                visual.StateLabel.text = "";
                return;
            }
            bool banned = DraftState.BannedIds.Contains(id);
            bool selected = id == DraftState.SelectedMissionId || id == DraftState.SelectedDeploymentId;
            if (banned)
            {
                visual.Background.color = new Color(0.16f, 0.08f, 0.08f, 1f);
                visual.StateLabel.text = "밴됨";
            }
            else if (selected)
            {
                visual.Background.color = new Color(0.25f, 0.55f, 0.3f, 1f);
                visual.StateLabel.text = "선택됨";
            }
            else
            {
                visual.Background.color = new Color(0.22f, 0.22f, 0.22f, 1f);
                visual.StateLabel.text = "";
            }
        }

        private void BuildNextButton()
        {
            var go = new GameObject("NextButton", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, MarginPx);
            rect.sizeDelta = new Vector2(200f, 40f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.25f, 0.55f, 0.3f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OnNextButtonClicked);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = "다음";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 16f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.raycastTarget = false;

            _nextButtonGo = go;
            _nextButtonGo.SetActive(false);
        }

        private void RefreshNextButtonState()
        {
            if (_nextButtonGo == null)
            {
                return;
            }
            bool ready = DraftState.SelectedMissionId != null && DraftState.SelectedDeploymentId != null;
            _nextButtonGo.SetActive(ready);
        }

        /// <summary>누구든 "다음"을 누르면 둘 다 같이 넘어가야 한다(사용자
        /// 지정) — 요청만 보내고, 실제 전환은 그 요청이 되돌아오는 방송
        /// (BoardNetworkSync.ProceedToTerrainRpc → ProceedToTerrain)을 거쳐서
        /// 누른 쪽 자신을 포함한 양쪽 모두에서 동시에 일어난다.</summary>
        private void OnNextButtonClicked()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError("[CardDraftController] BoardNetworkSync.Instance가 없음 — 다음 단계 요청을 못 보냄");
                    return;
                }
                BoardNetworkSync.Instance.RequestProceedToTerrainServerRpc();
                return;
            }
            ProceedToTerrainLocal();
        }

        /// <summary>BoardNetworkSync.ProceedToTerrainRpc가 방송을 받았을 때
        /// (누른 쪽 자신도 포함) 호출한다.</summary>
        public void ProceedToTerrain()
        {
            ProceedToTerrainLocal();
        }

        private void ProceedToTerrainLocal()
        {
            var missionCard = DraftState.FindCard(DraftState.SelectedMissionId);
            var deploymentCard = DraftState.FindCard(DraftState.SelectedDeploymentId);
            if (missionCard == null || deploymentCard == null)
            {
                return;
            }

            MapData.Clear();
            MapData.HasData = true;
            MapData.MapPreset = deploymentCard.MapPreset;
            if (deploymentCard.Zones != null)
            {
                MapData.DeploymentZones.AddRange(deploymentCard.Zones);
            }
            if (deploymentCard.Objectives != null)
            {
                MapData.MissionObjectives.AddRange(deploymentCard.Objectives);
            }

            MissionSettingsData.Clear();
            MissionSettingsData.HasData = true;
            MissionSettingsData.MissionName = missionCard.DisplayName;
            MissionSettingsData.MissionParameters = missionCard.MissionParameters;
            MissionSettingsData.ScoringConditions = missionCard.ScoringConditions;
            MissionSettingsData.AdditionalConditions = missionCard.AdditionalConditions;
            MissionSettingsData.BaseSupply = missionCard.BaseSupply;
            MissionSettingsData.SupplyPerRound = missionCard.SupplyPerRound;
            MissionSettingsData.RoundLength = missionCard.RoundLength;
            MissionSettingsData.EngagementScale = missionCard.EngagementScale;

            SceneManager.LoadScene(GameConstants.TerrainSetupSceneName);
        }

        // ── 공용 위젯 ────────────────────────────────────────────────

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        /// <summary>SelectionController.DrawMapThumbnail과 완전히 같은
        /// 코드(이 프로젝트가 비슷한 썸네일 그리기 코드를 화면마다 중복시켜
        /// 온 것과 같은 방식).</summary>
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
                // 1/3번은 팀 A, 2/4번은 팀 B, 5번(중앙)은 중립 색(사용자 지정
                // — GameConstants.GetMissionObjectiveBaseColor가 이미 게임
                // 화면 등에서 쓰는 것과 같은 규칙).
                img.color = GameConstants.GetMissionObjectiveBaseColor(o.Number);
                img.raycastTarget = false;
            }
        }

        /// <summary>카드 배경 하나에 붙어 좌/우클릭을 구분해 알려준다 —
        /// Unity의 Button은 좌클릭(onClick)만 다루므로, 우클릭(밴)까지
        /// 받으려면 직접 구현해야 한다(TerrainPiece.OnPointerDown과 같은
        /// 패턴).</summary>
        private class CardClickHandler : MonoBehaviour, IPointerDownHandler
        {
            public Action LeftClicked;
            public Action RightClicked;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    LeftClicked?.Invoke();
                }
                else if (eventData.button == PointerEventData.InputButton.Right)
                {
                    RightClicked?.Invoke();
                }
            }
        }
    }
}
