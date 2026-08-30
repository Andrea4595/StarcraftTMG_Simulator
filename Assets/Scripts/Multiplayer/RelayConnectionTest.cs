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
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>Relay 연결 배관만 검증하는 임시 테스트 패널(Entry 화면 왼쪽
    /// 아래 구석) — 게임 상태 동기화는 아직 없고, 호스트가 참가 코드를 받고
    /// 클라이언트가 그 코드로 같은 Relay 세션에 접속되는지만 확인한다. 실제
    /// 멀티 기능 설계가 잡히면 이 패널은 걷어내고 진짜 화면으로 교체될
    /// 예정(그래서 EntryController처럼 이벤트로 씬 전환을 올리지 않고,
    /// 여기서 직접 NetworkManager/Relay를 건드린다). Relay/Authentication
    /// SDK가 코루틴이 아니라 Task 기반이라, 이 프로젝트에서 처음으로
    /// async/await를 쓴다(기존 "코루틴 없음" 관례와 별개 — 코루틴을 쓰는 건
    /// 아니라서 어긋나지 않는다).</summary>
    [RequireComponent(typeof(RectTransform))]
    public class RelayConnectionTest : MonoBehaviour
    {
        private const int MaxConnections = 1; // 호스트 제외 인원 수(2인용 테스트)

        private TextMeshProUGUI _statusLabel;
        private InputDialog _joinCodeDialog;

        private void Awake()
        {
            BuildUi();

            var dialogGo = new GameObject("JoinCodeDialog", typeof(RectTransform));
            dialogGo.transform.SetParent(transform.parent, false);
            _joinCodeDialog = dialogGo.AddComponent<InputDialog>();
            _joinCodeDialog.Confirmed += OnJoinCodeConfirmed;

            var nm = NetworkManager.Singleton;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
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
            SetStatus($"연결됨 (clientId={clientId}, 총 {NetworkManager.Singleton.ConnectedClients.Count}명)");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            SetStatus($"연결 끊김 (clientId={clientId})");
        }

        private void OnHostButtonClicked()
        {
            _ = StartHostAsync();
        }

        private void OnJoinButtonClicked()
        {
            _joinCodeDialog.Open("참가 코드 입력", "");
        }

        private void OnJoinCodeConfirmed(string code)
        {
            code = code.Trim();
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("참가 코드를 입력하세요");
                return;
            }
            _ = JoinAsync(code);
        }

        private async Task StartHostAsync()
        {
            SetStatus("호스트 준비 중...");
            try
            {
                await EnsureSignedInAsync();

                var nm = NetworkManager.Singleton;
                if (nm.IsListening) nm.Shutdown();

                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxConnections);
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                var transport = nm.GetComponent<UnityTransport>();
                transport.SetHostRelayData(allocation.RelayServer.IpV4, (ushort)allocation.RelayServer.Port,
                        allocation.AllocationIdBytes, allocation.Key, allocation.ConnectionData);

                if (nm.StartHost())
                {
                    SpawnBoardNetworkSync();
                    SetStatus($"호스트 시작됨 - 참가 코드: {joinCode}");
                }
                else
                {
                    SetStatus("호스트 시작 실패");
                }
            }
            catch (Exception e)
            {
                SetStatus($"호스트 오류: {e.Message}");
            }
        }

        /// <summary>보드 상태 RPC 창구(BoardNetworkSync)는 호스트가 세션당 한
        /// 번만 스폰하면 된다 — 이후 접속하는 클라이언트도 NGO의 초기 동기화로
        /// 자동으로 받는다.</summary>
        private static void SpawnBoardNetworkSync()
        {
            var prefab = Resources.Load<GameObject>("Multiplayer/BoardNetworkSync");
            if (prefab == null)
            {
                Debug.LogError("[RelayConnectionTest] BoardNetworkSync 프리팹을 못 찾음 (Resources/Multiplayer/BoardNetworkSync)");
                return;
            }
            var instance = Instantiate(prefab);
            instance.GetComponent<NetworkObject>().Spawn();
        }

        private async Task JoinAsync(string code)
        {
            SetStatus("접속 중...");
            try
            {
                await EnsureSignedInAsync();

                var nm = NetworkManager.Singleton;
                if (nm.IsListening) nm.Shutdown();

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);

                var transport = nm.GetComponent<UnityTransport>();
                transport.SetClientRelayData(joinAllocation.RelayServer.IpV4, (ushort)joinAllocation.RelayServer.Port,
                        joinAllocation.AllocationIdBytes, joinAllocation.Key, joinAllocation.ConnectionData,
                        joinAllocation.HostConnectionData);

                SetStatus(nm.StartClient() ? "접속 시도 중..." : "접속 시작 실패");
            }
            catch (Exception e)
            {
                SetStatus($"접속 오류: {e.Message}");
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

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text;
            Debug.Log($"[RelayConnectionTest] {text}");
        }

        private void BuildUi()
        {
            var root = (RectTransform)transform;
            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.anchoredPosition = new Vector2(24f, 24f);
            root.sizeDelta = new Vector2(260f, 0f);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.14f, 0.14f, 0.14f, 0.92f);

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = CreateLabel(root, "멀티 연결 테스트", 14f, FontStyles.Bold, new Color(0.8f, 0.8f, 0.8f, 1f));
            header.GetComponent<LayoutElement>().preferredHeight = 20f;

            _statusLabel = CreateLabel(root, "연결 안 됨", 12f, FontStyles.Normal, new Color(0.7f, 0.85f, 0.7f, 1f));
            _statusLabel.textWrappingMode = TextWrappingModes.Normal;
            _statusLabel.GetComponent<LayoutElement>().preferredHeight = 48f;

            CreateButton(root, "호스트로 시작", OnHostButtonClicked);
            CreateButton(root, "참가 코드로 접속", OnJoinButtonClicked);
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, FontStyles style, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>();
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        private static void CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 32f;

            var img = go.AddComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.22f, 1f);
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
            text.fontSize = 13f;
            text.color = Color.white;
            text.raycastTarget = false;
        }
    }
}
