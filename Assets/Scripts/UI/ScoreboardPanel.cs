using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>화면 맨 위를 가로지르는 전광판 — 왼쪽에 A팀(빨강), 오른쪽에
    /// B팀(파랑), 중앙에 라운드 표시. Godot판 GameBoard.gd의
    /// _build_scoreboard()/_add_stat_spinbox() 포팅이지만 배치는 새로 짰다
    /// (Godot판은 좌상단에 뜨는 작은 세로형 패널이었다). 값은 MatchState(정적
    /// 클래스)에 직접 읽고 쓴다. 미션VP/파괴VP는 직접 수정, 종합VP는 계산된
    /// 값을 보여주기만 한다. 라운드는 스핀박스가 아니라 클릭식 네모 표시기이고
    /// (BuildRoundIndicator), 서플라이 상한은 더 이상 여기서 직접 입력받지
    /// 않는다 — 미션 셋업(MissionSettingsData)에서 정한 공식으로 라운드가 바뀔 때마다
    /// 자동 계산된다(SetRoundNumber). 각 팀 줄 아래에는 서플라이 소비 현황을
    /// 네모로 보여주는데, 이건 보드에 실제로 배치된 유닛을 봐야 해서
    /// (SetBoardManager) BoardManager 참조가 필요하다 — 그 외 나머지는 여전히
    /// 독립적이다.</summary>
    [RequireComponent(typeof(RectTransform))]
    public class ScoreboardPanel : MonoBehaviour
    {
        private const float SupplyPipSize = 10f;
        private const float SupplyPipSpacing = 2f;
        private const float SupplyPreviewFlashSpeed = 6f; // 라디안/초 — Base.cs의 CoherencyFlashSpeed와 동일한 속도.
        private static readonly Color SupplyUsedColor = new Color(0.25f, 0.55f, 1f, 1f);
        private static readonly Color SupplyFreeColor = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color SupplyOverflowColor = new Color(0.9f, 0.15f, 0.15f, 1f);

        private const float RoundPipSize = 16f;
        private const float RoundPipSpacing = 3f;
        private const float MissionButtonWidth = 44f;
        private static readonly Color RoundActiveColor = new Color(1f, 0.85f, 0.1f, 1f);
        private static readonly Color RoundInactiveColor = new Color(0.45f, 0.45f, 0.45f, 1f);

        private readonly List<RawImage> _roundPips = new List<RawImage>();

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

        // ExitConfirmDialog 등 다른 다이얼로그처럼 부트스트랩이 따로 만들어서
        // 나중에 넣어준다(Awake() 시점엔 아직 없을 수 있음) — 버튼 자체의
        // 활성/비활성은 MissionSettingsData.HasData만 보면 되므로(이 static
        // 데이터는 씬 로드 전에 이미 확정) 이 참조가 늦게 들어와도 문제없다.
        private MissionInfoDialog _missionInfoDialog;

        public void SetMissionInfoDialog(MissionInfoDialog dialog)
        {
            _missionInfoDialog = dialog;
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

            IntStepperField.Create(statRowGo.transform, "미션VP", 52f, MatchState.MissionVp[team], 0, 999, v =>
            {
                MatchState.MissionVp[team] = v;
                RefreshTotalLabel(team);
            });

            IntStepperField.Create(statRowGo.transform, "파괴VP", 52f, MatchState.KillVp[team], 0, 999, v =>
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

        /// <summary>네모 개수는 기본적으로 MatchState.Supply(공유 상한)이지만,
        /// 실제 배치량이나(초과 배치) 배치 고스트 미리보기가 그 상한을 넘어서면
        /// 필요한 만큼 네모를 더 늘려서(뒤에 이어붙여) 넘치는 양도 그대로 보여준다
        /// — 개수가 바뀔 때만 지우고 다시 만든다. 칠하는 규칙(논리적 인덱스
        /// idx 기준, A팀은 화면 왼쪽=idx 0, B팀은 화면 오른쪽=idx 0으로 뒤집힘):
        /// (1) 상한 이내에서 실제로 이미 배치된 만큼 = 파란색(고정),
        /// (2) 상한 이내에서 배치 고스트가 추가로 먹을 부분 = 회색↔팀색 반짝임,
        /// (3) 상한 이내에서 남는 부분 = 회색(고정),
        /// (4) 상한을 넘어 실제로 이미 배치된 부분(진짜 초과 배치) = 빨간색(고정),
        /// (5) 상한을 넘어 배치 고스트가 추가로 밀어넣는 부분 = 회색↔빨간색 반짝임.
        /// (2)/(5)는 사용자가 예비대 목록에서 유닛을 클릭해 배치 고스트를 띄운
        /// 동안만 나타난다(BoardManager.GetTeamPreviewSupply, 한 번에 한 팀만
        /// 배치 중일 수 있음).</summary>
        private void RefreshSupplyRow(string team)
        {
            if (!_supplyRowContainers.TryGetValue(team, out var container))
            {
                return;
            }
            int total = Mathf.Max(MatchState.Supply, 0);
            int used = _board != null ? _board.GetTeamSupplyUsed(team) : 0;
            int previewCost = _board != null ? _board.GetTeamPreviewSupply(team) : 0;
            int previewEnd = used + previewCost;
            int displayCount = Mathf.Max(total, Mathf.Max(used, previewEnd));

            var pips = _supplyPips[team];
            if (pips.Count != displayCount)
            {
                foreach (var pip in pips)
                {
                    Destroy(pip.gameObject);
                }
                pips.Clear();
                var squareTexture = Resources.Load<Texture2D>("UI/Square");
                for (int i = 0; i < displayCount; i++)
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

            var teamColor = GameConstants.TeamColors.TryGetValue(team, out var tc) ? tc : SupplyUsedColor;
            teamColor.a = 1f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * SupplyPreviewFlashSpeed);

            // B팀은 화면 오른쪽에 붙어 있으므로 "우측 정렬"로 보이려면 이미 쓴
            // 만큼(파란색)이 패널 가장자리(오른쪽)에 붙어 안쪽(가운데)으로
            // 자라나야 한다(사용자 요청) — 화면상 자리(i, 왼쪽부터)는 그대로
            // 두고, 색 규칙을 판단할 "논리적 인덱스"만 좌우로 뒤집는다.
            bool mirror = team == "B";

            for (int i = 0; i < pips.Count; i++)
            {
                int idx = mirror ? pips.Count - 1 - i : i;
                Color color;
                if (idx < total)
                {
                    if (idx < used)
                    {
                        color = SupplyUsedColor;
                    }
                    else if (idx < previewEnd)
                    {
                        color = Color.Lerp(SupplyFreeColor, teamColor, pulse);
                    }
                    else
                    {
                        color = SupplyFreeColor;
                    }
                }
                else
                {
                    color = idx < used
                        ? SupplyOverflowColor
                        : Color.Lerp(SupplyFreeColor, SupplyOverflowColor, pulse);
                }
                pips[i].color = color;
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

            // 가로 배치로 바꿨다 — "라운드"(타이틀+체커) 블록 옆에 "미션"
            // 버튼을 붙이되, 라운드 블록 자체는 계속 화면 상단 정중앙에
            // 있어야 한다는 사용자 지정 때문에, 반대쪽에 버튼과 정확히
            // 같은 폭의 투명 스페이서를 둬서 좌우 대칭을 맞춘다(아래 참고) —
            // 이 Center 전체는 여전히 화면 정중앙(0.5,0.5)에 앵커돼 있으므로,
            // 좌우가 대칭이면 가운데 자식(RoundColumn)의 중심도 자동으로
            // 화면 정중앙과 일치한다.
            var layout = centerGo.AddComponent<HorizontalLayoutGroup>();
            // 스페이서-RoundColumn 간격과 RoundColumn-버튼 간격이 항상 같은
            // 값을 쓴다(HorizontalLayoutGroup.spacing은 모든 자식 사이에
            // 균일하게 적용됨) — 그래서 이 값을 키워도 RoundColumn이 화면
            // 정중앙에서 벗어나지 않는다(사용자 요청으로 라운드-버튼 간격을
            // 더 벌림).
            layout.spacing = 28f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = centerGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var spacerGo = new GameObject("Spacer", typeof(RectTransform));
            spacerGo.transform.SetParent(centerRect, false);
            var spacerLe = spacerGo.AddComponent<LayoutElement>();
            spacerLe.preferredWidth = MissionButtonWidth;
            spacerLe.preferredHeight = 1f;

            var roundColumnGo = new GameObject("RoundColumn", typeof(RectTransform));
            roundColumnGo.transform.SetParent(centerRect, false);
            var roundColumnLayout = roundColumnGo.AddComponent<VerticalLayoutGroup>();
            roundColumnLayout.spacing = 2f;
            roundColumnLayout.childAlignment = TextAnchor.MiddleCenter;
            roundColumnLayout.childControlWidth = true;
            roundColumnLayout.childControlHeight = true;
            roundColumnLayout.childForceExpandWidth = false;
            roundColumnLayout.childForceExpandHeight = false;
            var roundColumnRect = (RectTransform)roundColumnGo.transform;

            var titleLabel = CreateLabel(roundColumnRect, "라운드", 12f, 80f, new Color(0.7f, 0.7f, 0.7f, 1f), FontStyles.Normal);
            titleLabel.alignment = TextAlignmentOptions.Center;

            BuildRoundIndicator(roundColumnRect);

            BuildMissionButton(centerRect);
        }

        /// <summary>"라운드"(타이틀+체커) 블록 옆에 붙는 작은 "미션" 버튼 —
        /// 누르면 MissionInfoDialog가 미션 셋업에서 정한 값(미션 파라미터/
        /// 점수 획득 조건/추가 조건 등)을 읽기 전용 모달로 보여준다. 미션
        /// 셋업을 거치지 않고 게임판에 들어온 경우(MissionSettingsData.HasData
        /// == false)엔 보여줄 내용이 없다는 걸 명확히 하려고 버튼 자체를
        /// 비활성화한다(사용자 지정). 폭은 BuildCenter의 반대쪽 스페이서와
        /// 반드시 같아야 한다(MissionButtonWidth 상수 공유 — 좌우 대칭 유지).</summary>
        private void BuildMissionButton(Transform parent)
        {
            var go = new GameObject("MissionButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = MissionButtonWidth;
            le.preferredHeight = 20f;
            var img = go.AddComponent<Image>();
            bool enabled = MissionSettingsData.HasData;
            img.color = enabled ? new Color(0.3f, 0.3f, 0.3f, 1f) : new Color(0.2f, 0.2f, 0.2f, 1f);
            var btn = go.AddComponent<Button>();
            btn.interactable = enabled;
            btn.onClick.AddListener(() => _missionInfoDialog?.Open());

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "미션";
            label.fontSize = 12f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1f);
            label.raycastTarget = false;
        }

        /// <summary>'최대 라운드 수'(미션 설정에서 정한 값)만큼 네모를 늘어놓고,
        /// 맨 왼쪽부터 지금 라운드까지 노란색, 나머지는 회색으로 칠한다 —
        /// 스테퍼가 아니라 아무 네모나 클릭하면 그 네모까지가 "지금 라운드"가
        /// 되는 방식(사용자 요청). 서플라이 입력칸은 여기서 완전히 빠졌다 —
        /// 이제 라운드가 바뀔 때마다 MissionData의 기본값+라운드당 증가량으로
        /// MatchState.Supply를 자동 계산한다(SetRoundNumber).</summary>
        private void BuildRoundIndicator(Transform parent)
        {
            var rowGo = new GameObject("RoundRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = RoundPipSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var squareTexture = Resources.Load<Texture2D>("UI/Square");
            int maxRounds = Mathf.Max(MissionSettingsData.RoundLength, 1);
            for (int i = 0; i < maxRounds; i++)
            {
                int roundNumber = i + 1;
                var go = new GameObject($"RoundPip_{roundNumber}", typeof(RectTransform));
                go.transform.SetParent(rowGo.transform, false);
                var le = go.AddComponent<LayoutElement>();
                le.preferredWidth = RoundPipSize;
                le.preferredHeight = RoundPipSize;
                var img = go.AddComponent<RawImage>();
                img.texture = squareTexture;
                var btn = go.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => SetRoundNumber(roundNumber));
                _roundPips.Add(img);
            }

            SetRoundNumber(Mathf.Clamp(MatchState.RoundNumber, 1, maxRounds));
        }

        /// <summary>라운드를 바꾸고 — 미션 설정에서 정한 "기본 서플라이 한도" +
        /// "라운드당 추가 서플라이 제공량" × (지금 라운드 - 1) 공식으로 서플라이
        /// 상한도 곧바로 다시 계산한다(1라운드는 증가분 없이 기본값 그대로,
        /// 사용자 요청). 라운드 네모 색도 즉시 갱신한다.</summary>
        private void SetRoundNumber(int roundNumber)
        {
            MatchState.RoundNumber = roundNumber;
            MatchState.Supply = MissionSettingsData.BaseSupply + MissionSettingsData.SupplyPerRound * (roundNumber - 1);
            for (int i = 0; i < _roundPips.Count; i++)
            {
                _roundPips[i].color = i < roundNumber ? RoundActiveColor : RoundInactiveColor;
            }
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

    }
}
