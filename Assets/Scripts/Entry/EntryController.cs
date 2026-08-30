using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>맨 처음 뜨는 화면(2026-08-30 재구성, 2026-08-31에 "이어하기"
    /// 추가) — 화면 중앙에 큰 "게임 시작" 버튼 하나(실제 게임 흐름:
    /// Selection → TerrainSetup → GameBoard로 이어짐), 화면 오른쪽 아래
    /// 구석에 작은 "이어하기"/"배치 프리셋 제작"/"미션 프리셋 제작" 버튼
    /// 세 개가 사용자 지정. "이어하기"는 LoadGame 화면(저장 파일 목록)으로,
    /// 나머지 둘은 각각 MapAuthoring/MissionAuthoring 화면으로 간다(프리셋을
    /// 미리 만들어두는 편집기, 게임 흐름과 무관하고 완료하면 다시 여기로
    /// 돌아온다). 예전엔 "맵"/"미션" 두 큰 버튼이 곧 그 게임의 라이브 셋업
    /// 화면으로 이어졌지만, 이제 셋업 화면 자체가 "제작" 전용으로 바뀌면서
    /// 이 화면의 역할도 같이 바뀌었다. 실제 씬 전환은 이 컴포넌트를 만든
    /// 쪽(GameFlowBootstrap)이 담당한다 — 이 컴포넌트는 클릭 이벤트만
    /// 올린다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class EntryController : MonoBehaviour
    {
        public event Action StartGamePicked;
        public event Action LoadGamePicked;
        public event Action MapAuthoringPicked;
        public event Action MissionAuthoringPicked;

        private const float PrimaryButtonSize = 260f;
        private const float CornerButtonWidth = 200f;
        private const float CornerButtonHeight = 44f;
        private const float CornerMarginPx = 24f;

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

            CreateBigButton(root, "게임 시작", new Color(0.2f, 0.45f, 0.3f, 1f), () => StartGamePicked?.Invoke());

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
        }

        private static void CreateBigButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(PrimaryButtonSize, PrimaryButtonSize);

            var img = go.AddComponent<Image>();
            img.color = color;
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
            text.fontSize = 32f;
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
