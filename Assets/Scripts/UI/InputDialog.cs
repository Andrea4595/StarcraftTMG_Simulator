using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 재사용 가능한 한 줄 텍스트 입력 다이얼로그. Godot판에서는 데미지/이름
    /// 변경/메모/범위 입력이 전부 거의 같은 모양의 별도 스크립트였는데,
    /// 여기서는 하나로 합치고 호출부가 제목/초기값/파싱 방식을 정한다.
    /// 값을 읽기 전 IME 문제를 걱정할 필요는 없다(TMP_InputField로 실측
    /// 확인 완료 — 조합 중 텍스트가 정상적으로 누적된다).
    /// 패널 바깥을 클릭하면 취소된다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class InputDialog : MonoBehaviour, IPointerDownHandler
    {
        public event Action<string> Confirmed;
        public event Action Cancelled;

        private TextMeshProUGUI _titleLabel;
        private TMP_InputField _valueEdit;

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
            panelRect.sizeDelta = new Vector2(320f, 160f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            // 패널 클릭은 바깥 클릭(취소)으로 안 새어나가게 별도로 막는다.
            panelGo.AddComponent<PanelBlocker>();

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _titleLabel = CreateLabel(panelGo.transform, "", 18f);

            _valueEdit = CreateInputField(panelGo.transform);

            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panelGo.transform, false);
            var buttonRowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            buttonRowLayout.spacing = 8f;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childForceExpandWidth = true;
            buttonRowLayout.childControlHeight = true;

            CreateButton(buttonRow.transform, "확인", OnConfirmPressed);
            CreateButton(buttonRow.transform, "취소", OnCancelPressed);

            _valueEdit.onSubmit.AddListener(_ => OnConfirmPressed());

            gameObject.SetActive(false);
        }

        public void Open(string title, string initialValue)
        {
            _titleLabel.text = title;
            _valueEdit.text = initialValue ?? "";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _valueEdit.Select();
            _valueEdit.ActivateInputField();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void OnConfirmPressed()
        {
            string value = _valueEdit.text;
            Close();
            Confirmed?.Invoke(value);
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

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 6f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            return label;
        }

        private static TMP_InputField CreateInputField(Transform parent)
        {
            var go = new GameObject("InputField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 40f;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            var inputField = go.AddComponent<TMP_InputField>();

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(go.transform, false);
            var textAreaRect = (RectTransform)textAreaGo.transform;
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(8f, 4f);
            textAreaRect.offsetMax = new Vector2(-8f, -4f);
            textAreaGo.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 20f;
            text.color = Color.white;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            return inputField;
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
        /// 막기만 하는 투명 핸들러 — 패널 자체는 별다른 동작이 없다.</summary>
        private class PanelBlocker : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData eventData)
            {
                eventData.Use();
            }
        }
    }
}
