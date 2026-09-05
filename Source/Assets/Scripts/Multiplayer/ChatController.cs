using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 화면 좌측 하단 채팅(2026-09-04 신설, 사용자 요청) — 엔터를 누르면
    /// 입력창이 뜨고 자동 포커싱되며, 입력 후 엔터로 전송하면 입력창은
    /// 닫힌다. 사용자 지정대로 "채팅은 토스트 메시지를 그대로 활용" —
    /// 원래 BoardManager.ToastStack.cs에 있던 토스트 스택(되돌리기/전술카드
    /// 사용 알림용)을 그대로 이 클래스로 옮겨와서, 채팅 메시지도 같은
    /// 스택에 같은 페이드/쌓기 규칙으로 함께 뜬다. 채팅 메시지는 팀
    /// 이름표를 붙여("[A] 안녕" 형태 — [유닛]/[택티컬] 대괄호 표기와 같은
    /// 문법) 그 팀 색으로 칠해 누가 말했는지 구분한다(사용자 지정).
    ///
    /// BoardManager는 GameBoard 씬에서만(코드로) 지어지는 반면, 채팅은
    /// 멀티가 진행되는 모든 화면(CardPrep/CardDraft/TerrainSetup/GameBoard)
    /// 에서 다 떠야 한다는 요구라 — MultiplayerConnectDialog/
    /// DisconnectNoticeController와 완전히 같은 이유로 GameFlowBootstrap이
    /// 앱 시작 시 한 번만 만들고 DontDestroyOnLoad + Instance로 승격했다.
    /// BoardManager.UndoRedo.cs의 ShowUndoToast는 이제 이 컴포넌트의
    /// PushToast를 직접 부른다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ChatController : MonoBehaviour
    {
        public static ChatController Instance { get; private set; }

        // ── 토스트 스택(원래 BoardManager.ToastStack.cs) ───────────────────
        private const float ToastStackVisibleDurationSeconds = 5f;
        private const float ToastStackFadeDurationSeconds = 0.3f;
        // 실제 렌더링된 높이를 매 프레임 읽는 대신 고정값을 쓴다(레이아웃
        // 그룹이 생성 첫 프레임엔 크기를 아직 확정 안 해서). 사용자가
        // 에디터에서 실측한 값 — ToastStackGap이 0인 상태에서 이 값이 실제
        // 렌더 높이와 다르면 그만큼 빈틈/겹침이 생긴다. 폰트 크기나
        // 패딩(HorizontalLayoutGroup.padding)을 바꾸면 다시 실측해서 맞출 것.
        private const float ToastStackRowHeight = 31.52f;
        private const float ToastStackGap = 0f; // 사용자 요청 — 토스트끼리 간격 없이 딱 붙게.

        // ── 채팅 입력창 ─────────────────────────────────────────────────
        private const float ChatInputWidth = 260f;
        private const float ChatInputHeight = 32f;
        private const float ChatToastGap = 8f; // 입력창 위쪽 끝과 그 위 첫 토스트 사이 여백.
        private const float CornerMargin = 16f; // GameBoard가 아닌 씬(패널/마커바가 없음)에서의 화면 가장자리 여백.
        private const int ChatMessageMaxLength = 120;

        private sealed class ToastStackEntry
        {
            public GameObject Go;
            public CanvasGroup Group;
            public float HideTime;
        }
        private readonly List<ToastStackEntry> _toastStackEntries = new List<ToastStackEntry>();

        private GameObject _chatInputGo;
        private CanvasGroup _chatInputGroup;
        private TMP_InputField _chatInputField;
        private bool _chatOpen;
        // Enter로 감지한 그 프레임에 바로 ActivateInputField()를 부르면, 그
        // Return 키다운 이벤트가 아직 이번 프레임 이벤트 큐에 남아있는 채로
        // 막 포커싱된 입력창에 다시 전달돼 열자마자 곧바로 제출되는(같은
        // 키 입력이 두 번 처리되는) 위험이 있다 — 이 프로젝트에 아직 이런
        // "전역 단축키로 감지한 뒤 그 즉시 입력창에 포커스" 패턴 선례가
        // 없어서 실측 확인이 안 되므로, 방어적으로 한 프레임 미뤄서 연다.
        private bool _openChatNextFrame;
        // 닫힘 처리(OnChatSubmit→CloseChatInput)가 TMP_InputField 자신의
        // onSubmit 콜백 안에서, 이 컴포넌트의 Update()가 그 프레임의
        // Input.GetKeyDown(Return)을 확인하기 *전에* 먼저 일어날 수 있다 —
        // 그러면 _chatOpen이 이미 false로 바뀐 상태에서 아직 true인 같은
        // 프레임의 GetKeyDown을 내 Update()가 그대로 읽어, 방금 막 닫은 걸
        // 그 즉시 다시 여는 것으로 오해했다(사용자 진단 — "채팅창이 한 번
        // 닫히면 0.08초 동안 조작을 무시해"). 몇 프레임을 미루는 것만으론
        // 못 막는다 — 문제는 프레임 수가 아니라 "같은 물리적 키 입력이
        // 두 시스템(TMP의 콜백, 내 폴링)에 다 읽힌다"는 것이라, 닫힌
        // 직후 짧은 시간 동안 아예 다시 열기 판정 자체를 쉬게 한다.
        private const float ReopenCooldownSeconds = 0.08f;
        private float _suppressOpenUntil = -1f;

        private void Awake()
        {
            Instance = this;

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            BuildChatInput();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            UpdateToastStack();

            if (_openChatNextFrame)
            {
                _openChatNextFrame = false;
                OpenChatInput();
                return;
            }

            if (_chatOpen)
            {
                return; // 이미 열려 있음 — onSubmit이 알아서 닫는다.
            }
            if (Time.time < _suppressOpenUntil)
            {
                return; // 방금 보낸/닫은 그 엔터가 이 프레임에 재해석되는 걸 막는다.
            }
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                return;
            }
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return; // 혼자 플레이 중엔 채팅할 상대가 없다.
            }
            if (IsAnotherInputFieldFocused())
            {
                return; // 다른 입력창(이름 짓기 등)이 이 엔터를 먼저 가져가야 한다.
            }
            _openChatNextFrame = true;
        }

        /// <summary>다른 입력창이 지금 이 엔터를 가져가야 하는지 판단한다.
        /// 채팅 자신의 입력창은 반드시 제외해야 한다 — 닫혀 있어도(CanvasGroup
        /// 으로 숨김/비활성 처리만 하고 GameObject 자체는 계속 살아있음,
        /// 아래 BuildChatInput 참고) EventSystem.currentSelectedGameObject가
        /// 저절로 안 풀리고 그 오브젝트를 계속 가리키는 채로 남을 수 있다.
        /// 그래서 이 예외 없이는 메시지를 한 번 보내고 나면 "다른 입력창이
        /// 열려있다"고 잘못 판단해 엔터로 다시 못 여는 버그가 있었다(사용자
        /// 보고).</summary>
        private bool IsAnotherInputFieldFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null || selected == _chatInputGo)
            {
                return false;
            }
            return selected.GetComponent<TMP_InputField>() != null;
        }

        private void OpenChatInput()
        {
            _chatOpen = true;
            _chatInputField.text = "";
            _chatInputGroup.alpha = 1f;
            _chatInputGroup.interactable = true;
            _chatInputGroup.blocksRaycasts = true;
            _chatInputField.Select();
            _chatInputField.ActivateInputField();
        }

        /// <summary>사용자 지정 — 메시지를 보내고 나면 입력창은 닫힌다.
        /// GameObject 자체를 SetActive(false)로 끄면, 그게 TMP_InputField
        /// 자신의 onSubmit 콜백 *안*에서 일어날 때 TMP가 아직 진행 중이던
        /// 자기 자신의 뒤처리(선택 해제 등)와 겹쳐서(재진입) 실제로는 안
        /// 닫히는 문제가 있었다(사용자 보고, 여러 번 재현). 그래서 GameObject
        /// 는 계속 활성 상태로 두고 CanvasGroup(alpha/interactable/
        /// blocksRaycasts)만으로 숨김·비활성화하며, 포커스 해제는 TMP가
        /// 직접 제공하는 DeactivateInputField()로 — 둘 다 콜백 안에서 불러도
        /// 안전한, TMP 자신의 상태 전이 방식과 충돌하지 않는 정식 API다.</summary>
        private void CloseChatInput()
        {
            _chatOpen = false;
            _suppressOpenUntil = Time.time + ReopenCooldownSeconds;
            _chatInputField.DeactivateInputField();
            _chatInputGroup.alpha = 0f;
            _chatInputGroup.interactable = false;
            _chatInputGroup.blocksRaycasts = false;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _chatInputGo)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void OnChatSubmit(string text)
        {
            CloseChatInput();
            text = text.Trim();
            if (text.Length == 0)
            {
                return;
            }
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening
                    || BoardNetworkSync.Instance == null)
            {
                return;
            }
            BoardNetworkSync.Instance.RequestSendChatServerRpc(NetworkTeam.LocalTeam(), text);
        }

        /// <summary>BoardNetworkSync.BroadcastChatRpc가 방송을 받았을 때(보낸
        /// 쪽 자신도 포함, 마커/유닛 등과 같은 "방송 루프백으로 로컬 반영"
        /// 패턴) 호출한다.</summary>
        internal void ReceiveChatMessage(string team, string message)
        {
            PushToast($"[{team}] {message}", team);
        }

        private void BuildChatInput()
        {
            _chatInputGo = new GameObject("ChatInput", typeof(RectTransform));
            _chatInputGo.transform.SetParent(transform, false);
            var rect = (RectTransform)_chatInputGo.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(ChatInputWidth, ChatInputHeight);

            var bg = _chatInputGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            _chatInputGroup = _chatInputGo.AddComponent<CanvasGroup>();

            _chatInputField = _chatInputGo.AddComponent<TMP_InputField>();
            _chatInputField.characterLimit = ChatMessageMaxLength;

            var textAreaGo = new GameObject("TextArea", typeof(RectTransform));
            textAreaGo.transform.SetParent(_chatInputGo.transform, false);
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
            text.fontSize = 13f;
            text.color = Color.white;

            _chatInputField.textViewport = textAreaRect;
            _chatInputField.textComponent = text;

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform));
            placeholderGo.transform.SetParent(textAreaGo.transform, false);
            var placeholderRect = (RectTransform)placeholderGo.transform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;
            var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
            placeholder.text = "메시지 입력...";
            placeholder.fontSize = 13f;
            placeholder.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.raycastTarget = false;
            _chatInputField.placeholder = placeholder;

            _chatInputField.onSubmit.AddListener(OnChatSubmit);

            // 시작은 닫힌 상태 — GameObject는 계속 활성 상태로 두고(위
            // CloseChatInput 주석 참고) CanvasGroup으로만 숨긴다.
            _chatInputGroup.alpha = 0f;
            _chatInputGroup.interactable = false;
            _chatInputGroup.blocksRaycasts = false;
        }

        /// <summary>GameBoard 씬은 팀 A 로스터 패널(왼쪽 가장자리)과 마커바
        /// (아래쪽 가장자리)가 화면 진짜 모서리를 덮고 있어서, 그 안쪽(지도가
        /// 실제로 보이는 영역의 모서리)에 자리를 잡아야 한다(BoardManager.
        /// ToastStack.cs가 원래 쓰던 계산 그대로 재사용). 다른 씬(CardPrep/
        /// CardDraft/TerrainSetup)엔 그 패널/바가 아예 없으므로(조사 확인 —
        /// 그 씬들의 컨트롤러는 GameConstants.PendingPanelWidth/
        /// MarkerBarHeight를 전혀 참조하지 않는다) 화면 진짜 모서리에서 작은
        /// 여백만 두면 된다.</summary>
        private static void GetCorner(out float x, out float y)
        {
            bool onGameBoard = SceneManager.GetActiveScene().name == GameConstants.GameBoardSceneName;
            x = onGameBoard ? GameConstants.PendingPanelWidth + 16f : CornerMargin;
            y = onGameBoard ? GameConstants.MarkerBarHeight + 16f : CornerMargin;
        }

        /// <summary>화면 좌측 하단 구석에 토스트를 하나 새로 띄운다. team이
        /// 유효한 팀 코드("A"/"B")면 그 팀 색으로, 아니면(빈 문자열 — 팀
        /// 소유가 뚜렷하지 않은 행동) 흰색으로 텍스트를 칠한다.
        /// BoardManager.UndoRedo.cs(되돌리기/전술카드 알림)와 이 클래스
        /// 자신(채팅, ReceiveChatMessage 경유)이 함께 부른다.</summary>
        internal void PushToast(string message, string team = "")
        {
            var go = new GameObject("Toast", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);

            var img = go.AddComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            // 배경/라벨 색을 각각 건드리지 않고 통째로 페이드시키려고
            // CanvasGroup.alpha 하나로 처리한다(ScreenshotToast와 동일한 이유).
            var group = go.AddComponent<CanvasGroup>();

            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = message;
            label.fontSize = 13f;
            label.color = GameConstants.ResolveTeamTextColor(team);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;

            go.transform.SetAsLastSibling();

            _toastStackEntries.Add(new ToastStackEntry
            {
                Go = go,
                Group = group,
                HideTime = Time.time + ToastStackVisibleDurationSeconds,
            });
        }

        /// <summary>매 프레임 폴링(코루틴 없음, 이 프로젝트 관례) — 토스트
        /// 페이드/제거/재배치와, 채팅 입력창·토스트 스택 시작 높이를 매
        /// 프레임 다시 계산해 배치한다(씬이 바뀌면 GetCorner의 기준 좌표
        /// 자체가 바뀌므로). 채팅 입력창이 열려있든 닫혀있든 토스트 시작
        /// 높이는 항상 입력창 높이+여백만큼 위로 고정 이동해둔다(사용자
        /// 요청 — "기존 토스트 메시지 출력 높이를 위로 살짝 높이고, 그
        /// 자리에 채팅창을 넣는거야") — 입력창 여닫음에 따라 토스트 위치가
        /// 들쭉날쭉하지 않도록.</summary>
        private void UpdateToastStack()
        {
            for (int i = _toastStackEntries.Count - 1; i >= 0; i--)
            {
                var e = _toastStackEntries[i];
                if (e.Go == null)
                {
                    _toastStackEntries.RemoveAt(i);
                    continue;
                }
                float fadeElapsed = Time.time - e.HideTime;
                if (fadeElapsed >= ToastStackFadeDurationSeconds)
                {
                    Destroy(e.Go);
                    _toastStackEntries.RemoveAt(i);
                    continue;
                }
                e.Group.alpha = fadeElapsed <= 0f ? 1f : 1f - fadeElapsed / ToastStackFadeDurationSeconds;
            }

            GetCorner(out float baseX, out float cornerY);

            if (_chatInputGo != null)
            {
                ((RectTransform)_chatInputGo.transform).anchoredPosition = new Vector2(baseX, cornerY);
            }

            float y = cornerY + ChatInputHeight + ChatToastGap;
            for (int i = _toastStackEntries.Count - 1; i >= 0; i--)
            {
                var rect = (RectTransform)_toastStackEntries[i].Go.transform;
                rect.anchoredPosition = new Vector2(baseX, y);
                y += ToastStackRowHeight + ToastStackGap;
            }
        }
    }
}
