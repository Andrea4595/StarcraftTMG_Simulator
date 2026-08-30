using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>스코어보드 바로 아래에 붙는 페이즈 표시 바 — Movement Phase >
    /// Assault Phase > Combat Phase > Cleanup Phase 순서를 한 줄로 보여주고,
    /// 현재 페이즈만 밝게 강조한다. 클릭하면 다음 페이즈로 넘어가고(Cleanup
    /// 다음은 다시 Movement로 순환), 값은 MatchState.PhaseIndex에 저장된다
    /// (사용자 요청: 라운드는 이미 있는 스테퍼로 충분하니 페이즈만 추가).</summary>
    [RequireComponent(typeof(RectTransform))]
    public class PhaseBar : MonoBehaviour
    {
        private static readonly Color ActiveColor = Color.white;
        private static readonly Color InactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color SeparatorColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        private readonly List<TextMeshProUGUI> _phaseLabels = new List<TextMeshProUGUI>();

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -GameConstants.ScoreboardHeight);
            rect.sizeDelta = new Vector2(0f, GameConstants.PhaseBarHeight);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);

            var btn = gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(AdvancePhase);

            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(transform, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = Vector2.zero;
            rowRect.anchorMax = Vector2.one;
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = Vector2.zero;

            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var names = MatchState.PhaseNames;
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0)
                {
                    CreateLabel(rowRect, ">", 14f, SeparatorColor, FontStyles.Normal, raycastTarget: false);
                }
                var label = CreateLabel(rowRect, names[i], 15f, InactiveColor, FontStyles.Normal, raycastTarget: false);
                _phaseLabels.Add(label);
            }

            RefreshHighlight();
        }

        private void AdvancePhase()
        {
            MatchState.PhaseIndex = (MatchState.PhaseIndex + 1) % MatchState.PhaseNames.Length;
            RefreshHighlight();
        }

        private void RefreshHighlight()
        {
            for (int i = 0; i < _phaseLabels.Count; i++)
            {
                bool active = i == MatchState.PhaseIndex;
                _phaseLabels[i].color = active ? ActiveColor : InactiveColor;
                _phaseLabels[i].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, Color color, FontStyles style, bool raycastTarget)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 8f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.Midline;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = raycastTarget;
            return label;
        }
    }
}
