using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 유닛 상세 패널 ────────────────────────────────────────────────
        // 두 가지 트리거로 뜬다(사용자 스펙): (1) 예비대에서 유닛 버튼을
        // 클릭한 뒤부터 팔로워 배치까지 배치 프로세스가 완전히 끝날 때까지
        // (_pendingDeploymentDef / _unitMoveActive+_unitMoveIsDeployment),
        // (2) 이미 배치된 유닛을 우클릭해 다이얼 메뉴가 떠 있는 동안
        // (radialMenu.gameObject.activeSelf && _menuTarget), (3) 유닛을
        // 좌클릭(_selectedUnitForDetailByTeam). 매 프레임 폴링하되(Update()에서
        // 호출), 실제 다시 그리기는 "표시 대상이 바뀔 때"만 한다
        // (_unitDetailLastSourceByTeam 참고) — 다이얼 메뉴가 "범위 표시" 같은
        // 하위 메뉴로 넘어가며 같은 프레임 안에서 Close() 후 다시 Open()되는
        // 경우, 폴링이 프레임 경계에서만 관찰하므로 그 사이의 순간적인 비활성
        // 상태를 놓쳐 자연히 패널이 안 깜빡인다.
        //
        // A팀/B팀 패널은 서로 완전히 독립이다(사용자 요청: 주사위 시뮬레이터로
        // 굴리면서 공격자의 무기 프로필과 수비자의 방어/회피 정보를 동시에
        // 봐야 한다 — 그러려면 두 팀 패널이 각자 다른 유닛을 동시에 보여줄
        // 수 있어야 한다) — 그래서 위 세 트리거 전부 team별로 따로 계산한다.
        //
        // 토큰(Unit.IsToken)과 애드혹 유닛(Unit.Detail==null/PendingUnitDef.
        // Detail==null)은 대상 밖 — 후자는 이름/모델 수 등 기본 정보만
        // 보여준다(사용자 스펙: "유닛"이라고 명시했으므로 토큰 제외).

        private void UpdateUnitDetailPanel()
        {
            foreach (var team in _unitDetailContainers.Keys)
            {
                UpdateUnitDetailPanelForTeam(team);
            }
        }

        private void UpdateUnitDetailPanelForTeam(string team)
        {
            object source = null;

            if (_pendingDeploymentDef != null && _pendingDeploymentDef.Team == team)
            {
                source = _pendingDeploymentDef;
            }
            else if (_unitMoveActive && _unitMoveIsDeployment && _unitMoveUnit != null && _unitMoveUnit.Team == team)
            {
                source = _unitMoveUnit;
            }
            else if (radialMenu != null && radialMenu.gameObject.activeSelf
                    && _menuTarget != null && _menuTarget.Unit != null && !_menuTarget.Unit.IsToken
                    && _menuTarget.Unit.Team == team)
            {
                source = _menuTarget.Unit;
            }
            else if (_selectedUnitForDetailByTeam.TryGetValue(team, out var selected) && selected != null)
            {
                // 좌클릭 선택 — 가장 낮은 우선순위. 우클릭으로 다이얼 메뉴가
                // 열리는 순간(OnMenuRequested) 이 team의 값은 이미 지워지므로
                // (사용자 지정: "A 보던 것은 끄고 B를 보고 있는 것"), 메뉴가
                // 닫힌 뒤 여기로 도로 떨어져 좌클릭 선택이 되살아나는 일은
                // 없다 — 우클릭 스펙("메뉴 선택하면 꺼짐")이 항상 그대로 지켜진다.
                source = selected;
            }

            bool hadLast = _unitDetailLastSourceByTeam.TryGetValue(team, out var lastSource) && lastSource != null;

            if (source == null)
            {
                if (hadLast)
                {
                    HideUnitDetail(team);
                    _unitDetailLastSourceByTeam[team] = null;
                }
                return;
            }

            // source(Unit)는 살아있는 객체라 같은 참조를 계속 보여주는 동안에도
            // Models.Count가 바뀔 수 있다(예: 배치 중 팔로워가 나중에 붙는
            // 경우, 또는 이후 모델이 죽거나 복제되는 경우) — 참조만 비교하면
            // 이런 변화를 놓쳐서 스쿼드 단계 하이라이트 등이 낡은 값으로
            // 굳어버린다(실제로 발생한 버그: 배치 직후 모델 2개인데 1개일
            // 때의 단계로 표시됨 — 리딩 모델만 있던 순간에 그려진 뒤 팔로워가
            // 붙어도 다시 안 그려졌던 것). 그래서 참조 동일성과 모델 수를
            // 함께 확인한다.
            int currentModelCount = source is PendingUnitDef pd ? pd.ModelCount : ((Unit)source).Models.Count;
            bool modelCountUnchanged = _unitDetailLastModelCountByTeam.TryGetValue(team, out var lastModelCount)
                    && lastModelCount == currentModelCount;

            if (hadLast && ReferenceEquals(source, lastSource) && modelCountUnchanged)
            {
                return; // 이미 이 대상으로, 같은 모델 수로 그려져 있다 — 매 프레임 다시 그릴 필요 없다.
            }

            var vm = source is PendingUnitDef pendingDef
                    ? ViewModelFromPending(pendingDef)
                    : ViewModelFromUnit((Unit)source);
            ShowUnitDetail(team, vm);
            _unitDetailLastSourceByTeam[team] = source;
            _unitDetailLastModelCountByTeam[team] = currentModelCount;
        }

        private void ShowUnitDetail(string team, UnitDetailViewModel vm)
        {
            if (!_unitDetailContainers.TryGetValue(team, out var content)
                    || !_unitDetailLayoutElements.TryGetValue(team, out var detailLe))
            {
                return;
            }

            RenderUnitDetail(content, vm);
            SuppressNormalPanelSections(team);
            detailLe.gameObject.SetActive(true);
        }

        /// <summary>평소 예비대 목록/토큰 섹션(TopRegion, 위쪽 60%)을 가리고
        /// 상세 패널만 보이게 한다 — 택티컬 카드(BottomRegion, 아래쪽 40%)는
        /// 고정 분할 영역이라 손대지 않는다(사용자 지정: "유닛 상세 정보는
        /// 유닛 리스트를 보여주는 칸만 할애해서 보여주도록 해"). ShowUnitDetail이
        /// 직접 부르고, HideUnitDetail도 다른 team의 상태를 지키기 위해 다시
        /// 부른다(아래 참고).</summary>
        private void SuppressNormalPanelSections(string team)
        {
            if (_rosterImportButtons.TryGetValue(team, out var importButton))
            {
                importButton.SetActive(false);
            }
            if (_pendingUnitsListLayoutElements.TryGetValue(team, out var unitListLe))
            {
                unitListLe.gameObject.SetActive(false);
            }
            if (_tokenSectionRoots.TryGetValue(team, out var tokenRoot))
            {
                tokenRoot.SetActive(false);
            }
        }

        /// <summary>평소 예비대 패널 상태를 되살린다 — "로드됨"/개수에 따른
        /// 표시 여부는 그 자체로 상태가 있는 로직이라(RefreshPanelLayout 등)
        /// 여기서 새로 베끼지 않고 기존 Refresh*() 파이프라인을 그냥 다시
        /// 부른다. RefreshPendingList/RefreshRosterTokenList는 "두 팀 다"
        /// 훑으며 예비대/토큰 섹션을 되살리므로(팀별로 분리돼 있지 않음) —
        /// 지금 끄는 건 이 team뿐인데, 그 사이 다른 team이 여전히 자기 유닛
        /// 상세를 보여주는 중이었다면 그 team의 목록 섹션까지 덩달아 되살아나
        /// 버려서 "목록 + 상세가 같이 보이는" 실제 버그가 났었다. 그래서
        /// Refresh 이후 다른 team 중 지금도 상세를 보여주고 있는 team이 있으면
        /// 그 섹션들을 다시 눌러 끈다. RefreshTacticalCardList는 더 이상 여기서
        /// 부르지 않는다 — 택티컬 카드(BottomRegion)는 유닛 상세 표시와
        /// 무관하게 항상 그대로이므로 건드릴 이유가 없다.</summary>
        private void HideUnitDetail(string team)
        {
            if (_unitDetailLayoutElements.TryGetValue(team, out var detailLe))
            {
                detailLe.gameObject.SetActive(false);
            }
            RefreshPendingList();
            RefreshRosterTokenList();

            foreach (var kv in _unitDetailLastSourceByTeam)
            {
                if (kv.Key != team && kv.Value != null)
                {
                    SuppressNormalPanelSections(kv.Key);
                }
            }
        }

        private class UnitDetailViewModel
        {
            public string Name;
            public string Team;
            public int ModelCount;
            public Vector2 SizeMm;
            public bool CanMove;
            public float MoveInch;
            public float CoherencyInch;
            public bool IsDisplacement;
            public List<SupplyTier> SupplyTiers;
            public int? SupplyOverride;
            public int CurrentSupply;
            public List<RangeSpec> Ranges;
            public RosterUnitDetail Detail;
        }

        private static UnitDetailViewModel ViewModelFromPending(PendingUnitDef def)
        {
            return new UnitDetailViewModel
            {
                Name = def.Name,
                Team = def.Team,
                ModelCount = def.ModelCount,
                SizeMm = def.SizeMm,
                CanMove = def.CanMove,
                MoveInch = def.MoveInch,
                CoherencyInch = def.CoherencyInch,
                IsDisplacement = def.IsDisplacement,
                SupplyTiers = def.SupplyTiers,
                SupplyOverride = def.SupplyOverride,
                CurrentSupply = ComputeSupplyForCount(def.SupplyTiers, def.SupplyOverride, def.ModelCount),
                Ranges = def.Ranges,
                Detail = def.Detail,
            };
        }

        private UnitDetailViewModel ViewModelFromUnit(Unit unit)
        {
            var leading = unit.Models.Count > 0 ? unit.Models[0] : null;
            return new UnitDetailViewModel
            {
                Name = unit.UnitName,
                Team = unit.Team,
                ModelCount = unit.Models.Count,
                SizeMm = leading != null ? leading.SizeMm : Vector2.zero,
                CanMove = unit.CanMove,
                MoveInch = unit.MoveInch,
                CoherencyInch = unit.CoherencyInch,
                IsDisplacement = leading != null && leading.IsDisplacement,
                SupplyTiers = unit.SupplyTiers,
                SupplyOverride = unit.SupplyOverride,
                CurrentSupply = unit.CurrentSupplyCost(),
                Ranges = _unitRanges.TryGetValue(unit, out var ranges) ? ranges : new List<RangeSpec>(),
                Detail = unit.Detail,
            };
        }

        private static int ComputeSupplyForCount(List<SupplyTier> tiers, int? supplyOverride, int count)
        {
            if (supplyOverride.HasValue)
            {
                return supplyOverride.Value;
            }
            foreach (var tier in tiers)
            {
                if (count >= tier.ModelMin && count <= tier.ModelMax)
                {
                    return tier.Supply;
                }
            }
            return 0;
        }

        private void RenderUnitDetail(RectTransform content, UnitDetailViewModel vm)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }

            var detail = vm.Detail;
            string title = detail != null ? KoPreferred(detail.NameKo, detail.NameEn) : vm.Name;
            if (string.IsNullOrEmpty(title))
            {
                title = vm.Name;
            }
            AddDetailText(content, title, 18f, Color.white, FontStyles.Bold);

            if (detail != null && detail.Tags.Count > 0)
            {
                var tagTexts = new List<string>();
                foreach (var tag in detail.Tags)
                {
                    tagTexts.Add(KoPreferred(tag.NameKo, tag.NameEn));
                }
                AddDetailText(content, string.Join(", ", tagTexts), 12f, new Color(0.85f, 0.85f, 0.85f, 1f));
            }

            if (vm.IsDisplacement)
            {
                AddDetailText(content, "변위 유닛", 13f, new Color(1f, 0.8f, 0.4f, 1f));
            }

            RenderUnitStatRow(content, detail, vm.CanMove, vm.MoveInch, vm.CoherencyInch);

            if (vm.SupplyTiers != null && vm.SupplyTiers.Count > 0)
            {
                RenderSquadTiers(content, vm.SupplyTiers, vm.ModelCount);
            }

            if (vm.Ranges != null && vm.Ranges.Count > 0)
            {
                AddDetailSectionLabel(content, "사거리");
                foreach (var range in vm.Ranges)
                {
                    AddDetailText(content, $"{range.Inch:0.#}\" ({(range.AlwaysShow ? "상시 표시" : "호버 시 표시")})", 12f, new Color(0.85f, 0.85f, 0.85f, 1f));
                }
            }

            if (detail != null && detail.Abilities.Count > 0)
            {
                RenderAbilitiesSection(content, detail.Abilities);
            }
        }

        /// <summary>weapon-kind 항목은 더 이상 하나씩 그리지 않는다 — 사용자 지정:
        /// 원거리(Assault 페이즈) 무기가 있으면 그걸 전부 모아 "사격"이라는
        /// 합성 액티브 능력 하나로, 근거리(Combat 페이즈) 무기가 있으면 "근접
        /// 공격"이라는 합성 능력 하나로 묶는다. 둘 다 클릭하면 곧바로(펼치기
        /// 단계 없이) WeaponProfileDialog가 그 카테고리의 모든 무기를 표로
        /// 종합해서 보여준다 — rule-kind 능력의 "클릭→펼치기→룰 텍스트"와는
        /// 다른, 더 직접적인 상호작용이다(사용자 지정: "이 능력을 클릭하면
        /// ... 모달을 띄워"). 이 프로젝트의 로스터 데이터에서 weapon-kind는
        /// 실제로 phase가 Assault/Combat 둘 중 하나뿐이라(다른 phase의 무기는
        /// 관측된 적 없음) 이 매핑이 안전하다.
        ///
        /// rule 능력과 두 합성 능력을 전부 하나의 목록으로 합친 뒤 페이즈
        /// 순서(Any > Movement > Assault > Combat, 사용자 지정)로 다시
        /// 정렬한다 — LINQ의 OrderBy는 안정 정렬이라 같은 페이즈 안에서는
        /// 로스터 JSON에 나온 원래 순서가 그대로 유지된다.</summary>
        private void RenderAbilitiesSection(Transform content, List<RosterAbilityEntry> abilities)
        {
            var items = new List<AbilityRenderItem>();
            var rangedWeapons = new List<(string Name, RosterWeaponStat Weapon)>();
            var meleeWeapons = new List<(string Name, RosterWeaponStat Weapon)>();

            foreach (var ability in abilities)
            {
                if (ability.Kind == "weapon" && ability.Weapon != null)
                {
                    string weaponName = KoPreferred(ability.NameKo, ability.NameEn);
                    if (ability.Phase == "Assault")
                    {
                        rangedWeapons.Add((weaponName, ability.Weapon));
                    }
                    else if (ability.Phase == "Combat")
                    {
                        meleeWeapons.Add((weaponName, ability.Weapon));
                    }
                    // 그 외 phase의 weapon 항목은 관측된 적 없음 — 안전하게 건너뜀(표시 누락보다는
                    // 잘못된 분류가 더 나쁘다).
                }
                else
                {
                    items.Add(new AbilityRenderItem { Phase = ability.Phase, Rule = ability });
                }
            }

            if (rangedWeapons.Count > 0)
            {
                items.Add(new AbilityRenderItem
                {
                    Phase = "Assault",
                    TriggerName = "사격",
                    TriggerWeapons = rangedWeapons,
                });
            }
            if (meleeWeapons.Count > 0)
            {
                items.Add(new AbilityRenderItem
                {
                    Phase = "Combat",
                    TriggerName = "근접 공격",
                    TriggerWeapons = meleeWeapons,
                });
            }

            if (items.Count == 0)
            {
                return;
            }

            AddDivider(content);
            foreach (var item in items.OrderBy(i => PhaseOrder(i.Phase)))
            {
                if (item.Rule != null)
                {
                    RenderAbility(content, item.Rule);
                }
                else
                {
                    RenderWeaponProfileTrigger(content, item.TriggerName, item.Phase, item.TriggerWeapons);
                }
            }
        }

        private class AbilityRenderItem
        {
            public string Phase;
            public RosterAbilityEntry Rule; // null이면 아래 Trigger* 필드로 채워진 합성 무기 프로필 항목.
            public string TriggerName;
            public List<(string Name, RosterWeaponStat Weapon)> TriggerWeapons;
        }

        private static int PhaseOrder(string phase)
        {
            switch (phase)
            {
                case "Any":
                    return 0;
                case "Movement":
                    return 1;
                case "Assault":
                    return 2;
                case "Combat":
                    return 3;
                default:
                    return 4;
            }
        }

        private static void AddDivider(Transform parent)
        {
            var go = new GameObject("Divider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 1f;
            le.minHeight = 1f;
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.15f);
            img.raycastTarget = false;
        }

        /// <summary>SHLD/SPD/EVA/ARM/HP/SIZ 6개를 한 줄에 값(위)+라벨(아래) 칸으로
        /// 나열한다(Document/유닛 스탯 표시 방법.png). SPD만 "이동력/이동력+코히런시"
        /// 비율로 특별 취급 — 나머지는 로스터 stat 값 그대로, 없으면(null) "-".</summary>
        private static void RenderUnitStatRow(Transform parent, RosterUnitDetail detail, bool canMove, float moveInch, float coherencyInch)
        {
            var rowGo = new GameObject("StatRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 14f;
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            string spdText = canMove ? $"{moveInch:0.#}/{moveInch + coherencyInch:0.#}" : "-";
            AddStatColumn(rowGo.transform, "SHLD", DashIfEmpty(detail?.Shield));
            AddStatColumn(rowGo.transform, "SPD", spdText);
            AddStatColumn(rowGo.transform, "EVA", DashIfEmpty(detail?.Evasion));
            AddStatColumn(rowGo.transform, "ARM", DashIfEmpty(detail?.Armor));
            AddStatColumn(rowGo.transform, "HP", DashIfEmpty(detail?.Hp));
            AddStatColumn(rowGo.transform, "SIZ", DashIfEmpty(detail?.Size));
        }

        private static string DashIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        private static void AddStatColumn(Transform parent, string label, string value)
        {
            var colGo = new GameObject($"Stat_{label}", typeof(RectTransform));
            colGo.transform.SetParent(parent, false);
            var colLayout = colGo.AddComponent<VerticalLayoutGroup>();
            colLayout.spacing = 2f;
            colLayout.childAlignment = TextAnchor.MiddleCenter;
            colLayout.childControlWidth = true;
            colLayout.childControlHeight = true;
            colLayout.childForceExpandWidth = false;
            colLayout.childForceExpandHeight = false;
            var colFitter = colGo.AddComponent<ContentSizeFitter>();
            colFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            colFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var valueGo = new GameObject("Value", typeof(RectTransform));
            valueGo.transform.SetParent(colGo.transform, false);
            var valueLabel = valueGo.AddComponent<TextMeshProUGUI>();
            valueLabel.text = value;
            valueLabel.fontSize = 14f;
            valueLabel.color = Color.white;
            valueLabel.fontStyle = FontStyles.Bold;
            valueLabel.alignment = TextAlignmentOptions.Center;
            valueLabel.enableWordWrapping = false;
            valueLabel.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(colGo.transform, false);
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 10f;
            labelText.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.enableWordWrapping = false;
            labelText.raycastTarget = false;
        }

        /// <summary>스쿼드(서플라이 단계표)를 파란 네모(서플라이 1개당 하나, 0이면
        /// ×) + 그 단계의 최대 모델 수로 이루어진 "알약" 모양으로 가로 나열한다
        /// (Document/스쿼드 표시 방법.png). supply_override는 일부러 무시하고
        /// 항상 tier.Supply(로스터 원본 값)를 그린다 — 사용자 지정: "여기서는
        /// supply_override 값을 무시하고, 유닛 데이터 본연의 정보를 표시".
        /// currentModelCount가 속한 단계는 흰 테두리로 표시한다("선택된 단계"
        /// 텍스트 대신) — squad_tier_index(로스터 작성 시점의 참고값)가 아니라
        /// 이 유닛이 지금 실제로 가진 모델 수로 매번 다시 찾는다(다른 서플라이
        /// 계산과 동일한 원칙).</summary>
        private static void RenderSquadTiers(Transform parent, List<SupplyTier> tiers, int currentModelCount)
        {
            var rowGo = new GameObject("SquadTierRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            foreach (var tier in tiers)
            {
                var pillGo = new GameObject($"Tier_{tier.ModelMin}_{tier.ModelMax}", typeof(RectTransform));
                pillGo.transform.SetParent(rowGo.transform, false);
                var pillImg = pillGo.AddComponent<Image>();
                pillImg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
                var pillLayout = pillGo.AddComponent<HorizontalLayoutGroup>();
                pillLayout.padding = new RectOffset(8, 8, 4, 4);
                pillLayout.spacing = 3f;
                pillLayout.childAlignment = TextAnchor.MiddleLeft;
                pillLayout.childControlWidth = true;
                pillLayout.childControlHeight = true;
                pillLayout.childForceExpandWidth = false;
                pillLayout.childForceExpandHeight = false;
                var pillFitter = pillGo.AddComponent<ContentSizeFitter>();
                pillFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                pillFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                if (tier.Supply <= 0)
                {
                    AddSquadTierGlyph(pillGo.transform, "×", new Color(0.6f, 0.6f, 0.6f, 1f));
                }
                else
                {
                    for (int i = 0; i < tier.Supply; i++)
                    {
                        var sqGo = new GameObject("Supply", typeof(RectTransform));
                        sqGo.transform.SetParent(pillGo.transform, false);
                        var sqImg = sqGo.AddComponent<Image>();
                        sqImg.color = new Color(0.25f, 0.5f, 0.95f, 1f);
                        var sqLe = sqGo.AddComponent<LayoutElement>();
                        sqLe.preferredWidth = 14f;
                        sqLe.preferredHeight = 14f;
                    }
                }

                AddSquadTierGlyph(pillGo.transform, tier.ModelMax.ToString(), Color.white);

                if (currentModelCount >= tier.ModelMin && currentModelCount <= tier.ModelMax)
                {
                    AddPillBorder(pillGo);
                }
            }
        }

        /// <summary>레이아웃에 영향을 주지 않는(ignoreLayout) 얇은 흰 막대 4개를
        /// pill의 네 변에 겹쳐서 테두리처럼 보이게 한다 — 전용 스프라이트 없이
        /// 순수 코드로 만들 수 있는 가장 단순한 방법.</summary>
        private static void AddPillBorder(GameObject pillGo)
        {
            const float thickness = 2f;
            AddBorderBar(pillGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -thickness), Vector2.zero);
            AddBorderBar(pillGo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thickness));
            AddBorderBar(pillGo.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(thickness, 0f));
            AddBorderBar(pillGo.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-thickness, 0f), Vector2.zero);
        }

        private static void AddBorderBar(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject("Border", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var img = go.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        private static void AddSquadTierGlyph(Transform parent, string text, Color color)
        {
            var go = new GameObject("Glyph", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 14f;
            label.color = color;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
        }

        /// <summary>헤더 한 줄(페이즈 아이콘 + 타입별 색상 이름(+[업그레이드]) +
        /// rule이면 우측에 (코스트)) 항상 보이고, 그 아래 룰 텍스트는 접힌 채로
        /// 시작해서 헤더를 클릭할 때마다 펼쳐지고 닫힌다. 페이즈/타입/코스트가
        /// 전부 헤더 자체에서 시각적으로 드러나므로 예전에 상세 안에 텍스트로
        /// 따로 적던 "페이즈:/타입:/코스트:" 줄은 전부 제거했다(중복). weapon-kind는
        /// 이제 여기 안 온다 — RenderAbilitiesSection이 미리 걸러서
        /// RenderWeaponProfileTrigger로 따로 보낸다.</summary>
        /// <summary>showCost=false로 부르면(택티컬 카드 능력) 헤더 우측의
        /// (코스트) 표시를 아예 뺀다 — 택티컬 카드 능력은 사용에 비용이
        /// 들지 않아서(사용자 지정), 로스터 JSON의 "cost" 필드가 있어도
        /// 의미가 없다. 유닛 능력 쪽 호출은 기본값(true)으로 기존과 동일.</summary>
        private void RenderAbility(Transform content, RosterAbilityEntry ability, bool showCost = true)
        {
            var abilityGo = new GameObject("Ability", typeof(RectTransform));
            abilityGo.transform.SetParent(content, false);
            var abilityLayout = abilityGo.AddComponent<VerticalLayoutGroup>();
            abilityLayout.spacing = 2f;
            abilityLayout.childControlWidth = true;
            abilityLayout.childControlHeight = true;
            abilityLayout.childForceExpandWidth = true;
            abilityLayout.childForceExpandHeight = false;
            var abilityFitter = abilityGo.AddComponent<ContentSizeFitter>();
            abilityFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string headerBase = KoPreferred(ability.NameKo, ability.NameEn);
            if (ability.IsUpgrade)
            {
                headerBase += " [업그레이드]";
            }
            string costSuffix = showCost && ability.Kind == "rule" ? $"({ability.Cost})" : null;
            Color color = AbilityTypeColor(ability);

            var headerRowGo = BuildAbilityHeaderRow(abilityGo.transform, ability.Phase, color, headerBase, costSuffix, out _, out var chevronLabel);
            var headerButton = headerRowGo.GetComponent<Button>();

            var detailsGo = new GameObject("Details", typeof(RectTransform));
            detailsGo.transform.SetParent(abilityGo.transform, false);
            var detailsLayout = detailsGo.AddComponent<VerticalLayoutGroup>();
            detailsLayout.spacing = 2f;
            detailsLayout.childControlWidth = true;
            detailsLayout.childControlHeight = true;
            detailsLayout.childForceExpandWidth = true;
            detailsLayout.childForceExpandHeight = false;
            var detailsFitter = detailsGo.AddComponent<ContentSizeFitter>();
            detailsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            detailsGo.SetActive(false); // 기본 접힘.

            void UpdateChevron(bool expanded)
            {
                chevronLabel.text = expanded ? "▼" : "▶";
            }
            UpdateChevron(false);
            headerButton.onClick.AddListener(() =>
            {
                bool expanded = !detailsGo.activeSelf;
                detailsGo.SetActive(expanded);
                UpdateChevron(expanded);
            });

            if (!string.IsNullOrEmpty(ability.RuleKo) || !string.IsNullOrEmpty(ability.RuleEn))
            {
                AddDetailText(detailsGo.transform, !string.IsNullOrEmpty(ability.RuleKo) ? ability.RuleKo : ability.RuleEn, 12f, new Color(0.85f, 0.85f, 0.85f, 1f));
            }
        }

        /// <summary>"사격"/"근접 공격" 합성 능력 — 펼치기 단계 없이 클릭하는
        /// 즉시 WeaponProfileDialog를 연다(사용자 지정: "이 능력을 클릭하면
        /// ... 모달을 띄워"). Active 타입 색으로 고정 — 이 합성 능력 자체는
        /// 로스터에 없는, 시뮬레이터가 만들어 붙인 것이라 실제 RosterAbilityEntry가
        /// 없으므로 AbilityTypeColor에 넘길 임시(placeholder) 항목을 하나
        /// 만들어 색상 계산 로직을 그대로 재사용한다(중복 방지). 이름 옆에
        /// 그 카테고리에 속한 무기 이름을 전부 괄호로 나열한다(사용자 지정 —
        /// "사격 ( C-14 소총 | AGG-12 | 로켓 발사기 )" 형태).</summary>
        private void RenderWeaponProfileTrigger(Transform content, string name, string phase, List<(string Name, RosterWeaponStat Weapon)> weapons)
        {
            var wrapperGo = new GameObject("Ability", typeof(RectTransform));
            wrapperGo.transform.SetParent(content, false);
            var wrapperLayout = wrapperGo.AddComponent<VerticalLayoutGroup>();
            wrapperLayout.childControlWidth = true;
            wrapperLayout.childControlHeight = true;
            wrapperLayout.childForceExpandWidth = true;
            wrapperLayout.childForceExpandHeight = false;
            var wrapperFitter = wrapperGo.AddComponent<ContentSizeFitter>();
            wrapperFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Color activeColor = AbilityTypeColor(new RosterAbilityEntry { Kind = "rule", Type = "Active" });
            string weaponList = string.Join(" | ", weapons.Select(w => w.Name));
            string displayText = $"{name} ( {weaponList} )";
            var headerRowGo = BuildAbilityHeaderRow(wrapperGo.transform, phase, activeColor, displayText, null, out _, out _);
            headerRowGo.GetComponent<Button>().onClick.AddListener(() => weaponProfileDialog?.Open(weapons));
        }

        /// <summary>능력 헤더 한 줄(페이즈 아이콘 + 접기/펼치기 화살표 자리 +
        /// 색상 이름 + 선택적 코스트)을 짓는다 — RenderAbility(펼치기 토글용)와
        /// RenderWeaponProfileTrigger(즉시 클릭용) 둘 다 이 모양을 그대로
        /// 쓰고, Button.onClick 배선만 호출부가 각자 다르게 붙인다. 아이콘도
        /// 이름과 같은 색으로 물들인다(사용자 지정 — 그러려고 아이콘 PNG를
        /// 일부러 흰색 실루엣으로만 준비해뒀다고 함, CaptureMarker의 flag.png와
        /// 같은 기법).
        ///
        /// 화살표는 일부러 이름 텍스트 안에 글자로 붙이지 않고 고정 폭의 별도
        /// 칸으로 뺐다 — 예전엔 RenderAbility가 "▶ "/"▼ "를 이름 문자열
        /// 앞에 직접 붙였는데, RenderWeaponProfileTrigger("사격"/"근접 공격")는
        /// 애초에 접고 펼 게 없어 그 문자열이 없었다 — 그래서 아이콘과 이름
        /// 사이 시각적 여백이 둘 사이에 달라 보였다(사용자가 지적). 이제는
        /// 화살표 칸 자체가 항상 같은 폭을 차지하므로(내용이 없으면 빈 채로),
        /// 아이콘→이름 간격이 접기/펼치기 유무와 무관하게 항상 똑같다.</summary>
        private GameObject BuildAbilityHeaderRow(Transform parent, string phase, Color color, string text, string costSuffix, out TextMeshProUGUI nameLabel, out TextMeshProUGUI chevronLabel)
        {
            var headerRowGo = new GameObject("Header", typeof(RectTransform));
            headerRowGo.transform.SetParent(parent, false);
            var headerRowImg = headerRowGo.AddComponent<Image>();
            headerRowImg.color = new Color(0f, 0f, 0f, 0f); // 보이진 않지만 클릭을 받으려면 raycastTarget이 있는 Graphic이 필요하다.
            var headerRowLayout = headerRowGo.AddComponent<HorizontalLayoutGroup>();
            headerRowLayout.spacing = 6f;
            headerRowLayout.childAlignment = TextAnchor.MiddleLeft;
            headerRowLayout.childControlWidth = true;
            headerRowLayout.childControlHeight = true;
            headerRowLayout.childForceExpandWidth = false;
            headerRowLayout.childForceExpandHeight = false;
            var headerButton = headerRowGo.AddComponent<Button>();
            headerButton.transition = Selectable.Transition.None;

            var phaseIcon = GetPhaseIcon(phase);
            if (phaseIcon != null)
            {
                var iconGo = new GameObject("PhaseIcon", typeof(RectTransform));
                iconGo.transform.SetParent(headerRowGo.transform, false);
                var iconImg = iconGo.AddComponent<RawImage>();
                iconImg.texture = phaseIcon;
                iconImg.color = color;
                iconImg.raycastTarget = false;
                var iconLe = iconGo.AddComponent<LayoutElement>();
                iconLe.preferredWidth = 16f;
                iconLe.preferredHeight = 16f;
            }

            var chevronGo = new GameObject("Chevron", typeof(RectTransform));
            chevronGo.transform.SetParent(headerRowGo.transform, false);
            chevronLabel = chevronGo.AddComponent<TextMeshProUGUI>();
            chevronLabel.text = "";
            chevronLabel.fontSize = 13f;
            chevronLabel.color = color;
            chevronLabel.fontStyle = FontStyles.Bold;
            chevronLabel.alignment = TextAlignmentOptions.MidlineLeft;
            chevronLabel.enableWordWrapping = false;
            chevronLabel.raycastTarget = false;
            var chevronLe = chevronGo.AddComponent<LayoutElement>();
            chevronLe.preferredWidth = 12f;

            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(headerRowGo.transform, false);
            nameLabel = nameGo.AddComponent<TextMeshProUGUI>();
            nameLabel.text = text;
            nameLabel.fontSize = 13f;
            nameLabel.color = color;
            nameLabel.fontStyle = FontStyles.Bold;
            nameLabel.enableWordWrapping = true;
            nameLabel.raycastTarget = false;
            var nameLe = nameGo.AddComponent<LayoutElement>();
            nameLe.flexibleWidth = 1f;

            if (costSuffix != null)
            {
                var costGo = new GameObject("Cost", typeof(RectTransform));
                costGo.transform.SetParent(headerRowGo.transform, false);
                var costLabel = costGo.AddComponent<TextMeshProUGUI>();
                costLabel.text = costSuffix;
                costLabel.fontSize = 12f;
                costLabel.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                costLabel.alignment = TextAlignmentOptions.MidlineRight;
                costLabel.enableWordWrapping = false;
                costLabel.raycastTarget = false;
            }

            return headerRowGo;
        }

        private static Texture2D GetPhaseIcon(string phase)
        {
            switch (phase)
            {
                case "Any":
                    return Resources.Load<Texture2D>("UI/AnyPhase");
                case "Movement":
                    return Resources.Load<Texture2D>("UI/MovementPhase");
                case "Assault":
                    return Resources.Load<Texture2D>("UI/AssaultPhase");
                case "Combat":
                    return Resources.Load<Texture2D>("UI/CombatPhase");
                default:
                    return null;
            }
        }

        /// <summary>Active=파란색/Passive=초록색/Reaction=노란색(사용자 지정).
        /// 무기 프로필 항목 자체는 더 이상 이 함수를 개별적으로 타지 않는다
        /// (RenderAbilitiesSection이 미리 걸러 RenderWeaponProfileTrigger로
        /// 보내고, 그쪽은 항상 Active색 고정) — default 분기는 kind가 rule인데
        /// type이 셋 중 하나가 아닌 예외적인 경우의 안전망일 뿐이다.</summary>
        private static Color AbilityTypeColor(RosterAbilityEntry ability)
        {
            switch (ability.Type)
            {
                case "Active":
                    return new Color(0.4f, 0.6f, 1f, 1f);
                case "Passive":
                    return new Color(0.45f, 0.85f, 0.5f, 1f);
                case "Reaction":
                    return new Color(0.95f, 0.85f, 0.35f, 1f);
                default:
                    return Color.white;
            }
        }

        /// <summary>태그/능력 이름처럼 영문 설명을 더 이상 병기하지 않기로 한
        /// 자리에서 쓴다 — ko가 있으면 ko만, 없으면 en으로 대체.</summary>
        private static string KoPreferred(string ko, string en)
        {
            return !string.IsNullOrEmpty(ko) ? ko : en;
        }

        private static void AddDetailSectionLabel(Transform parent, string text)
        {
            AddDetailText(parent, text, 13f, new Color(0.75f, 0.75f, 0.75f, 1f), FontStyles.Bold);
        }

        private static TextMeshProUGUI AddDetailText(Transform parent, string text, float fontSize, Color color, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }
    }
}
