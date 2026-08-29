using UnityEngine;

namespace TmgBoard
{
    /// <summary>지도 뷰(mapArea 등, 하나의 RectTransform)를 마우스 커서 위치
    /// 기준으로 확대/축소하는 공용 로직 — 게임 보드(BoardManager.PanZoom.cs)와
    /// 미션 설정 화면(MissionSetupController) 둘 다 이 하나만 쓴다.
    ///
    /// target 자신의 ScreenPointToLocalPointInRectangle만 사용하고(부모
    /// 기준이 아니라) 완전히 자기참조적으로 계산하므로, target의 pivot/anchor가
    /// 무엇이든(중심이든 코너든) 항상 올바르게 동작한다 — 예전엔 각자 "부모
    /// 기준 로컬 좌표 - anchoredPosition" 식으로 따로 구현했었는데, 그건
    /// 부모의 pivot과 target의 anchor가 우연히 일치해야만(게임 보드는 둘 다
    /// 중심이라 우연히 맞았다) 정확한 결과가 나오는 취약한 수식이었다 —
    /// 미션 설정 화면(코너 anchor)에서 실제로 어긋나 줌 중심이 화면
    /// 좌하단으로 쏠리는 버그가 났다.</summary>
    public static class MapZoomUtil
    {
        /// <summary>target을 screenPos(마우스 화면 좌표) 기준으로 factor배
        /// 확대/축소한다. currentZoom(보통 1이 "뷰포트에 꽉 맞춘 기본 배율")을
        /// [minZoom,maxZoom]로 클램프한 새 값으로 돌려주고, target의
        /// anchoredPosition/localScale을 그 자리에서 갱신한다. baseScaleFactor는
        /// "뷰포트에 꽉 맞추는 기준 배율"(줌 1일 때의 실제 localScale) —
        /// 실제 적용 배율은 baseScaleFactor*newZoom이다.</summary>
        public static float ApplyZoomAtScreenPoint(RectTransform target, Vector2 screenPos, float currentZoom,
                float baseScaleFactor, float factor, float minZoom, float maxZoom)
        {
            float newZoom = Mathf.Clamp(currentZoom * factor, minZoom, maxZoom);
            if (Mathf.Approximately(newZoom, currentZoom))
            {
                return currentZoom;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(target, screenPos, null, out var localPoint))
            {
                return currentZoom;
            }

            float oldScale = target.localScale.x;
            float newScale = baseScaleFactor * newZoom;
            // 유도: 화면상 같은 지점(스크린포인트)이 줌 전후 계속 같은 자리에
            // 남으려면, target의 anchoredPosition은 localPoint*(oldScale-newScale)
            // 만큼만 움직이면 된다 — target 자신의 로컬(비스케일) 좌표계에서
            // 구한 localPoint라 이 관계식이 anchor/pivot과 무관하게 항상 성립한다.
            target.anchoredPosition += localPoint * (oldScale - newScale);
            target.localScale = new Vector3(newScale, newScale, 1f);
            return newZoom;
        }
    }
}
