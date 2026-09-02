using System;
using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 미션 목표 마커(순수 표시용 + 점령 링 색상 순환) ──────────────
        // 미션 설정 화면에서 놓은 목표를 그대로 재현한다. Godot판
        // GameBoard.gd의 _build_mission_data_visuals() 중 목표 부분 포팅 —
        // 드래그/삭제는 지원하지 않는다(그 편집은 미션 설정 화면의 몫).
        // 유일한 조작은 우클릭 → 점령 범위 링 색상을 흰→빨→파로 순환시키는
        // 것뿐(CaptureMarker와 같은 인터랙션, 삭제는 절대 없음) — 그래서
        // raycastTarget은 켜두되(우클릭을 받으려면 필요) AllowDrag=false로
        // 드래그만 막는다.

        // 번호(1~5)는 MapData.MissionObjectives에서 온 것이라 양쪽 클라이언트가
        // 이미 동일하게 갖고 있다(배치 프리셋 자체가 드래프트로 동기화됨) —
        // 그래서 마커/유닛과 달리 새로 네트워크 id를 발급할 필요 없이 번호
        // 그대로 방송 대상 지목에 쓸 수 있다.
        private readonly Dictionary<int, MissionObjectivePiece> _missionObjectivePiecesByNumber = new Dictionary<int, MissionObjectivePiece>();

        // 연속 클릭 합치기(Composite, 2026-09-04 추가, 사용자 요청 — "서클형
        // 조작기들도 숫자 조작기 처럼 {초기값} -> {최종값} 형태로 기록해줘")
        // 용 — 마커 번호별로 "그 연속 편집이 시작되기 전" 상태를 따로
        // 기억해둔다. ScoreboardPanel._roundStreakBase 등과 같은 역할.
        private readonly Dictionary<int, string> _missionObjectiveStreakBase = new Dictionary<int, string>();

        private void BuildMissionObjectiveVisuals()
        {
            if (!MapData.HasData)
            {
                return;
            }

            float diameter = GameConstants.MissionObjectiveTokenDiameterMm
                    + 2f * GameConstants.MissionObjectiveCaptureMarginInch * GameConstants.MmPerInch;

            foreach (var objective in MapData.MissionObjectives)
            {
                var go = new GameObject($"MissionObjective_{objective.Number}", typeof(RectTransform));
                go.transform.SetParent(baseLayer, false);
                var piece = go.AddComponent<MissionObjectivePiece>();
                piece.Number = objective.Number;
                var baseColor = GameConstants.GetMissionObjectiveBaseColor(objective.Number);
                piece.TokenColor = GameConstants.Muted(baseColor, GameConstants.MissionObjectiveSaturationFactor, GameConstants.MissionObjectiveValueFactor);
                piece.RectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                piece.RectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                piece.RectTransform.sizeDelta = new Vector2(diameter, diameter);
                piece.Center = CornerToCenterMm(objective.Position);
                piece.AllowDrag = false;
                piece.RightClickCyclesColor = true;
                piece.ColorCycleRequested += OnMissionObjectiveColorCycleRequested;
                _missionObjectivePiecesByNumber[objective.Number] = piece;
            }
        }

        /// <summary>우클릭 시(MissionObjectivePiece.ColorCycleRequested) 부른다
        /// — 멀티 연결 중이면 다음 상태를 절대값으로 미리 계산해 방송 요청만
        /// 하고(휠 회전 절대각과 같은 이유 — 메시지 유실에도 안 어긋나게),
        /// 실제 반영은 그 방송이 되돌아오는 걸 거친다. 되돌리기(2026-09-04
        /// 추가, 사용자 요청) — 마커 배치와 같은 자리에 Begin/
        /// CommitUndoTransaction을 건다(BoardSnapshot.MissionObjectiveStates
        /// 참고 — 마커 자신은 파괴/재생성 대상이 아니라 상태만 복원됨). 같은
        /// 마커를 연속으로 우클릭해도(사용자 요청 — Composite) 되돌리기
        /// 목록엔 한 항목만 남는다 — ScoreboardPanel.OnRoundPipClicked와
        /// 같은 방식, 마커 번호별로 compositeKey를 나눈다(마커마다 독립).</summary>
        private void OnMissionObjectiveColorCycleRequested(MissionObjectivePiece piece)
        {
            int idx = Array.IndexOf(MissionObjectivePiece.RingColorSequence, piece.RingColorState);
            string nextState = MissionObjectivePiece.RingColorSequence[(idx + 1) % MissionObjectivePiece.RingColorSequence.Length];
            string compositeKey = $"missionMarker:{piece.Number}";
            bool composite = IsTopUndoEntryComposite(compositeKey);
            string baseState = composite && _missionObjectiveStreakBase.TryGetValue(piece.Number, out var b) ? b : piece.RingColorState;
            _missionObjectiveStreakBase[piece.Number] = baseState;

            PerformNetworkedMutation(this, $"미션 마커 {piece.Number} {DescribeRingColorState(baseState)} -> {DescribeRingColorState(nextState)}", "미션 마커 색 변경",
                    () => BoardNetworkSync.Instance.RequestSetMissionObjectiveColorServerRpc(piece.Number, nextState),
                    () => piece.CycleRingColor(),
                    compositeKey: compositeKey);
        }

        /// <summary>RingColorState 원시값("inactive"/"white"/"red"/"blue")을
        /// 되돌리기 라벨/토스트에 쓸 한글 표시로 바꾼다(사용자 요청,
        /// 2026-09-04 — 원시값 그대로 보여주던 걸 교체). red/blue는 팀
        /// 색상이 아니라 팀 점령 상태를 뜻하므로(MissionObjectivePiece.
        /// ResolveRingColor 참고 — red="A"팀 색, blue="B"팀 색) "A 점령"/
        /// "B 점령"으로 표시한다.</summary>
        private static string DescribeRingColorState(string state)
        {
            switch (state)
            {
                case "inactive":
                    return "비활성";
                case "white":
                    return "활성";
                case "red":
                    return "A 점령";
                case "blue":
                    return "B 점령";
                default:
                    return state;
            }
        }

        /// <summary>BoardNetworkSync.SetMissionObjectiveColorRpc가 방송을
        /// 받았을 때(요청한 쪽 자신도 포함) 호출한다.</summary>
        internal void ApplyMissionObjectiveColorByNumber(int number, string colorState)
        {
            if (!_missionObjectivePiecesByNumber.TryGetValue(number, out var piece))
            {
                Debug.LogError($"[BoardManager] 색을 바꿀 미션 마커를 못 찾음(번호={number})");
                return;
            }
            piece.SetRingColorState(colorState);
        }

        /// <summary>마우스 아래 미션 목표 마커(원형 판정, 회전 불필요 — 항상
        /// 정원이라 FindBaseAtPoint처럼 회전 보정을 할 필요가 없다)를 찾는다.
        /// 이 마커는 순수 표시/우클릭-색상순환 전용인데도 raycastTarget이
        /// 켜져 있어(우클릭을 받으려면 필요) 3인치 점령 링 전체 크기만큼
        /// EventSystem.IsPointerOverGameObject()에 "UI 위"로 잡혀버린다 —
        /// 리딩 모델 배치 완료(HandlePendingDeploymentInput)나 지도 팬/줌
        /// (HandlePanAndZoom)처럼 정말 UI 패널 위인지와 무관해야 하는 곳에서는
        /// FindBaseAtPoint의 overBase 예외 패턴과 같은 방식으로 이 결과를 써서
        /// "실은 UI가 아니다"라고 무시해야 한다(사용자 보고 버그 두 건, 2026-09-01
        /// 백로그 → 2026-09-02 수정).</summary>
        private MissionObjectivePiece FindMissionObjectiveAtPoint(Vector2 point)
        {
            foreach (var piece in _missionObjectivePiecesByNumber.Values)
            {
                if (piece == null)
                {
                    continue;
                }
                float radius = piece.RectTransform.sizeDelta.x / 2f;
                if (Vector2.Distance(point, piece.Center) <= radius)
                {
                    return piece;
                }
            }
            return null;
        }

        /// <summary>지금 마우스 아래에 미션 목표 마커가 있는지만 확인하는
        /// 편의 래퍼 — HandlePendingDeploymentInput/HandlePanAndZoom처럼
        /// "IsPointerOverUi()가 true여도 사실 이건 무시해도 되는 raycastable"
        /// 예외 판정에 쓴다.</summary>
        private bool IsOverMissionObjective()
        {
            return TryGetLocalMouse(out var local) && FindMissionObjectiveAtPoint(local) != null;
        }
    }
}
