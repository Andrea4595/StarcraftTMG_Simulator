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

        // 목록 박스 최대 높이. 토큰 목록은 ListMaxHeight에서 캡(그 이상은
        // 스크롤, 5개 미만이면 ComputeFittedHeight로 그만큼만 핏하게 줄어듦).
        // 예비대 유닛 목록은 항상 더 큰 UnitListExpandedMaxHeight를 쓴다 —
        // 토큰 섹션 유무와 무관하다(RefreshListHeights 참고).
        private const float ListMaxHeight = 180f;
        private const float UnitListExpandedMaxHeight = 480f;

        // 아직 이 팀에 로스터를 하나도 안 불러왔을 때 "A 로스터 불러오기"
        // 버튼이 패널 전체를 꽉 채우도록 쓰는 큰 높이. 로스터를 불러오고
        // 나면(OnRosterFileSelected) 이 버튼은 아예 숨긴다 — 그 뒤로는
        // 예비대/토큰 목록만 보인다.
        private const float RosterImportButtonFillHeight = 400f;

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

            // 아직 로스터를 안 불러온 상태에선 이 버튼이 패널을 꽉 채운
            // 큰 호출 유도 버튼으로 보인다. OnRosterFileSelected가 임포트에
            // 성공하면 이 버튼 자체를 SetActive(false)로 완전히 숨긴다.
            _rosterImportButtons[team] = CreateListButton(panel, $"{team} 로스터 불러오기", () => ImportRoster(team), RosterImportButtonFillHeight);

            // 예비대 유닛 목록 — 항목 수만큼 핏하게 커지다가 UnitListExpandedMaxHeight
            // 에서 스크롤로 전환된다(토큰 섹션 유무와 무관). 실제 높이는
            // RefreshPendingList()가 매번 다시 계산해서 적용한다.
            _pendingUnitsListContainers[team] = ScrollListUtil.Create(panel, ListMaxHeight, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _, out var unitListLayoutElement);
            _pendingUnitsListLayoutElements[team] = unitListLayoutElement;

            // 토큰 목록 — 유닛과 달리 배치해도 목록에서 안 지워진다(몇 번이든 재배치 가능).
            // 라벨+목록을 한 래퍼에 담아서 통째로 켜고 끌 수 있게 한다 —
            // 이 팀에 로스터로 들어온 토큰이 하나도 없으면 RefreshRosterTokenList가
            // 이 래퍼 자체를 꺼서 빈 "토큰" 제목만 덩그러니 남는 걸 막는다.
            var tokenSectionGo = new GameObject("TokenSection", typeof(RectTransform));
            tokenSectionGo.transform.SetParent(panel, false);
            var tokenSectionLayout = tokenSectionGo.AddComponent<VerticalLayoutGroup>();
            tokenSectionLayout.spacing = 8f;
            tokenSectionLayout.childControlWidth = true;
            tokenSectionLayout.childForceExpandWidth = true;
            tokenSectionLayout.childControlHeight = true;
            tokenSectionLayout.childForceExpandHeight = false;
            var tokenSectionFitter = tokenSectionGo.AddComponent<ContentSizeFitter>();
            tokenSectionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateSectionLabel(tokenSectionGo.transform, "토큰");
            // 토큰이 5개 미만이면 핏하게 줄어든다(같은 ComputeFittedHeight 방식) —
            // RefreshRosterTokenList()가 매번 다시 계산해서 적용한다.
            _rosterTokenListContainers[team] = ScrollListUtil.Create(tokenSectionGo.transform, ListMaxHeight, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _, out var tokenListLayoutElement);
            _rosterTokenListLayoutElements[team] = tokenListLayoutElement;

            _tokenSectionRoots[team] = tokenSectionGo;
            tokenSectionGo.SetActive(false); // RefreshRosterTokenList()가 토큰이 생기면 켠다.
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
        /// 전부 이 모양을 공유한다). preferredHeight를 키우면 "A 로스터
        /// 불러오기" 버튼처럼 패널을 꽉 채우는 큰 버튼도 만들 수 있다.</summary>
        private static GameObject CreateListButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float preferredHeight = 32f)
        {
            var btnGo = new GameObject($"Btn_{label}", typeof(RectTransform));
            btnGo.transform.SetParent(parent, false);
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = preferredHeight;
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

            return btnGo;
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
            RefreshListHeights();
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

                bool hasAny = false;
                for (int i = 0; i < _pendingRosterTokens.Count; i++)
                {
                    var def = _pendingRosterTokens[i];
                    if (def.Team != team)
                    {
                        continue;
                    }
                    hasAny = true;
                    int capturedIndex = i;
                    CreateListButton(container, def.Name, () => StartRosterTokenPlacement(capturedIndex));
                }

                if (_tokenSectionRoots.TryGetValue(team, out var sectionRoot))
                {
                    sectionRoot.SetActive(hasAny);
                }
            }
            RefreshListHeights();
        }

        /// <summary>예비대 유닛/토큰 두 목록의 박스 높이를 현재 항목 수 기준으로
        /// 다시 맞춘다 — 내용(버튼)은 안 건드리고 크기만. 두 Refresh*List()가
        /// 끝에서 공통으로 부른다. 예비대 목록의 캡은 토큰 유무와 무관하게
        /// 항상 UnitListExpandedMaxHeight다 — 한때 토큰이 있으면 더 좁은
        /// ListMaxHeight로 캡했었는데, 그러면 토큰 섹션이 있다는 이유만으로
        /// 패널 전체 세로 크기가 줄어드는 문제가 있었다(사용자 리포트). 토큰
        /// 섹션은 있으면 그 아래에 자기 몫(ListMaxHeight 캡, 5개 미만이면 더
        /// 줄어듦)만큼 추가로 붙을 뿐, 유닛 목록 크기에 영향을 주지 않는다.</summary>
        private void RefreshListHeights()
        {
            foreach (var kv in _pendingUnitsListLayoutElements)
            {
                string team = kv.Key;
                int unitCount = 0;
                foreach (var def in _pendingUnits)
                {
                    if (def.Team == team)
                    {
                        unitCount++;
                    }
                }
                ScrollListUtil.ApplyFittedHeight(kv.Value, unitCount, UnitListExpandedMaxHeight);
            }

            foreach (var kv in _rosterTokenListLayoutElements)
            {
                string team = kv.Key;
                int tokenCount = 0;
                foreach (var def in _pendingRosterTokens)
                {
                    if (def.Team == team)
                    {
                        tokenCount++;
                    }
                }
                ScrollListUtil.ApplyFittedHeight(kv.Value, tokenCount, ListMaxHeight);
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
