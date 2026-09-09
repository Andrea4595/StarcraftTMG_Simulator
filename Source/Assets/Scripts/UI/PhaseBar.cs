using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>스코어보드 바로 아래에 붙는 페이즈 표시 바 — Movement Phase >
    /// Assault Phase > Combat Phase > Cleanup Phase 순서를 한 줄로 보여주고,
    /// 현재 페이즈만 밝게 강조한다. 클릭하면 다음 페이즈로 넘어가고(Cleanup
    /// 다음은 다시 Movement로 순환), 값은 MatchState.PhaseIndex에 저장된다
    /// (사용자 요청: 라운드는 이미 있는 스테퍼로 충분하니 페이즈만 추가).</summary>
    [RequireComponent(typeof(RectTransform))]
    public class PhaseBar : MonoBehaviour
    {
        private static readonly Color ActiveColor = Color.white;
        private static readonly Color InactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        private static readonly Color SeparatorColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        private readonly List<TextMeshProUGUI> _phaseLabels = new List<TextMeshProUGUI>();

        // ScoreboardPanel과 달리 이 컴포넌트는 부트스트랩이 BoardManager
        // 참조를 따로 주입해주지 않는다 — 되돌리기 편입(2026-09-04 추가,
        // 사용자 요청) 때문에 처음 필요해져서, BoardNetworkSync.cs의 RPC
        // 핸들러들이 이미 쓰는 것과 같은 방식(FindFirstObjectByType, 한 번
        // 찾은 뒤 캐시)으로 늦게 구한다.
        private BoardManager _board;

        private BoardManager Board()
        {
            if (_board == null)
            {
                _board = Object.FindFirstObjectByType<BoardManager>();
            }
            return _board;
        }

        private void Awake()
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -GameConstants.ScoreboardHeight);
            rect.sizeDelta = new Vector2(0f, GameConstants.PhaseBarHeight);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);

            var btn = gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.onClick.AddListener(AdvancePhase);

            var rowGo = new GameObject("Row", typeof(RectTransform));
            rowGo.transform.SetParent(transform, false);
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.anchorMin = Vector2.zero;
            rowRect.anchorMax = Vector2.one;
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = Vector2.zero;

            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var names = MatchState.PhaseNames;
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0)
                {
                    CreateLabel(rowRect, ">", 14f, SeparatorColor, FontStyles.Normal, raycastTarget: false);
                }
                var label = CreateLabel(rowRect, names[i], 15f, InactiveColor, FontStyles.Normal, raycastTarget: false);
                _phaseLabels.Add(label);
            }

            RefreshHighlight();
        }

        /// <summary>매 프레임 폴링(코루틴 없음, 이 프로젝트 관례) —
        /// MatchState.PhaseIndex를 다시 읽어 화면을 맞춘다(2026-09-04 추가).
        /// 되돌리기/다시실행(BoardManager.RestoreBoardSnapshot)이 이 값을
        /// 직접 바꿔놓는데 이 컴포넌트로 되돌아오는 참조가 없어서, 매 프레임
        /// 스스로 다시 읽는 방식으로 따라간다 — ScoreboardPanel.
        /// RefreshFromMatchState와 같은 이유/관례.</summary>
        private void Update()
        {
            RefreshHighlight();
        }

        private const string PhaseCompositeKey = "phase";
        // 연속 클릭 합치기(Composite, 2026-09-04 추가, 사용자 요청 —
        // "서클형 조작기들도 숫자 조작기 처럼 {초기값} -> {최종값} 형태로
        // 기록해줘") 용 — ScoreboardPanel._roundStreakBase와 같은 이유/역할.
        private int _phaseStreakBase = -1;

        /// <summary>클릭하면 부른다(사용자 조작) — 멀티 연결 중이면 다음
        /// 인덱스를 절대값으로 방송 요청만 한다(휠 회전 절대각 동기화와
        /// 같은 이유 — "한 칸 전진" 액션 자체를 보내면 메시지가 하나
        /// 유실됐을 때 양쪽이 서로 다른 페이즈로 어긋난 채 못 돌아온다).
        /// 되돌리기(2026-09-04 추가, 사용자 요청) — 마커 배치와 같은 자리에
        /// Begin/CommitUndoTransaction을 건다. 연속으로 눌러도(사용자 요청 —
        /// Composite) 되돌리기 목록엔 한 항목만 남는다 — ScoreboardPanel.
        /// OnRoundPipClicked와 같은 방식.</summary>
        private void AdvancePhase()
        {
            int nextIndex = (MatchState.PhaseIndex + 1) % MatchState.PhaseNames.Length;
            var board = Board();
            bool composite = board != null && board.IsTopUndoEntryComposite(PhaseCompositeKey);
            // _phaseStreakBase는 -1로 시작한다 — 이 컴포넌트가 로컬에서 한
            // 번도 페이즈를 누른 적 없는 채로(세이브 로드나 멀티 상대의
            // 되돌리기 항목이 이미 스택 맨 위를 "phase" 키로 차지하고 있어서)
            // composite만 true인 경우, 초기화 안 된 -1을 그대로 배열
            // 인덱스로 쓰면 IndexOutOfRangeException이 난다(사용자 보고,
            // 2026-09-09). ScoreboardPanel의 미션/킬 VP 스테퍼가 이미 쓰는
            // "로컬에 실제로 값이 있을 때만 신뢰" 패턴과 같은 이유로 방어.
            int baseIndex = (composite && _phaseStreakBase >= 0) ? _phaseStreakBase : MatchState.PhaseIndex;
            _phaseStreakBase = baseIndex;
            // 결과가 스트릭 시작 전 값으로 되돌아오면(사용자 요청, 2026-09-09)
            // 되돌리기 목록에서 그 항목 자체를 지운다 — ActivePlayerBar와
            // 같은 이유/자리.
            bool noop = nextIndex == baseIndex;

            string label = $"[점수판] 페이즈 {MatchState.PhaseNames[baseIndex]} -> {MatchState.PhaseNames[nextIndex]}";
            BoardManager.PerformNetworkedMutation(board, label, "페이즈 변경",
                    () => BoardNetworkSync.Instance.RequestSetPhaseServerRpc(nextIndex),
                    () => SetPhaseIndex(nextIndex),
                    compositeKey: PhaseCompositeKey,
                    discardIfNoChangeFromStreakStart: noop);
        }

        private void SetPhaseIndex(int index)
        {
            MatchState.PhaseIndex = index;
            RefreshHighlight();
        }

        /// <summary>BoardNetworkSync.SetPhaseRpc가 방송을 받았을 때(누른
        /// 쪽 자신도 포함) 호출한다.</summary>
        public void ApplyRemotePhaseIndex(int index)
        {
            SetPhaseIndex(index);
        }

        private void RefreshHighlight()
        {
            for (int i = 0; i < _phaseLabels.Count; i++)
            {
                bool active = i == MatchState.PhaseIndex;
                _phaseLabels[i].color = active ? ActiveColor : InactiveColor;
                _phaseLabels[i].fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string text, float fontSize, Color color, FontStyles style, bool raycastTarget)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = fontSize + 8f;
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.fontStyle = style;
            label.alignment = TextAlignmentOptions.Midline;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = raycastTarget;
            return label;
        }
    }
}
