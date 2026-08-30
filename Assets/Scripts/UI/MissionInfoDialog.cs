using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 게임 화면 라운드 표시기 옆 "미션" 버튼을 누르면 뜨는 읽기 전용 모달 —
    /// MissionSettingsData(미션 셋업 화면에서 "완료"를 눌러 넘어온 값)를
    /// 그대로 보여주기만 한다(수정 불가). ConfirmDialog.cs와 같은 "배경 전체를
    /// 덮고 바깥 클릭/자기 버튼으로만 닫히는" 진짜 모달 구조를 그대로 따른다 —
    /// WeaponProfileDialog/DiceRollDialog는 주사위를 굴리며 동시에 참고해야
    /// 해서 일부러 비독점 창으로 바꿨지만, 이건 그런 동시 참고 요구가 없어서
    /// 사용자가 명시적으로 "모달 형태"를 요청했다. 패널 높이는 고정값 대신
    /// ContentSizeFitter로 내용에 맞춰 자동 계산한다(WeaponProfileDialog와
    /// 같은 이유 — 섹션 개수/줄바꿈에 따라 필요한 높이가 달라진다).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MissionInfoDialog : MonoBehaviour, IPointerDownHandler
    {
        private const float PanelWidth = 480f;
        private const float ScrollMaxHeight = 320f;

        private TextMeshProUGUI _titleLabel;
        private TextMeshProUGUI _roundStatsLabel;
        private TextMeshProUGUI _missionParametersLabel;
        private TextMeshProUGUI _scoringConditionsLabel;
        private TextMeshProUGUI _additionalConditionsLabel;
        private TextMeshProUGUI _engagementLabel;
        private RectTransform _scrollContent;
        private RectTransform _panelRect;

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            _panelRect = (RectTransform)panelGo.transform;
            _panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta = new Vector2(PanelWidth, 0f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            panelGo.AddComponent<PanelBlocker>();

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var panelFitter = panelGo.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // 섹션 개수/높이에 따라 세로만 내용에 맞춘다(WeaponProfileDialog와 같은 패턴).

            _titleLabel = CreateLabel(panelGo.transform, "미션 정보", 18f, FontStyles.Bold, Color.white);
            _titleLabel.alignment = TextAlignmentOptions.Center; // 미션 이름 헤더는 중앙 정렬(사용자 지정).
            _titleLabel.GetComponent<LayoutElement>().preferredHeight = 24f;

            _roundStatsLabel = CreateLabel(panelGo.transform, "", 14f, FontStyles.Normal, new Color(0.85f, 0.85f, 0.85f, 1f));
            _roundStatsLabel.GetComponent<LayoutElement>().preferredHeight = 30f; // 좁은 화면에서 2줄로 접혀도 안전하도록 여유있게.

            _scrollContent = ScrollListUtil.Create(panelGo.transform, ScrollMaxHeight, new Color(0f, 0f, 0f, 0.15f),
                    out _, out _);

            _missionParametersLabel = AddSection(_scrollContent, "미션 파라미터", addDividerBefore: false);
            _scoringConditionsLabel = AddSection(_scrollContent, "점수 획득 조건", addDividerBefore: true);
            _additionalConditionsLabel = AddSection(_scrollContent, "추가 조건", addDividerBefore: true);

            // 전투 규모는 모달 맨 아래, 우측 정렬로 따로 표시한다(사용자 지정).
            _engagementLabel = CreateLabel(panelGo.transform, "", 13f, FontStyles.Normal, new Color(0.7f, 0.75f, 0.85f, 1f));
            _engagementLabel.alignment = TextAlignmentOptions.MidlineRight;
            _engagementLabel.GetComponent<LayoutElement>().preferredHeight = 20f;

            var closeGo = new GameObject("Close", typeof(RectTransform));
            closeGo.transform.SetParent(panelGo.transform, false);
            var closeLe = closeGo.AddComponent<LayoutElement>();
            closeLe.preferredHeight = 36f;
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.onClick.AddListener(Close);
            var closeLabel = CreateLabel(closeGo.transform, "닫기", 16f, FontStyles.Normal, Color.white);
            var closeLabelRect = (RectTransform)closeLabel.transform;
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;
            Destroy(closeLabel.GetComponent<LayoutElement>());
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.raycastTarget = false;

            gameObject.SetActive(false);
        }

        /// <summary>본문 항목 하나 — 작은 회색 굵은 제목 줄 + 그 아래 흰색
        /// 본문(자동 줄바꿈). addDividerBefore가 true면 이전 섹션과 확실히
        /// 구분되도록 얇은 구분선을 먼저 그린다(사용자 지정 — "각각 분리되어
        /// 표시"). 값은 나중에 Open()에서 채운다.</summary>
        private static TextMeshProUGUI AddSection(Transform parent, string headerText, bool addDividerBefore)
        {
            if (addDividerBefore)
            {
                var dividerGo = new GameObject("Divider", typeof(RectTransform));
                dividerGo.transform.SetParent(parent, false);
                var dividerLe = dividerGo.AddComponent<LayoutElement>();
                dividerLe.preferredHeight = 1f;
                var dividerImg = dividerGo.AddComponent<Image>();
                dividerImg.color = new Color(1f, 1f, 1f, 0.12f);
            }

            var header = CreateLabel(parent, headerText, 12f, FontStyles.Bold, new Color(0.6f, 0.6f, 0.6f, 1f));
            header.GetComponent<LayoutElement>().preferredHeight = 18f;

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(parent, false);
            var bodyLe = bodyGo.AddComponent<LayoutElement>();
            bodyLe.flexibleWidth = 1f;
            var body = bodyGo.AddComponent<TextMeshProUGUI>();
            body.fontSize = 14f;
            body.color = Color.white;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.raycastTarget = false;
            return body;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, FontStyles style, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>();
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        private static string OrEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? "(내용 없음)" : value;
        }

        /// <summary>MissionSettingsData(현재 확정된 미션)를 그대로 읽어
        /// 채운다 — 게임 화면의 "미션" 버튼이 쓰는 경로.</summary>
        public void Open()
        {
            Open(MissionSettingsData.MissionName, MissionSettingsData.MissionParameters, MissionSettingsData.ScoringConditions,
                    MissionSettingsData.AdditionalConditions, MissionSettingsData.BaseSupply, MissionSettingsData.SupplyPerRound,
                    MissionSettingsData.RoundLength, MissionSettingsData.EngagementScale);
        }

        /// <summary>임의의 미션 데이터를 직접 넣어 연다(2026-08-31 추가) —
        /// CardDraft 화면에서 아직 확정되지 않은(=MissionSettingsData에
        /// 반영되기 전인) 후보 미션 카드의 정보를 미리 보여주는 용도.</summary>
        public void Open(string missionName, string missionParameters, string scoringConditions, string additionalConditions,
                int baseSupply, int supplyPerRound, int roundLength, string engagementScale)
        {
            _titleLabel.text = string.IsNullOrEmpty(missionName) ? "미션 정보" : missionName;
            _roundStatsLabel.text =
                    $"라운드 길이: {roundLength}  -  " +
                    $"기본 서플라이: {baseSupply}  -  " +
                    $"라운드 당 서플라이: {supplyPerRound}";
            _missionParametersLabel.text = OrEmpty(missionParameters);
            _scoringConditionsLabel.text = OrEmpty(scoringConditions);
            _additionalConditionsLabel.text = OrEmpty(additionalConditions);
            _engagementLabel.text = engagementScale;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Close();
        }

        /// <summary>패널 위 클릭이 배경의 OnPointerDown(닫기)으로 새어나가지
        /// 않게 막기만 하는 투명 핸들러 — ConfirmDialog.cs와 같은 패턴.</summary>
        private class PanelBlocker : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData eventData)
            {
                eventData.Use();
            }
        }
    }
}
