using System;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// Entry 화면 "같이 하기" 버튼이 여는 정식 멀티 연결 모달(2026-09-01 신설,
    /// 2026-09-02 EventSystem/NetworkManager/DisconnectNoticeController와
    /// 같은 방식으로 영구(DontDestroyOnLoad) 컴포넌트로 승격 — GameBoard
    /// 마커바의 "같이 하기" 버튼처럼 Entry가 아닌 다른 씬에서도 이 모달을
    /// 열 수 있어야 해서, `GameFlowBootstrap.EnsureMultiplayerConnectDialog()`가
    /// 앱 시작 시 한 번만 만들고 `Instance`로 어디서든 참조한다) —
    /// RelayConnectionTest(임시 테스트 패널)를 대체한다. 실제 Relay/
    /// NetworkManager 연결 로직 자체는 그 패널에서 그대로 옮겨왔다 — 바뀐
    /// 건 UI 구조뿐(사용자 지정 3단계 흐름): 처음엔 "호스트로 시작"/"참가
    /// 코드로 접속" 두 버튼만 보여주고(ChoiceView), "호스트로 시작"을
    /// 누르면 참가 코드 + 복사 버튼을 보여주며 상대를 기다리고(HostView),
    /// "참가 코드로 접속"을 누르면 코드 입력칸을 보여준다(JoinView). 총
    /// 2명이 연결되는 즉시 다음 화면으로 넘어가는 것도 그대로(사용자 지정
    /// — "호스트에게 연결되는 즉시") — 다만 2026-09-02부터 그 "다음 화면"이
    /// 호스트가 지금 어디 있느냐에 따라 갈린다(OnClientConnected 참고):
    /// 호스트가 Entry에서 새로 시작한 경우엔 예전처럼 CardPrep으로, 호스트가
    /// 이미 GameBoard에서 혼자 플레이 중이었다면 지금 보드 상태를 그대로
    /// 클라이언트에게 넘겨(BoardManager.BuildFullStateTree, LoadGame 화면과
    /// 같은 경로) 둘 다 그 GameBoard에서 합류한다.
    ///
    /// ConfirmDialog/InputDialog와 같은 "배경 전체를 덮는 진짜 모달" 구조를
    /// 따르되, 바깥 클릭으로 닫히지는 않는다(의도적 차이) — 이 안에서
    /// Relay 세션을 만들고 상대 접속을 기다리는 실제 네트워크 부작용이
    /// 있으므로, 실수로 바깥을 클릭해 조용히 닫히면 호스트가 계속 열린
    /// 채로 남을 수 있다. 대신 각 단계에 명시적 "취소"/"뒤로"/"닫기"
    /// 버튼을 두고, 그 버튼들이 눌릴 때 NetworkManager.Shutdown()까지
    /// 확실히 호출한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MultiplayerConnectDialog : MonoBehaviour
    {
        public static MultiplayerConnectDialog Instance { get; private set; }

        private const int MaxConnections = 1; // 호스트 제외 인원 수(2인용).
        private const float PanelWidth = 380f;

        private TextMeshProUGUI _titleLabel;

        private GameObject _choiceViewGo;
        private TMP_InputField _nicknameInputField;
        private GameObject _hostChoiceViewGo;

        private GameObject _hostViewGo;
        private TextMeshProUGUI _hostStatusLabel;
        private GameObject _hostCodeRowGo;
        private TextMeshProUGUI _hostCodeLabel;
        private TextMeshProUGUI _hostCopiedLabel;

        private GameObject _joinViewGo;
        private TMP_InputField _joinInputField;
        private TextMeshProUGUI _joinStatusLabel;

        private string _currentJoinCode;
        private float _copiedFeedbackHideTime = -1f;

        // OpenAndStartHosting()으로 들어온 세션인지 표시(2026-09-04 추가,
        // 사용자 요청 — "게임 화면에서 호스트 시작 눌렀다가 취소하면 바로
        // 꺼지게 하자"). 이 경로는 ChoiceView/HostChoiceView를 아예 거치지
        // 않고 곧장 호스팅을 시작하므로, "취소"를 눌렀을 때 되돌아갈 의미
        // 있는 이전 화면이 없다 — Open()으로 들어온 정상 흐름(ChoiceView →
        // "호스트로 시작" → HostChoiceView → "새 게임"/"이어하기")과 달리
        // OnHostCancelClicked가 그냥 모달을 닫아야 한다.
        private bool _openedViaInstantHost;

        // 진행 중인 비동기 시도(호스트/참가)가 그 사이 취소/뒤로가기로
        // 더 이상 유효하지 않게 됐는지 구분하는 용도 — 취소/뒤로/새 시도
        // 시작 시마다 올라간다. await 재개 시점마다 이 값이 시작할 때
        // 캡처해둔 값과 같은지 확인하고, 다르면 그 결과를 화면에 반영하지
        // 않고 조용히 버린다(사용자가 이미 다른 곳으로 넘어간 뒤이므로).
        private int _operationToken;

        private void Awake()
        {
            Instance = this;
            BuildUi();
            // OpenAndStartHosting()으로 곧장 호스팅을 시작하는 경로(ChoiceView를
            // 아예 안 거침)에서도 저장된 닉네임이 쓰이도록, 화면에 뭘 띄우기
            // 전부터 미리 채워둔다.
            _nicknameInputField.text = PlayerConfig.LoadNickname();

            var nm = NetworkManager.Singleton;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;

            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        private void Update()
        {
            if (_copiedFeedbackHideTime >= 0f && Time.time >= _copiedFeedbackHideTime)
            {
                _hostCopiedLabel.gameObject.SetActive(false);
                _copiedFeedbackHideTime = -1f;
            }
        }

        public void Open()
        {
            _openedViaInstantHost = false;
            ShowChoiceView();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        /// <summary>"호스트로 시작"→"이어하기"(OnHostContinueClicked)에서
        /// 저장 파일을 고른 뒤, 그 상태가 GameBoard에 다 채워지고 나면
        /// BoardManager.Start()가 부른다. GameBoard 마커바의 멀티 버튼
        /// (BoardManager.Markers.cs → CreateMultiplayerButton, 2026-09-04
        /// 추가)도 같은 이유로 이걸 직접 부른다 — 둘 다 ChoiceView/
        /// HostChoiceView를 거치지 않고, "호스트로 시작"→"새 게임"을 바로
        /// 누른 것과 똑같이 곧장 호스팅을 시작하고 코드를 띄운다.</summary>
        public void OpenAndStartHosting()
        {
            _openedViaInstantHost = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            BeginHosting();
        }

        public void Close()
        {
            gameObject.SetActive(false);
        }

        // ── 연결 상태 콜백 ────────────────────────────────────────────────

        /// <summary>호스트/클라이언트 양쪽 다에서 발생 — 호스트는 상대가
        /// 들어올 때, 클라이언트는 자기 자신이 접속에 성공할 때. 다음 화면
        /// 결정은 호스트만 내려서 방송한다(2026-09-02 변경 — 예전엔 양쪽이
        /// 각자 "2명이면 CardPrep"을 독립적으로 계산했는데, GameBoard
        /// 마커바에서도 이 모달을 열 수 있게 되면서 호스트가 이미 GameBoard
        /// 중일 수도 있어 더 이상 안전하지 않다). 호스트가 지금 GameBoard에
        /// 있으면 그 상태를 그대로 넘겨받는 쪽(BroadcastFullStateForMidGameJoin)
        /// 으로, 아니면 기존 CardPrep 흐름(RequestBroadcastProceedToCardPrep)
        /// 으로 — 클라이언트는 어느 쪽이든 그 방송을 받을 때까지 스스로
        /// 씬을 넘어가지 않고 그냥 기다린다.</summary>
        private void OnClientConnected(ulong clientId)
        {
            int count = NetworkManager.Singleton.ConnectedClients.Count;
            string msg = $"연결됨 (총 {count}명)";
            if (_hostViewGo.activeSelf)
            {
                SetHostWaitingStatus(msg);
            }
            else if (_joinViewGo.activeSelf)
            {
                SetJoinStatus(msg, isError: false);
            }

            if (count >= 2)
            {
                // 곧 다음 화면(또는 지금 화면 그대로 게임 계속)으로 넘어가니
                // 모달은 여기서 닫는다 — 실제 전환은(특히 게임 도중 합류의
                // 전체 상태 전송은 청크가 여러 프레임에 걸쳐 도착할 수 있어)
                // 조금 뒤에 일어날 수 있지만, 이 모달이 더 볼 일은 없다.
                Close();

                // 내 닉네임을 상대에게 방송 — 호스트/참가자 둘 다 이
                // 콜백에서 count>=2를 보는 시점에 부른다(정상 플로우와
                // 게임 도중 합류 양쪽 다 반드시 거침). BoardNetworkSync.cs의
                // BroadcastLocalNickname 주석 참고.
                BoardNetworkSync.Instance?.BroadcastLocalNickname();
            }

            if (count >= 2 && NetworkManager.Singleton.IsServer)
            {
                if (SceneManager.GetActiveScene().name == GameConstants.GameBoardSceneName)
                {
                    UnityEngine.Object.FindFirstObjectByType<BoardManager>()?.BroadcastFullStateForMidGameJoin();
                }
                else
                {
                    BoardNetworkSync.Instance?.RequestBroadcastProceedToCardPrep();
                }
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_hostViewGo.activeSelf)
            {
                SetHostWaitingStatus("연결 끊김");
            }
            else if (_joinViewGo.activeSelf)
            {
                SetJoinStatus("연결 끊김", isError: true);
            }
        }

        // ── ChoiceView ───────────────────────────────────────────────────

        private void ShowChoiceView()
        {
            _titleLabel.text = "같이 하기";
            _choiceViewGo.SetActive(true);
            _hostChoiceViewGo.SetActive(false);
            _hostViewGo.SetActive(false);
            _joinViewGo.SetActive(false);
        }

        private void OnCloseButtonClicked()
        {
            Close();
        }

        // ── HostChoiceView ───────────────────────────────────────────────

        /// <summary>ChoiceView의 "호스트로 시작"을 누르면 곧장 호스팅을
        /// 시작하지 않고 먼저 "새 게임"/"이어하기"를 물어본다(사용자 요청,
        /// 2026-09-04 재구성 — 예전엔 Entry 단계에서 먼저 물어봤는데, 참가
        /// 하는 쪽엔 "이어하기"가 의미 없어서 호스트/참가를 가르기도 전에
        /// 물어보면 안 맞았다).</summary>
        private void OnHostButtonClicked()
        {
            _titleLabel.text = "호스트로 시작";
            _choiceViewGo.SetActive(false);
            _hostChoiceViewGo.SetActive(true);
            _hostViewGo.SetActive(false);
            _joinViewGo.SetActive(false);
        }

        private void OnHostChoiceBackClicked()
        {
            ShowChoiceView();
        }

        private void OnHostNewGameClicked()
        {
            BeginHosting();
        }

        /// <summary>닉네임 입력칸의 현재 값을 저장하고 PlayerIdentity에
        /// 반영한다 — 호스트는 BeginHosting() 진입 시(OpenAndStartHosting
        /// 경로 포함), 참가자는 OnJoinConfirmClicked() 진입 시 부른다.
        /// 상대 쪽 닉네임 칸은 지난 세션의 값이 남아있을 수 있으니 함께
        /// 비워둔다 — 실제 값은 GameBoard 진입 시 BoardManager.
        /// BroadcastLocalNicknameIfNetworked로 다시 채워진다.</summary>
        private void CaptureAndSaveLocalNickname(string team)
        {
            string nickname = _nicknameInputField.text.Trim();
            PlayerConfig.SaveNickname(nickname);
            PlayerIdentity.Nicknames[team] = nickname;
            PlayerIdentity.Nicknames[NetworkTeam.OpponentOf(team)] = "";
        }

        /// <summary>Entry의 "이어하기" 화면(LoadGame 씬)을 그대로 재사용한다
        /// — 이 다이얼로그는 씬과 무관하게 어디서든 뜰 수 있는 영구
        /// 컴포넌트라 씬을 직접 옮기기 전에 반드시 먼저 닫아야, 전환된
        /// LoadGame 화면 위를 계속 덮고 있지 않는다. GameLoadRequest.
        /// AutoOpenMultiplayerAfterLoad를 세워두면, 저장 파일을 고르고
        /// GameBoard에 그 상태가 다 채워진 직후 BoardManager.Start()가
        /// OpenAndStartHosting()을 불러 "새 게임"을 누른 것과 똑같이 곧장
        /// 호스팅을 시작한다.</summary>
        private void OnHostContinueClicked()
        {
            GameLoadRequest.AutoOpenMultiplayerAfterLoad = true;
            Close();
            SceneManager.LoadScene(GameConstants.LoadGameSceneName);
        }

        // ── HostView ─────────────────────────────────────────────────────

        /// <summary>실제 호스팅 시작 — HostChoiceView의 "새 게임"과
        /// OpenAndStartHosting(저장 불러오기 후 자동 진입) 둘 다 이걸
        /// 부른다.</summary>
        private void BeginHosting()
        {
            CaptureAndSaveLocalNickname(NetworkTeam.Host);

            _titleLabel.text = "호스트로 시작";
            _choiceViewGo.SetActive(false);
            _hostChoiceViewGo.SetActive(false);
            _hostViewGo.SetActive(true);
            _joinViewGo.SetActive(false);

            _hostCodeRowGo.SetActive(false);
            _hostCopiedLabel.gameObject.SetActive(false);
            _copiedFeedbackHideTime = -1f;
            SetHostStatus("호스트 준비 중...");

            int token = ++_operationToken;
            _ = StartHostAsync(token);
        }

        /// <summary>2026-09-04 수정(사용자 요청) — OpenAndStartHosting()으로
        /// 들어온 세션(게임 화면 멀티 버튼, "이어하기" 자동 호스팅)이면
        /// ChoiceView로 돌아가지 않고 모달 자체를 바로 닫는다. 그 경로는
        /// 애초에 ChoiceView/HostChoiceView를 거친 적이 없어서 "돌아갈
        /// 이전 화면"이라는 개념 자체가 안 맞는다. Open()으로 들어온 정상
        /// 흐름(ChoiceView → "호스트로 시작" → HostChoiceView → "새 게임"/
        /// "이어하기" → 여기)은 기존대로 ChoiceView로 돌아간다.</summary>
        private void OnHostCancelClicked()
        {
            ++_operationToken; // 진행 중이던 시도가 있었다면 여기서 무효화.
            ShutdownNetworkIfListening();
            if (_openedViaInstantHost)
            {
                Close();
                return;
            }
            ShowChoiceView();
        }

        private void OnCopyButtonClicked()
        {
            GUIUtility.systemCopyBuffer = _currentJoinCode ?? "";
            _hostCopiedLabel.gameObject.SetActive(true);
            _copiedFeedbackHideTime = Time.time + 1.2f;
        }

        private async Task StartHostAsync(int token)
        {
            try
            {
                await EnsureSignedInAsync();
                if (token != _operationToken) return;

                var nm = NetworkManager.Singleton;
                if (nm.IsListening) nm.Shutdown();

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
                if (token != _operationToken) return;
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                if (token != _operationToken) return;

                var transport = nm.GetComponent<UnityTransport>();
                transport.SetHostRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
                        allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData);

                if (nm.StartHost())
                {
                    if (token != _operationToken)
                    {
                        // 그 사이 취소됨 — 방금 세운 호스트를 바로 정리한다.
                        nm.Shutdown();
                        return;
                    }
                    SpawnBoardNetworkSync();
                    ShowHostCode(joinCode);
                }
                else
                {
                    SetHostStatus("호스트 시작 실패");
                }
            }
            catch (Exception e)
            {
                if (token == _operationToken)
                {
                    SetHostStatus($"호스트 오류: {e.Message}");
                }
            }
        }

        private void ShowHostCode(string joinCode)
        {
            _currentJoinCode = joinCode;
            _hostCodeLabel.text = joinCode;
            _hostCodeRowGo.SetActive(true);
            SetHostWaitingStatus("상대방을 기다리는 중...");
        }

        private void SetHostStatus(string text)
        {
            _hostStatusLabel.text = text;
            _hostStatusLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        }

        private void SetHostWaitingStatus(string text)
        {
            _hostStatusLabel.text = text;
            _hostStatusLabel.color = new Color(0.7f, 0.85f, 0.7f, 1f);
        }

        /// <summary>보드 상태 RPC 창구(BoardNetworkSync)는 호스트가 세션당 한
        /// 번만 스폰하면 된다 — 이후 접속하는 클라이언트도 NGO의 초기 동기화로
        /// 자동으로 받는다.</summary>
        private static void SpawnBoardNetworkSync()
        {
            var prefab = Resources.Load<GameObject>("Multiplayer/BoardNetworkSync");
            if (prefab == null)
            {
                Debug.LogError("[MultiplayerConnectDialog] BoardNetworkSync 프리팹을 못 찾음 (Resources/Multiplayer/BoardNetworkSync)");
                return;
            }
            var instance = Instantiate(prefab);
            instance.GetComponent<NetworkObject>().Spawn();
        }

        // ── JoinView ─────────────────────────────────────────────────────

        private void OnJoinButtonClicked()
        {
            _titleLabel.text = "참가 코드로 접속";
            _choiceViewGo.SetActive(false);
            _hostViewGo.SetActive(false);
            _joinViewGo.SetActive(true);

            _joinInputField.text = "";
            SetJoinStatus("", isError: false);
            _joinInputField.Select();
            _joinInputField.ActivateInputField();
        }

        /// <summary>클립보드가 정확히 6자(Relay 참가 코드 길이)면 그대로
        /// 붙여넣는다 — 호스트 쪽 "복사" 버튼(OnCopyButtonClicked)과 짝이
        /// 맞는 편의 기능(사용자 요청, 2026-09-02). 이미 입력된 내용이
        /// 있으면 덮어쓰지 않는다 — 포커스가 갈 때마다(뷰가 열릴 때 자동
        /// 포커스되는 경우 포함) 매번 확인하므로.</summary>
        private void TryAutoPasteJoinCodeFromClipboard()
        {
            if (!string.IsNullOrEmpty(_joinInputField.text))
            {
                return;
            }
            string clipboard = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(clipboard) && clipboard.Length == 6)
            {
                _joinInputField.text = clipboard;
            }
        }

        private void OnJoinBackClicked()
        {
            ++_operationToken; // 진행 중이던 시도가 있었다면 여기서 무효화.
            ShutdownNetworkIfListening();
            ShowChoiceView();
        }

        private void OnJoinConfirmClicked()
        {
            string code = _joinInputField.text.Trim();
            if (string.IsNullOrEmpty(code))
            {
                SetJoinStatus("참가 코드를 입력하세요", isError: true);
                return;
            }
            CaptureAndSaveLocalNickname(NetworkTeam.Client);

            SetJoinStatus("접속 중...", isError: false);
            int token = ++_operationToken;
            _ = JoinAsync(code, token);
        }

        private async Task JoinAsync(string code, int token)
        {
            try
            {
                await EnsureSignedInAsync();
                if (token != _operationToken) return;

                var nm = NetworkManager.Singleton;
                if (nm.IsListening) nm.Shutdown();

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);
                if (token != _operationToken) return;

                var transport = nm.GetComponent<UnityTransport>();
                transport.SetClientRelayData(joinAllocation.RelayServer.IpV4, (ushort)joinAllocation.RelayServer.Port,
                        joinAllocation.AllocationIdBytes, joinAllocation.Key, joinAllocation.ConnectionData,
                        joinAllocation.HostConnectionData);

                if (nm.StartClient())
                {
                    SetJoinStatus("접속 시도 중...", isError: false);
                }
                else
                {
                    SetJoinStatus("접속 시작 실패 - 다시 시도해주세요", isError: true);
                }
            }
            catch (Exception e)
            {
                if (token == _operationToken)
                {
                    // 잘못된 코드 등 접속 실패 — 같은 입력칸에 에러를 보여주고
                    // 재입력할 수 있게 둔다(화면 전환 없음, 사용자 지정).
                    SetJoinStatus($"접속 실패: {e.Message}", isError: true);
                }
            }
        }

        private void SetJoinStatus(string text, bool isError)
        {
            _joinStatusLabel.text = text;
            _joinStatusLabel.color = isError ? new Color(1f, 0.55f, 0.55f, 1f) : new Color(0.7f, 0.85f, 0.7f, 1f);
        }

        // ── 공용 ─────────────────────────────────────────────────────────

        private static void ShutdownNetworkIfListening()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                nm.Shutdown();
            }
        }

        private static async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
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

            // 배경 전체를 덮는 진짜 모달(ConfirmDialog/InputDialog와 같은
            // 모양)이지만, 클릭해도 안 닫힌다(의도적 — 클래스 주석 참고).
            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.5f);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(PanelWidth, 0f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);

            var panelLayout = panelGo.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(20, 20, 16, 16);
            panelLayout.spacing = 14f;
            panelLayout.childControlWidth = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandHeight = false;
            var panelFitter = panelGo.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _titleLabel = CreateLabel(panelGo.transform, "같이 하기", 18f, FontStyles.Bold);
            _titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

            BuildChoiceView(panelGo.transform);
            BuildHostChoiceView(panelGo.transform);
            BuildHostView(panelGo.transform);
            BuildJoinView(panelGo.transform);
        }

        private void BuildChoiceView(Transform parent)
        {
            _choiceViewGo = new GameObject("ChoiceView", typeof(RectTransform));
            _choiceViewGo.transform.SetParent(parent, false);
            var layout = _choiceViewGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            _nicknameInputField = CreateNicknameInputField(_choiceViewGo.transform);

            CreateButton(_choiceViewGo.transform, "호스트로 시작", 40f, OnHostButtonClicked);
            CreateButton(_choiceViewGo.transform, "참가 코드로 접속", 40f, OnJoinButtonClicked);
            CreateButton(_choiceViewGo.transform, "닫기", 32f, OnCloseButtonClicked);
        }

        /// <summary>닉네임 입력칸 — CreateInputField(참가 코드용)와 같은 모양이되,
        /// 비어 있을 때 안내 문구를 보여주는 placeholder가 추가로 있다. 값은
        /// Awake에서 PlayerConfig.LoadNickname()으로 미리 채워지고, "호스트로
        /// 시작"/"접속" 시점에 CaptureAndSaveLocalNickname이 다시 저장한다.</summary>
        private static TMP_InputField CreateNicknameInputField(Transform parent)
        {
            var go = new GameObject("NicknameField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 36f;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            var inputField = go.AddComponent<TMP_InputField>();
            inputField.characterLimit = 12;

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(go.transform, false);
            var textAreaRect = (RectTransform)textAreaGo.transform;
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(8f, 4f);
            textAreaRect.offsetMax = new Vector2(-8f, -4f);
            textAreaGo.AddComponent<RectMask2D>();

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(textAreaGo.transform, false);
            var placeholderRect = (RectTransform)placeholderGo.transform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholder.text = "닉네임 (선택 사항)";
            placeholder.fontSize = 16f;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(1f, 1f, 1f, 0.4f);

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

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            return inputField;
        }

        /// <summary>"호스트로 시작" 다음, 실제 호스팅 전에 묻는 "새 게임"/
        /// "이어하기"(사용자 요청, 2026-09-04).</summary>
        private void BuildHostChoiceView(Transform parent)
        {
            _hostChoiceViewGo = new GameObject("HostChoiceView", typeof(RectTransform));
            _hostChoiceViewGo.transform.SetParent(parent, false);
            var layout = _hostChoiceViewGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            CreateButton(_hostChoiceViewGo.transform, "새 게임", 40f, OnHostNewGameClicked);
            CreateButton(_hostChoiceViewGo.transform, "이어하기", 40f, OnHostContinueClicked);
            CreateButton(_hostChoiceViewGo.transform, "뒤로", 32f, OnHostChoiceBackClicked);
        }

        private void BuildHostView(Transform parent)
        {
            _hostViewGo = new GameObject("HostView", typeof(RectTransform));
            _hostViewGo.transform.SetParent(parent, false);
            var layout = _hostViewGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            _hostStatusLabel = CreateLabel(_hostViewGo.transform, "", 14f, FontStyles.Normal);
            _hostStatusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            // 코드 + 복사 버튼 줄 — 코드가 실제로 나오기 전까진 숨김.
            _hostCodeRowGo = new GameObject("CodeRow", typeof(RectTransform));
            _hostCodeRowGo.transform.SetParent(_hostViewGo.transform, false);
            var codeRowLayout = _hostCodeRowGo.AddComponent<HorizontalLayoutGroup>();
            codeRowLayout.spacing = 8f;
            codeRowLayout.childAlignment = TextAnchor.MiddleCenter;
            codeRowLayout.childControlWidth = true;
            codeRowLayout.childForceExpandWidth = false;
            codeRowLayout.childControlHeight = true;
            codeRowLayout.childForceExpandHeight = false;
            _hostCodeRowGo.AddComponent<LayoutElement>().preferredHeight = 44f;

            var codeBgGo = new GameObject("CodeBg", typeof(RectTransform));
            codeBgGo.transform.SetParent(_hostCodeRowGo.transform, false);
            var codeBgLe = codeBgGo.AddComponent<LayoutElement>();
            codeBgLe.flexibleWidth = 1f;
            codeBgLe.preferredHeight = 44f;
            var codeBg = codeBgGo.AddComponent<Image>();
            codeBg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            _hostCodeLabel = CreateLabel(codeBgGo.transform, "", 22f, FontStyles.Bold);
            var codeLabelRect = (RectTransform)_hostCodeLabel.transform;
            codeLabelRect.anchorMin = Vector2.zero;
            codeLabelRect.anchorMax = Vector2.one;
            codeLabelRect.offsetMin = Vector2.zero;
            codeLabelRect.offsetMax = Vector2.zero;

            var copyBtnGo = new GameObject("CopyButton", typeof(RectTransform));
            copyBtnGo.transform.SetParent(_hostCodeRowGo.transform, false);
            var copyBtnLe = copyBtnGo.AddComponent<LayoutElement>();
            copyBtnLe.preferredWidth = 36f;
            copyBtnLe.preferredHeight = 36f;
            var copyImg = copyBtnGo.AddComponent<RawImage>();
            copyImg.texture = Resources.Load<Texture2D>("UI/CopyButton");
            var copyBtn = copyBtnGo.AddComponent<Button>();
            copyBtn.targetGraphic = copyImg;
            copyBtn.onClick.AddListener(OnCopyButtonClicked);

            _hostCopiedLabel = CreateLabel(_hostViewGo.transform, "복사됨", 12f, FontStyles.Normal);
            _hostCopiedLabel.color = new Color(0.6f, 0.85f, 0.6f, 1f);
            _hostCopiedLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;
            _hostCopiedLabel.gameObject.SetActive(false);

            CreateButton(_hostViewGo.transform, "취소", 32f, OnHostCancelClicked);
        }

        private void BuildJoinView(Transform parent)
        {
            _joinViewGo = new GameObject("JoinView", typeof(RectTransform));
            _joinViewGo.transform.SetParent(parent, false);
            var layout = _joinViewGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            _joinInputField = CreateInputField(_joinViewGo.transform);
            _joinInputField.onSubmit.AddListener(_ => OnJoinConfirmClicked());
            // 입력칸을 클릭(포커스)하면 클립보드에 참가 코드로 보이는 6자
            // 문자열이 있는지 확인해 자동으로 채워준다(사용자 요청,
            // 2026-09-02) — 호스트 쪽 "복사" 버튼과 짝이 맞는 편의 기능.
            // 이미 뭔가 입력돼 있으면 덮어쓰지 않는다.
            _joinInputField.onSelect.AddListener(_ => TryAutoPasteJoinCodeFromClipboard());

            _joinStatusLabel = CreateLabel(_joinViewGo.transform, "", 13f, FontStyles.Normal);
            _joinStatusLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
            _joinStatusLabel.textWrappingMode = TextWrappingModes.Normal;

            var buttonRow = new GameObject("Buttons", typeof(RectTransform));
            buttonRow.transform.SetParent(_joinViewGo.transform, false);
            var buttonRowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            buttonRowLayout.spacing = 8f;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childForceExpandWidth = true;
            buttonRowLayout.childControlHeight = true;
            buttonRow.AddComponent<LayoutElement>().preferredHeight = 36f;

            CreateButton(buttonRow.transform, "접속", 36f, OnJoinConfirmClicked);
            CreateButton(buttonRow.transform, "뒤로", 36f, OnJoinBackClicked);
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, FontStyles style)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private static void CreateButton(Transform parent, string label, float height, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = height;

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
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 16f;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static TMP_InputField CreateInputField(Transform parent)
        {
            var go = new GameObject("InputField", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 40f;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);
            var inputField = go.AddComponent<TMP_InputField>();

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(go.transform, false);
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
            text.fontSize = 20f;
            text.color = Color.white;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            return inputField;
        }
    }
}
