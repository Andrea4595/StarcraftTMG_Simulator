using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 빈 땅 우클릭으로 여는 이모트 선택 팝업(2026-09-02 신설, 같은 날 사용자
    /// 요청으로 그리드에서 RadialMenu.cs와 완전히 같은 "진짜 다이얼" 모양으로
    /// 재구성) — 원형 배경(Resources/UI/DialBackground.png) 위에 스프라이트
    /// 개수만큼 구분선(Resources/UI/DialLine.png)을 방사형으로 두고, 각
    /// 조각의 가운데에 이모지 하나씩을 얹는다. 클릭 판정도 RadialMenu와
    /// 동일하게 원 전체를 덮는 DialClickArea 하나가 각도로 어느 조각인지
    /// 계산한다 — 라벨 대신 텍스트메시프로 기본 예제 스프라이트 애셋
    /// EmojiOne(표정 이모지 16종, Resources/Sprite Assets/EmojiOne)의
    /// "&lt;sprite index=N&gt;" 태그로 이모지 하나만 그린다는 것만 다르다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class EmotePickerPanel : MonoBehaviour, IPointerDownHandler
    {
        public event Action<int> EmoteChosen;

        private const float IconRadius = 92f;   // 이모지 중심까지 거리(RadialMenu의 TextRadius와 같은 자리).
        private const float OuterRadius = 128f; // 클릭 판정 반경 + 배경 이미지 반경.
        private const float LineLength = 128f;
        private const float LineThickness = 3f;
        private const float LineAlpha = 0.2f;
        private const float IconSize = 32f;

        private RectTransform _dialRoot;
        private TMP_SpriteAsset _spriteAsset;
        private int _spriteCount;

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 화면 전체를 덮는 투명 배경 — 바깥(원 밖) 클릭 감지용(RadialMenu와 동일).
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);

            _spriteAsset = Resources.Load<TMP_SpriteAsset>("Sprite Assets/EmojiOne");
            _spriteCount = _spriteAsset != null ? _spriteAsset.spriteCharacterTable.Count : 0;

            var rootGo = new GameObject("DialRoot", typeof(RectTransform));
            rootGo.transform.SetParent(transform, false);
            _dialRoot = (RectTransform)rootGo.transform;
            _dialRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _dialRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _dialRoot.pivot = new Vector2(0.5f, 0.5f);
            _dialRoot.sizeDelta = Vector2.zero;

            gameObject.SetActive(false);
        }

        public void Open(Vector2 screenPos)
        {
            for (int i = _dialRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_dialRoot.GetChild(i).gameObject);
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screenPos, null, out var local))
            {
                _dialRoot.anchoredPosition = local;
            }

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

            const float startAngle = Mathf.PI / 2f;
            float segment = _spriteCount > 1 ? Mathf.PI * 2f / _spriteCount : 0f;

            if (_spriteCount > 1)
            {
                var lineTexture = Resources.Load<Texture2D>("UI/DialLine");
                for (int i = 0; i < _spriteCount; i++)
                {
                    float boundaryAngle = startAngle - segment / 2f + segment * i;
                    CreateLine(lineTexture, boundaryAngle);
                }
            }

            for (int i = 0; i < _spriteCount; i++)
            {
                float angle = startAngle + segment * i;
                Vector2 offset = _spriteCount > 1
                        ? new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * IconRadius
                        : Vector2.zero;
                CreateIcon(i, offset);
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
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
            img.color = new Color(1f, 1f, 1f, LineAlpha);
            img.raycastTarget = false; // 클릭 판정은 DialClickArea 하나가 전담.
        }

        private void CreateIcon(int spriteIndex, Vector2 offset)
        {
            var go = new GameObject($"Emote_{spriteIndex}", typeof(RectTransform));
            go.transform.SetParent(_dialRoot, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(IconSize, IconSize);
            rect.anchoredPosition = offset;

            var label = go.AddComponent<TextMeshProUGUI>();
            label.spriteAsset = _spriteAsset;
            label.text = $"<sprite index={spriteIndex}>";
            label.fontSize = IconSize * 0.9f;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; // 클릭 판정은 DialClickArea 하나가 전담.
        }

        /// <summary>DialClickArea가 넘겨준 "다이얼 중심으로부터의 로컬 오프셋"으로
        /// 각도/거리를 계산해서 어느 조각인지 찾는다 — RadialMenu.HandleDialClick과
        /// 완전히 같은 계산, 대상만 문자열 액션 대신 스프라이트 인덱스.</summary>
        private void HandleDialClick(Vector2 localOffset)
        {
            if (_spriteCount == 0)
            {
                Close();
                return;
            }
            if (localOffset.magnitude > OuterRadius)
            {
                Close();
                return;
            }
            if (_spriteCount == 1)
            {
                Choose(0);
                return;
            }

            const float startAngle = Mathf.PI / 2f;
            float segment = Mathf.PI * 2f / _spriteCount;
            float clickAngle = Mathf.Atan2(localOffset.y, localOffset.x);
            float rel = Mathf.Repeat(clickAngle - startAngle + segment / 2f, Mathf.PI * 2f);
            int index = Mathf.Clamp(Mathf.FloorToInt(rel / segment), 0, _spriteCount - 1);
            Choose(index);
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void Choose(int spriteIndex)
        {
            // close()를 emit보다 먼저(RadialMenu와 같은 이유).
            Close();
            EmoteChosen?.Invoke(spriteIndex);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Close();
        }

        /// <summary>다이얼 배경 하나에만 붙는다 — RadialMenu.DialClickArea와
        /// 완전히 동일.</summary>
        private class DialClickArea : MonoBehaviour, IPointerDownHandler
        {
            public Action<Vector2> OnLocalClick;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        (RectTransform)transform, eventData.position, null, out var local))
                {
                    OnLocalClick?.Invoke(local);
                }
            }
        }
    }
}
