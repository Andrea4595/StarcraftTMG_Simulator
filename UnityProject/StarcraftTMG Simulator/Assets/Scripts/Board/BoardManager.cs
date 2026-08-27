using System.Collections.Generic;
using TMPro;
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
        [SerializeField] private Vector2 mapSizeMm = new Vector2(36f * GameConstants.MmPerInch, 36f * GameConstants.MmPerInch);

        private const float DuplicateGapMm = 4f;
        private const float FollowerRingFraction = 0.7f;
        private const float CoherencyEpsilonMm = 0.5f; // 경계에 스냅됐을 때 부동소수점 오차로 오탐지되는 것 방지
        private const float FollowerSnapThresholdMm = 6f;
        private const float FollowerOutwardSnapThresholdMm = 40f; // 경계 밖으로는 훨씬 강하게 붙잡아둔다

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

        private readonly List<Base> _pieces = new List<Base>();
        private Base _menuTarget;

        private Base _draggingPiece;
        private Base _draggingFollower;
        private Vector2 _dragOffset;

        private bool _unitMoveActive;
        private Base _unitMoveLeading;
        private Unit _unitMoveUnit;
        private string _unitMovePhase = ""; // "leading" / "followers"
        private Vector2 _unitMoveStartPoint;
        private readonly Dictionary<Base, Vector2> _unitMoveOriginalPositions = new Dictionary<Base, Vector2>();

        private RectTransform _unitMovePanel;
        private TextMeshProUGUI _unitMoveWarningLabel;

        // ── 예비대 배치 ──────────────────────────────────────────────────
        // 팀별로 별도 패널(A는 왼쪽, B는 오른쪽)에 나눠 보여준다.
        private readonly List<PendingUnitDef> _pendingUnits = new List<PendingUnitDef>();
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
        private readonly List<PendingTokenDef> _pendingRosterTokens = new List<PendingTokenDef>();
        private readonly Dictionary<string, RectTransform> _rosterTokenListContainers = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, LayoutElement> _rosterTokenListLayoutElements = new Dictionary<string, LayoutElement>();
        // 라벨+목록을 함께 켜고 끄기 위한 래퍼 — 토큰이 하나도 없는 팀은
        // "토큰" 섹션 자체를 접어둔다(RefreshRosterTokenList가 관리).
        private readonly Dictionary<string, GameObject> _tokenSectionRoots = new Dictionary<string, GameObject>();
        // "팀|이름" -> Unit. 같은 토큰은 이미 배치된 것과 한 유닛으로 합쳐진다.
        private readonly Dictionary<string, Unit> _rosterTokenUnits = new Dictionary<string, Unit>();
        private PendingTokenDef _pendingRosterTokenDef;

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
        private const float MarkerBarHeight = 44f;
        private static readonly (string Kind, string Label)[] MarkerBarEntries =
        {
            ("activation", "활성화 마커"),
            ("capture", "점령 마커"),
            ("movement", "이동 마커"),
            ("assault", "돌격 마커"),
            ("combat", "전투 마커"),
            ("buff", "버프 마커"),
            ("debuff", "디버프 마커"),
        };

        private Texture2D _activationTextureMovement;
        private Texture2D _activationTextureAssault;
        private Texture2D _activationTextureDone;
        private Texture2D _captureTexture;
        private Dictionary<string, Texture2D> _iconTextures;

        private string _placingMarkerKind = ""; // 비었으면 없음. MarkerBarEntries의 Kind 값 중 하나
        private MarkerBase _draggingMarker;
        private Vector2 _markerDragOffset;
        private MarkerBase _markerPlacementPreview;

        // ── 되돌리기(ctrl+z) / 다시 실행(ctrl+shift+z) ───────────────────
        // "트랜잭션 전 상태를 통째로 스냅샷 → 스택에 push" 방식. Godot판과
        // 동일한 설계(GameBoard.gd 맨 아래 섹션) — 커밋되는 모든 스냅샷은
        // "그 트랜잭션이 시작되기 전" 상태를 담으므로, undo는 스택에서
        // 하나 꺼내 그 상태로 복원하면 된다. 드래그처럼 여러 입력 이벤트에
        // 걸친 동작은 시작 지점에서 BeginUndoTransaction()을, (변위 베이스를
        // 밀어내는 후속 배치까지 포함해서) 완료 지점에서 CommitUndoTransaction()을
        // 부른다.
        private readonly List<BoardSnapshot> _undoStack = new List<BoardSnapshot>();
        private readonly List<BoardSnapshot> _redoStack = new List<BoardSnapshot>();
        private bool _undoPendingActive;
        private BoardSnapshot _undoPendingSnapshot;

        private void Start()
        {
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
            BuildPendingPanel();
            if (markerLayer != null)
            {
                BuildMarkerBar();
            }
            UpdateMapLayout();
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

        /// <summary>마커(활성화/점령/아이콘) 배치용 텍스처와 레이어를 별도로
        /// 주입한다 — Configure()가 이미 파라미터가 많아서 마커 쪽은 분리했다.
        /// 마커바 UI는 다음 Start()에서 지어진다.</summary>
        public void ConfigureMarkers(RectTransform markerLayerRef,
                Texture2D activationMovement, Texture2D activationAssault, Texture2D activationDone,
                Texture2D captureTexture, Dictionary<string, Texture2D> iconTextures)
        {
            markerLayer = markerLayerRef;
            _activationTextureMovement = activationMovement;
            _activationTextureAssault = activationAssault;
            _activationTextureDone = activationDone;
            _captureTexture = captureTexture;
            _iconTextures = iconTextures;
        }

        /// <summary>로스터 JSON 임포트용 파일 탐색기 다이얼로그를 주입한다.</summary>
        public void ConfigureRoster(RosterFileDialog rosterFileDialogRef)
        {
            rosterFileDialog = rosterFileDialogRef;
        }

        /// <summary>미션 설정 핸드오프 등, 인스펙터 대신 코드로 지도 크기를
        /// 지정할 때(예: MissionData.MapPreset에서 온 크기).</summary>
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
            _pendingUnits.Add(def);
            RefreshPendingList();
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

        private void Update()
        {
            HandlePanAndZoom();
            HandleMeasureInput();
            HandleUndoRedoInput();
            UpdateHoveredUnit();

            if (_unitMoveActive && Input.GetMouseButtonDown(1))
            {
                CancelUnitMove();
                return;
            }

            if (_draggingPiece != null)
            {
                if (Input.GetMouseButtonUp(0))
                {
                    EndPieceDrag();
                    return;
                }
                if (TryGetLocalMouse(out var local))
                {
                    var desired = local + _dragOffset;
                    // 모델 메뉴얼 이동/리딩 모델 이동: 변위 베이스는 통과할 수 있다.
                    _draggingPiece.Center = ResolvePosition(_draggingPiece, desired, true);
                    UpdateUnitMoveDistanceLabel();
                }
                return;
            }

            if (_draggingFollower != null)
            {
                if (Input.GetMouseButtonUp(0))
                {
                    _draggingFollower = null;
                    return;
                }
                if (TryGetLocalMouse(out var local))
                {
                    var desired = local + _dragOffset;
                    _draggingFollower.Center = ResolveFollowerPosition(_draggingFollower, desired, _unitMoveLeading.Center);
                    UpdateUnitMoveWarning();
                }
                return;
            }

            if (_displacementQueue.Count > 0)
            {
                HandleDisplacementPlacementInput();
                return;
            }

            if (_pendingDeploymentDef != null)
            {
                HandlePendingDeploymentInput();
                return;
            }

            if (_pendingRosterTokenDef != null)
            {
                HandlePendingRosterTokenInput();
                return;
            }

            if (_draggingMarker != null)
            {
                HandleMarkerDragInput();
                return;
            }

            if (_placingMarkerKind != "")
            {
                HandleMarkerPlacementInput();
                return;
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
            if (_pendingDeploymentDef != null)
            {
                // 배치할 유닛을 놓을 자리를 고르는 중엔 기존 베이스를 잡아 끌 수 없다.
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

            // 유닛 이동 중이 아닌 일반 드래그 — 여기서 되돌리기 트랜잭션을 열고,
            // 마우스를 뗄 때(EndPieceDrag) 실제로 뭔가 바뀌었으면 커밋한다.
            BeginUndoTransaction();
            _draggingPiece = piece;
            TryGetLocalMouse(out var localFree);
            _dragOffset = piece.Center - localFree;
            piece.transform.SetAsLastSibling();
        }

        private void EndPieceDrag()
        {
            bool finishedLeading = _unitMoveActive && _unitMovePhase == "leading" && _draggingPiece == _unitMoveLeading;
            var movedPiece = _draggingPiece;
            _draggingPiece = null;

            var overlapping = FindOverlappingDisplacementBases(movedPiece);
            if (overlapping.Count > 0)
            {
                StartDisplacementPlacement(movedPiece, overlapping, finishedLeading);
            }
            else if (finishedLeading)
            {
                FinishLeadingMove();
            }
            else
            {
                // 변위 처리도 없었고 유닛 이동도 아닌 일반 드래그 완료 — 여기서
                // 바로 되돌리기 트랜잭션을 커밋한다.
                CommitUndoTransaction();
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
                DeleteRangeAtIndex(_rangeDeleteTargetUnit, idx);
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
            BeginUndoTransaction();
            if (int.TryParse(value, out int dmg))
            {
                _menuTarget.Damage = Mathf.Max(dmg, 0);
                _menuTarget.Refresh();
            }
            _menuTarget = null;
            CommitUndoTransaction();
        }

        private void OnMemoConfirmed(string value)
        {
            if (_menuTarget == null)
            {
                return;
            }
            BeginUndoTransaction();
            _menuTarget.Memo = value.Trim();
            _menuTarget = null;
            CommitUndoTransaction();
        }

        private void RemoveBase(Base piece)
        {
            BeginUndoTransaction();
            piece.Unit?.Models.Remove(piece);
            _pieces.Remove(piece);
            Destroy(piece.gameObject);
            _menuTarget = null;
            CommitUndoTransaction();
        }

        private void DuplicateBase(Base piece)
        {
            BeginUndoTransaction();
            var unit = piece.Unit;
            var newPiece = CreatePieceObject(unit, piece.SizeMm, piece.FillColor, piece.IsDisplacement);
            unit?.Models.Add(newPiece);

            var desired = piece.Center + new Vector2(piece.BoundingRadius + newPiece.BoundingRadius + DuplicateGapMm, 0f);
            newPiece.Center = ResolvePosition(newPiece, desired, false);
            newPiece.Refresh();
            _menuTarget = null;
            CommitUndoTransaction();
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
            BeginUndoTransaction();
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
            };

            foreach (var model in unit.Models.ToArray())
            {
                if (_draggingPiece == model)
                {
                    _draggingPiece = null;
                }
                _pieces.Remove(model);
                Destroy(model.gameObject);
            }
            unit.Models.Clear();

            // 되돌려진 유닛은 재배치 시 def.Ranges로 다시 등록되므로, 이 (곧
            // 버려질) Unit 객체에 대한 항목은 지운다 — 안 지우면 _unitRanges가
            // 참조를 계속 들고 있어 정리되지 않는다.
            _unitRanges.Remove(unit);
            if (unit == _hoveredUnit)
            {
                _hoveredUnit = null;
            }
            RefreshRangeOverlays();

            _pendingUnits.Add(def);
            RefreshPendingList();
            _menuTarget = null;
            CommitUndoTransaction();
        }

    }
}
