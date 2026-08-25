using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 보드 위 베이스 조각들을 소유하고, 다이얼 메뉴 액션(데미지 기록/모델
    /// 제거/복제/이름 변경/메모/유닛 되돌리기)과 유닛 이동 워크플로우(리딩 모델
    /// 배치→팔로워 배치→완료/취소, 코헤런시 판정), 예비대 배치(배치 목록→리딩
    /// 모델 드래그→팔로워 자동 배치), 변위 베이스 재배치(밀어낸 자리 재지정)를
    /// 처리한다. Godot판 GameBoard.gd 포팅. 배치 밴드는 MissionData에 이
    /// 팀의 배치구역이 있으면 실제 구역을, 없으면(미션 설정을 안 거쳤으면)
    /// 지도 가장자리 폴백을 보여준다. 마우스를 올린 유닛의 메모를 모델
    /// 아래에 띄워준다(MemoOverlay).
    /// 아직 없는 것: 로스터 토큰, 범위 표시, 되돌리기(ctrl+z) — 전부 다음 단계.
    /// </summary>
    public class BoardManager : MonoBehaviour
    {
        [SerializeField] private RectTransform baseLayer;
        [SerializeField] private RadialMenu radialMenu;
        [SerializeField] private InputDialog damageDialog;
        [SerializeField] private InputDialog renameDialog;
        [SerializeField] private InputDialog memoDialog;
        [SerializeField] private GuidelineOverlay guideline;
        [SerializeField] private MemoOverlay memoOverlay;
        [SerializeField] private Vector2 mapSizeMm = new Vector2(36f * GameConstants.MmPerInch, 36f * GameConstants.MmPerInch);

        private const float DuplicateGapMm = 4f;
        private const float FollowerRingFraction = 0.7f;
        private const float CoherencyEpsilonMm = 0.5f; // 경계에 스냅됐을 때 부동소수점 오차로 오탐지되는 것 방지
        private const float FollowerSnapThresholdMm = 6f;
        private const float FollowerOutwardSnapThresholdMm = 40f; // 경계 밖으로는 훨씬 강하게 붙잡아둔다

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
        private readonly List<PendingUnitDef> _pendingUnits = new List<PendingUnitDef>();
        private RectTransform _pendingPanel;
        private PendingUnitDef _pendingDeploymentDef;
        private bool _unitMoveIsDeployment;
        private PendingUnitDef _deploymentDefSnapshot;
        private int _pendingFollowerCount;
        private Base _placementPreview;

        // ── 변위 베이스 재배치 ──────────────────────────────────────────
        private Base _displacementAnchor;
        private readonly List<Base> _displacementQueue = new List<Base>();
        private bool _displacementResumeLeadingFinish;

        // ── 메모 호버 표시 ───────────────────────────────────────────────
        private Unit _hoveredUnit;
        private readonly List<(string Text, Vector2 Pos)> _memoEntries = new List<(string, Vector2)>();

        private void Start()
        {
            // Awake가 아니라 Start에서 구독한다 — 코드로 씬을 구성할 때(부트스트랩
            // 등) Configure()가 AddComponent 직후 동기적으로 불리는데, 그게 Awake
            // 이후·Start 이전에 끝나므로 여기서는 항상 필드가 채워져 있다.
            radialMenu.ActionChosen += OnActionChosen;
            damageDialog.Confirmed += OnDamageConfirmed;
            renameDialog.Confirmed += OnRenameConfirmed;
            memoDialog.Confirmed += OnMemoConfirmed;
            BuildUnitMovePanel();
            BuildPendingPanel();
        }

        /// <summary>씬을 코드로 구성할 때(부트스트랩 등) 인스펙터 대신 쓰는 초기화.</summary>
        public void Configure(RectTransform baseLayerRef, RadialMenu radialMenuRef,
                InputDialog damageDialogRef, InputDialog renameDialogRef, InputDialog memoDialogRef,
                GuidelineOverlay guidelineRef, MemoOverlay memoOverlayRef)
        {
            baseLayer = baseLayerRef;
            radialMenu = radialMenuRef;
            damageDialog = damageDialogRef;
            renameDialog = renameDialogRef;
            memoDialog = memoDialogRef;
            guideline = guidelineRef;
            memoOverlay = memoOverlayRef;
        }

        /// <summary>미션 설정 핸드오프 등, 인스펙터 대신 코드로 지도 크기를
        /// 지정할 때(예: MissionData.MapPreset에서 온 크기).</summary>
        public void SetMapSizeMm(Vector2 sizeMm)
        {
            mapSizeMm = sizeMm;
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
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private bool TryGetLocalMouse(out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(baseLayer, Input.mousePosition, null, out local);
        }

        // ── 메모 호버 표시 ───────────────────────────────────────────────

        private void UpdateHoveredUnit()
        {
            Base hoveredBase = TryGetLocalMouse(out var mouseLocal) ? FindBaseAtPoint(mouseLocal) : null;
            _hoveredUnit = hoveredBase != null ? hoveredBase.Unit : null;

            _memoEntries.Clear();
            if (_hoveredUnit != null)
            {
                foreach (var model in _hoveredUnit.Models)
                {
                    if (model == null || string.IsNullOrEmpty(model.Memo))
                    {
                        continue;
                    }
                    // Godot판은 Y가 아래로 증가해 +값이 "아래"였다 — Unity는 Y가
                    // 위로 증가하므로 부호를 뒤집어야 같은 위치(모델 아래)에 뜬다.
                    _memoEntries.Add((model.Memo, model.Center + new Vector2(0f, -(model.BoundingRadius + 10f))));
                }
            }
            if (memoOverlay != null)
            {
                memoOverlay.SetEntries(_memoEntries);
            }
        }

        /// <summary>마우스 아래(맨 위에 그려진 것부터)의 베이스를 찾는다 —
        /// 회전된 타원 그대로 판정한다. Godot판 _find_base_at_point 포팅.</summary>
        private Base FindBaseAtPoint(Vector2 point)
        {
            for (int i = baseLayer.childCount - 1; i >= 0; i--)
            {
                var piece = baseLayer.GetChild(i).GetComponent<Base>();
                if (piece == null)
                {
                    continue;
                }
                float rot = -piece.RotationRadians;
                Vector2 offset = point - piece.Center;
                float cos = Mathf.Cos(rot);
                float sin = Mathf.Sin(rot);
                Vector2 local = new Vector2(offset.x * cos - offset.y * sin, offset.x * sin + offset.y * cos);
                float rx = piece.SizeMm.x / 2f;
                float ry = piece.SizeMm.y / 2f;
                if (rx < 0.0001f || ry < 0.0001f)
                {
                    continue;
                }
                if ((local.x * local.x) / (rx * rx) + (local.y * local.y) / (ry * ry) <= 1f)
                {
                    return piece;
                }
            }
            return null;
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
            var isTokenUnit = piece.Unit != null && piece.Unit.IsToken;
            var canMove = piece.Unit == null || piece.Unit.CanMove;

            var options = new List<RadialMenuOption>();
            if (!isTokenUnit)
            {
                options.Add(new RadialMenuOption("데미지 기록", "damage"));
            }
            options.Add(new RadialMenuOption("모델 제거", "remove"));
            options.Add(new RadialMenuOption("모델 복제", "duplicate"));
            if (!isTokenUnit && canMove)
            {
                options.Add(new RadialMenuOption("유닛 이동 시작", "start_unit_move"));
            }
            options.Add(new RadialMenuOption("유닛 이름 변경", "rename"));
            if (!isTokenUnit)
            {
                options.Add(new RadialMenuOption("유닛 되돌리기", "revert_unit"));
            }
            options.Add(new RadialMenuOption("메모 작성", "memo"));

            radialMenu.Open(options, screenPos);
        }

        private void OnActionChosen(string action)
        {
            if (_menuTarget == null)
            {
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
                case "rename":
                    renameDialog.Open("유닛 이름 변경", _menuTarget.Unit != null ? _menuTarget.Unit.UnitName : "");
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
            }
        }

        private void OnDamageConfirmed(string value)
        {
            if (_menuTarget == null)
            {
                return;
            }
            if (int.TryParse(value, out int dmg))
            {
                _menuTarget.Damage = Mathf.Max(dmg, 0);
                _menuTarget.Refresh();
            }
            _menuTarget = null;
        }

        private void OnRenameConfirmed(string value)
        {
            if (_menuTarget == null || _menuTarget.Unit == null || string.IsNullOrWhiteSpace(value))
            {
                _menuTarget = null;
                return;
            }
            var unit = _menuTarget.Unit;
            unit.UnitName = value.Trim();
            foreach (var model in unit.Models)
            {
                model.Refresh();
            }
            _menuTarget = null;
        }

        private void OnMemoConfirmed(string value)
        {
            if (_menuTarget == null)
            {
                return;
            }
            _menuTarget.Memo = value.Trim();
            _menuTarget = null;
        }

        private void RemoveBase(Base piece)
        {
            piece.Unit?.Models.Remove(piece);
            _pieces.Remove(piece);
            Destroy(piece.gameObject);
            _menuTarget = null;
        }

        private void DuplicateBase(Base piece)
        {
            var unit = piece.Unit;
            var newPiece = CreatePieceObject(unit, piece.SizeMm, piece.FillColor, piece.IsDisplacement);
            unit?.Models.Add(newPiece);

            var desired = piece.Center + new Vector2(piece.BoundingRadius + newPiece.BoundingRadius + DuplicateGapMm, 0f);
            newPiece.Center = ResolvePosition(newPiece, desired, false);
            newPiece.Refresh();
            _menuTarget = null;
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
            var unit = piece.Unit;

            var damages = new List<int>();
            foreach (var model in unit.Models)
            {
                damages.Add(model.Damage);
            }

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
                Damages = damages,
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

            _pendingUnits.Add(def);
            RefreshPendingList();
            _menuTarget = null;
        }

        // ── 유닛 이동 워크플로우 ────────────────────────────────────────

        public void StartUnitMove(Base leading)
        {
            if (leading == null || leading.Unit == null || _unitMoveActive)
            {
                return;
            }

            _unitMoveActive = true;
            _unitMoveLeading = leading;
            _unitMoveUnit = leading.Unit;
            _unitMovePhase = "leading";
            _unitMoveStartPoint = leading.Center;

            _unitMoveOriginalPositions.Clear();
            foreach (var model in _unitMoveUnit.Models)
            {
                _unitMoveOriginalPositions[model] = model.Center;
            }

            // 원은 "베이스 테두리로부터 이동거리만큼"을 나타내야 하므로 리딩 모델
            // 반지름만큼 더해서 그린다. 모델이 하나뿐인 유닛은 코헤런시만큼
            // 이동력이 늘어난다(팔로워를 코헤런시 안에 배치할 필요가 없으므로).
            guideline.CenterMm = _unitMoveStartPoint;
            guideline.RadiusMm = leading.BoundingRadius + EffectiveMoveInch(_unitMoveUnit) * GameConstants.MmPerInch;
            _menuTarget = null;
            UpdateUnitMoveDistanceLabel();
        }

        private float EffectiveMoveInch(Unit unit)
        {
            if (unit.Models.Count <= 1)
            {
                return unit.MoveInch + unit.CoherencyInch;
            }
            return unit.MoveInch;
        }

        private void UpdateUnitMoveDistanceLabel()
        {
            if (!(_unitMoveActive && _unitMovePhase == "leading"))
            {
                return;
            }
            float distMm = Vector2.Distance(_unitMoveLeading.Center, _unitMoveStartPoint);
            guideline.LabelText = $"{distMm / GameConstants.MmPerInch:F1}\"";
            guideline.LabelPos = _unitMoveLeading.Center + new Vector2(0f, _unitMoveLeading.BoundingRadius + 14f);
        }

        private void FinishLeadingMove()
        {
            _unitMovePhase = "followers";
            if (_unitMoveIsDeployment && _pendingFollowerCount > 0)
            {
                SpawnDeploymentFollowers();
            }
            AutoPlaceFollowers();
            UpdateUnitMoveGuideline();
            UpdateUnitMoveWarning();
            if (_unitMoveUnit.Models.Count <= 1)
            {
                CompleteUnitMove();
                return;
            }
            ShowUnitMovePanel(true);
        }

        /// <summary>배치의 리딩 모델이 실제로 놓인 뒤에야 나머지 모델을 만든다 —
        /// 그 전에 만들면 클릭 지점에 겹쳐서 충돌 해소를 방해하게 된다.
        /// "유닛 되돌리기"로 되돌아온 유닛이면 damages[0]은 리딩 모델에 이미
        /// 쓰였으므로, 팔로워는 그 뒤 순서대로 이어서 가져간다.</summary>
        private void SpawnDeploymentFollowers()
        {
            var damages = _deploymentDefSnapshot != null ? _deploymentDefSnapshot.Damages : null;
            for (int i = 0; i < _pendingFollowerCount; i++)
            {
                var follower = CreatePieceObject(_unitMoveUnit, _unitMoveLeading.SizeMm, _unitMoveLeading.FillColor, _unitMoveLeading.IsDisplacement);
                int damageIndex = i + 1;
                if (damages != null && damageIndex < damages.Count)
                {
                    follower.Damage = damages[damageIndex];
                }
                follower.Center = _unitMoveLeading.Center;
                follower.Refresh();
                _unitMoveUnit.Models.Add(follower);
            }
            _pendingFollowerCount = 0;
        }

        private void AutoPlaceFollowers()
        {
            var followers = new List<Base>();
            foreach (var model in _unitMoveUnit.Models)
            {
                if (model != _unitMoveLeading)
                {
                    followers.Add(model);
                }
            }
            if (followers.Count == 0)
            {
                return;
            }

            float boundary = CoherencyBoundaryRadiusMm();
            Vector2 leadingCenter = _unitMoveLeading.Center;
            for (int i = 0; i < followers.Count; i++)
            {
                var follower = followers[i];
                float ringRadius = (boundary - follower.BoundingRadius) * FollowerRingFraction;
                float angle = (Mathf.PI * 2f / followers.Count) * i - Mathf.PI / 2f;
                var desired = leadingCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
                follower.Center = ResolvePosition(follower, desired, false);
            }
        }

        private float CoherencyBoundaryRadiusMm()
        {
            return _unitMoveLeading.BoundingRadius + _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch;
        }

        private void UpdateUnitMoveGuideline()
        {
            float coherencyMm = _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch;
            var boundary = EllipseMath.EllipseOffsetPolygonAt(_unitMoveLeading.Center, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians, coherencyMm);
            var closed = new Vector2[boundary.Length + 1];
            boundary.CopyTo(closed, 0);
            closed[boundary.Length] = boundary[0];

            guideline.RadiusMm = 0f;
            guideline.BandPolylines = new List<Vector2[]> { closed };
            guideline.LabelText = "";
        }

        private void UpdateUnitMoveWarning()
        {
            bool anyOut = false;
            foreach (var model in _unitMoveUnit.Models)
            {
                if (model == _unitMoveLeading)
                {
                    continue;
                }
                if (!IsFollowerWithinCoherency(model))
                {
                    anyOut = true;
                    break;
                }
            }
            if (_unitMoveWarningLabel != null)
            {
                _unitMoveWarningLabel.gameObject.SetActive(anyOut);
            }
        }

        private bool IsFollowerWithinCoherency(Base follower)
        {
            float coherencyMm = _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch + CoherencyEpsilonMm;
            var boundary = EllipseMath.EllipseOffsetPolygonAt(_unitMoveLeading.Center, _unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians, coherencyMm);
            var followerPoly = EllipseMath.EllipsePolygonAt(follower.Center, follower.SizeMm, follower.RotationRadians);
            foreach (var p in followerPoly)
            {
                if (!EllipseMath.PointInConvexPolygon(p, boundary))
                {
                    return false;
                }
            }
            return true;
        }

        private Vector2 ResolveFollowerPosition(Base piece, Vector2 desiredCenter, Vector2 leadingCenter)
        {
            // 코헤런시 경계 근처로 드래그하면 스냅되도록 돕는다 — 리딩·팔로워가
            // 둘 다 원형일 때만(타원이 끼면 방향/회전에 따라 경계가 계속
            // 달라져서 스냅이 오히려 어긋나 보인다).
            bool useSnap = IsCircular(_unitMoveLeading.SizeMm) && IsCircular(piece.SizeMm);
            var pos = desiredCenter;
            for (int i = 0; i < EllipseMath.CollisionIterations; i++)
            {
                var before = pos;
                if (useSnap)
                {
                    float maxCenterDistance = DirectionalMaxFollowerDistance(piece, leadingCenter, pos);
                    pos = SnapToCoherencyBoundary(pos, leadingCenter, maxCenterDistance);
                }
                pos = ResolvePosition(piece, pos, false);
                if (Vector2.Distance(pos, before) < 0.01f)
                {
                    break;
                }
            }
            return pos;
        }

        private static bool IsCircular(Vector2 sizeMm)
        {
            return Mathf.Approximately(sizeMm.x, sizeMm.y);
        }

        /// <summary>이 방향으로 팔로워를 얼마나 멀리 놓을 수 있는지 — "팔로워
        /// 베이스 전체가 리딩 테두리로부터 코헤런시 이내"라는 규칙을 그대로
        /// 따른다: 팔로워의 바깥쪽 끝(centerDistance + followerEdge)이 리딩의
        /// 코헤런시 경계(leadingEdge + coherencyMm)를 넘지 않아야 하므로
        /// centerDistance = leadingEdge + coherencyMm - followerEdge. (두 베이스의
        /// 마주보는 면 사이 간격이 코헤런시가 되는 지점이 아니라, 노란 경계선
        /// 자체에 팔로워의 먼 쪽 끝이 닿는 지점 — 더하면 항상 그 경계 바깥으로
        /// 스냅된다.)</summary>
        private float DirectionalMaxFollowerDistance(Base follower, Vector2 leadingCenter, Vector2 atPoint)
        {
            var direction = atPoint - leadingCenter;
            direction = direction.sqrMagnitude < 0.0001f ? Vector2.right : direction.normalized;
            float coherencyMm = _unitMoveUnit.CoherencyInch * GameConstants.MmPerInch;
            float leadingEdge = EllipseMath.EllipseRadiusInDirection(_unitMoveLeading.SizeMm, _unitMoveLeading.RotationRadians, direction);
            float followerEdge = EllipseMath.EllipseRadiusInDirection(follower.SizeMm, follower.RotationRadians, direction);
            return leadingEdge + coherencyMm - followerEdge;
        }

        private static Vector2 SnapToCoherencyBoundary(Vector2 pos, Vector2 leadingCenter, float maxCenterDistance)
        {
            var offset = pos - leadingCenter;
            float dist = offset.magnitude;
            if (dist > 0.01f)
            {
                if (dist >= maxCenterDistance && dist - maxCenterDistance <= FollowerOutwardSnapThresholdMm)
                {
                    pos = leadingCenter + offset.normalized * maxCenterDistance;
                }
                else if (dist < maxCenterDistance && maxCenterDistance - dist <= FollowerSnapThresholdMm)
                {
                    pos = leadingCenter + offset.normalized * maxCenterDistance;
                }
            }
            return pos;
        }

        private void OnUnitMoveCompletePressed()
        {
            if (!_unitMoveActive || _unitMovePhase != "followers")
            {
                return;
            }
            CompleteUnitMove();
        }

        private void CompleteUnitMove()
        {
            var casualties = new List<Base>();
            foreach (var model in _unitMoveUnit.Models)
            {
                if (model == _unitMoveLeading)
                {
                    continue;
                }
                if (!IsFollowerWithinCoherency(model))
                {
                    casualties.Add(model);
                }
            }
            foreach (var casualty in casualties)
            {
                _unitMoveUnit.Models.Remove(casualty);
                _pieces.Remove(casualty);
                Destroy(casualty.gameObject);
            }
            EndUnitMove();
        }

        private void CancelUnitMove()
        {
            if (_unitMoveIsDeployment)
            {
                // 배치 중 취소: 아직 게임에 존재한 적 없는 유닛이므로 되돌릴 위치가
                // 없다. 만든 모델을 전부 지우고, 정의를 예비대 목록에 되돌려놓는다.
                foreach (var model in _unitMoveUnit.Models.ToArray())
                {
                    _pieces.Remove(model);
                    Destroy(model.gameObject);
                }
                _unitMoveUnit.Models.Clear();
                if (_deploymentDefSnapshot != null)
                {
                    _pendingUnits.Add(_deploymentDefSnapshot);
                    RefreshPendingList();
                }
            }
            else
            {
                foreach (var kv in _unitMoveOriginalPositions)
                {
                    if (kv.Key != null)
                    {
                        kv.Key.Center = kv.Value;
                    }
                }
            }
            _draggingPiece = null;
            _draggingFollower = null;
            EndUnitMove();
        }

        private void EndUnitMove()
        {
            _unitMoveActive = false;
            _unitMoveLeading = null;
            _unitMoveUnit = null;
            _unitMovePhase = "";
            _unitMoveOriginalPositions.Clear();
            _unitMoveIsDeployment = false;
            _deploymentDefSnapshot = null;
            _pendingFollowerCount = 0;
            _displacementAnchor = null;
            _displacementQueue.Clear();
            _displacementResumeLeadingFinish = false;
            ShowUnitMovePanel(false);
            guideline.ClearBand();
        }

        private void ShowUnitMovePanel(bool show)
        {
            // 경고 라벨의 표시 여부는 UpdateUnitMoveWarning()이 전담한다 — 여기서
            // 손대면 FinishLeadingMove()가 먼저 켜둔 경고를 지워버리게 된다.
            if (_unitMovePanel != null)
            {
                _unitMovePanel.gameObject.SetActive(show);
            }
            if (!show && _unitMoveWarningLabel != null)
            {
                _unitMoveWarningLabel.gameObject.SetActive(false);
            }
        }

        private void BuildUnitMovePanel()
        {
            // baseLayer는 항상 Canvas의 직속 자식이라는 전제 — 패널도 같은
            // Canvas 아래(GraphicRaycaster가 닿는 범위)에 둬야 한다.
            var canvasParent = baseLayer != null ? baseLayer.parent : transform;

            var panelGo = new GameObject("UnitMovePanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasParent, false);
            _unitMovePanel = (RectTransform)panelGo.transform;
            _unitMovePanel.anchorMin = new Vector2(1f, 1f);
            _unitMovePanel.anchorMax = new Vector2(1f, 1f);
            _unitMovePanel.pivot = new Vector2(1f, 1f);
            _unitMovePanel.anchoredPosition = new Vector2(-16f, -16f);
            _unitMovePanel.sizeDelta = new Vector2(220f, 90f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            var warningGo = new GameObject("Warning", typeof(RectTransform));
            warningGo.transform.SetParent(panelGo.transform, false);
            _unitMoveWarningLabel = warningGo.AddComponent<TextMeshProUGUI>();
            _unitMoveWarningLabel.text = "코헤런시를 이탈한 모델은 즉시 사상자로서 제거됩니다.";
            _unitMoveWarningLabel.fontSize = 13f;
            _unitMoveWarningLabel.color = new Color(1f, 0.3f, 0.3f, 1f);
            _unitMoveWarningLabel.enableWordWrapping = true;
            warningGo.SetActive(false);

            var btnGo = new GameObject("CompleteButton", typeof(RectTransform));
            btnGo.transform.SetParent(panelGo.transform, false);
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 32f;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(OnUnitMoveCompletePressed);
            var btnLabelGo = new GameObject("Label", typeof(RectTransform));
            btnLabelGo.transform.SetParent(btnGo.transform, false);
            var btnLabelRect = (RectTransform)btnLabelGo.transform;
            btnLabelRect.anchorMin = Vector2.zero;
            btnLabelRect.anchorMax = Vector2.one;
            btnLabelRect.offsetMin = Vector2.zero;
            btnLabelRect.offsetMax = Vector2.zero;
            var btnLabel = btnLabelGo.AddComponent<TextMeshProUGUI>();
            btnLabel.text = "완료";
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.fontSize = 16f;
            btnLabel.color = Color.white;
            btnLabel.raycastTarget = false;

            panelGo.SetActive(false);
        }

        // ── 예비대 배치 ──────────────────────────────────────────────────

        private void BuildPendingPanel()
        {
            var canvasParent = baseLayer != null ? baseLayer.parent : transform;

            var panelGo = new GameObject("PendingUnitsPanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasParent, false);
            _pendingPanel = (RectTransform)panelGo.transform;
            _pendingPanel.anchorMin = new Vector2(0f, 1f);
            _pendingPanel.anchorMax = new Vector2(0f, 1f);
            _pendingPanel.pivot = new Vector2(0f, 1f);
            _pendingPanel.anchoredPosition = new Vector2(16f, -16f);
            _pendingPanel.sizeDelta = new Vector2(220f, 40f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // AddPendingUnit()은 부트스트랩 등에서 Configure() 직후(Start() 전에)
            // 불릴 수 있는데, 그때는 이 패널이 아직 없어 RefreshPendingList()가
            // 아무 것도 못 그리고 조용히 넘어간다 — 여기서 한 번 더 그려준다.
            RefreshPendingList();
        }

        private void RefreshPendingList()
        {
            if (_pendingPanel == null)
            {
                return;
            }
            for (int i = _pendingPanel.childCount - 1; i >= 0; i--)
            {
                Destroy(_pendingPanel.GetChild(i).gameObject);
            }

            for (int i = 0; i < _pendingUnits.Count; i++)
            {
                var def = _pendingUnits[i];
                int capturedIndex = i;

                var btnGo = new GameObject($"Pending_{def.Name}", typeof(RectTransform));
                btnGo.transform.SetParent(_pendingPanel, false);
                var btnLe = btnGo.AddComponent<LayoutElement>();
                btnLe.preferredHeight = 32f;
                var btnImg = btnGo.AddComponent<Image>();
                btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                var btn = btnGo.AddComponent<Button>();
                btn.onClick.AddListener(() => StartDeployment(capturedIndex));

                var labelGo = new GameObject("Label", typeof(RectTransform));
                labelGo.transform.SetParent(btnGo.transform, false);
                var labelRect = (RectTransform)labelGo.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                var label = labelGo.AddComponent<TextMeshProUGUI>();
                label.text = $"{def.Name} ({def.Team}, {def.ModelCount}모델)";
                label.alignment = TextAlignmentOptions.Center;
                label.fontSize = 13f;
                label.color = Color.white;
                label.raycastTarget = false;
            }
        }

        /// <summary>예비대 목록에서 index번째 정의의 배치를 시작한다 — 목록에서
        /// 빼고, 배치 미리보기(고스트)와 배치 밴드를 보여준다. 실제 리딩 모델
        /// 생성/드래그는 지도 배경을 클릭하는 순간(BeginDeploymentDrag) 이루어진다.</summary>
        public void StartDeployment(int index)
        {
            if (_unitMoveActive || _pendingDeploymentDef != null || _displacementQueue.Count > 0
                    || index < 0 || index >= _pendingUnits.Count)
            {
                return;
            }
            var def = _pendingUnits[index];
            _pendingUnits.RemoveAt(index);
            RefreshPendingList();

            _pendingDeploymentDef = def;
            ShowBasePlacementPreview(def.SizeMm, def.FillColor, def.IsDisplacement);
            ShowDeploymentBand(def);
        }

        private void HandlePendingDeploymentInput()
        {
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi())
            {
                // 빈 곳 우클릭 — 배치 취소, 정의를 예비대 목록으로 되돌린다.
                _pendingUnits.Add(_pendingDeploymentDef);
                _pendingDeploymentDef = null;
                RefreshPendingList();
                ClearPlacementPreview();
                ClearDeploymentBand();
                return;
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                if (TryGetLocalMouse(out var clickPoint))
                {
                    BeginDeploymentDrag(clickPoint);
                }
                return;
            }

            if (TryGetLocalMouse(out var mouseLocal))
            {
                UpdatePlacementPreviewPosition(mouseLocal);
            }
        }

        /// <summary>지도 배경 클릭으로 실제 리딩 모델을 만들고, 곧바로 일반
        /// 유닛-이동의 "리딩 모델 드래그" 상태로 들어간다 — 이후로는
        /// EndPieceDrag()/FinishLeadingMove()가 일반 유닛 이동과 동일하게
        /// 처리한다(변위 베이스 밀어내기 포함).</summary>
        private void BeginDeploymentDrag(Vector2 clickPoint)
        {
            ClearPlacementPreview();
            ClearDeploymentBand();

            var def = _pendingDeploymentDef;
            _pendingDeploymentDef = null;

            var unit = new Unit
            {
                UnitName = def.Name,
                Team = def.Team,
                CoherencyInch = def.CoherencyInch,
                MoveInch = def.MoveInch,
                CanMove = def.CanMove,
            };

            var leading = CreatePieceObject(unit, def.SizeMm, def.FillColor, def.IsDisplacement);
            // "유닛 되돌리기"로 되돌아온 유닛은 데미지 기록을 유지한 채 재배치된다.
            if (def.Damages.Count > 0)
            {
                leading.Damage = def.Damages[0];
            }
            unit.Models.Add(leading);

            // 나머지 모델은 아직 만들지 않는다 — 리딩 모델이 실제로 놓이기 전까지는
            // 클릭 지점에 겹쳐서 충돌 해소를 방해하게 된다.
            _pendingFollowerCount = Mathf.Max(def.ModelCount - 1, 0);
            _deploymentDefSnapshot = def;

            _unitMoveActive = true;
            _unitMoveIsDeployment = true;
            _unitMoveLeading = leading;
            _unitMoveUnit = unit;
            _unitMovePhase = "leading";
            _unitMoveOriginalPositions.Clear();

            // 배치구역/이동거리 밴드는 참고용으로만 보여주고, 실제 배치 위치는
            // 자유롭게 아무 데나 놓을 수 있다 — 충돌 회피와 지도 경계만 지킨다.
            // 리딩 모델 이동이므로 변위 베이스는 통과할 수 있다.
            leading.Center = ResolvePosition(leading, clickPoint, true);
            leading.Refresh();

            _draggingPiece = leading;
            TryGetLocalMouse(out var mouseLocal);
            _dragOffset = leading.Center - mouseLocal;
            leading.transform.SetAsLastSibling();
        }

        private void ShowBasePlacementPreview(Vector2 sizeMm, Color fillColor, bool isDisplacement)
        {
            ClearPlacementPreview();
            var go = new GameObject("PlacementPreview", typeof(RectTransform));
            go.transform.SetParent(baseLayer, false);
            var preview = go.AddComponent<Base>();
            preview.SizeMm = sizeMm;
            var mutedColor = fillColor;
            mutedColor.a *= 0.5f;
            preview.FillColor = mutedColor;
            preview.IsDisplacement = isDisplacement;
            preview.raycastTarget = false;
            preview.Refresh();
            _placementPreview = preview;
        }

        private void UpdatePlacementPreviewPosition(Vector2 localMouse)
        {
            if (_placementPreview != null)
            {
                _placementPreview.Center = localMouse;
            }
        }

        private void ClearPlacementPreview()
        {
            if (_placementPreview != null)
            {
                Destroy(_placementPreview.gameObject);
                _placementPreview = null;
            }
        }

        /// <summary>배치 밴드를 참고용으로 보여준다 — MissionData에 이 팀의
        /// 배치구역이 있으면 실제 구역(들)을 "약통" 모양(양 끝이 둥근) 폴리곤
        /// 으로, 없으면 지도 전체 가장자리 안쪽 테두리를 폴백으로 보여준다.
        /// 실제 배치 위치는 이 밴드에 제약받지 않는다 — 충돌 회피와 지도
        /// 경계만 지키면 어디든 놓을 수 있다(딥 스트라이크 등 예외를 일일이
        /// 모델링하는 대신 플레이어가 규칙에 맞게 직접 배치하도록 맡긴다).</summary>
        private void ShowDeploymentBand(PendingUnitDef def)
        {
            float radius = EllipseMath.BoundingRadius(def.SizeMm);
            float moveMm = def.MoveInch * GameConstants.MmPerInch;

            var segments = TeamZoneSegments(def.Team);
            var polylines = new List<Vector2[]>();

            if (segments.Count == 0)
            {
                polylines.Add(FallbackEdgeBandPolyline(radius, moveMm));
            }
            else
            {
                float visualDepth = radius * 2f + moveMm;
                foreach (var zone in segments)
                {
                    var capsule = BuildCapsulePolygon(zone.Edge, zone.StartAlong, zone.EndAlong, visualDepth);
                    var closed = new Vector2[capsule.Length + 1];
                    capsule.CopyTo(closed, 0);
                    closed[capsule.Length] = capsule[0];
                    polylines.Add(closed);
                }
            }

            guideline.BandPolylines = polylines;
        }

        private void ClearDeploymentBand()
        {
            guideline.BandPolylines = new List<Vector2[]>();
        }

        private static List<DeploymentZoneData> TeamZoneSegments(string team)
        {
            var result = new List<DeploymentZoneData>();
            if (!MissionData.HasData)
            {
                return result;
            }
            foreach (var zone in MissionData.DeploymentZones)
            {
                if (zone.Player == team)
                {
                    result.Add(zone);
                }
            }
            return result;
        }

        private Vector2[] FallbackEdgeBandPolyline(float radius, float moveMm)
        {
            float inset = radius + moveMm + radius;
            var p = new Vector2(inset, inset);
            var s = new Vector2(Mathf.Max(mapSizeMm.x - inset * 2f, 0f), Mathf.Max(mapSizeMm.y - inset * 2f, 0f));
            return new[] { p, p + new Vector2(s.x, 0f), p + s, p + new Vector2(0f, s.y), p };
        }

        /// <summary>구간 [a,b]에서 depth만큼 보드 안쪽으로 뻗은 "약통" 모양(양
        /// 끝은 컴퍼스로 그린 것처럼 둥글게) 외곽선. 지도 가장자리 쪽은 닫지
        /// 않아도 된다 — 밴드를 그릴 때 마지막 점을 첫 점과 이어서 자연히
        /// 가장자리를 따라 닫히게 한다(호출부에서 처리). 여러 구역을 하나의
        /// 다각형으로 합치는 것(Godot판의 Geometry2D.merge_polygons)은 아직
        /// 안 한다 — 인접한 구역끼리는 윤곽선이 겹쳐 보일 수 있다.</summary>
        private Vector2[] BuildCapsulePolygon(string edge, float a, float b, float depth)
        {
            const int steps = 16;
            var points = new Vector2[(steps + 1) * 2];
            int idx = 0;
            for (int i = 0; i <= steps; i++)
            {
                float t = 180f - 90f * i / (float)steps;
                float rad = t * Mathf.Deg2Rad;
                points[idx++] = ClampToMap(LocalToWorld(edge, new Vector2(a + depth * Mathf.Cos(rad), depth * Mathf.Sin(rad))));
            }
            for (int i = 0; i <= steps; i++)
            {
                float t = 90f - 90f * i / (float)steps;
                float rad = t * Mathf.Deg2Rad;
                points[idx++] = ClampToMap(LocalToWorld(edge, new Vector2(b + depth * Mathf.Cos(rad), depth * Mathf.Sin(rad))));
            }
            return points;
        }

        /// <summary>p = (along, depth-into-board)를 지도 로컬 mm 좌표로 변환한다.</summary>
        private Vector2 LocalToWorld(string edge, Vector2 p)
        {
            switch (edge)
            {
                case "left":
                    return new Vector2(p.y, p.x);
                case "right":
                    return new Vector2(mapSizeMm.x - p.y, p.x);
                case "top":
                    return new Vector2(p.x, p.y);
                case "bottom":
                    return new Vector2(p.x, mapSizeMm.y - p.y);
                default:
                    return Vector2.zero;
            }
        }

        private Vector2 ClampToMap(Vector2 p)
        {
            return new Vector2(Mathf.Clamp(p.x, 0f, mapSizeMm.x), Mathf.Clamp(p.y, 0f, mapSizeMm.y));
        }

        // ── 변위 베이스 재배치 ──────────────────────────────────────────

        /// <summary>원형 근사 거리가 아니라, 실제 겹침 판정과 똑같은 (회전된)
        /// 타원 폴리곤+SAT 검사를 그대로 재사용한다.</summary>
        private List<Base> FindOverlappingDisplacementBases(Base movedPiece)
        {
            var result = new List<Base>();
            if (movedPiece == null)
            {
                return result;
            }
            var polyA = EllipseMath.EllipsePolygonAt(movedPiece.Center, movedPiece.SizeMm, movedPiece.RotationRadians);
            foreach (var other in _pieces)
            {
                if (other == movedPiece || !other.IsDisplacement)
                {
                    continue;
                }
                var polyB = EllipseMath.EllipsePolygonAt(other.Center, other.SizeMm, other.RotationRadians);
                if (EllipseMath.PolygonOverlapMtv(polyA, movedPiece.Center, polyB, other.Center).HasValue)
                {
                    result.Add(other);
                }
            }
            return result;
        }

        /// <summary>모델 메뉴얼 이동/리딩 모델 이동이 끝난 직후, 방금 통과한 변위
        /// 베이스(들)의 새 위치는 이동한 사람이 직접 정한다: 항상 anchor에 딱
        /// 붙은 채(원하는 거리 0") 마우스를 따라가다가, 클릭하면 확정된다.</summary>
        private void StartDisplacementPlacement(Base anchor, List<Base> queue, bool resumeLeadingFinish)
        {
            _displacementAnchor = anchor;
            _displacementQueue.Clear();
            _displacementQueue.AddRange(queue);
            _displacementResumeLeadingFinish = resumeLeadingFinish;

            if (TryGetLocalMouse(out var mouseLocal))
            {
                var piece = _displacementQueue[0];
                piece.Center = ResolveDisplacementDragPosition(piece, mouseLocal);
            }
        }

        private void HandleDisplacementPlacementInput()
        {
            if (TryGetLocalMouse(out var local))
            {
                var piece = _displacementQueue[0];
                piece.Center = ResolveDisplacementDragPosition(piece, local);
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                _displacementQueue.RemoveAt(0);
                if (_displacementQueue.Count == 0)
                {
                    bool resume = _displacementResumeLeadingFinish;
                    _displacementAnchor = null;
                    _displacementResumeLeadingFinish = false;
                    if (resume)
                    {
                        // 유닛 이동/배치 중에 변위를 통과한 경우 — 그 트랜잭션을 이어서 마무리한다.
                        FinishLeadingMove();
                    }
                }
            }
        }

        /// <summary>변위 베이스는 anchor 테두리에 정확히 맞닿은 채(원하는 거리 0")
        /// 마우스를 따라 돈다 — 원형 근사(bounding radius) 합이 아니라, 그 방향의
        /// 실제 타원 반지름을 각각 재서 더해야 회전된 타원끼리도 정확히 맞닿는다.</summary>
        private Vector2 ResolveDisplacementDragPosition(Base piece, Vector2 desiredCenter)
        {
            Vector2 anchorCenter = _displacementAnchor.Center;
            Vector2 offset = desiredCenter - anchorCenter;
            if (offset.magnitude < 0.01f)
            {
                offset = Vector2.right;
            }
            Vector2 direction = offset.normalized;
            float minDist = EllipseMath.EllipseRadiusInDirection(_displacementAnchor.SizeMm, _displacementAnchor.RotationRadians, direction)
                    + EllipseMath.EllipseRadiusInDirection(piece.SizeMm, piece.RotationRadians, direction);
            Vector2 pos = anchorCenter + direction * minDist;
            return ResolvePosition(piece, pos, false);
        }
    }
}
