using System;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        // ── 스크린샷 토스트(사용자 요청: 파일명을 우측 하단에 잠깐 띄웠다가
        // 내리고, 클릭하면 스크린샷 폴더를 연다 — 사라질 땐 1초에 걸쳐 서서히
        // 투명해진다) — 이 프로젝트는 코루틴을 안 쓰는 관례라(다른 타이머성
        // 연출도 전부 Time.time 비교를 Update()에서 하는 방식) 여기도 같은
        // 방식을 따른다.
        private const float ScreenshotToastVisibleDurationSeconds = 2.5f; // 완전히 보이는 구간(페이드 시작 전).
        private const float ScreenshotToastFadeDurationSeconds = 1f;
        private GameObject _screenshotToastGo;
        private CanvasGroup _screenshotToastCanvasGroup;
        private TextMeshProUGUI _screenshotToastLabel;
        // Time.time 기준 "다 보여준 뒤 페이드를 시작하는 시각" — 음수면 토스트가
        // 안 떠 있거나 이미 다 사라진 상태.
        private float _screenshotToastHideTime = -1f;
        private string _lastScreenshotDirectory;

        /// <summary>게임 보드가 뜨는 시점(Start())에 한 번 불러서 세션 타임스탬프를
        /// 고정한다.</summary>
        private void InitScreenshotSession()
        {
            _screenshotSessionTimestamp = DateTime.Now.ToString("yyMMddHHmmss");
            _screenshotCounter = 0;
        }

        private void TakeScreenshot()
        {
            string path = BuildScreenshotPath();
            ScreenCapture.CaptureScreenshot(path);
            ShowScreenshotToast(Path.GetFileName(path), Path.GetDirectoryName(path));
        }

        /// <summary>Screenshots/(게임 시작 시각)_(이번 세션 몇 번째 스샷인지).png
        /// 형태의 저장 경로를 만든다. 타임스탬프는 세션 내내 고정, 뒤의
        /// 번호만 찍을 때마다 올라간다.</summary>
        private string BuildScreenshotPath()
        {
            string dir = Path.Combine(AppPaths.ExeDirectory(), "Screenshots");
            Directory.CreateDirectory(dir);

            if (_screenshotSessionTimestamp == null)
            {
                InitScreenshotSession(); // 정상적으로는 Start()에서 이미 불렸어야 한다 — 안전망.
            }
            string path = Path.Combine(dir, $"{_screenshotSessionTimestamp}_{_screenshotCounter:D2}.png");
            _screenshotCounter++;
            return path;
        }

        /// <summary>매 프레임 폴링 — ScreenshotToastVisibleDurationSeconds가
        /// 지나면 그때부터 ScreenshotToastFadeDurationSeconds에 걸쳐 알파를
        /// 1→0으로 서서히 낮추고(사용자 요청), 다 낮아지면 완전히 끈다.
        /// BoardManager.cs의 Update()가 부른다.</summary>
        private void UpdateScreenshotToast()
        {
            if (_screenshotToastHideTime < 0f || _screenshotToastGo == null)
            {
                return;
            }
            float fadeElapsed = Time.time - _screenshotToastHideTime;
            if (fadeElapsed <= 0f)
            {
                return; // 아직 완전히 보이는 구간(페이드 시작 전).
            }
            if (fadeElapsed >= ScreenshotToastFadeDurationSeconds)
            {
                _screenshotToastGo.SetActive(false);
                _screenshotToastHideTime = -1f;
                return;
            }
            _screenshotToastCanvasGroup.alpha = 1f - fadeElapsed / ScreenshotToastFadeDurationSeconds;
        }

        private void ShowScreenshotToast(string fileName, string directory)
        {
            _lastScreenshotDirectory = directory;
            EnsureScreenshotToast();
            _screenshotToastLabel.text = fileName;
            _screenshotToastCanvasGroup.alpha = 1f;
            _screenshotToastGo.SetActive(true);
            _screenshotToastGo.transform.SetAsLastSibling();
            _screenshotToastHideTime = Time.time + ScreenshotToastVisibleDurationSeconds;
        }

        /// <summary>지도 뷰포트(팀 패널 사이, 마커바 위)의 우측 하단 구석에
        /// 붙인다 — 화면 절대 우측하단은 B팀 패널/마커바와 겹친다.</summary>
        private void EnsureScreenshotToast()
        {
            if (_screenshotToastGo != null)
            {
                return;
            }

            var canvasParent = GetCanvasParent();

            var go = new GameObject("ScreenshotToast", typeof(RectTransform));
            go.transform.SetParent(canvasParent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-(GameConstants.PendingPanelWidth + 16f), GameConstants.MarkerBarHeight + 16f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            // 배경/라벨 색을 각각 건드리지 않고 통째로 페이드시키려고
            // CanvasGroup.alpha 하나로 처리한다.
            _screenshotToastCanvasGroup = go.AddComponent<CanvasGroup>();

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            _screenshotToastLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _screenshotToastLabel.fontSize = 13f;
            _screenshotToastLabel.color = Color.white;
            _screenshotToastLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _screenshotToastLabel.raycastTarget = false;

            // 클릭하면 스크린샷 폴더를 연다(사용자 요청) — 배경 Image 자체가
            // raycastTarget=true(기본값)라 버튼의 targetGraphic으로 그냥 쓴다.
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(OpenScreenshotFolder);

            _screenshotToastGo = go;
            _screenshotToastGo.SetActive(false);
        }

        private void OpenScreenshotFolder()
        {
            if (string.IsNullOrEmpty(_lastScreenshotDirectory))
            {
                return;
            }
            Process.Start("explorer.exe", $"\"{_lastScreenshotDirectory}\"");
        }
    }
}
