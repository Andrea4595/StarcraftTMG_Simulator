using System.Collections.Generic;
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
        // 따로 막았다). 히스토리 패널만 별도의 항상-켜진 Canvas/Raycaster에
        // 올려서 그것만 계속 클릭 가능하게 한다(MultiplayerConnectDialog_Canvas/
        // Chat_Canvas와 같은 패턴). 패널 자체는 UndoHistoryDialog.cs와 같은
        // "떠있는 비독점 참고창" 모양을 재사용 — 사용자 요청(2026-09-03,
        // 처음엔 하단 << < > >> 바였다가 "되돌리기 창처럼 히스토리 리스트를
        // 띄워달라"고 재요청) — 항목을 직접 클릭해서 그 지점으로 점프하므로
        // 별도의 단계 이동/페이즈 점프 버튼은 필요 없어졌다(목록에서 원하는
        // 행을 바로 클릭하면 됨).

        private static readonly Color ReplayRowColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color ReplayCurrentRowColor = new Color(0.25f, 0.42f, 0.25f, 1f);

        private RectTransform _replayListContent;

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

            // 실제 입력 차단 지점 — 이 한 줄이 위 클래스 주석에서 설명한
            // "모든 클릭 기반 상태 변경 경로"를 전부 막는다.
            if (_mainRaycaster != null)
            {
                _mainRaycaster.enabled = false;
            }

            BuildReplayHistoryPanel();

            _replayMode = true;
            ShowReplayFrame(0);
        }

        /// <summary>index번째 프레임을 화면에 그린다 — RestoreBoardSnapshot을
        /// 직접 부른다(BeginUndoTransaction/CommitUndoTransaction으로 감싸지
        /// 않음). 감쌌다면 숨겨진 라이브 undo/redo 스택(UndoRedoService)이
        /// 재생 스텝마다 오염됐을 것이다 — 리플레이는 그 스택과 완전히
        /// 무관해야 한다.</summary>
        private void ShowReplayFrame(int index)
        {
            if (_replayFrames == null || _replayFrames.Count == 0)
            {
                return;
            }
            _replayFrameIndex = Mathf.Clamp(index, 0, _replayFrames.Count - 1);
            RestoreBoardSnapshot(_replayFrames[_replayFrameIndex].Snapshot);
            RefreshReplayList();
        }

        private void ReplayExit()
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(GameConstants.EntrySceneName);
        }

        /// <summary>UndoHistoryDialog.cs와 같은 "떠있는 비독점 참고창" 위치
        /// (플레이어 B 로스터 창 바로 왼쪽)에 짓는다 — 다만 그건 열고 닫는
        /// 모달인 반면 이건 리플레이 내내 계속 떠있는 채로 둔다(닫으면 재생
        /// 조작 수단이 아예 없어지므로).</summary>
        private void BuildReplayHistoryPanel()
        {
            var canvasGo = new GameObject("ReplayHistory_Canvas");
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
            panelRect.sizeDelta = new Vector2(GameConstants.PendingPanelWidth / 2f * 1.8f, 0f);
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

            // 되돌리기 창(260f)보다 조금 더 크게 — 여기선 이 목록 자체가
            // 유일한 재생 조작 수단이라 UndoHistoryDialog보다 화면을 더
            // 차지할 가치가 있다.
            _replayListContent = ScrollListUtil.Create(panelGo.transform, 400f, new Color(0f, 0f, 0f, 0.15f), out _, out _);

            var exitGo = new GameObject("Exit", typeof(RectTransform));
            exitGo.transform.SetParent(panelGo.transform, false);
            var exitLe = exitGo.AddComponent<LayoutElement>();
            exitLe.preferredHeight = 36f;
            var exitImg = exitGo.AddComponent<Image>();
            exitImg.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            var exitBtn = exitGo.AddComponent<Button>();
            exitBtn.onClick.AddListener(ReplayExit);
            var exitLabel = CreateReplayLabel(exitGo.transform, "나가기", 16f, FontStyles.Normal, Color.white);
            var exitLabelRect = (RectTransform)exitLabel.transform;
            exitLabelRect.anchorMin = Vector2.zero;
            exitLabelRect.anchorMax = Vector2.one;
            exitLabelRect.offsetMin = Vector2.zero;
            exitLabelRect.offsetMax = Vector2.zero;
            Destroy(exitLabel.GetComponent<LayoutElement>());
            exitLabel.alignment = TextAlignmentOptions.Center;
            exitLabel.raycastTarget = false;
        }

        /// <summary>목록을 통째로 다시 그린다 — 현재 보여주는 프레임 행만
        /// 다른 색으로 강조한다(UndoHistoryDialog의 isUndone/locked 배경
        /// 구분과 같은 자리, 다른 기준). 행을 클릭하면 그 프레임으로 곧장
        /// 점프한다 — 별도의 단계 이동 버튼 없이 이게 유일한 조작 수단.</summary>
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

                var itemGo = new GameObject("Row", typeof(RectTransform));
                itemGo.transform.SetParent(_replayListContent, false);
                var itemLe = itemGo.AddComponent<LayoutElement>();
                itemLe.preferredHeight = 30f;
                var itemBg = itemGo.AddComponent<Image>();
                itemBg.color = isCurrent ? ReplayCurrentRowColor : ReplayRowColor;
                var itemBtn = itemGo.AddComponent<Button>();
                itemBtn.targetGraphic = itemBg;
                itemBtn.onClick.AddListener(() => ShowReplayFrame(capturedIndex));

                var labelColor = string.IsNullOrEmpty(frame.Team) ? Color.white : GameConstants.ResolveTeamTextColor(frame.Team);
                var label = CreateReplayLabel(itemGo.transform, $"{i + 1}. {frame.Label}", 13f, FontStyles.Normal, labelColor);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
                Destroy(label.GetComponent<LayoutElement>());
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.raycastTarget = false;
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
    }
}
