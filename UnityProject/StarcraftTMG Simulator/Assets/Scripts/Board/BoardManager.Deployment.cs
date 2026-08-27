using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 예비대 배치 ──────────────────────────────────────────────────

        /// <summary>팀 A는 왼쪽, 팀 B는 오른쪽에 각자 로스터 불러오기 버튼 +
        /// 예비대 유닛 목록 + 토큰 목록을 담은 패널을 하나씩 짓는다.</summary>
        private void BuildPendingPanel()
        {
            BuildTeamPanel("A", left: true);
            BuildTeamPanel("B", left: false);

            // AddPendingUnit()은 부트스트랩 등에서 Configure() 직후(Start() 전에)
            // 불릴 수 있는데, 그때는 이 패널들이 아직 없어 RefreshPendingList()가
            // 아무 것도 못 그리고 조용히 넘어간다 — 여기서 한 번 더 그려준다.
            RefreshPendingList();
            RefreshRosterTokenList();
        }

        private void BuildTeamPanel(string team, bool left)
        {
            var canvasParent = GetCanvasParent();

            var panelGo = new GameObject($"PendingPanel_{team}", typeof(RectTransform));
            panelGo.transform.SetParent(canvasParent, false);
            var panel = (RectTransform)panelGo.transform;
            float xAnchor = left ? 0f : 1f;
            panel.anchorMin = new Vector2(xAnchor, 1f);
            panel.anchorMax = new Vector2(xAnchor, 1f);
            panel.pivot = new Vector2(xAnchor, 1f);
            // 상단 스코어보드 바(ScoreboardPanel, 화면 맨 위를 가로지름) 아래로
            // 내려서 겹치지 않게 한다.
            panel.anchoredPosition = new Vector2(left ? 16f : -16f, -(GameConstants.ScoreboardHeight + 16f));
            panel.sizeDelta = new Vector2(220f, 40f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateListButton(panel, $"{team} 로스터 불러오기", () => ImportRoster(team));

            // 예비대 유닛 목록 — 길어지면 늘어나는 대신 스크롤된다.
            // RefreshPendingList()가 이 컨테이너의 자식만 갈아끼운다.
            _pendingUnitsListContainers[team] = ScrollListUtil.Create(panel, 180f, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _);

            // 토큰 목록 — 유닛과 달리 배치해도 목록에서 안 지워진다(몇 번이든 재배치 가능).
            CreateSectionLabel(panel, "토큰");
            _rosterTokenListContainers[team] = ScrollListUtil.Create(panel, 180f, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _);
        }

        private static void CreateSectionLabel(Transform parent, string text)
        {
            var go = new GameObject("SectionLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 18f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 13f;
            label.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>예비대 패널의 버튼 하나(로스터 불러오기/예비대 유닛/토큰이
        /// 전부 이 모양을 공유한다).</summary>
        private static void CreateListButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject($"Btn_{label}", typeof(RectTransform));
            btnGo.transform.SetParent(parent, false);
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 32f;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 13f;
            labelText.color = Color.white;
            labelText.raycastTarget = false;
        }

        private void RefreshPendingList()
        {
            foreach (var kv in _pendingUnitsListContainers)
            {
                string team = kv.Key;
                var container = kv.Value;
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    Destroy(container.GetChild(i).gameObject);
                }

                for (int i = 0; i < _pendingUnits.Count; i++)
                {
                    var def = _pendingUnits[i];
                    if (def.Team != team)
                    {
                        continue;
                    }
                    int capturedIndex = i;
                    CreateListButton(container, $"{def.Name} ({def.ModelCount}모델)", () => StartDeployment(capturedIndex));
                }
            }
        }

        private void RefreshRosterTokenList()
        {
            foreach (var kv in _rosterTokenListContainers)
            {
                string team = kv.Key;
                var container = kv.Value;
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    Destroy(container.GetChild(i).gameObject);
                }

                for (int i = 0; i < _pendingRosterTokens.Count; i++)
                {
                    var def = _pendingRosterTokens[i];
                    if (def.Team != team)
                    {
                        continue;
                    }
                    int capturedIndex = i;
                    CreateListButton(container, def.Name, () => StartRosterTokenPlacement(capturedIndex));
                }
            }
        }

        /// <summary>예비대 목록에서 index번째 정의의 배치를 시작한다 — 목록에서
        /// 빼고, 배치 미리보기(고스트)와 배치 밴드를 보여준다. 실제 리딩 모델
        /// 생성/드래그는 지도 배경을 클릭하는 순간(BeginDeploymentDrag) 이루어진다.</summary>
        public void StartDeployment(int index)
        {
            if (_unitMoveActive || _pendingDeploymentDef != null || _pendingRosterTokenDef != null || _displacementQueue.Count > 0
                    || index < 0 || index >= _pendingUnits.Count)
            {
                return;
            }
            // 여기서 예비대 목록에서 이미 항목을 빼므로, 되돌리기 트랜잭션도
            // 여기서 열어야 한다 — BeginDeploymentDrag()에서 열면 이미 빠진
            // 뒤라 되돌려도 목록에 복원이 안 된다. 지도 클릭 전에 우클릭으로
            // 취소하면(HandlePendingDeploymentInput) 폐기, 실제로 배치까지
            // 마치면 CompleteUnitMove()에서 커밋된다.
            BeginUndoTransaction();
            var def = _pendingUnits[index];
            _pendingUnits.RemoveAt(index);
            RefreshPendingList();

            _pendingDeploymentDef = def;
            ShowBasePlacementPreview(def.SizeMm, def.FillColor, def.IsDisplacement);
            ShowDeploymentBand(def);
        }

        private void HandlePendingDeploymentInput()
        {
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi())
            {
                // 빈 곳 우클릭 — 배치 취소, 정의를 예비대 목록으로 되돌린다.
                _pendingUnits.Add(_pendingDeploymentDef);
                _pendingDeploymentDef = null;
                RefreshPendingList();
                ClearPlacementPreview();
                ClearDeploymentBand();
                // StartDeployment()에서 연 트랜잭션을 그냥 버린다 — 예비대
                // 목록에서 뺐던 걸 그대로 되돌려놨을 뿐 보드는 전혀 안 바뀌었다.
                DiscardUndoTransaction();
                return;
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                if (TryGetLocalMouse(out var clickPoint))
                {
                    BeginDeploymentDrag(clickPoint);
                }
                return;
            }

            if (TryGetLocalMouse(out var mouseLocal))
            {
                UpdatePlacementPreviewPosition(mouseLocal);
            }
        }

    }
}
