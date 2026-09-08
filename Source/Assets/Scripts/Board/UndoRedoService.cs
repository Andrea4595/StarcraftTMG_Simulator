using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>되돌리기/다시하기 스택, 연속편집 합치기(Composite), 게임 도중
    /// 합류 락(Locked), 네트워크 방송/수신, 히스토리 UI 조회를 전담한다 —
    /// 예전엔 BoardManager.UndoRedo.cs 한 파일에 전부 있었다(2026-09-02,
    /// BoardManager 리팩토링 Phase 3에서 분리). 실제 보드 상태 캡처/복원
    /// (BoardManager.CaptureBoardSnapshot/RestoreBoardSnapshot)과 다이얼로그
    /// 열림 체크(BoardManager.IsUndoBlocked)는 BoardManager의 씬 참조/라이브
    /// GameObject에 직접 의존해서 여기로 옮길 수 없었다 — BoardManager에
    /// 남겨두고 _board를 통해 호출한다.
    ///
    /// BoardManager는 이 클래스의 공개 메서드를 이름 그대로 얇게 포워딩한다
    /// (BoardManager.UndoRedo.cs) — 그래서 ScoreboardPanel/PhaseBar/
    /// BoardNetworkSync/UndoHistoryDialog 등 기존 호출부는 전혀 안 바뀐다.</summary>
    internal sealed class UndoRedoService
    {
        private readonly BoardManager _board;

        internal UndoRedoService(BoardManager board)
        {
            _board = board;
        }

        private class UndoEntry
        {
            public BoardSnapshot Snapshot;
            public string Label;
            // 2026-09-04 추가 — 토스트/되돌리기 목록 텍스트를 행위자 팀
            // 색으로 칠하기 위한 것. 유닛/전술카드처럼 특정 팀 소유가 뚜렷한
            // 행동만 채워지고("A"/"B"), 마커처럼 팀 개념이 없는 행동은 빈
            // 문자열("")로 남아 기본(흰색) 표시로 빠진다. 네트워크로 문자열
            // null을 안전하게 못 보내므로 "빈 문자열=팀 없음"을 프로젝트
            // 전체에서 일관되게 쓴다(null 아님).
            public string Team = "";
            // 연속 편집 합치기(Composite, 2026-09-04 추가, 사용자 요청) 용 키.
            // 예: 같은 팀의 미션VP 스테퍼를 연달아 3번 눌러도 되돌리기
            // 목록엔 항목 하나만 남고(라벨만 최신 값으로 계속 갱신), 그
            // 항목의 Snapshot(되돌아갈 대상)은 첫 클릭 때 캡처된 "그 연속
            // 편집 시작 전" 상태 그대로 고정된다 — CommitUndoTransaction
            // 참고. 빈 문자열이면 합치기 대상이 아님(대부분의 행동).
            public string CompositeKey = "";
            // 게임 도중 멀티 합류(2026-09-04 재구성, 사용자 요청 — "되돌리기
            // 리스트를 지울 필욘 없어, 조작만 불가능해 보이게") 시점에 이미
            // 쌓여있던 항목은 전부 이걸 true로 세운다. 그 Snapshot 안엔
            // 합류 전(백필 이전) 네트워크 id가 그대로 박제돼 있어서, 되돌려
            // 보면 유닛/마커 중복 생성 버그로 이어진다(사용자가 실제로
            // 재현) — 그래서 데이터는 보존하되(리플레이/기록 목적)
            // CancelOperationsDownTo/RestoreOperationsDownTo/UndoHistoryDialog
            // 양쪽에서 이 항목을 대상으로 한 조작 자체를 막는다.
            // LockExistingUndoHistoryForMidGameJoin 참고.
            public bool Locked;
        }

        private readonly List<UndoEntry> _undoStack = new List<UndoEntry>();
        private readonly List<UndoEntry> _redoStack = new List<UndoEntry>();
        private bool _undoPendingActive;
        private BoardSnapshot _undoPendingSnapshot;
        private string _undoPendingLabel;
        private string _undoPendingTeam = "";
        private string _undoPendingCompositeKey = "";

        // 두 스택 중 하나라도 바뀔 때마다 올라간다 — UndoHistoryDialog가 열려
        // 있는 동안 이 값을 매 프레임 폴링해서(코루틴 없이) 바뀌었으면 목록을
        // 다시 그린다.
        private int _undoHistoryVersion;

        internal int UndoHistoryVersion => _undoHistoryVersion;

        /// <summary>BoardManager.IsUndoBlocked 전용 — 진행 중인 트랜잭션이
        /// 있으면(드래그/유닛 이동/변위 배치 등) 그 중간 상태를 되돌리기로
        /// 덮어써서 망가뜨리면 안 되므로 새 되돌리기/카스케이드 조작을 막는다.</summary>
        internal bool HasPendingTransaction => _undoPendingActive;

        internal void BeginUndoTransaction(string label, string team = "", string compositeKey = "")
        {
            if (_undoPendingActive)
            {
                return;
            }
            _undoPendingSnapshot = _board.CaptureBoardSnapshot();
            _undoPendingLabel = label;
            _undoPendingTeam = team ?? "";
            _undoPendingCompositeKey = compositeKey ?? "";
            _undoPendingActive = true;
        }

        /// <summary>compositeKey가 채워져 있고 지금 _undoStack 맨 위 항목이
        /// 같은 키라면, 그건 "연속 편집을 계속하는 중"이라는 뜻이다(사용자
        /// 요청, 2026-09-04 — "이 전 기록과 동일한 값을 편집하고 있다면
        /// Composit 해줘"). 호출부(ScoreboardPanel 등)가 BeginUndoTransaction
        /// 전에 이걸로 미리 물어봐서, "원래 시작값"을 라벨에 계속 써야
        /// 하는지 판단하는 데 쓴다.</summary>
        internal bool IsTopUndoEntryComposite(string compositeKey)
        {
            return !string.IsNullOrEmpty(compositeKey)
                    && _undoStack.Count > 0
                    && _undoStack[_undoStack.Count - 1].CompositeKey == compositeKey;
        }

        /// <summary>broadcast=false는 로스터 임포트처럼 이 메서드 자체가 이미
        /// "방송을 받았을 때 모든 클라이언트가 각자 부르는" 공유 적용
        /// 지점 안에서 호출되는 경우 전용이다(2026-09-04 버그 수정, 사용자
        /// 보고 — "멀티에서 로스터 불러오면 되돌리기 목록에 중복으로 두 번
        /// 뜬다"). 마커/유닛 이동 등 대부분의 액션은 "누른 쪽만" Begin/
        /// Commit을 부르고(방송을 받아 시각 요소만 그리는 쪽은 따로 손대지
        /// 않음) 그래서 상대 스택엔 이 커밋 내용이 방송(BroadcastUndoPushIfNetworked)
        /// 으로만 전달돼야 한다. 반면 ApplyRosterImport는 임포트 자체를
        /// 방송받은 모든 클라이언트(호스트 자신 포함)가 각자 그 안에서
        /// Begin/Commit을 부르므로, 이미 양쪽 다 자기 스택에 동등한 항목을
        /// 갖는다 — 거기에 더해 평소처럼 방송까지 하면 상대가 한 번 더
        /// 받아 쌓아서 중복이 생긴다.</summary>
        internal void CommitUndoTransaction(bool broadcast = true)
        {
            if (!_undoPendingActive)
            {
                return;
            }

            bool isComposite = IsTopUndoEntryComposite(_undoPendingCompositeKey);
            if (isComposite)
            {
                // 새 항목을 쌓지 않는다 — 기존 맨 위 항목의 Snapshot(연속
                // 편집이 시작되기 전 상태)은 그대로 두고 라벨만 최신 값으로
                // 갈아 끼운다. 방금 캡처한 _undoPendingSnapshot(이번 클릭
                // 직전 상태)은 버려진다 — 필요 없다, 되돌아갈 목표는 여전히
                // "연속 편집 시작 전"이어야 하므로.
                _undoStack[_undoStack.Count - 1].Label = _undoPendingLabel;
                _undoStack[_undoStack.Count - 1].Team = _undoPendingTeam;
            }
            else
            {
                _undoStack.Add(new UndoEntry
                {
                    Snapshot = _undoPendingSnapshot,
                    Label = _undoPendingLabel,
                    Team = _undoPendingTeam,
                    CompositeKey = _undoPendingCompositeKey,
                });
            }
            _redoStack.Clear();
            _undoHistoryVersion++;
            ShowUndoLogEntry(_undoPendingLabel, _undoPendingTeam);
            // 멀티 연결 중이면 상대의 되돌리기 스택에도 똑같이 반영해달라고
            // 방송한다 — 이게 없으면 상대가 한 조작은 내 스택에, 내가 한
            // 조작은 상대 스택에 전혀 안 남아서, 나중에 누구든 카스케이드로
            // 되돌리면 그 사이 상대가 만들거나 지운 것까지 통째로
            // 덮어써버리는 문제가 있었다(사용자 보고 — "undo redo를
            // 복잡하게 조작하면 서로 꼬여서 없던게 생기고 있던게 사라짐").
            // 합치는 중이면 스냅샷 전체를 다시 보낼 필요가 없다 —
            // 상대에게도 "이미 있는 맨 위 항목의 라벨만 바꿔라"는 훨씬 작은
            // 메시지만 보낸다(BroadcastUndoRelabelIfNetworked).
            if (broadcast)
            {
                if (isComposite)
                {
                    BroadcastUndoRelabelIfNetworked(_undoPendingLabel, _undoPendingTeam);
                }
                else
                {
                    BroadcastUndoPushIfNetworked(_undoPendingSnapshot, _undoPendingLabel, _undoPendingTeam, _undoPendingCompositeKey);
                }
            }
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
            _undoPendingLabel = null;
            _undoPendingTeam = "";
            _undoPendingCompositeKey = "";
        }

        internal void DiscardUndoTransaction()
        {
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
            _undoPendingLabel = null;
            _undoPendingTeam = "";
            _undoPendingCompositeKey = "";
        }

        private void UndoOneStep()
        {
            if (_undoStack.Count == 0)
            {
                return;
            }
            var current = _board.CaptureBoardSnapshot();
            var entry = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(new UndoEntry { Snapshot = current, Label = entry.Label, Team = entry.Team, CompositeKey = entry.CompositeKey, Locked = entry.Locked });
            _board.RestoreBoardSnapshot(entry.Snapshot);
            _undoHistoryVersion++;
        }

        private void RedoOneStep()
        {
            if (_redoStack.Count == 0)
            {
                return;
            }
            var current = _board.CaptureBoardSnapshot();
            var entry = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(new UndoEntry { Snapshot = current, Label = entry.Label, Team = entry.Team, CompositeKey = entry.CompositeKey, Locked = entry.Locked });
            _board.RestoreBoardSnapshot(entry.Snapshot);
            _undoHistoryVersion++;
        }

        /// <summary>UndoHistoryDialog의 "조작 리스트" 항목을 클릭했을 때
        /// 부른다 — stackIndex는 그 항목의 _undoStack 안 위치(0-based, 오래된
        /// 것이 0). 그보다 나중(위)에 쌓인 것들도 전부 함께 취소되어 Undo
        /// 리스트로 넘어간다.</summary>
        internal void CancelOperationsDownTo(int stackIndex)
        {
            if (_board.IsUndoBlocked())
            {
                return;
            }
            // 잠긴(게임 도중 합류 이전) 항목은 조작 대상이 될 수 없다 —
            // UndoHistoryDialog가 이미 그 행을 클릭 못 하게 막아두지만, 방어적으로
            // 여기서도 한 번 더 막는다. 잠긴 항목은 항상 스택 맨 아래(가장 오래된
            // 쪽)에만 있으므로, 대상 자신만 확인하면 그보다 위(더 최근)는 전부
            // 안전하다고 보장된다(잠긴 항목보다 아래로는 취소가 내려가지 않으므로).
            if (stackIndex < 0 || stackIndex >= _undoStack.Count || _undoStack[stackIndex].Locked)
            {
                return;
            }
            int stepCount = _undoStack.Count - stackIndex;
            string topLabel = stepCount > 0 ? _undoStack[_undoStack.Count - 1].Label : null;
            string topTeam = stepCount > 0 ? _undoStack[_undoStack.Count - 1].Team : "";
            while (_undoStack.Count > stackIndex)
            {
                UndoOneStep();
            }
            BroadcastUndoCascadeIfNetworked(false, stackIndex);
            if (stepCount > 0)
            {
                ShowUndoCascadeLogEntry(isRedo: false, stepCount, topLabel, topTeam);
            }
        }

        /// <summary>UndoHistoryDialog의 "Undo 리스트" 항목을 클릭했을 때
        /// 부른다 — stackIndex는 그 항목의 _redoStack 안 위치(0-based, 가장
        /// 먼저 취소됐던 것이 0). 그 항목까지(포함, 즉 그보다 나중에 취소된
        /// 것들까지 전부) 복원되어 조작 리스트로 되돌아간다.</summary>
        internal void RestoreOperationsDownTo(int stackIndex)
        {
            if (_board.IsUndoBlocked())
            {
                return;
            }
            // CancelOperationsDownTo와 같은 방어적 잠금 확인 — 잠긴 항목은
            // CancelOperationsDownTo 쪽에서 이미 막혀 _redoStack으로 넘어올 수
            // 없으므로 실제로는 항상 통과하지만, 혹시 모를 경로를 위해 남겨둔다.
            if (stackIndex < 0 || stackIndex >= _redoStack.Count || _redoStack[stackIndex].Locked)
            {
                return;
            }
            int stepCount = _redoStack.Count - stackIndex;
            string topLabel = stepCount > 0 ? _redoStack[_redoStack.Count - 1].Label : null;
            string topTeam = stepCount > 0 ? _redoStack[_redoStack.Count - 1].Team : "";
            while (_redoStack.Count > stackIndex)
            {
                RedoOneStep();
            }
            BroadcastUndoCascadeIfNetworked(true, stackIndex);
            if (stepCount > 0)
            {
                ShowUndoCascadeLogEntry(isRedo: true, stepCount, topLabel, topTeam);
            }
        }

        /// <summary>커밋된 되돌리기 항목 하나를 상대에게도 알린다 — 상대는
        /// 이걸 받아 자기 자신의 _undoStack에 똑같은 항목을 쌓는다
        /// (ApplyRemoteUndoPush). 보드 자체를 여기서 다시 그리지는 않는다 —
        /// 그 변화는 이미 그 액션 전용 방송(마커/유닛/범위 등)으로 따로
        /// 오고 있다. 이렇게 두 클라이언트의 되돌리기 스택 내용을 항상
        /// 같은 순서로 맞춰두면, 나중에 어느 쪽이 카스케이드(되돌리기/다시
        /// 실행)를 하든 서로 어긋나지 않는다.</summary>
        private void BroadcastUndoPushIfNetworked(BoardSnapshot snapshot, string label, string team, string compositeKey)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 되돌리기 기록 공유를 못 보냄");
                return;
            }
            string json = MiniJson.Write(BuildSnapshotTree(snapshot));
            BoardNetworkSync.Instance.RequestBroadcastUndoPush(label, team ?? "", compositeKey ?? "", json);
        }

        /// <summary>상대가 커밋한 되돌리기 항목을 받았을 때 호출한다
        /// (BoardNetworkSync.BroadcastUndoPushChunkRpc — 자기 자신이 보낸
        /// 방송의 메아리는 이미 거기서 걸러진다). 로컬에서 직접 커밋한 것과
        /// 똑같은 모양으로 내 스택에 쌓는다 — 보드 자체는 안 건드린다(그
        /// 변화는 별도의 액션 전용 방송이 따로 반영한다).</summary>
        internal void ApplyRemoteUndoPush(string label, string team, string compositeKey, string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
            {
                Debug.LogError("[BoardManager] 되돌리기 기록 JSON 파싱 실패");
                return;
            }
            _undoStack.Add(new UndoEntry { Snapshot = ParseSnapshotTree(root), Label = label, Team = team ?? "", CompositeKey = compositeKey ?? "" });
            _redoStack.Clear();
            _undoHistoryVersion++;
            ShowUndoLogEntry(label, team);
        }

        /// <summary>연속 편집을 합치는 중일 때(CommitUndoTransaction의
        /// isComposite 분기) 상대에게 보낸다 — 스냅샷 전체를 다시 보내는
        /// 대신 "네 스택 맨 위 항목의 라벨만 이걸로 바꿔라"는 아주 작은
        /// 메시지 하나만 보낸다(청크 불필요, 이모트/드래프트 선택 RPC와
        /// 같은 이유). 두 스택은 이미 같은 순서로 맞춰져 있으므로
        /// (BroadcastUndoPushIfNetworked 덕분에), "맨 위"를 바꾸라는 것만으로
        /// 어느 항목인지 특정하는 데 충분하다.</summary>
        private void BroadcastUndoRelabelIfNetworked(string label, string team)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 되돌리기 라벨 갱신을 못 보냄");
                return;
            }
            BoardNetworkSync.Instance.RequestBroadcastUndoRelabel(label, team ?? "");
        }

        /// <summary>BoardNetworkSync.BroadcastUndoRelabelRpc가 방송을 받았을
        /// 때(자기 자신의 메아리는 이미 거기서 걸러진다) 호출한다 — 내
        /// 스택 맨 위 항목의 라벨만 갈아 끼운다(새 항목을 쌓지 않음).</summary>
        internal void ApplyRemoteUndoRelabel(string label, string team)
        {
            if (_undoStack.Count == 0)
            {
                Debug.LogError("[BoardManager] 되돌리기 라벨을 갱신할 항목이 없음 — 스택이 상대와 어긋났을 수 있음");
                return;
            }
            _undoStack[_undoStack.Count - 1].Label = label;
            _undoStack[_undoStack.Count - 1].Team = team ?? "";
            _undoHistoryVersion++;
            ShowUndoLogEntry(label, team);
        }

        /// <summary>되돌리기 카스케이드(CancelOperationsDownTo/
        /// RestoreOperationsDownTo)가 끝난 직후 호출한다 — 미연결이면 아무
        /// 것도 안 한다. 보드 상태를 통째로 다시 보내는 대신, "네 스택에서도
        /// 여기까지 취소/복원해라"는 목표 인덱스만 보낸다 — 상대의 스택도
        /// BroadcastUndoPushIfNetworked 덕에 내 것과 같은 내용이므로, 상대가
        /// 자기 스택으로 같은 카스케이드를 그대로 재생하면(ApplyRemoteUndoCascade)
        /// 결과 보드도 자연히 똑같아진다.</summary>
        private void BroadcastUndoCascadeIfNetworked(bool isRedo, int targetIndex)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 되돌리기 진행을 못 보냄");
                return;
            }
            BoardNetworkSync.Instance.RequestBroadcastUndoCascade(isRedo, targetIndex);
        }

        /// <summary>상대가 되돌리기/다시 실행 카스케이드를 수행했을 때 호출한다
        /// (BoardNetworkSync.BroadcastUndoCascadeRpc — 자기 자신의 메아리는
        /// 이미 거기서 걸러진다) — 내 스택으로 똑같은 카스케이드를 재생한다.
        /// 두 스택 내용이 이미 같으므로(BroadcastUndoPushIfNetworked 참고)
        /// 결과 보드도 상대와 같아진다.</summary>
        internal void ApplyRemoteUndoCascade(bool isRedo, int targetIndex)
        {
            // BroadcastUndoPushIfNetworked 덕에 이 시점엔 내 스택 내용이 상대와
            // 이미 같으므로, CancelOperationsDownTo/RestoreOperationsDownTo가
            // 로컬에서 하던 stepCount/topLabel 계산을 여기서도 그대로 반복해
            // 토스트를 똑같이 띄울 수 있다.
            if (isRedo)
            {
                int stepCount = _redoStack.Count - targetIndex;
                string topLabel = stepCount > 0 ? _redoStack[_redoStack.Count - 1].Label : null;
                string topTeam = stepCount > 0 ? _redoStack[_redoStack.Count - 1].Team : "";
                while (_redoStack.Count > targetIndex)
                {
                    RedoOneStep();
                }
                if (stepCount > 0)
                {
                    ShowUndoCascadeLogEntry(isRedo: true, stepCount, topLabel, topTeam);
                }
            }
            else
            {
                int stepCount = _undoStack.Count - targetIndex;
                string topLabel = stepCount > 0 ? _undoStack[_undoStack.Count - 1].Label : null;
                string topTeam = stepCount > 0 ? _undoStack[_undoStack.Count - 1].Team : "";
                while (_undoStack.Count > targetIndex)
                {
                    UndoOneStep();
                }
                if (stepCount > 0)
                {
                    ShowUndoCascadeLogEntry(isRedo: false, stepCount, topLabel, topTeam);
                }
            }
        }

        /// <summary>BoardSnapshot(캡처된 "이전" 보드 상태, 라이브 오브젝트가
        /// 아니라 값 복사본)을 방송용 JSON 트리로 직렬화한다 — BoardManager.
        /// Save.cs의 BuildUnitsTree 등과 모양은 같지만, 그건 라이브 baseLayer/
        /// markerLayer를 순회하는 반면 이건 스냅샷 자신의 내부 목록을 순회한다
        /// (읽는 대상이 다를 뿐 그 외엔 같은 이유로 같은 구조를 씀).</summary>
        private static Dictionary<string, object> BuildSnapshotTree(BoardSnapshot snapshot)
        {
            var rangesByUnit = new Dictionary<int, List<RangeSpec>>();
            foreach (var rangeSnap in snapshot.Ranges)
            {
                rangesByUnit[rangeSnap.UnitRef] = rangeSnap.Ranges;
            }

            var units = new List<object>();
            for (int i = 0; i < snapshot.Units.Count; i++)
            {
                var u = snapshot.Units[i];
                var models = new List<object>();
                foreach (var m in u.Models)
                {
                    models.Add(new Dictionary<string, object>
                    {
                        { "center", GameSaveIO.Vec2ToTree(m.Center) },
                        { "rotation_degrees", (double)m.RotationDegrees },
                        { "size_mm", GameSaveIO.Vec2ToTree(m.SizeMm) },
                        { "fill_color", GameSaveIO.ColorToTree(m.FillColor) },
                        { "damage", m.Damage },
                        { "is_displacement", m.IsDisplacement },
                        { "memo", m.Memo },
                    });
                }
                var ranges = rangesByUnit.TryGetValue(i, out var r) ? r : new List<RangeSpec>();
                units.Add(new Dictionary<string, object>
                {
                    { "network_unit_id", u.NetworkUnitId },
                    { "unit_name", u.UnitName }, { "team", u.Team },
                    { "coherency_inch", (double)u.CoherencyInch }, { "move_inch", (double)u.MoveInch },
                    { "is_token", u.IsToken }, { "can_move", u.CanMove },
                    { "supply_override", u.SupplyOverride },
                    { "supply_tiers", GameSaveIO.SupplyTiersToTree(u.SupplyTiers) },
                    { "ranges", GameSaveIO.RangesToTree(ranges) },
                    { "detail", GameSaveIO.DetailToTree(u.Detail) },
                    { "models", models },
                });
            }

            var pendingUnits = new List<object>();
            foreach (var def in snapshot.PendingUnits)
            {
                var damages = new List<object>();
                foreach (var d in def.Damages) damages.Add(d);
                var specialists = new List<object>();
                foreach (var s in def.Specialists) specialists.Add(s);
                pendingUnits.Add(new Dictionary<string, object>
                {
                    { "name", def.Name }, { "team", def.Team }, { "model_count", def.ModelCount },
                    { "size_mm", GameSaveIO.Vec2ToTree(def.SizeMm) }, { "fill_color", GameSaveIO.ColorToTree(def.FillColor) },
                    { "move_inch", (double)def.MoveInch }, { "coherency_inch", (double)def.CoherencyInch },
                    { "can_move", def.CanMove }, { "is_displacement", def.IsDisplacement },
                    { "supply_tiers", GameSaveIO.SupplyTiersToTree(def.SupplyTiers) },
                    { "damages", damages }, { "ranges", GameSaveIO.RangesToTree(def.Ranges) },
                    { "supply_override", def.SupplyOverride }, { "specialists", specialists },
                    { "detail", GameSaveIO.DetailToTree(def.Detail) },
                });
            }

            var pendingTokens = new List<object>();
            foreach (var def in snapshot.PendingRosterTokens)
            {
                pendingTokens.Add(new Dictionary<string, object>
                {
                    { "name", def.Name }, { "team", def.Team }, { "size_mm", GameSaveIO.Vec2ToTree(def.SizeMm) },
                    { "is_displacement", def.IsDisplacement }, { "ranges", GameSaveIO.RangesToTree(def.Ranges) },
                });
            }

            var markers = new List<object>();
            foreach (var m in snapshot.Markers)
            {
                markers.Add(new Dictionary<string, object>
                {
                    { "kind", m.Kind }, { "center", GameSaveIO.Vec2ToTree(m.Center) }, { "state", m.State },
                    { "network_marker_id", m.NetworkMarkerId },
                });
            }

            // 2026-09-04부터 정의 전체(Remaining만이 아니라)를 담는다 —
            // BoardSnapshot.TacticalCards 주석 참고. BuildTacticalCardsTree
            // (BoardManager.Save.cs)와 똑같은 모양으로 직렬화해서, 읽는 쪽도
            // 그 파싱기(ParseTacticalCardDefTree, BoardManager.Load.cs)를
            // 그대로 재사용할 수 있게 맞췄다.
            var tacticalCards = new List<object>();
            foreach (var def in snapshot.TacticalCards)
            {
                var abilities = new List<object>();
                foreach (var a in def.Abilities)
                {
                    abilities.Add(GameSaveIO.AbilityToTree(a));
                }
                tacticalCards.Add(new Dictionary<string, object>
                {
                    { "name", def.Name }, { "team", def.Team }, { "count", def.Count }, { "remaining", def.Remaining },
                    { "resource_abbr", def.ResourceAbbr }, { "resource_amount", def.ResourceAmount },
                    { "abilities", abilities },
                });
            }

            var rosterLoadedTeams = new List<object>();
            foreach (var team in snapshot.RosterLoadedTeams)
            {
                rosterLoadedTeams.Add(team);
            }

            var missionObjectiveStates = new List<object>();
            foreach (var s in snapshot.MissionObjectiveStates)
            {
                missionObjectiveStates.Add(new Dictionary<string, object> { { "number", s.Number }, { "ring_state", s.RingState } });
            }

            return new Dictionary<string, object>
            {
                { "units", units },
                { "pending_units", pendingUnits },
                { "pending_tokens", pendingTokens },
                { "markers", markers },
                { "tactical_cards", tacticalCards },
                // 점수판(2026-09-04 추가) — BoardSnapshot.RoundNumber 등 주석 참고.
                { "round_number", snapshot.RoundNumber },
                { "mission_vp_a", snapshot.MissionVpA }, { "mission_vp_b", snapshot.MissionVpB },
                { "kill_vp_a", snapshot.KillVpA }, { "kill_vp_b", snapshot.KillVpB },
                { "phase_index", snapshot.PhaseIndex },
                { "roster_loaded_teams", rosterLoadedTeams },
                { "mission_objective_states", missionObjectiveStates },
            };
        }

        /// <summary>BuildSnapshotTree의 역과정 — 상대가 보낸 JSON을 새
        /// BoardSnapshot 값 객체로 되돌린다(라이브 GameObject는 전혀 안
        /// 만든다 — 그건 이 스냅샷이 실제로 RestoreBoardSnapshot에 쓰일
        /// 때(카스케이드 재생 시점)에야 일어난다).</summary>
        private static BoardSnapshot ParseSnapshotTree(Dictionary<string, object> root)
        {
            var snapshot = new BoardSnapshot();

            foreach (var raw in GameSaveIO.GetList(root, "units"))
            {
                if (!(raw is Dictionary<string, object> u))
                {
                    continue;
                }
                var unitSnap = new UnitSnapshot
                {
                    NetworkUnitId = GameSaveIO.GetInt(u, "network_unit_id", -1),
                    UnitName = GameSaveIO.GetString(u, "unit_name"),
                    Team = GameSaveIO.GetString(u, "team", "neutral"),
                    CoherencyInch = GameSaveIO.GetFloat(u, "coherency_inch", GameConstants.DefaultCoherencyInch),
                    MoveInch = GameSaveIO.GetFloat(u, "move_inch", GameConstants.DefaultMoveInch),
                    IsToken = GameSaveIO.GetBool(u, "is_token"),
                    CanMove = GameSaveIO.GetBool(u, "can_move", true),
                    SupplyOverride = GameSaveIO.GetNullableInt(u, "supply_override"),
                    Detail = GameSaveIO.DetailFromTree(u.TryGetValue("detail", out var detailRaw) ? detailRaw : null),
                };
                unitSnap.SupplyTiers.AddRange(GameSaveIO.TreeToSupplyTiers(GameSaveIO.GetList(u, "supply_tiers")));

                foreach (var rawModel in GameSaveIO.GetList(u, "models"))
                {
                    if (!(rawModel is Dictionary<string, object> m))
                    {
                        continue;
                    }
                    unitSnap.Models.Add(new ModelSnapshot
                    {
                        Center = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(m, "center")),
                        RotationDegrees = GameSaveIO.GetFloat(m, "rotation_degrees"),
                        SizeMm = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(m, "size_mm")),
                        FillColor = GameSaveIO.TreeToColor(GameSaveIO.GetDict(m, "fill_color")),
                        Damage = GameSaveIO.GetInt(m, "damage"),
                        IsDisplacement = GameSaveIO.GetBool(m, "is_displacement"),
                        Memo = GameSaveIO.GetString(m, "memo"),
                    });
                }

                int unitIndex = snapshot.Units.Count;
                snapshot.Units.Add(unitSnap);

                var ranges = GameSaveIO.TreeToRanges(GameSaveIO.GetList(u, "ranges"));
                if (ranges.Count > 0)
                {
                    snapshot.Ranges.Add(new RangeSnapshot { UnitRef = unitIndex, Ranges = ranges });
                }
            }

            foreach (var raw in GameSaveIO.GetList(root, "pending_units"))
            {
                if (raw is Dictionary<string, object> p)
                {
                    snapshot.PendingUnits.Add(BoardManager.ParsePendingUnitDefTree(p));
                }
            }

            foreach (var raw in GameSaveIO.GetList(root, "pending_tokens"))
            {
                if (!(raw is Dictionary<string, object> t))
                {
                    continue;
                }
                snapshot.PendingRosterTokens.Add(new PendingTokenDef
                {
                    Name = GameSaveIO.GetString(t, "name"),
                    Team = GameSaveIO.GetString(t, "team", "neutral"),
                    SizeMm = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(t, "size_mm")),
                    IsDisplacement = GameSaveIO.GetBool(t, "is_displacement"),
                    Ranges = GameSaveIO.TreeToRanges(GameSaveIO.GetList(t, "ranges")),
                });
            }

            foreach (var raw in GameSaveIO.GetList(root, "markers"))
            {
                if (raw is Dictionary<string, object> m)
                {
                    snapshot.Markers.Add(new MarkerSnapshot
                    {
                        Kind = GameSaveIO.GetString(m, "kind"),
                        Center = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(m, "center")),
                        State = GameSaveIO.GetString(m, "state"),
                        NetworkMarkerId = GameSaveIO.GetInt(m, "network_marker_id", -1),
                    });
                }
            }

            // ParseTacticalCardDefTree(BoardManager.Load.cs)를 그대로
            // 재사용한다 — BuildTacticalCardsTree(파일 저장)가 쓰는 것과
            // 완전히 같은 트리 모양이라(위 BuildSnapshotTree 참고) 파서도
            // 공유할 수 있다.
            foreach (var raw in GameSaveIO.GetList(root, "tactical_cards"))
            {
                if (raw is Dictionary<string, object> c)
                {
                    snapshot.TacticalCards.Add(BoardManager.ParseTacticalCardDefTree(c));
                }
            }

            snapshot.RoundNumber = GameSaveIO.GetInt(root, "round_number", 1);
            snapshot.MissionVpA = GameSaveIO.GetInt(root, "mission_vp_a");
            snapshot.MissionVpB = GameSaveIO.GetInt(root, "mission_vp_b");
            snapshot.KillVpA = GameSaveIO.GetInt(root, "kill_vp_a");
            snapshot.KillVpB = GameSaveIO.GetInt(root, "kill_vp_b");
            snapshot.PhaseIndex = GameSaveIO.GetInt(root, "phase_index");

            foreach (var raw in GameSaveIO.GetList(root, "roster_loaded_teams"))
            {
                if (raw is string team)
                {
                    snapshot.RosterLoadedTeams.Add(team);
                }
            }

            foreach (var raw in GameSaveIO.GetList(root, "mission_objective_states"))
            {
                if (raw is Dictionary<string, object> s)
                {
                    snapshot.MissionObjectiveStates.Add(new MissionObjectiveStateSnapshot
                    {
                        Number = GameSaveIO.GetInt(s, "number"),
                        RingState = GameSaveIO.GetString(s, "ring_state"),
                    });
                }
            }

            return snapshot;
        }

        /// <summary>리플레이(BoardManager.Replay.cs, 2026-09-03 추가) 전용 —
        /// 저장 파일의 "undo_history" 트리에서 undo_stack만(설계대로
        /// redo_stack은 무시 — 실제로 일어난 일이 아닌, 취소됐던 갈래라
        /// 재생에서 보여줄 이유가 없다) 시간순 그대로 파싱해 돌려준다.
        /// ApplySeededUndoHistory와 달리 살아있는 스택을 안 건드리고
        /// (부작용 없음), 항목 하나하나를 새 BoardSnapshot으로 만들어
        /// 리스트에 담기만 한다 — RestoreBoardSnapshot으로 순서대로
        /// 보여주는 건 호출부(BoardManager.Replay.cs) 몫. root가 null이면
        /// (undo_history 없는 옛 저장 파일) 빈 리스트를 돌려준다 —
        /// 호출부가 "현재 상태" 프레임 하나만으로도 우아하게 재생할 수
        /// 있게.</summary>
        internal static List<(BoardSnapshot Snapshot, string Label, string Team)> ParseOrderedUndoStack(Dictionary<string, object> root)
        {
            var list = new List<(BoardSnapshot, string, string)>();
            if (root == null)
            {
                return list;
            }
            foreach (var raw in GameSaveIO.GetList(root, "undo_stack"))
            {
                if (raw is Dictionary<string, object> e)
                {
                    list.Add((ParseSnapshotTree(GameSaveIO.GetDict(e, "snapshot")), GameSaveIO.GetString(e, "label"), GameSaveIO.GetString(e, "team")));
                }
            }
            return list;
        }

        /// <summary>UndoHistoryDialog가 하나로 합쳐진 시간순 목록을 그릴 때
        /// 부른다(2026-09-01 재구성 — 예전엔 "조작 리스트"/"Undo 리스트" 두
        /// 목록으로 따로 보여줬는데, 사용자 요청으로 하나로 합쳤다). 실제
        /// 수행됐던 순서 기준 최근 것이 맨 위: 먼저 취소된(=원래 더 최근에
        /// 수행됐던) _redoStack 항목들을 앞쪽 배열 순서 그대로, 이어서 아직
        /// 안 취소된 _undoStack 항목들을 뒤집어서 붙인다 — 카스케이드가 두
        /// 스택을 쌓는 방식(각각 "가장 최근 것이 배열 반대쪽 끝") 때문에
        /// 이 조합이 정확히 시간 역순이 된다. IsUndone인 항목은 StackIndex가
        /// _redoStack 기준(RestoreOperationsDownTo로), 아니면 _undoStack
        /// 기준(CancelOperationsDownTo로) 이다.</summary>
        internal List<(string Label, int StackIndex, bool IsUndone, string Team, bool Locked)> GetCombinedHistoryForDisplay()
        {
            var list = new List<(string, int, bool, string, bool)>();
            for (int i = 0; i < _redoStack.Count; i++)
            {
                list.Add((_redoStack[i].Label, i, true, _redoStack[i].Team, _redoStack[i].Locked));
            }
            for (int i = _undoStack.Count - 1; i >= 0; i--)
            {
                list.Add((_undoStack[i].Label, i, false, _undoStack[i].Team, _undoStack[i].Locked));
            }
            return list;
        }

        /// <summary>되돌리기 스택 전체(라벨+스냅샷, 순서 그대로)를 트리로
        /// 만든다. 두 호출부가 있고 includeLocked로 갈린다:
        /// - 게임 도중 멀티 합류 전송(BoardManager.MidGameHandoff.cs,
        ///   includeLocked: true) — 합류 직전 LockExistingUndoHistoryForMidGameJoin
        ///   이 표시해둔 Locked를 그대로 실어 보내야, 받는 쪽도 같은 항목을
        ///   조작 대상에서 뺄 수 있다.
        /// - 저장 파일(BoardManager.Save.cs, includeLocked: false) — Locked는
        ///   "지금 이 멀티 세션이 살아있는 동안만" 의미 있는 런타임 플래그일
        ///   뿐, 저장 데이터에 들어갈 이유가 없다(사용자 지적, 2026-09-09 —
        ///   "세이브 파일엔 잠금 여부가 포함될 이유가 없지 않아?"). 아예 그
        ///   키 자체를 안 쓴다 — ApplySeededUndoHistory가 이미 GameSaveIO.
        ///   GetBool(..., fallback: false)로 읽으므로, 키가 없으면 그냥 항상
        ///   Locked=false로 불러와진다(옛 저장 파일과 똑같이 처리됨, 코드
        ///   변경 불필요).</summary>
        internal Dictionary<string, object> BuildUndoHistoryTree(bool includeLocked)
        {
            var undoList = new List<object>();
            foreach (var entry in _undoStack)
            {
                undoList.Add(BuildUndoEntryTree(entry, includeLocked));
            }
            var redoList = new List<object>();
            foreach (var entry in _redoStack)
            {
                redoList.Add(BuildUndoEntryTree(entry, includeLocked));
            }
            return new Dictionary<string, object> { { "undo_stack", undoList }, { "redo_stack", redoList } };
        }

        private static Dictionary<string, object> BuildUndoEntryTree(UndoEntry entry, bool includeLocked)
        {
            var tree = new Dictionary<string, object> { { "label", entry.Label }, { "team", entry.Team }, { "snapshot", BuildSnapshotTree(entry.Snapshot) } };
            if (includeLocked)
            {
                tree["locked"] = entry.Locked;
            }
            return tree;
        }

        /// <summary>BuildUndoHistoryTree의 역과정 — 게임 도중 멀티 합류로 막
        /// 만들어진(텅 빈) 내 스택을 호스트가 보내준 내용으로 그대로
        /// 채운다. 배열 순서 자체가 이미 스택 순서라 그대로 옮기기만 하면
        /// 된다. `locked` 플래그도 그대로 옮겨서, 합류 이전 항목은 호스트와
        /// 똑같이 이 클라이언트에서도 조작 대상이 될 수 없게 막힌다
        /// (LockExistingUndoHistoryForMidGameJoin/UndoEntry.Locked 참고).
        ///
        /// 예전엔(2026-09-02) "합류 이전 스냅샷에 박제된 옛 NetworkUnitId
        /// (백필 이전 값, -1)로 되돌리면 유닛/마커가 중복 생성될 수 있다"는
        /// 문제를 히스토리 자체를 합류 시점에 통째로 지워버리는 걸로
        /// 해결했었다(ClearUndoHistoryForMidGameJoin, 사용자가 실제로 버그를
        /// 재현해 보인 뒤 내린 판단). 2026-09-04, 사용자가 "지울 필요
        /// 없다, 리스트는 다 보여주고 조작만 막아달라"고 재요청 — 데이터는
        /// 보존하고 조작만 잠그는 쪽으로 다시 바꿨다.</summary>
        internal void ApplySeededUndoHistory(Dictionary<string, object> root)
        {
            _undoStack.Clear();
            _redoStack.Clear();
            foreach (var raw in GameSaveIO.GetList(root, "undo_stack"))
            {
                if (raw is Dictionary<string, object> e)
                {
                    _undoStack.Add(new UndoEntry { Label = GameSaveIO.GetString(e, "label"), Team = GameSaveIO.GetString(e, "team"), Locked = GameSaveIO.GetBool(e, "locked"), Snapshot = ParseSnapshotTree(GameSaveIO.GetDict(e, "snapshot")) });
                }
            }
            foreach (var raw in GameSaveIO.GetList(root, "redo_stack"))
            {
                if (raw is Dictionary<string, object> e)
                {
                    _redoStack.Add(new UndoEntry { Label = GameSaveIO.GetString(e, "label"), Team = GameSaveIO.GetString(e, "team"), Locked = GameSaveIO.GetBool(e, "locked"), Snapshot = ParseSnapshotTree(GameSaveIO.GetDict(e, "snapshot")) });
                }
            }
            _undoHistoryVersion++; // 되돌리기 모달이 열려 있었다면 다시 그리도록.
        }

        /// <summary>BroadcastFullStateForMidGameJoin이 상태를 내보내기 직전에
        /// 부른다 — 합류 이전에 쌓여있던 되돌리기 히스토리를 지우지 않고
        /// 그대로 둔 채, 그 안의 기존 항목 전부(양쪽 스택 다)에
        /// Locked=true만 표시한다(2026-09-04 재구성, 사용자 요청 — "되돌리기
        /// 리스트를 지울 필욘 없어. 히스토리에 있는 모든 항목을 보여주되,
        /// 조작만 불가능해 보이게 처리해줘"). 원래(2026-09-02)는 통째로
        /// 지워버리는 방식이었다 — 그 안에 박제된 옛(백필 이전) 네트워크 id로
        /// 되돌리면 유닛/마커가 중복 생성되는 버그를, 사용자가 직접 재현해
        /// 보인 뒤 "히스토리 자체를 없애서" 막기로 했던 결정. 지금은 데이터를
        /// 지우는 대신 CancelOperationsDownTo/RestoreOperationsDownTo와
        /// UndoHistoryDialog 양쪽에서 Locked 항목을 대상으로 한 조작 자체를
        /// 막아 같은 안전성을 유지한다(UndoEntry.Locked 참고). 합류 시점부터
        /// 새로 쌓이는 항목은 Locked=false로, 이미 backfill된 살아있는 상태를
        /// 기준으로 캡처되므로 안전하게 조작할 수 있다.</summary>
        internal void LockExistingUndoHistoryForMidGameJoin()
        {
            foreach (var entry in _undoStack)
            {
                entry.Locked = true;
            }
            foreach (var entry in _redoStack)
            {
                entry.Locked = true;
            }
            _undoHistoryVersion++;
        }

        /// <summary>상대와의 연결이 끊겨 멀티 세션이 끝났을 때 부른다
        /// (DisconnectNoticeController.OnClientDisconnected) — 위 잠금을 푼다.
        /// 잠금 자체가 "멀티 플레이 중 사고(중복 생성) 방지" 목적으로만
        /// 걸리는 것이라(사용자 지정: "잠기는건 오직 멀티 플레이에서 사고를
        /// 방지하기 위한 조치니까"), 세션이 끝나면 그 위험도 같이 끝난다 —
        /// 계속 잠긴 채로 남겨둘 이유가 없다.</summary>
        internal void UnlockAllUndoHistoryAfterMultiplayerEnded()
        {
            foreach (var entry in _undoStack)
            {
                entry.Locked = false;
            }
            foreach (var entry in _redoStack)
            {
                entry.Locked = false;
            }
            _undoHistoryVersion++;
        }

        // ── 되돌리기 로그(2026-09-02 신설, 2026-09-09 토스트→영구 로그로
        // 교체) ─────────────────────────────────────────────────────────
        // 조작이 하나 기록되거나(CommitUndoTransaction) 되돌리기/복원
        // 카스케이드가 일어날 때마다 화면 좌측 하단 채팅/로그 창에 한 줄
        // 남긴다. 실제 GameObject 생성/스크롤은 ChatController(영구 싱글턴,
        // Multiplayer/ChatController.cs)의 공용 로그가 담당한다(원래는
        // 잠깐 떴다 사라지는 토스트였는데, 사용자 요청으로 채팅과 합쳐
        // 스크롤 가능한 영구 로그가 됐다 — PushToast였던 자리가 이제
        // PushLogEntry). 멀티 연결 중이면 상대 화면에도 똑같이 뜬다
        // (2026-09-02, 사용자 요청으로 확장) — 새 RPC 없이, 이미 스택
        // 동기화를 위해 흐르고 있던 데이터를 재사용한다: 커밋은
        // ApplyRemoteUndoPush가 받은 label을, 카스케이드는
        // ApplyRemoteUndoCascade가 (두 스택이 이미 상대와 같은 내용이므로)
        // CancelOperationsDownTo/RestoreOperationsDownTo와 똑같은 방식으로
        // 직접 계산한 stepCount/topLabel을 그대로 써서 로컬에서 한 번 더
        // ShowUndoLogEntry/ShowUndoCascadeLogEntry를 부른다.

        private void ShowUndoLogEntry(string message, string team = "")
        {
            ChatController.Instance?.PushLogEntry(message, team);
        }

        private void ShowUndoCascadeLogEntry(bool isRedo, int stepCount, string topLabel, string topTeam)
        {
            string verb = isRedo ? "복원" : "되돌림";
            string message = stepCount > 1 ? $"{verb}: {topLabel} 외 {stepCount - 1}건" : $"{verb}: {topLabel}";
            ShowUndoLogEntry(message, topTeam);
        }
    }
}
