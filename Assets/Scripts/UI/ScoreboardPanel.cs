using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>화면 맨 위를 가로지르는 전광판 — 왼쪽에 A팀(빨강), 오른쪽에
    /// B팀(파랑), 중앙에 라운드/서플라이. Godot판 GameBoard.gd의
    /// _build_scoreboard()/_add_stat_spinbox() 포팅이지만 배치는 새로 짰다
    /// (Godot판은 좌상단에 뜨는 작은 세로형 패널이었다). 값은 MatchState(정적
    /// 클래스)에 직접 읽고 쓴다. 미션VP/파괴VP는 직접 수정, 종합VP는 계산된
    /// 값을 보여주기만 한다. 각 팀 줄 아래에는 서플라이 소비 현황을 네모로
    /// 보여주는데, 이건 보드에 실제로 배치된 유닛을 봐야 해서(SetBoardManager)
    /// BoardManager 참조가 필요하다 — 그 외 나머지는 여전히 독립적이다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class ScoreboardPanel : MonoBehaviour
    {
        private const float SupplyPipSize = 10f;
        private const float SupplyPipSpacing = 2f;
        private static readonly Color SupplyUsedColor = new Color(0.25f, 0.55f, 1f, 1f);
        private static readonly Color SupplyFreeColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        private readonly Dictionary<string, TextMeshProUGUI> _totalVpLabels = new Dictionary<string, TextMeshProUGUI>();
        private readonly Dictionary<string, RectTransform> _supplyRowContainers = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, List<RawImage>> _supplyPips = new Dictionary<string, List<RawImage>>();
        private readonly Dictionary<string, TextMeshProUGUI> _teamNameLabels = new Dictionary<string, TextMeshProUGUI>();
        private readonly Dictionary<string, GameObject> _colorPopups = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, bool> _colorPopupLeft = new Dictionary<string, bool>();

        // BoardManager는 부트스트랩이 두 객체를 다 만든 뒤 SetBoardManager()로
        // 나중에 넣어준다(Awake() 시점엔 아직 BoardManager가 없을 수 있음) —
        // 이 컴포넌트는 원래 BoardManager와 무관하게 독립적으로 짓게 설계했지만,
        // "배치된 유닛이 소비 중인 서플라이"를 보여주려면 보드 상태를 봐야만
        // 해서 이 기능만큼은 어쩔 수 없이 참조가 필요하다.
        private BoardManager _board;

        public void SetBoardManager(BoardManager board)
        {
            _board = board;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, GameConstants.ScoreboardHeight);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            BuildTeamSide("A", left: true);
            BuildCenter();
            BuildTeamSide("B", left: false);
        }

        /// <summary>팀 한 쪽 전체 — 위: 이름/미션VP/파괴VP/종합VP 한 줄, 아래:
        /// 서플라이 현황 네모 한 줄. 두 줄을 세로로 쌓는 바깥 컬럼이 새로
        /// 생겼다(이전엔 한 줄짜리 가로 그룹이었다).</summary>
        private void BuildTeamSide(string team, bool left)
        {
            var sideGo = new GameObject($"Team_{team}", typeof(RectTransform));
            sideGo.transform.SetParent(transform, false);
            var sideRect = (RectTransform)sideGo.transform;
            float xAnchor = left ? 0f : 1f;
            sideRect.anchorMin = new Vector2(xAnchor, 0.5f);
            sideRect.anchorMax = new Vector2(xAnchor, 0.5f);
            sideRect.pivot = new Vector2(xAnchor, 0.5f);
            sideRect.anchoredPosition = new Vector2(left ? 20f : -20f, 0f);

            var columnLayout = sideGo.AddComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 4f;
            columnLayout.childAlignment = left ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;
            columnLayout.childForceExpandWidth = false;
            columnLayout.childForceExpandHeight = false;
            // sideRect는 point anchor(anchorMin==anchorMax)라 크기가 전부
            // sizeDelta에서 나오는데, 그걸 따로 안 정해줬으니 컨텐츠 크기에
            // 맞춰 스스로 커지도록 양쪽 축 다 Fitter가 필요하다 — 이걸
            // 빼먹으면(실제로 한 번 빼먹었었다) sideRect가 기본 크기인 채로
            // 남아 자식들이 그 좁은 상자 안에 짓눌려 전부 겹쳐 보인다.
            var columnFitter = sideGo.AddComponent<ContentSizeFitter>();
            columnFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            columnFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var statRowGo = new GameObject("StatRow", typeof(RectTransform));
            statRowGo.transform.SetParent(sideRect, false);
            var statLayout = statRowGo.AddComponent<HorizontalLayoutGroup>();
            statLayout.spacing = 14f;
            statLayout.childAlignment = TextAnchor.MiddleLeft;
            statLayout.childControlWidth = true;
            statLayout.childControlHeight = true;
            statLayout.childForceExpandWidth = false;
            statLayout.childForceExpandHeight = false;

            var nameLabel = CreateTeamNameButton(statRowGo.transform, team);
            _teamNameLabels[team] = nameLabel;
            BuildColorPopup(team, left);

            CreateStatInputField(statRowGo.transform, "미션VP", MatchState.MissionVp[team], 0, 999, v =>
            {
                MatchState.MissionVp[team] = v;
                RefreshTotalLabel(team);
            });

            CreateStatInputField(statRowGo.transform, "파괴VP", MatchState.KillVp[team], 0, 999, v =>
            {
                MatchState.KillVp[team] = v;
                RefreshTotalLabel(team);
            });

            var totalLabel = CreateLabel(statRowGo.transform, "", 16f, 90f, Color.white, FontStyles.Normal);
            _totalVpLabels[team] = totalLabel;
            RefreshTotalLabel(team);

            BuildSupplyRow(sideRect, team);
        }

        /// <summary>서플라이 네모 한 줄을 담을 빈 컨테이너만 만들어둔다 — 실제
        /// 네모는 Update()에서 RefreshSupplyRow()가 채운다(라운드/서플라이
        /// 값과 보드에 배치된 유닛 둘 다에 따라 매 프레임 바뀔 수 있어서).</summary>
        private void BuildSupplyRow(Transform parent, string team)
        {
            var rowGo = new GameObject("SupplyRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = SupplyPipSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            _supplyRowContainers[team] = (RectTransform)rowGo.transform;
            _supplyPips[team] = new List<RawImage>();
        }

        private void Update()
        {
            RefreshSupplyRow("A");
            RefreshSupplyRow("B");
        }

        /// <summary>네모 개수를 MatchState.Supply(공유 상한)에 맞추고 — 개수가
        /// 실제로 바뀔 때만 지우고 다시 만든다 — 그 중 앞쪽 N개(N = 이 팀이
        /// 보드에 배치한 유닛들의 현재 서플라이 값(BoardManager.
        /// GetTeamSupplyUsed, 유닛마다 남은 모델 수 기준으로 다시 계산됨) 합)를
        /// 파란색으로, 나머지는 회색으로 칠한다. 개수 자체는 스테퍼/휠 조작
        /// 때만 바뀌므로 매 프레임 새로 만드는 건 색칠뿐이라 가볍다.</summary>
        private void RefreshSupplyRow(string team)
        {
            if (!_supplyRowContainers.TryGetValue(team, out var container))
            {
                return;
            }
            int total = Mathf.Max(MatchState.Supply, 0);
            var pips = _supplyPips[team];

            if (pips.Count != total)
            {
                foreach (var pip in pips)
                {
                    Destroy(pip.gameObject);
                }
                pips.Clear();
                var squareTexture = Resources.Load<Texture2D>("UI/Square");
                for (int i = 0; i < total; i++)
                {
                    var go = new GameObject("Pip", typeof(RectTransform));
                    go.transform.SetParent(container, false);
                    var le = go.AddComponent<LayoutElement>();
                    le.preferredWidth = SupplyPipSize;
                    le.preferredHeight = SupplyPipSize;
                    var img = go.AddComponent<RawImage>();
                    img.texture = squareTexture;
                    pips.Add(img);
                }
            }

            int used = Mathf.Clamp(_board != null ? _board.GetTeamSupplyUsed(team) : 0, 0, total);
            for (int i = 0; i < pips.Count; i++)
            {
                pips[i].color = i < used ? SupplyUsedColor : SupplyFreeColor;
            }
        }

        private void BuildCenter()
        {
            var centerGo = new GameObject("Center", typeof(RectTransform));
            centerGo.transform.SetParent(transform, false);
            var centerRect = (RectTransform)centerGo.transform;
            centerRect.anchorMin = new Vector2(0.5f, 0.5f);
            centerRect.anchorMax = new Vector2(0.5f, 0.5f);
            centerRect.pivot = new Vector2(0.5f, 0.5f);
            centerRect.anchoredPosition = Vector2.zero;

            var layout = centerGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = centerGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateStatInputField(centerRect, "라운드", MatchState.RoundNumber, 1, 20, v => MatchState.RoundNumber = v);
            CreateStatInputField(centerRect, "서플라이", MatchState.Supply, 0, 999, v => MatchState.Supply = v);
        }

        private void RefreshTotalLabel(string team)
        {
            if (_totalVpLabels.TryGetValue(team, out var label))
            {
                label.text = $"종합VP {MatchState.TotalVp(team)}";
            }
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, float preferredWidth, Color color, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.preferredHeight = fontSize + 8f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false; // 좁은 스코어보드 바 안이라 줄바꿈되면 보기 나쁘다 — 폭이 좁으면 넘치더라도 한 줄로.
            label.raycastTarget = false;
            return label;
        }

        /// <summary>"플레이어 A"/"플레이어 B" 라벨 — 다른 라벨과 달리 클릭하면
        /// 색상 팔레트 팝업이 뜬다(사용자 요청). raycastTarget을 켜고 Button을
        /// 붙인다는 점만 CreateLabel과 다르다.</summary>
        private TextMeshProUGUI CreateTeamNameButton(Transform parent, string team)
        {
            var teamColor = GameConstants.TeamColors.TryGetValue(team, out var c) ? c : Color.white;

            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 96f;
            le.preferredHeight = 26f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = $"플레이어 {team}";
            label.fontSize = 18f;
            label.color = teamColor;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            label.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = label;
            btn.onClick.AddListener(() => ToggleColorPopup(team));

            return label;
        }

        /// <summary>team 쪽 색상 팔레트 팝업을 만든다(숨긴 채로) — 위치는 짓는
        /// 시점이 아니라 열 때마다(ToggleColorPopup) 그 팀 이름 라벨의 실제
        /// 화면 위치를 기준으로 다시 계산한다(라벨이 VerticalLayoutGroup/
        /// HorizontalLayoutGroup 안에 있어 실제 화면 위치가 미리 알 수 있는
        /// 고정값이 아니기 때문). 부모도 이 시점엔 아직 transform.parent가
        /// null이라(부트스트랩이 Awake 다음에야 SetParent) 여기서는 일단
        /// 자기 자신 아래 임시로 둔다 — 실제로 열릴 때 캔버스 루트로 옮겨진다.
        /// 스와치를 고르면 BoardManager.SetTeamColor로 보드 전체(배치된
        /// 유닛/예비대/마커/미션 목표 마커)에 즉시 소급 적용되고, 이 라벨의
        /// 표시 색도 같이 바뀐다.</summary>
        private void BuildColorPopup(string team, bool left)
        {
            var popupGo = new GameObject($"ColorPopup_{team}", typeof(RectTransform));
            popupGo.transform.SetParent(transform, false);

            var bg = popupGo.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.05f, 0.97f);

            var grid = popupGo.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(28f, 28f);
            grid.spacing = new Vector2(6f, 6f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 6;

            var fitter = popupGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var swatchColor in GameConstants.TeamColorPalette)
            {
                var swatchGo = new GameObject("Swatch", typeof(RectTransform));
                swatchGo.transform.SetParent(popupGo.transform, false);
                var img = swatchGo.AddComponent<Image>();
                img.color = swatchColor;

                var swatchBtn = swatchGo.AddComponent<Button>();
                swatchBtn.targetGraphic = img;
                swatchBtn.onClick.AddListener(() =>
                {
                    _board?.SetTeamColor(team, swatchColor);
                    if (_teamNameLabels.TryGetValue(team, out var lbl))
                    {
                        lbl.color = swatchColor;
                    }
                    popupGo.SetActive(false);
                });
            }

            popupGo.SetActive(false);
            _colorPopups[team] = popupGo;
            _colorPopupLeft[team] = left;
        }

        /// <summary>클릭한 팀의 팝업을 열려 있으면 닫고, 닫혀 있으면 (다른 팀
        /// 팝업은 닫으면서) 연다. 열 때마다 캔버스 루트로 옮기고 맨 위로
        /// 올린다 — 그렇지 않으면 나중에 만들어지는 예비대 패널
        /// (BoardManager.BuildPendingPanel)이 같은 캔버스 안에서 더 나중
        /// 형제라 팝업을 가려버린다(사용자가 실제로 겪은 문제).</summary>
        private void ToggleColorPopup(string team)
        {
            bool wasOpen = _colorPopups.TryGetValue(team, out var popup) && popup.activeSelf;
            foreach (var other in _colorPopups.Values)
            {
                other.SetActive(false);
            }
            if (wasOpen || popup == null)
            {
                return;
            }

            var canvasRoot = transform.parent as RectTransform;
            if (canvasRoot != null)
            {
                popup.transform.SetParent(canvasRoot, false);
                PositionPopupUnderLabel(team, popup, canvasRoot);
            }
            popup.transform.SetAsLastSibling();
            popup.SetActive(true);
        }

        /// <summary>팝업의 (왼쪽 팀이면 좌상단, 오른쪽 팀이면 우상단) 꼭짓점을
        /// 그 팀 이름 라벨의 실제 화면상 아래쪽 모서리에 맞춘다 — 라벨이
        /// 레이아웃 그룹 안에 있어 화면 위치가 고정값이 아니므로 매번
        /// GetWorldCorners로 다시 읽어야 한다.</summary>
        private void PositionPopupUnderLabel(string team, GameObject popup, RectTransform canvasRoot)
        {
            if (!_teamNameLabels.TryGetValue(team, out var label))
            {
                return;
            }
            bool left = _colorPopupLeft.TryGetValue(team, out var l) && l;

            var corners = new Vector3[4]; // 0=좌하, 1=좌상, 2=우상, 3=우하 (월드 좌표)
            ((RectTransform)label.transform).GetWorldCorners(corners);
            Vector3 anchorWorldCorner = left ? corners[0] : corners[3];
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, anchorWorldCorner);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screenPoint, null, out var localPoint);

            var popupRect = (RectTransform)popup.transform;
            popupRect.anchorMin = popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(left ? 0f : 1f, 1f);
            popupRect.anchoredPosition = localPoint;
        }

        /// <summary>"라벨: [숫자 표시칸][위/아래 아이콘 스테퍼]" 한 묶음. 직접
        /// 타이핑은 막고(readOnly), 1씩 증감만 허용한다 — 스테퍼 버튼 클릭
        /// 또는 칸 위에서 휠 스크롤. 값은 항상 [minValue, maxValue]로
        /// 클램프된다.</summary>
        private static void CreateStatInputField(Transform parent, string labelText, int initial, int minValue, int maxValue, System.Action<int> onChanged)
        {
            var rowGo = new GameObject($"Stat_{labelText}", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;

            CreateLabel(rowGo.transform, labelText, 14f, 52f, Color.white, FontStyles.Normal);

            int currentValue = Mathf.Clamp(initial, minValue, maxValue);

            var fieldGo = new GameObject("Input", typeof(RectTransform));
            fieldGo.transform.SetParent(rowGo.transform, false);
            var le = fieldGo.AddComponent<LayoutElement>();
            le.preferredWidth = 44f;
            le.preferredHeight = 30f;
            var bg = fieldGo.AddComponent<Image>();
            bg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var inputField = fieldGo.AddComponent<TMP_InputField>();
            inputField.contentType = TMP_InputField.ContentType.IntegerNumber;
            inputField.readOnly = true; // 직접 타이핑 금지 — 스테퍼/휠로만 값을 바꾼다.

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(fieldGo.transform, false);
            var textAreaRect = (RectTransform)textAreaGo.transform;
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(6f, 2f);
            textAreaRect.offsetMax = new Vector2(-6f, -2f);
            textAreaGo.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 16f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.text = currentValue.ToString();

            void ApplyValue(int newValue)
            {
                currentValue = Mathf.Clamp(newValue, minValue, maxValue);
                inputField.SetTextWithoutNotify(currentValue.ToString());
                onChanged(currentValue);
            }

            var scrollHandler = fieldGo.AddComponent<ScrollStepHandler>();
            scrollHandler.OnStep = delta => ApplyValue(currentValue + delta);

            var stepperGo = new GameObject("Stepper", typeof(RectTransform));
            stepperGo.transform.SetParent(rowGo.transform, false);
            var stepperLe = stepperGo.AddComponent<LayoutElement>();
            stepperLe.preferredWidth = 16f;
            stepperLe.preferredHeight = 28f;
            var stepperLayout = stepperGo.AddComponent<VerticalLayoutGroup>();
            stepperLayout.spacing = 2f;
            stepperLayout.childControlWidth = true;
            stepperLayout.childControlHeight = true;
            stepperLayout.childForceExpandWidth = true;
            stepperLayout.childForceExpandHeight = false;

            // 마커바(CreateMarkerBarButton)와 같은 방식 — RawImage+Texture2D로
            // Sprite 임포트 타입을 신경 안 써도 된다. 버튼 자체의 세로 크기는
            // (스테퍼 컨테이너의 자동 분배에 맡기지 않고) 각 버튼에 직접
            // LayoutElement.preferredHeight를 줘서 명시적으로 작게 고정한다.
            CreateStepperButton(stepperGo.transform, Resources.Load<Texture2D>("UI/Up"), () => ApplyValue(currentValue + 1));
            CreateStepperButton(stepperGo.transform, Resources.Load<Texture2D>("UI/Down"), () => ApplyValue(currentValue - 1));
        }

        private static void CreateStepperButton(Transform parent, Texture2D icon, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("StepBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 16f;
            le.preferredHeight = 13f;

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
        }

        /// <summary>입력칸 위에서 휠을 굴리면 1씩 증감 — 위로 굴리면 +1, 아래로
        /// 굴리면 -1. TMP_InputField는 IScrollHandler를 구현하지 않으므로
        /// 같은 GameObject에 별도로 붙여도 충돌하지 않는다.</summary>
        private class ScrollStepHandler : MonoBehaviour, IScrollHandler
        {
            public System.Action<int> OnStep;

            public void OnScroll(PointerEventData eventData)
            {
                if (eventData.scrollDelta.y > 0f)
                {
                    OnStep?.Invoke(1);
                }
                else if (eventData.scrollDelta.y < 0f)
                {
                    OnStep?.Invoke(-1);
                }
            }
        }
    }
}
