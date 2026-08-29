using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 유닛 상세 패널의 "사격"/"근접 공격" 능력을 누르면 뜨는 읽기 전용
    /// 팝업 — 그 유닛의 원거리(Assault 페이즈) 또는 근거리(Combat 페이즈)
    /// 무기 프로필 전부를 표(헤더 한 줄 + 무기마다 한 줄, Name/RNG/TGT/RoA/
    /// HIT/SURGE/S.DIE/DMG/KEYWORD)로 종합해서 보여준다(사용자 지정 —
    /// "모든 원거리/근거리 무기 프로필이 종합적으로 보여지는거지").
    ///
    /// 배경 전체를 덮어 클릭을 막는 모달이 아니다 — 주사위 시뮬레이터로
    /// 굴리면서 이 무기 프로필과 상대 유닛의 방어/회피 정보를 동시에 봐야
    /// 한다는 사용자 요청에 따라, 다른 창/보드 클릭을 전혀 막지 않는 "떠있는
    /// 비독점 참고창"으로 만들었다 — 배경 Image 자체가 없고(그래서 클릭이
    /// 그대로 통과한다), 닫기는 오직 자신의 "닫기" 버튼으로만 한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WeaponProfileDialog : MonoBehaviour
    {
        private static readonly string[] ColumnHeaders = { "Name", "RNG", "TGT", "RoA", "HIT", "SURGE", "S.DIE", "DMG", "KEYWORD" };
        private static readonly float[] ColumnWidths = { 100f, 50f, 64f, 44f, 44f, 76f, 56f, 44f, 260f };

        /// <summary>TGT/SURGE는 이제 로스터 JSON이 tags/keyword와 같은 방식으로
        /// {"en","ko"} 쌍을 직접 실어 보낸다 — 예전엔 룰북을 대조해 하드코딩
        /// 번역표로 옮겼는데, 사용자 지정으로 그 방식을 버리고 "받은 데이터를
        /// 그대로 표시"하는 쪽으로 바꿨다(번역표 삭제). ko가 없으면 en으로.</summary>
        private static string KoPreferred(RosterTag tag)
        {
            if (tag == null)
            {
                return "-";
            }
            return !string.IsNullOrEmpty(tag.NameKo) ? tag.NameKo : tag.NameEn;
        }

        private RectTransform _tableRoot;

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // 이 루트 자체엔 배경 Image가 없다 — 화면 전체를 덮는 좌표계로만
            // 쓰고(Panel을 화면 하단 가운데에 앵커시키기 위해), raycast는
            // 전혀 받지 않아 클릭이 그대로 지도/다른 창으로 통과한다.

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            // 지도가 보이는 영역(팀 패널 사이, 마커바 위) 하단에 딱 붙이고
            // 좌우를 꽉 채운다(사용자 지정) — X는 스트레치(0~1)에 팀 패널
            // 폭만큼 인셋(-2*PendingPanelWidth), Y는 마커바 바로 위에 고정
            // 앵커, 높이는 계속 ContentSizeFitter가 위로 자라며 관리한다.
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, GameConstants.MarkerBarHeight);
            panelRect.sizeDelta = new Vector2(-2f * GameConstants.PendingPanelWidth, 0f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
            // 배경이 없어졌으니 "바깥 클릭 취소"로 새어나갈 일도 없다 — 패널
            // 자체는 이미 raycastTarget=true인 Image가 있어 그 아래(지도)로
            // 클릭이 통과하지 않는 것으로 충분하다(PanelBlocker 불필요).

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var panelFitter = panelGo.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; // 무기 개수에 따라 표 행이 늘어나므로 세로만 내용에 맞춘다.

            var tableGo = new GameObject("Table", typeof(RectTransform));
            tableGo.transform.SetParent(panelGo.transform, false);
            _tableRoot = (RectTransform)tableGo.transform;
            var tableLayout = tableGo.AddComponent<VerticalLayoutGroup>();
            tableLayout.spacing = 6f;
            tableLayout.childAlignment = TextAnchor.UpperLeft;
            tableLayout.childControlWidth = true;
            tableLayout.childControlHeight = true;
            tableLayout.childForceExpandWidth = true;
            tableLayout.childForceExpandHeight = false;
            var tableFitter = tableGo.AddComponent<ContentSizeFitter>();
            tableFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var closeGo = new GameObject("Close", typeof(RectTransform));
            closeGo.transform.SetParent(panelGo.transform, false);
            var closeLe = closeGo.AddComponent<LayoutElement>();
            closeLe.preferredHeight = 36f;
            var closeImg = closeGo.AddComponent<Image>();
            closeImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.onClick.AddListener(Close);
            var closeLabelGo = new GameObject("Label", typeof(RectTransform));
            closeLabelGo.transform.SetParent(closeGo.transform, false);
            var closeLabelRect = (RectTransform)closeLabelGo.transform;
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;
            var closeLabel = closeLabelGo.AddComponent<TextMeshProUGUI>();
            closeLabel.text = "닫기";
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.fontSize = 16f;
            closeLabel.color = Color.white;
            closeLabel.raycastTarget = false;

            gameObject.SetActive(false);
        }

        /// <summary>weapons는 그 카테고리(원거리/근거리)에 속하는 (무기 이름,
        /// 스탯) 쌍 전부 — 한 무기짜리 표가 아니라 헤더 한 줄 + 무기마다
        /// 한 줄인 진짜 표를 그린다. 창 자체에 제목은 없다(사용자 지정으로
        /// 제거) — 어느 능력을 눌러서 열었는지는 표의 Name 열들로 이미
        /// 드러난다. 무기 키워드 이름도 영문 병기 없이 한글만 보여준다
        /// (사용자 요청 — 태그/능력 이름과 같은 정책).</summary>
        public void Open(List<(string Name, RosterWeaponStat Weapon)> weapons)
        {
            for (int i = _tableRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_tableRoot.GetChild(i).gameObject);
            }

            AddTableRow(ColumnHeaders, true);
            foreach (var (name, weapon) in weapons)
            {
                string surgeText = weapon.Surge.Count > 0 ? string.Join("/", weapon.Surge.ConvertAll(KoPreferred)) : "-";
                string keywordText = "-";
                if (weapon.Keywords.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var kw in weapon.Keywords)
                    {
                        string text = !string.IsNullOrEmpty(kw.NameKo) ? kw.NameKo : kw.NameEn;
                        string suffix = !string.IsNullOrEmpty(kw.SuffixKo) ? kw.SuffixKo : kw.SuffixEn;
                        if (!string.IsNullOrEmpty(suffix))
                        {
                            text += $" {suffix}";
                        }
                        parts.Add(text);
                    }
                    keywordText = string.Join(", ", parts);
                }

                AddTableRow(new[] { name, weapon.Range, KoPreferred(weapon.Target), weapon.Roa, weapon.Hit, surgeText, weapon.SurgeDie, weapon.Damage, keywordText }, false);
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        private void AddTableRow(IReadOnlyList<string> values, bool isHeader)
        {
            var rowGo = new GameObject(isHeader ? "HeaderRow" : "Row", typeof(RectTransform));
            rowGo.transform.SetParent(_tableRoot, false);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            for (int i = 0; i < values.Count && i < ColumnWidths.Length; i++)
            {
                var cellGo = new GameObject($"Cell_{i}", typeof(RectTransform));
                cellGo.transform.SetParent(rowGo.transform, false);
                var cellLe = cellGo.AddComponent<LayoutElement>();
                cellLe.preferredWidth = ColumnWidths[i];
                // 패널이 이제 지도 폭 전체로 늘어나므로(화면 크기에 따라
                // 훨씬 넓어짐), 컬럼도 고정폭에 머물지 않고 원래 비율(같은
                // ColumnWidths 값)대로 남는 공간을 나눠 가지며 늘어난다.
                cellLe.flexibleWidth = ColumnWidths[i];
                var cellLabel = cellGo.AddComponent<TextMeshProUGUI>();
                cellLabel.text = string.IsNullOrEmpty(values[i]) ? "-" : values[i];
                cellLabel.fontSize = isHeader ? 11f : 13f;
                cellLabel.color = isHeader ? new Color(0.6f, 0.6f, 0.6f, 1f) : Color.white;
                cellLabel.fontStyle = isHeader ? FontStyles.Bold : FontStyles.Normal;
                // Name(0번)과 KEYWORD(마지막 칸)는 왼쪽 정렬 — KEYWORD는 여러 키워드가
                // 길게 이어질 수 있어 가운데 정렬이면 읽기 불편하다(사용자 지적).
                bool leftAlign = i == 0 || i == ColumnWidths.Length - 1;
                cellLabel.alignment = leftAlign ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
                cellLabel.enableWordWrapping = true;
                cellLabel.raycastTarget = false;
            }
        }

    }
}
