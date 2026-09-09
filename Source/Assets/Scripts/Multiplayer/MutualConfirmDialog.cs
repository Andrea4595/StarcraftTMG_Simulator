using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 씬 전환 상호 확인창(2026-09-09 신설, 사용자 지정) — CardDraft→
    /// TerrainSetup, TerrainSetup→GameBoard 두 전환 앞에서, 양쪽 다 각자
    /// "준비 완료"를 눌러야 실제로 넘어간다. 누구든 "다음"/"게임 시작"을
    /// 누르면 이 창이 양쪽 화면에 동시에 뜬다(RolloffDialog/DiceRollDialog가
    /// 이미 쓰는 "열림/닫힘 자체를 방송하는" 패턴 재사용 — 안 그러면 누른
    /// 쪽만 창을 보고 상대는 아무 반응 없는 화면에 남는다). 왼쪽=A/오른쪽=B
    /// 버튼은 각자 자기 팀 것만 조작 가능(사용자 지정) — 누르면 초록,
    /// 다시 누르면 꺼지는 토글이고, 둘 다 초록이면 실제 전환 RPC(기존에
    /// 버튼이 직접 부르던 RequestProceedToTerrainServerRpc/
    /// RequestStartGameServerRpc)를 호스트만 한 번 쏜다 — 양쪽이 같은 방송을
    /// 각자 독립적으로 받아 이 조건에 동시 도달하므로, 걸러주지 않으면 두
    /// 번 쏘게 된다. 우측 위 X는 양쪽 다 창을 닫고 두 토글을 모두
    /// 리셋한다(사용자 지정 — "취소"에 가까운 의미, 다시 열면 둘 다 새로
    /// 눌러야 함).
    ///
    /// MultiplayerConnectDialog/DisconnectNoticeController와 같은 방식으로
    /// GameFlowBootstrap이 앱 시작 시 한 번만 만들고 DontDestroyOnLoad로
    /// 유지한다 — CardDraft/TerrainSetup 어느 씬에 있든 떠야 하므로.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MutualConfirmDialog : MonoBehaviour
    {
        public static MutualConfirmDialog Instance { get; private set; }

        public const string ContextCardDraftToTerrain = "CardDraftToTerrain";
        public const string ContextTerrainToGameBoard = "TerrainToGameBoard";

        private static readonly Color ReadyColor = new Color(0.25f, 0.65f, 0.3f, 1f);
        private static readonly Color NotReadyColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        private static readonly Color DisabledColor = new Color(0.14f, 0.14f, 0.14f, 1f);
        private const float ButtonRowHeight = 56f;

        private string _context;
        private bool _readyA;
        private bool _readyB;
        private bool _transitionFired;

        private GameObject _panelGo;
        private TextMeshProUGUI _titleLabel;
        private Image _buttonAImage;
        private Image _buttonBImage;
        private Button _buttonA;
        private Button _buttonB;

        private void Awake()
        {
            Instance = this;
            BuildUi();
            _panelGo.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ── 요청(로컬 클릭 → 방송) ───────────────────────────────────────

        /// <summary>"다음"/"게임 시작" 버튼이 부른다 — 자기 혼자 여는 게
        /// 아니라 상대 화면에도 똑같이 뜨도록 방송을 요청한다.</summary>
        public void RequestOpen(string context)
        {
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[MutualConfirmDialog] BoardNetworkSync.Instance가 없음 — 확인창을 못 엶");
                return;
            }
            BoardNetworkSync.Instance.RequestOpenMutualConfirmServerRpc(context);
        }

        private void OnReadyButtonClicked(string team)
        {
            if (_context == null || team != NetworkTeam.LocalTeam())
            {
                return;
            }
            bool newValue = team == NetworkTeam.Host ? !_readyA : !_readyB;
            BoardNetworkSync.Instance?.RequestSetMutualReadyServerRpc(_context, team, newValue);
        }

        private void OnCloseButtonClicked()
        {
            if (_context == null)
            {
                return;
            }
            BoardNetworkSync.Instance?.RequestCloseMutualConfirmServerRpc(_context);
        }

        // ── 방송 반영(BoardNetworkSync가 부름, 누른 쪽 자신도 포함) ───────

        internal void OpenLocal(string context)
        {
            _context = context;
            _readyA = false;
            _readyB = false;
            _transitionFired = false;
            _titleLabel.text = ResolveMessage(context);

            string localTeam = NetworkTeam.LocalTeam();
            _buttonA.interactable = localTeam == NetworkTeam.Host;
            _buttonB.interactable = localTeam == NetworkTeam.Client;

            RefreshButtons();
            _panelGo.SetActive(true);
            transform.SetAsLastSibling();
        }

        internal void ApplyRemoteReady(string context, string team, bool ready)
        {
            if (context != _context)
            {
                return;
            }
            if (team == NetworkTeam.Host)
            {
                _readyA = ready;
            }
            else
            {
                _readyB = ready;
            }
            RefreshButtons();

            if (_readyA && _readyB && !_transitionFired)
            {
                _transitionFired = true;
                string firedContext = _context;
                CloseLocalAndReset();
                if (NetworkTeam.LocalTeam() == NetworkTeam.Host)
                {
                    FireContextAction(firedContext);
                }
            }
        }

        internal void CloseRemote(string context)
        {
            if (context != _context)
            {
                return;
            }
            CloseLocalAndReset();
        }

        /// <summary>상대가 이탈해 연결이 끊기면(DisconnectNoticeController)
        /// 이 확인창이 열려 있어도 의미가 없으니 같이 정리한다 — 방송을
        /// 기다리지 않고 즉시 로컬에서만 닫는다(연결이 이미 끊긴 뒤라
        /// 방송할 상대가 없음).</summary>
        internal void ForceCloseOnDisconnect()
        {
            if (_context != null)
            {
                CloseLocalAndReset();
            }
        }

        private void CloseLocalAndReset()
        {
            _context = null;
            _readyA = false;
            _readyB = false;
            _panelGo.SetActive(false);
        }

        private static void FireContextAction(string context)
        {
            if (BoardNetworkSync.Instance == null)
            {
                return;
            }
            switch (context)
            {
                case ContextCardDraftToTerrain:
                    BoardNetworkSync.Instance.RequestProceedToTerrainServerRpc();
                    break;
                case ContextTerrainToGameBoard:
                    BoardNetworkSync.Instance.RequestStartGameServerRpc();
                    break;
            }
        }

        private static string ResolveMessage(string context)
        {
            switch (context)
            {
                case ContextCardDraftToTerrain:
                    return "지형 배치 화면으로 넘어갈까요?";
                case ContextTerrainToGameBoard:
                    return "게임을 시작할까요?";
                default:
                    return "다음 화면으로 넘어갈까요?";
            }
        }

        /// <summary>상대 쪽 버튼은(아직 준비 완료 전이면) 조작 불가임을
        /// 눈으로 알 수 있게 더 어둡게 칠한다(사용자 지정, 2026-09-09) —
        /// 이미 초록(준비 완료)이면 그 자체로 상태가 분명하므로 어둡게
        /// 처리하지 않는다.</summary>
        private void RefreshButtons()
        {
            string localTeam = NetworkTeam.LocalTeam();
            _buttonAImage.color = _readyA ? ReadyColor : (localTeam == NetworkTeam.Host ? NotReadyColor : DisabledColor);
            _buttonBImage.color = _readyB ? ReadyColor : (localTeam == NetworkTeam.Client ? NotReadyColor : DisabledColor);
        }

        // ── UI 구성(DisconnectNoticeController와 같은 모달 뼈대) ─────────

        private void BuildUi()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _panelGo = new GameObject("Panel", typeof(RectTransform));
            _panelGo.transform.SetParent(transform, false);
            var panelRoot = (RectTransform)_panelGo.transform;
            panelRoot.anchorMin = Vector2.zero;
            panelRoot.anchorMax = Vector2.one;
            panelRoot.offsetMin = Vector2.zero;
            panelRoot.offsetMax = Vector2.zero;

            var bg = _panelGo.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.6f);

            var boxGo = new GameObject("Box", typeof(RectTransform));
            boxGo.transform.SetParent(_panelGo.transform, false);
            var boxRect = (RectTransform)boxGo.transform;
            boxRect.anchorMin = new Vector2(0.5f, 0.5f);
            boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.pivot = new Vector2(0.5f, 0.5f);
            boxRect.sizeDelta = new Vector2(360f, 0f);
            var boxImage = boxGo.AddComponent<Image>();
            boxImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);

            var layout = boxGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 16);
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fitter = boxGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titleLabel = CreateLabel(boxGo.transform, "", 16f);
            _titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(boxGo.transform, false);
            var buttonRowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            buttonRowLayout.spacing = 12f;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childForceExpandWidth = true;
            buttonRowLayout.childControlHeight = true;
            buttonRowLayout.childForceExpandHeight = false;
            buttonRow.AddComponent<LayoutElement>().preferredHeight = ButtonRowHeight;

            _buttonA = CreateReadyButton(buttonRow.transform, NetworkTeam.Host, out _buttonAImage);
            _buttonA.onClick.AddListener(() => OnReadyButtonClicked(NetworkTeam.Host));
            _buttonB = CreateReadyButton(buttonRow.transform, NetworkTeam.Client, out _buttonBImage);
            _buttonB.onClick.AddListener(() => OnReadyButtonClicked(NetworkTeam.Client));

            // 오른쪽 위 X — DisconnectNoticeController와 같은 위치/모양.
            var closeGo = new GameObject("CloseButton", typeof(RectTransform));
            closeGo.transform.SetParent(boxGo.transform, false);
            var closeRect = (RectTransform)closeGo.transform;
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-6f, -6f);
            closeRect.sizeDelta = new Vector2(24f, 24f);
            closeGo.AddComponent<LayoutElement>().ignoreLayout = true;

            var closeImg = closeGo.AddComponent<Image>();
            closeImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            closeBtn.onClick.AddListener(OnCloseButtonClicked);

            var closeLabelGo = new GameObject("Label", typeof(RectTransform));
            closeLabelGo.transform.SetParent(closeGo.transform, false);
            var closeLabelRect = (RectTransform)closeLabelGo.transform;
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;
            var closeText = closeLabelGo.AddComponent<TextMeshProUGUI>();
            // "×"(U+00D7)는 프로젝트 폰트 아틀라스에 없는 글리프라 ASCII
            // "X"로 대체한다(DisconnectNoticeController와 같은 이유).
            closeText.text = "X";
            closeText.alignment = TextAlignmentOptions.Center;
            closeText.fontSize = 16f;
            closeText.color = Color.white;
            closeText.raycastTarget = false;
        }

        /// <summary>team쪽 "준비 완료" 버튼 — 로컬 팀 것만 실제로 클릭
        /// 가능하다(OpenLocal이 매번 interactable을 다시 세팅). Button.
        /// transition을 꺼서(None) Unity 기본 색 전환이 RefreshButtons의
        /// 초록/회색 수동 색칠과 충돌하지 않게 한다 — interactable=false여도
        /// 색이 안 바뀌므로 RefreshButtons가 계속 진짜 상태를 보여준다.</summary>
        private static Button CreateReadyButton(Transform parent, string team, out Image image)
        {
            var go = new GameObject($"ReadyButton_{team}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            // buttonRow의 HorizontalLayoutGroup이 childControlHeight=true +
            // childForceExpandHeight=false라, LayoutElement가 없는 자식은
            // preferredHeight가 0으로 계산돼 Image/Button 영역이 완전히
            // 찌그러진다(실제 버그로 발견 — 배경 없이 글자만 떠 있고 클릭도
            // 안 먹던 원인). 행 자체와 같은 높이를 명시해준다.
            go.AddComponent<LayoutElement>().preferredHeight = ButtonRowHeight;

            var img = go.AddComponent<Image>();
            img.color = NotReadyColor;
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.targetGraphic = img;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = $"{PlayerIdentity.DisplayName(team)}\n준비 완료";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 14f;
            text.color = Color.white;
            text.raycastTarget = false;

            image = img;
            return btn;
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }
    }
}
