using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 재사용 가능한 예/아니오 확인 다이얼로그 — 텍스트 입력 없이 메시지 한
    /// 줄(줄바꿈 허용)과 "확인"/"취소" 버튼만 있다. InputDialog.cs의 배경/
    /// 패널/버튼 짓는 방식을 그대로 따르되, 입력칸만 뺐다. 패널 바깥을
    /// 클릭하면 취소로 처리된다(사용자 입장에서 "실수로 눌렀다"에 안전한
    /// 기본값).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ConfirmDialog : MonoBehaviour, IPointerDownHandler
    {
        public event Action Confirmed;
        public event Action Cancelled;

        private TextMeshProUGUI _messageLabel;

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
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(360f, 160f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            panelGo.AddComponent<PanelBlocker>();

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 16);
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _messageLabel = CreateMessageLabel(panelGo.transform);

            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panelGo.transform, false);
            var buttonRowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            buttonRowLayout.spacing = 8f;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childForceExpandWidth = true;
            buttonRowLayout.childControlHeight = true;
            var buttonRowLe = buttonRow.AddComponent<LayoutElement>();
            buttonRowLe.preferredHeight = 36f;

            CreateButton(buttonRow.transform, "확인", OnConfirmPressed);
            CreateButton(buttonRow.transform, "취소", OnCancelPressed);

            gameObject.SetActive(false);
        }

        public void Open(string message)
        {
            _messageLabel.text = message;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void OnConfirmPressed()
        {
            Close();
            Confirmed?.Invoke();
        }

        private void OnCancelPressed()
        {
            Close();
            Cancelled?.Invoke();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            OnCancelPressed();
        }

        private static TextMeshProUGUI CreateMessageLabel(Transform parent)
        {
            var go = new GameObject("Message", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 90f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.fontSize = 16f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        private static void CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
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
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 16f;
            labelText.color = Color.white;
            labelText.raycastTarget = false;
        }

        /// <summary>패널 위 클릭이 배경의 OnPointerDown(취소)으로 새어나가지 않게
        /// 막기만 하는 투명 핸들러 — InputDialog.cs와 같은 패턴.</summary>
        private class PanelBlocker : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData eventData)
            {
                eventData.Use();
            }
        }
    }
}
