using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 세로 스크롤 목록(ScrollRect+Viewport+Content) 뼈대를 만드는 공용 헬퍼 —
    /// 예비대/토큰 목록, 로스터 파일 탐색기가 전부 같은 모양을 쓴다. 돌려주는
    /// content RectTransform에 항목(버튼 등)을 채우면 된다.
    ///
    /// content의 VerticalLayoutGroup은 반드시 childControlHeight=true로 둬야
    /// 한다 — false로 두면 레이아웃 그룹이 각 자식의 "다음 형제를 어디에
    /// 배치할지"(커서 이동)는 LayoutElement.preferredHeight 대로 계산하면서도
    /// 정작 그 자식 자신의 RectTransform 실제 높이는 안 바꾸는데, 새로 만든
    /// RectTransform은 기본 높이가 preferredHeight와 다르므로 그 차이만큼
    /// 형제끼리 서로 겹쳐 보인다 — 실제로 예비대 목록/로스터 파일 탐색기에서
    /// 이 증상이 나서 알게 된 문제(이 프로젝트의 반복되는 Unity UI 함정).
    /// </summary>
    public static class ScrollListUtil
    {
        private const float ScrollbarWidth = 14f;

        public static RectTransform Create(Transform parent, float height, Color background, out ScrollRect scrollRect)
        {
            var scrollGo = new GameObject("ScrollList", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scrollRectTransform = (RectTransform)scrollGo.transform;
            var le = scrollGo.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            var bg = scrollGo.AddComponent<Image>();
            bg.color = background;
            scrollGo.AddComponent<RectMask2D>();
            scrollRect = scrollGo.AddComponent<ScrollRect>();

            // Viewport는 스크롤바가 차지할 폭만큼 오른쪽을 비워둔다.
            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollRectTransform, false);
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = new Vector2(-ScrollbarWidth, 0f);
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            // 앵커를 point(기본값)에서 stretch로 바꾸기만 하고 offsetMin/Max를
            // 0으로 정리하지 않으면, 새 RectTransform의 기본 offset이 그대로
            // 남아 content가 뷰포트보다 옆으로 삐져나가고 — 그 삐져나온 부분이
            // Viewport의 RectMask2D에 잘려서 항목 텍스트 왼쪽이 잘려 보인다.
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var contentLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 4f;
            contentLayout.padding = new RectOffset(4, 4, 4, 4);
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 세로 스크롤바 — Unity가 기본 "Scroll View" 프리팹에서 쓰는 것과
            // 같은 구조(배경 위에 SlidingArea, 그 안에 실제로 드래그하는 Handle).
            var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform));
            scrollbarGo.transform.SetParent(scrollRectTransform, false);
            var scrollbarRect = (RectTransform)scrollbarGo.transform;
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.sizeDelta = new Vector2(ScrollbarWidth, 0f);
            scrollbarRect.anchoredPosition = Vector2.zero;
            var scrollbarBg = scrollbarGo.AddComponent<Image>();
            scrollbarBg.color = new Color(0f, 0f, 0f, 0.3f);
            var scrollbar = scrollbarGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var slidingAreaGo = new GameObject("SlidingArea", typeof(RectTransform));
            slidingAreaGo.transform.SetParent(scrollbarRect, false);
            var slidingAreaRect = (RectTransform)slidingAreaGo.transform;
            slidingAreaRect.anchorMin = Vector2.zero;
            slidingAreaRect.anchorMax = Vector2.one;
            slidingAreaRect.offsetMin = new Vector2(2f, 2f);
            slidingAreaRect.offsetMax = new Vector2(-2f, -2f);

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(slidingAreaRect, false);
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.sizeDelta = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.6f, 0.6f, 0.6f, 0.9f);

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImg;

            scrollRect.viewport = viewportRect;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            // 기본값(1)은 마우스 휠 한 칸에 거의 안 움직이는 수준으로 느리다.
            scrollRect.scrollSensitivity = 30f;

            return content;
        }
    }
}
