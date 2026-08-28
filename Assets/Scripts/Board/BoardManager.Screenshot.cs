using System;
using System.IO;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 스크린샷 ────────────────────────────────────────────────────
        // 지금 화면에 보이는 그대로만 찍는다 — 예전엔 캡처 직전 잠깐 지도를
        // 화면에 꽉 채운 전용 뷰로 바꿨다가 찍고 나서 원래대로 되돌렸는데,
        // 그 "꽉 채우기"가 FitButton(마커바)이라는 일반 조작으로 따로
        // 분리됐다(BoardManager.PanZoom.cs FitMapToView 참고) — 원하는
        // 구도로 찍고 싶으면 그 버튼을 먼저 누르면 된다(사용자 요청).

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
            ScreenCapture.CaptureScreenshot(BuildScreenshotPath());
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
