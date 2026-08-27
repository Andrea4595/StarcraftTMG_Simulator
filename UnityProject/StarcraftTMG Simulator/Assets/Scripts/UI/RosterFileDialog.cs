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
    /// 클릭으로 선택+확정, "위로" 버튼으로 상위 폴더. 시작 폴더는 이 저장소의
    /// Document/ 폴더(로스터 JSON이 실제로 있는 곳)를 우선 찾고, 없으면
    /// Application.dataPath로 대체한다. 패널 바깥을 클릭하면 취소된다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RosterFileDialog : MonoBehaviour, IPointerDownHandler
    {
        public event Action<string> FileSelected;
        public event Action Cancelled;

        private static readonly Color FolderColor = new Color(0.32f, 0.32f, 0.38f, 1f);
        private static readonly Color FileColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        private string _currentDir;
        private TextMeshProUGUI _pathLabel;
        private RectTransform _listContent;

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

            CreateLabel(panelGo.transform, "로스터 JSON 선택", 18f, 24f);

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
            _pathLabel.enableWordWrapping = false;
            _pathLabel.overflowMode = TextOverflowModes.Truncate;

            // 스크롤 목록.
            _listContent = ScrollListUtil.Create(panelGo.transform, 340f, new Color(0.1f, 0.1f, 0.1f, 1f), out _);

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
            _currentDir = ResolveInitialDirectory();
            RefreshList();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private static string ResolveInitialDirectory()
        {
            // Assets -> StarcraftTMG Simulator -> UnityProject -> 저장소 루트 -> Document
            try
            {
                string candidate = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "Document"));
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // 아래 대체 경로로 넘어간다.
            }
            return Application.dataPath;
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
                files = Directory.GetFiles(_currentDir, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                files = Array.Empty<string>();
            }

            foreach (var dir in dirs)
            {
                string capturedDir = dir;
                CreateEntry("📁 " + Path.GetFileName(dir), FolderColor, () =>
                {
                    _currentDir = capturedDir;
                    RefreshList();
                });
            }
            foreach (var file in files)
            {
                string capturedFile = file;
                CreateEntry(Path.GetFileName(file), FileColor, () =>
                {
                    Close();
                    FileSelected?.Invoke(capturedFile);
                });
            }

            if (dirs.Length == 0 && files.Length == 0)
            {
                CreateLabel(_listContent, "(비어 있음 — .json 파일이 없습니다)", 13f, 24f);
            }
        }

        private void CreateEntry(string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Entry_{label}", typeof(RectTransform));
            go.transform.SetParent(_listContent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 32f;
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
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
    }
}
