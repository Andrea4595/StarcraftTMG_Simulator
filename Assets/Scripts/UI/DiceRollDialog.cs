using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 주사위 굴리기 툴 — 어택 풀 지정 → 히트 굴림 → 아머 굴림 → 회피 굴림, 4단계를
    /// 한 창에서 거친다. 목표치 판정이나 서지 타입 등 무기별 규칙은 전혀 모른다
    /// (이 프로젝트의 "매뉴얼 시뮬레이터, 규칙은 사람이 직접 본다" 철학 그대로) —
    /// 그저 정해진 개수만큼 주사위를 굴려서 보여주고, 플레이어가 눈으로 목표치와
    /// 비교해서 "여기까지가 성공"이라고 직접 커트라인(좌클릭)을 정하면 그 다음
    /// 풀로 넘길 뿐이다. 서지 주사위 지정(우클릭)도 마찬가지로 순수 표시 보조.
    /// 보드/BoardManager 상태와 완전히 무관한 독립 컴포넌트 — ScoreboardPanel과
    /// 같은 패턴으로 Awake()에서 자기 UI를 스스로 짓는다.
    ///
    /// 배경 전체를 덮어 클릭을 막는 모달이 아니다 — 이 창을 열어둔 채로 무기
    /// 프로필/상대 유닛의 방어·회피 정보를 동시에 봐야 한다는 사용자 요청에
    /// 따라 다른 창/보드 클릭을 막지 않는 "떠있는 비독점 참고창"으로 만들었다
    /// — 화면 아래쪽 가운데(마커바 위)에 고정으로 떠 있고, 닫기는 오직 자신의
    /// "닫기" 버튼으로만 한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DiceRollDialog : MonoBehaviour
    {
        private const int Columns = 10;
        private const int Rows = 4;
        private const int MaxPoolSize = Columns * Rows;
        private const float CellSize = 34f;
        private const float CellSpacing = 4f;
        private const float PanelWidth = 440f;

        private static readonly Color LightSquareColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        private static readonly Color DarkSquareColor = new Color(0.26f, 0.26f, 0.26f, 1f);
        private static readonly Color ActiveFaceColor = Color.white;
        private static readonly Color DisabledFaceColor = new Color(0.32f, 0.32f, 0.32f, 1f);
        private static readonly Color SurgeToggleOnColor = new Color(0.9f, 0.15f, 0.15f, 1f);
        private static readonly Color SurgeToggleOffColor = new Color(0.4f, 0.15f, 0.15f, 1f);

        private enum Stage { AttackPoolSelect, HitResult, ArmorResult, EvadeResult }

        private class Die
        {
            public int Value;
            public bool IsSurge;
        }

        private class DieCellView
        {
            public GameObject Go;
            public RawImage Face;
            public RawImage SurgeIcon;
            public TextMeshProUGUI CountLabel;
        }

        private class Snapshot
        {
            public Stage Stage;
            public List<Die> Dice;
            public Die BonusSurgeDie;
            public bool SurgeToggleOn;
            public int ConfirmedPoolSize;
            public int Threshold;
        }

        private Texture2D _squareTexture;
        private Texture2D[] _diceTextures;
        private Texture2D _surgeIconTexture;

        private Stage _stage;
        private List<Die> _dice = new List<Die>();
        private Die _bonusSurgeDie;
        private bool _surgeToggleOn;
        private int _confirmedPoolSize;
        private int _threshold = -1; // -1 = 아직 커트라인을 정하지 않음
        private readonly Stack<Snapshot> _history = new Stack<Snapshot>();

        // 아머 굴림 결과 화면은 서지 주사위(첫 줄)/일반 주사위(둘째 줄)를 시각적으로
        // 분리해 보여줘야 해서, 그리드 하나가 아니라 항상 두 줄(GridRowA/B)을
        // 만들어두고 안 쓰는 쪽은 SetActive(false)로 접는다 — 다른 화면들은
        // 전부 GridRowA 하나만 채운다.
        private RectTransform _gridRowA;
        private RectTransform _gridRowB;
        private readonly List<DieCellView> _cells = new List<DieCellView>();

        private GameObject _surgeToggleGo;
        private RawImage _surgeToggleBg;
        private RawImage _surgeToggleFace;
        private TextMeshProUGUI _activeCountLabel;

        private TextMeshProUGUI _titleLabel;
        private Button _actionButton;
        private TextMeshProUGUI _actionButtonLabel;
        private Button _undoButton;

        private void Awake()
        {
            _squareTexture = Resources.Load<Texture2D>("UI/Square");
            _diceTextures = new[]
            {
                Resources.Load<Texture2D>("UI/Dice1"),
                Resources.Load<Texture2D>("UI/Dice2"),
                Resources.Load<Texture2D>("UI/Dice3"),
                Resources.Load<Texture2D>("UI/Dice4"),
                Resources.Load<Texture2D>("UI/Dice5"),
                Resources.Load<Texture2D>("UI/Dice6"),
            };
            _surgeIconTexture = Resources.Load<Texture2D>("UI/DiceSurge");

            BuildUi();
            gameObject.SetActive(false);
        }

        public void Open()
        {
            _history.Clear();
            _dice = new List<Die>();
            _bonusSurgeDie = null;
            _surgeToggleOn = false;
            _confirmedPoolSize = 0;
            _threshold = -1;
            _stage = Stage.AttackPoolSelect;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RebuildForStage();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        // ── UI 골격 생성(한 번만) ────────────────────────────────────────

        private void BuildUi()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // 배경 Image가 없다 — 화면 전체를 덮는 좌표계로만 쓰고(Panel을
            // 화면 위쪽 가운데에 앵커시키기 위해), raycast는 안 받아서 클릭이
            // 그대로 지도/다른 창으로 통과한다.

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            // 화면 위쪽 가운데(스코어보드 바로 아래)에 고정 — 무기 프로필이
            // 이제 지도 하단에 딱 붙으므로 겹치지 않게 반대쪽(위)에 자리를
            // 잡았다(둘 다 동시에 열어두고 봐야 한다는 사용자 요청).
            panelRect.anchorMin = new Vector2(0.5f, 1f);
            panelRect.anchorMax = new Vector2(0.5f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);
            panelRect.anchoredPosition = new Vector2(0f, -(GameConstants.ScoreboardHeight + 20f));
            panelRect.sizeDelta = new Vector2(PanelWidth, 0f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.13f, 0.13f, 0.13f, 0.98f);
            // 배경이 없어졌으니 "바깥 클릭 취소"로 새어나갈 일도 없다 — 패널
            // 자체는 이미 raycastTarget=true인 Image가 있어 그 아래(지도)로
            // 클릭이 통과하지 않는 것으로 충분하다(ClickBlocker 불필요).

            var panelLayout = panelGo.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(16, 16, 14, 14);
            panelLayout.spacing = 10f;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;
            var panelFitter = panelGo.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titleLabel = CreateTitleLabel(panelRect);
            BuildGridRows(panelRect);
            BuildSurgeToggleRow(panelRect);
            BuildButtonRow(panelRect);
        }

        private static TextMeshProUGUI CreateTitleLabel(Transform parent)
        {
            var go = new GameObject("Title", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 26f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.fontSize = 18f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private void BuildGridRows(Transform parent)
        {
            var wrapperGo = new GameObject("GridWrapper", typeof(RectTransform));
            wrapperGo.transform.SetParent(parent, false);
            var wrapperLayout = wrapperGo.AddComponent<VerticalLayoutGroup>();
            wrapperLayout.spacing = CellSpacing;
            wrapperLayout.childAlignment = TextAnchor.UpperCenter;
            wrapperLayout.childControlWidth = true;
            wrapperLayout.childControlHeight = true;
            wrapperLayout.childForceExpandWidth = false;
            wrapperLayout.childForceExpandHeight = false;
            var wrapperFitter = wrapperGo.AddComponent<ContentSizeFitter>();
            wrapperFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            wrapperFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _gridRowA = BuildOneGridRow(wrapperGo.transform);
            _gridRowB = BuildOneGridRow(wrapperGo.transform);
        }

        private RectTransform BuildOneGridRow(Transform parent)
        {
            var go = new GameObject("GridRow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;

            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellSize, CellSize);
            grid.spacing = new Vector2(CellSpacing, CellSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperLeft;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = Columns * CellSize + (Columns - 1) * CellSpacing;

            return rect;
        }

        /// <summary>서지 주사위(우측 하단 빨간 사각형) — 그리드와 별개로 관리되는
        /// 유일한 셀. 어택 풀 지정 화면에서만 클릭으로 켜고 끌 수 있고, 히트 굴림
        /// 결과 화면에서는 굴려진 눈만 보여주는 표시 전용, 그 이후엔 아예 숨는다.</summary>
        private void BuildSurgeToggleRow(Transform parent)
        {
            var rowGo = new GameObject("SurgeRow", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = TextAnchor.MiddleRight;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            var rowLe = rowGo.AddComponent<LayoutElement>();
            rowLe.preferredHeight = CellSize;

            _surgeToggleGo = new GameObject("SurgeToggle", typeof(RectTransform));
            _surgeToggleGo.transform.SetParent(rowGo.transform, false);
            var rect = (RectTransform)_surgeToggleGo.transform;
            rect.sizeDelta = new Vector2(CellSize, CellSize);

            _surgeToggleBg = _surgeToggleGo.AddComponent<RawImage>();
            _surgeToggleBg.texture = _squareTexture;
            _surgeToggleBg.color = SurgeToggleOffColor;

            var faceGo = new GameObject("Face", typeof(RectTransform));
            faceGo.transform.SetParent(_surgeToggleGo.transform, false);
            var faceRect = (RectTransform)faceGo.transform;
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = Vector2.zero;
            faceRect.offsetMax = Vector2.zero;
            _surgeToggleFace = faceGo.AddComponent<RawImage>();
            _surgeToggleFace.raycastTarget = false;
            faceGo.SetActive(false);

            var btn = _surgeToggleGo.AddComponent<Button>();
            btn.targetGraphic = _surgeToggleBg;
            btn.onClick.AddListener(OnSurgeToggleClicked);

            // 서지 토글과 같은 줄, 같은 자리를 공유한다(아머/회피 결과 화면에서는
            // 서지 토글이 아예 안 쓰이니 그 자리에 활성 주사위 개수를 보여준다).
            var countGo = new GameObject("ActiveCount", typeof(RectTransform));
            countGo.transform.SetParent(rowGo.transform, false);
            var countRect = (RectTransform)countGo.transform;
            countRect.sizeDelta = new Vector2(180f, CellSize);
            _activeCountLabel = countGo.AddComponent<TextMeshProUGUI>();
            _activeCountLabel.alignment = TextAlignmentOptions.MidlineRight;
            _activeCountLabel.fontSize = 15f;
            _activeCountLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
            _activeCountLabel.raycastTarget = false;
            _activeCountLabel.text = "";
            countGo.SetActive(false);
        }

        private void BuildButtonRow(Transform parent)
        {
            var rowGo = new GameObject("Buttons", typeof(RectTransform));
            rowGo.transform.SetParent(parent, false);
            var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            (_undoButton, _) = CreateButton(rowGo.transform, "되돌리기", OnUndoClicked);
            (_actionButton, _actionButtonLabel) = CreateButton(rowGo.transform, "히트 굴림", OnActionButtonClicked);
            // 배경 클릭으로 닫는 방법이 없어졌으니(비독점 창으로 전환) 닫는
            // 수단이 이 버튼 하나뿐이다 — 빠뜨리면 창을 다시 못 닫는다.
            CreateButton(rowGo.transform, "닫기", Close);
        }

        private static (Button, TextMeshProUGUI) CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
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
            labelText.fontSize = 16f;
            labelText.color = Color.white;
            labelText.raycastTarget = false;

            return (btn, labelText);
        }

        private DieCellView CreateCell(Transform parent)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var faceGo = new GameObject("Face", typeof(RectTransform));
            faceGo.transform.SetParent(go.transform, false);
            var faceRect = (RectTransform)faceGo.transform;
            faceRect.anchorMin = Vector2.zero;
            faceRect.anchorMax = Vector2.one;
            faceRect.offsetMin = Vector2.zero;
            faceRect.offsetMax = Vector2.zero;
            var face = faceGo.AddComponent<RawImage>();

            var iconGo = new GameObject("SurgeIcon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var icon = iconGo.AddComponent<RawImage>();
            icon.texture = _surgeIconTexture;
            // DiceSurge.png는 실제 도안이 1023x867 캔버스 중 가운데 867x867
            // 정사각형에만 들어있고 양옆에 투명 여백이 있다(좌우 78px씩) —
            // 그대로 셀에 꽉 채우면 도안이 주사위보다 작아 보인다. uvRect로
            // 그 여백을 잘라내서 도안 자체가 셀을 정확히 꽉 채우게 한다.
            icon.uvRect = new Rect(78f / 1023f, 0f, 867f / 1023f, 1f);
            icon.raycastTarget = false;
            iconGo.SetActive(false);

            var labelGo = new GameObject("Count", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 15f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.black;
            label.raycastTarget = false;
            label.text = "";

            return new DieCellView { Go = go, Face = face, SurgeIcon = icon, CountLabel = label };
        }

        private void SetupCellHandlers(GameObject cellGo, System.Action onEnter, System.Action onExit, System.Action onLeftClick, System.Action onRightClick)
        {
            var handler = cellGo.AddComponent<DiceCellInputHandler>();
            handler.OnEnter = onEnter;
            handler.OnExit = onExit;
            handler.OnLeftClick = onLeftClick;
            handler.OnRightClick = onRightClick;
        }

        // ── 단계별 화면 구성 ─────────────────────────────────────────────

        private void RebuildForStage()
        {
            ClearGridCells();
            switch (_stage)
            {
                case Stage.AttackPoolSelect:
                    BuildAttackPoolSelectView();
                    break;
                case Stage.HitResult:
                    BuildHitResultView();
                    break;
                case Stage.ArmorResult:
                    BuildArmorResultView();
                    break;
                case Stage.EvadeResult:
                    BuildEvadeResultView();
                    break;
            }
            RefreshSurgeToggleVisual();
            _activeCountLabel.gameObject.SetActive(_stage == Stage.ArmorResult || _stage == Stage.EvadeResult);
            RefreshActionButton();
            RefreshUndoButton();
            RefreshTitle();
        }

        private void ClearGridCells()
        {
            foreach (var cell in _cells)
            {
                if (cell.Go != null)
                {
                    Destroy(cell.Go);
                }
            }
            _cells.Clear();
        }

        private void BuildAttackPoolSelectView()
        {
            _gridRowB.gameObject.SetActive(false);
            for (int i = 0; i < MaxPoolSize; i++)
            {
                int index = i;
                var cell = CreateCell(_gridRowA);
                cell.Face.texture = _squareTexture;
                SetupCellHandlers(cell.Go,
                        onEnter: () => RefreshPoolDisplay(index),
                        onExit: () => RefreshPoolDisplay(_confirmedPoolSize > 0 ? _confirmedPoolSize - 1 : -1),
                        onLeftClick: () => ConfirmPoolSize(index + 1),
                        onRightClick: null);
                _cells.Add(cell);
            }
            RefreshPoolDisplay(_confirmedPoolSize > 0 ? _confirmedPoolSize - 1 : -1);
        }

        private void RefreshPoolDisplay(int highlightLastIndex)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                bool lit = highlightLastIndex < 0 || i <= highlightLastIndex;
                _cells[i].Face.color = lit ? LightSquareColor : DarkSquareColor;
                _cells[i].CountLabel.text = (i == highlightLastIndex) ? (highlightLastIndex + 1).ToString() : "";
            }
        }

        private void ConfirmPoolSize(int size)
        {
            _confirmedPoolSize = Mathf.Clamp(size, 1, MaxPoolSize);
            RefreshPoolDisplay(_confirmedPoolSize - 1);
            RefreshActionButton();
        }

        /// <summary>히트 굴림 결과 — "이 주사위까지가 명중"이라는 판정이라
        /// 좌클릭한 지점 뒤(오른쪽)가 비활성화된다("접미사 방식"). 우클릭으로
        /// 첫 주사위부터 서지 주사위 지정도 여기서만 가능하다.</summary>
        private void BuildHitResultView()
        {
            _gridRowB.gameObject.SetActive(false);
            for (int i = 0; i < _dice.Count; i++)
            {
                int index = i;
                var die = _dice[i];
                var cell = CreateCell(_gridRowA);
                cell.Face.texture = _diceTextures[Mathf.Clamp(die.Value - 1, 0, 5)];
                cell.SurgeIcon.gameObject.SetActive(die.IsSurge);
                SetupCellHandlers(cell.Go,
                        onEnter: () => PreviewSuffixDisable(index),
                        onExit: () => PreviewSuffixDisable(_threshold > 0 ? _threshold - 1 : -1),
                        onLeftClick: () => ConfirmSuffixThreshold(index + 1),
                        onRightClick: () => ToggleSurgeBoundary(index));
                _cells.Add(cell);
            }
            PreviewSuffixDisable(_threshold > 0 ? _threshold - 1 : -1);
        }

        /// <summary>아머 굴림 결과 — 서지 주사위(이미 아머롤을 건너뛴 것들)는
        /// 첫 줄에, 나머지(진짜로 아머 판정을 받는 주사위)는 둘째 줄에 따로
        /// 보여준다(사용자 요청). 둘째 줄에서 좌클릭/호버는 "그 줄의 첫
        /// 주사위부터 클릭한 주사위까지"를 비활성화한다("접두사 방식") —
        /// 방어(아머) 판정에 성공해 데미지 풀로 안 넘어가는 주사위들을
        /// 표시하는 굴림이라는 의미. 첫 줄(서지)은 이 화면에서 아예 조작
        /// 대상이 아니다.</summary>
        private void BuildArmorResultView()
        {
            int surgeCount = _dice.Count(d => d.IsSurge);
            for (int i = 0; i < _dice.Count; i++)
            {
                int index = i;
                var die = _dice[i];
                bool isRow2 = index >= surgeCount;
                var cell = CreateCell(isRow2 ? _gridRowB : _gridRowA);
                cell.Face.texture = _diceTextures[Mathf.Clamp(die.Value - 1, 0, 5)];
                cell.SurgeIcon.gameObject.SetActive(die.IsSurge);
                if (isRow2)
                {
                    int localIndex = index - surgeCount;
                    SetupCellHandlers(cell.Go,
                            onEnter: () => PreviewPrefixDisable(surgeCount, localIndex),
                            onExit: () => PreviewPrefixDisable(surgeCount, _threshold - 1),
                            onLeftClick: () => ConfirmPrefixThreshold(surgeCount, localIndex + 1),
                            onRightClick: null);
                }
                _cells.Add(cell);
            }
            _gridRowB.gameObject.SetActive(_dice.Count > surgeCount);
            PreviewPrefixDisable(surgeCount, _threshold - 1);
        }

        /// <summary>회피 굴림 결과 — 아머 화면의 "둘째 줄" 규칙을 화면 전체에
        /// 적용한 것과 같다(서지 구분이 이미 없어졌으므로 전체가 그 "둘째
        /// 줄"). 첫 주사위부터 마우스가 올라간/클릭한 주사위까지 비활성화.</summary>
        private void BuildEvadeResultView()
        {
            _gridRowB.gameObject.SetActive(false);
            for (int i = 0; i < _dice.Count; i++)
            {
                int index = i;
                var die = _dice[i];
                var cell = CreateCell(_gridRowA);
                cell.Face.texture = _diceTextures[Mathf.Clamp(die.Value - 1, 0, 5)];
                SetupCellHandlers(cell.Go,
                        onEnter: () => PreviewPrefixDisable(0, index),
                        onExit: () => PreviewPrefixDisable(0, _threshold - 1),
                        onLeftClick: () => ConfirmPrefixThreshold(0, index + 1),
                        onRightClick: null);
                _cells.Add(cell);
            }
            PreviewPrefixDisable(0, _threshold - 1);
        }

        /// <summary>"이 지점까지가 성공/유효"라는 접미사 방식 — 클릭한 인덱스
        /// 뒤(오른쪽)가 비활성화된다. 어택 풀 지정(RefreshPoolDisplay)과 완전히
        /// 같은 판정이라 히트 굴림 결과 화면에서만 쓴다.</summary>
        private void PreviewSuffixDisable(int highlightLastIndex)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                bool active = highlightLastIndex < 0 || i <= highlightLastIndex;
                _cells[i].Face.color = active ? ActiveFaceColor : DisabledFaceColor;
            }
        }

        private void ConfirmSuffixThreshold(int size)
        {
            _threshold = Mathf.Clamp(size, 1, _cells.Count);
            PreviewSuffixDisable(_threshold - 1);
            RefreshActionButton();
        }

        /// <summary>"그룹의 시작부터 이 지점까지"가 비활성화되는 접두사 방식 —
        /// groupStartIndex는 전체 _cells 기준 시작 인덱스(아머 화면의 둘째 줄은
        /// surgeCount, 회피 화면은 항상 0), highlightLastLocalIndex는 그
        /// 그룹 안에서의 상대 인덱스. 아무 것도 선택 안 한 상태(-1 이하)는
        /// "0개 비활성화"라는 유효한 기본값이다 — 방어/회피가 하나도 성공하지
        /// 않았을 수도 있으므로.</summary>
        private void PreviewPrefixDisable(int groupStartIndex, int highlightLastLocalIndex)
        {
            int disabledCount = 0;
            for (int i = 0; i < _cells.Count; i++)
            {
                bool disabled = i >= groupStartIndex && (i - groupStartIndex) <= highlightLastLocalIndex;
                _cells[i].Face.color = disabled ? DisabledFaceColor : ActiveFaceColor;
                if (disabled)
                {
                    disabledCount++;
                }
            }
            _activeCountLabel.text = $"활성 주사위: {_cells.Count - disabledCount}개";
        }

        private void ConfirmPrefixThreshold(int groupStartIndex, int count)
        {
            _threshold = Mathf.Max(count, 0);
            PreviewPrefixDisable(groupStartIndex, _threshold - 1);
        }

        /// <summary>우클릭한 주사위까지(첫 번째 주사위부터) 서지 주사위로
        /// 고정한다 — 이미 그 지점까지 정확히 고정돼 있으면(같은 주사위를 다시
        /// 우클릭) 전부 해제한다. 항상 "앞에서부터 N개" 형태의 연속 구간이므로
        /// 별도 경계 필드 없이 현재 고정된 개수만 세면 된다.</summary>
        private void ToggleSurgeBoundary(int index)
        {
            int currentCount = _dice.Count(d => d.IsSurge);
            int newBoundary = (currentCount == index + 1) ? 0 : index + 1;
            for (int i = 0; i < _dice.Count; i++)
            {
                _dice[i].IsSurge = i < newBoundary;
            }
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].SurgeIcon.gameObject.SetActive(_dice[i].IsSurge);
            }
        }

        private void OnSurgeToggleClicked()
        {
            if (_stage != Stage.AttackPoolSelect)
            {
                return;
            }
            _surgeToggleOn = !_surgeToggleOn;
            _surgeToggleBg.color = _surgeToggleOn ? SurgeToggleOnColor : SurgeToggleOffColor;
        }

        private void RefreshSurgeToggleVisual()
        {
            switch (_stage)
            {
                case Stage.AttackPoolSelect:
                    _surgeToggleGo.SetActive(true);
                    _surgeToggleBg.enabled = true;
                    _surgeToggleFace.gameObject.SetActive(false);
                    _surgeToggleBg.color = _surgeToggleOn ? SurgeToggleOnColor : SurgeToggleOffColor;
                    break;
                case Stage.HitResult:
                    _surgeToggleGo.SetActive(_bonusSurgeDie != null);
                    if (_bonusSurgeDie != null)
                    {
                        // Dice*.png는 흰색 실루엣 + 눈금 부분만 알파 구멍인
                        // 텍스처라, 흰색으로 칠하면 "흰 배경에 (뒤에 있던 빨간
                        // 사각형이 비쳐서) 빨간 눈금"이 된다. 원하는 "빨간
                        // 배경에 투명 눈금"은 반대로 — 뒤의 빨간 사각형(Bg)을
                        // 끄고 텍스처 자체를 빨갛게 칠하면, 몸통은 빨갛게
                        // 채워지고 눈금 구멍은 진짜로 투명해져 패널의 어두운
                        // 배경이 그대로 비친다.
                        _surgeToggleBg.enabled = false;
                        _surgeToggleFace.gameObject.SetActive(true);
                        _surgeToggleFace.color = SurgeToggleOnColor;
                        _surgeToggleFace.texture = _diceTextures[Mathf.Clamp(_bonusSurgeDie.Value - 1, 0, 5)];
                    }
                    break;
                default:
                    _surgeToggleGo.SetActive(false);
                    break;
            }
        }

        private void RefreshActionButton()
        {
            switch (_stage)
            {
                case Stage.AttackPoolSelect:
                    _actionButton.gameObject.SetActive(true);
                    _actionButtonLabel.text = "히트 굴림";
                    _actionButton.interactable = _confirmedPoolSize > 0;
                    break;
                case Stage.HitResult:
                    _actionButton.gameObject.SetActive(true);
                    _actionButtonLabel.text = "아머 굴림";
                    _actionButton.interactable = _threshold > 0;
                    break;
                case Stage.ArmorResult:
                    // 접두사 방식이라 "0개 비활성화"(방어 성공 없음)도 유효한
                    // 선택이므로, 히트 결과 화면과 달리 클릭 여부와 무관하게
                    // 항상 눌러서 다음으로 넘어갈 수 있다.
                    _actionButton.gameObject.SetActive(true);
                    _actionButtonLabel.text = "회피 굴림";
                    _actionButton.interactable = true;
                    break;
                case Stage.EvadeResult:
                    _actionButton.gameObject.SetActive(false);
                    break;
            }
        }

        private void RefreshUndoButton()
        {
            _undoButton.interactable = _history.Count > 0;
        }

        private void RefreshTitle()
        {
            _titleLabel.text = _stage switch
            {
                Stage.AttackPoolSelect => "어택 풀 지정",
                Stage.HitResult => "히트 굴림 결과",
                Stage.ArmorResult => "아머 굴림 결과",
                Stage.EvadeResult => "회피 굴림 결과",
                _ => "",
            };
        }

        // ── 단계 전환 ────────────────────────────────────────────────────

        private void OnActionButtonClicked()
        {
            switch (_stage)
            {
                case Stage.AttackPoolSelect:
                    DoHitRoll();
                    break;
                case Stage.HitResult:
                    DoArmorRoll();
                    break;
                case Stage.ArmorResult:
                    DoEvadeRoll();
                    break;
            }
        }

        private void PushSnapshot()
        {
            _history.Push(new Snapshot
            {
                Stage = _stage,
                Dice = _dice.Select(d => new Die { Value = d.Value, IsSurge = d.IsSurge }).ToList(),
                BonusSurgeDie = _bonusSurgeDie == null ? null : new Die { Value = _bonusSurgeDie.Value },
                SurgeToggleOn = _surgeToggleOn,
                ConfirmedPoolSize = _confirmedPoolSize,
                Threshold = _threshold,
            });
        }

        private void DoHitRoll()
        {
            if (_confirmedPoolSize <= 0)
            {
                return;
            }
            PushSnapshot();
            _dice = new List<Die>();
            for (int i = 0; i < _confirmedPoolSize; i++)
            {
                _dice.Add(new Die { Value = Random.Range(1, 7), IsSurge = false });
            }
            _bonusSurgeDie = _surgeToggleOn ? new Die { Value = Random.Range(1, 7) } : null;
            SortDiceDescending();
            _threshold = -1;
            _stage = Stage.HitResult;
            RebuildForStage();
        }

        private void DoArmorRoll()
        {
            if (_threshold <= 0)
            {
                return;
            }
            PushSnapshot();
            _dice = _dice.Take(_threshold).ToList();
            foreach (var die in _dice)
            {
                if (!die.IsSurge)
                {
                    die.Value = Random.Range(1, 7);
                }
            }
            // 서지 주사위(첫 줄)를 앞으로, 나머지(둘째 줄)는 눈 내림차순으로 —
            // BuildArmorResultView가 이 순서를 그대로 두 줄로 나눠 그린다.
            _dice = _dice.OrderByDescending(d => d.IsSurge).ThenByDescending(d => d.Value).ToList();
            _bonusSurgeDie = null;
            _threshold = -1;
            _stage = Stage.ArmorResult;
            RebuildForStage();
        }

        /// <summary>아머 굴림 결과 화면에서 넘어온다 — 서지 주사위(첫 줄)는
        /// 이미 아머롤을 건너뛰었으므로 전부 그대로 데미지 풀(=회피 굴림)로
        /// 넘어가고, 둘째 줄(일반 주사위) 중 방금 정한 접두사 구간(방어 성공,
        /// _threshold개)만 제외한 나머지가 넘어간다. _threshold가 0(아무 것도
        /// 선택 안 함)이어도 유효한 선택 — "방어에 아무 것도 성공 못 함".</summary>
        private void DoEvadeRoll()
        {
            PushSnapshot();
            int surgeCount = _dice.Count(d => d.IsSurge);
            int disabledInRow2 = Mathf.Max(_threshold, 0);
            var keep = new List<Die>();
            keep.AddRange(_dice.Take(surgeCount));
            keep.AddRange(_dice.Skip(surgeCount).Skip(disabledInRow2));
            _dice = keep;
            foreach (var die in _dice)
            {
                die.Value = Random.Range(1, 7);
                die.IsSurge = false;
            }
            SortDiceDescending();
            _threshold = -1;
            _stage = Stage.EvadeResult;
            RebuildForStage();
        }

        private void SortDiceDescending()
        {
            _dice = _dice.OrderByDescending(d => d.Value).ToList();
        }

        private void OnUndoClicked()
        {
            if (_history.Count == 0)
            {
                return;
            }
            var snap = _history.Pop();
            _stage = snap.Stage;
            _dice = snap.Dice;
            _bonusSurgeDie = snap.BonusSurgeDie;
            _surgeToggleOn = snap.SurgeToggleOn;
            _confirmedPoolSize = snap.ConfirmedPoolSize;
            _threshold = snap.Threshold;
            RebuildForStage();
        }

        /// <summary>주사위 칸 하나의 호버/좌클릭/우클릭을 알려준다 — Button은
        /// 좌클릭만 다루므로 MarkerBase/TacticalCardClickHandler와 같은 패턴으로
        /// IPointerDownHandler를 직접 구현해 버튼을 구분한다.</summary>
        private class DiceCellInputHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
        {
            public System.Action OnEnter;
            public System.Action OnExit;
            public System.Action OnLeftClick;
            public System.Action OnRightClick;

            public void OnPointerEnter(PointerEventData eventData)
            {
                OnEnter?.Invoke();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                OnExit?.Invoke();
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    OnLeftClick?.Invoke();
                    eventData.Use();
                }
                else if (eventData.button == PointerEventData.InputButton.Right)
                {
                    OnRightClick?.Invoke();
                    eventData.Use();
                }
            }
        }
    }
}
