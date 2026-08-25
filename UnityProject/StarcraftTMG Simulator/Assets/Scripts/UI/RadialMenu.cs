using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public struct RadialMenuOption
    {
        public string Label;
        public string Action;

        public RadialMenuOption(string label, string action)
        {
            Label = label;
            Action = action;
        }
    }

    /// <summary>
    /// 재사용 가능한 우클릭 다이얼 메뉴. Godot판 scenes/common/RadialMenu.gd 포팅.
    /// Open()으로 액션 목록을 원형으로 배치해서 보여주고, 버튼을 누르면
    /// ActionChosen을 올린 뒤 스스로 닫힌다. 바깥을 클릭해도 닫힌다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RadialMenu : MonoBehaviour, IPointerDownHandler
    {
        public event Action<string> ActionChosen;

        private const float ButtonWidth = 110f;
        private const float ButtonHeight = 36f;
        private const float Radius = 70f;

        private RectTransform _buttonHolder;

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 화면 전체를 덮는 투명 배경 — 바깥 클릭 감지용.
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);

            var holderGo = new GameObject("ButtonHolder", typeof(RectTransform));
            holderGo.transform.SetParent(transform, false);
            _buttonHolder = (RectTransform)holderGo.transform;
            _buttonHolder.anchorMin = Vector2.zero;
            _buttonHolder.anchorMax = Vector2.one;
            _buttonHolder.offsetMin = Vector2.zero;
            _buttonHolder.offsetMax = Vector2.zero;

            gameObject.SetActive(false);
        }

        public void Open(IReadOnlyList<RadialMenuOption> actions, Vector2 screenPos)
        {
            for (int i = _buttonHolder.childCount - 1; i >= 0; i--)
            {
                Destroy(_buttonHolder.GetChild(i).gameObject);
            }

            int count = actions.Count;
            const float startAngle = Mathf.PI / 2f;
            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + (Mathf.PI * 2f / count) * i;
                Vector2 offset = count > 1
                        ? new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Radius
                        : Vector2.zero;
                Vector2 center = screenPos + offset;

                string action = actions[i].Action;
                CreateButton(actions[i].Label, center, () => Choose(action));
            }

            gameObject.SetActive(true);
        }

        private void CreateButton(string label, Vector2 screenPos, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject($"Option_{label}", typeof(RectTransform));
            btnGo.transform.SetParent(_buttonHolder, false);
            var btnRect = (RectTransform)btnGo.transform;
            btnRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            btnRect.anchorMin = new Vector2(0.5f, 0.5f);
            btnRect.anchorMax = new Vector2(0.5f, 0.5f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.position = screenPos;

            var img = btnGo.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(btnGo.transform, false);
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

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void Choose(string action)
        {
            // close()를 emit보다 먼저 — 핸들러가 새 목록으로 다시 열 수도 있는데(예:
            // 범위 삭제 시 하위 목록), emit 이후에 close()를 부르면 방금 다시 연
            // 메뉴를 즉시 닫아버리게 된다.
            Close();
            ActionChosen?.Invoke(action);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Close();
        }
    }
}
