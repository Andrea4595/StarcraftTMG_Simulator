using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>맨 처음 뜨는 화면(2026-08-30 재구성, 2026-08-31에 "이어하기"
    /// 추가, 2026-09-01에 "게임 시작" 큰 버튼 하나를 "혼자 하기"/"같이 하기"
    /// 두 개로 재구성, 2026-09-04에 "이어하기"를 두 큰 버튼에도 통합) —
    /// 화면 중앙에 같은 크기 정사각형 버튼 두 개가 나란히: "혼자 하기"와
    /// "같이 하기". 둘 다 이제 바로 흐름을 시작하지 않고 먼저 "새 게임"/
    /// "이어하기" 선택 창(_choiceDialogGo)을 띄운다(사용자 요청 — "이어하기"
    /// 를 별도 구석 버튼이 아니라 이 두 버튼을 통해서도 갈 수 있게). "새
    /// 게임"을 고르면 기존 StartGamePicked/MultiplayerPicked을 그대로
    /// 올리고, "이어하기"를 고르면 각각 LoadGamePicked(혼자 하기 쪽, 기존
    /// 구석 버튼과 동일)/LoadGameForMultiplayerPicked(같이 하기 쪽, 신규 —
    /// 저장 파일을 고른 뒤 GameBoard에 그 상태로 들어가자마자
    /// MultiplayerConnectDialog를 자동으로 열어준다, GameFlowBootstrap 참고)
    /// 을 올린다. 화면 오른쪽 아래 구석의 작은 "이어하기"/"배치 프리셋
    /// 제작"/"미션 프리셋 제작" 버튼 세 개는 그대로 남겨뒀다(사용자 지정 —
    /// 기존 지름길을 없앨 필요는 없다는 판단, "이어하기"는 여전히 LoadGame
    /// 화면으로, 나머지 둘은 각각 MapAuthoring/MissionAuthoring 화면으로).
    /// 실제 씬 전환은 이 컴포넌트를 만든 쪽(GameFlowBootstrap)이 담당한다 —
    /// 이 컴포넌트는 클릭 이벤트만 올린다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class EntryController : MonoBehaviour
    {
        public event Action StartGamePicked;
        public event Action MultiplayerPicked;
        public event Action LoadGamePicked;
        public event Action LoadGameForMultiplayerPicked;
        public event Action MapAuthoringPicked;
        public event Action MissionAuthoringPicked;

        private const float PrimaryButtonSize = 260f;
        private const float PrimaryButtonGap = 24f;
        private const float CornerButtonWidth = 200f;
        private const float CornerButtonHeight = 44f;
        private const float CornerMarginPx = 24f;

        private GameObject _choiceDialogGo;
        private TextMeshProUGUI _choiceTitleLabel;
        private Action _choiceNewGameAction;
        private Action _choiceContinueAction;

        private void Awake()
        {
            var root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 1f);

            var title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(root, false);
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, PrimaryButtonSize / 2f + 50f);
            titleRect.sizeDelta = new Vector2(600f, 60f);
            var titleLabel = title.AddComponent<TextMeshProUGUI>();
            titleLabel.text = "스타 TMG 시뮬레이터";
            titleLabel.fontSize = 24f;
            titleLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.raycastTarget = false;

            // "혼자 하기"/"같이 하기" 같은 크기 정사각형 두 개를 화면 중앙에
            // 나란히(사용자 지정, 2026-09-01 재구성 — 예전엔 "게임 시작" 큰
            // 버튼 하나뿐이었다). 가운데 간격을 기준으로 좌우 대칭 배치.
            float halfOffset = PrimaryButtonSize / 2f + PrimaryButtonGap / 2f;
            CreateBigButton(root, "혼자 하기", new Color(0.2f, 0.45f, 0.3f, 1f), "UI/Singleplay", new Vector2(-halfOffset, 0f),
                    () => OpenChoiceDialog("혼자 하기", () => StartGamePicked?.Invoke(), () => LoadGamePicked?.Invoke()));
            CreateBigButton(root, "같이 하기", new Color(0.2f, 0.35f, 0.5f, 1f), "UI/Multiplay", new Vector2(halfOffset, 0f),
                    () => OpenChoiceDialog("같이 하기", () => MultiplayerPicked?.Invoke(), () => LoadGameForMultiplayerPicked?.Invoke()));

            // 오른쪽 아래 구석의 작은 "제작" 도구 진입점 두 개(사용자 지정 —
            // 메인 흐름과 시각적으로 분리되도록 눈에 덜 띄는 자리/크기).
            var cornerRow = new GameObject("CornerRow", typeof(RectTransform));
            cornerRow.transform.SetParent(root, false);
            var cornerRect = (RectTransform)cornerRow.transform;
            cornerRect.anchorMin = new Vector2(1f, 0f);
            cornerRect.anchorMax = new Vector2(1f, 0f);
            cornerRect.pivot = new Vector2(1f, 0f);
            cornerRect.anchoredPosition = new Vector2(-CornerMarginPx, CornerMarginPx);

            var cornerLayout = cornerRow.AddComponent<HorizontalLayoutGroup>();
            cornerLayout.spacing = 10f;
            cornerLayout.childAlignment = TextAnchor.MiddleRight;
            cornerLayout.childControlWidth = false;
            cornerLayout.childForceExpandWidth = false;
            cornerLayout.childControlHeight = false;
            cornerLayout.childForceExpandHeight = false;
            var cornerFitter = cornerRow.AddComponent<ContentSizeFitter>();
            cornerFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            cornerFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateCornerButton(cornerRect, "이어하기", () => LoadGamePicked?.Invoke());
            CreateCornerButton(cornerRect, "배치 프리셋 제작", () => MapAuthoringPicked?.Invoke());
            CreateCornerButton(cornerRect, "미션 프리셋 제작", () => MissionAuthoringPicked?.Invoke());

            BuildChoiceDialog(root);
        }

        /// <summary>"혼자 하기"/"같이 하기"를 누르면 먼저 뜨는 "새 게임"/
        /// "이어하기" 선택 창(사용자 요청, 2026-09-04) — ConfirmDialog.cs와
        /// 같은 모양(배경 딤 + 패널 + 바깥 클릭 시 아무 것도 안 하고 닫힘)
        /// 이지만 버튼 라벨이 "확인"/"취소"가 아니라 "새 게임"/"이어하기"라
        /// 그 컴포넌트를 그대로 재사용하지 않고 이 화면 전용으로 새로
        /// 짓는다(이 프로젝트의 화면별 중복 관례).</summary>
        private void BuildChoiceDialog(Transform root)
        {
            _choiceDialogGo = new GameObject("ChoiceDialog", typeof(RectTransform));
            _choiceDialogGo.transform.SetParent(root, false);
            var rect = (RectTransform)_choiceDialogGo.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var bg = _choiceDialogGo.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.35f);
            var bgHandler = _choiceDialogGo.AddComponent<ChoiceDialogBackgroundHandler>();
            bgHandler.Closed = CloseChoiceDialog;

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_choiceDialogGo.transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(320f, 160f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            panelGo.AddComponent<ChoiceDialogPanelBlocker>();

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 16);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(panelGo.transform, false);
            titleGo.AddComponent<LayoutElement>().preferredHeight = 24f;
            _choiceTitleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            _choiceTitleLabel.fontSize = 16f;
            _choiceTitleLabel.color = Color.white;
            _choiceTitleLabel.alignment = TextAlignmentOptions.Center;
            _choiceTitleLabel.raycastTarget = false;

            CreateChoiceButton(panelGo.transform, "새 게임", () => _choiceNewGameAction?.Invoke());
            CreateChoiceButton(panelGo.transform, "이어하기", () => _choiceContinueAction?.Invoke());

            _choiceDialogGo.SetActive(false);
        }

        private void OpenChoiceDialog(string title, Action onNewGame, Action onContinue)
        {
            _choiceTitleLabel.text = title;
            _choiceNewGameAction = () => { CloseChoiceDialog(); onNewGame(); };
            _choiceContinueAction = () => { CloseChoiceDialog(); onContinue(); };
            _choiceDialogGo.SetActive(true);
            _choiceDialogGo.transform.SetAsLastSibling();
        }

        private void CloseChoiceDialog()
        {
            _choiceDialogGo.SetActive(false);
        }

        private static void CreateChoiceButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 40f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 16f;
            labelText.color = Color.white;
            labelText.raycastTarget = false;
        }

        /// <summary>배경 클릭 시 그냥 닫기만 한다(ConfirmDialog의 "취소"와
        /// 달리 이 창엔 "취소"라는 제3의 선택지가 없다 — 새 게임도 이어하기도
        /// 아닌 것을 골랐다는 뜻이므로 아무 이벤트도 안 올린다).</summary>
        private sealed class ChoiceDialogBackgroundHandler : MonoBehaviour, IPointerDownHandler
        {
            public Action Closed;
            public void OnPointerDown(PointerEventData eventData)
            {
                Closed?.Invoke();
            }
        }

        /// <summary>패널 위 클릭이 배경의 OnPointerDown(닫기)으로 새어나가지
        /// 않게 막기만 하는 투명 핸들러 — ConfirmDialog.cs와 같은 패턴.</summary>
        private sealed class ChoiceDialogPanelBlocker : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData eventData)
            {
                eventData.Use();
            }
        }

        /// <summary>정사각형 버튼 위쪽엔 아이콘(Singleplay.png/Multiplay.png,
        /// 사용자 요청 2026-09-02), 아래쪽 고정 띠엔 글자 라벨 — 색 배경은
        /// 버튼 전체(테두리+캡션 띠)에 그대로 깔린다.</summary>
        private static void CreateBigButton(Transform parent, string label, Color color, string iconResourcePath, Vector2 anchoredPosition, UnityEngine.Events.UnityAction onClick)
        {
            const float IconMargin = 20f;
            const float LabelHeight = 56f;
            const float IconLabelGap = 10f; // 아이콘과 아래 글자 사이 공백(사용자 요청).
            const float IconScale = 0.8f; // 아이콘 자체 크기를 80%로 축소(사용자 요청).

            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(PrimaryButtonSize, PrimaryButtonSize);

            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(IconMargin, LabelHeight + IconLabelGap);
            iconRect.offsetMax = new Vector2(-IconMargin, -IconMargin);
            var iconImg = iconGo.AddComponent<RawImage>();
            iconImg.texture = Resources.Load<Texture2D>(iconResourcePath);
            iconImg.raycastTarget = false;
            var iconFitter = iconGo.AddComponent<AspectRatioFitter>();
            iconFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            iconFitter.aspectRatio = 1f;
            // FitInParent가 정한 크기 그대로에서 한 번 더 축소한다 — pivot이
            // 기본값(0.5,0.5)이라 중심을 유지한 채 줄어든다.
            iconGo.transform.localScale = Vector3.one * IconScale;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.sizeDelta = new Vector2(0f, LabelHeight);
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 28f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static void CreateCornerButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(CornerButtonWidth, CornerButtonHeight);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
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
            text.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            text.raycastTarget = false;
        }
    }
}
