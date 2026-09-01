using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// "로스터 불러오기"에서 뜨는 간단한 파일 탐색기 — 디렉터리를 오가며 *.json
    /// 파일을 고른다. Godot판은 OS 네이티브 FileDialog를 썼지만, Unity
    /// 스탠드얼론에는 그런 게 기본으로 없어서(플러그인 없이는) 직접 만든
    /// 목록형 탐색기로 대신한다 — 폴더 목록 → (더블)클릭으로 들어가기, 파일
    /// 클릭으로 선택+확정, "위로" 버튼으로 상위 폴더. 시작 폴더는 실행 파일
    /// 옆 Rosters/ 폴더다(ResolveInitialDirectory 참고, 없으면 만든다).
    /// 패널 바깥을 클릭하면 취소된다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RosterFileDialog : MonoBehaviour, IPointerDownHandler
    {
        public event Action<string> FileSelected;
        public event Action Cancelled;

        /// <summary>파일 항목(폴더 제외) 위에 마우스가 올라가면 그 전체 경로와
        /// 함께 올라간다 — 이 컴포넌트 자체는 "미리보기가 뭔지" 전혀 모르고,
        /// 호출부(예: 미션 프리셋 불러오기)가 원하면 구독해서 자기가 원하는
        /// 미리보기를 그리면 된다(사용자 요청 — 로스터 임포트 쪽은 그냥
        /// 구독 안 하면 기존과 동일).</summary>
        public event Action<string> FileHighlighted;
        public event Action FileHighlightCleared;

        private static readonly Color FolderColor = new Color(0.32f, 0.32f, 0.38f, 1f);
        private static readonly Color FileColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        private string _currentDir;
        private string _startDirectoryOverride;
        private TextMeshProUGUI _titleLabel;
        private TextMeshProUGUI _pathLabel;
        private RectTransform _listContent;
        private string _fileExtensionFilter = "*.json";

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
            panelRect.sizeDelta = new Vector2(560f, 480f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            panelGo.AddComponent<PanelBlocker>();

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            _titleLabel = CreateLabel(panelGo.transform, "로스터 JSON 선택", 18f, 24f);

            var pathRow = new GameObject("PathRow", typeof(RectTransform));
            pathRow.transform.SetParent(panelGo.transform, false);
            var pathRowLe = pathRow.AddComponent<LayoutElement>();
            pathRowLe.preferredHeight = 28f;
            var pathRowLayout = pathRow.AddComponent<HorizontalLayoutGroup>();
            pathRowLayout.spacing = 8f;
            pathRowLayout.childControlWidth = true;
            pathRowLayout.childForceExpandWidth = false;
            pathRowLayout.childControlHeight = true;

            var upBtnGo = new GameObject("UpButton", typeof(RectTransform));
            upBtnGo.transform.SetParent(pathRow.transform, false);
            var upBtnLe = upBtnGo.AddComponent<LayoutElement>();
            upBtnLe.preferredWidth = 60f;
            var upBtnImg = upBtnGo.AddComponent<Image>();
            upBtnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var upBtn = upBtnGo.AddComponent<Button>();
            upBtn.onClick.AddListener(GoUp);
            var upBtnLabelGo = new GameObject("Label", typeof(RectTransform));
            upBtnLabelGo.transform.SetParent(upBtnGo.transform, false);
            var upBtnLabelRect = (RectTransform)upBtnLabelGo.transform;
            upBtnLabelRect.anchorMin = Vector2.zero;
            upBtnLabelRect.anchorMax = Vector2.one;
            upBtnLabelRect.offsetMin = Vector2.zero;
            upBtnLabelRect.offsetMax = Vector2.zero;
            var upBtnLabel = upBtnLabelGo.AddComponent<TextMeshProUGUI>();
            upBtnLabel.text = "위로";
            upBtnLabel.alignment = TextAlignmentOptions.Center;
            upBtnLabel.fontSize = 14f;
            upBtnLabel.color = Color.white;
            upBtnLabel.raycastTarget = false;

            var pathLabelGo = new GameObject("PathLabel", typeof(RectTransform));
            pathLabelGo.transform.SetParent(pathRow.transform, false);
            var pathLabelLe = pathLabelGo.AddComponent<LayoutElement>();
            pathLabelLe.flexibleWidth = 1f;
            _pathLabel = pathLabelGo.AddComponent<TextMeshProUGUI>();
            _pathLabel.fontSize = 13f;
            _pathLabel.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            _pathLabel.alignment = TextAlignmentOptions.MidlineLeft;
            _pathLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _pathLabel.overflowMode = TextOverflowModes.Truncate;

            // 스크롤 목록.
            _listContent = ScrollListUtil.Create(panelGo.transform, 340f, new Color(0.1f, 0.1f, 0.1f, 1f), out _, out _);

            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(panelGo.transform, false);
            var buttonRowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            buttonRowLayout.spacing = 8f;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childForceExpandWidth = true;
            CreateButton(buttonRow.transform, "취소", OnCancelPressed);

            gameObject.SetActive(false);
        }

        public void Open()
        {
            _currentDir = _startDirectoryOverride ?? ResolveInitialDirectory();
            RefreshList();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>이 브라우저는 "폴더 오가며 *.json 하나 고르기"라는 동작
        /// 자체는 로스터든 미션 프리셋이든 완전히 동일해서, 인스턴스를
        /// 재사용할 수 있게 제목/확장자만 바꿔 여는 오버로드를 추가했다
        /// (사용자 요청 — 미션 프리셋 불러오기도 같은 파일 브라우저 UX).</summary>
        public void Open(string title, string fileExtensionFilter = "*.json")
        {
            _titleLabel.text = title;
            _fileExtensionFilter = fileExtensionFilter;
            Open();
        }

        /// <summary>기본 시작 폴더(Document/) 대신 여기서 열게 한다 — 미션
        /// 프리셋은 로스터와 다른 폴더(Deployments/)를 쓰므로 필요할 때마다
        /// (Open 직전에) 새로 계산해서 넣어준다. null로 되돌리면 기본값으로
        /// 복귀한다.</summary>
        public void SetStartDirectory(string directory)
        {
            _startDirectoryOverride = directory;
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        /// <summary>저장소의 Document/ 폴더를 찾는다 — 로스터 JSON을 여기서
        /// 열고 저장한다(미션 프리셋 불러오기는 더 이상 이 다이얼로그를 쓰지
        /// 않는다 — MapSetupController의 항상 보이는 우측 목록으로 대체됨). Unity
        /// 프로젝트가 저장소 루트 자체로 승격되기 전에는 "Assets ->
        /// StarcraftTMG Simulator -> UnityProject -> 저장소 루트"로 3단계
        /// 위였는데(2026-08-28 리포 재구성으로 그 중첩이 사라졌다), 이 경로
        /// 계산이 같이 갱신되지 않아서 계속 3단계 위(Assets 폴더 자신 근처
        /// 엉뚱한 곳)를 가리키고 있었다 — Directory.Exists가 실패하니 매번
        /// 조용히 Application.dataPath(Assets 폴더)로 폴백해왔던 것. 지금은
        /// Assets 바로 한 단계 위가 저장소 루트이므로 그게 맞다. 혹시 모를
        /// 예전 중첩 구조(리포를 되돌렸다거나) 대비로 3단계 위도 한 번 더
        /// 확인해본다.
        public static string ResolveDocumentDirectory()
        {
            foreach (var upLevels in new[] { new[] { ".." }, new[] { "..", "..", ".." } })
            {
                try
                {
                    var parts = new List<string> { Application.dataPath };
                    parts.AddRange(upLevels);
                    parts.Add("Document");
                    string candidate = Path.GetFullPath(Path.Combine(parts.ToArray()));
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // 다음 후보로 넘어간다.
                }
            }
            return Application.dataPath;
        }

        /// <summary>기본 시작 폴더는 실행 파일 옆 Rosters/ 폴더다(사용자 요청,
        /// 2026-09-01 백로그 → 2026-09-02 적용) — 예전엔 저장소의 Document/
        /// 폴더(ResolveDocumentDirectory, 에디터 개발 중 로스터 JSON이 있던
        /// 자리)를 기본값으로 썼는데, 빌드된 실행 파일 기준으론 그 폴더가
        /// 존재하지 않는다. AppPaths.ExeDirectory()는 스크린샷 저장 위치
        /// 계산과 같은 방식(에디터에서는 저장소 루트, 빌드에서는 실행 파일
        /// 옆)이라 그대로 재사용한다. 폴더가 없으면 스크린샷 폴더처럼 처음
        /// 열 때 만들어준다 — 없으면 빈 목록/에러 대신 그냥 빈 폴더가 뜬다.</summary>
        private static string ResolveInitialDirectory()
        {
            string rostersDir = Path.Combine(AppPaths.ExeDirectory(), "Rosters");
            try
            {
                Directory.CreateDirectory(rostersDir);
                return rostersDir;
            }
            catch (Exception)
            {
                return ResolveDocumentDirectory(); // 실행 파일 옆에 폴더를 못 만들면(권한 등) 예전 기본값으로 폴백.
            }
        }

        private void GoUp()
        {
            try
            {
                var parent = Directory.GetParent(_currentDir);
                if (parent != null)
                {
                    _currentDir = parent.FullName;
                    RefreshList();
                }
            }
            catch (Exception)
            {
                // 접근 불가한 상위 폴더 — 그냥 무시.
            }
        }

        private void RefreshList()
        {
            _pathLabel.text = _currentDir;
            // 폴더를 옮기면 지금 목록의 항목들이 통째로 파괴되는데, 마우스가
            // 그 위에 있던 채로 파괴되면 OnPointerExit이 안 불릴 수 있다 —
            // 미리보기가 존재하지 않는 파일을 계속 가리키는 걸 막기 위해
            // 여기서 미리 확실히 지워둔다.
            FileHighlightCleared?.Invoke();

            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_listContent.GetChild(i).gameObject);
            }

            string[] dirs;
            string[] files;
            try
            {
                dirs = Directory.GetDirectories(_currentDir);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                dirs = Array.Empty<string>();
            }
            try
            {
                files = Directory.GetFiles(_currentDir, _fileExtensionFilter);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                files = Array.Empty<string>();
            }

            foreach (var dir in dirs)
            {
                string capturedDir = dir;
                // "📁"(U+1F4C1)는 이 프로젝트가 쓰는 PretendardVariable SDF
                // 폰트 아틀라스에 없는 글리프(이모지)라 안 보인다 — 대신
                // 사용자가 추가한 Resources/UI/Forder.png 아이콘을 쓴다
                // (showFolderIcon).
                CreateEntry(Path.GetFileName(dir), FolderColor, () =>
                {
                    _currentDir = capturedDir;
                    RefreshList();
                }, showFolderIcon: true);
            }
            foreach (var file in files)
            {
                string capturedFile = file;
                CreateEntry(Path.GetFileName(file), FileColor, () =>
                {
                    Close();
                    FileSelected?.Invoke(capturedFile);
                }, capturedFile);
            }

            if (dirs.Length == 0 && files.Length == 0)
            {
                CreateLabel(_listContent, "(비어 있음 - .json 파일이 없습니다)", 13f, 24f);
            }
        }

        private void CreateEntry(string label, Color color, UnityEngine.Events.UnityAction onClick, string hoverFilePath = null, bool showFolderIcon = false)
        {
            var go = new GameObject($"Entry_{label}", typeof(RectTransform));
            go.transform.SetParent(_listContent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 32f;
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            if (hoverFilePath != null)
            {
                var hover = go.AddComponent<EntryHoverHandler>();
                hover.OnEnter = () => FileHighlighted?.Invoke(hoverFilePath);
                hover.OnExit = () => FileHighlightCleared?.Invoke();
            }

            // 폴더 항목은 사용자가 추가한 Resources/UI/Forder.png 아이콘을
            // 라벨 왼쪽에 붙인다(2026-09-02) — 그만큼 라벨 시작 x를 밀어낸다.
            float labelStartX = 8f;
            if (showFolderIcon)
            {
                const float IconSize = 18f;
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(go.transform, false);
                var iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(8f, 0f);
                iconRect.sizeDelta = new Vector2(IconSize, IconSize);
                var iconImg = iconGo.AddComponent<RawImage>();
                iconImg.texture = Resources.Load<Texture2D>("UI/Forder");
                iconImg.raycastTarget = false;
                labelStartX = 8f + IconSize + 6f;
            }

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(labelStartX, 0f);
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.fontSize = 14f;
            labelText.color = Color.white;
            labelText.raycastTarget = false;
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

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, float preferredHeight)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            return label;
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

        private class PanelBlocker : MonoBehaviour, IPointerDownHandler
        {
            public void OnPointerDown(PointerEventData eventData)
            {
                eventData.Use();
            }
        }

        private class EntryHoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action OnEnter;
            public Action OnExit;

            public void OnPointerEnter(PointerEventData eventData)
            {
                OnEnter?.Invoke();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                OnExit?.Invoke();
            }
        }
    }
}
