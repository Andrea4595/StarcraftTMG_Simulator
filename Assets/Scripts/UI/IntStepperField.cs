using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>"라벨: [숫자 표시칸][위/아래 아이콘 스테퍼]" 한 묶음 — 직접
    /// 타이핑은 막고(readOnly), 스테퍼 버튼 클릭 또는 칸 위 휠 스크롤로만
    /// 1씩 증감한다. 값은 항상 [minValue, maxValue]로 클램프된다. 원래
    /// ScoreboardPanel 안에만 있던 위젯이었는데, MissionSetupController의
    /// 미션 설정 값(기본 서플라이/라운드당 서플라이/라운드 길이)도 똑같은
    /// 위젯이 필요해져서 공유 유틸리티로 뽑아냈다. 화살표 버튼 크기는
    /// `stepperButtonHeight`(선택 인자, 기본 13f) 하나만 정하면 되고, 너비는
    /// Resources/UI/SpinnerUp·Down.png의 실제 텍스처 가로세로 비율을 읽어서
    /// 자동으로 계산한다(원본이 정사각형이 아니라 세로로 긴 96x259라서, 억지로
    /// 정사각형 박스에 욱여넣어 찌그러뜨리지 않으려고 — 사용자 요청).</summary>
    public static class IntStepperField
    {
        /// <param name="labelLeftControlRight">true면 라벨은 왼쪽에 붙이고 입력칸+
        /// 스테퍼는 이 묶음의 오른쪽 끝에 붙인다(그 사이는 빈 공간) — 흔한
        /// "설정 화면" 행 관례. false(기본값, 기존 호출부 — ScoreboardPanel의
        /// 좁은 스코어보드 행 — 는 그대로 라벨 바로 옆에 입력칸이 붙는
        /// 기존 모양을 유지)면 라벨 바로 옆에 입력칸이 붙는다.</param>
        public static void Create(Transform parent, string labelText, float labelWidth, int initial, int minValue, int maxValue, Action<int> onChanged, float stepperButtonHeight = 13f, bool labelLeftControlRight = false)
        {
            var rowGo = new GameObject($"Stat_{labelText}", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            // flexibleWidth=1을 줘서, 이 묶음을 여러 개 한 줄에 나란히 놓고
            // childForceExpandWidth=true인 부모 아래 두면 남는 폭을 똑같이
            // 나눠 받아 줄 전체를 채울 수 있게 한다(MissionSetupController의
            // BuildStatRow가 이 용도로 씀) — force-expand가 꺼진 부모(예:
            // ScoreboardPanel)에서는 이 값이 그냥 무시되므로 기존 쓰임에는
            // 영향이 없다.
            rowGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            CreateLabel(rowGo.transform, labelText, labelWidth);

            if (labelLeftControlRight)
            {
                var spacerGo = new GameObject("Spacer", typeof(RectTransform));
                spacerGo.transform.SetParent(rowGo.transform, false);
                spacerGo.AddComponent<LayoutElement>().flexibleWidth = 1f;
            }

            int currentValue = Mathf.Clamp(initial, minValue, maxValue);

            var fieldGo = new GameObject("Input", typeof(RectTransform));
            fieldGo.transform.SetParent(rowGo.transform, false);
            var le = fieldGo.AddComponent<LayoutElement>();
            le.preferredWidth = 44f;
            le.preferredHeight = 30f;
            var bg = fieldGo.AddComponent<Image>();
            bg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var inputField = fieldGo.AddComponent<TMP_InputField>();
            inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputField.readOnly = true; // 직접 타이핑 금지 — 스테퍼/휠로만 값을 바꾼다.

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(fieldGo.transform, false);
            var textAreaRect = (RectTransform)textAreaGo.transform;
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(6f, 2f);
            textAreaRect.offsetMax = new Vector2(-6f, -2f);
            textAreaGo.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 16f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.text = currentValue.ToString();

            void ApplyValue(int newValue)
            {
                currentValue = Mathf.Clamp(newValue, minValue, maxValue);
                inputField.SetTextWithoutNotify(currentValue.ToString());
                onChanged(currentValue);
            }

            var scrollHandler = fieldGo.AddComponent<ScrollStepHandler>();
            scrollHandler.OnStep = delta => ApplyValue(currentValue + delta);

            // 실제 스피너 아이콘(정사각형이 아니라 세로로 긴 96x259 비율)의
            // 가로세로 비율을 텍스처에서 직접 읽어서, 그 비율 그대로 버튼
            // 너비를 계산한다 — 하드코딩된 비율 숫자 없이, 나중에 아이콘이
            // 또 바뀌어도 자동으로 맞는 비율을 낸다.
            var upIcon = Resources.Load<Texture2D>("UI/SpinnerUp");
            var downIcon = Resources.Load<Texture2D>("UI/SpinnerDown");
            float iconAspect = (upIcon != null && upIcon.height > 0) ? (float)upIcon.width / upIcon.height : 1f;
            float buttonWidth = stepperButtonHeight * iconAspect;

            var stepperGo = new GameObject("Stepper", typeof(RectTransform));
            stepperGo.transform.SetParent(rowGo.transform, false);
            var stepperLe = stepperGo.AddComponent<LayoutElement>();
            stepperLe.preferredWidth = buttonWidth;
            stepperLe.preferredHeight = stepperButtonHeight * 2f + 2f; // 버튼 2개 + spacing 2f.
            // flexibleWidth를 0으로 못박아야 한다 — 안 그러면(기본값 -1,
            // "미지정") 바로 아래 VerticalLayoutGroup이 childForceExpandWidth=
            // true라서 "Stepper" 자신을 ILayoutElement로 볼 때 flexibleWidth=1로
            // 스스로 보고해버리고, 이 값이 우선순위에서 새는 바람에 rowLayout이
            // 이 행의 남는 폭을 전부 Stepper 하나에 몰아준다(실제로 이 버그를
            // 겪고 나서 알게 됨).
            stepperLe.flexibleWidth = 0f;
            var stepperLayout = stepperGo.AddComponent<VerticalLayoutGroup>();
            stepperLayout.spacing = 2f;
            stepperLayout.childControlWidth = true;
            stepperLayout.childControlHeight = true;
            stepperLayout.childForceExpandWidth = true;
            stepperLayout.childForceExpandHeight = false;

            CreateStepperButton(stepperGo.transform, upIcon, buttonWidth, stepperButtonHeight, () => ApplyValue(currentValue + 1));
            CreateStepperButton(stepperGo.transform, downIcon, buttonWidth, stepperButtonHeight, () => ApplyValue(currentValue - 1));
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float preferredWidth)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.preferredHeight = 22f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 14f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return label;
        }

        private static void CreateStepperButton(Transform parent, Texture2D icon, float width, float height, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("StepBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
        }

        /// <summary>입력칸 위에서 휠을 굴리면 1씩 증감 — 위로 굴리면 +1, 아래로
        /// 굴리면 -1. TMP_InputField는 IScrollHandler를 구현하지 않으므로
        /// 같은 GameObject에 별도로 붙여도 충돌하지 않는다.</summary>
        private class ScrollStepHandler : MonoBehaviour, IScrollHandler
        {
            public Action<int> OnStep;

            public void OnScroll(PointerEventData eventData)
            {
                if (eventData.scrollDelta.y > 0f)
                {
                    OnStep?.Invoke(1);
                }
                else if (eventData.scrollDelta.y < 0f)
                {
                    OnStep?.Invoke(-1);
                }
            }
        }
    }
}
