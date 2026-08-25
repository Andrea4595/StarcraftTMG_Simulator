using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 보드 위 베이스 조각들을 소유하고, 다이얼 메뉴 액션(데미지 기록/모델
    /// 제거/복제/이름 변경/메모)과 유닛 이동 워크플로우(리딩 모델 배치→팔로워
    /// 배치→완료/취소, 코헤런시 판정)를 처리한다. Godot판 GameBoard.gd 포팅.
    /// 배치(예비대)/변위 재배치는 아직 없다(다음 단계).
    /// </summary>
    public class BoardManager : MonoBehaviour
    {
        [SerializeField] private RectTransform baseLayer;
        [SerializeField] private RadialMenu radialMenu;
        [SerializeField] private InputDialog damageDialog;
        [SerializeField] private InputDialog renameDialog;
        [SerializeField] private InputDialog memoDialog;
        [SerializeField] private GuidelineOverlay guideline;
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
        }

        /// <summary>씬을 코드로 구성할 때(부트스트랩 등) 인스펙터 대신 쓰는 초기화.</summary>
        public void Configure(RectTransform baseLayerRef, RadialMenu radialMenuRef,
                InputDialog damageDialogRef, InputDialog renameDialogRef, InputDialog memoDialogRef,
                GuidelineOverlay guidelineRef)
        {
            baseLayer = baseLayerRef;
            radialMenu = radialMenuRef;
            damageDialog = damageDialogRef;
            renameDialog = renameDialogRef;
            memoDialog = memoDialogRef;
            guideline = guidelineRef;
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
            }
        }

        private bool TryGetLocalMouse(out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(baseLayer, Input.mousePosition, null, out local);
        }

        private void OnDragRequested(Base piece)
        {
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
            _draggingPiece = null;
            if (finishedLeading)
            {
                FinishLeadingMove();
            }
        }

        /// <summary>다른 베이스들과 절대 안 겹치게, 그리고 지도 경계 안으로 위치를
        /// 보정한다. allowDisplacementOverlap이면 변위 베이스는 장애물로 안 친다
        /// (모델 메뉴얼 이동/리딩 모델 이동 중일 때 — 다음 단계에서 실제로 씀).</summary>
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
                // "revert_unit"은 예비대 개념이 생기는 다음 단계에서 연결한다.
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
            foreach (var kv in _unitMoveOriginalPositions)
            {
                if (kv.Key != null)
                {
                    kv.Key.Center = kv.Value;
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
    }
}
