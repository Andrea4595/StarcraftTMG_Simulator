using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>맨 처음 뜨는 화면 — 화면 중앙에 "맵"/"미션" 큰 버튼 두 개가
    /// 좌우로 있고, 어느 쪽을 먼저 고르느냐로 이후 셋업 순서가 정해진다
    /// (사용자 요청). 실제 씬 전환/순서 관리(GameFlowState)는 이 컴포넌트를
    /// 만든 쪽(GameFlowBootstrap)이 담당한다 — 이 컴포넌트는 클릭 이벤트만
    /// 올린다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class EntryController : MonoBehaviour
    {
        public event Action MapPicked;
        public event Action MissionPicked;

        private const float ButtonSize = 260f;

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
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -60f);
            titleRect.sizeDelta = new Vector2(600f, 60f);
            var titleLabel = title.AddComponent<TextMeshProUGUI>();
            titleLabel.text = "무엇부터 설정할까요?";
            titleLabel.fontSize = 24f;
            titleLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.raycastTarget = false;

            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(root, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0.5f, 0.5f);
            rowRect.anchorMax = new Vector2(0.5f, 0.5f);
            rowRect.pivot = new Vector2(0.5f, 0.5f);
            rowRect.anchoredPosition = Vector2.zero;

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 40f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var fitter = row.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateBigButton(rowRect, "맵", new Color(0.2f, 0.35f, 0.55f, 1f), () => MapPicked?.Invoke());
            CreateBigButton(rowRect, "미션", new Color(0.55f, 0.3f, 0.2f, 1f), () => MissionPicked?.Invoke());
        }

        private static void CreateBigButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

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
            text.fontSize = 36f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.raycastTarget = false;
        }
    }
}
