using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 스크린샷 ────────────────────────────────────────────────────

        // 캡처 직전 지도를 이 만큼의 위아래 공백을 두고, 상단 바(스코어보드)/
        // 하단 바(마커바) 사이 구간의 중앙에 높이 기준으로 꽉 채운다(36x36"/
        // 54x36" 둘 다 폭과 무관하게 이 규칙 하나로 처리된다 — 폭이
        // 넘치더라도 잘라내지 않고 그대로 좌우로 삐져나가게 둔다).
        private const float ScreenshotHeightMarginPx = 60f;

        // "게임이 시작된 시간" 하나를 세션 내내 재사용하고(사용자 요청 —
        // 찍을 때마다 시간이 바뀌면 안 됨), 뒤의 _NN은 이번 세션에서 몇 번째
        // 스크린샷인지를 나타내는 순번이다.
        private string _screenshotSessionTimestamp;
        private int _screenshotCounter;

        /// <summary>게임 보드가 뜨는 시점(Start())에 한 번 불러서 세션 타임스탬프를
        /// 고정한다.</summary>
        private void InitScreenshotSession()
        {
            _screenshotSessionTimestamp = DateTime.Now.ToString("yyMMddHHmmss");
            _screenshotCounter = 0;
        }

        private void TakeScreenshot()
        {
            StartCoroutine(CaptureScreenshotRoutine());
        }

        private IEnumerator CaptureScreenshotRoutine()
        {
            if (mapArea == null)
            {
                yield break;
            }

            // 1. 현재 팬/줌 상태를 저장해 두고, 지도를 상단/하단 바 사이 구간의
            // 중앙에 높이 기준으로 꽉 채운 스크린샷 전용 뷰로 바꾼다.
            // _zoomLevel(평소의 확대/축소
            // 배율 필드)은 건드리지 않는다 — 스크린샷 뷰는 localScale을 직접
            // 정하고 끝에 그 값을 그대로 복원하므로 _zoomLevel과는 무관하다.
            Vector3 savedScale = mapArea.localScale;
            Vector2 savedAnchoredPos = mapArea.anchoredPosition;

            ApplyScreenshotFitView();

            // 2. 위 변경이 실제로 한 프레임 렌더링된 뒤에 캡처해야 한다 —
            // 같은 프레임 안에서 바로 되돌리면(3번) 렌더링 전에 원상복구되어
            // 캡처에 반영이 안 될 수 있다.
            yield return new WaitForEndOfFrame();

            string path = BuildScreenshotPath();
            ScreenCapture.CaptureScreenshot(path);

            // 3. CaptureScreenshot()은 이 프레임 끝에서 프레임버퍼를 읽어가는
            // 방식이라, 한 프레임 더 기다린 뒤에 복원해야 캡처된 화면이
            // 스크린샷 전용 뷰 그대로 남는다.
            yield return new WaitForEndOfFrame();

            mapArea.localScale = savedScale;
            mapArea.anchoredPosition = savedAnchoredPos;
        }

        /// <summary>지도 폭이 화면을 넘치든 말든 상관없이, 지도 높이가 상단
        /// 바(스코어보드)와 하단 바(마커바) 사이 구간에 ScreenshotHeightMarginPx
        /// 만큼 여백을 두고 그 구간 중앙을 채우도록 mapArea의 스케일/위치를
        /// 맞춘다. 두 바는 지도 위에 그대로 겹쳐 그려지므로(공간을 밀어내지
        /// 않음) 화면 전체 높이를 기준으로 중앙 정렬하면 안 되고, 이 둘을
        /// 뺀 "실제로 보이는" 구간을 기준으로 잡아야 한다 — 안 그러면 특히
        /// 위쪽(스코어보드가 마커바보다 커서) 지도 가장자리가 바 뒤에 가려
        /// 잘려 보인다(사용자 스크린샷으로 확인). 36x36"(정사각형)과
        /// 54x36"(가로가 긴) 둘 다 이 규칙 하나로 동일하게 처리된다. 평소의
        /// 줌 범위(MinZoom~MaxZoom)와 무관하게 필요한 배율을 그대로 쓴다.</summary>
        private void ApplyScreenshotFitView()
        {
            var parent = mapArea.parent as RectTransform;
            Vector2 avail = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);

            float usableHeight = avail.y - GameConstants.ScoreboardHeight - MarkerBarHeight;
            float fitScale = Mathf.Max(usableHeight - ScreenshotHeightMarginPx * 2f, 1f) / mapSizeMm.y;
            mapArea.localScale = new Vector3(fitScale, fitScale, 1f);

            // 화면 정중앙이 아니라 두 바 사이 구간의 중앙으로 피봇을 옮긴다 —
            // 위 바가 아래 바보다 크면(지금은 84 vs 44) 그만큼 지도를 아래로
            // 내려야 두 바 사이에서 시각적으로 가운데에 온다.
            float pivotY = (MarkerBarHeight - GameConstants.ScoreboardHeight) / 2f;
            mapArea.anchoredPosition = new Vector2(0f, pivotY);
        }

        /// <summary>Screenshots/(게임 시작 시각)_(이번 세션 몇 번째 스샷인지).png
        /// 형태의 저장 경로를 만든다. 타임스탬프는 세션 내내 고정, 뒤의
        /// 번호만 찍을 때마다 올라간다.</summary>
        private string BuildScreenshotPath()
        {
            string dir = Path.Combine(ExeDirectory(), "Screenshots");
            Directory.CreateDirectory(dir);

            if (_screenshotSessionTimestamp == null)
            {
                InitScreenshotSession(); // 정상적으로는 Start()에서 이미 불렸어야 한다 — 안전망.
            }
            string path = Path.Combine(dir, $"{_screenshotSessionTimestamp}_{_screenshotCounter:D2}.png");
            _screenshotCounter++;
            return path;
        }

        /// <summary>빌드된 실행 파일과 같은 위치. Application.dataPath는 빌드에서
        /// "<실행파일 옆>/<제품명>_Data"를 가리키므로, 그 한 단계 위 폴더가
        /// 실행 파일이 있는 자리다(에디터에서는 프로젝트 폴더가 나온다 —
        /// Assets 폴더의 한 단계 위).</summary>
        private static string ExeDirectory()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
