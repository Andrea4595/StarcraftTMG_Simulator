using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 상대방과의 연결이 끊겼을 때(호스트 입장에선 클라이언트 이탈, 클라이언트
    /// 입장에선 호스트와의 연결 상실 — 둘 다 NetworkManager.OnClientDisconnectCallback으로
    /// 온다) 뜨는 알림 모달(2026-09-02 신설). 어느 씬에 있든(CardPrep/
    /// CardDraft/TerrainSetup/GameBoard) 떠야 하므로 EventSystem/NetworkManager와
    /// 같은 방식으로 GameFlowBootstrap이 앱 시작 시 한 번만 만들고
    /// DontDestroyOnLoad로 유지한다 — 자기 자신의 Canvas를 따로 갖는다(현재
    /// 씬의 캔버스에 얹혀사는 게 아니라).
    ///
    /// 사용자 지정 두 가지 경우:
    /// - GameBoard 중: "연결이 종료되었습니다." + "저장"(현재 판을 저장,
    ///   BoardManager.RequestSave 재사용) + "나가기"(Entry로).
    /// - 그 전 단계(CardPrep/CardDraft/TerrainSetup): 저장할 게 아직 없으므로
    ///   같은 메시지 + "나가기"만.
    ///
    /// "진짜 이탈"과 "MultiplayerConnectDialog에서 호스트를 취소/뒤로가기로
    /// 정리하는 것"을 구분해야 한다 — 후자도 내부적으로 NetworkManager.Shutdown()을
    /// 부르므로 OnClientDisconnectCallback이 걸릴 수 있지만, 그건 아직 상대와
    /// 제대로 2명이 붙기 *전*이다. 그래서 이 컴포넌트도 독립적으로
    /// "한 번이라도 2명이 다 붙은 적이 있는지"(_wasFullyConnected)를 추적해서,
    /// 그게 true였을 때만 진짜 알림을 띄운다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DisconnectNoticeController : MonoBehaviour
    {
        /// <summary>내가 직접 나가는(마커바 "나가기" → BoardManager.
        /// OnExitConfirmed) 경우처럼, 스스로 NetworkManager.Shutdown()을
        /// 부르기 직전에 켜둔다(사용자 요청, 2026-09-02 — "나간 사람"
        /// 본인에게는 이 알림이 뜨면 안 됨). Shutdown() 이후 내 자신의
        /// OnClientDisconnectCallback이 오면(오지 않을 수도 있다 — NGO가
        /// 자기 자신의 셧다운에도 이 콜백을 부르는지는 역할/버전에 따라
        /// 다를 수 있어 방어적으로 처리) 그 한 번만 걸러내고 바로 끈다.</summary>
        public static bool SuppressNextNotice;

        /// <summary>연결된 상태에서 스스로 다른 화면으로 나갈 때 공통으로
        /// 부른다(GameBoard "나가기"뿐 아니라 CardPrep/TerrainSetup의
        /// "뒤로가기"도 포함 — 2026-09-09 버그 수정: 이 화면들의 뒤로가기가
        /// SceneManager.LoadScene만 부르고 NetworkManager는 그대로 둬서,
        /// 미션 세팅 중 한쪽이 나가도 연결이 안 끊기던 문제. 나 자신에게는
        /// "연결 종료" 알림이 뜨면 안 되므로 Shutdown() 전에
        /// SuppressNextNotice를 세워둔다. 연결 중이 아니면 아무 일도
        /// 안 한다.</summary>
        public static void LeaveMultiplayerSessionIfConnected()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                SuppressNextNotice = true;
                NetworkManager.Singleton.Shutdown();
            }
        }

        private bool _wasFullyConnected;

        private GameObject _panelGo;
        private GameObject _saveButtonGo;

        private void Awake()
        {
            BuildUi();

            var nm = NetworkManager.Singleton;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;

            // 캔버스 루트 자신(이 컴포넌트가 붙은 GameObject)은 항상 켜져
            // 있고, 실제로 보였다 안 보였다 하는 건 안쪽 패널뿐이다.
            _panelGo.SetActive(false);
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton.ConnectedClients.Count >= 2)
            {
                _wasFullyConnected = true;
                // 이전 세션에서 혹시 못 소비된 채 남아있었을 수도 있는 플래그를
                // 새 세션 시작 시점에 확실히 초기화한다(방어적 처리 — 원래
                // OnClientDisconnected가 소비하지만, 그게 한 번도 안 불렸을
                // 가능성에 대비).
                SuppressNextNotice = false;
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (SuppressNextNotice)
            {
                // 내가 직접 나간 것 — 알림을 안 띄우고 상태만 정리한다.
                SuppressNextNotice = false;
                _wasFullyConnected = false;
                return;
            }
            if (!_wasFullyConnected)
            {
                return; // 아직 상대와 제대로 붙은 적이 없다 — 연결 시도/취소 중일 뿐, 진짜 이탈이 아니다.
            }
            _wasFullyConnected = false;
            UnlockUndoHistoryIfOnGameBoard();
            MutualConfirmDialog.Instance?.ForceCloseOnDisconnect();
            ShowNotice();
        }

        /// <summary>진짜 상대 이탈이 확정된 시점(위)에 한 번 부른다 —
        /// LockExistingUndoHistoryForMidGameJoin이 건 잠금을 푼다. 잠금은
        /// "멀티 플레이 중 사고 방지" 목적으로만 걸리므로(사용자 지정 —
        /// [[project_multiplayer_architecture]]) 세션이 끝나면 필요 없다.
        /// 이후 사용자가 "나가기"를 눌러 씬을 떠나든, "X"로 닫고 그 자리에서
        /// 솔로로 계속하든(BoardManager 인스턴스가 그대로 살아남는 경우) 둘 다
        /// 커버된다 — 어느 쪽이든 이 시점 이후로는 되돌리기를 다시 쓸 수
        /// 있어야 한다. GameBoard가 아닌 다른 씬(CardPrep/CardDraft/
        /// TerrainSetup)에서 끊기면 BoardManager 자체가 없으므로 조용히
        /// 아무 일도 안 한다(풀어줄 되돌리기 히스토리가 애초에 없음).</summary>
        private static void UnlockUndoHistoryIfOnGameBoard()
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            board?.UnlockAllUndoHistoryAfterMultiplayerEnded();
        }

        private void ShowNotice()
        {
            bool onGameBoard = SceneManager.GetActiveScene().name == GameConstants.GameBoardSceneName;
            _saveButtonGo.SetActive(onGameBoard);
            _panelGo.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>이름 입력창이 뜨는 동안만 알림을 잠깐 숨기고, 그 창이
        /// 확인/취소 어느 쪽으로든 닫히면(BoardManager.SaveDialogClosed) 알림
        /// 모달로 되돌아온다(사용자 요청, 2026-09-02) — 저장 한 번으로 끝이
        /// 아니라, 그 뒤에도 여전히 "저장"을 다시 누르거나 "나가기"를 고를 수
        /// 있어야 하므로.</summary>
        private void OnSaveButtonClicked()
        {
            _panelGo.SetActive(false);
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[DisconnectNoticeController] BoardManager를 못 찾음 — 저장 요청을 못 보냄");
                ShowNotice();
                return;
            }
            void OnSaveDialogClosed()
            {
                board.SaveDialogClosed -= OnSaveDialogClosed;
                ShowNotice();
            }
            board.SaveDialogClosed += OnSaveDialogClosed;
            board.RequestSave();
        }

        private void OnExitButtonClicked()
        {
            _panelGo.SetActive(false);
            _wasFullyConnected = false;
            ShutdownNetworkIfListening();
            SceneManager.LoadScene(GameConstants.EntrySceneName);
        }

        /// <summary>모달 오른쪽 위 x 버튼 — 상대가 이탈했다는 걸 그냥 인지만
        /// 하고, 씬 이동 없이 지금 화면에서 계속한다(사용자 요청, 2026-09-02:
        /// "혼자 하기를 하듯이 바로 돌아가게끔") — 실제로 죽은 연결을 그냥
        /// 두면 안 되므로 나가기와 마찬가지로 NetworkManager는 정리한다.</summary>
        private void OnCloseButtonClicked()
        {
            _panelGo.SetActive(false);
            _wasFullyConnected = false;
            ShutdownNetworkIfListening();
        }

        private static void ShutdownNetworkIfListening()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        // ── UI 구성 ──────────────────────────────────────────────────────

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

            // 배경 전체를 덮는 진짜 모달(ConfirmDialog와 같은 모양) — 바깥
            // 클릭으로 안 닫힌다(연결이 끊긴 사실 자체를 인지해야 하므로,
            // 실수로 넘어가면 안 된다는 점에서 MultiplayerConnectDialog와
            // 같은 이유).
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
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fitter = boxGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var messageLabel = CreateLabel(boxGo.transform, "연결이 종료되었습니다.", 16f);
            messageLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

            const float IconButtonSize = 56f;
            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(boxGo.transform, false);
            var buttonRowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            buttonRowLayout.spacing = 16f;
            buttonRowLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childForceExpandWidth = false;
            buttonRowLayout.childControlHeight = true;
            buttonRowLayout.childForceExpandHeight = false;
            buttonRow.AddComponent<LayoutElement>().preferredHeight = IconButtonSize;

            // 마커바의 저장/나가기 버튼과 같은 아이콘(SaveButton.png/
            // ExitButton.png)을 그대로 재사용해 시각 언어를 통일한다(사용자
            // 요청, 2026-09-02).
            _saveButtonGo = CreateIconButton(buttonRow.transform, "UI/SaveButton", IconButtonSize, OnSaveButtonClicked);
            CreateIconButton(buttonRow.transform, "UI/ExitButton", IconButtonSize, OnExitButtonClicked);

            // 오른쪽 위 x — VerticalLayoutGroup 흐름에서 빼고(ignoreLayout)
            // boxGo 오른쪽 위 구석에 직접 앉힌다. 맨 마지막에 추가해 다른
            // 자식들 위(맨 앞)에 그려지게 한다.
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
            // "×"(U+00D7)는 이 프로젝트가 쓰는 PretendardVariable SDF 폰트
            // 아틀라스에 없는 글리프라 안 보인다(—/▶/▼/·/→ 등과 같은 부류의
            // 기존에 이미 겪은 문제) — ASCII "X"로 대체한다.
            closeText.text = "X";
            closeText.alignment = TextAlignmentOptions.Center;
            closeText.fontSize = 16f;
            closeText.color = Color.white;
            closeText.raycastTarget = false;
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
            return label;
        }

        /// <summary>BoardManager.Markers.cs의 CreateExitButton/CreateSaveButton과
        /// 같은 모양(RawImage + Button, 텍스트 없이 아이콘만) — 마커바에서 이미
        /// 쓰던 아이콘을 그대로 재사용해 시각 언어를 통일한다(사용자 요청,
        /// 2026-09-02).</summary>
        private static GameObject CreateIconButton(Transform parent, string resourcePath, float size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{resourcePath}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>(resourcePath);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            return go;
        }
    }
}
