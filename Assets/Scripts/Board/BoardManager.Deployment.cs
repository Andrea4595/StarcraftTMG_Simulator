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
        // 예비대/토큰/택티컬 카드 목록만 보인다.
        private const float RosterImportButtonFillHeight = 400f;

        // 팀 패널을 위(예비대 유닛/토큰/유닛 상세) 60% : 아래(택티컬 카드)
        // 40%로 고정 분할한다(사용자 지정 — "위 아래로 나눌거야... 딱
        // 잡아줘"). 화면 크기에 따라 패널 자체 높이가 바뀌므로 픽셀이 아니라
        // 앵커 비율로 나눈다 — BuildTeamPanel 참고.
        private const float TacticalCardRegionFraction = 0.4f;

        // GameConstants.PendingPanelWidth/MarkerBarHeight로 옮겼다 — 지도
        // 뷰포트(화면 중앙, 팀 패널 사이 남는 영역) 경계를 알아야 하는
        // WeaponProfileDialog/DiceRollDialog 같은 독립 컴포넌트도 같은 값을
        // 참조해야 해서, 이 파일만의 private const로는 부족해졌다.

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
            // 화면 좌/우 가장자리에 딱 붙이고, 세로는 상단 바(스코어보드+페이즈바,
            // GameConstants.TopBarHeight)와 하단 바(마커바) 사이 구간에 꽉
            // 채운다 — X는 점 앵커(고정 폭은
            // sizeDelta.x), Y는 스트레치 앵커(0~1)로 두고 그 구간 높이만큼
            // sizeDelta.y를 음수로 줄여서 정확히 그 사이만 차지하게 한다.
            // anchoredPosition.y는 스크린샷 뷰 피봇과 같은 이유로 두 바
            // 높이가 다르니(84 vs 44) 그 차이의 절반만큼 보정한다.
            panel.anchorMin = new Vector2(xAnchor, 0f);
            panel.anchorMax = new Vector2(xAnchor, 1f);
            panel.pivot = new Vector2(xAnchor, 0.5f);
            panel.anchoredPosition = new Vector2(0f, (GameConstants.MarkerBarHeight - GameConstants.TopBarHeight) / 2f);
            panel.sizeDelta = new Vector2(GameConstants.PendingPanelWidth, -(GameConstants.TopBarHeight + GameConstants.MarkerBarHeight));

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            // panel 자신은 이제 순수 컨테이너다 — 위(예비대 유닛/토큰/유닛
            // 상세) 60% / 아래(택티컬 카드) 40%로 고정 분할한다(사용자 지정).
            // 화면 크기에 따라 panel 높이가 달라지므로 픽셀이 아니라 앵커
            // 비율(TacticalCardRegionFraction~1.0 / 0~TacticalCardRegionFraction)로
            // 나눈다 — 그래서 panel 자체엔 더 이상 VerticalLayoutGroup을
            // 안 붙이고, 아래 두 영역이 각자 자기 안에서만 레이아웃을 갖는다.
            var topRegionGo = new GameObject("TopRegion", typeof(RectTransform));
            topRegionGo.transform.SetParent(panel, false);
            var topRegion = (RectTransform)topRegionGo.transform;
            topRegion.anchorMin = new Vector2(0f, TacticalCardRegionFraction);
            topRegion.anchorMax = new Vector2(1f, 1f);
            topRegion.offsetMin = Vector2.zero;
            topRegion.offsetMax = Vector2.zero;
            var topLayout = topRegionGo.AddComponent<VerticalLayoutGroup>();
            topLayout.padding = new RectOffset(12, 12, 12, 6);
            topLayout.spacing = 8f;
            topLayout.childControlWidth = true;
            topLayout.childForceExpandWidth = true;
            topLayout.childControlHeight = true;
            topLayout.childForceExpandHeight = false;

            var bottomRegionGo = new GameObject("BottomRegion", typeof(RectTransform));
            bottomRegionGo.transform.SetParent(panel, false);
            var bottomRegion = (RectTransform)bottomRegionGo.transform;
            bottomRegion.anchorMin = new Vector2(0f, 0f);
            bottomRegion.anchorMax = new Vector2(1f, TacticalCardRegionFraction);
            bottomRegion.offsetMin = Vector2.zero;
            bottomRegion.offsetMax = Vector2.zero;
            var bottomLayout = bottomRegionGo.AddComponent<VerticalLayoutGroup>();
            bottomLayout.padding = new RectOffset(12, 12, 6, 12);
            bottomLayout.spacing = 8f;
            bottomLayout.childControlWidth = true;
            bottomLayout.childForceExpandWidth = true;
            bottomLayout.childControlHeight = true;
            bottomLayout.childForceExpandHeight = false;

            // 아직 로스터를 안 불러온 상태에선 이 버튼이 위쪽 60% 영역을 꽉
            // 채운 큰 호출 유도 버튼으로 보인다. OnRosterFileSelected가
            // 임포트에 성공하면 이 버튼 자체를 SetActive(false)로 완전히 숨긴다.
            _rosterImportButtons[team] = CreateListButton(topRegion, $"{team} 로스터 불러오기", () => ImportRoster(team), RosterImportButtonFillHeight);

            // 예비대 유닛 목록 — 항목 수만큼 핏하게 커지다가 UnitListExpandedMaxHeight
            // 에서 스크롤로 전환된다(토큰 섹션 유무와 무관). 실제 높이는
            // RefreshPendingList()가 매번 다시 계산해서 적용한다.
            _pendingUnitsListContainers[team] = ScrollListUtil.Create(topRegion, ListMaxHeight, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _, out var unitListLayoutElement);
            _pendingUnitsListLayoutElements[team] = unitListLayoutElement;
            unitListLayoutElement.gameObject.SetActive(false); // RefreshPanelLayout()이 로드 여부에 따라 켠다.

            // 토큰 목록 — 유닛과 달리 배치해도 목록에서 안 지워진다(몇 번이든 재배치 가능).
            // 라벨+목록을 한 래퍼에 담아서 통째로 켜고 끌 수 있게 한다 —
            // 이 팀에 로스터로 들어온 토큰이 하나도 없으면 RefreshRosterTokenList가
            // 이 래퍼 자체를 꺼서 빈 "토큰" 제목만 덩그러니 남는 걸 막는다.
            var tokenSectionGo = new GameObject("TokenSection", typeof(RectTransform));
            tokenSectionGo.transform.SetParent(topRegion, false);
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

            // 유닛 상세 패널 — topRegion(위쪽 60%) 안에서만 예비대 목록 등을
            // 가리고 나타난다(사용자 지정: "유닛 상세 정보는 유닛 리스트를
            // 보여주는 칸만 할애해서 보여주도록 해") — 아래쪽 40%(택티컬
            // 카드)는 그대로 둔다. 평소엔 비활성 — BoardManager.UnitDetail.cs의
            // UpdateUnitDetailPanel()이 켜고 끈다.
            var detailContent = ScrollListUtil.Create(topRegion, 200f, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _, out var detailLayoutElement);
            detailLayoutElement.flexibleHeight = 1f;
            _unitDetailContainers[team] = detailContent;
            _unitDetailLayoutElements[team] = detailLayoutElement;
            detailLayoutElement.gameObject.SetActive(false);

            // 택티컬 카드 — 이제 아래쪽 40% 영역(bottomRegion) 그 자체가 이
            // 섹션이다(고정 분할 영역이라 예전처럼 "카드가 없으면 통째로
            // 숨기기"는 하지 않는다 — 항상 그 자리를 차지하는 게 사용자
            // 지정("딱 잡아줘")의 취지에 맞는다. 라벨은 항상 보이고, 목록은
            // 카드가 없으면 그냥 빈 채로 보인다).
            CreateSectionLabel(bottomRegion, "택티컬 카드");
            _tacticalCardListContainers[team] = ScrollListUtil.Create(bottomRegion, ListMaxHeight, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _, out var tacticalListLayoutElement);
            tacticalListLayoutElement.flexibleHeight = 1f;
            _tacticalCardListLayoutElements[team] = tacticalListLayoutElement;
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
            RefreshPanelLayout();
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
            RefreshPanelLayout();
        }

        /// <summary>택티컬 카드 목록을 다시 그린다 — 임포트 때만 불린다(카드
        /// 자체의 좌/우클릭 소진·복구는 버튼을 다시 그리지 않고 핍 색만
        /// 바로 바꾼다, RefreshTacticalCardPips 참고). 이 섹션은 이제 고정
        /// 40% 영역(BottomRegion) 그 자체라 카드가 하나도 없어도 통째로
        /// 숨기지 않는다(예전엔 토큰 섹션처럼 접었었다) — "딱 잡아준" 분할을
        /// 유지하는 게 사용자 지정 취지.</summary>
        private void RefreshTacticalCardList()
        {
            foreach (var kv in _tacticalCardListContainers)
            {
                string team = kv.Key;
                var container = kv.Value;
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    Destroy(container.GetChild(i).gameObject);
                }

                foreach (var def in _pendingTacticalCards)
                {
                    if (def.Team != team)
                    {
                        continue;
                    }
                    CreateTacticalCardButton(container, def);
                    RenderTacticalCardAbilities(container, def);
                }
            }
        }

        /// <summary>카드 버튼 바로 아래에, 그 카드의 능력 이름을 들여쓰기해서
        /// 나열한다(Document/택티컬 카드 리스트.png) — 이름을 클릭하면
        /// 펼쳐지며 정보가 드러난다(사용자 지정). 유닛 능력과 완전히 같은
        /// 모양(kind/name/phase/type/cost/rule)이라 BoardManager.UnitDetail.cs의
        /// RenderAbility를 그대로 재사용한다(같은 파셜 클래스라 바로 호출
        /// 가능) — 페이즈 아이콘/타입별 색상/클릭-펼치기까지 전부 동일하게
        /// 동작한다. showCost: false만 다르게 넘긴다 — 택티컬 카드 능력은
        /// 사용에 비용이 안 들어서(사용자 지정) 로스터에 "cost" 값이 있어도
        /// 헤더에 (코스트)를 안 보여준다.</summary>
        private void RenderTacticalCardAbilities(Transform parent, TacticalCardDef def)
        {
            if (def.Abilities.Count == 0)
            {
                return;
            }

            var wrapperGo = new GameObject($"Abilities_{def.Name}", typeof(RectTransform));
            wrapperGo.transform.SetParent(parent, false);
            var wrapperLayout = wrapperGo.AddComponent<VerticalLayoutGroup>();
            wrapperLayout.padding = new RectOffset(24, 0, 4, 4);
            wrapperLayout.spacing = 4f;
            wrapperLayout.childControlWidth = true;
            wrapperLayout.childForceExpandWidth = true;
            wrapperLayout.childControlHeight = true;
            wrapperLayout.childForceExpandHeight = false;
            var wrapperFitter = wrapperGo.AddComponent<ContentSizeFitter>();
            wrapperFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var ability in def.Abilities)
            {
                RenderAbility(wrapperGo.transform, ability, showCost: false);
            }
        }

        /// <summary>카드 이름 + 보유 매수만큼의 핍(Resources/UI/TacticalCount.png)을
        /// 담은 버튼 하나. 좌클릭하면 핍을 하나 소진(회색으로)하고, 이미 다
        /// 소진된 상태에서 좌클릭하면 전부 복구된다. 우클릭은 반대 —
        /// 하나씩 복구하다가, 이미 꽉 찬 상태에서 우클릭하면 전부
        /// 소진된다(사용자 요청).</summary>
        private void CreateTacticalCardButton(Transform parent, TacticalCardDef def)
        {
            var btnGo = new GameObject($"Card_{def.Name}", typeof(RectTransform));
            btnGo.transform.SetParent(parent, false);
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 32f;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = TacticalCardNormalColor;

            var layout = btnGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 6, 4, 4);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(btnGo.transform, false);
            var nameLabel = nameGo.AddComponent<TextMeshProUGUI>();
            nameLabel.text = def.Name;
            nameLabel.fontSize = 13f;
            nameLabel.color = Color.white;
            nameLabel.raycastTarget = false;
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            nameLabel.overflowMode = TextOverflowModes.Truncate;
            var nameLe = nameGo.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f; // 이름이 남는 폭을 먹고, 핍은 오른쪽에 자기 크기만.

            // 종족 포인트(테란 CP/저그 BM/프로토스 EN) 제공량 — 로스터에
            // resource_label이 없는 구형 파일은 ResourceAbbr가 빈 문자열이라
            // 자동으로 표시를 건너뛴다(사용자 지정 — 카드에 CP 제공량 기재).
            if (!string.IsNullOrEmpty(def.ResourceAbbr))
            {
                var resourceGo = new GameObject("Resource", typeof(RectTransform));
                resourceGo.transform.SetParent(btnGo.transform, false);
                var resourceLabel = resourceGo.AddComponent<TextMeshProUGUI>();
                resourceLabel.text = $"{def.ResourceAbbr} {def.ResourceAmount}";
                resourceLabel.fontSize = 12f;
                resourceLabel.color = new Color(0.7f, 0.75f, 0.85f, 1f);
                resourceLabel.alignment = TextAlignmentOptions.MidlineRight;
                resourceLabel.raycastTarget = false;
                resourceLabel.textWrappingMode = TextWrappingModes.NoWrap;
                var resourceLe = resourceGo.AddComponent<LayoutElement>();
                resourceLe.preferredWidth = 40f;
            }

            var pipsGo = new GameObject("Pips", typeof(RectTransform));
            pipsGo.transform.SetParent(btnGo.transform, false);
            var pipsLayout = pipsGo.AddComponent<HorizontalLayoutGroup>();
            pipsLayout.spacing = 2f;
            pipsLayout.childAlignment = TextAnchor.MiddleRight;
            pipsLayout.childControlWidth = false;
            pipsLayout.childControlHeight = false;
            pipsLayout.childForceExpandWidth = false;
            pipsLayout.childForceExpandHeight = false;
            var pipsLe = pipsGo.AddComponent<LayoutElement>();
            pipsLe.preferredWidth = def.Count * 12f + Mathf.Max(0, def.Count - 1) * 2f;

            var pipTexture = Resources.Load<Texture2D>("UI/TacticalCount");
            var pips = new List<RawImage>();
            for (int i = 0; i < def.Count; i++)
            {
                var pipGo = new GameObject("Pip", typeof(RectTransform));
                pipGo.transform.SetParent(pipsGo.transform, false);
                var pipRect = (RectTransform)pipGo.transform;
                pipRect.sizeDelta = new Vector2(12f, 20f); // 원본(297x525) 비율에 가깝게.
                var pipImg = pipGo.AddComponent<RawImage>();
                pipImg.texture = pipTexture;
                pipImg.raycastTarget = false;
                pips.Add(pipImg);
            }
            RefreshTacticalCardVisual(btnImg, nameLabel, pips, def);

            var handler = btnGo.AddComponent<TacticalCardClickHandler>();
            handler.OnLeftClick = () =>
            {
                def.Remaining = def.Remaining > 0 ? def.Remaining - 1 : def.Count;
                RefreshTacticalCardVisual(btnImg, nameLabel, pips, def);
            };
            handler.OnRightClick = () =>
            {
                def.Remaining = def.Remaining < def.Count ? def.Remaining + 1 : 0;
                RefreshTacticalCardVisual(btnImg, nameLabel, pips, def);
            };
        }

        private static readonly Color TacticalCardNormalColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color TacticalCardExhaustedColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color TacticalCardNameNormalColor = Color.white;
        private static readonly Color TacticalCardNameDimColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color TacticalPipUsedColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        /// <summary>핍 색뿐 아니라 버튼 배경/이름 폰트 색도 같이 갱신한다 — 다
        /// 소진되면(Remaining==0) 카드 버튼 전체가 어두워지는데, 이때 흰색
        /// 폰트만 혼자 튀지 않도록 폰트도 같이 어둡게 낮춘다(사용자 요청).</summary>
        private static void RefreshTacticalCardVisual(Image btnImg, TextMeshProUGUI nameLabel, List<RawImage> pips, TacticalCardDef def)
        {
            bool exhausted = def.Remaining <= 0;
            int used = def.Count - def.Remaining;
            for (int i = 0; i < pips.Count; i++)
            {
                pips[i].color = i < used ? TacticalPipUsedColor : Color.white;
            }
            btnImg.color = exhausted ? TacticalCardExhaustedColor : TacticalCardNormalColor;
            nameLabel.color = exhausted ? TacticalCardNameDimColor : TacticalCardNameNormalColor;
        }

        /// <summary>택티컬 카드 버튼 하나에만 붙는다 — Button은 좌클릭만 다룰 수
        /// 있어서, 좌/우클릭을 둘 다 받으려면 IPointerDownHandler를 직접
        /// 구현해야 한다(MarkerBase/MissionObjectivePiece와 같은 패턴).</summary>
        private class TacticalCardClickHandler : MonoBehaviour, IPointerDownHandler
        {
            public System.Action OnLeftClick;
            public System.Action OnRightClick;

            public void OnPointerDown(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    OnLeftClick?.Invoke();
                }
                else if (eventData.button == PointerEventData.InputButton.Right)
                {
                    OnRightClick?.Invoke();
                }
            }
        }

        /// <summary>팀별로 (1) "로스터 불러오기" 큰 버튼 vs 예비대 유닛 목록 중
        /// 뭘 보여줄지 정하고, (2) 목록 박스 높이를 현재 항목 수에 맞게 다시
        /// 잡는다. 두 Refresh*List()가 끝에서 공통으로 부른다.
        ///
        /// 로드 판정(_rosterLoadedTeams)은 한 번 켜지면 계속 유지되는
        /// 단방향 스위치다 — 예비대를 전부 배치해서 목록이 다시 비어도 큰
        /// 버튼이 되살아나면 안 되기 때문(사용자 요청). 임포트 성공 시
        /// OnRosterFileSelected가 직접 이 집합에 team을 추가하고, 여기서는
        /// 그 집합에 없더라도 "이미 예비대/토큰이 있는" 팀은 로드된 것으로
        /// 취급해 자동으로 편입한다 — 부트스트랩의 AddPendingUnit()이
        /// Start() 이전에 미리 예비대를 채워 넣는 경로(임포트를 거치지
        /// 않음)까지 같이 커버하기 위해서다.
        ///
        /// 예비대 목록의 높이 캡은 토큰 유무와 무관하게 항상
        /// UnitListExpandedMaxHeight다 — 한때 토큰이 있으면 더 좁은
        /// ListMaxHeight로 캡했었는데, 그러면 토큰 섹션이 있다는 이유만으로
        /// 패널 전체 세로 크기가 줄어드는 문제가 있었다(사용자 리포트). 토큰
        /// 섹션은 있으면 그 아래에 자기 몫(ListMaxHeight 캡, 5개 미만이면 더
        /// 줄어듦)만큼 추가로 붙을 뿐, 유닛 목록 크기에 영향을 주지 않는다.</summary>
        private void RefreshPanelLayout()
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
                int tokenCount = 0;
                foreach (var def in _pendingRosterTokens)
                {
                    if (def.Team == team)
                    {
                        tokenCount++;
                    }
                }

                if (!_rosterLoadedTeams.Contains(team) && (unitCount > 0 || tokenCount > 0))
                {
                    _rosterLoadedTeams.Add(team);
                }
                bool loaded = _rosterLoadedTeams.Contains(team);

                if (_rosterImportButtons.TryGetValue(team, out var importButton))
                {
                    importButton.SetActive(!loaded);
                }
                kv.Value.gameObject.SetActive(loaded);
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
            BeginUndoTransaction($"{_pendingUnits[index].Team} {_pendingUnits[index].Name} 배치");
            var def = _pendingUnits[index];
            RemovePendingUnitDefAt(index);

            _pendingDeploymentDef = def;
            ShowBasePlacementPreview(def.SizeMm, def.FillColor, def.IsDisplacement);
            ShowDeploymentBand(def);
        }

        private void HandlePendingDeploymentInput()
        {
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi())
            {
                // 빈 곳 우클릭 — 배치 취소, 정의를 예비대 목록으로 되돌린다.
                AddPendingUnitDef(_pendingDeploymentDef);
                _pendingDeploymentDef = null;
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
                // 클릭 전 고스트 미리보기 단계에서도 Shift를 누르면 밴드
                // 경계에 스냅되어야 한다 — 가이드라인을 보여주며 마우스를
                // 따라다니는 고스트가 실제 배치 결과를 미리 보여주는 것이므로,
                // 실제 클릭(BeginDeploymentDrag)에서만 스냅되면 미리보기와
                // 실제 배치 위치가 어긋나 보인다(사용자 지적). 고스트의 현재
                // 회전(휠로 돌렸을 수 있다 — HandlePanAndZoom 참고)을 그대로
                // 스냅 계산에 반영한다.
                float previewRotation = _placementPreview != null ? _placementPreview.RotationRadians : 0f;
                UpdatePlacementPreviewPosition(SnapToGuidelineBoundary(mouseLocal, _pendingDeploymentDef.SizeMm, previewRotation));
            }
        }

    }
}
