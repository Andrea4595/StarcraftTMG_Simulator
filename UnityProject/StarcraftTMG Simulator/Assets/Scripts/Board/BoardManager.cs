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
        [SerializeField] private RectTransform mapArea;
        [SerializeField] private RadialMenu radialMenu;
        [SerializeField] private InputDialog damageDialog;
        [SerializeField] private InputDialog renameDialog;
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
        private PendingUnitDef _pendingDeploymentDef;
        private bool _unitMoveIsDeployment;
        private PendingUnitDef _deploymentDefSnapshot;
        private int _pendingFollowerCount;
        private Base _placementPreview;

        // ── 로스터 JSON 임포트 / 토큰 ────────────────────────────────────
        [SerializeField] private RosterFileDialog rosterFileDialog;
        private string _rosterImportTeam = "A";
        private readonly List<PendingTokenDef> _pendingRosterTokens = new List<PendingTokenDef>();
        private readonly Dictionary<string, RectTransform> _rosterTokenListContainers = new Dictionary<string, RectTransform>();
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
            renameDialog.Confirmed += OnRenameConfirmed;
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
                InputDialog damageDialogRef, InputDialog renameDialogRef, InputDialog memoDialogRef,
                GuidelineOverlay guidelineRef, MemoOverlay memoOverlayRef,
                RangeInputDialog rangeInputDialogRef, RangeOverlay rangeOutlineLayerRef, RangeOverlay rangeFillLayerRef,
                MeasureOverlay measureLayerRef)
        {
            baseLayer = baseLayerRef;
            mapArea = mapAreaRef;
            radialMenu = radialMenuRef;
            damageDialog = damageDialogRef;
            renameDialog = renameDialogRef;
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

        /// <summary>지도가 뷰포트 안에 들어오도록 기본 축소 비율을 다시 계산하고,
        /// 현재 줌 레벨을 반영해 mapArea를 중앙에 배치한다. 창 크기가 바뀌거나
        /// 지도 크기가 바뀔 때 호출된다 — Godot판 _layout()과 같은 지점에서
        /// 패닝 오프셋을 초기화(재중앙)한다는 것도 동일하다.</summary>
        private void UpdateMapLayout()
        {
            if (mapArea == null)
            {
                return;
            }

            var parent = mapArea.parent as RectTransform;
            Vector2 avail = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);

            float scaleFactor = Mathf.Min(avail.x / mapSizeMm.x, avail.y / mapSizeMm.y);
            scaleFactor = Mathf.Min(scaleFactor, 1f);
            _baseScaleFactor = scaleFactor;

            mapArea.sizeDelta = mapSizeMm;
            float totalScale = scaleFactor * _zoomLevel;
            mapArea.localScale = new Vector3(totalScale, totalScale, 1f);
            mapArea.anchoredPosition = Vector2.zero;
            _lastViewportSize = avail;
        }

        /// <summary>마우스가 가리키는 지도 위 지점이 화면상 같은 자리에 그대로
        /// 있도록 확대/축소하면서 위치를 함께 보정한다. Godot판 _zoom_at() 포팅.</summary>
        private void ZoomAt(Vector2 screenPos, float factor)
        {
            if (mapArea == null)
            {
                return;
            }
            var parent = mapArea.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            float newZoom = Mathf.Clamp(_zoomLevel * factor, MinZoom, MaxZoom);
            if (Mathf.Approximately(newZoom, _zoomLevel))
            {
                return;
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPos, null, out var mouseLocal))
            {
                return;
            }

            float oldScale = mapArea.localScale.x;
            Vector2 mapPoint = (mouseLocal - mapArea.anchoredPosition) / oldScale;

            _zoomLevel = newZoom;
            float newScale = _baseScaleFactor * _zoomLevel;
            mapArea.localScale = new Vector3(newScale, newScale, 1f);
            mapArea.anchoredPosition = mouseLocal - mapPoint * newScale;
        }

        /// <summary>가운데 버튼 드래그로 화면 이동, 마우스 휠로 커서 위치 기준
        /// 확대/축소. UI 패널 위에서 시작한 경우는 무시한다.</summary>
        private void HandlePanAndZoom()
        {
            if (mapArea == null)
            {
                return;
            }
            var parent = mapArea.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (Input.GetMouseButtonDown(2) && !IsPointerOverUi())
            {
                _panning = true;
                _lastPanScreenPos = Input.mousePosition;
            }
            if (Input.GetMouseButtonUp(2))
            {
                _panning = false;
            }

            if (_panning)
            {
                Vector2 currentScreenPos = Input.mousePosition;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, currentScreenPos, null, out var curLocal)
                        && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, _lastPanScreenPos, null, out var prevLocal))
                {
                    mapArea.anchoredPosition += curLocal - prevLocal;
                }
                _lastPanScreenPos = currentScreenPos;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f) && !IsPointerOverUi())
            {
                float factor = scroll > 0f ? ZoomStep : 1f / ZoomStep;
                ZoomAt(Input.mousePosition, factor);
            }

            Vector2 avail = parent.rect.size;
            if (avail != _lastViewportSize)
            {
                UpdateMapLayout();
            }
        }

        // ── 거리 재기(스페이스바) ───────────────────────────────────────

        /// <summary>스페이스바를 누르고 있는 동안 시작점부터 지금 마우스
        /// 위치(또는 그 아래 베이스)까지 거리를 잰다. Godot판 GameBoard.gd의
        /// 스페이스바 처리+_start_measuring/_stop_measuring/_update_measure_line
        /// 포팅. 이름/데미지/범위/메모 입력창에 포커스가 있을 때는 무시한다
        /// (그 필드에 스페이스 문자를 입력하는 것으로 취급).</summary>
        private void HandleMeasureInput()
        {
            if (measureLayer == null || IsTextFieldFocused())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                StartMeasuring();
            }
            else if (Input.GetKeyUp(KeyCode.Space))
            {
                StopMeasuring();
            }

            if (_measuring)
            {
                UpdateMeasureLine();
            }
        }

        private static bool IsTextFieldFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        private void StartMeasuring()
        {
            if (_measuring)
            {
                return;
            }
            _measuring = true;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                _measureFromBase = FindBaseAtPoint(mouseLocal);
                _measureFromPoint = mouseLocal;
            }
        }

        private void StopMeasuring()
        {
            _measuring = false;
            _measureFromBase = null;
            measureLayer.Hide();
        }

        private void UpdateMeasureLine()
        {
            if (!TryGetLocalMouse(out var mouseLocal))
            {
                return;
            }

            // 재는 도중에도 마우스가 다른 유닛 위로 올라가면 그 유닛의 베이스까지
            // 가장 가까운 거리를 재도록, 매 프레임 다시 찾는다(시작 쪽 베이스는
            // 스페이스바를 누른 순간에 고정).
            var toBase = FindBaseAtPoint(mouseLocal);
            if (toBase == _measureFromBase)
            {
                toBase = null;
            }

            Vector2 fromPos = _measureFromBase != null ? _measureFromBase.Center : _measureFromPoint;
            Vector2 toPos = toBase != null ? toBase.Center : mouseLocal;

            // 양쪽(또는 한쪽)이 타원이면, 서로에게 가장 가까운 점을 번갈아 다시
            // 계산하는 것을 몇 차례 반복해 수렴시킨다(충돌 해소와 같은 방식).
            for (int i = 0; i < 6; i++)
            {
                if (_measureFromBase != null)
                {
                    fromPos = EllipseMath.ClosestPointOnEllipseWorld(_measureFromBase.Center, _measureFromBase.SizeMm, _measureFromBase.RotationRadians, toPos);
                }
                if (toBase != null)
                {
                    toPos = EllipseMath.ClosestPointOnEllipseWorld(toBase.Center, toBase.SizeMm, toBase.RotationRadians, fromPos);
                }
            }

            float distMm = Vector2.Distance(fromPos, toPos);
            measureLayer.SetLine(fromPos, toPos, $"{distMm / GameConstants.MmPerInch:F1}\"");
        }

        // ── 마커(활성화/점령/아이콘) ───────────────────────────────────

        /// <summary>화면 하단 중앙에 마커 종류별 버튼을 모아 놓은 작은 패널을
        /// 띄운다 — 누르면 StartMarkerPlacement()로 배치 모드에 들어간다.
        /// Godot판 _build_marker_bar() 포팅. 버튼은 아이콘만 보여준다(글자
        /// 없음). 패널 자체가 ContentSizeFitter로 버튼 묶음 크기에 딱 맞게
        /// 줄어들고 화면 폭 전체가 아니라 중앙 한 곳에 뭉쳐 보이도록, 예전의
        /// "화면 전체 폭 바 + 그 안의 줄"(HorizontalLayoutGroup의
        /// childForceExpand 기본값이 true라서 버튼들이 그 넓은 폭에 흩뿌려져
        /// 보였다) 대신 바 자체를 레이아웃 그룹으로 두고 내용물 크기로
        /// 줄어들게 했다.</summary>
        private void BuildMarkerBar()
        {
            var canvasParent = GetCanvasParent();

            var barGo = new GameObject("MarkerBar", typeof(RectTransform));
            barGo.transform.SetParent(canvasParent, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 8f);

            var bg = barGo.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);

            var layout = barGo.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = barGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var entry in MarkerBarEntries)
            {
                Texture2D icon = entry.Kind switch
                {
                    "activation" => _activationTextureMovement,
                    "capture" => _captureTexture,
                    _ => _iconTextures != null && _iconTextures.TryGetValue(entry.Kind, out var t) ? t : null,
                };
                CreateMarkerBarButton(barRect, entry.Kind, icon);
            }
        }

        private void CreateMarkerBarButton(Transform parent, string kind, Texture2D icon)
        {
            var go = new GameObject($"MarkerBtn_{kind}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(MarkerBarHeight - 8f, MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = icon;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => StartMarkerPlacement(kind));
        }

        private void StartMarkerPlacement(string kind)
        {
            if (_unitMoveActive)
            {
                return;
            }
            _placingMarkerKind = kind;
            ShowMarkerPlacementPreview(kind);
        }

        private void ShowMarkerPlacementPreview(string kind)
        {
            ClearMarkerPlacementPreview();
            var marker = CreateMarkerObject(kind, markerLayer);
            marker.raycastTarget = false;
            var c = marker.color;
            c.a *= 0.5f;
            marker.color = c;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                marker.Center = mouseLocal;
            }
            _markerPlacementPreview = marker;
        }

        private void ClearMarkerPlacementPreview()
        {
            if (_markerPlacementPreview != null)
            {
                Destroy(_markerPlacementPreview.gameObject);
                _markerPlacementPreview = null;
            }
        }

        /// <summary>kind에 맞는 마커 컴포넌트를 새로 만들어 parent 아래에 붙인다
        /// (아직 위치/이벤트 연결은 안 함 — 배치 미리보기/실제 배치 양쪽에서
        /// 공용으로 쓴다).</summary>
        private MarkerBase CreateMarkerObject(string kind, Transform parent)
        {
            switch (kind)
            {
                case "activation":
                {
                    var go = new GameObject("ActivationMarker", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    var marker = go.AddComponent<ActivationMarker>();
                    marker.Configure(_activationTextureMovement, _activationTextureAssault, _activationTextureDone);
                    return marker;
                }
                case "capture":
                {
                    var go = new GameObject("CaptureMarker", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    var marker = go.AddComponent<CaptureMarker>();
                    marker.Configure(_captureTexture);
                    return marker;
                }
                default:
                {
                    var go = new GameObject($"IconMarker_{kind}", typeof(RectTransform));
                    go.transform.SetParent(parent, false);
                    var marker = go.AddComponent<IconMarker>();
                    _iconTextures.TryGetValue(kind, out var tex);
                    marker.Configure(kind, tex);
                    return marker;
                }
            }
        }

        /// <summary>배치 모드 중 마우스 클릭 처리 — 지도 위 왼쪽 버튼이면 실제로
        /// 배치하고, 그 외 버튼이면 배치를 취소한다. 어느 쪽이든 배치 모드는
        /// 끝난다. Godot판 스페이스바 처리 바로 아래의 _placing_marker_kind
        /// 분기 포팅. UI(마커바 버튼 등) 위 클릭은 여기서 손대지 않는다 — 예를
        /// 들어 다른 마커 버튼을 눌러 종류를 바꾸는 클릭은 그 버튼의
        /// onClick(StartMarkerPlacement)이 처리하는데, 여기서도 같은 클릭을
        /// "배치 모드 종료"로 취급해버리면 막 시작된 새 배치가 같은 프레임에
        /// 취소돼버린다.</summary>
        private void HandleMarkerPlacementInput()
        {
            if (!IsPointerOverUi() && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
            {
                if (Input.GetMouseButtonDown(0) && TryGetLocalMouse(out var mouseLocal))
                {
                    PlaceMarker(_placingMarkerKind, mouseLocal);
                }
                _placingMarkerKind = "";
                ClearMarkerPlacementPreview();
                return;
            }

            if (TryGetLocalMouse(out var hoverLocal) && _markerPlacementPreview != null)
            {
                _markerPlacementPreview.Center = hoverLocal;
            }
        }

        private void PlaceMarker(string kind, Vector2 point)
        {
            BeginUndoTransaction();
            var marker = CreateMarkerObject(kind, markerLayer);
            marker.Center = ClampMarkerToMap(marker, point);
            marker.DragRequested += OnMarkerDragRequested;
            switch (kind)
            {
                case "activation":
                    marker.RightClicked += OnActivationMarkerRightClicked;
                    break;
                case "capture":
                    marker.RightClicked += OnCaptureMarkerRightClicked;
                    break;
                default:
                    marker.RightClicked += OnIconMarkerRightClicked;
                    break;
            }
            CommitUndoTransaction();
        }

        /// <summary>markerLayer는 baseLayer와 같은 중심-원점 mm 좌표계이므로
        /// (mapSizeMm의 절반이 지도 중심) 그 기준으로 클램프한다.</summary>
        private Vector2 ClampMarkerToMap(MarkerBase marker, Vector2 desiredCenter)
        {
            Vector2 half = marker.RectTransform.sizeDelta / 2f;
            Vector2 mapHalf = mapSizeMm / 2f;
            return new Vector2(
                    Mathf.Clamp(desiredCenter.x, -mapHalf.x + half.x, mapHalf.x - half.x),
                    Mathf.Clamp(desiredCenter.y, -mapHalf.y + half.y, mapHalf.y - half.y));
        }

        private void OnMarkerDragRequested(MarkerBase piece)
        {
            BeginUndoTransaction();
            _draggingMarker = piece;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                _markerDragOffset = piece.Center - mouseLocal;
            }
            piece.transform.SetAsLastSibling();
        }

        private void HandleMarkerDragInput()
        {
            if (Input.GetMouseButtonUp(0))
            {
                _draggingMarker = null;
                CommitUndoTransaction();
                return;
            }
            if (TryGetLocalMouse(out var mouseLocal))
            {
                var desired = mouseLocal + _markerDragOffset;
                _draggingMarker.Center = ClampMarkerToMap(_draggingMarker, desired);
            }
        }

        private void OnActivationMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 우클릭은 이동 → 돌격 → 완료를 계속 순환한다. shift+우클릭이 삭제.
            BeginUndoTransaction();
            var marker = (ActivationMarker)piece;
            if (shiftHeld)
            {
                Destroy(marker.gameObject);
                CommitUndoTransaction();
                return;
            }
            int idx = System.Array.IndexOf(ActivationMarker.StateSequence, marker.State);
            marker.SetState(ActivationMarker.StateSequence[(idx + 1) % ActivationMarker.StateSequence.Length]);
            CommitUndoTransaction();
        }

        private void OnCaptureMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 우클릭은 흰색 → 빨간색 → 파란색을 계속 순환한다. shift+우클릭이 삭제 —
            // 색 순환에 종료 지점이 없어서(활성화 마커처럼 마지막에 사라지는 게
            // 아님) 삭제는 별도 입력으로 뺐다.
            BeginUndoTransaction();
            var marker = (CaptureMarker)piece;
            if (shiftHeld)
            {
                Destroy(marker.gameObject);
                CommitUndoTransaction();
                return;
            }
            int idx = System.Array.IndexOf(CaptureMarker.ColorSequence, marker.ColorState);
            marker.SetColorState(CaptureMarker.ColorSequence[(idx + 1) % CaptureMarker.ColorSequence.Length]);
            CommitUndoTransaction();
        }

        private void OnIconMarkerRightClicked(MarkerBase piece, bool shiftHeld)
        {
            // 순환 없이 우클릭 한 번으로 바로 삭제(shift 여부는 상관없다).
            BeginUndoTransaction();
            Destroy(piece.gameObject);
            CommitUndoTransaction();
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

        // ── 메모 호버 표시 ───────────────────────────────────────────────

        private void UpdateHoveredUnit()
        {
            _hoveredBase = TryGetLocalMouse(out var mouseLocal) ? FindBaseAtPoint(mouseLocal) : null;
            _hoveredUnit = _hoveredBase != null ? _hoveredBase.Unit : null;

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

            RefreshRangeOverlays();
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
            _menuScreenPos = screenPos;
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
            options.Add(new RadialMenuOption("범위 표시", "range_display"));

            radialMenu.Open(options, screenPos);
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

        private void OnRenameConfirmed(string value)
        {
            if (_menuTarget == null || _menuTarget.Unit == null || string.IsNullOrWhiteSpace(value))
            {
                _menuTarget = null;
                return;
            }
            BeginUndoTransaction();
            var unit = _menuTarget.Unit;
            unit.UnitName = value.Trim();
            foreach (var model in unit.Models)
            {
                model.Refresh();
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

        // ── 범위 표시 ────────────────────────────────────────────────────

        private void OnRangeConfirmed(float value, bool alwaysShow)
        {
            // alwaysShow는 이 범위 항목이 만들어질 때 한 번 정해지면 끝 — 나중에
            // 값을 바꾸는 기능은 의도적으로 제공하지 않는다(Godot판과 동일).
            // 마음에 안 들면 지우고("범위 표시 → 제거") 새로 추가하면 된다.
            if (_rangeTargetUnit == null)
            {
                return;
            }
            BeginUndoTransaction();
            if (!_unitRanges.TryGetValue(_rangeTargetUnit, out var ranges))
            {
                ranges = new List<RangeSpec>();
                _unitRanges[_rangeTargetUnit] = ranges;
            }
            ranges.Add(new RangeSpec { Inch = value, AlwaysShow = alwaysShow });
            _rangeTargetUnit = null;
            RefreshRangeOverlays();
            CommitUndoTransaction();
        }

        private void HandleDeleteRangeRequest(Base piece)
        {
            if (piece == null || piece.Unit == null)
            {
                return;
            }
            var unit = piece.Unit;
            if (!_unitRanges.TryGetValue(unit, out var ranges) || ranges.Count == 0)
            {
                return;
            }

            if (ranges.Count == 1)
            {
                BeginUndoTransaction();
                _unitRanges.Remove(unit);
                RefreshRangeOverlays();
                CommitUndoTransaction();
                return;
            }

            _rangeDeleteTargetUnit = unit;
            var options = new List<RadialMenuOption>();
            for (int i = 0; i < ranges.Count; i++)
            {
                options.Add(new RadialMenuOption($"{ranges[i].Inch:F1}\" 범위 삭제", $"delete_range_{i}"));
            }
            radialMenu.Open(options, _menuScreenPos);
        }

        private void DeleteRangeAtIndex(Unit unit, int idx)
        {
            if (unit == null || !_unitRanges.TryGetValue(unit, out var ranges))
            {
                return;
            }
            if (idx < 0 || idx >= ranges.Count)
            {
                return;
            }
            BeginUndoTransaction();
            ranges.RemoveAt(idx);
            if (ranges.Count == 0)
            {
                _unitRanges.Remove(unit);
            }
            _rangeDeleteTargetUnit = null;
            RefreshRangeOverlays();
            CommitUndoTransaction();
        }

        /// <summary>등록된 사거리마다(상시 표시거나 지금 마우스가 올라간 유닛의
        /// 것이면) 그 유닛 모델들의 오프셋 다각형을 모아 Outline/Fill 레이어에
        /// 넘긴다. 마우스가 올라간 특정 모델 하나는 별도로 강조 채우기도
        /// 더한다. Godot판 _refresh_range_overlays() 포팅 — BoardManager의
        /// Update()가 매 프레임 UpdateHoveredUnit()을 부르므로, 움직이는
        /// 모델을 따라 범위 표시도 실시간으로 갱신된다.</summary>
        private void RefreshRangeOverlays()
        {
            if (rangeOutlineLayer == null || rangeFillLayer == null)
            {
                return;
            }

            var entries = new List<RangeOverlayEntry>();
            var highlightPolygons = new List<Vector2[]>();

            foreach (var kv in _unitRanges)
            {
                var unit = kv.Key;
                bool isHoveredUnit = unit == _hoveredUnit;
                foreach (var spec in kv.Value)
                {
                    if (!spec.AlwaysShow && !isHoveredUnit)
                    {
                        continue;
                    }

                    float offsetMm = spec.Inch * GameConstants.MmPerInch;
                    var entry = new RangeOverlayEntry
                    {
                        LabelText = $"{spec.Inch:F1}\"",
                        Hovered = isHoveredUnit,
                    };
                    Vector2 topPoint = Vector2.zero;
                    bool hasTopPoint = false;
                    foreach (var model in unit.Models)
                    {
                        if (model == null)
                        {
                            continue;
                        }
                        var polygon = EllipseMath.EllipseOffsetPolygonAt(model.Center, model.SizeMm, model.RotationRadians, offsetMm);
                        entry.Polygons.Add(polygon);
                        foreach (var p in polygon)
                        {
                            // 라벨은 다각형 뭉치의 맨 위(화면상 가장 위, Unity는 Y가
                            // 위로 증가하므로 y가 가장 큰 점)에 둔다.
                            if (!hasTopPoint || p.y > topPoint.y)
                            {
                                topPoint = p;
                                hasTopPoint = true;
                            }
                        }
                    }
                    if (entry.Polygons.Count == 0)
                    {
                        continue;
                    }
                    entry.LabelPos = topPoint + new Vector2(0f, 8f);
                    entries.Add(entry);

                    // 마우스가 이 유닛의 특정 모델 하나 위에 있으면, 그 모델 하나만을
                    // 중심으로 한 범위를 별도로 더 진한 채우기로 겹쳐 그린다.
                    if (isHoveredUnit && _hoveredBase != null && _hoveredBase.Unit == unit)
                    {
                        highlightPolygons.Add(EllipseMath.EllipseOffsetPolygonAt(
                                _hoveredBase.Center, _hoveredBase.SizeMm, _hoveredBase.RotationRadians, offsetMm));
                    }
                }
            }

            // rangeFillLayer는 이제 상시 배경 채우기가 없고 호버 강조 채우기만
            // 그리므로(RangeOverlay 참고), entries가 아니라 highlightPolygons만
            // 필요하다.
            rangeOutlineLayer.SetEntries(entries);
            rangeFillLayer.SetHighlightPolygons(highlightPolygons);
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

        // ── 유닛 이동 워크플로우 ────────────────────────────────────────

        public void StartUnitMove(Base leading)
        {
            if (leading == null || leading.Unit == null || _unitMoveActive)
            {
                return;
            }

            // 리딩 모델 배치부터 완료(또는 배치 중 취소)까지 전체를 되돌리기 한
            // 단계로 묶는다 — CompleteUnitMove()에서 커밋, CancelUnitMove()에서는
            // 폐기(원래 상태로 이미 되돌렸으므로 별도 되돌리기 단계가 필요 없음).
            BeginUndoTransaction();

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
                bool outOfCoherency = !IsFollowerWithinCoherency(model);
                // 코헤런시를 벗어난 모델은 하얗게 반짝인다(Base.CoherencyWarning) —
                // 전부 검사해야 하므로(첫 번째에서 break하지 않는다) 글로벌 경고
                // 문구뿐 아니라 어떤 모델이 문제인지도 바로 보인다.
                model.CoherencyWarning = outOfCoherency;
                if (outOfCoherency)
                {
                    anyOut = true;
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

        /// <summary>코헤런시 밖에 남은 팔로워가 있어도 모델을 지우지 않는다 —
        /// 이동 중 경고 라벨(_unitMoveWarningLabel)로 알려주는 것으로 충분하다
        /// (사용자 요청으로 자동 제거를 뺐다).</summary>
        private void CompleteUnitMove()
        {
            CommitUndoTransaction();
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
            // 취소는 원래 상태로 되돌렸을 뿐 새로운 변화가 없으므로, 열어둔
            // 되돌리기 트랜잭션은 커밋하지 않고 그냥 버린다.
            DiscardUndoTransaction();
            EndUnitMove();
        }

        private void EndUnitMove()
        {
            // 반짝임은 이동 상호작용 중에만 보여주는 실시간 경고다 — 이동이
            // 끝나면(완료든 취소든) 꺼둔다(_unitMoveUnit이 null이 되기 전에).
            if (_unitMoveUnit != null)
            {
                foreach (var model in _unitMoveUnit.Models)
                {
                    model.CoherencyWarning = false;
                }
            }
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

        /// <summary>고정 화면 UI(패널 등)를 매달 Canvas를 찾는다. mapArea는
        /// 패닝/줌으로 움직이므로 그 부모(=Canvas)를 써야 패널이 지도를
        /// 따라 움직이지 않는다.</summary>
        private Transform GetCanvasParent()
        {
            if (mapArea != null)
            {
                return mapArea.parent;
            }
            return baseLayer != null ? baseLayer.parent : transform;
        }

        private void BuildUnitMovePanel()
        {
            var canvasParent = GetCanvasParent();

            var panelGo = new GameObject("UnitMovePanel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasParent, false);
            // 화면 중앙 아래(마커바보다 위)에 둔다 — 예전엔 우측 상단이었는데,
            // B 로스터 패널이 그 자리로 옮겨오면서 서로 겹쳐 "완료" 버튼이
            // 안 보이는 문제가 있었다.
            _unitMovePanel = (RectTransform)panelGo.transform;
            _unitMovePanel.anchorMin = new Vector2(0.5f, 0f);
            _unitMovePanel.anchorMax = new Vector2(0.5f, 0f);
            _unitMovePanel.pivot = new Vector2(0.5f, 0f);
            _unitMovePanel.anchoredPosition = new Vector2(0f, 70f);
            _unitMovePanel.sizeDelta = new Vector2(260f, 90f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var warningGo = new GameObject("Warning", typeof(RectTransform));
            warningGo.transform.SetParent(panelGo.transform, false);
            _unitMoveWarningLabel = warningGo.AddComponent<TextMeshProUGUI>();
            _unitMoveWarningLabel.text = "코헤런시를 벗어나는 모델이 있습니다.";
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
            btnLabel.text = "이동 확정";
            btnLabel.alignment = TextAlignmentOptions.Center;
            btnLabel.fontSize = 16f;
            btnLabel.color = Color.white;
            btnLabel.raycastTarget = false;

            panelGo.SetActive(false);
        }

        // ── 예비대 배치 ──────────────────────────────────────────────────

        /// <summary>팀 A는 왼쪽, 팀 B는 오른쪽에 각자 로스터 불러오기 버튼 +
        /// 예비대 유닛 목록 + 토큰 목록을 담은 패널을 하나씩 짓는다.</summary>
        private void BuildPendingPanel()
        {
            BuildTeamPanel("A", left: true);
            BuildTeamPanel("B", left: false);

            // AddPendingUnit()은 부트스트랩 등에서 Configure() 직후(Start() 전에)
            // 불릴 수 있는데, 그때는 이 패널들이 아직 없어 RefreshPendingList()가
            // 아무 것도 못 그리고 조용히 넘어간다 — 여기서 한 번 더 그려준다.
            RefreshPendingList();
            RefreshRosterTokenList();
        }

        private void BuildTeamPanel(string team, bool left)
        {
            var canvasParent = GetCanvasParent();

            var panelGo = new GameObject($"PendingPanel_{team}", typeof(RectTransform));
            panelGo.transform.SetParent(canvasParent, false);
            var panel = (RectTransform)panelGo.transform;
            float xAnchor = left ? 0f : 1f;
            panel.anchorMin = new Vector2(xAnchor, 1f);
            panel.anchorMax = new Vector2(xAnchor, 1f);
            panel.pivot = new Vector2(xAnchor, 1f);
            panel.anchoredPosition = new Vector2(left ? 16f : -16f, -16f);
            panel.sizeDelta = new Vector2(220f, 40f);

            var bg = panelGo.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateListButton(panel, $"{team} 로스터 불러오기", () => ImportRoster(team));

            // 예비대 유닛 목록 — 길어지면 늘어나는 대신 스크롤된다.
            // RefreshPendingList()가 이 컨테이너의 자식만 갈아끼운다.
            _pendingUnitsListContainers[team] = ScrollListUtil.Create(panel, 180f, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _);

            // 토큰 목록 — 유닛과 달리 배치해도 목록에서 안 지워진다(몇 번이든 재배치 가능).
            CreateSectionLabel(panel, "토큰");
            _rosterTokenListContainers[team] = ScrollListUtil.Create(panel, 180f, new Color(0.1f, 0.1f, 0.1f, 0.6f), out _);
        }

        private static void CreateSectionLabel(Transform parent, string text)
        {
            var go = new GameObject("SectionLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 18f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 13f;
            label.color = new Color(0.75f, 0.75f, 0.75f, 1f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>예비대 패널의 버튼 하나(로스터 불러오기/예비대 유닛/토큰이
        /// 전부 이 모양을 공유한다).</summary>
        private static void CreateListButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = new GameObject($"Btn_{label}", typeof(RectTransform));
            btnGo.transform.SetParent(parent, false);
            var btnLe = btnGo.AddComponent<LayoutElement>();
            btnLe.preferredHeight = 32f;
            var btnImg = btnGo.AddComponent<Image>();
            btnImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var btn = btnGo.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var labelText = labelGo.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 13f;
            labelText.color = Color.white;
            labelText.raycastTarget = false;
        }

        private void RefreshPendingList()
        {
            foreach (var kv in _pendingUnitsListContainers)
            {
                string team = kv.Key;
                var container = kv.Value;
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    Destroy(container.GetChild(i).gameObject);
                }

                for (int i = 0; i < _pendingUnits.Count; i++)
                {
                    var def = _pendingUnits[i];
                    if (def.Team != team)
                    {
                        continue;
                    }
                    int capturedIndex = i;
                    CreateListButton(container, $"{def.Name} ({def.ModelCount}모델)", () => StartDeployment(capturedIndex));
                }
            }
        }

        private void RefreshRosterTokenList()
        {
            foreach (var kv in _rosterTokenListContainers)
            {
                string team = kv.Key;
                var container = kv.Value;
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    Destroy(container.GetChild(i).gameObject);
                }

                for (int i = 0; i < _pendingRosterTokens.Count; i++)
                {
                    var def = _pendingRosterTokens[i];
                    if (def.Team != team)
                    {
                        continue;
                    }
                    int capturedIndex = i;
                    CreateListButton(container, def.Name, () => StartRosterTokenPlacement(capturedIndex));
                }
            }
        }

        /// <summary>예비대 목록에서 index번째 정의의 배치를 시작한다 — 목록에서
        /// 빼고, 배치 미리보기(고스트)와 배치 밴드를 보여준다. 실제 리딩 모델
        /// 생성/드래그는 지도 배경을 클릭하는 순간(BeginDeploymentDrag) 이루어진다.</summary>
        public void StartDeployment(int index)
        {
            if (_unitMoveActive || _pendingDeploymentDef != null || _pendingRosterTokenDef != null || _displacementQueue.Count > 0
                    || index < 0 || index >= _pendingUnits.Count)
            {
                return;
            }
            // 여기서 예비대 목록에서 이미 항목을 빼므로, 되돌리기 트랜잭션도
            // 여기서 열어야 한다 — BeginDeploymentDrag()에서 열면 이미 빠진
            // 뒤라 되돌려도 목록에 복원이 안 된다. 지도 클릭 전에 우클릭으로
            // 취소하면(HandlePendingDeploymentInput) 폐기, 실제로 배치까지
            // 마치면 CompleteUnitMove()에서 커밋된다.
            BeginUndoTransaction();
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
                // StartDeployment()에서 연 트랜잭션을 그냥 버린다 — 예비대
                // 목록에서 뺐던 걸 그대로 되돌려놨을 뿐 보드는 전혀 안 바뀌었다.
                DiscardUndoTransaction();
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

        // ── 로스터 JSON 임포트 / 토큰 ────────────────────────────────────

        private const float TokenColorSaturationFactor = 0.45f;
        private const float TokenColorValueFactor = 0.9f;

        private void ImportRoster(string team)
        {
            if (rosterFileDialog == null)
            {
                return;
            }
            _rosterImportTeam = team;
            rosterFileDialog.Open();
        }

        private void OnRosterFileSelected(string path)
        {
            string jsonText;
            try
            {
                jsonText = System.IO.File.ReadAllText(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"로스터 파일을 열 수 없습니다: {path} ({e.Message})");
                return;
            }

            if (!RosterImporter.TryImport(jsonText, _rosterImportTeam, out var units, out var tokens, out var error))
            {
                Debug.LogWarning($"로스터 파일 형식이 올바르지 않습니다: {path} — {error}");
                return;
            }

            _pendingUnits.AddRange(units);
            _pendingRosterTokens.AddRange(tokens);
            RefreshPendingList();
            RefreshRosterTokenList();
        }

        /// <summary>토큰 정의는 유닛과 달리 목록에서 지우지 않는다 — 몇 번이든
        /// 다시 배치 가능해야 하므로. 실제 배치는 지도 배경 클릭에서 시작한다
        /// (HandlePendingRosterTokenInput → BeginRosterTokenPlacement).</summary>
        private void StartRosterTokenPlacement(int index)
        {
            if (_unitMoveActive || _pendingDeploymentDef != null || index < 0 || index >= _pendingRosterTokens.Count)
            {
                return;
            }
            _pendingRosterTokenDef = _pendingRosterTokens[index];
            var fillColor = GameConstants.TeamColors.TryGetValue(_pendingRosterTokenDef.Team, out var c) ? c : GameConstants.TeamColors["neutral"];
            ShowBasePlacementPreview(_pendingRosterTokenDef.SizeMm,
                    MutedColor(fillColor, TokenColorSaturationFactor, TokenColorValueFactor),
                    _pendingRosterTokenDef.IsDisplacement);
        }

        /// <summary>토큰은 팀 색을 그대로 쓰지 않고 채도를 낮추고 살짝만 어둡게
        /// 해서 일반 모델과 구분되게 한다 — 단순히 어둡게만 하면(RGB를 그대로
        /// 곱하면 채도는 안 바뀌고 명도만 낮아짐) 라벨 텍스트(검은색)와 대비가
        /// 부족해져 이름이 잘 안 보였다(Godot판과 동일한 이유).</summary>
        private static Color MutedColor(Color c, float saturationFactor, float valueFactor)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var rgb = Color.HSVToRGB(h, Mathf.Clamp01(s * saturationFactor), Mathf.Clamp01(v * valueFactor));
            return new Color(rgb.r, rgb.g, rgb.b, c.a);
        }

        private void HandlePendingRosterTokenInput()
        {
            if (Input.GetMouseButtonDown(1) && !IsPointerOverUi())
            {
                // 빈 곳 우클릭 — 배치 취소. 트랜잭션은 실제로 지도를 클릭할 때
                // (BeginRosterTokenPlacement)까지 시작하지 않으므로 버릴 것도 없다.
                _pendingRosterTokenDef = null;
                ClearPlacementPreview();
                return;
            }

            if (Input.GetMouseButtonDown(0) && !IsPointerOverUi())
            {
                if (TryGetLocalMouse(out var clickPoint))
                {
                    BeginRosterTokenPlacement(clickPoint);
                }
                return;
            }

            if (TryGetLocalMouse(out var mouseLocal))
            {
                UpdatePlacementPreviewPosition(mouseLocal);
            }
        }

        /// <summary>배치 직후 바로 일반 드래그로 이어지므로(아래 _draggingPiece),
        /// 커밋은 EndPieceDrag()의 드래그 종료 지점에서 자연히 이루어진다.
        /// _unitMoveActive를 켜지 않으므로 코헤런시/리딩-팔로워 흐름은 전혀
        /// 타지 않는다 — 이후로는 일반 베이스 드래그와 완전히 동일하게
        /// 처리된다(변위 베이스 관통, 놓을 때 겹친 변위 베이스 밀어내기 포함).</summary>
        private void BeginRosterTokenPlacement(Vector2 clickPoint)
        {
            BeginUndoTransaction();
            ClearPlacementPreview();

            var def = _pendingRosterTokenDef;
            _pendingRosterTokenDef = null;

            // 같은 이름+팀의 토큰이 이미 지도 위에 있으면 새 Unit을 만들지 않고
            // 그 Unit에 모델만 추가한다 — "동일한 다른 토큰과 한 유닛으로 취급".
            string key = $"{def.Team}|{def.Name}";
            if (!_rosterTokenUnits.TryGetValue(key, out var unit))
            {
                unit = new Unit { UnitName = def.Name, Team = def.Team, IsToken = true };
                _rosterTokenUnits[key] = unit;

                // 이 토큰이 처음 배치되는 순간(=새 Unit)에만 로스터에 미리 등록된
                // 범위를 "범위 표시"에 넣는다 — 같은 토큰을 또 배치해도 같은
                // Unit에 모델만 늘어날 뿐이므로 여기서 또 등록하면 중복된다.
                if (def.Ranges.Count > 0)
                {
                    _unitRanges[unit] = new List<RangeSpec>(def.Ranges);
                    RefreshRangeOverlays();
                }
            }

            var fillColor = GameConstants.TeamColors.TryGetValue(def.Team, out var c) ? c : GameConstants.TeamColors["neutral"];
            var piece = CreatePieceObject(unit, def.SizeMm, MutedColor(fillColor, TokenColorSaturationFactor, TokenColorValueFactor), def.IsDisplacement);
            unit.Models.Add(piece);
            piece.Center = ResolvePosition(piece, clickPoint, true);
            piece.Refresh();

            _draggingPiece = piece;
            TryGetLocalMouse(out var mouseLocal);
            _dragOffset = piece.Center - mouseLocal;
            piece.transform.SetAsLastSibling();
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

            // 로스터에 미리 등록된 범위 표시가 있으면(또는 "유닛 되돌리기"로
            // 유지해온 것이면) 배치와 동시에 다시 등록한다 — 다이얼 메뉴로
            // 하나하나 추가한 것과 동일하게 취급된다.
            if (def.Ranges.Count > 0)
            {
                _unitRanges[unit] = new List<RangeSpec>(def.Ranges);
            }

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
            return new[]
            {
                CornerToCenterMm(p),
                CornerToCenterMm(p + new Vector2(s.x, 0f)),
                CornerToCenterMm(p + s),
                CornerToCenterMm(p + new Vector2(0f, s.y)),
                CornerToCenterMm(p),
            };
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
                points[idx++] = CornerToCenterMm(ClampToMap(LocalToWorld(edge, new Vector2(a + depth * Mathf.Cos(rad), depth * Mathf.Sin(rad)))));
            }
            for (int i = 0; i <= steps; i++)
            {
                float t = 90f - 90f * i / (float)steps;
                float rad = t * Mathf.Deg2Rad;
                points[idx++] = CornerToCenterMm(ClampToMap(LocalToWorld(edge, new Vector2(b + depth * Mathf.Cos(rad), depth * Mathf.Sin(rad)))));
            }
            return points;
        }

        /// <summary>p = (along, depth-into-board)를 지도 로컬 mm 좌표(모서리-원점,
        /// [0,mapSize] 범위)로 변환한다 — 가장자리를 기준으로 한 계산이라
        /// 모서리-원점 쪽이 자연스럽다. 실제 렌더링(baseLayer/guideline)은
        /// 중심-원점이므로, 이 함수의 결과는 항상 CornerToCenterMm()을 거쳐야
        /// 한다(호출부인 BuildCapsulePolygon에서 처리).</summary>
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

        /// <summary>모서리-원점([0,mapSize]) mm 좌표를 baseLayer/guideline이 쓰는
        /// 중심-원점([-mapSize/2,+mapSize/2]) 좌표로 바꾼다 — 배치 밴드(약통
        /// 모양 배치구역, 폴백 가장자리 밴드) 계산은 지도 가장자리를 기준으로
        /// 하는 게 자연스러워 내부적으로 모서리-원점을 쓰지만, 실제로 그리는
        /// 대상(GuidelineOverlay)은 중심-원점이라 결과를 넘기기 전에 항상 이
        /// 변환을 거쳐야 한다.</summary>
        private Vector2 CornerToCenterMm(Vector2 cornerPoint)
        {
            return cornerPoint - mapSizeMm / 2f;
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
                    else
                    {
                        // 일반 드래그가 변위 베이스를 밀어낸 경우 — 원래 드래그부터 지금
                        // 이 배치까지를 한 트랜잭션으로 커밋한다.
                        CommitUndoTransaction();
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

        // ── 되돌리기(ctrl+z) / 다시 실행(ctrl+shift+z) ───────────────────

        private void HandleUndoRedoInput()
        {
            if (!Input.GetKeyDown(KeyCode.Z) || !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                return;
            }
            if (IsTextFieldFocused())
            {
                // 이름/데미지/범위 입력 필드에 포커스가 있으면 그 필드 자체의
                // 실행취소(ctrl+z)로 남겨둔다 — 게임판 되돌리기가 가로채지 않는다.
                return;
            }
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shiftHeld)
            {
                Redo();
            }
            else
            {
                Undo();
            }
        }

        private void BeginUndoTransaction()
        {
            if (_undoPendingActive)
            {
                return;
            }
            _undoPendingSnapshot = CaptureBoardSnapshot();
            _undoPendingActive = true;
        }

        private void CommitUndoTransaction()
        {
            if (!_undoPendingActive)
            {
                return;
            }
            _undoStack.Add(_undoPendingSnapshot);
            _redoStack.Clear();
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
        }

        private void DiscardUndoTransaction()
        {
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
        }

        /// <summary>진행 중인 트랜잭션이 있으면(드래그/유닛 이동/변위 배치 등) 그
        /// 중간 상태를 되돌리기로 덮어써서 망가뜨리면 안 되므로 무시한다.
        /// 다이얼로그나 다이얼 메뉴가 떠 있을 때도 마찬가지 — 그 뒤에서 보드가
        /// 바뀌면 열려 있는 창이 가리키는 대상(_menuTarget 등)이 붕 뜨게 된다.
        /// Godot판은 이 목록에 메모 다이얼로그를 빼먹었는데(아마 실수), Unity의
        /// UnityEngine.Object는 파괴된 오브젝트를 == null로 안전하게 취급해서
        /// 위험이 적긴 하지만 굳이 같은 구멍을 재현할 이유가 없어 포함시켰다.</summary>
        private bool IsUndoBlocked()
        {
            if (_undoPendingActive)
            {
                return true;
            }
            if (IsDialogVisible(damageDialog) || IsDialogVisible(renameDialog) || IsDialogVisible(memoDialog)
                    || IsDialogVisible(rangeInputDialog) || (radialMenu != null && radialMenu.gameObject.activeSelf))
            {
                return true;
            }
            return false;
        }

        private static bool IsDialogVisible(InputDialog dialog)
        {
            return dialog != null && dialog.gameObject.activeSelf;
        }

        private static bool IsDialogVisible(RangeInputDialog dialog)
        {
            return dialog != null && dialog.gameObject.activeSelf;
        }

        private void Undo()
        {
            if (IsUndoBlocked() || _undoStack.Count == 0)
            {
                return;
            }
            var current = CaptureBoardSnapshot();
            var previous = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(current);
            RestoreBoardSnapshot(previous);
        }

        private void Redo()
        {
            if (IsUndoBlocked() || _redoStack.Count == 0)
            {
                return;
            }
            var current = CaptureBoardSnapshot();
            var next = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(current);
            RestoreBoardSnapshot(next);
        }

        private BoardSnapshot CaptureBoardSnapshot()
        {
            var snapshot = new BoardSnapshot();
            var unitRefIds = new Dictionary<Unit, int>();

            for (int i = 0; i < baseLayer.childCount; i++)
            {
                var piece = baseLayer.GetChild(i).GetComponent<Base>();
                if (piece == null || piece.Unit == null)
                {
                    continue;
                }
                var unit = piece.Unit;
                if (!unitRefIds.TryGetValue(unit, out int uidx))
                {
                    uidx = snapshot.Units.Count;
                    unitRefIds[unit] = uidx;
                    snapshot.Units.Add(new UnitSnapshot
                    {
                        UnitName = unit.UnitName,
                        Team = unit.Team,
                        CoherencyInch = unit.CoherencyInch,
                        MoveInch = unit.MoveInch,
                        IsToken = unit.IsToken,
                        CanMove = unit.CanMove,
                    });
                }
                snapshot.Units[uidx].Models.Add(new ModelSnapshot
                {
                    Center = piece.Center,
                    RotationDegrees = piece.RotationDegrees,
                    SizeMm = piece.SizeMm,
                    FillColor = piece.FillColor,
                    Damage = piece.Damage,
                    IsDisplacement = piece.IsDisplacement,
                    Memo = piece.Memo,
                });
            }

            foreach (var kv in _unitRanges)
            {
                if (!unitRefIds.TryGetValue(kv.Key, out int uidx))
                {
                    continue; // 보드에 모델이 하나도 없는 유닛(방금 마지막 모델이 지워짐) — 스냅샷에서 뺀다.
                }
                snapshot.Ranges.Add(new RangeSnapshot { UnitRef = uidx, Ranges = new List<RangeSpec>(kv.Value) });
            }

            foreach (var def in _pendingUnits)
            {
                snapshot.PendingUnits.Add(ClonePendingUnitDef(def));
            }

            foreach (var def in _pendingRosterTokens)
            {
                snapshot.PendingRosterTokens.Add(ClonePendingTokenDef(def));
            }

            if (markerLayer != null)
            {
                for (int i = 0; i < markerLayer.childCount; i++)
                {
                    var markerGo = markerLayer.GetChild(i).gameObject;
                    // 배치 미리보기(반투명 고스트)는 실제 마커가 아니므로 스냅샷에서 뺀다.
                    if (_markerPlacementPreview != null && markerGo == _markerPlacementPreview.gameObject)
                    {
                        continue;
                    }
                    if (markerGo.TryGetComponent<ActivationMarker>(out var act))
                    {
                        snapshot.Markers.Add(new MarkerSnapshot { Kind = "activation", Center = act.Center, State = act.State });
                    }
                    else if (markerGo.TryGetComponent<CaptureMarker>(out var cap))
                    {
                        snapshot.Markers.Add(new MarkerSnapshot { Kind = "capture", Center = cap.Center, State = cap.ColorState });
                    }
                    else if (markerGo.TryGetComponent<IconMarker>(out var icon))
                    {
                        snapshot.Markers.Add(new MarkerSnapshot { Kind = icon.Kind, Center = icon.Center, State = "" });
                    }
                }
            }

            return snapshot;
        }

        private void RestoreBoardSnapshot(BoardSnapshot snapshot)
        {
            for (int i = baseLayer.childCount - 1; i >= 0; i--)
            {
                Destroy(baseLayer.GetChild(i).gameObject);
            }
            _pieces.Clear();

            if (markerLayer != null)
            {
                for (int i = markerLayer.childCount - 1; i >= 0; i--)
                {
                    var child = markerLayer.GetChild(i);
                    if (_markerPlacementPreview != null && child.gameObject == _markerPlacementPreview.gameObject)
                    {
                        continue; // 배치 미리보기는 스냅샷 대상이 아니었으니 복원 때도 안 지운다.
                    }
                    Destroy(child.gameObject);
                }
            }

            // 지워진 베이스/유닛을 참조하던 값들을 전부 정리 — 복원 뒤에도 남아있으면
            // 파괴된 인스턴스를 가리키는 참조가 된다.
            _hoveredBase = null;
            _hoveredUnit = null;
            _menuTarget = null;
            _rangeTargetUnit = null;
            _rangeDeleteTargetUnit = null;
            _unitRanges.Clear();
            _rosterTokenUnits.Clear();
            _draggingPiece = null;
            _draggingFollower = null;
            _draggingMarker = null;

            var restoredUnits = new List<Unit>();
            foreach (var unitSnap in snapshot.Units)
            {
                var unit = new Unit
                {
                    UnitName = unitSnap.UnitName,
                    Team = unitSnap.Team,
                    CoherencyInch = unitSnap.CoherencyInch,
                    MoveInch = unitSnap.MoveInch,
                    IsToken = unitSnap.IsToken,
                    CanMove = unitSnap.CanMove,
                };
                restoredUnits.Add(unit);
                if (unit.IsToken)
                {
                    _rosterTokenUnits[$"{unit.Team}|{unit.UnitName}"] = unit;
                }

                foreach (var modelSnap in unitSnap.Models)
                {
                    var piece = CreatePieceObject(unit, modelSnap.SizeMm, modelSnap.FillColor, modelSnap.IsDisplacement);
                    piece.Damage = modelSnap.Damage;
                    piece.Memo = modelSnap.Memo;
                    piece.Center = modelSnap.Center;
                    piece.RotationDegrees = modelSnap.RotationDegrees;
                    piece.Refresh();
                    unit.Models.Add(piece);
                }
            }

            foreach (var rangeSnap in snapshot.Ranges)
            {
                var unit = restoredUnits[rangeSnap.UnitRef];
                _unitRanges[unit] = new List<RangeSpec>(rangeSnap.Ranges);
            }

            _pendingUnits.Clear();
            foreach (var def in snapshot.PendingUnits)
            {
                _pendingUnits.Add(ClonePendingUnitDef(def));
            }
            RefreshPendingList();

            _pendingRosterTokens.Clear();
            foreach (var def in snapshot.PendingRosterTokens)
            {
                _pendingRosterTokens.Add(ClonePendingTokenDef(def));
            }
            RefreshRosterTokenList();

            if (markerLayer != null)
            {
                foreach (var markerSnap in snapshot.Markers)
                {
                    MarkerBase marker = CreateMarkerObject(markerSnap.Kind, markerLayer);
                    switch (markerSnap.Kind)
                    {
                        case "activation":
                            ((ActivationMarker)marker).SetState(markerSnap.State);
                            marker.RightClicked += OnActivationMarkerRightClicked;
                            break;
                        case "capture":
                            ((CaptureMarker)marker).SetColorState(markerSnap.State);
                            marker.RightClicked += OnCaptureMarkerRightClicked;
                            break;
                        default:
                            marker.RightClicked += OnIconMarkerRightClicked;
                            break;
                    }
                    marker.Center = markerSnap.Center;
                    marker.DragRequested += OnMarkerDragRequested;
                }
            }

            RefreshRangeOverlays();
        }

        private static PendingUnitDef ClonePendingUnitDef(PendingUnitDef def)
        {
            return new PendingUnitDef
            {
                Name = def.Name,
                Team = def.Team,
                ModelCount = def.ModelCount,
                SizeMm = def.SizeMm,
                FillColor = def.FillColor,
                MoveInch = def.MoveInch,
                CoherencyInch = def.CoherencyInch,
                CanMove = def.CanMove,
                IsDisplacement = def.IsDisplacement,
                Damages = new List<int>(def.Damages),
                Ranges = new List<RangeSpec>(def.Ranges),
            };
        }

        private static PendingTokenDef ClonePendingTokenDef(PendingTokenDef def)
        {
            return new PendingTokenDef
            {
                Name = def.Name,
                Team = def.Team,
                SizeMm = def.SizeMm,
                IsDisplacement = def.IsDisplacement,
                Ranges = new List<RangeSpec>(def.Ranges),
            };
        }

        private class UnitSnapshot
        {
            public string UnitName;
            public string Team;
            public float CoherencyInch;
            public float MoveInch;
            public bool IsToken;
            public bool CanMove;
            public readonly List<ModelSnapshot> Models = new List<ModelSnapshot>();
        }

        private class ModelSnapshot
        {
            public Vector2 Center;
            public float RotationDegrees;
            public Vector2 SizeMm;
            public Color FillColor;
            public int Damage;
            public bool IsDisplacement;
            public string Memo;
        }

        private class RangeSnapshot
        {
            public int UnitRef;
            public List<RangeSpec> Ranges;
        }

        private class MarkerSnapshot
        {
            public string Kind; // "activation" / "capture" / 그 외(아이콘 kind 문자열)
            public Vector2 Center;
            public string State; // activation: State 문자열, capture: ColorState 문자열, icon: 안 씀("")
        }

        private class BoardSnapshot
        {
            public readonly List<UnitSnapshot> Units = new List<UnitSnapshot>();
            public readonly List<RangeSnapshot> Ranges = new List<RangeSnapshot>();
            public readonly List<PendingUnitDef> PendingUnits = new List<PendingUnitDef>();
            public readonly List<PendingTokenDef> PendingRosterTokens = new List<PendingTokenDef>();
            public readonly List<MarkerSnapshot> Markers = new List<MarkerSnapshot>();
        }
    }
}
