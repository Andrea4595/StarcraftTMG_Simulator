using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 미션 프리셋 제작 화면(2026-08-30 재구성 — 예전 이름 "미션 셋업") —
    /// 미션 이름, 미션 파라미터/점수 획득 조건/추가 조건(텍스트), 서플라이·
    /// 라운드 공식(기본 서플라이/라운드당 서플라이/라운드 길이), 전투 규모
    /// (STANDARD ENGAGEMENT / SKIRMISH LEVEL)를 입력받는다. 지도를 다루지
    /// 않으므로 MapAuthoringController보다 훨씬 단순하다 — 팬/줌/배치구역
    /// 같은 것 없이 그냥 입력 폼 하나.
    ///
    /// 배치 제작 화면과 마찬가지로 이제 게임 시작 흐름에 끼는 라이브 셋업
    /// 화면이 아니라, 나중에 Selection 화면에서 골라 쓸 "미션 프리셋"을
    /// 만들어두는 편집기 전용 화면이다 — Entry 한쪽 구석 버튼으로만 들어온다.
    /// "완료 ▶" 버튼은 이제 MissionSettingsData를 채우고 다음 단계로
    /// 넘어가는 게 아니라 그냥 Entry로 돌아가기다 — 프리셋으로 남기려면
    /// 명시적으로 "프리셋 저장" 버튼을 눌러야 한다(사용자 지정).
    ///
    /// 이 컴포넌트가 붙은 GameObject 자체가 화면 전체를 덮는 RectTransform
    /// (Canvas의 직속 자식, anchors (0,0)-(1,1))이라고 가정한다 —
    /// MapAuthoringController와 같은 방식.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MissionAuthoringController : MonoBehaviour
    {
        public event Action BackRequested;

        private const float MarginPx = 8f;
        private const float PresetListPanelWidth = 220f;
        private const float FieldWidth = 560f;

        private RectTransform Root => (RectTransform)transform;

        private TMP_InputField _missionNameField;
        private TMP_InputField _missionParametersField;
        private TMP_InputField _scoringConditionsField;
        private TMP_InputField _additionalConditionsField;
        private RectTransform _statRowContainer;

        private int _baseSupply;
        private int _supplyPerRound;
        private int _roundLength = 5;
        private string _engagementScale = MissionSettingsData.EngagementScaleStandard;
        private readonly Dictionary<string, Button> _engagementButtons = new Dictionary<string, Button>();

        private InputDialog _presetNameDialog;
        private RectTransform _presetListContent;

        private void Start()
        {
            BuildTopRow();
            BuildFormPanel();
            BuildPresetDialogs();
            BuildPresetListPanel();
            RefreshEngagementHighlight();
        }

        // ── 상단 바 ──────────────────────────────────────────────────

        private void BuildTopRow()
        {
            var rowGo = new GameObject("TopRow", typeof(RectTransform));
            rowGo.transform.SetParent(Root, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(0f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(MarginPx, -MarginPx);
            rowRect.sizeDelta = new Vector2(900f, 32f);

            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            CreateIconButton(rowRect, Resources.Load<Texture2D>("UI/BackButton"), 32f, () => BackRequested?.Invoke());
            CreateIconButton(rowRect, Resources.Load<Texture2D>("UI/SaveButton"), 32f, OnSavePresetPressed);
        }

        // ── 입력 폼 ──────────────────────────────────────────────────

        private void BuildFormPanel()
        {
            var panelGo = new GameObject("FormPanel", typeof(RectTransform));
            panelGo.transform.SetParent(Root, false);
            var panelRect = (RectTransform)panelGo.transform;
            // 화면 중앙에 정렬(사용자 요청) — 다만 우측 프리셋 목록 패널이
            // 따로 공간을 차지하고 있으니, 화면 정중앙이 아니라 "그 패널을
            // 뺀 나머지 영역"의 중앙에 오도록 우측 패널 폭의 절반만큼
            // 왼쪽으로 보정한다.
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            float topInset = MarginPx + 32f + MarginPx; // 상단 바 높이 + 여백.
            float bottomInset = MarginPx;
            float rightPanelFootprint = PresetListPanelWidth + MarginPx * 2f;
            panelRect.anchoredPosition = new Vector2(-rightPanelFootprint / 2f, (bottomInset - topInset) / 2f);
            panelRect.sizeDelta = new Vector2(FieldWidth, -(topInset + bottomInset));

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            // false로 두면(예전 버그) 레이아웃 그룹이 "다음 형제를 어디에
            // 배치할지"는 LayoutElement.preferredHeight로 계산하면서도 정작
            // 자식 자신의 RectTransform 실제 높이는 안 바꿔서, 전투 규모
            // 버튼 줄과 그 아래 스피너들이 서로 겹쳐 클릭이 엉뚱한 곳으로
            // 먹혔다(이 프로젝트에 이미 기록된 반복 함정 — ScrollListUtil.cs
            // 참고).
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            // 미션 이름은 한 줄짜리 입력칸 — 다른 3개(멀티라인)와 같은 이유로
            // 한 번만 짓고 프리셋을 불러올 때도 계속 재사용한다(CreateSingleLineField
            // 참고, 캐럿/IME 처리는 CreateMultilineField와 동일한 필요최소치).
            _missionNameField = CreateSingleLineField(panelRect, "미션 이름");

            // 스탯 행 컨테이너는 여기서 한 번만 만든다 — 실제 채우기는
            // RefreshStatRow()가 한다(IntStepperField가 값을 나중에 바꿀
            // 방법을 안 줘서 프리셋을 불러올 때마다 자식만 다시 짓는다).
            // 텍스트 입력칸 3개는 반대로 절대 다시 안 짓는다 — 한 번 만든
            // TMP_InputField를 프리셋 로드 때도 계속 재사용해야, InputDialog
            // (이름/메모 등)처럼 캐럿/선택 상태가 안전하게 유지된다(아래
            // 참고 — 예전엔 프리셋 로드 때마다 통째로 부수고 새로 지었더니
            // 캐럿이 아예 안 보이고 타이핑하면 기존 내용이 지워지는 버그가
            // 났었다).
            var statRowGo = new GameObject("StatRow", typeof(RectTransform));
            statRowGo.transform.SetParent(panelRect, false);
            var statRowLe = statRowGo.AddComponent<LayoutElement>();
            statRowLe.preferredHeight = 30f;
            var statRowLayout = statRowGo.AddComponent<VerticalLayoutGroup>();
            statRowLayout.spacing = 4f;
            statRowLayout.childControlWidth = true;
            statRowLayout.childForceExpandWidth = true;
            statRowLayout.childControlHeight = true;
            statRowLayout.childForceExpandHeight = false;
            _statRowContainer = (RectTransform)statRowGo.transform;
            RefreshStatRow();

            _missionParametersField = CreateMultilineField(panelRect, "미션 파라미터", 90f);
            _scoringConditionsField = CreateMultilineField(panelRect, "점수 획득 조건", 90f);
            _additionalConditionsField = CreateMultilineField(panelRect, "추가 조건", 90f);

            BuildEngagementRow(panelRect);
        }

        /// <summary>라운드 길이/기본 서플라이/라운드 당 서플라이를 한 줄에
        /// 나란히 보여준다 — labelLeftControlRight(IntStepperField.cs 참고)
        /// 로 라벨을 왼쪽에, 입력칸+스테퍼를 각 묶음의 오른쪽 끝에 붙이게
        /// 되면서 셋을 한 줄에 둬도 라벨이 잘리지 않게 됐다. 프리셋을
        /// 불러올 때 새 값으로 다시 부른다.</summary>
        private void RefreshStatRow()
        {
            for (int i = _statRowContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(_statRowContainer.GetChild(i).gameObject);
            }

            var row1 = CreateStatSubRow("StatRow1");
            IntStepperField.Create(row1, "라운드 길이", 95f, _roundLength, 1, 20, v => _roundLength = v, labelLeftControlRight: true);
            IntStepperField.Create(row1, "기본 서플라이", 95f, _baseSupply, 0, 999, v => _baseSupply = v, labelLeftControlRight: true);
            IntStepperField.Create(row1, "라운드 당 서플라이", 95f, _supplyPerRound, 0, 999, v => _supplyPerRound = v, labelLeftControlRight: true);
        }

        private Transform CreateStatSubRow(string name)
        {
            var rowGo = new GameObject(name, typeof(RectTransform));
            rowGo.transform.SetParent(_statRowContainer, false);
            var rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 30f;
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandHeight = false;
            return rowGo.transform;
        }

        /// <summary>이 프로세스에 전역으로 남아있는 Input.compositionString을
        /// 강제로 정리한다 — imeCompositionMode를 껐다 원래대로 되돌리면
        /// OS가 밀려있던 조합 상태를 커밋/취소하고 놓아준다. TMP_InputField.
        /// UpdateLabel()이 이 값을 "지금 이 필드가 포커스를 갖고 있는지"와
        /// 무관하게 그대로 자기 텍스트에 박아 넣는 바람에(화면을 열거나
        /// 프리셋을 불러올 때 UpdateLabel이 다시 불리면서) 엉뚱한 자모가
        /// 붙는 버그가 났다 — 그 UpdateLabel 재호출 직전에 불러서 막는다.</summary>
        private static void FlushImeComposition()
        {
            var mode = Input.imeCompositionMode;
            Input.imeCompositionMode = IMECompositionMode.Off;
            Input.imeCompositionMode = mode;
        }

        private const float MultilineScrollbarWidth = 14f;

        private static TMP_InputField CreateMultilineField(Transform parent, string labelText, float fieldHeight)
        {
            // 자동으로 계속 늘어나게 하지 않는다(사용자 요청) — TMP_InputField의
            // 자체 스크롤 계산과 우리 쪽 리사이즈가 계속 어긋나는 버그만
            // 반복해서 냈다. 대신 기본 높이의 1.5배로 고정하고, 넘치는
            // 내용은 스크롤바로 본다.
            fieldHeight *= 1.5f;

            var groupGo = new GameObject($"Field_{labelText}", typeof(RectTransform));
            groupGo.transform.SetParent(parent, false);
            var groupLayout = groupGo.AddComponent<VerticalLayoutGroup>();
            groupLayout.spacing = 4f;
            groupLayout.childControlWidth = true;
            groupLayout.childForceExpandWidth = true;
            groupLayout.childControlHeight = true;
            groupLayout.childForceExpandHeight = false;
            var groupLe = groupGo.AddComponent<LayoutElement>();
            groupLe.preferredHeight = fieldHeight + 24f;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(groupGo.transform, false);
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.preferredHeight = 18f;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = labelText;
            label.fontSize = 13f;
            label.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            label.raycastTarget = false;

            var fieldGo = new GameObject("InputField", typeof(RectTransform));
            fieldGo.transform.SetParent(groupGo.transform, false);
            var fieldLe = fieldGo.AddComponent<LayoutElement>();
            fieldLe.preferredHeight = fieldHeight;
            var bg = fieldGo.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var inputField = fieldGo.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(fieldGo.transform, false);
            var textAreaRect = (RectTransform)textAreaGo.transform;
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            // 오른쪽에 세로 스크롤바가 들어갈 자리를 비워둔다.
            textAreaRect.offsetMin = new Vector2(8f, 6f);
            textAreaRect.offsetMax = new Vector2(-8f - MultilineScrollbarWidth - 4f, -6f);
            textAreaGo.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 14f;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.text = "";
            // "타이핑하면 기존 내용이 다 지워진다" 버그의 진짜 원인 —
            // TMP_InputField의 Control Settings 중 OnFocus-Select All이 켜져
            // 있으면 포커스를 얻는 순간 텍스트 전체가 선택되고, 그 상태에서
            // 아무 키나 누르면 선택된 전체가 그 키 입력으로 치환된다(사용자가
            // 직접 인스펙터에서 찾아냄 — 아래 SetTextResetCaret이 시도했던
            // "캐럿을 텍스트 끝으로" 우회는 이 근본 원인을 안 건드려서
            // 효과가 없었다). 꺼서 막는다.
            inputField.onFocusSelectAll = false;
            // Caret 자식 게임 오브젝트가 아예 안 만들어지던 진짜 원인 —
            // TMP_InputField는 AddComponent되는 순간 OnEnable()이 즉시
            // 실행되는데, 그 시점엔 아직 textComponent/textViewport가
            // 비어 있어서(몇 줄 위에서 방금 대입했지만 이미 지나간
            // OnEnable에는 반영 안 됨) 내부적으로 Caret 자식을 만드는
            // 로직이 "텍스트 컴포넌트 없음"으로 스킵된다. 모든 참조를
            // 다 채운 지금, enabled를 껐다 켜서 OnEnable을 강제로 다시
            // 태우면 그제서야 Caret 자식이 제대로 만들어진다.
            //
            // 단, OnEnable()은 그 안에서 UpdateLabel()도 같이 부르는데(TMP_
            // InputField.cs OnEnable 끝부분), UpdateLabel()은 "지금 이 필드가
            // 포커스를 갖고 있는지"와 무관하게 프로세스 전역인 Input.
            // compositionString(다른 입력칸에서 조합 중이던, 아직 커밋 안 된
            // 한글 자모가 남아있을 수 있음)을 그대로 "<u>...</u>"로 감싸서
            // 자기 텍스트에 박아 넣어버린다 — 화면을 열자마자 "ㄴ" 같은
            // 자모가 붙어 있던 버그의 정체(실제로 겪음). 아래에서 밀려있는
            // 조합 상태를 먼저 정리한다.
            FlushImeComposition();
            inputField.enabled = false;
            inputField.enabled = true;
            // 이전엔 Caret Material이 비어 있어서 안 보이는 줄 알았는데,
            // 실제 원인은 위의 생성 순서 문제였다. 그래도 Material이
            // 비어 있는 경우를 대비해 기본 UI 머티리얼을 한 번 더
            // 보정해준다(생성 시점을 완전히 보장할 수 없으므로 포커스
            // 때마다도 재시도).
            FixCaretMaterial(inputField);
            inputField.onSelect.AddListener(_ => FixCaretMaterial(inputField));

            // 세로 스크롤바 — Unity 기본 "Input Field (TMP)"의 멀티라인
            // 프리팹과 같은 구조(배경 위에 SlidingArea, 그 안에 Handle).
            var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform));
            scrollbarGo.transform.SetParent(fieldGo.transform, false);
            var scrollbarRect = (RectTransform)scrollbarGo.transform;
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 1f);
            scrollbarRect.offsetMin = new Vector2(-MultilineScrollbarWidth - 2f, 4f);
            scrollbarRect.offsetMax = new Vector2(-2f, -4f);
            var scrollbarBg = scrollbarGo.AddComponent<Image>();
            scrollbarBg.color = new Color(0f, 0f, 0f, 0.3f);
            var scrollbar = scrollbarGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.TopToBottom;

            var slidingAreaGo = new GameObject("SlidingArea", typeof(RectTransform));
            slidingAreaGo.transform.SetParent(scrollbarRect, false);
            var slidingAreaRect = (RectTransform)slidingAreaGo.transform;
            slidingAreaRect.anchorMin = Vector2.zero;
            slidingAreaRect.anchorMax = Vector2.one;
            slidingAreaRect.offsetMin = new Vector2(2f, 2f);
            slidingAreaRect.offsetMax = new Vector2(-2f, -2f);

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(slidingAreaRect, false);
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.sizeDelta = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.6f, 0.6f, 0.6f, 0.9f);

            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImg;
            inputField.verticalScrollbar = scrollbar;
            inputField.scrollSensitivity = 5f;

            return inputField;
        }

        /// <summary>미션 이름 한 줄짜리 입력칸 — CreateMultilineField와 같은
        /// 캐럿/IME 고정 절차(FlushImeComposition → enabled 껐다 켜기 →
        /// FixCaretMaterial)를 그대로 거친다(이 프로젝트에서 bare
        /// AddComponent&lt;TMP_InputField&gt;()는 항상 이 처리가 필요하다 —
        /// 위 CreateMultilineField의 주석에 기록된 실제로 겪은 버그들 참고).
        /// 스크롤바 없이 한 줄만 받으므로 그 부분만 뺐다.</summary>
        private static TMP_InputField CreateSingleLineField(Transform parent, string labelText, float fieldHeight = 32f)
        {
            var groupGo = new GameObject($"Field_{labelText}", typeof(RectTransform));
            groupGo.transform.SetParent(parent, false);
            var groupLayout = groupGo.AddComponent<VerticalLayoutGroup>();
            groupLayout.spacing = 4f;
            groupLayout.childControlWidth = true;
            groupLayout.childForceExpandWidth = true;
            groupLayout.childControlHeight = true;
            groupLayout.childForceExpandHeight = false;
            var groupLe = groupGo.AddComponent<LayoutElement>();
            groupLe.preferredHeight = fieldHeight + 24f;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(groupGo.transform, false);
            var labelLe = labelGo.AddComponent<LayoutElement>();
            labelLe.preferredHeight = 18f;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = labelText;
            label.fontSize = 13f;
            label.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            label.raycastTarget = false;

            var fieldGo = new GameObject("InputField", typeof(RectTransform));
            fieldGo.transform.SetParent(groupGo.transform, false);
            var fieldLe = fieldGo.AddComponent<LayoutElement>();
            fieldLe.preferredHeight = fieldHeight;
            var bg = fieldGo.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            var inputField = fieldGo.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.SingleLine;

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(fieldGo.transform, false);
            var textAreaRect = (RectTransform)textAreaGo.transform;
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(8f, 4f);
            textAreaRect.offsetMax = new Vector2(-8f, -4f);
            textAreaGo.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 14f;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.text = "";
            inputField.onFocusSelectAll = false;
            FlushImeComposition();
            inputField.enabled = false;
            inputField.enabled = true;
            FixCaretMaterial(inputField);
            inputField.onSelect.AddListener(_ => FixCaretMaterial(inputField));

            return inputField;
        }

        /// <summary>TMP_InputField가 필요할 때 내부적으로 만드는 "Caret"
        /// 자식(TMP_SelectionCaret)의 Material이 코드로 직접 컴포넌트를
        /// 붙였을 때는 비어 있어서 캐럿이 안 그려지던 문제 — 사용자가
        /// 인스펙터에서 유니티 내장 "Default-Line" 머티리얼을 수동으로
        /// 넣어서 해결했었는데, 코드에서 Resources.GetBuiltinResource로
        /// 같은 걸 가져오면 이 프로젝트에서는 null이 반환된다("Failed to
        /// find Default-Line.mat" 엔진 경고 — Editor.log로 확인). 그래서
        /// "Default-Line"이라는 특정 리소스 대신, uGUI가 항상 보장하는
        /// 기본 UI 머티리얼(Graphic.defaultGraphicMaterial)을 쓴다 — 실제로
        /// 필요했던 건 그 머티리얼 자체가 아니라 material 프로퍼티에 어떤
        /// non-null 값이든 한 번 세팅해서 CanvasRenderer 초기화를 트리거하는
        /// 것으로 보인다. Caret 자식이 아직 안 만들어졌으면(GetComponentInChildren
        /// 결과가 null) 조용히 넘어간다 — 호출한 쪽에서 필요할 때 다시
        /// 부른다.</summary>
        private static void FixCaretMaterial(TMP_InputField field)
        {
            var caret = field.GetComponentInChildren<TMP_SelectionCaret>(true);
            if (caret != null)
            {
                caret.material = Graphic.defaultGraphicMaterial;
            }
        }

        private void BuildEngagementRow(Transform parent)
        {
            var rowGo = new GameObject("EngagementRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 32f;
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            foreach (var scale in new[] { MissionSettingsData.EngagementScaleStandard, MissionSettingsData.EngagementScaleSkirmish })
            {
                string captured = scale;
                var btn = CreateButton(rowGo.transform, scale, () => OnEngagementButtonPressed(captured));
                _engagementButtons[scale] = btn;
            }
        }

        private void OnEngagementButtonPressed(string scale)
        {
            _engagementScale = scale;
            RefreshEngagementHighlight();
        }

        private void RefreshEngagementHighlight()
        {
            foreach (var kv in _engagementButtons)
            {
                bool selected = kv.Key == _engagementScale;
                var img = kv.Value.GetComponent<Image>();
                if (img != null)
                {
                    img.color = selected ? new Color(0.25f, 0.45f, 0.75f, 1f) : new Color(0.3f, 0.3f, 0.3f, 1f);
                }
                var label = kv.Value.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        // ── 미션 프리셋 저장/불러오기(Missions/ 폴더) ─────────────────────

        private void BuildPresetDialogs()
        {
            _presetNameDialog = new GameObject("PresetNameDialog").AddComponent<InputDialog>();
            _presetNameDialog.transform.SetParent(Root, false);
            _presetNameDialog.Confirmed += OnPresetNameConfirmed;
        }

        private void OnSavePresetPressed()
        {
            _presetNameDialog.Open("미션 프리셋 이름", "");
        }

        private void OnPresetNameConfirmed(string name)
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            string path;
            try
            {
                path = Path.Combine(ResolveMissionsDirectory(), $"{SanitizeFileName(name)}.json");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"미션 프리셋 저장 경로를 만들 수 없습니다: {e.Message}");
                return;
            }

            try
            {
                MissionSettingsPresetIO.Save(path, _missionNameField.text, _missionParametersField.text, _scoringConditionsField.text,
                        _additionalConditionsField.text, _baseSupply, _supplyPerRound, _roundLength, _engagementScale);
                RefreshPresetList();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"미션 프리셋을 저장하지 못했습니다: {path} ({e.Message})");
            }
        }

        private void BuildPresetListPanel()
        {
            var panelGo = new GameObject("PresetList", typeof(RectTransform));
            panelGo.transform.SetParent(Root, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-MarginPx, 0f);
            panelRect.sizeDelta = new Vector2(PresetListPanelWidth, -(MarginPx * 2f));

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var title = CreateLabel(panelRect, "미션 프리셋 (클릭하면 바로 불러오기)");
            var titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.preferredHeight = 32f;

            _presetListContent = ScrollListUtil.Create(panelRect, 100f, new Color(0f, 0f, 0f, 0.15f), out _, out var scrollLe);
            scrollLe.flexibleHeight = 1f;

            RefreshPresetList();
        }

        private void RefreshPresetList()
        {
            for (int i = _presetListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_presetListContent.GetChild(i).gameObject);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(ResolveMissionsDirectory(), "*.json");
            }
            catch (Exception)
            {
                return;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var path in files)
            {
                if (!TryLoadPresetFile(path, out var preset))
                {
                    continue;
                }
                CreatePresetListItem(path, preset);
            }
        }

        private void CreatePresetListItem(string path, MissionPresetData preset)
        {
            var itemGo = new GameObject("PresetItem", typeof(RectTransform));
            itemGo.transform.SetParent(_presetListContent, false);
            var itemLayout = itemGo.AddComponent<VerticalLayoutGroup>();
            itemLayout.padding = new RectOffset(6, 6, 6, 6);
            itemLayout.spacing = 2f;
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandWidth = true;
            itemLayout.childControlHeight = true;
            itemLayout.childForceExpandHeight = false;

            var itemBg = itemGo.AddComponent<Image>();
            itemBg.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var itemBtn = itemGo.AddComponent<Button>();
            itemBtn.targetGraphic = itemBg;
            itemBtn.onClick.AddListener(() => LoadPreset(preset));

            string displayName = string.IsNullOrEmpty(preset.MissionName) ? Path.GetFileNameWithoutExtension(path) : preset.MissionName;
            var nameLabel = CreateLabel((RectTransform)itemGo.transform, displayName);
            nameLabel.fontSize = 13f;
            nameLabel.fontStyle = FontStyles.Bold;
            var nameLe = nameLabel.gameObject.AddComponent<LayoutElement>();
            nameLe.preferredHeight = 18f;

            var scaleLabel = CreateLabel((RectTransform)itemGo.transform, preset.EngagementScale);
            scaleLabel.fontSize = 11f;
            scaleLabel.color = new Color(0.7f, 0.75f, 0.85f, 1f);
            var scaleLe = scaleLabel.gameObject.AddComponent<LayoutElement>();
            scaleLe.preferredHeight = 16f;

            string preview = string.IsNullOrEmpty(preset.MissionParameters) ? "" : Truncate(preset.MissionParameters, 40);
            if (!string.IsNullOrEmpty(preview))
            {
                var previewLabel = CreateLabel((RectTransform)itemGo.transform, preview);
                previewLabel.fontSize = 11f;
                previewLabel.color = new Color(0.65f, 0.65f, 0.65f, 1f);
                var previewLe = previewLabel.gameObject.AddComponent<LayoutElement>();
                previewLe.preferredHeight = 16f;
            }
        }

        private static string Truncate(string s, int maxLen)
        {
            s = s.Replace("\n", " ").Replace("\r", "");
            return s.Length <= maxLen ? s : s.Substring(0, maxLen) + "...";
        }

        /// <summary>지금 채워진 값을 버리고 preset의 값으로 모든 입력칸/버튼
        /// 상태를 되돌린다 — 프리셋 목록 클릭이 이걸 쓴다.
        /// 텍스트 입력칸 3개는 절대 다시 안 짓고(BuildFormPanel/RefreshStatRow
        /// 참고) 항상 같은 인스턴스에 .text만 새로 넣는다 — InputDialog
        /// (이름/메모 등)가 매번 재사용하는 것과 같은 방식이라야 캐럿/선택
        /// 상태가 안전하게 유지된다.</summary>
        private void LoadPreset(MissionPresetData preset)
        {
            _baseSupply = preset.BaseSupply;
            _supplyPerRound = preset.SupplyPerRound;
            _roundLength = preset.RoundLength;
            _engagementScale = preset.EngagementScale;
            // 서플라이/라운드 길이 스피너는 IntStepperField가 각자 자기
            // 표시칸을 들고 있어서(별도 참조를 안 남겨뒀다) 값을 새로 밀어넣을
            // 방법이 없다 — 그 셋만 다시 짓는다(텍스트 입력칸은 안 건드림).
            RefreshStatRow();
            RefreshEngagementHighlight();

            // 프리셋 불러오면 내용에 엉뚱한 자모가 붙던 버그도 같은 원인
            // (FlushImeComposition 주석 참고) — .text 대입도 내부적으로
            // UpdateLabel()을 다시 태운다.
            FlushImeComposition();
            SetTextResetCaret(_missionNameField, preset.MissionName);
            SetTextResetCaret(_missionParametersField, preset.MissionParameters);
            SetTextResetCaret(_scoringConditionsField, preset.ScoringConditions);
            SetTextResetCaret(_additionalConditionsField, preset.AdditionalConditions);
        }

        /// <summary>TMP_InputField.text를 코드로 직접 대입하면 캐럿/선택 범위가
        /// 이전 상태(예: 빈 문자열이었을 때의 "전체 선택"류 내부 상태)에
        /// 그대로 남는 경우가 있어서, 그 다음 사용자가 칸을 클릭하고 아무
        /// 키나 누르면 "선택된 범위"가 통째로 새 키 입력으로 치환되며 방금
        /// 넣어준 내용이 전부 지워지는 버그가 났다(실제로 겪음). 텍스트를
        /// 넣은 직후 캐럿/선택을 텍스트 맨 끝, 선택 없음 상태로 명시적으로
        /// 되돌려서 막는다.</summary>
        private static void SetTextResetCaret(TMP_InputField field, string value)
        {
            field.text = value ?? "";
            int end = field.text.Length;
            field.caretPosition = end;
            field.selectionAnchorPosition = end;
            field.selectionFocusPosition = end;
        }

        private static bool TryLoadPresetFile(string path, out MissionPresetData preset)
        {
            preset = default;
            string jsonText;
            try
            {
                jsonText = File.ReadAllText(path);
            }
            catch (Exception)
            {
                return false;
            }
            if (!MissionSettingsPresetIO.TryLoad(jsonText, out var missionName, out var missionParameters, out var scoringConditions,
                    out var additionalConditions, out var baseSupply, out var supplyPerRound, out var roundLength,
                    out var engagementScale, out _))
            {
                return false;
            }
            preset = new MissionPresetData
            {
                MissionName = missionName,
                MissionParameters = missionParameters,
                ScoringConditions = scoringConditions,
                AdditionalConditions = additionalConditions,
                BaseSupply = baseSupply,
                SupplyPerRound = supplyPerRound,
                RoundLength = roundLength,
                EngagementScale = engagementScale,
            };
            return true;
        }

        private class MissionPresetData
        {
            public string MissionName;
            public string MissionParameters;
            public string ScoringConditions;
            public string AdditionalConditions;
            public int BaseSupply;
            public int SupplyPerRound;
            public int RoundLength;
            public string EngagementScale;
        }

        /// <summary>미션 프리셋은 맵 프리셋(Deployments/)과 별개인 Missions/
        /// 폴더에 저장한다 — 스크린샷/맵 프리셋과 같은 AppPaths.ExeDirectory()
        /// 계산을 쓴다.</summary>
        private static string ResolveMissionsDirectory()
        {
            string dir = Path.Combine(AppPaths.ExeDirectory(), "Missions");
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        // ── 공용 위젯 ────────────────────────────────────────────────

        private static TextMeshProUGUI CreateLabel(RectTransform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 12f;
            t.color = Color.white;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, float width = 160f)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(width, 32f);
            // 이 버튼이 childControlWidth/Height=true인 부모(전투 규모 토글,
            // 프리셋 목록의 무작위 로드 줄) 안에 들어갈 수도 있는데, 배경
            // Image에는 스프라이트가 없어서(순수 색칠용) ILayoutElement로서
            // preferredWidth/Height 둘 다 0을 보고한다 — LayoutElement 없이는
            // 그 부모가 이 버튼을 폭·높이 0으로 접어버려서(가로만 고치고
            // 세로를 안 고치면 폭은 있는데 높이가 0인 채로 남아 여전히
            // 안 보이는 반쪽짜리 수정이 된다 — 실제로 겪음) 배경은 사라지고
            // 텍스트만 그 0크기 밖으로 삐져나와 떠 있어 보였다. 가로·세로
            // 둘 다 preferred+flexible을 줘서 어느 축으로 눌러도 안전하게 한다.
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 1f;
            // 높이는 flexibleHeight를 안 준다 — 이 버튼이 들어가는 줄들은
            // childForceExpandHeight=false라서 굳이 필요 없는데, 실제로
            // 넣어봤더니 버튼이 줄 전체 남는 세로 공간을 다 차지해버려서
            // 세로로 거대해지는 부작용이 났다(가로축과 세로축의 cross-axis
            // 처리가 대칭이 아닌 듯— 원인을 완전히 규명하진 못했지만, 뺐더니
            // 해결됨을 확인). preferredHeight만으로 "스프라이트 없는 배경
            // Image가 0을 보고하는" 원래 문제는 충분히 고쳐진다.
            le.preferredHeight = 32f;

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
            labelText.fontSize = 13f;
            labelText.color = Color.white;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.raycastTarget = false;

            return btn;
        }

        private static Button CreateIconButton(Transform parent, Texture2D icon, float size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("IconBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(size, size);

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            return btn;
        }
    }
}
