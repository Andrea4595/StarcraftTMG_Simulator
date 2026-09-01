using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// "되돌리기" 패널(2026-09-01 신설, 같은 날 재구성 세 번) — 마커바의
    /// 되돌리기 버튼을 누르면 뜬다. 예전엔 Ctrl+Z/Ctrl+Shift+Z 단축키로
    /// 한 단계씩만 오갈 수 있었는데(완전히 제거됨), 이제 지금까지의 모든
    /// 조작을 실제 수행된 시간순(최근이 위)으로 한 목록에 보여준다 — 아직
    /// 취소 안 된 항목은 밝게, 이미 취소된 항목은 어둡게 표시된다. 밝은
    /// 항목을 클릭하면 그 지점까지(그 뒤에 쌓인 것들까지 포함) 한꺼번에
    /// 취소되고, 어두운 항목을 클릭하면 그 지점까지 한꺼번에 복원(redo)
    /// 된다(BoardManager.UndoRedo.cs가 보드 전체 스냅샷을 쌓는 방식이라
    /// 중간 항목 하나만 독립적으로 처리할 수 없어서, "그 시점까지 전부"가
    /// 유일하게 의미 있는 단위 — 사용자와 확인한 설계).
    ///
    /// 배경 전체를 덮는 진짜 모달이 아니다 — DiceRollDialog와 같은 "떠있는
    /// 비독점 참고창" 구조: 배경 Image가 없어서 패널 바깥(대부분의 화면)의
    /// 클릭은 그대로 지도로 통과한다. 위치/크기는 사용자 지정으로 두 번
    /// 조정됨 — 처음엔 화면 우측 절반을 꽉 채우게 했다가 "너무 과하다"는
    /// 피드백을 받고, 지금은 플레이어 B 로스터 창(GameConstants.
    /// PendingPanelWidth) 바로 왼쪽에 그 폭의 절반 크기로 살짝 띄운 작은
    /// 창으로 줄였다(BuildPendingPanel 참고 — 같은 상수를 공유). 닫기는
    /// 오직 자신의 "닫기" 버튼으로만 한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UndoHistoryDialog : MonoBehaviour
    {
        private static readonly Color RowColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color UndoneRowColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color UndoneLabelColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        private BoardManager _board;
        private RectTransform _listContent;
        // BoardManager.UndoHistoryVersion을 마지막으로 확인했을 때의 값 —
        // Update()에서 매 프레임 비교해 바뀌었으면 다시 그린다. 이 컴포넌트는
        // 닫혀있는 동안(SetActive(false)) Unity가 Update() 자체를 안 불러주므로
        // 열려있을 때만 폴링된다(코루틴 없이, 이 프로젝트의 기존 Time.time
        // 폴링 관례와 같은 결의 접근). 로컬 커밋/카스케이드는 물론, 창이 열려
        // 있는 동안 상대에게서 ApplyRemoteUndoPush/ApplyRemoteUndoCascade가
        // 도착하는 경우도 이걸로 함께 잡힌다.
        private int _lastSeenVersion = -1;

        private void Awake()
        {
            // 배경이 없어졌으니(비독점 창) 이 루트 자체는 좌표계 용도일
            // 뿐이다 — DiceRollDialog.cs와 같은 이유.
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // 플레이어 B 로스터 창(GameConstants.PendingPanelWidth, 화면
            // 우측 가장자리) 바로 왼쪽에 살짝 띄운 떠있는 창 — 폭은 그
            // 로스터 창의 절반(사용자 지정, 화면 절반은 너무 과했다는
            // 피드백으로 수정). 세로는 화면에 꽉 채우지 않고 내용에 맞춰
            // 스스로 크기를 정한다(ContentSizeFitter) — WeaponProfileDialog
            // 처럼 화면을 가로지르는 스트레치가 아니라 DiceRollDialog처럼
            // 한 점에 고정된 떠있는 창.
            const float PanelGapPx = 12f;
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-(GameConstants.PendingPanelWidth + PanelGapPx), -(GameConstants.TopBarHeight + PanelGapPx));
            panelRect.sizeDelta = new Vector2(GameConstants.PendingPanelWidth / 2f * 1.8f, 0f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var panelFitter = panelGo.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleLabel = CreateLabel(panelGo.transform, "되돌리기", 16f, FontStyles.Bold, Color.white);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.GetComponent<LayoutElement>().preferredHeight = 22f;

            // 떠있는 창이라 세로가 내용에 맞춰지므로, 목록 자체는 고정
            // 최대 높이(그 이상은 스크롤)로 되돌린다 — 화면 꽉 채우던
            // flexibleHeight는 더 이상 안 맞는다.
            _listContent = ScrollListUtil.Create(panelGo.transform, 260f, new Color(0f, 0f, 0f, 0.15f), out _, out _);

            var closeGo = new GameObject("Close", typeof(RectTransform));
            closeGo.transform.SetParent(panelGo.transform, false);
            var closeLe = closeGo.AddComponent<LayoutElement>();
            closeLe.preferredHeight = 36f;
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.onClick.AddListener(Close);
            var closeLabel = CreateLabel(closeGo.transform, "닫기", 16f, FontStyles.Normal, Color.white);
            var closeLabelRect = (RectTransform)closeLabel.transform;
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;
            Object.Destroy(closeLabel.GetComponent<LayoutElement>());
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.raycastTarget = false;

            gameObject.SetActive(false);
        }

        public void Open(BoardManager board)
        {
            _board = board;
            SyncAndRefresh();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        /// <summary>열려있는 동안만 Unity가 불러준다(SetActive(false)면 Update() 자체가
        /// 안 돎) — BoardManager의 되돌리기 스택이 그새 바뀌었으면(내 커밋/카스케이드는
        /// 물론 상대에게서 온 것까지) 목록을 다시 그린다.</summary>
        private void Update()
        {
            if (_board == null)
            {
                return;
            }
            if (_board.UndoHistoryVersion != _lastSeenVersion)
            {
                SyncAndRefresh();
            }
        }

        private void SyncAndRefresh()
        {
            _lastSeenVersion = _board.UndoHistoryVersion;
            RefreshList();
        }

        /// <summary>목록을 다시 그린다 — 항목을 클릭했을 때(취소/복원 실행
        /// 직후)도 SyncAndRefresh를 통해 다시 부른다.</summary>
        private void RefreshList()
        {
            if (_board == null)
            {
                return;
            }

            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(_listContent.GetChild(i).gameObject);
            }

            var rows = _board.GetCombinedHistoryForDisplay();
            if (rows.Count == 0)
            {
                var emptyLabel = CreateLabel(_listContent, "조작 내역이 없습니다.", 13f, FontStyles.Normal, new Color(0.55f, 0.55f, 0.55f, 1f));
                emptyLabel.GetComponent<LayoutElement>().preferredHeight = 28f;
                return;
            }

            foreach (var row in rows)
            {
                int capturedIndex = row.StackIndex;
                bool isUndone = row.IsUndone;

                var itemGo = new GameObject("Row", typeof(RectTransform));
                itemGo.transform.SetParent(_listContent, false);
                var itemLe = itemGo.AddComponent<LayoutElement>();
                itemLe.preferredHeight = 30f;
                var itemBg = itemGo.AddComponent<Image>();
                itemBg.color = isUndone ? UndoneRowColor : RowColor;
                var itemBtn = itemGo.AddComponent<Button>();
                itemBtn.targetGraphic = itemBg;
                itemBtn.onClick.AddListener(() =>
                {
                    if (isUndone)
                    {
                        _board.RestoreOperationsDownTo(capturedIndex);
                    }
                    else
                    {
                        _board.CancelOperationsDownTo(capturedIndex);
                    }
                    SyncAndRefresh();
                });

                var label = CreateLabel(itemGo.transform, row.Label, 13f, FontStyles.Normal, isUndone ? UndoneLabelColor : Color.white);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
                Object.Destroy(label.GetComponent<LayoutElement>());
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.raycastTarget = false;
            }
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, FontStyles style, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>();
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }
    }
}
