using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// "이어하기" 화면(2026-08-31 신설) — Entry 구석 버튼으로 들어온다.
    /// Saves/ 폴더의 저장 파일을 목록으로 보여주고, 클릭하면 그 파일을
    /// 파싱해서 SaveGamePicked로 올린다 — 실제 씬 전환(GameLoadRequest에
    /// 담아 GameBoard로)은 이 컴포넌트를 만든 쪽(부트스트랩)이 담당한다
    /// (다른 화면들과 같은 "컨트롤러는 이벤트만 올리고 씬 이름은 모른다"
    /// 원칙).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class LoadGameController : MonoBehaviour
    {
        public event Action<Dictionary<string, object>> SaveGamePicked;
        public event Action BackRequested;

        private const float MarginPx = 16f;

        private RectTransform Root => (RectTransform)transform;
        private RectTransform _listContent;

        private void Start()
        {
            BuildBackButton();
            BuildTitle();
            BuildList();
        }

        private void BuildBackButton()
        {
            var go = new GameObject("BackButton", typeof(RectTransform));
            go.transform.SetParent(Root, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(MarginPx, -MarginPx);
            rect.sizeDelta = new Vector2(32f, 32f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/BackButton");
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => BackRequested?.Invoke());
        }

        private void BuildTitle()
        {
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(Root, false);
            var titleRect = (RectTransform)titleGo.transform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -MarginPx);
            titleRect.sizeDelta = new Vector2(700f, 32f);
            var titleLabel = titleGo.AddComponent<TextMeshProUGUI>();
            titleLabel.text = "저장된 게임을 골라주세요";
            titleLabel.fontSize = 18f;
            titleLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.raycastTarget = false;
        }

        private void BuildList()
        {
            var panelGo = new GameObject("ListPanel", typeof(RectTransform));
            panelGo.transform.SetParent(Root, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.offsetMin = new Vector2(-260f, MarginPx);
            panelRect.offsetMax = new Vector2(260f, -(MarginPx + 32f + MarginPx));

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            _listContent = ScrollListUtil.Create(panelRect, 100f, new Color(0f, 0f, 0f, 0.15f), out _, out var scrollLe);
            scrollLe.flexibleHeight = 1f;

            RefreshList();
        }

        private void RefreshList()
        {
            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_listContent.GetChild(i).gameObject);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(GameSaveIO.ResolveSavesDirectory(), "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return;
            }

            bool any = false;
            foreach (var path in files)
            {
                if (!GameSaveIO.TryReadFile(path, out var tree, out _))
                {
                    continue;
                }
                any = true;
                CreateListItem(path, tree);
            }

            if (!any)
            {
                var emptyLabel = CreateLabel(_listContent, "저장된 게임이 없습니다.");
                emptyLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            }
        }

        /// <summary>항목 하나 — 파일 이름 + "미션 이름 · 라운드 N · OO 페이즈"
        /// 요약 한 줄. 클릭하면 이미 다 읽어둔 tree를 그대로 올린다(다시
        /// 읽지 않음).</summary>
        private void CreateListItem(string path, Dictionary<string, object> tree)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            var mission = GameSaveIO.GetDict(tree, "mission");
            var matchState = GameSaveIO.GetDict(tree, "match_state");
            string missionName = GameSaveIO.GetString(mission, "mission_name");
            int round = GameSaveIO.GetInt(matchState, "round_number", 1);
            int phaseIndex = GameSaveIO.GetInt(matchState, "phase_index", 0);
            string phaseName = phaseIndex >= 0 && phaseIndex < MatchState.PhaseNames.Length ? MatchState.PhaseNames[phaseIndex] : "";
            string summary = string.IsNullOrEmpty(missionName)
                    ? $"라운드 {round} - {phaseName}"
                    : $"{missionName} - 라운드 {round} - {phaseName}";

            var itemGo = new GameObject("Item", typeof(RectTransform));
            itemGo.transform.SetParent(_listContent, false);
            var itemLayout = itemGo.AddComponent<VerticalLayoutGroup>();
            itemLayout.padding = new RectOffset(8, 8, 6, 6);
            itemLayout.spacing = 2f;
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandWidth = true;
            itemLayout.childControlHeight = true;
            itemLayout.childForceExpandHeight = false;

            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemBg;
            itemBtn.onClick.AddListener(() => SaveGamePicked?.Invoke(tree));

            var nameLabel = CreateLabel(itemGo.transform, name);
            nameLabel.fontSize = 14f;
            nameLabel.fontStyle = FontStyles.Bold;
            nameLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var summaryLabel = CreateLabel(itemGo.transform, summary);
            summaryLabel.fontSize = 12f;
            summaryLabel.color = new Color(0.7f, 0.75f, 0.85f, 1f);
            summaryLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 13f;
            label.color = Color.white;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }
    }
}
