using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 보드 위 베이스 조각들을 소유하고, 다이얼 메뉴 액션(데미지 기록/모델
    /// 제거/리스폰/메모/리저브 복귀)과 유닛 이동 워크플로우(리딩 모델
    /// 배치→팔로워 배치→완료/취소, 코헤런시 판정), 예비대 배치(배치 목록→리딩
    /// 모델 드래그→팔로워 자동 배치), 변위 베이스 재배치(밀어낸 자리 재지정)를
    /// 처리한다. Godot판 GameBoard.gd 포팅. 배치 밴드는 MissionData에 이
    /// 팀의 배치구역이 있으면 실제 구역을, 없으면(미션 설정을 안 거쳤으면)
    /// 지도 가장자리 폴백을 보여준다. 마우스를 올린 유닛의 메모를 모델
    /// 아래에 띄워준다(MemoOverlay).
    /// 이 파일은 코어(필드 선언, Start/Configure류, Update 디스패처, 조각
    /// 스폰/드래그, 다이얼 메뉴 라우팅, 모델 제거/복제/되돌리기)만 담고,
    /// 기능별 나머지는 같은 폴더의 partial 파일들에 나뉘어 있다:
    /// BoardManager.PanZoom / .Measure / .Markers / .Memo / .Range /
    /// .UnitMove / .Deployment / .Roster / .Displacement / .UndoRedo.cs.
    /// </summary>
    public partial class BoardManager : MonoBehaviour
    {
        [SerializeField] private RectTransform baseLayer;
        [SerializeField] private RectTransform mapArea;
        [SerializeField] private RadialMenu radialMenu;
        [SerializeField] private InputDialog damageDialog;
        [SerializeField] private InputDialog memoDialog;
        [SerializeField] private GuidelineOverlay guideline;
        [SerializeField] private MemoOverlay memoOverlay;
        [SerializeField] private RangeInputDialog rangeInputDialog;
        [SerializeField] private RangeOverlay rangeOutlineLayer;
        [SerializeField] private RangeOverlay rangeFillLayer;
        [SerializeField] private MeasureOverlay measureLayer;
        [SerializeField] private RectTransform markerLayer;
        [SerializeField] private RectTransform terrainLayer;
        [SerializeField] private DiceRollDialog diceRollDialog;
        [SerializeField] private RolloffDialog rolloffDialog;
        [SerializeField] private UndoHistoryDialog undoHistoryDialog;
        [SerializeField] private WeaponProfileDialog weaponProfileDialog;
        [SerializeField] private ConfirmDialog exitConfirmDialog;
        [SerializeField] private InputDialog saveNameDialog;
        [SerializeField] private EmotePickerPanel emotePickerPanel;
        [SerializeField] private Vector2 mapSizeMm = new Vector2(36f * GameConstants.MmPerInch, 36f * GameConstants.MmPerInch);

        private const float DuplicateGapMm = 4f;
        private const float FollowerRingFraction = 0.7f;
        private const float CoherencyEpsilonMm = 0.5f; // 경계에 스냅됐을 때 부동소수점 오차로 오탐지되는 것 방지
        private const float FollowerSnapThresholdMm = 6f;
        private const float RayOriginOffsetMm = 1f; // 배치 밴드 경계 위 점에서 반직선을 쏠 때, 자기 자신이 속한 변과의 자기교차(t≈0) 방지
        private const float UnitMoveConfirmIconSizeMm = 24f; // "이동 확정" 체크 아이콘(ComfirmDial.png) 크기
        private const float UnitMoveConfirmIconMarginMm = 16f; // 리딩 모델 테두리로부터 위로 띄우는 여백

        // ── 전열/지원열 하이라이팅 + 인게이지 경고 ───────────────────────
        // EngageDistanceMm은 둘 다 공유한다(같은 룰북 개념 — 인게이지 거리 1").
        private const float EngageDistanceMm = GameConstants.MmPerInch; // 인게이지 거리(1")
        private const float BaseContactThresholdMm = 2f; // "베이스 접촉" 판정 여유(완전히 0이 아니라 살짝 여유를 둠)
        private readonly HashSet<Base> _combatRowHighlighted = new HashSet<Base>();
        private readonly HashSet<Base> _engageWarningHighlighted = new HashSet<Base>();

        // ── 화면 이동/확대축소(패닝/줌) ─────────────────────────────────
        // mapArea가 baseLayer/guideline/memoOverlay를 감싸고, 이 하나의
        // 트랜스폼(localScale/anchoredPosition)만 조작해서 지도 전체를
        // 함께 움직인다. Godot판 GameBoard.gd의 _map_area/_layout/_zoom_at
        // 포팅. baseLayer 등은 mapArea를 그대로 꽉 채우므로(anchor 0~1),
        // 조각들의 anchoredPosition(=mm 중심 좌표) 의미는 그대로 유지된다.
        private const float ZoomStep = 1.1f;
        private const float MinZoom = 0.3f;
        private const float MaxZoom = 4f;

        private float _zoomLevel = 1f;
        private float _baseScaleFactor = 1f;
        private bool _panning;
        private Vector2 _lastPanScreenPos;
        private Vector2 _lastViewportSize;

        private readonly PieceRoster _pieces = new PieceRoster();
        private Base _menuTarget;

        // 네트워크 id(int) -> 라이브 오브젝트 매핑 — 유닛/마커 각각 하나씩
        // (NetworkIdentityRegistry.cs 참고). UnitSync.cs/Markers.cs뿐 아니라
        // Range.cs/UndoRedo.cs/Load.cs/MidGameHandoff.cs도 직접 쓴다.
        private readonly NetworkIdentityRegistry<Unit> _networkedUnits = new();
        private readonly NetworkIdentityRegistry<MarkerBase> _networkedMarkers = new();

        // 되돌리기/다시하기 스택 등(UndoRedoService.cs 참고, 2026-09-02
        // 리팩토링 Phase 3) — 캡처/복원 자체(BoardManager.UndoRedo.cs)는 씬
        // 참조에 직접 의존해서 여기 남아있고, 이 서비스가 그 둘을 호출한다.
        // 필드 초기화식에서 this를 못 써서(CS0027) Awake()에서 만든다 —
        // 다른 컴포넌트의 Start()보다 항상 먼저 실행되므로 안전하다.
        private UndoRedoService _undoRedo;

        private void Awake()
        {
            _undoRedo = new UndoRedoService(this);
        }

        // 리플레이 모드(BoardManager.Replay.cs 참고, 2026-09-03 추가) —
        // Entry의 "리플레이" 버튼으로 저장 파일을 열었을 때만 쓰인다.
        private bool _replayMode;
        private List<(BoardSnapshot Snapshot, string Label, string Team)> _replayFrames;
        private int _replayFrameIndex;
        private GraphicRaycaster _mainRaycaster;

        private Base _draggingPiece;
        private Base _draggingFollower;
        private Vector2 _dragOffset;

        // 일반 드래그(유닛 이동 중이 아닐 때) 전용 — 클릭과 드래그를 구분한다
        // (사용자 보고, 2026-09-09: "유닛 정보 확인하려는데 이동됨"). 마우스를
        // 누른 순간 바로 _draggingPiece를 세우지 않고, 이 임계값을 넘는
        // 움직임이 실제로 있어야만 진짜 드래그로 승격한다 — 그 전까지는
        // 클릭(상세 패널 선택은 OnDragRequested 맨 앞에서 이미 무조건
        // 처리됨)일 뿐, 되돌리기 트랜잭션도 위치 재계산도 하지 않는다.
        private const float ClickVsDragThresholdMm = 3f;
        private Base _clickCandidatePiece;
        private Vector2 _clickCandidateMouseDownLocal;

        private bool _unitMoveActive;
        private Base _unitMoveLeading;
        private Unit _unitMoveUnit;
        private string _unitMovePhase = ""; // "leading" / "followers"
        private Vector2 _unitMoveStartPoint;
        // 리딩 모델 웨이포인트 경로 — 뗄 때마다(드래그 종료) 그 지점이 여기
        // 쌓이고, 리딩 단계는 끝나지 않는다("이동 확정" 버튼을 눌러야
        // FinishLeadingMove로 넘어감). _unitMoveLastAnchor는 마지막으로
        // 확정된 지점(웨이포인트 중 마지막, 없으면 시작점) — Shift 스냅
        // 밴드의 중심이자 지금 드래그 중인 마지막 구간의 시작점이다.
        private readonly List<Vector2> _unitMoveWaypoints = new List<Vector2>();
        private Vector2 _unitMoveLastAnchor;
        // 웨이포인트마다 그 자리에 남는 반투명 고스트(리딩 모델과 같은 크기/
        // 색/회전) — ShowBasePlacementPreview와 같은 패턴(사용자 요청).
        private readonly List<Base> _unitMoveWaypointGhosts = new List<Base>();

        // 상대가 자신의 리딩 모델을 재이동시키는 동안 보여주는 "구경용"
        // 고스트 — 위 _unitMoveWaypointGhosts와 완전히 별개 목록(사용자
        // 요청: 상대 이동/가이드라인도 공유). -1이면 지금 표시 중인 상대
        // 이동이 없다는 뜻.
        private readonly List<Base> _remoteUnitMoveGhosts = new List<Base>();
        private int _remoteUnitMoveNetworkId = -1;
        private readonly Dictionary<Base, Vector2> _unitMoveOriginalPositions = new Dictionary<Base, Vector2>();

        private RectTransform _unitMovePanel;
        private TextMeshProUGUI _unitMoveWarningLabel;
        private RectTransform _unitMoveConfirmIcon;

        // ── 예비대 배치 ──────────────────────────────────────────────────
        // 팀별로 별도 패널(A는 왼쪽, B는 오른쪽)에 나눠 보여준다.
        private readonly OwnedList<PendingUnitDef> _pendingUnits = new OwnedList<PendingUnitDef>();
        private readonly Dictionary<string, RectTransform> _pendingUnitsListContainers = new Dictionary<string, RectTransform>();
        // 항목 수에 맞춰 목록 박스 높이를 그때그때 다시 맞추는 데 쓴다
        // (ScrollListUtil.ApplyFittedHeight) — RefreshPendingList/
        // RefreshRosterTokenList 참고.
        private readonly Dictionary<string, LayoutElement> _pendingUnitsListLayoutElements = new Dictionary<string, LayoutElement>();
        private PendingUnitDef _pendingDeploymentDef;
        private bool _unitMoveIsDeployment;
        private PendingUnitDef _deploymentDefSnapshot;
        private int _pendingFollowerCount;
        private Base _placementPreview;

        // ── 로스터 JSON 임포트 / 토큰 ────────────────────────────────────
        [SerializeField] private RosterFileDialog rosterFileDialog;
        private string _rosterImportTeam = "A";
        // 로스터를 불러오고 나면(또는 이미 예비대가 있으면) 이 버튼은 숨기고
        // 목록을 보여준다 — 한 번 "로드됨"으로 판정되면 나중에 목록이 다시
        // 비어도(전부 배치해서) 버튼이 되살아나지 않는다. RefreshPanelLayout 참고.
        private readonly Dictionary<string, GameObject> _rosterImportButtons = new Dictionary<string, GameObject>();
        private readonly HashSet<string> _rosterLoadedTeams = new HashSet<string>();
        private readonly OwnedList<PendingTokenDef> _pendingRosterTokens = new OwnedList<PendingTokenDef>();
        private readonly Dictionary<string, RectTransform> _rosterTokenListContainers = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, LayoutElement> _rosterTokenListLayoutElements = new Dictionary<string, LayoutElement>();
        // 라벨+목록을 함께 켜고 끄기 위한 래퍼 — 토큰이 하나도 없는 팀은
        // "토큰" 섹션 자체를 접어둔다(RefreshRosterTokenList가 관리).
        private readonly Dictionary<string, GameObject> _tokenSectionRoots = new Dictionary<string, GameObject>();
        // "팀|이름" -> Unit. 같은 토큰은 이미 배치된 것과 한 유닛으로 합쳐진다.
        private readonly Dictionary<string, Unit> _rosterTokenUnits = new Dictionary<string, Unit>();
        private PendingTokenDef _pendingRosterTokenDef;

        // 택티컬 카드 — 배치되는 게 아니라 그냥 목록에서 켜고 끄기만 하므로
        // (좌클릭 소진/우클릭 복구) 토큰과 달리 "배치 중" 상태 자체가 없다.
        private readonly OwnedList<TacticalCardDef> _pendingTacticalCards = new OwnedList<TacticalCardDef>();
        private readonly Dictionary<string, RectTransform> _tacticalCardListContainers = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, LayoutElement> _tacticalCardListLayoutElements = new Dictionary<string, LayoutElement>();

        // ── 유닛 상세 패널 ────────────────────────────────────────────────
        // 팀 패널의 위쪽 60% 영역(TopRegion — 로스터 불러오기 버튼/예비대
        // 목록/토큰을 전부 가림)만 대신 채운다 — 아래쪽 40%(택티컬 카드)는
        // 그대로 둔다(사용자 지정). BoardManager.UnitDetail.cs 참고.
        private readonly Dictionary<string, RectTransform> _unitDetailContainers = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, LayoutElement> _unitDetailLayoutElements = new Dictionary<string, LayoutElement>();
        // team별로 독립된 "지금 이 패널에 무엇을 마지막으로 그렸나" 기록 —
        // 두 팀 패널이 동시에, 서로 다른 유닛을 보여줄 수 있어야 한다(사용자
        // 요청: 공격자 무기 프로필 + 수비자 방어/회피를 동시에 봐야 함).
        private readonly Dictionary<string, object> _unitDetailLastSourceByTeam = new Dictionary<string, object>();
        // 위 소스가 같은 참조를 유지하는 동안에도(같은 Unit) 모델 수가 바뀔
        // 수 있어서(팔로워가 나중에 붙거나, 모델이 죽는 등) 참조 비교만으로는
        // 다시 그려야 할 시점을 놓친다 — 마지막으로 그렸을 때의 모델 수도
        // 같이 기록해둔다(UpdateUnitDetailPanelForTeam 참고).
        private readonly Dictionary<string, int> _unitDetailLastModelCountByTeam = new Dictionary<string, int>();
        // 지도 위 유닛을 좌클릭하면 그 유닛의 team 쪽 항목이 켜진다
        // (OnDragRequested) — 빈 땅을 좌클릭하면 둘 다 꺼진다(Update() 맨 끝),
        // 같은 team의 다른 유닛을 좌클릭하면 그 team 항목만 바뀐다. 다른 team
        // 유닛을 좌클릭해도 이 team의 항목엔 영향 없음(서로 독립). 우클릭
        // 다이얼 메뉴 쪽 트리거보다 낮은 우선순위 — UpdateUnitDetailPanel 참고.
        private readonly Dictionary<string, Unit> _selectedUnitForDetailByTeam = new Dictionary<string, Unit>();

        // ── 변위 베이스 재배치 ──────────────────────────────────────────
        private Base _displacementAnchor;
        private readonly List<Base> _displacementQueue = new List<Base>();
        private bool _displacementResumeLeadingFinish;

        // ── 메모 호버 표시 ───────────────────────────────────────────────
        private Unit _hoveredUnit;
        private Base _hoveredBase;
        private readonly List<(string Text, Vector2 Pos)> _memoEntries = new List<(string, Vector2)>();

        // ── 범위 표시 ────────────────────────────────────────────────────
        private readonly Dictionary<Unit, List<RangeSpec>> _unitRanges = new Dictionary<Unit, List<RangeSpec>>();
        private Unit _rangeTargetUnit;
        private Unit _rangeDeleteTargetUnit;
        private Vector2 _menuScreenPos;

        // ── 거리 재기(스페이스바) ───────────────────────────────────────
        private bool _measuring;
        private Vector2 _measureFromPoint;
        private Base _measureFromBase;

        // ── 마커(활성화/점령/아이콘) ───────────────────────────────────
        // 값 자체는 GameConstants.MarkerBarHeight로 옮겼다(WeaponProfileDialog/
        // DiceRollDialog 같은 독립 컴포넌트도 지도 뷰포트 경계를 알아야 해서).
        private static readonly (string Kind, string Label)[] MarkerBarEntries =
        {
            ("activation", "활성화 마커"),
            ("capture", "점령 마커"),
            ("movement", "이동 마커"),
            ("assault", "돌격 마커"),
            ("combat", "전투 마커"),
            ("buff", "버프 마커"),
            ("debuff", "디버프 마커"),
            ("blast", "블라스트 템플릿"),
        };

        /// <summary>마커바 아이콘에 마우스를 올렸을 때 보여줄 조작법 —
        /// 마커 종류마다 우클릭 동작이 다르다(활성화/점령은 순환+Shift로 삭제,
        /// 아이콘류는 그냥 삭제)는 걸 그때그때 알려달라는 사용자 요청.</summary>
        private static readonly Dictionary<string, string> MarkerControlHints = new Dictionary<string, string>
        {
            { "activation", "좌클릭: 배치 - 드래그: 이동 - 우클릭: 상태 순환(이동->돌격->완료) - Shift+우클릭: 삭제" },
            { "capture", "좌클릭: 배치 - 드래그: 이동 - 우클릭: 색 순환(중립 / 플레이어 A / B) - Shift+우클릭: 삭제" },
            { "movement", "좌클릭: 배치 - 드래그: 이동 - 우클릭: 삭제" },
            { "assault", "좌클릭: 배치 - 드래그: 이동 - 우클릭: 삭제" },
            { "combat", "좌클릭: 배치 - 드래그: 이동 - 우클릭: 삭제" },
            { "buff", "좌클릭: 배치 - 드래그: 이동 - 우클릭: 삭제" },
            { "debuff", "좌클릭: 배치 - 드래그: 이동 - 우클릭: 삭제" },
            { "blast", "좌클릭: 배치(근처 유닛에 자동 스냅) - 드래그: 이동(스냅) - 우클릭: 삭제" },
        };

        private TextMeshProUGUI _markerHintLabel;

        private ActivationMarker _activationMarkerPrefab;
        private CaptureMarker _captureMarkerPrefab;
        private Dictionary<string, IconMarker> _iconMarkerPrefabsByKind;

        private string _placingMarkerKind = ""; // 비었으면 없음. MarkerBarEntries의 Kind 값 중 하나
        private MarkerBase _draggingMarker;
        private Vector2 _markerDragOffset;
        private MarkerBase _markerPlacementPreview;

        // 되돌리기(취소/복원) 관련 필드·로직은 전부 BoardManager.UndoRedo.cs에
        // 있다 — 화면 우측 하단 "되돌리기" 버튼 → 모달(UndoHistoryDialog)로
        // 조작 리스트/Undo 리스트를 보여주고 선택적으로 취소·복원한다
        // (2026-09-01 재구성, 예전 Ctrl+Z 단축키 방식은 제거됨).

        /// <summary>되돌리기 조작 리스트에 유닛을 언급할 때 공통으로 쓰는
        /// 표기 — "[유닛] 유닛이름" 형태(2026-09-04 재구성: 팀 문자는 이제
        /// 텍스트가 아니라 색으로 표시하므로 — BeginUndoTransaction의 team
        /// 인자, GameConstants.ResolveTeamTextColor 참고 — 이름 앞에서
        /// 뺐다). unit이 null이면(방어적) 그냥 "[유닛]".</summary>
        private static string DescribeUnit(Unit unit)
        {
            return unit == null ? "[유닛]" : $"[유닛] {unit.UnitName}";
        }

        private void Start()
        {
            // ChatController는 씬 전환을 넘어 계속 사는 싱글턴이라(DontDestroyOnLoad),
            // 새 GameBoard 세션(신규 게임/불러오기/게임 도중 합류 전부 포함)이
            // 시작될 때마다 이전 세션의 채팅/액션 로그를 지워야 섞여 보이지
            // 않는다(사용자 지정, 2026-09-09).
            ChatController.Instance?.ClearLog();

            // Awake가 아니라 Start에서 구독한다 — 코드로 씬을 구성할 때(부트스트랩
            // 등) Configure()가 AddComponent 직후 동기적으로 불리는데, 그게 Awake
            // 이후·Start 이전에 끝나므로 여기서는 항상 필드가 채워져 있다.
            radialMenu.ActionChosen += OnActionChosen;
            damageDialog.Confirmed += OnDamageConfirmed;
            memoDialog.Confirmed += OnMemoConfirmed;
            rangeInputDialog.Confirmed += OnRangeConfirmed;
            rangeInputDialog.Cancelled += () => _rangeTargetUnit = null;
            if (rosterFileDialog != null)
            {
                rosterFileDialog.FileSelected += OnRosterFileSelected;
            }
            BuildUnitMovePanel();
            BuildUnitMoveConfirmIcon();
            BuildPendingPanel();
            if (markerLayer != null)
            {
                BuildMarkerBar();
            }
            UpdateMapLayout();
            InitScreenshotSession();
            BuildMissionObjectiveVisuals();
            BuildDeploymentZoneVisuals();
            BuildTerrainVisuals();

            // 저장된 게임을 불러오는 중이면(GameLoadRequest, Entry의 "이어하기"
            // 화면에서 세팅됨) — 위 세 줄이 지형/배치구역/미션 마커를 이미
            // MapData 기준으로 지어놓은 뒤라야 라이브 상태(유닛/마커/미션
            // 마커 순환 상태 등)를 안전하게 덮어씌울 수 있다. 정적 부분
            // (MapData/MissionSettingsData/MatchState/TeamColors)은
            // GameFlowBootstrap이 이 씬을 짓기 전에 이미 채워뒀다. 반드시
            // 마지막에 비워야 다음에 "새 게임"으로 이 씬에 다시 들어왔을 때
            // 이전 저장을 다시 불러오지 않는다.
            if (GameLoadRequest.PendingData != null)
            {
                var pendingData = GameLoadRequest.PendingData;
                GameLoadRequest.PendingData = null;
                ApplyLoadedLiveState(pendingData);
                // 리플레이 진입이면(Entry의 "리플레이" 버튼, GameLoadRequest.
                // IsReplayLoad) 방금 지은 라이브 상태를 그대로 두지 않고
                // 읽기 전용 재생 모드로 전환한다 — BoardManager.Replay.cs
                // 참고.
                if (GameLoadRequest.IsReplayLoad)
                {
                    GameLoadRequest.IsReplayLoad = false;
                    EnterReplayMode(pendingData);
                }
            }

            // 게임 도중 멀티 합류로 들어온 경우에만 채워져 있다(저장 파일
            // 불러오기는 되돌리기 히스토리를 안 담으므로 이 값이 없다) —
            // 호스트의 되돌리기 스택을 그대로 이어받는다(BoardManager.
            // UndoRedo.cs의 ApplySeededUndoHistory).
            if (GameLoadRequest.PendingUndoHistory != null)
            {
                ApplySeededUndoHistory(GameLoadRequest.PendingUndoHistory);
                GameLoadRequest.PendingUndoHistory = null;
            }

            // "같이 하기"→"호스트로 시작"→"이어하기"로 들어온 경우만 켜져
            // 있다(2026-09-04 추가, MultiplayerConnectDialog.
            // OnHostContinueClicked 참고) — 이 판을 곧바로 호스팅할 수 있게,
            // "호스트로 시작"을 다시 누른 것처럼 곧장 호스팅을 시작하고
            // 코드를 띄운다(선택 화면들을 다시 거치지 않는다).
            if (GameLoadRequest.AutoOpenMultiplayerAfterLoad)
            {
                GameLoadRequest.AutoOpenMultiplayerAfterLoad = false;
                MultiplayerConnectDialog.Instance?.OpenAndStartHosting();
            }
        }

        /// <summary>씬을 코드로 구성할 때(부트스트랩 등) 인스펙터 대신 쓰는 초기화.</summary>
        public void Configure(RectTransform baseLayerRef, RectTransform mapAreaRef, RadialMenu radialMenuRef,
                InputDialog damageDialogRef, InputDialog memoDialogRef,
                GuidelineOverlay guidelineRef, MemoOverlay memoOverlayRef,
                RangeInputDialog rangeInputDialogRef, RangeOverlay rangeOutlineLayerRef, RangeOverlay rangeFillLayerRef,
                MeasureOverlay measureLayerRef)
        {
            baseLayer = baseLayerRef;
            mapArea = mapAreaRef;
            radialMenu = radialMenuRef;
            damageDialog = damageDialogRef;
            memoDialog = memoDialogRef;
            guideline = guidelineRef;
            memoOverlay = memoOverlayRef;
            rangeInputDialog = rangeInputDialogRef;
            rangeOutlineLayer = rangeOutlineLayerRef;
            rangeFillLayer = rangeFillLayerRef;
            measureLayer = measureLayerRef;
        }

        /// <summary>마커(활성화/점령/아이콘) 배치용 프리팹과 레이어를 별도로
        /// 주입한다 — Configure()가 이미 파라미터가 많아서 마커 쪽은 분리했다.
        /// 마커바 UI는 다음 Start()에서 지어진다. 각 프리팹은 자기 텍스처/크기를
        /// 이미 갖고 있으므로 여기선 kind별 조회용 딕셔너리만 만든다.</summary>
        public void ConfigureMarkers(RectTransform markerLayerRef,
                ActivationMarker activationMarkerPrefab, CaptureMarker captureMarkerPrefab,
                IconMarker[] iconMarkerPrefabs)
        {
            markerLayer = markerLayerRef;
            _activationMarkerPrefab = activationMarkerPrefab;
            _captureMarkerPrefab = captureMarkerPrefab;
            _iconMarkerPrefabsByKind = new Dictionary<string, IconMarker>();
            foreach (var prefab in iconMarkerPrefabs)
            {
                if (prefab != null) _iconMarkerPrefabsByKind[prefab.Kind] = prefab;
            }
        }

        /// <summary>로스터 JSON 임포트용 파일 탐색기 다이얼로그를 주입한다.</summary>
        public void ConfigureRoster(RosterFileDialog rosterFileDialogRef)
        {
            rosterFileDialog = rosterFileDialogRef;
        }

        /// <summary>미션 설정에서 넘어온 지형 조각(읽기 전용 표시용) 레이어를
        /// 주입한다 — 물리 없는 순수 시각 참고용(사용자와 합의된 방침).</summary>
        public void ConfigureTerrain(RectTransform terrainLayerRef)
        {
            terrainLayer = terrainLayerRef;
        }

        /// <summary>주사위 굴리기 툴 창을 주입한다 — 보드 상태와 완전히 무관한
        /// 독립 컴포넌트라 마커바 버튼에서 여닫는 것 외에는 BoardManager가
        /// 손댈 일이 없다.</summary>
        public void ConfigureDiceRoll(DiceRollDialog diceRollDialogRef)
        {
            diceRollDialog = diceRollDialogRef;
        }

        /// <summary>롤 오프 모달을 주입한다 — 다이스 롤 창과 같은 이유로 보드
        /// 상태와 완전히 무관한 독립 컴포넌트다.</summary>
        public void ConfigureRolloff(RolloffDialog rolloffDialogRef)
        {
            rolloffDialog = rolloffDialogRef;
        }

        /// <summary>되돌리기 모달을 주입한다 — 롤 오프 창과 같은 이유로 보드
        /// 상태와 완전히 무관한 독립 컴포넌트지만, 조작 리스트/Undo 리스트를
        /// 그리려면 이 BoardManager를 다시 참조해야 해서(GetOperationHistoryForDisplay
        /// 등) Open() 호출부(CreateUndoHistoryButton)가 자기 자신을 넘겨준다.</summary>
        public void ConfigureUndoHistory(UndoHistoryDialog undoHistoryDialogRef)
        {
            undoHistoryDialog = undoHistoryDialogRef;
        }

        /// <summary>빈 땅 우클릭으로 여는 이모트 선택 팝업을 주입한다(BoardManager.Emote.cs
        /// 참고) — 롤오프/되돌리기 창과 같은 이유로 독립 컴포넌트다.</summary>
        public void ConfigureEmote(EmotePickerPanel emotePickerPanelRef)
        {
            emotePickerPanel = emotePickerPanelRef;
            emotePickerPanel.EmoteChosen += OnEmoteChosen;
        }

        /// <summary>유닛 상세 패널의 무기 능력 항목 "무기 프로필 보기" 버튼이 여는
        /// 팝업을 주입한다 — 다이스 롤 창과 같은 이유로 독립 컴포넌트다.</summary>
        public void ConfigureWeaponProfile(WeaponProfileDialog weaponProfileDialogRef)
        {
            weaponProfileDialog = weaponProfileDialogRef;
        }

        /// <summary>마커바 "나가기" 버튼(BoardManager.Markers.cs의
        /// CreateExitButton)이 여는 확인 창을 주입한다 — 확인을 누르면
        /// 진행 중이던 판을 버리고 맨 처음 화면(Entry)으로 돌아간다
        /// (OnExitConfirmed).</summary>
        public void ConfigureExit(ConfirmDialog exitConfirmDialogRef)
        {
            exitConfirmDialog = exitConfirmDialogRef;
            exitConfirmDialog.Confirmed += OnExitConfirmed;
        }

        /// <summary>마커바 "저장" 버튼(BoardManager.Markers.cs의
        /// CreateSaveButton)이 여는 이름 입력 창을 주입한다 — 확인하면
        /// 그 이름으로 Saves/ 폴더에 저장한다(BoardManager.Save.cs의
        /// SaveGame).</summary>
        public void ConfigureSave(InputDialog saveNameDialogRef)
        {
            saveNameDialog = saveNameDialogRef;
            saveNameDialog.Confirmed += OnSaveNameConfirmed;
            saveNameDialog.Confirmed += _ => SaveDialogClosed?.Invoke();
            saveNameDialog.Cancelled += () => SaveDialogClosed?.Invoke();
        }

        /// <summary>저장 이름 입력창이 확인/취소 어느 쪽으로든 닫히면 올라간다
        /// (RequestSave로 이 창을 연 바깥 컴포넌트가 그 뒤에 할 일이 있을 때
        /// 쓴다 — DisconnectNoticeController가 "저장" 후 자기 알림 모달을
        /// 다시 띄우는 데 쓴다).</summary>
        public event System.Action SaveDialogClosed;

        /// <summary>마커바 "저장" 버튼(BoardManager.Markers.cs의 CreateSaveButton)과
        /// 똑같이 이름 입력 창을 연다 — DisconnectNoticeController처럼 이 씬
        /// 바깥의 컴포넌트가 저장을 트리거해야 할 때 쓴다(상대방과의 연결이
        /// 끊겼을 때 뜨는 "저장" 버튼).</summary>
        public void RequestSave()
        {
            if (saveNameDialog != null)
            {
                saveNameDialog.Open("저장 이름", "");
            }
        }

        private void OnSaveNameConfirmed(string name)
        {
            name = name.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            string path;
            try
            {
                path = System.IO.Path.Combine(GameSaveIO.ResolveSavesDirectory(), $"{SanitizeSaveFileName(name)}.json");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"저장 경로를 만들 수 없습니다: {e.Message}");
                return;
            }
            try
            {
                SaveGame(path);
                ShowScreenshotToast($"{SanitizeSaveFileName(name)}.json", GameSaveIO.ResolveSavesDirectory());
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"게임을 저장하지 못했습니다: {path} ({e.Message})");
            }
        }

        private static string SanitizeSaveFileName(string name)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        /// <summary>2026-08-30 재구성 이후로는 Entry→Selection→TerrainSetup→
        /// GameBoard가 씬 하나짜리 선형 흐름이라(예전 GameFlowState의
        /// "맵/미션 중 어느 쪽을 먼저 끝냈는지" 교차-씬 추적이 더 이상 필요
        /// 없다), Entry로 돌아가는 것 외에 별도로 리셋할 흐름 상태는 MatchState
        /// 뿐이었다. 그런데 GameConstants.TeamColors(플레이어가 스코어보드에서
        /// 바꾼 A/B 색)는 그동안 여기서 전혀 리셋되지 않아서, 나갔다 새로
        /// 시작한 판의 지형 배치 화면(미션 마커 참고 표시 등)에 지난 판에서
        /// 바꾼 색이 그대로 남아있는 실제 버그가 있었다(사용자 발견) — 같이
        /// 리셋한다.</summary>
        private void OnExitConfirmed()
        {
            // 멀티 연결 중이었다면 여기서 확실히 끊는다(사용자 보고, 2026-09-02
            // — 예전엔 이 버튼이 씬만 옮기고 NetworkManager는 그대로 둬서,
            // 나가고 나서도 호스트/클라이언트 세션이 계속 살아있었다). 내가
            // 스스로 나가는 것이므로 DisconnectNoticeController의 알림이 나
            // 자신에게는 뜨면 안 된다(사용자 요청, 같은 날) — Shutdown() 전에
            // 미리 표시해둔다.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                DisconnectNoticeController.SuppressNextNotice = true;
                NetworkManager.Singleton.Shutdown();
            }
            MatchState.Reset();
            GameConstants.ResetTeamColors();
            UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.EntrySceneName);
        }

        /// <summary>맵 셋업 핸드오프 등, 인스펙터 대신 코드로 지도 크기를
        /// 지정할 때(예: MapData.MapPreset에서 온 크기).</summary>
        public void SetMapSizeMm(Vector2 sizeMm)
        {
            mapSizeMm = sizeMm;
            UpdateMapLayout();
        }

        public Base SpawnBase(Vector2 sizeMm, Color fillColor, string unitName, string team, Vector2 desiredCenter, bool isDisplacement = false)
        {
            var unit = new Unit { UnitName = unitName, Team = team };
            var piece = CreatePieceObject(unit, sizeMm, fillColor, isDisplacement);
            unit.Models.Add(piece);
            piece.Center = ResolvePosition(piece, desiredCenter, false);
            piece.Refresh();
            return piece;
        }

        /// <summary>테스트용 — 모델 여러 개가 같은 유닛을 이루는 상태로 스폰한다
        /// (리딩+팔로워 워크플로우 확인용). 리딩 모델을 돌려준다.</summary>
        public Base SpawnUnit(Vector2 sizeMm, Color fillColor, string unitName, string team, int modelCount, Vector2 centerMm, float moveInch, float coherencyInch)
        {
            var unit = new Unit { UnitName = unitName, Team = team, MoveInch = moveInch, CoherencyInch = coherencyInch };
            Base leading = null;
            for (int i = 0; i < Mathf.Max(modelCount, 1); i++)
            {
                var piece = CreatePieceObject(unit, sizeMm, fillColor, false);
                unit.Models.Add(piece);
                var desired = centerMm + new Vector2(i * (sizeMm.x + 2f), 0f);
                piece.Center = ResolvePosition(piece, desired, false);
                piece.Refresh();
                if (i == 0)
                {
                    leading = piece;
                }
            }
            return leading;
        }

        /// <summary>예비대 목록에 정의를 하나 등록한다(테스트/로스터 임포트용).</summary>
        public void AddPendingUnit(PendingUnitDef def)
        {
            AddPendingUnitDef(def);
        }

        private Base CreatePieceObject(Unit unit, Vector2 sizeMm, Color fillColor, bool isDisplacement)
        {
            var go = new GameObject($"Base_{unit.UnitName}", typeof(RectTransform));
            go.transform.SetParent(baseLayer, false);
            var piece = go.AddComponent<Base>();
            piece.Unit = unit;
            piece.SizeMm = sizeMm;
            piece.FillColor = fillColor;
            piece.IsDisplacement = isDisplacement;
            piece.MenuRequested += OnMenuRequested;
            piece.DragRequested += OnDragRequested;
            _pieces.Add(piece);
            return piece;
        }

        /// <summary>매 프레임 실제 디스패치는 BoardInputController.RunFrame으로
        /// 옮겼다(2026-09-03, 리팩토링 Phase 5) — 아래 internal 프로퍼티/메서드
        /// 들이 그 창구다. 드래그 계산 자체(ResolvePosition 등 조각/보드
        /// 상태에 깊이 의존하는 부분)는 여전히 BoardManager 안에 남아있다 —
        /// BoardInputController는 상태를 안 갖고 "이번 프레임에 어느 분기를
        /// 탈지"만 판단해 이 메서드들을 부른다.</summary>
        private void Update()
        {
            BoardInputController.RunFrame(this);
        }

        internal bool IsUnitMoveActive => _unitMoveActive;
        internal bool IsDraggingPiece => _draggingPiece != null;
        internal bool IsClickCandidatePending => _clickCandidatePiece != null;
        internal bool IsDraggingFollower => _draggingFollower != null;
        internal bool HasDisplacementQueue => _displacementQueue.Count > 0;
        internal bool HasPendingDeployment => _pendingDeploymentDef != null;
        internal bool HasPendingRosterToken => _pendingRosterTokenDef != null;
        internal bool IsDraggingMarker => _draggingMarker != null;
        internal bool IsPlacingMarker => _placingMarkerKind != "";

        /// <summary>유닛 이동(리딩 모델)/팔로워 배치 중에만 적 인게이지 경고를
        /// 켠다 — 일반 모델 드래그(유닛 이동 워크플로 밖)에는 적용하지
        /// 않는다(사용자 요청 범위 그대로).</summary>
        internal void UpdateEngageWarningForCurrentDrag()
        {
            Base engageCheckPiece = null;
            if (_unitMoveActive && _unitMovePhase == "leading" && _draggingPiece == _unitMoveLeading)
            {
                engageCheckPiece = _draggingPiece;
            }
            else if (_draggingFollower != null)
            {
                engageCheckPiece = _draggingFollower;
            }
            UpdateEngageWarning(engageCheckPiece);
        }

        /// <summary>일반 드래그(유닛 이동 중이 아닐 때)에서 클릭과 드래그를
        /// 구분한다 — OnDragRequested가 마우스를 누른 순간 여기로 넘겨준
        /// "후보"를, ClickVsDragThresholdMm을 넘게 움직였을 때만 진짜
        /// 드래그(되돌리기 트랜잭션 시작 + _draggingPiece 승격)로 바꾼다.
        /// 그 전에 마우스를 떼면 순수 클릭으로 끝난다 — 상세 패널 선택은
        /// OnDragRequested 맨 앞에서 이미 처리됐으므로 여기선 아무것도 더
        /// 안 해도 된다(위치 재계산도, 되돌리기 기록도, 네트워크 브로드캐스트도
        /// 전혀 없음).</summary>
        internal void HandleClickCandidateInput()
        {
            if (Input.GetMouseButtonUp(0))
            {
                _clickCandidatePiece = null;
                return;
            }
            if (!TryGetLocalMouse(out var local))
            {
                return;
            }
            if (Vector2.Distance(local, _clickCandidateMouseDownLocal) < ClickVsDragThresholdMm)
            {
                return;
            }
            var piece = _clickCandidatePiece;
            _clickCandidatePiece = null;
            BeginUndoTransaction($"{DescribeUnit(piece.Unit)} 이동", piece.Unit?.Team);
            _draggingPiece = piece;
            _dragOffset = piece.Center - local;
            piece.transform.SetAsLastSibling();
        }

        internal void HandlePieceDragInput()
        {
            if (Input.GetMouseButtonUp(0))
            {
                EndPieceDrag();
                return;
            }
            if (TryGetLocalMouse(out var local))
            {
                var desired = local + _dragOffset;
                if (_unitMoveActive && _unitMovePhase == "leading" && _draggingPiece == _unitMoveLeading)
                {
                    desired = ResolveLeadingDragCenter(desired);
                }
                // 모델 메뉴얼 이동/리딩 모델 이동: 변위 베이스는 통과할 수 있다.
                _draggingPiece.Center = ResolvePosition(_draggingPiece, desired, true);
                UpdateUnitMoveDistanceLabel();
            }
        }

        internal void HandleFollowerDragInput()
        {
            if (Input.GetMouseButtonUp(0))
            {
                _draggingFollower = null;
                // 팔로워를 하나 옮길 때마다 공유한다(마커 드래그와 같은
                // 패턴 — 끝난 시점 위치만, 실시간 아님). FinishLeadingMove의
                // 첫 공유와 이유는 같다.
                BroadcastUnitIfNetworked(_unitMoveUnit);
                return;
            }
            if (TryGetLocalMouse(out var local))
            {
                var desired = local + _dragOffset;
                _draggingFollower.Center = ResolveFollowerPosition(_draggingFollower, desired);
                UpdateUnitMoveWarning();
            }
        }

        /// <summary>이번 프레임 클릭이 그 어떤 특수 처리도 안 탔을 때(베이스를
        /// 클릭했다면 IsDraggingPiece 분기가 이미 처리했을 것이므로, 남은
        /// 가능성은 빈 땅이나 마커/미션 목표물처럼 유닛이 아닌 다른
        /// raycastable) 부른다.</summary>
        internal void HandleEmptyClickFallthrough()
        {
            // 리플레이 중엔 여기까지 오면 안 된다 — 라캐스터를 꺼서 막는 건
            // "다른 조각/버튼 위" 클릭만이고, 이 메서드 자체는 Input.*를
            // 직접 폴링하므로 라캐스터랑 무관하게 매 프레임 계속 불린다.
            // 안 막으면 리플레이 중 우클릭할 때마다 이모트 피커가 뜨는데,
            // 그 피커도 같은(꺼진) 캔버스 위라 닫을 수도 없는 채로 떠버린다.
            if (_replayMode)
            {
                return;
            }

            // 사용자 지정: "빈 땅을 클릭하거나... 꺼주면 돼" — 어느 team인지
            // 특정할 수 없는 제스처이므로 두 team의 좌클릭 선택을 한꺼번에
            // 해제한다.
            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                _selectedUnitForDetailByTeam.Clear();
            }

            // 같은 논리로 빈 땅 우클릭 — 유닛/마커/미션 목표물 위였다면 그
            // 자신의 OnPointerDown이 이미 처리했을 것이므로(다이얼 메뉴,
            // 마커 삭제, 미션 마커 색 순환 등), 여기까지 내려온 우클릭은
            // 진짜 빈 땅이다. 이모트 선택 팝업을 연다(사용자 요청,
            // BoardManager.Emote.cs 참고).
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi() && TryGetLocalMouse(out var emoteBoardPoint))
            {
                OpenEmotePicker(emoteBoardPoint);
            }
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private bool TryGetLocalMouse(out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(baseLayer, Input.mousePosition, null, out local);
        }

        private void OnDragRequested(Base piece)
        {
            // 좌클릭 = 유닛 상세 패널 선택(사용자 지정) — 어떤 분기로 이어지든
            // (일반 드래그/리딩·팔로워 이동 드래그) 상관없이 좌클릭 자체가
            // "이 유닛을 보여달라"는 뜻이므로 이 함수의 맨 앞에서 무조건 처리한다.
            // 토큰은 대상 밖(우클릭 트리거와 동일한 정책). team별로 따로
            // 저장하므로 다른 team의 기존 선택은 안 건드린다.
            if (piece.Unit != null && !piece.Unit.IsToken)
            {
                _selectedUnitForDetailByTeam[piece.Unit.Team] = piece.Unit;
            }

            if (_pendingDeploymentDef != null)
            {
                // 배치할 유닛을 놓을 자리를 고르는 중엔 기존 베이스를 잡아 끌 수 없다.
                return;
            }

            if (IsPlacingMarker)
            {
                // 마커를 배치하는 중엔 유닛을 클릭해도 그 유닛을 옮기지 않는다
                // (사용자 보고, 2026-09-06 — "BT를 배치할 때 유닛을 클릭하면
                // 유닛 이동으로 처리되며 배치가 안 됨"). 위 _pendingDeploymentDef/
                // 아래 HasDisplacementQueue 가드와 같은 이유 — 여기서 드래그를
                // 시작해버리면 다음 프레임부터 BoardInputController.RunFrame의
                // IsDraggingPiece 분기가 IsPlacingMarker보다 먼저 걸려 배치
                // 자체가 영영 처리되지 않는다.
                return;
            }

            if (HasDisplacementQueue)
            {
                // 변위 배치 위치를 고르는 중엔 새 드래그를 시작하지 않는다
                // (사용자 요청, 2026-09-03 버그 보고 — "변위 위치를 정할 때
                // 다른 유닛/토큰을 클릭하면 그 모델을 이동시키려 해서 변위
                // 배치가 안 됨"). 막지 않으면 이 클릭이 여기서 새 드래그를
                // 먼저 시작해버리고, 다음 프레임부터 BoardInputController.
                // RunFrame의 IsDraggingPiece 분기가 HasDisplacementQueue보다
                // 먼저 걸려 변위 배치 자체가 영영 처리되지 않는다.
                return;
            }

            if (_unitMoveActive)
            {
                if (_unitMovePhase == "leading" && piece == _unitMoveLeading)
                {
                    _draggingPiece = piece;
                    TryGetLocalMouse(out var local);
                    _dragOffset = piece.Center - local;
                }
                else if (_unitMovePhase == "followers" && piece != _unitMoveLeading && piece.Unit == _unitMoveUnit)
                {
                    _draggingFollower = piece;
                    TryGetLocalMouse(out var local);
                    _dragOffset = piece.Center - local;
                }
                return;
            }

            // 유닛 이동 중이 아닌 일반 드래그 — 클릭인지 드래그인지 아직
            // 모르므로 바로 드래그를 시작하지 않고 "클릭 후보"로만 기록한다.
            // ClickVsDragThresholdMm을 넘는 움직임이 실제로 확인되면
            // HandleClickCandidateInput이 그때 되돌리기 트랜잭션을 열고
            // _draggingPiece로 승격한다.
            TryGetLocalMouse(out var candidateLocal);
            _clickCandidatePiece = piece;
            _clickCandidateMouseDownLocal = candidateLocal;
        }

        private void EndPieceDrag()
        {
            bool wasLeadingDrag = _unitMoveActive && _unitMovePhase == "leading" && _draggingPiece == _unitMoveLeading;
            var movedPiece = _draggingPiece;
            _draggingPiece = null;

            var overlapping = FindOverlappingDisplacementBases(movedPiece);
            if (overlapping.Count > 0)
            {
                StartDisplacementPlacement(movedPiece, overlapping, wasLeadingDrag);
            }
            else if (wasLeadingDrag)
            {
                // 배치(신규 유닛을 처음 놓는 것)는 예전처럼 한 번의 드래그로
                // 바로 끝난다 — 웨이포인트 경로는 이미 배치된 유닛을 다시
                // 옮길 때만 적용된다(사용자 요청 범위).
                if (_unitMoveIsDeployment)
                {
                    FinishLeadingMove();
                }
                else
                {
                    CommitLeadingWaypoint();
                }
            }
            else
            {
                // 변위 처리도 없었고 유닛 이동도 아닌 일반 드래그 완료(로스터
                // 토큰 최초 배치 포함 — BeginRosterTokenPlacement도 이 경로로
                // 커밋된다) — 여기서 바로 되돌리기 트랜잭션을 커밋한다.
                CommitUndoTransaction();
                if (movedPiece != null)
                {
                    BroadcastUnitIfNetworked(movedPiece.Unit);
                }
            }
        }

        /// <summary>다른 베이스들과 절대 안 겹치게, 그리고 지도 경계 안으로 위치를
        /// 보정한다. allowDisplacementOverlap이면 변위 베이스는 장애물로 안 친다
        /// (모델 메뉴얼 이동/리딩 모델 이동/배치 중일 때).</summary>
        public Vector2 ResolvePosition(Base piece, Vector2 desiredCenter, bool allowDisplacementOverlap)
        {
            var others = new List<(IEllipseBody body, Vector2 center)>();
            foreach (var other in _pieces)
            {
                if (other == piece)
                {
                    continue;
                }
                others.Add((other, other.Center));
            }
            return EllipseMath.ResolvePosition(piece, desiredCenter, others, mapSizeMm, allowDisplacementOverlap);
        }

        private void OnMenuRequested(Base piece, Vector2 screenPos)
        {
            if (_pendingDeploymentDef != null)
            {
                // 배치 중엔 기존 베이스의 다이얼 메뉴 대신 빈 곳 우클릭이 배치 취소로 쓰인다.
                return;
            }
            if (_unitMoveActive)
            {
                // 유닛 이동 중엔 다이얼 메뉴 대신 우클릭이 취소로 쓰인다(Update()에서 처리).
                return;
            }
            _menuTarget = piece;
            // 우클릭으로 다이얼 메뉴가 실제로 열리는 순간, 이 유닛과 같은
            // team에서 이전에 좌클릭으로 선택해뒀던 것은 완전히 꺼진다(사용자
            // 지정: "A 보던 것은 끄고 B를 보고 있는 것" — A/B가 같은
            // 유닛이든 다른 유닛이든 마찬가지). team별로 독립이므로 다른
            // team의 좌클릭 선택은 안 건드린다 — 이렇게 해야 메뉴가 닫힌 뒤
            // 좌클릭 선택으로 되돌아가는 일 없이, 우클릭 스펙 그대로("메뉴
            // 선택하면 꺼짐") 완전히 사라진다.
            if (piece.Unit != null)
            {
                _selectedUnitForDetailByTeam.Remove(piece.Unit.Team);
            }
            // 마우스 우클릭 지점이 아니라, 우클릭한 모델 자체를 다이얼 중심으로
            // 삼는다 — 패닝/줌 중인 mapArea 아래에 있는 조각의 화면 좌표를
            // 직접 구한다(Screen Space Overlay라 카메라 인자는 null).
            _menuScreenPos = RectTransformUtility.WorldToScreenPoint(null, piece.transform.position);
            var isTokenUnit = piece.Unit != null && piece.Unit.IsToken;
            var canMove = piece.Unit == null || piece.Unit.CanMove;

            var options = new List<RadialMenuOption>();
            if (!isTokenUnit)
            {
                options.Add(new RadialMenuOption("데미지 기록", "damage"));
            }
            options.Add(new RadialMenuOption("모델 제거", "remove"));
            if (!isTokenUnit)
            {
                options.Add(new RadialMenuOption("리스폰", "duplicate"));
            }
            if (!isTokenUnit && canMove)
            {
                options.Add(new RadialMenuOption("이동", "start_unit_move"));
            }
            if (!isTokenUnit)
            {
                options.Add(new RadialMenuOption("리저브 복귀", "revert_unit"));
            }
            options.Add(new RadialMenuOption("메모 작성", "memo"));
            options.Add(new RadialMenuOption("범위 표시", "range_display"));

            radialMenu.Open(options, _menuScreenPos);
        }

        private void OnActionChosen(string action)
        {
            if (_menuTarget == null)
            {
                return;
            }

            if (action.StartsWith("delete_range_"))
            {
                int idx = int.Parse(action.Substring("delete_range_".Length));
                RequestDeleteRange(_rangeDeleteTargetUnit, idx);
                return;
            }

            switch (action)
            {
                case "damage":
                    damageDialog.Open("데미지 입력", _menuTarget.Damage.ToString());
                    break;
                case "remove":
                    RemoveBase(_menuTarget);
                    break;
                case "duplicate":
                    DuplicateBase(_menuTarget);
                    break;
                case "memo":
                    memoDialog.Open("모델 메모", _menuTarget.Memo);
                    break;
                case "start_unit_move":
                    StartUnitMove(_menuTarget);
                    break;
                case "revert_unit":
                    RevertUnit(_menuTarget);
                    break;
                case "range_display":
                    radialMenu.Open(new List<RadialMenuOption>
                    {
                        new RadialMenuOption("추가", "range_add"),
                        new RadialMenuOption("제거", "range_remove"),
                    }, _menuScreenPos);
                    break;
                case "range_add":
                    _rangeTargetUnit = _menuTarget.Unit;
                    rangeInputDialog.Open(0f);
                    break;
                case "range_remove":
                    HandleDeleteRangeRequest(_menuTarget);
                    break;
            }
        }

        private void OnDamageConfirmed(string value)
        {
            if (_menuTarget == null)
            {
                return;
            }
            BeginUndoTransaction($"{DescribeUnit(_menuTarget.Unit)} 데미지 변경", _menuTarget.Unit?.Team);
            var unit = _menuTarget.Unit;
            if (int.TryParse(value, out int dmg))
            {
                _menuTarget.Damage = Mathf.Max(dmg, 0);
                _menuTarget.Refresh();
            }
            _menuTarget = null;
            CommitUndoTransaction();
            // 모델 개수는 안 바뀌므로 유닛 이동/배치와 같은 방송 하나로
            // 충분하다 — 받는 쪽은 이미 있는 UpdateUnitModelsFromTree의
            // damage 필드 반영을 그대로 탄다(새 RPC 불필요).
            BroadcastUnitIfNetworked(unit);
        }

        private void OnMemoConfirmed(string value)
        {
            if (_menuTarget == null)
            {
                return;
            }
            BeginUndoTransaction($"{DescribeUnit(_menuTarget.Unit)} 메모 변경", _menuTarget.Unit?.Team);
            var unit = _menuTarget.Unit;
            _menuTarget.Memo = value.Trim();
            _menuTarget = null;
            CommitUndoTransaction();
            BroadcastUnitIfNetworked(unit);
        }

        private void RemoveBase(Base piece)
        {
            BeginUndoTransaction($"{DescribeUnit(piece.Unit)} 모델 제거", piece.Unit?.Team);
            var unit = piece.Unit;
            unit?.Models.Remove(piece);
            _pieces.Remove(piece);
            Destroy(piece.gameObject);
            _menuTarget = null;
            CommitUndoTransaction();
            // 모델 개수가 줄어드는 경우 — 받는 쪽 UpdateUnitModelsFromTree는
            // 개수가 안 맞으면 통째로 다시 짓는 방식으로 방어적으로 처리한다
            // (부드러운 이동 트윈은 없지만 정확하다 — 유닛 이동만큼 자주
            // 일어나는 조작이 아니라 이 정도로 충분).
            BroadcastUnitIfNetworked(unit);
        }

        private void DuplicateBase(Base piece)
        {
            BeginUndoTransaction($"{DescribeUnit(piece.Unit)} 모델 복제", piece.Unit?.Team);
            var unit = piece.Unit;
            var newPiece = CreatePieceObject(unit, piece.SizeMm, piece.FillColor, piece.IsDisplacement);
            unit?.Models.Add(newPiece);

            var desired = piece.Center + new Vector2(piece.BoundingRadius + newPiece.BoundingRadius + DuplicateGapMm, 0f);
            newPiece.Center = ResolvePosition(newPiece, desired, false);
            newPiece.Refresh();
            _menuTarget = null;
            CommitUndoTransaction();
            // 모델 개수가 느는 경우 — RemoveBase와 같은 이유로 그냥 유닛
            // 전체를 다시 방송한다(개수 불일치 시 통째로 다시 짓는 방어적
            // 경로를 그대로 탄다).
            BroadcastUnitIfNetworked(unit);
        }

        /// <summary>유닛을 배치 전 상태로 되돌린다 — 남은 모델 개수와 각 모델의
        /// 데미지는 그대로 유지한 채, 다시 배치할 수 있도록 예비대 목록으로
        /// 돌려보낸다.</summary>
        private void RevertUnit(Base piece)
        {
            if (piece == null || piece.Unit == null)
            {
                return;
            }
            BeginUndoTransaction($"{DescribeUnit(piece.Unit)} 리저브 복귀", piece.Unit?.Team);
            var unit = piece.Unit;

            var damages = new List<int>();
            foreach (var model in unit.Models)
            {
                damages.Add(model.Damage);
            }

            var ranges = _unitRanges.TryGetValue(unit, out var existingRanges)
                    ? new List<RangeSpec>(existingRanges)
                    : new List<RangeSpec>();

            var def = new PendingUnitDef
            {
                Name = unit.UnitName,
                Team = unit.Team,
                ModelCount = unit.Models.Count,
                SizeMm = piece.SizeMm,
                FillColor = piece.FillColor,
                MoveInch = unit.MoveInch,
                CoherencyInch = unit.CoherencyInch,
                CanMove = unit.CanMove,
                IsDisplacement = piece.IsDisplacement,
                SupplyTiers = new List<SupplyTier>(unit.SupplyTiers),
                Damages = damages,
                Ranges = ranges,
                SupplyOverride = unit.SupplyOverride,
                Detail = unit.Detail,
            };

            foreach (var model in unit.Models.ToArray())
            {
                if (_draggingPiece == model)
                {
                    _draggingPiece = null;
                }
                if (_clickCandidatePiece == model)
                {
                    _clickCandidatePiece = null;
                }
                _pieces.Remove(model);
                Destroy(model.gameObject);
            }
            unit.Models.Clear();
            // 이 유닛이 배치/이동 중 한 번이라도 방송된 적 있으면(NetworkUnitId
            // 배정됨) 보드에서 사라졌다고 상대에게도 알린다 — 안 그러면 상대
            // 화면엔 이 유닛이 계속 남아있는데 예비대에도 새로 나타나는
            // 어긋난 상태가 된다.
            BroadcastDeleteUnitIfNetworked(unit);

            // 되돌려진 유닛은 재배치 시 def.Ranges로 다시 등록되므로, 이 (곧
            // 버려질) Unit 객체에 대한 항목은 지운다 — 안 지우면 _unitRanges가
            // 참조를 계속 들고 있어 정리되지 않는다.
            _unitRanges.Remove(unit);
            if (unit == _hoveredUnit)
            {
                _hoveredUnit = null;
            }
            if (_selectedUnitForDetailByTeam.TryGetValue(unit.Team, out var selectedForTeam) && selectedForTeam == unit)
            {
                _selectedUnitForDetailByTeam.Remove(unit.Team);
            }
            RefreshRangeOverlays();

            AddPendingUnitDef(def);
            _menuTarget = null;
            CommitUndoTransaction();
        }

    }
}
