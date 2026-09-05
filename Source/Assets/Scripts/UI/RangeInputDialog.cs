using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// '범위 표시 → 추가'에서 뜨는 입력창 — 인치 단위 거리(소수 가능)와 "상시
    /// 표시" 여부를 받는다. Godot판 RangeInputDialog.gd 포팅. InputDialog와
    /// 거의 같은 모양이지만 체크박스(토글)가 하나 더 있어서 별도 컴포넌트로
    /// 뒀다(Godot판도 별도 씬이었다). 상시 표시 여부는 한 번 정해지면 나중에
    /// 못 바꾼다 — 마음에 안 들면 지우고 새로 추가해야 한다(의도된 동작).
    /// 패널 바깥을 클릭하면 취소된다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RangeInputDialog : MonoBehaviour, IPointerDownHandler
    {
        public event Action<float, bool> Confirmed;
        public event Action Cancelled;

        private TMP_InputField _valueEdit;
        private Toggle _alwaysShowToggle;

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
            panelRect.sizeDelta = new Vector2(280f, 210f);
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

            CreateLabel(panelGo.transform, "범위 입력 (인치)");
            _valueEdit = CreateInputField(panelGo.transform);
            _alwaysShowToggle = CreateToggle(panelGo.transform, "상시 표시");

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

        public void Open(float initialValue)
        {
            _valueEdit.text = initialValue > 0f ? initialValue.ToString("F1", CultureInfo.InvariantCulture) : "";
            _alwaysShowToggle.isOn = true;
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
            bool alwaysShow = _alwaysShowToggle.isOn;
            float.TryParse(_valueEdit.text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            Close();
            if (value > 0f)
            {
                Confirmed?.Invoke(value, alwaysShow);
            }
            else
            {
                Cancelled?.Invoke();
            }
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

        private static void CreateLabel(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 24f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 18f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
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
            inputField.contentType = TMP_InputField.ContentType.DecimalNumber;

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

        private static Toggle CreateToggle(Transform parent, string label)
        {
            var go = new GameObject("Toggle", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
            var toggle = go.AddComponent<Toggle>();

            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(go.transform, false);
            var bgRect = (RectTransform)bgGo.transform;
            bgRect.anchorMin = new Vector2(0f, 0.5f);
            bgRect.anchorMax = new Vector2(0f, 0.5f);
            bgRect.pivot = new Vector2(0f, 0.5f);
            bgRect.sizeDelta = new Vector2(22f, 22f);
            bgRect.anchoredPosition = Vector2.zero;
            var bgImage = bgGo.AddComponent<Image>();
            bgImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

            var checkGo = new GameObject("Checkmark", typeof(RectTransform));
            checkGo.transform.SetParent(bgGo.transform, false);
            var checkRect = (RectTransform)checkGo.transform;
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(4f, 4f);
            checkRect.offsetMax = new Vector2(-4f, -4f);
            var checkImage = checkGo.AddComponent<Image>();
            checkImage.color = new Color(0.4f, 0.85f, 0.4f, 1f);

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.isOn = true;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(30f, 0f);
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 14f;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.raycastTarget = false;

            return toggle;
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
