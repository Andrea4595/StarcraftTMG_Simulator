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
    /// 재사용 가능한 우클릭 다이얼 메뉴. 원래 Godot판(scenes/common/RadialMenu.gd)도
    /// 실은 원형으로 배치한 네모 버튼일 뿐이었는데(진짜 파이/휠 모양이 아니었다),
    /// 이번에 진짜 다이얼처럼 보이도록 새로 짰다: 가운데 원형 배경
    /// (Resources/UI/DialBackground.png, 부드러운 반투명 비네트) 위에 옵션
    /// 개수만큼 구분선(Resources/UI/DialLine.png, 중심에서 바깥으로 뻗는 스포크 —
    /// 왼쪽 끝이 중심, 오른쪽으로 갈수록 옅어지며 바깥으로 뻗는다)을
    /// 방사형으로 두고, 각 조각의 가운데에 라벨 텍스트만 얹는다. 네모 버튼이
    /// 없으니 클릭 판정도 각 라벨의 작은 사각형이 아니라 원 전체를 덮는
    /// DialClickArea 하나가 클릭 지점의 각도/거리로 어느 조각인지 계산해서
    /// 처리한다 — 조각 어디를 눌러도 선택되는 진짜 파이 메뉴 방식.
    /// Open()으로 액션 목록을 보여주고, 선택하면 ActionChosen을 올린 뒤 스스로
    /// 닫힌다. 바깥(원 밖)을 클릭해도 닫힌다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RadialMenu : MonoBehaviour, IPointerDownHandler
    {
        public event Action<string> ActionChosen;

        private const float TextRadius = 92f;      // 라벨 중심까지 거리
        private const float OuterRadius = 128f;     // 클릭 판정 반경 + 배경 이미지 반경
        private const float LineLength = 128f;      // 구분선 길이(중심→바깥)
        private const float LineThickness = 3f;
        private const float LineAlpha = 0.2f;
        private const float LabelWidth = 108f;

        private RectTransform _dialRoot;
        private IReadOnlyList<RadialMenuOption> _currentOptions;
        private float _startAngle;

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 화면 전체를 덮는 투명 배경 — 바깥(원 밖) 클릭 감지용.
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);

            var rootGo = new GameObject("DialRoot", typeof(RectTransform));
            rootGo.transform.SetParent(transform, false);
            _dialRoot = (RectTransform)rootGo.transform;
            _dialRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _dialRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _dialRoot.pivot = new Vector2(0.5f, 0.5f);
            _dialRoot.sizeDelta = Vector2.zero;

            gameObject.SetActive(false);
        }

        public void Open(IReadOnlyList<RadialMenuOption> actions, Vector2 screenPos)
        {
            for (int i = _dialRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_dialRoot.GetChild(i).gameObject);
            }

            _currentOptions = actions;
            _startAngle = Mathf.PI / 2f;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screenPos, null, out var local))
            {
                _dialRoot.anchoredPosition = local;
            }

            int count = actions.Count;

            var background = new GameObject("DialBackground", typeof(RectTransform)).AddComponent<RawImage>();
            background.transform.SetParent(_dialRoot, false);
            var backgroundRect = (RectTransform)background.transform;
            backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
            backgroundRect.pivot = new Vector2(0.5f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(OuterRadius * 2f, OuterRadius * 2f);
            background.texture = Resources.Load<Texture2D>("UI/DialBackground");
            background.raycastTarget = true;

            var clickArea = background.gameObject.AddComponent<DialClickArea>();
            clickArea.OnLocalClick = HandleDialClick;

            float segment = count > 1 ? Mathf.PI * 2f / count : 0f;

            if (count > 1)
            {
                var lineTexture = Resources.Load<Texture2D>("UI/DialLine");
                for (int i = 0; i < count; i++)
                {
                    float boundaryAngle = _startAngle - segment / 2f + segment * i;
                    CreateLine(lineTexture, boundaryAngle);
                }
            }

            for (int i = 0; i < count; i++)
            {
                float angle = _startAngle + segment * i;
                Vector2 offset = count > 1
                        ? new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * TextRadius
                        : Vector2.zero;
                CreateLabel(actions[i].Label, offset);
            }

            gameObject.SetActive(true);
        }

        private static float segmentAngleFor(int count)
        {
            return Mathf.PI * 2f / count;
        }

        private void CreateLine(Texture2D texture, float angle)
        {
            var lineGo = new GameObject("Divider", typeof(RectTransform));
            lineGo.transform.SetParent(_dialRoot, false);
            var lineRect = (RectTransform)lineGo.transform;
            lineRect.anchorMin = new Vector2(0.5f, 0.5f);
            lineRect.anchorMax = new Vector2(0.5f, 0.5f);
            lineRect.pivot = new Vector2(0f, 0.5f); // 중심 쪽 끝을 축으로 회전 — 스포크처럼 바깥으로 뻗는다.
            lineRect.sizeDelta = new Vector2(LineLength, LineThickness);
            lineRect.anchoredPosition = Vector2.zero;
            lineRect.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);

            var img = lineGo.AddComponent<RawImage>();
            img.texture = texture;
            img.color = new Color(1f, 1f, 1f, LineAlpha); // 원본 텍스처는 거의 불투명 — 구분선답게 알파를 많이 낮춘다.
            img.raycastTarget = false; // 클릭 판정은 DialClickArea 하나가 전담 — 선이 가로채면 안 된다.
        }

        private void CreateLabel(string text, Vector2 offset)
        {
            var labelGo = new GameObject($"Option_{text}", typeof(RectTransform));
            labelGo.transform.SetParent(_dialRoot, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(LabelWidth, 40f);
            labelRect.anchoredPosition = offset;

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 16f;
            label.color = Color.white;
            label.enableWordWrapping = false;
            label.raycastTarget = false; // 클릭 판정은 DialClickArea 하나가 전담.
        }

        /// <summary>DialClickArea가 넘겨준 "다이얼 중심으로부터의 로컬 오프셋"으로
        /// 각도/거리를 계산해서 어느 조각인지 찾는다. 원 밖을 눌렀으면(거리 >
        /// OuterRadius) 바깥 클릭과 동일하게 그냥 닫는다.</summary>
        private void HandleDialClick(Vector2 localOffset)
        {
            if (_currentOptions == null || _currentOptions.Count == 0)
            {
                Close();
                return;
            }
            if (localOffset.magnitude > OuterRadius)
            {
                Close();
                return;
            }

            int count = _currentOptions.Count;
            if (count == 1)
            {
                Choose(_currentOptions[0].Action);
                return;
            }

            float clickAngle = Mathf.Atan2(localOffset.y, localOffset.x);
            float segment = segmentAngleFor(count);
            float rel = Mathf.Repeat(clickAngle - _startAngle + segment / 2f, Mathf.PI * 2f);
            int index = Mathf.Clamp(Mathf.FloorToInt(rel / segment), 0, count - 1);
            Choose(_currentOptions[index].Action);
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

        /// <summary>다이얼 배경 하나에만 붙는다 — 눌린 화면 좌표를 이 RectTransform
        /// 기준 로컬 좌표(=다이얼 중심으로부터의 오프셋, 피벗이 0.5,0.5라서 바로
        /// 그 의미가 된다)로 바꿔서 콜백에 넘긴다.</summary>
        private class DialClickArea : MonoBehaviour, IPointerDownHandler
        {
            public Action<Vector2> OnLocalClick;

            public void OnPointerDown(PointerEventData eventData)
            {
                // 이 프로젝트의 캔버스는 전부 Screen Space Overlay라 카메라 인자는
                // null이어야 한다(BoardManager.TryGetLocalMouse()와 동일 관례).
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        (RectTransform)transform, eventData.position, null, out var local))
                {
                    OnLocalClick?.Invoke(local);
                }
            }
        }
    }
}
