using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 리플레이 모드(2026-09-03 신설, 같은 날 하단 바 → 히스토리 목록으로
        // 재구성) ──────────────────────────────────────────────────────────
        // Entry의 "리플레이" 버튼으로 저장 파일을 열면 여기로 들어온다.
        // 핵심 아이디어: GameBoard 씬 전체(맵/조각/마커/점수판/페이즈바/다이얼
        // 메뉴/모든 다이얼로그, undo 히스토리 모달까지)가 GameFlowBootstrap.
        // BuildGameBoard()가 만드는 단 하나의 Canvas/GraphicRaycaster
        // ("Base_Test_Canvas")에 얹혀 있다 — Unity의 포인터 이벤트
        // (IPointerDownHandler/Button.onClick)는 전부 이 라캐스터를 거쳐
        // 디스패치되므로, 그 라캐스터 하나를 끄면 그 아래 모든 클릭/드래그
        // 기반 상태 변경 경로가 한 번에 막힌다 — 조각/마커/미션마커의 자체
        // OnPointerDown, RadialMenu, 모든 Button, 택티컬 카드 클릭, 점수판/
        // 페이즈바까지 예외 없이 전부 포함(팬/줌/측정은 Input.*를 직접
        // 폴링해서 라캐스터와 무관하게 계속 동작 — 정확히 원하는 동작. 단
        // HandleEmptyClickFallthrough의 이모트 피커는 예외라 _replayMode로
        // 따로 막았다). 리플레이 전용 UI(히스토리 패널, 마커바 대체 버튼)는
        // 전부 별도의 항상-켜진 Canvas/Raycaster에 올려서 그것만 계속 클릭
        // 가능하게 한다(MultiplayerConnectDialog_Canvas/Chat_Canvas와 같은
        // 패턴) — 원래 마커바는 메인 캔버스 아래라 라캐스터를 끄면 그 자식으로
        // 새로 지은 버튼도 같이 막히므로, 자식 재구성이 아니라 바 전체를
        // 숨기고 새 캔버스에 새로 짓는 방식을 쓴다(2026-09-05, 백로그 항목 1
        // 구현 중 발견).

        private static readonly Color ReplayRowColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color ReplayCurrentRowColor = new Color(0.25f, 0.42f, 0.25f, 1f);
        private static readonly Color ReplayHeaderTextColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        private const float ReplayNoColumnWidth = 26f;
        private const float ReplayToggleColumnWidth = 28f;
        // 되돌리기 창(260f)보다 크게 잡았던 예전 폭(252f)으로는 "내용" 글자가
        // 조금만 길어도 출력 체크박스 칸이 같이 밀려 보이는 문제가 있었다
        // (사용자 보고, 2026-09-05) — 훨씬 넉넉하게 잡는다.
        private const float ReplayPanelWidth = 400f;

        private RectTransform _replayListContent;
        // _replayFrames와 같은 길이/순서로 "출력(GIF에 포함)" 체크 상태를
        // 담는다 — 기본 전부 체크(사용자 요청, 2026-09-05 백로그 항목 3).
        private List<bool> _replayIncludeInGif;

        /// <summary>GameFlowBootstrap.BuildGameBoard()가 항상 넘겨준다 — 실제로
        /// 쓰이는지(끄는지)는 GameLoadRequest.IsReplayLoad를 본 뒤 여기
        /// BoardManager 쪽이 판단한다(다른 Configure* 메서드들과 같은
        /// 패턴).</summary>
        internal void ConfigureReplay(GraphicRaycaster mainRaycaster)
        {
            _mainRaycaster = mainRaycaster;
        }

        /// <summary>BoardManager.Start()가 ApplyLoadedLiveState 직후, 그
        /// 라이브 상태를 편집 가능한 채로 두는 대신 부른다. pendingData는
        /// 방금 그 라이브 상태를 지은 바로 그 저장 트리 — "undo_history"
        /// 키를 다시 읽어 재생 타임라인을 만든다.</summary>
        private void EnterReplayMode(Dictionary<string, object> pendingData)
        {
            // 방금 ApplyLoadedLiveState가 지은 최종 상태를 마지막 프레임으로
            // 재사용한다 — 새 렌더링 경로를 안 만들어도 됨.
            var finalSnapshot = CaptureBoardSnapshot();

            var undoHistoryRoot = GameSaveIO.GetDict(pendingData, "undo_history");
            _replayFrames = UndoRedoService.ParseOrderedUndoStack(undoHistoryRoot);
            _replayFrames.Add((finalSnapshot, "현재 상태", ""));

            _replayIncludeInGif = new List<bool>(_replayFrames.Count);
            for (int i = 0; i < _replayFrames.Count; i++)
            {
                _replayIncludeInGif.Add(true);
            }

            // 실제 입력 차단 지점 — 이 한 줄이 위 클래스 주석에서 설명한
            // "모든 클릭 기반 상태 변경 경로"를 전부 막는다.
            if (_mainRaycaster != null)
            {
                _mainRaycaster.enabled = false;
            }

            // 원래 마커바는 방금 끈 라캐스터 아래라 그대로 두면 화면엔
            // 여전히 보이지만 아무 것도 눌리지 않는다 — 통째로 숨기고, 나가기
            // +GIF 버튼만 별도의 항상-켜진 캔버스에 새로 짓는다.
            if (_markerBarRect != null)
            {
                _markerBarRect.gameObject.SetActive(false);
            }
            BuildReplayMarkerBar();

            BuildReplayHistoryPanel();

            _replayMode = true;
            // -1로 시작해야 첫 ShowReplayFrame(0) 호출이 "이전 프레임 없음"으로
            // 판단해 반드시 완전히 다시 그린다 — 필드 기본값 0을 그대로
            // 두면 "0번 프레임을 0번 프레임과 비교"가 돼 그 시점의 진짜
            // 라이브 상태(막 불러온 저장의 최종 상태)를 0번 프레임과 같다고
            // 잘못 판단해 건너뛸 위험이 있다(RestoreBoardSnapshotForReplay
            // 참고).
            _replayFrameIndex = -1;
            ShowReplayFrame(0);
        }

        /// <summary>index번째 프레임을 화면에 그린다 — RestoreBoardSnapshotForReplay를
        /// 직접 부른다(BeginUndoTransaction/CommitUndoTransaction으로 감싸지
        /// 않음). 감쌌다면 숨겨진 라이브 undo/redo 스택(UndoRedoService)이
        /// 재생 스텝마다 오염됐을 것이다 — 리플레이는 그 스택과 완전히
        /// 무관해야 한다. 직전에 보여주던 프레임의 스냅샷도 같이 넘겨서
        /// (2026-09-09, 속도 개선 — 사용자 보고 "스냅샷 넘어가는게 꽤
        /// 느리다") 유닛/마커가 실제로 안 바뀌었으면 destroy-and-rebuild를
        /// 건너뛸 수 있게 한다.</summary>
        private void ShowReplayFrame(int index)
        {
            if (_replayFrames == null || _replayFrames.Count == 0)
            {
                return;
            }
            BoardSnapshot previousShown = (_replayFrameIndex >= 0 && _replayFrameIndex < _replayFrames.Count)
                    ? _replayFrames[_replayFrameIndex].Snapshot
                    : null;
            _replayFrameIndex = Mathf.Clamp(index, 0, _replayFrames.Count - 1);
            RestoreBoardSnapshotForReplay(_replayFrames[_replayFrameIndex].Snapshot, previousShown);
            RefreshReplayList();
        }

        // ── 리플레이 스텝 속도 개선(2026-09-09, 사용자 보고 "스냅샷
        // 넘어가는게 꽤 느린데") ────────────────────────────────────────────
        // RestoreBoardSnapshot(정확히는 그중 RestoreUnitsAndMarkers, 실제
        // GameObject를 destroy-and-rebuild하는 부분)이 라이브 되돌리기와
        // 리플레이 둘 다에서 매 스텝 무조건 다시 실행되던 게 느림의 원인
        // 이었다 — 유닛이 많을수록, 리플레이를 빠르게 넘길수록 그대로
        // 누적된다. 라이브 되돌리기 쪽(카스케이드가 같은 프레임에 여러 번
        // 연달아 부를 수 있고, 과거 "유닛 복제" 버그를 막으려고 일부러
        // DestroyImmediate를 쓰는 등 타이밍이 예민함)은 전혀 안 건드리고,
        // 리플레이가 프레임을 넘길 때만 쓰는 이 경로에서만 "직전에 보여준
        // 프레임과 유닛/사거리/마커가 완전히 똑같으면 그 destroy-and-rebuild
        // 자체를 건너뛴다"는 최적화를 적용한다 — 예비대/전술카드 목록이나
        // 라운드/VP/페이즈 같은 값 대입뿐인 RestoreNonBoardState는 원래도
        // 싸므로 항상 그대로 실행한다.
        //
        // 실질적인 한계: 이 스텝에서 유닛이 하나라도 옮겨지거나 모델이
        // 추가/제거되는 등 "진짜 바뀌는" 액션이면 비교가 실패해 여전히
        // 전체 재생성이 일어난다 — 라운드/VP/페이즈/활성 플레이어 전환처럼
        // 보드 자체는 안 건드리는 액션들 사이를 넘어갈 때만 이 최적화의
        // 효과를 본다. 유닛별로 안정적인 식별자가 멀티플레이 브로드캐스트를
        // 한 번도 안 탄 솔로 플레이 유닛에는 없어서(NetworkUnitId가 항상
        // -1), 그런 유닛까지 진짜 diff(이동만 값 갱신, 재생성 안 함)하려면
        // 훨씬 큰 변경이 필요하다 — 지금은 안전하게 범위를 좁혔다.
        internal void RestoreBoardSnapshotForReplay(BoardSnapshot snapshot, BoardSnapshot previousShown)
        {
            if (previousShown == null || !UnitsAndMarkersUnchangedForReplay(previousShown, snapshot))
            {
                RestoreUnitsAndMarkers(snapshot);
            }
            RestoreNonBoardState(snapshot);
        }

        private static bool UnitsAndMarkersUnchangedForReplay(BoardSnapshot a, BoardSnapshot b)
        {
            return UnitSnapshotListsEqual(a.Units, b.Units)
                    && RangeSnapshotListsEqual(a.Ranges, b.Ranges)
                    && MarkerSnapshotListsEqual(a.Markers, b.Markers);
        }

        private static bool UnitSnapshotListsEqual(List<UnitSnapshot> a, List<UnitSnapshot> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!UnitSnapshotEqual(a[i], b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool UnitSnapshotEqual(UnitSnapshot a, UnitSnapshot b)
        {
            if (a.NetworkUnitId != b.NetworkUnitId || a.UnitName != b.UnitName || a.Team != b.Team
                    || a.CoherencyInch != b.CoherencyInch || a.MoveInch != b.MoveInch || a.IsToken != b.IsToken
                    || a.CanMove != b.CanMove || a.SupplyOverride != b.SupplyOverride
                    || !ReferenceEquals(a.Detail, b.Detail))
            {
                return false;
            }
            if (a.SupplyTiers.Count != b.SupplyTiers.Count)
            {
                return false;
            }
            for (int i = 0; i < a.SupplyTiers.Count; i++)
            {
                var ta = a.SupplyTiers[i];
                var tb = b.SupplyTiers[i];
                if (ta.ModelMin != tb.ModelMin || ta.ModelMax != tb.ModelMax || ta.Supply != tb.Supply || ta.Pts != tb.Pts)
                {
                    return false;
                }
            }
            if (a.Models.Count != b.Models.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Models.Count; i++)
            {
                if (!ModelSnapshotEqual(a.Models[i], b.Models[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ModelSnapshotEqual(ModelSnapshot a, ModelSnapshot b)
        {
            return a.Center == b.Center && a.RotationDegrees == b.RotationDegrees && a.SizeMm == b.SizeMm
                    && a.FillColor.Equals(b.FillColor) && a.Damage == b.Damage && a.IsDisplacement == b.IsDisplacement
                    && a.Memo == b.Memo;
        }

        private static bool RangeSnapshotListsEqual(List<RangeSnapshot> a, List<RangeSnapshot> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                var ra = a[i];
                var rb = b[i];
                if (ra.UnitRef != rb.UnitRef || ra.Ranges.Count != rb.Ranges.Count)
                {
                    return false;
                }
                for (int j = 0; j < ra.Ranges.Count; j++)
                {
                    var sa = ra.Ranges[j];
                    var sb = rb.Ranges[j];
                    if (sa.Inch != sb.Inch || sa.AlwaysShow != sb.AlwaysShow)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool MarkerSnapshotListsEqual(List<MarkerSnapshot> a, List<MarkerSnapshot> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                var ma = a[i];
                var mb = b[i];
                if (ma.Kind != mb.Kind || ma.Center != mb.Center || ma.State != mb.State
                        || ma.NetworkMarkerId != mb.NetworkMarkerId)
                {
                    return false;
                }
            }
            return true;
        }

        private void ReplayExit()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.EntrySceneName);
        }

        // ── 리플레이용 마커바 대체(2026-09-05, 백로그 항목 1) ───────────────

        /// <summary>원래 마커바와 같은 자리(화면 하단, 왼쪽부터)에 나가기+GIF
        /// 버튼만 있는 얇은 바를 새로 짓는다 — 전용 캔버스라 메인 캔버스의
        /// 꺼진 라캐스터와 무관하게 계속 클릭 가능하다. 평소 나가기 버튼과
        /// 달리 확인 창 없이 곧장 나간다(리플레이는 잃을 진행 상황이 없다).</summary>
        // GIF 캡처 중에는 이 바 자체를 숨긴다(사용자 요청, 2026-09-05) — 화면에
        // 그대로 찍히면 안 되는 리플레이 전용 UI라서.
        private GameObject _replayMarkerBarCanvasGo;

        private void BuildReplayMarkerBar()
        {
            var canvasGo = new GameObject("ReplayMarkerBar_Canvas");
            _replayMarkerBarCanvasGo = canvasGo;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // ReplayHistory_Canvas와 같은 층.
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var barGo = new GameObject("Bar", typeof(RectTransform));
            barGo.transform.SetParent(canvasGo.transform, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            barRect.sizeDelta = new Vector2(0f, GameConstants.MarkerBarHeight);

            var bg = barGo.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);

            CreateReplayExitButton(barRect);
            CreateReplayGifButton(barRect);
        }

        private void CreateReplayExitButton(Transform parent)
        {
            var go = new GameObject("ReplayExitButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/ExitButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(ReplayExit);
        }

        private void CreateReplayGifButton(Transform parent)
        {
            var go = new GameObject("GifButton", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(10f + GameConstants.MarkerBarHeight, 0f);
            rect.sizeDelta = new Vector2(GameConstants.MarkerBarHeight - 8f, GameConstants.MarkerBarHeight - 8f);

            var img = go.AddComponent<RawImage>();
            img.texture = Resources.Load<Texture2D>("UI/GifButton");

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(StartGifExport);
        }

        // ── 히스토리 패널(2026-09-05, 백로그 항목 2/3으로 개편) ─────────────

        /// <summary>UndoHistoryDialog.cs와 같은 "떠있는 비독점 참고창" 위치
        /// (플레이어 B 로스터 창 바로 왼쪽)에 짓는다 — 다만 그건 열고 닫는
        /// 모달인 반면 이건 리플레이 내내 계속 떠있는 채로 둔다. "나가기"
        /// 버튼은 여기 없다(2026-09-05 제거) — 마커바 쪽에 새로 생겼으니
        /// 중복이다.</summary>
        // GIF 캡처 중에는 이 패널도 숨긴다(사용자 요청, 2026-09-05).
        private GameObject _replayHistoryPanelCanvasGo;

        private void BuildReplayHistoryPanel()
        {
            var canvasGo = new GameObject("ReplayHistory_Canvas");
            _replayHistoryPanelCanvasGo = canvasGo;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50; // 메인 게임판 캔버스보다 위, DontDestroyOnLoad 오버레이(Chat 등)보단 아래.
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            const float PanelGapPx = 12f;
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-(GameConstants.PendingPanelWidth + PanelGapPx), -(GameConstants.TopBarHeight + PanelGapPx));
            panelRect.sizeDelta = new Vector2(ReplayPanelWidth, 0f);
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);

            var layout = panelGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var panelFitter = panelGo.AddComponent<ContentSizeFitter>();
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleLabel = CreateReplayLabel(panelGo.transform, "리플레이", 16f, FontStyles.Bold, Color.white);
            titleLabel.alignment = TextAlignmentOptions.Center;
            titleLabel.GetComponent<LayoutElement>().preferredHeight = 22f;

            BuildReplayListHeader(panelGo.transform);

            // 되돌리기 창(260f)보다 조금 더 크게 — 여기선 이 목록 자체가
            // 유일한 재생 조작 수단이라 UndoHistoryDialog보다 화면을 더
            // 차지할 가치가 있다.
            _replayListContent = ScrollListUtil.Create(panelGo.transform, 400f, new Color(0f, 0f, 0f, 0.15f), out _, out _);
        }

        /// <summary>스크롤 목록 바로 위, 컬럼 이름표 한 줄 — RefreshReplayList의
        /// 각 행과 정확히 같은 중첩 구조(바깥 No.+내용 묶음 / 별도 칸의 출력)를
        /// 그대로 따라야 컬럼 경계가 픽셀 단위로 맞는다(바깥 레이아웃의
        /// spacing/padding이 하나라도 다르면 아래로 갈수록 어긋나 보인다).</summary>
        private void BuildReplayListHeader(Transform parent)
        {
            var headerGo = new GameObject("ListHeader", typeof(RectTransform));
            headerGo.transform.SetParent(parent, false);
            var headerLe = headerGo.AddComponent<LayoutElement>();
            headerLe.preferredHeight = 20f;
            var headerLayout = headerGo.AddComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 4f;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = true;

            var labelAreaGo = new GameObject("LabelArea", typeof(RectTransform));
            labelAreaGo.transform.SetParent(headerGo.transform, false);
            var labelAreaLe = labelAreaGo.AddComponent<LayoutElement>();
            labelAreaLe.flexibleWidth = 1f;
            var labelAreaLayout = labelAreaGo.AddComponent<HorizontalLayoutGroup>();
            labelAreaLayout.padding = new RectOffset(10, 6, 0, 0);
            labelAreaLayout.spacing = 6f;
            labelAreaLayout.childControlWidth = true;
            labelAreaLayout.childControlHeight = true;
            labelAreaLayout.childForceExpandWidth = false;
            labelAreaLayout.childForceExpandHeight = true;

            var noGo = new GameObject("No", typeof(RectTransform));
            noGo.transform.SetParent(labelAreaGo.transform, false);
            var noLe = noGo.AddComponent<LayoutElement>();
            noLe.preferredWidth = ReplayNoColumnWidth;
            AddColumnLabel(noGo, "No.", 11f, FontStyles.Bold, ReplayHeaderTextColor, TextAlignmentOptions.MidlineLeft);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(labelAreaGo.transform, false);
            var contentLe = contentGo.AddComponent<LayoutElement>();
            // preferredWidth를 명시적으로 0으로 박아둔다 — 안 그러면
            // TextMeshProUGUI 자신도 ILayoutElement라 "내용" 텍스트의 실제
            // 폭을 preferred로 보고하는데, LayoutElement.preferredWidth가
            // 기본값(-1, "미설정")이면 그 값이 그대로 새어나가 레이아웃
            // 그룹이 그걸 기준으로 계산해버린다(2026-09-05 버그 원인).
            contentLe.preferredWidth = 0f;
            contentLe.flexibleWidth = 1f;
            AddColumnLabel(contentGo, "내용", 11f, FontStyles.Bold, ReplayHeaderTextColor, TextAlignmentOptions.MidlineLeft);

            var toggleGo = new GameObject("Include", typeof(RectTransform));
            toggleGo.transform.SetParent(headerGo.transform, false);
            var toggleLe = toggleGo.AddComponent<LayoutElement>();
            toggleLe.preferredWidth = ReplayToggleColumnWidth;
            AddColumnLabel(toggleGo, "출력", 11f, FontStyles.Bold, ReplayHeaderTextColor, TextAlignmentOptions.Center);
        }

        /// <summary>목록을 통째로 다시 그린다 — 현재 보여주는 프레임 행만 다른
        /// 색으로 강조한다. 각 행은 No./내용/출력(체크박스) 세 컬럼으로
        /// 나뉜다(2026-09-05, 백로그 항목 3) — No.+내용 칸에만 Button을 올려
        /// 그 프레임으로 점프하게 하고, 체크박스는 별도 칸에 둬서 두 클릭
        /// 영역이 서로 안 겹치게 한다. GIF 내보내기 진행 중에는 점프 클릭을
        /// 막는다(재생 중인 프레임을 사용자가 멋대로 바꾸면 캡처 순서가
        /// 엉킨다) — 체크박스는 그대로 둬도 된다(내보낼 프레임 목록은 시작
        /// 시점에 이미 스냅샷됐으므로).</summary>
        private void RefreshReplayList()
        {
            if (_replayListContent == null)
            {
                return;
            }
            for (int i = _replayListContent.childCount - 1; i >= 0; i--)
            {
                Destroy(_replayListContent.GetChild(i).gameObject);
            }

            for (int i = 0; i < _replayFrames.Count; i++)
            {
                int capturedIndex = i;
                var frame = _replayFrames[i];
                bool isCurrent = i == _replayFrameIndex;
                var labelColor = string.IsNullOrEmpty(frame.Team) ? Color.white : GameConstants.ResolveTeamTextColor(frame.Team);

                var rowGo = new GameObject("Row", typeof(RectTransform));
                rowGo.transform.SetParent(_replayListContent, false);
                var rowLe = rowGo.AddComponent<LayoutElement>();
                rowLe.preferredHeight = 30f;
                var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 4f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;

                var jumpGo = new GameObject("JumpArea", typeof(RectTransform));
                jumpGo.transform.SetParent(rowGo.transform, false);
                var jumpLe = jumpGo.AddComponent<LayoutElement>();
                jumpLe.flexibleWidth = 1f;
                var jumpBg = jumpGo.AddComponent<Image>();
                jumpBg.color = isCurrent ? ReplayCurrentRowColor : ReplayRowColor;
                var jumpBtn = jumpGo.AddComponent<Button>();
                jumpBtn.targetGraphic = jumpBg;
                jumpBtn.onClick.AddListener(() =>
                {
                    if (_gifExportPhase != GifExportPhase.Idle)
                    {
                        return;
                    }
                    ShowReplayFrame(capturedIndex);
                });

                var jumpLayout = jumpGo.AddComponent<HorizontalLayoutGroup>();
                jumpLayout.padding = new RectOffset(10, 6, 0, 0);
                jumpLayout.spacing = 6f;
                jumpLayout.childControlWidth = true;
                jumpLayout.childControlHeight = true;
                jumpLayout.childForceExpandWidth = false;
                jumpLayout.childForceExpandHeight = true;

                var noGo = new GameObject("No", typeof(RectTransform));
                noGo.transform.SetParent(jumpGo.transform, false);
                var noLe = noGo.AddComponent<LayoutElement>();
                noLe.preferredWidth = ReplayNoColumnWidth;
                AddColumnLabel(noGo, $"{i + 1}", 13f, FontStyles.Normal, labelColor, TextAlignmentOptions.MidlineLeft);

                var contentGo = new GameObject("Content", typeof(RectTransform));
                contentGo.transform.SetParent(jumpGo.transform, false);
                var contentLe = contentGo.AddComponent<LayoutElement>();
                // 헤더의 같은 칸과 이유 동일(preferredWidth 미설정 시 TMP의
                // 텍스트 길이가 새어나가 출력 체크박스 칸까지 밀어낸다) —
                // 여기에 더해, 그래도 칸보다 긴 텍스트는 잘라내고 "..."로
                // 표시한다(Ellipsis) — 자르지 않으면 글자가 체크박스 칸
                // 위로 그냥 넘쳐 보인다.
                contentLe.preferredWidth = 0f;
                contentLe.flexibleWidth = 1f;
                var contentLabel = AddColumnLabel(contentGo, frame.Label, 13f, FontStyles.Normal, labelColor, TextAlignmentOptions.MidlineLeft);
                // Ellipsis였다가 되돌림(2026-09-05) — 이 프로젝트 폰트
                // (PretendardVariable SDF)에 "…" 글리프가 없어서 TMP가 매번
                // Truncate로 강제 전환하며 경고를 로그에 쏟아냈다(ScrollRect.
                // LateUpdate가 매 프레임 이 텍스트의 preferredHeight를 다시
                // 재는 과정에서 반복 트리거됨). 처음부터 Truncate를 쓰면
                // 그 폴백 자체가 필요 없어 경고도 안 난다.
                contentLabel.overflowMode = TextOverflowModes.Truncate;

                var toggleColumnGo = new GameObject("IncludeColumn", typeof(RectTransform));
                toggleColumnGo.transform.SetParent(rowGo.transform, false);
                var toggleColumnLe = toggleColumnGo.AddComponent<LayoutElement>();
                toggleColumnLe.preferredWidth = ReplayToggleColumnWidth;
                var toggle = CreateReplayIncludeToggle(toggleColumnGo.transform, _replayIncludeInGif[i]);
                toggle.onValueChanged.AddListener(v => _replayIncludeInGif[capturedIndex] = v);
            }
        }

        private static TextMeshProUGUI CreateReplayLabel(Transform parent, string text, float fontSize, FontStyles style, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>();
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>HorizontalLayoutGroup이 이미 크기를 정해주는 칸(go)에다
        /// 텍스트를 바로 붙인다 — CreateReplayLabel처럼 자식 GO를 새로 만들면
        /// 그 자식은 레이아웃 그룹이 직접 관리하는 대상이 아니라 앵커를 손으로
        /// 늘려줘야 하는데, 헤더/행의 No.·내용·출력 칸은 이미 자기 자신이
        /// 레이아웃 그룹의 자식이라 이렇게 붙이는 쪽이 더 간단하다.</summary>
        private static TextMeshProUGUI AddColumnLabel(GameObject go, string text, float fontSize, FontStyles style, Color color, TextAlignmentOptions alignment)
        {
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>RangeInputDialog.CreateToggle과 같은 배경+체크마크 구성이지만
        /// 라벨 텍스트가 없다(칸 자체가 좁고, 헤더에 이미 "출력"이 있어 각 행에
        /// 또 적을 필요가 없다) — 위치는 부모 칸 중앙에 고정.</summary>
        private static Toggle CreateReplayIncludeToggle(Transform parent, bool initialOn)
        {
            var go = new GameObject("Toggle", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var toggle = go.AddComponent<Toggle>();

            var bgGo = new GameObject("Background", typeof(RectTransform));
            bgGo.transform.SetParent(go.transform, false);
            var bgRect = (RectTransform)bgGo.transform;
            bgRect.anchorMin = new Vector2(0.5f, 0.5f);
            bgRect.anchorMax = new Vector2(0.5f, 0.5f);
            bgRect.pivot = new Vector2(0.5f, 0.5f);
            bgRect.sizeDelta = new Vector2(18f, 18f);
            var bgImage = bgGo.AddComponent<Image>();
            bgImage.color = new Color(0.3f, 0.3f, 0.3f, 1f);

            var checkGo = new GameObject("Checkmark", typeof(RectTransform));
            checkGo.transform.SetParent(bgGo.transform, false);
            var checkRect = (RectTransform)checkGo.transform;
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(3f, 3f);
            checkRect.offsetMax = new Vector2(-3f, -3f);
            var checkImage = checkGo.AddComponent<Image>();
            checkImage.color = new Color(0.4f, 0.85f, 0.4f, 1f);

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.isOn = initialOn;
            return toggle;
        }

        // ── GIF 내보내기(2026-09-05, 백로그 항목 4) ─────────────────────────
        // 코루틴 없이(이 프로젝트 관례) Update()로 폴링하는 상태 머신 —
        // WaitingRender는 ShowReplayFrame으로 화면을 바꾼 뒤 백버퍼가 실제로
        // 그 프레임을 반영할 시간을 한 틱 벌어주는 대기 상태, Capturing에서
        // 실제로 캡처한다. 체크 해제된(출력 안 함) 프레임은 목록에서 아예
        // 건너뛴다 — 시작 시점에 포함될 인덱스만 미리 뽑아둔다.
        private enum GifExportPhase { Idle, WaitingRender, Capturing }
        private GifExportPhase _gifExportPhase = GifExportPhase.Idle;
        private List<int> _gifIncludedFrameIndices;
        private List<GifEncoder.Frame> _gifCapturedFrames;
        private int _gifExportCursor;
        private int _gifExportReturnFrameIndex;
        private int _gifExportWidth;
        private int _gifExportHeight;

        // GIF 전체 길이 10초, 그중 마지막 장면에 3초를 더 얹는다(사용자 요청,
        // 2026-09-05) — 나머지 7초를 "전체 프레임 수"로 균등하게 나눈 값을
        // 모든 프레임(마지막 포함)의 기본 지속시간으로 쓰고, 마지막 프레임만
        // 그 위에 3초를 추가한다. 예: 프레임 10개면 기본 0.7초씩 + 마지막은
        // 0.7+3=3.7초 → 합계 9*0.7+3.7=10초.
        private const float GifTotalDurationSeconds = 10f;
        private const float GifLastFrameExtraSeconds = 3f;
        // ScreenCapture의 superSize(3D 카메라 대상 없어서 무효)와 캡처 중만
        // 메인 캔버스를 ScreenSpaceCamera로 바꿔 RenderTexture에 렌더하는
        // 방식(캡처가 끝난 뒤에도 리플레이 히스토리 패널의 렌더 순서가
        // 영구히 망가지는 새 버그를 냄) 둘 다 시도했다가 되돌렸다
        // (2026-09-05). 애초에 문제의 원인이 해상도(픽셀 수)가 아니라
        // GIF 팔레트 양자화 품질이었다는 게 이후 확인돼서, 캡처는 다시
        // 화면 실제 해상도 그대로 찍는 단순한 방식으로 되돌리고 품질은
        // GifEncoder의 팔레트 쪽에서 개선한다.

        private void StartGifExport()
        {
            if (_gifExportPhase != GifExportPhase.Idle || _replayFrames == null || _replayFrames.Count == 0)
            {
                return;
            }

            _gifIncludedFrameIndices = new List<int>();
            for (int i = 0; i < _replayFrames.Count; i++)
            {
                if (_replayIncludeInGif[i])
                {
                    _gifIncludedFrameIndices.Add(i);
                }
            }
            if (_gifIncludedFrameIndices.Count == 0)
            {
                return; // 체크된 프레임이 하나도 없음 — 내보낼 게 없다.
            }

            // 캡처에 안 보여야 할 리플레이 전용 UI를 끈다(사용자 요청,
            // 2026-09-05) — 마커바/히스토리 패널은 별도 캔버스라 한 번만
            // 꺼두면 캡처 내내 유지된다. 택티컬 카드 어빌리티 설명은
            // ShowReplayFrameForGifCapture가 프레임을 넘길 때마다 다시
            // 숨긴다(RestoreBoardSnapshot이 매번 그 목록을 새로 짓기 때문 —
            // 아래 그 메서드 설명 참고). 지도는 한 번만 핏하게 맞춘다(팬/줌은
            // 프레임을 넘겨도 안 바뀐다).
            FitMapToView();
            if (_replayMarkerBarCanvasGo != null)
            {
                _replayMarkerBarCanvasGo.SetActive(false);
            }
            if (_replayHistoryPanelCanvasGo != null)
            {
                _replayHistoryPanelCanvasGo.SetActive(false);
            }
            _gifExportReturnFrameIndex = _replayFrameIndex;
            _gifCapturedFrames = new List<GifEncoder.Frame>(_gifIncludedFrameIndices.Count);
            _gifExportCursor = 0;
            ShowReplayFrameForGifCapture(_gifIncludedFrameIndices[0]);
            _gifExportPhase = GifExportPhase.WaitingRender;
        }

        /// <summary>택티컬 카드 목록 컨테이너를 훑어 "Abilities_"로 시작하는
        /// 자식(RenderTacticalCardAbilities가 카드 버튼 바로 아래에 짓는 능력
        /// 설명 묶음, BoardManager.Deployment.cs)만 켜고/끈다 — 이름 접두사로
        /// 구분하는 이유는 그 메서드가 참조를 따로 저장해두지 않아서다(이름
        /// 규칙만으로 충분히 구분 가능해서 그 파일을 손댈 필요는 없었다).</summary>
        private void SetTacticalCardAbilitiesVisible(bool visible)
        {
            if (_tacticalCardListContainers == null)
            {
                return;
            }
            foreach (var container in _tacticalCardListContainers.Values)
            {
                if (container == null)
                {
                    continue;
                }
                for (int i = 0; i < container.childCount; i++)
                {
                    var child = container.GetChild(i);
                    if (child.name.StartsWith("Abilities_"))
                    {
                        child.gameObject.SetActive(visible);
                    }
                }
            }
        }

        /// <summary>매 프레임 BoardInputController.RunFrame이 부른다(다른
        /// UpdateX 폴링 메서드들과 같은 자리, replay 모드 여부와 무관하게
        /// 항상 호출되지만 Idle이면 즉시 리턴한다).</summary>
        internal void UpdateGifExport()
        {
            switch (_gifExportPhase)
            {
                case GifExportPhase.WaitingRender:
                    _gifExportPhase = GifExportPhase.Capturing;
                    break;

                case GifExportPhase.Capturing:
                    CaptureCurrentGifFrame();
                    _gifExportCursor++;
                    if (_gifExportCursor >= _gifIncludedFrameIndices.Count)
                    {
                        FinishGifExport();
                    }
                    else
                    {
                        ShowReplayFrameForGifCapture(_gifIncludedFrameIndices[_gifExportCursor]);
                        _gifExportPhase = GifExportPhase.WaitingRender;
                    }
                    break;
            }
        }

        /// <summary>ShowReplayFrame은 RestoreBoardSnapshot을 거치는데, 그 안의
        /// RefreshTacticalCardList()가 매번 카드 능력 설명 GameObject를
        /// 통째로 새로 짓는다(BoardManager.UndoRedo.cs:445) — 그래서
        /// StartGifExport에서 한 번만 SetTacticalCardAbilitiesVisible(false)를
        /// 불러봐야 다음 프레임으로 넘어가는 순간 다시 나타난다(2026-09-05
        /// 사용자 보고로 발견). GIF 캡처 중 프레임을 넘길 때는 항상 이
        /// 래퍼를 거쳐서 매번 다시 숨긴다.</summary>
        private void ShowReplayFrameForGifCapture(int index)
        {
            ShowReplayFrame(index);
            SetTacticalCardAbilitiesVisible(false);
        }

        private void CaptureCurrentGifFrame()
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (_gifCapturedFrames.Count == 0)
            {
                _gifExportWidth = tex.width;
                _gifExportHeight = tex.height;
            }

            // Texture2D.GetPixels32()는 아래→위(bottom-up) 행 순서인데
            // GIF는 위→아래를 기대한다 — 뒤집지 않으면 뒤집힌 채로 재생된다.
            var bottomUp = tex.GetPixels32();
            var topDown = new Color32[bottomUp.Length];
            for (int y = 0; y < tex.height; y++)
            {
                int srcRow = tex.height - 1 - y;
                Array.Copy(bottomUp, srcRow * tex.width, topDown, y * tex.width, tex.width);
            }
            Destroy(tex);

            bool isLastFrame = _gifExportCursor == _gifIncludedFrameIndices.Count - 1;
            float baseSeconds = (GifTotalDurationSeconds - GifLastFrameExtraSeconds) / _gifIncludedFrameIndices.Count;
            float delaySeconds = isLastFrame ? baseSeconds + GifLastFrameExtraSeconds : baseSeconds;
            int delay = Mathf.Max(1, Mathf.RoundToInt(delaySeconds * 100f));
            _gifCapturedFrames.Add(new GifEncoder.Frame(topDown, delay));
        }

        private void FinishGifExport()
        {
            byte[] bytes = GifEncoder.Encode(_gifExportWidth, _gifExportHeight, _gifCapturedFrames);

            string dir = Path.Combine(AppPaths.ExeDirectory(), "Replays");
            Directory.CreateDirectory(dir);
            string fileName = $"replay_{DateTime.Now:yyMMddHHmmss}.gif";
            string path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, bytes);

            _gifCapturedFrames = null;
            _gifIncludedFrameIndices = null;
            _gifExportPhase = GifExportPhase.Idle;

            // 캡처용으로 꺼뒀던 리플레이 전용 UI를 되돌린다.
            if (_replayMarkerBarCanvasGo != null)
            {
                _replayMarkerBarCanvasGo.SetActive(true);
            }
            if (_replayHistoryPanelCanvasGo != null)
            {
                _replayHistoryPanelCanvasGo.SetActive(true);
            }
            SetTacticalCardAbilitiesVisible(true);

            ShowReplayFrame(_gifExportReturnFrameIndex);
            // BoardManager.Screenshot.cs의 기존 토스트를 그대로 재사용한다 —
            // 파일명/폴더를 받아 우측 하단에 잠깐 띄우고 클릭하면 폴더를 여는
            // 동작이 이미 스크린샷 폴더에 종속돼 있지 않고 매개변수로 받는다.
            ShowScreenshotToast(fileName, dir);
        }
    }
}
