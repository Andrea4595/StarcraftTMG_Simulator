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
    /// 닫힌다. 채팅 메시지는 팀 이름표를 붙여("[A] 안녕" 형태 —
    /// [유닛]/[택티컬] 대괄호 표기와 같은 문법) 그 팀 색으로 칠해 누가
    /// 말했는지 구분한다(사용자 지정).
    ///
    /// **2026-09-09, 토스트→영구 스크롤 로그로 교체(사용자 요청)**: 원래는
    /// 채팅도 되돌리기/전술카드 알림과 같은 잠깐 떴다 사라지는 토스트
    /// 스택을 공유했는데, 사용자가 "토스트 대신 스크롤 가능한 영구 채팅
    /// 로그 창"으로 바꿔달라고 요청 — 입력창 위 자리(예전 토스트 자리)에
    /// 고정 크기의 스크롤 창(ScrollRect)이 대신 뜨고, `PushToast`를 부르던
    /// 모든 곳(채팅 수신 + 되돌리기/전술카드 등 모든 액션 알림,
    /// UndoRedoService.ShowUndoLogEntry)이 이제 `PushLogEntry`로 로그 한
    /// 줄을 영구히 추가한다. 스크롤이 맨 아래에 있었다면 새 줄이 생겨도
    /// 계속 맨 아래를 따라가고, 위로 스크롤해서 과거를 보고 있었다면 그
    /// 자리 그대로 유지된다(BuildChatLog/PushLogEntry 참고).
    ///
    /// BoardManager는 GameBoard 씬에서만(코드로) 지어지는 반면, 채팅/로그는
    /// 멀티가 진행되는 모든 화면(CardPrep/CardDraft/TerrainSetup/GameBoard)
    /// 에서 다 떠야 한다는 요구라 — MultiplayerConnectDialog/
    /// DisconnectNoticeController와 완전히 같은 이유로 GameFlowBootstrap이
    /// 앱 시작 시 한 번만 만들고 DontDestroyOnLoad + Instance로 승격했다.
    /// 씬 전환을 넘어 계속 살아있으므로, 새 게임판(GameBoard) 세션이
    /// 시작될 때마다 BoardManager.Start()가 ClearLog()를 불러 이전 세션의
    /// 로그가 섞여 보이지 않게 한다(사용자 지정).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ChatController : MonoBehaviour
    {
        public static ChatController Instance { get; private set; }

        // ── 채팅/로그 창 ────────────────────────────────────────────────
        private const float ChatInputWidth = 340f;
        private const float ChatInputHeight = 32f;
        private const float ChatLogWidth = ChatInputWidth; // 입력창과 폭을 맞춰 시각적으로 하나의 패널처럼 보이게.
        private const float ChatLogHeight = 200f;
        private const float ChatLogGap = 8f; // 입력창 위쪽 끝과 로그 창 아래쪽 끝 사이 여백.
        private const float ChatLogEntrySpacing = 2f;
        private const float ChatLogPadding = 6f; // Viewport 안쪽 여백 — 창 높이를 콘텐츠에 맞출 때도 같은 값을 더해준다.
        private const int ChatLogMaxEntries = 200; // 넘으면 오래된 줄부터 지운다(메모리/성능 — 사용자 지정).
        // 로그 창은 항상 떠 있지 않는다(사용자 요청, 2026-09-09) — 마지막
        // 활동(새 메시지 수신 또는 채팅 입력창 열기) 후 ChatLogFadeDelaySeconds
        // 동안은 그대로 보이고, 그 뒤 ChatLogFadeDurationSeconds에 걸쳐
        // 서서히 투명해진다.
        private const float ChatLogFadeDelaySeconds = 5f;
        private const float ChatLogFadeDurationSeconds = 3f;
        private const float CornerMargin = 16f; // GameBoard가 아닌 씬(패널/마커바가 없음)에서의 화면 가장자리 여백.
        private const int ChatMessageMaxLength = 120;

        private GameObject _chatLogGo;
        private CanvasGroup _chatLogCanvasGroup;
        private RectTransform _chatLogViewport;
        private RectTransform _chatLogContent;
        private ScrollRect _chatLogScrollRect;
        // 앱을 막 시작한 시점엔 Time.time 자체가 0에 가까워서(0부터
        // 세기 시작함), 필드 기본값 0을 그대로 두면 "지금과의 차이"가
        // 작게 나와 로그가 시작부터 켜져 보이는 버그가 있었다(사용자 보고,
        // 2026-09-09) — Awake에서 HideLogImmediately()로 명시적으로
        // "충분히 오래전"으로 세팅한다.
        private float _lastLogActivityTime;
        private readonly List<GameObject> _chatLogEntries = new List<GameObject>();

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

            BuildChatLog();
            BuildChatInput();
            HideLogImmediately();
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
            UpdateChatLayout();

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

        /// <summary>사용자 보고 — 버튼 등 UI를 한 번 조작한 뒤 채팅을 열려고
        /// 엔터를 누르면, 그 버튼이 EventSystem에 "선택된 상태"로 계속 남아
        /// 있다가 유니티 기본 Submit 액션(엔터에 매핑됨)에 의해 다시 눌려버려
        /// 의도치 않은 조작이 한 번 더 일어난다. LateUpdate는 EventSystem
        /// 자신의 Update(Submit 판정 포함)보다 항상 나중에 실행되므로,
        /// 클릭이 일어난 바로 그 프레임에 선택을 지워두면 실행 순서와
        /// 무관하게 다음 프레임부터는 안전하다 — 채팅 입력창 자신과 다른
        /// 입력창(TMP_InputField, 이름 짓기 등)은 계속 선택 상태를 유지해야
        /// 타이핑이 끊기지 않으므로 제외한다.</summary>
        private void LateUpdate()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }
            var selected = es.currentSelectedGameObject;
            if (selected == null || selected == _chatInputGo)
            {
                return;
            }
            if (selected.GetComponent<TMP_InputField>() != null)
            {
                return;
            }
            es.SetSelectedGameObject(null);
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
            // 채팅 입력창을 열면 로그도 즉시 다시 보인다(사용자 요청,
            // 2026-09-09) — 이미 투명해져 있었더라도.
            _lastLogActivityTime = Time.time;
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
                // 아무것도 입력하지 않고 엔터만 누른 경우 — 입력창을 닫는
                // 것뿐 아니라 로그도 바로 꺼진다(사용자 요청, 2026-09-09 —
                // "채팅창에 아무것도 입력하지 않고 그냥 엔터 키를 누르면
                // 입력창 제거와 함께 채팅창도 꺼줘").
                HideLogImmediately();
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
            PushLogEntry($"[{team}] {message}", team);
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
        /// 실제로 보이는 영역의 모서리)에 자리를 잡아야 한다(예전
        /// BoardManager.ToastStack.cs가 쓰던 계산 그대로 재사용). 다른 씬
        /// (CardPrep/CardDraft/TerrainSetup)엔 그 패널/바가 아예 없으므로
        /// (조사 확인 — 그 씬들의 컨트롤러는 GameConstants.PendingPanelWidth/
        /// MarkerBarHeight를 전혀 참조하지 않는다) 화면 진짜 모서리에서 작은
        /// 여백만 두면 된다.</summary>
        private static void GetCorner(out float x, out float y)
        {
            bool onGameBoard = SceneManager.GetActiveScene().name == GameConstants.GameBoardSceneName;
            x = onGameBoard ? GameConstants.PendingPanelWidth + 16f : CornerMargin;
            y = onGameBoard ? GameConstants.MarkerBarHeight + 16f : CornerMargin;
        }

        /// <summary>채팅 입력창 바로 위, 예전 토스트 스택이 뜨던 자리에 고정
        /// 크기의 스크롤 가능한 로그 창을 짓는다(ScrollRect+Viewport+Content,
        /// 표준 uGUI 구성) — PushLogEntry가 Content의 마지막 자식으로 한
        /// 줄씩 추가한다.</summary>
        private void BuildChatLog()
        {
            _chatLogGo = new GameObject("ChatLog", typeof(RectTransform));
            _chatLogGo.transform.SetParent(transform, false);
            var logRect = (RectTransform)_chatLogGo.transform;
            logRect.anchorMin = new Vector2(0f, 0f);
            logRect.anchorMax = new Vector2(0f, 0f);
            logRect.pivot = new Vector2(0f, 0f);
            // 높이는 0에서 시작 — 아직 아무 줄도 없으므로(사용자 요청,
            // 2026-09-09: 콘텐츠에 맞춰 아래쪽 정렬로 딱 맞게). 첫 줄이
            // 생기면 PushLogEntry가 UpdateChatLogSize로 실제 크기를 잡는다.
            logRect.sizeDelta = new Vector2(ChatLogWidth, 0f);

            var bg = _chatLogGo.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);

            _chatLogCanvasGroup = _chatLogGo.AddComponent<CanvasGroup>();

            var scrollRect = _chatLogGo.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(_chatLogGo.transform, false);
            _chatLogViewport = (RectTransform)viewportGo.transform;
            _chatLogViewport.anchorMin = Vector2.zero;
            _chatLogViewport.anchorMax = Vector2.one;
            _chatLogViewport.offsetMin = new Vector2(ChatLogPadding, ChatLogPadding);
            _chatLogViewport.offsetMax = new Vector2(-ChatLogPadding, -ChatLogPadding);
            viewportGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(_chatLogViewport, false);
            _chatLogContent = (RectTransform)contentGo.transform;
            _chatLogContent.anchorMin = new Vector2(0f, 1f);
            _chatLogContent.anchorMax = new Vector2(1f, 1f);
            _chatLogContent.pivot = new Vector2(0.5f, 1f);
            // sizeDelta를 명시적으로 0으로 리셋해야 한다 — 새로 만든
            // RectTransform의 기본 sizeDelta는 (100,100)인데, 가로로 꽉 채운
            // (anchorMin.x=0/anchorMax.x=1) 상태에서 그 기본값이 그대로
            // 남으면 실제 너비가 뷰포트보다 100px 더 넓어져서 가운데 정렬된
            // 채 양쪽으로 삐져나온다 — 왼쪽으로 삐져나온 부분이 Viewport의
            // RectMask2D에 잘려 "로그 왼쪽이 잘려 보인다"는 버그로 나타났다
            // (사용자 보고, 2026-09-09). sizeDelta.y는 곧바로 아래
            // ContentSizeFitter가 실제 콘텐츠 높이로 덮어쓰므로 0이어도 무방.
            _chatLogContent.sizeDelta = Vector2.zero;
            _chatLogContent.anchoredPosition = Vector2.zero;

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = ChatLogEntrySpacing;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = _chatLogViewport;
            scrollRect.content = _chatLogContent;
            _chatLogScrollRect = scrollRect;
        }

        /// <summary>로그 창에 한 줄을 영구히 추가한다(더 이상 페이드/사라짐
        /// 없음 — 사용자 요청, 2026-09-09). team이 유효한 팀 코드("A"/"B")면
        /// 그 팀 색으로, 아니면(빈 문자열 — 팀 소유가 뚜렷하지 않은 행동)
        /// 흰색으로 텍스트를 칠한다. UndoRedoService.ShowUndoLogEntry(되돌리기/
        /// 전술카드 등 모든 액션 알림)와 이 클래스 자신(채팅, ReceiveChatMessage
        /// 경유)이 함께 부른다.
        ///
        /// 스크롤 동작(사용자 지정): 추가하기 *전에* 이미 맨 아래를 보고
        /// 있었으면, 추가한 뒤에도 다시 맨 아래로 스크롤한다(새 메시지가
        /// 계속 보임). 맨 아래가 아니었으면(과거 스크롤 중) 아무 것도 안
        /// 건드린다 — Content가 위쪽(pivot=top)에 고정된 채 아래로만
        /// 늘어나므로, 끝에 새 줄을 추가해도 이미 보고 있던 부분의 화면상
        /// 위치는 원래 안 움직인다(따로 보정할 필요 없음); 다만 ScrollRect의
        /// 정규화된 스크롤 값 자체는(전체 높이가 늘어났으므로) "맨 아래"
        /// 기준에서 살짝 밀려나므로, 맨 아래였던 경우에만 명시적으로
        /// 다시 0으로 스냅해준다.</summary>
        internal void PushLogEntry(string message, string team = "")
        {
            _lastLogActivityTime = Time.time;
            bool wasAtBottom = IsScrolledToBottom();

            var rowGo = new GameObject("LogRow", typeof(RectTransform));
            rowGo.transform.SetParent(_chatLogContent, false);
            var label = rowGo.AddComponent<TextMeshProUGUI>();
            label.text = message;
            label.fontSize = 13f;
            label.color = GameConstants.ResolveTeamTextColor(team);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;

            _chatLogEntries.Add(rowGo);
            while (_chatLogEntries.Count > ChatLogMaxEntries)
            {
                var oldest = _chatLogEntries[0];
                _chatLogEntries.RemoveAt(0);
                if (oldest != null)
                {
                    Destroy(oldest);
                }
            }

            // Content 높이가 즉시 재계산돼야(다음 프레임까지 기다리지 않고)
            // 아래 IsScrolledToBottom/스냅이 방금 추가한 줄을 포함한 최신
            // 크기 기준으로 정확히 계산된다.
            Canvas.ForceUpdateCanvases();
            UpdateChatLogSize();
            if (wasAtBottom)
            {
                _chatLogScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        /// <summary>로그 창 자체의 높이를 콘텐츠에 맞춰 위쪽 끝만 움직인다 —
        /// 창은 하단(입력창 바로 위)에 고정된 채(pivot=(0,0)) 내용이 적으면
        /// 짧게, ChatLogHeight까지는 내용만큼 자라다가 그 이상은 기존처럼
        /// 스크롤된다(사용자 요청, 2026-09-09 — "채팅창은 하단 정렬로,
        /// 배경인 검은색을 핏하게 아래로 줄여줘"). Content 자체의 실제 높이
        /// (ContentSizeFitter가 계산한 값)에 Viewport 안쪽 여백(위+아래)을
        /// 다시 더해야 창 전체 높이가 나온다.</summary>
        private void UpdateChatLogSize()
        {
            float height = Mathf.Min(_chatLogContent.rect.height + ChatLogPadding * 2f, ChatLogHeight);
            ((RectTransform)_chatLogGo.transform).sizeDelta = new Vector2(ChatLogWidth, height);
        }

        /// <summary>content가 viewport보다 짧아 아직 스크롤할 게 없으면
        /// (메시지가 몇 줄 안 쌓였을 때) 무조건 "맨 아래"로 취급한다 —
        /// ScrollRect.verticalNormalizedPosition의 경계값 동작(스크롤 불가
        /// 상태에서 0/1 중 뭘 돌려주는지)에 기대지 않기 위한 방어적 처리.
        /// 그 외에는 0에 가까운지(약간의 여유를 두고) 본다.</summary>
        private bool IsScrolledToBottom()
        {
            if (_chatLogContent.rect.height <= _chatLogViewport.rect.height + 0.5f)
            {
                return true;
            }
            return _chatLogScrollRect.verticalNormalizedPosition <= 0.01f;
        }

        /// <summary>BoardManager.Start()가 새 GameBoard 세션(신규 게임/불러오기/
        /// 게임 도중 합류 전부 포함)이 시작될 때마다 부른다 — ChatController는
        /// 씬 전환을 넘어 계속 사는 싱글턴이라, 안 지우면 이전 세션의 로그가
        /// 새 세션 로그와 섞여 보인다(사용자 지정, 2026-09-09).</summary>
        internal void ClearLog()
        {
            foreach (var go in _chatLogEntries)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            _chatLogEntries.Clear();
            Canvas.ForceUpdateCanvases();
            UpdateChatLogSize();
        }

        /// <summary>매 프레임 폴링(코루틴 없음, 이 프로젝트 관례) — 채팅
        /// 입력창·로그 창 위치를 매 프레임 다시 계산해 배치한다(씬이 바뀌면
        /// GetCorner의 기준 좌표 자체가 바뀌므로). 채팅 입력창이 열려있든
        /// 닫혀있든 로그 창 위치는 항상 입력창 높이+여백만큼 위로 고정
        /// 이동해둔다(입력창 여닫음에 따라 로그 위치가 들쭉날쭉하지 않도록,
        /// 예전 토스트 위치 계산과 같은 이유).</summary>
        private void UpdateChatLayout()
        {
            GetCorner(out float baseX, out float cornerY);

            if (_chatInputGo != null)
            {
                ((RectTransform)_chatInputGo.transform).anchoredPosition = new Vector2(baseX, cornerY);
            }

            if (_chatLogGo != null)
            {
                ((RectTransform)_chatLogGo.transform).anchoredPosition = new Vector2(baseX, cornerY + ChatInputHeight + ChatLogGap);
            }

            UpdateChatLogFade();
        }

        /// <summary>마지막 활동(새 메시지 수신 또는 채팅 입력창 열기)으로부터
        /// ChatLogFadeDelaySeconds 동안은 완전히 보이고, 그 뒤
        /// ChatLogFadeDurationSeconds에 걸쳐 서서히 투명해진다(사용자 요청,
        /// 2026-09-09 — "항상 떠 있지 말고"). 완전히 투명해지면 뒤에 있는
        /// 보드 클릭이 막히지 않도록 blocksRaycasts도 함께 끈다(입력창을
        /// 숨길 때 CanvasGroup으로 처리하는 것과 같은 이유).</summary>
        private void UpdateChatLogFade()
        {
            if (_chatLogCanvasGroup == null)
            {
                return;
            }
            float alpha;
            if (_chatOpen)
            {
                // 입력창을 띄워둔 채 가만히 있는 동안은(아직 안 보내고 타이핑
                // 중이든, 그냥 열어만 두었든) 로그가 계속 켜져 있어야 한다
                // (사용자 요청, 2026-09-09) — 경과 시간과 무관하게 항상 완전히
                // 보이게 고정.
                alpha = 1f;
            }
            else
            {
                float elapsed = Time.time - _lastLogActivityTime;
                alpha = elapsed <= ChatLogFadeDelaySeconds
                        ? 1f
                        : 1f - Mathf.Clamp01((elapsed - ChatLogFadeDelaySeconds) / ChatLogFadeDurationSeconds);
            }
            _chatLogCanvasGroup.alpha = alpha;
            _chatLogCanvasGroup.blocksRaycasts = alpha > 0f;
            _chatLogCanvasGroup.interactable = alpha > 0f;
        }

        /// <summary>_lastLogActivityTime을 "충분히 오래전"으로 세팅해 다음
        /// UpdateChatLogFade에서 즉시(페이드 없이) 완전히 투명해지게 한다 —
        /// Time.time의 절대값이 앱 시작 시점엔 0에 가까울 수 있으므로,
        /// 고정된 과거 시각이 아니라 항상 "지금 기준 충분히 오래전"으로
        /// 상대적으로 계산한다.</summary>
        private void HideLogImmediately()
        {
            _lastLogActivityTime = Time.time - (ChatLogFadeDelaySeconds + ChatLogFadeDurationSeconds + 1f);
        }
    }
}
