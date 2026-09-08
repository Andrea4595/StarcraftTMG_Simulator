using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 되돌리기 ──────────────────────────────────────────────────────
        // "트랜잭션 전 상태를 통째로 스냅샷 → 스택에 push" 방식은 그대로다
        // (Godot판과 동일한 설계). 2026-09-01 재구성(사용자 지정) — 예전엔
        // Ctrl+Z/Ctrl+Shift+Z 단축키로만 한 단계씩 오갔는데, 이제 단축키는
        // 완전히 없애고 화면 우측 하단 "되돌리기" 버튼 → 모달
        // (UndoHistoryDialog)로 바꿨다. 모달은 "조작 리스트"(지금 되돌릴 수
        // 있는 것들)와 "Undo 리스트"(이미 취소해서 저쪽으로 넘어간 것들, 다시
        // 복원 가능) 두 목록을 보여준다.
        //
        // 2026-09-02, 리팩토링 Phase 3 — 스택/합치기/락/네트워크 방송/히스토리
        // UI 조회는 UndoRedoService.cs로 옮겼다. 이 파일엔 실제 보드 상태를
        // 만지는 캡처/복원(CaptureBoardSnapshot/RestoreBoardSnapshot,
        // ClearLiveBoardState)과 다이얼로그 체크(IsUndoBlocked)만 남는다 —
        // 둘 다 BoardManager의 씬 참조/라이브 GameObject에 직접 의존해서
        // UndoRedoService로 옮길 수 없었다(그쪽은 이 둘을 _board를 통해
        // 부른다). 아래 forwarders는 UndoRedoService로 그대로 위임하는 얇은
        // 창구다 — 기존 호출부(ScoreboardPanel/PhaseBar/BoardNetworkSync/
        // UndoHistoryDialog 등)는 이름이 그대로라 전혀 안 바뀐다.

        internal int UndoHistoryVersion => _undoRedo.UndoHistoryVersion;

        // 2026-09-04부터 internal — 스코어보드(ScoreboardPanel.cs)가 라운드/VP
        // 조작을 되돌리기에 편입시키면서, BoardManager 밖에서 처음으로 이
        // 트랜잭션 메서드들을 직접 부를 필요가 생겼다.
        internal void BeginUndoTransaction(string label, string team = "", string compositeKey = "")
        {
            _undoRedo.BeginUndoTransaction(label, team, compositeKey);
        }

        internal bool IsTopUndoEntryComposite(string compositeKey)
        {
            return _undoRedo.IsTopUndoEntryComposite(compositeKey);
        }

        internal void CommitUndoTransaction(bool broadcast = true)
        {
            _undoRedo.CommitUndoTransaction(broadcast);
        }

        internal void DiscardUndoTransaction()
        {
            _undoRedo.DiscardUndoTransaction();
        }

        /// <summary>"Begin → (멀티 연결 중이면 RPC 요청만 보내고, 실제 반영은 방송이
        /// 돌아왔을 때 ApplyRemote*가 처리 | 아니면 로컬 즉시 반영) → Commit" 패턴을
        /// 하나로 모았다(2026-09-02, BoardManager 리팩토링 Phase 0) — 점수판 라운드/VP,
        /// 페이즈, 마커 배치/삭제/상태변경, 미션 마커, 사거리 추가/삭제에 거의 그대로
        /// 복붙돼 있던 걸 통합. board가 null이면(부트스트랩 전 등) 되돌리기 bookkeeping만
        /// 건너뛰고 요청/반영 자체는 그대로 실행한다 — 기존 각 호출부의 `_board?.` 널
        /// 안전 동작을 그대로 보존하기 위함. BoardNetworkSync.Instance가 없으면(있어선
        /// 안 되는 상태) 로그만 남기고 트랜잭션을 버린다(Discard) — 방송도 로컬 반영도
        /// 없었으므로.</summary>
        internal static void PerformNetworkedMutation(BoardManager board, string label, string actionDescription, Action requestRpc, Action applyLocal, string team = "", string compositeKey = "")
        {
            board?.BeginUndoTransaction(label, team, compositeKey);
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (BoardNetworkSync.Instance == null)
                {
                    Debug.LogError($"[BoardManager] BoardNetworkSync.Instance가 없음 — {actionDescription} 요청을 못 보냄");
                    board?.DiscardUndoTransaction();
                    return;
                }
                requestRpc();
                board?.CommitUndoTransaction();
                return;
            }
            applyLocal();
            board?.CommitUndoTransaction();
        }

        internal void CancelOperationsDownTo(int stackIndex)
        {
            _undoRedo.CancelOperationsDownTo(stackIndex);
        }

        internal void RestoreOperationsDownTo(int stackIndex)
        {
            _undoRedo.RestoreOperationsDownTo(stackIndex);
        }

        internal void ApplyRemoteUndoPush(string label, string team, string compositeKey, string json)
        {
            _undoRedo.ApplyRemoteUndoPush(label, team, compositeKey, json);
        }

        internal void ApplyRemoteUndoRelabel(string label, string team)
        {
            _undoRedo.ApplyRemoteUndoRelabel(label, team);
        }

        internal void ApplyRemoteUndoCascade(bool isRedo, int targetIndex)
        {
            _undoRedo.ApplyRemoteUndoCascade(isRedo, targetIndex);
        }

        internal List<(string Label, int StackIndex, bool IsUndone, string Team, bool Locked)> GetCombinedHistoryForDisplay()
        {
            return _undoRedo.GetCombinedHistoryForDisplay();
        }

        internal Dictionary<string, object> BuildUndoHistoryTree()
        {
            return _undoRedo.BuildUndoHistoryTree();
        }

        internal void ApplySeededUndoHistory(Dictionary<string, object> root)
        {
            _undoRedo.ApplySeededUndoHistory(root);
        }

        internal void LockExistingUndoHistoryForMidGameJoin()
        {
            _undoRedo.LockExistingUndoHistoryForMidGameJoin();
        }

        internal void UnlockAllUndoHistoryAfterMultiplayerEnded()
        {
            _undoRedo.UnlockAllUndoHistoryAfterMultiplayerEnded();
        }

        /// <summary>진행 중인 트랜잭션이 있으면(드래그/유닛 이동/변위 배치 등) 그
        /// 중간 상태를 되돌리기로 덮어써서 망가뜨리면 안 되므로 무시한다.
        /// 다이얼로그나 다이얼 메뉴가 떠 있을 때도 마찬가지 — 그 뒤에서 보드가
        /// 바뀌면 열려 있는 창이 가리키는 대상(_menuTarget 등)이 붕 뜨게 된다.
        /// Godot판은 이 목록에 메모 다이얼로그를 빼먹었는데(아마 실수), Unity의
        /// UnityEngine.Object는 파괴된 오브젝트를 == null로 안전하게 취급해서
        /// 위험이 적긴 하지만 굳이 같은 구멍을 재현할 이유가 없어 포함시켰다.
        /// UndoRedoService.CancelOperationsDownTo/RestoreOperationsDownTo가
        /// _board를 통해 부른다(2026-09-02 분리, 씬 참조 다이얼로그 필드에
        /// 직접 의존해서 그쪽으로 옮길 수 없었다).</summary>
        internal bool IsUndoBlocked()
        {
            if (_undoRedo.HasPendingTransaction)
            {
                return true;
            }
            if (IsDialogVisible(damageDialog) || IsDialogVisible(memoDialog)
                    || IsDialogVisible(rangeInputDialog) || (radialMenu != null && radialMenu.gameObject.activeSelf)
                    || (diceRollDialog != null && diceRollDialog.gameObject.activeSelf))
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

        internal BoardSnapshot CaptureBoardSnapshot()
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
                    var unitSnap = new UnitSnapshot
                    {
                        NetworkUnitId = unit.NetworkUnitId,
                        UnitName = unit.UnitName,
                        Team = unit.Team,
                        CoherencyInch = unit.CoherencyInch,
                        MoveInch = unit.MoveInch,
                        IsToken = unit.IsToken,
                        CanMove = unit.CanMove,
                        SupplyOverride = unit.SupplyOverride,
                        Detail = unit.Detail,
                    };
                    unitSnap.SupplyTiers.AddRange(unit.SupplyTiers);
                    snapshot.Units.Add(unitSnap);
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

            // 2026-09-04부터 정의 전체를 복제한다(전엔 Remaining만 인덱스로
            // 짝지어 담았는데, 그건 "카드 정의 자체는 임포트 때만 바뀐다"는
            // 가정 위에서만 안전했다 — 로스터 불러오기 자체가 되돌리기
            // 대상이 되면서 그 가정이 깨졌으므로 PendingUnits/
            // PendingRosterTokens와 같은 방식으로 맞춘다).
            foreach (var def in _pendingTacticalCards)
            {
                snapshot.TacticalCards.Add(CloneTacticalCardDef(def));
            }

            // 라운드/서플라이/VP(2026-09-04 추가) — 점수판 조작(ScoreboardPanel.cs)
            // 도 이제 되돌리기 대상이라 보드 상태와 함께 캡처해야 한다.
            snapshot.RoundNumber = MatchState.RoundNumber;
            snapshot.MissionVpA = MatchState.MissionVp["A"];
            snapshot.MissionVpB = MatchState.MissionVp["B"];
            snapshot.KillVpA = MatchState.KillVp["A"];
            snapshot.KillVpB = MatchState.KillVp["B"];
            snapshot.PhaseIndex = MatchState.PhaseIndex;
            snapshot.RosterLoadedTeams.AddRange(_rosterLoadedTeams);

            foreach (var kv in _missionObjectivePiecesByNumber)
            {
                if (kv.Value == null)
                {
                    continue;
                }
                snapshot.MissionObjectiveStates.Add(new MissionObjectiveStateSnapshot { Number = kv.Key, RingState = kv.Value.RingColorState });
            }

            foreach (var markerGo in EnumerateRealMarkers())
            {
                if (markerGo.TryGetComponent<ActivationMarker>(out var act))
                {
                    snapshot.Markers.Add(new MarkerSnapshot { Kind = "activation", Center = act.Center, State = act.State, NetworkMarkerId = act.NetworkMarkerId });
                }
                else if (markerGo.TryGetComponent<CaptureMarker>(out var cap))
                {
                    snapshot.Markers.Add(new MarkerSnapshot { Kind = "capture", Center = cap.Center, State = cap.ColorState, NetworkMarkerId = cap.NetworkMarkerId });
                }
                else if (markerGo.TryGetComponent<IconMarker>(out var icon))
                {
                    snapshot.Markers.Add(new MarkerSnapshot { Kind = icon.Kind, Center = icon.Center, State = "", NetworkMarkerId = icon.NetworkMarkerId });
                }
            }

            return snapshot;
        }

        /// <summary>보드 위 유닛/모델/마커를 전부 지우고 관련 보조 상태를
        /// 초기화한다 — RestoreBoardSnapshot이 실제로 되살리기 직전에 호출하는
        /// 공통 첫 단계. _networkedUnits도 함께 비운다 — 예전엔 여기
        /// 없었는데, 지워지는 유닛들을 가리키던 항목이 그대로 남아 파괴된
        /// 인스턴스를 참조하는 채로 굳어 있었다(멀티 동기화를 추가하며 발견 —
        /// 되돌리기 뒤 그 유닛을 다시 움직이면 상대와 어긋날 수 있었던
        /// 잠재적 원인. 지금은 UnitSnapshot.NetworkUnitId를 그대로 되살려서
        /// 바로 아래에서 다시 채운다).</summary>
        private void ClearLiveBoardState()
        {
            for (int i = baseLayer.childCount - 1; i >= 0; i--)
            {
                var child = baseLayer.GetChild(i);
                // 미션 목표 마커/배치구역 표시는 언두 대상이 아니다 — 미션
                // 셋업에서 고정된 순수 표시용이라 스냅샷에도 안 담겼는데, 그런
                // 줄 모르고 baseLayer의 모든 자식을 무조건 지워버렸던 게
                // "Ctrl+Z 누르면 미션 마커가 전부 사라진다"는 버그의 원인이었다
                // (지운 뒤 다시 만들어주는 코드가 없어서 영영 사라졌다).
                if (child.GetComponent<MissionObjectivePiece>() != null
                        || child.name.StartsWith("DeploymentZoneEdge_"))
                {
                    continue;
                }
                // DestroyImmediate 필수 — 되돌리기 모달의 "그 지점까지 전부"
                // 카스케이드가 같은 프레임 안에서 CaptureBoardSnapshot과
                // RestoreBoardSnapshot을 여러 번 연달아 부른다. 일반 Destroy()는
                // 프레임 끝까지 실제 제거를 미루므로, 다음 카스케이드 단계의
                // CaptureBoardSnapshot이 "아직 안 지워진 이전 조각들"까지
                // baseLayer 자식으로 다시 읽어버려 유닛이 매 단계마다 배로
                // 불어나는 실제 버그가 있었다(사용자 발견 — "undo, redo를
                // 반복하다보면 유닛이 엄청나게 복제됨").
                DestroyImmediate(child.gameObject);
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
                    DestroyImmediate(child.gameObject); // 위와 같은 이유.
                }
            }

            // 지워진 베이스/유닛을 참조하던 값들을 전부 정리 — 복원 뒤에도 남아있으면
            // 파괴된 인스턴스를 가리키는 참조가 된다.
            _hoveredBase = null;
            _hoveredUnit = null;
            _menuTarget = null;
            _selectedUnitForDetailByTeam.Clear();
            _rangeTargetUnit = null;
            _rangeDeleteTargetUnit = null;
            _unitRanges.Clear();
            _rosterTokenUnits.Clear();
            _draggingPiece = null;
            _draggingFollower = null;
            _draggingMarker = null;
            _networkedUnits.Clear();
            // 2026-09-02 추가 — 마커도 유닛과 같은 이유로 비워야 한다. 지워지는
            // 마커들을 가리키던 항목을 그대로 두면 파괴된 인스턴스를 참조하게
            // 되고(유닛 쪽과 같은 문제), MarkerSnapshot.NetworkMarkerId를 이제
            // 그대로 되살려서 바로 아래에서 다시 채운다.
            _networkedMarkers.Clear();
        }

        internal void RestoreBoardSnapshot(BoardSnapshot snapshot)
        {
            ClearLiveBoardState();

            var restoredUnits = new List<Unit>();
            foreach (var unitSnap in snapshot.Units)
            {
                var unit = new Unit
                {
                    NetworkUnitId = unitSnap.NetworkUnitId,
                    UnitName = unitSnap.UnitName,
                    Team = unitSnap.Team,
                    CoherencyInch = unitSnap.CoherencyInch,
                    MoveInch = unitSnap.MoveInch,
                    IsToken = unitSnap.IsToken,
                    CanMove = unitSnap.CanMove,
                    SupplyOverride = unitSnap.SupplyOverride,
                    Detail = unitSnap.Detail,
                };
                unit.SupplyTiers.AddRange(unitSnap.SupplyTiers);
                restoredUnits.Add(unit);
                if (unit.NetworkUnitId >= 0)
                {
                    // ClearLiveBoardState가 이 되살리기 직전에 _networkedUnits를
                    // 이미 비워뒀다 — 여기서 다시 채워야 이후 이 유닛을 다시
                    // 옮기거나 범위를 추가할 때(BroadcastUnitIfNetworked 등) 상대와
                    // 같은 id로 계속 짝지어진다.
                    _networkedUnits.Set(unit.NetworkUnitId, unit);
                }
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

            // "로스터 로드됨" 판정을 예비대/토큰/택티컬 카드 목록보다 먼저
            // 되돌린다(2026-09-04 추가) — 아래 Refresh*List()들이 부르는
            // RefreshPanelLayout()이 이 값을 바로 참조하므로, 그보다 먼저
            // 맞춰둬야 한다. BoardSnapshot.RosterLoadedTeams 주석 참고 —
            // 로스터 불러오기 자체를 되돌리는 경우에만(그 이전 스냅샷엔
            // team이 없었으므로) 정확히 부활한다.
            _rosterLoadedTeams.Clear();
            foreach (var team in snapshot.RosterLoadedTeams)
            {
                _rosterLoadedTeams.Add(team);
            }

            // 세 예비대 계열 목록(_pendingUnits/_pendingRosterTokens/
            // _pendingTacticalCards)을 전부 복원부터 끝낸 다음에야
            // Refresh*List()들을 부른다(2026-09-04 버그 수정, 사용자 보고 —
            // "A 로드, B 로드, B 되돌리기 해도 B 버튼이 안 부활하고, A까지
            // 되돌려야 그제서야 부활함"). 예전엔 _pendingUnits만 먼저
            // 복원하고 바로 RefreshPendingList()를 불렀는데, 그 안의
            // RefreshPanelLayout()이 "아직 안 비운" 이전 _pendingRosterTokens
            // 값을 그대로 읽어 tokenCount>0으로 오판 — 위에서 방금 비워둔
            // _rosterLoadedTeams을 자기 진단 로직(RefreshPanelLayout 자체
            // 주석의 "이미 예비대/토큰이 있으면 로드된 것으로 자동 편입")으로
            // 즉시 되살려버렸다. 세 목록을 다 갈아치운 뒤에 한 번씩만
            // Refresh*List()를 불러야 그 자기 진단이 실제로 복원된 최종
            // 상태를 보고 판단한다.
            _pendingUnits.Clear();
            foreach (var def in snapshot.PendingUnits)
            {
                _pendingUnits.Add(ClonePendingUnitDef(def));
            }

            _pendingRosterTokens.Clear();
            foreach (var def in snapshot.PendingRosterTokens)
            {
                _pendingRosterTokens.Add(ClonePendingTokenDef(def));
            }

            // 2026-09-04부터 목록 자체를 통째로 되살린다(PendingUnits/
            // PendingRosterTokens와 같은 방식) — 로스터 불러오기가 되돌리기
            // 대상이 되면서, 그 액션이 추가한 카드까지 되돌아가야 하므로
            // "정의는 안 바뀐다"는 예전 가정(Remaining만 인덱스로 복원)이 더
            // 이상 안전하지 않다.
            _pendingTacticalCards.Clear();
            foreach (var def in snapshot.TacticalCards)
            {
                _pendingTacticalCards.Add(CloneTacticalCardDef(def));
            }

            RefreshPendingList();
            RefreshRosterTokenList();
            RefreshTacticalCardList();

            // 라운드/서플라이/VP(2026-09-04 추가) — ScoreboardPanel.Update()가
            // 매 프레임 MatchState를 다시 읽어 화면(라운드 네모/VP 스테퍼)을
            // 그리므로, 여기서는 값만 되돌리면 된다(별도 갱신 호출 불필요 —
            // 서플라이 네모/팀색과 같은 이미 있던 관례). 서플라이 상한
            // (MatchState.Supply)은 라운드+미션 설정으로 항상 결정적으로
            // 재계산되므로(ScoreboardPanel.SetRoundNumber) 따로 스냅샷/복원할
            // 필요가 없다.
            MatchState.RoundNumber = snapshot.RoundNumber;
            MatchState.MissionVp["A"] = snapshot.MissionVpA;
            MatchState.MissionVp["B"] = snapshot.MissionVpB;
            MatchState.KillVp["A"] = snapshot.KillVpA;
            MatchState.KillVp["B"] = snapshot.KillVpB;
            // 페이즈(2026-09-04 추가) — PhaseBar도 스코어보드처럼 매 프레임
            // MatchState를 다시 읽어 스스로 화면을 맞춘다(PhaseBar.Update
            // 참고).
            MatchState.PhaseIndex = snapshot.PhaseIndex;

            // 미션 목표 마커 점령 링 상태(2026-09-04 추가) — 마커 자신은
            // 파괴/재생성 대상이 아니므로(BoardSnapshot.MissionObjectiveStates
            // 주석 참고) 상태만 번호로 찾아 되돌린다.
            foreach (var stateSnap in snapshot.MissionObjectiveStates)
            {
                if (_missionObjectivePiecesByNumber.TryGetValue(stateSnap.Number, out var piece) && piece != null)
                {
                    piece.SetRingColorState(stateSnap.RingState);
                }
            }

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

                    // 2026-09-02 추가 — 이게 없으면 되돌리기/다시실행을 한 번만
                    // 해도 이 마커가 네트워크 id를 잃어서(항상 -1로 새로 만들어짐)
                    // 이후 삭제/상태변경 방송이 아무 대상도 못 찾고 씹혔다.
                    if (markerSnap.NetworkMarkerId >= 0)
                    {
                        marker.NetworkMarkerId = markerSnap.NetworkMarkerId;
                        _networkedMarkers.Set(markerSnap.NetworkMarkerId, marker);
                    }
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
                SupplyTiers = new List<SupplyTier>(def.SupplyTiers),
                Damages = new List<int>(def.Damages),
                Ranges = new List<RangeSpec>(def.Ranges),
                SupplyOverride = def.SupplyOverride,
                Specialists = new List<string>(def.Specialists),
                Detail = def.Detail,
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

        private static TacticalCardDef CloneTacticalCardDef(TacticalCardDef def)
        {
            return new TacticalCardDef
            {
                Name = def.Name,
                Team = def.Team,
                Count = def.Count,
                Remaining = def.Remaining,
                ResourceAbbr = def.ResourceAbbr,
                ResourceAmount = def.ResourceAmount,
                Abilities = new List<RosterAbilityEntry>(def.Abilities),
            };
        }
    }

    // ── 되돌리기 스냅샷 값 객체들(2026-09-02, 리팩토링 Phase 3에서 최상위로
    // 분리) ──────────────────────────────────────────────────────────────
    // 예전엔 BoardManager의 private 중첩 클래스였다. CaptureBoardSnapshot/
    // RestoreBoardSnapshot(BoardManager, 위)과 UndoRedoService(스택 보관/
    // JSON 직렬화) 양쪽이 다 다뤄야 해서 internal 최상위 타입으로 뺐다 —
    // 그 외 어떤 파일도 이 타입들을 직접 쓰지 않는다(BoardManager 밖에서는
    // 전부 GetCombinedHistoryForDisplay가 돌려주는 튜플/트리를 통해서만
    // 되돌리기 상태를 본다).

    internal class UnitSnapshot
    {
        // 되돌리기 스택을 상대와 동기화(BroadcastUndoPushIfNetworked)하려면
        // 양쪽이 같은 유닛을 같은 id로 계속 가리켜야 한다 — 이 필드가
        // 없던 예전엔 RestoreBoardSnapshot이 되살린 유닛마다 id가
        // 사라져서(-1), 되돌리기 뒤 그 유닛을 다시 옮기면 상대 화면엔
        // 중복 유닛이 새로 생기는 버그가 있었다.
        public int NetworkUnitId = -1;
        public string UnitName;
        public string Team;
        public float CoherencyInch;
        public float MoveInch;
        public bool IsToken;
        public bool CanMove;
        public int? SupplyOverride;
        public RosterUnitDetail Detail;
        public readonly List<SupplyTier> SupplyTiers = new List<SupplyTier>();
        public readonly List<ModelSnapshot> Models = new List<ModelSnapshot>();
    }

    internal class ModelSnapshot
    {
        public Vector2 Center;
        public float RotationDegrees;
        public Vector2 SizeMm;
        public Color FillColor;
        public int Damage;
        public bool IsDisplacement;
        public string Memo;
    }

    internal class RangeSnapshot
    {
        public int UnitRef;
        public List<RangeSpec> Ranges;
    }

    internal class MarkerSnapshot
    {
        public string Kind; // "activation" / "capture" / 그 외(아이콘 kind 문자열)
        public Vector2 Center;
        public string State; // activation: State 문자열, capture: ColorState 문자열, icon: 안 씀("")
        // 2026-09-02 추가 — 이게 없어서 멀티 중 되돌리기/다시실행을 한 번만
        // 해도 마커 전체가 네트워크 id를 잃어버려(RestoreBoardSnapshot이
        // 마커를 통째로 새로 만들면서 id를 안 이어받았음) 그 뒤 삭제/상태
        // 순환 방송이 전부 씹히는 버그가 있었다(사용자 보고로 발견) —
        // UnitSnapshot.NetworkUnitId와 완전히 같은 이유로 같은 자리에 추가.
        public int NetworkMarkerId = -1;
    }

    internal class MissionObjectiveStateSnapshot
    {
        public int Number;
        public string RingState;
    }

    internal class BoardSnapshot
    {
        public readonly List<UnitSnapshot> Units = new List<UnitSnapshot>();
        public readonly List<RangeSnapshot> Ranges = new List<RangeSnapshot>();
        public readonly List<PendingUnitDef> PendingUnits = new List<PendingUnitDef>();
        public readonly List<PendingTokenDef> PendingRosterTokens = new List<PendingTokenDef>();
        public readonly List<MarkerSnapshot> Markers = new List<MarkerSnapshot>();
        // 전술 카드 사용/복구도 되돌리기 대상이다(2026-09-04, 사용자
        // 요청). 처음엔 "_pendingTacticalCards는 임포트 때만 추가/삭제
        // 되고 그 뒤엔 순서가 안 바뀐다"는 가정으로 Remaining만 인덱스로
        // 짝지어 담았는데, 같은 날 로스터 불러오기 자체도 되돌리기
        // 대상이 되면서(ApplyRosterImport) 그 가정이 깨져서 PendingUnits/
        // PendingRosterTokens와 같은 방식(정의 전체 복제)으로 바꿨다.
        public readonly List<TacticalCardDef> TacticalCards = new List<TacticalCardDef>();
        // 점수판(라운드/미션·파괴 VP)도 되돌리기 대상이다(2026-09-04,
        // 사용자 요청 — "리플레이가 의미를 가지려면 점수판 조작도
        // 기록돼야 한다"). 서플라이 상한(MatchState.Supply)은 라운드+
        // 미션 설정으로 항상 결정적으로 재계산되므로(ScoreboardPanel.
        // SetRoundNumber) 따로 담지 않는다.
        public int RoundNumber;
        public int MissionVpA;
        public int MissionVpB;
        public int KillVpA;
        public int KillVpB;
        // 페이즈도 되돌리기 대상이다(2026-09-04, 사용자 요청).
        public int PhaseIndex;
        // 로스터 "로드됨" 판정도 되돌리기 대상이어야 한다(2026-09-04,
        // 사용자 요청 — "로스터 불러오기를 취소하면 버튼이 부활해야
        // 한다"). 이 필드는 원래(BoardManager.Deployment.cs의
        // RefreshPanelLayout 주석 참고) "한 번 켜지면 계속 유지되는
        // 단방향 스위치"로 설계됐는데, 그건 "예비대를 배치해서 목록이
        // 비는" 경우에만 해당하는 얘기였다 — 매 스냅샷에 이 값을 그대로
        // 담아두면, 배치로 목록이 비어도(그 이후 스냅샷들은 계속 team이
        // 들어있는 채로 캡처됨) 여전히 안 부활하고, 반대로 로스터
        // 불러오기 자체를 되돌려 그 이전 스냅샷(team이 아직 없던 시점)
        // 으로 돌아가면 정확히 그때만 부활한다 — 별도 로직 없이 스냅샷
        // 캡처/복원 자체가 두 요구를 동시에 만족시킨다.
        public readonly List<string> RosterLoadedTeams = new List<string>();
        // 미션 목표 마커(점령 링) 우클릭 색 순환도 되돌리기 대상이다
        // (2026-09-04, 사용자 요청). 그 마커 자체(MissionObjectivePiece)는
        // 되돌리기의 파괴/재생성 대상이 아니라(ClearLiveBoardState가
        // 일부러 건너뛴다 — 미션 셋업에서 고정된 영구 표시용) 상태 값만
        // 번호로 짝지어 담는다.
        public readonly List<MissionObjectiveStateSnapshot> MissionObjectiveStates = new List<MissionObjectiveStateSnapshot>();
    }
}
