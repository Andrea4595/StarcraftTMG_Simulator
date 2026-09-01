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
        // 복원 가능) 두 목록을 보여준다. 목록 중 아무 항목이나 골라 취소/
        // 복원하면 그 지점까지 전부 한꺼번에 처리된다 — 보드 전체 스냅샷을
        // 쌓는 방식이라 중간 항목 하나만 독립적으로(그 뒤 조작은 그대로 둔
        // 채) 취소하는 건 인과관계상 불가능/무의미하기 때문(사용자와 확인한
        // 설계). 새 조작을 하나 커밋하면 Undo 리스트(redo 스택)는 기존과
        // 동일하게 비워진다(그 시점부터 "미래"가 갈라지므로).
        //
        // 각 스냅샷에 이제 사람이 읽을 수 있는 설명(Label)이 같이 붙는다 —
        // 리스트에 보여주기 위해 필요해졌다(예전엔 스냅샷에 설명이 전혀
        // 없었다). BeginUndoTransaction(label)을 부르는 모든 곳이 그 라벨을
        // 정한다.

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
        // 다시 그린다. 로컬 커밋/카스케이드는 물론 상대에게서 받은
        // ApplyRemoteUndoPush/ApplyRemoteUndoCascade도 UndoOneStep/RedoOneStep을
        // 그대로 타므로 여기 한 곳만 올리면 전부 커버된다.
        private int _undoHistoryVersion;

        internal int UndoHistoryVersion => _undoHistoryVersion;

        // 2026-09-04부터 internal — 스코어보드(ScoreboardPanel.cs)가 라운드/VP
        // 조작을 되돌리기에 편입시키면서, BoardManager 밖에서 처음으로 이
        // 트랜잭션 메서드들을 직접 부를 필요가 생겼다(그 전까진 전부
        // BoardManager 자신의 파셜 클래스 파일 안에서만 쓰였다).
        internal void BeginUndoTransaction(string label, string team = "", string compositeKey = "")
        {
            if (_undoPendingActive)
            {
                return;
            }
            _undoPendingSnapshot = CaptureBoardSnapshot();
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
            ShowUndoToast(_undoPendingLabel, _undoPendingTeam);
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

        private void UndoOneStep()
        {
            if (_undoStack.Count == 0)
            {
                return;
            }
            var current = CaptureBoardSnapshot();
            var entry = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(new UndoEntry { Snapshot = current, Label = entry.Label, Team = entry.Team, CompositeKey = entry.CompositeKey, Locked = entry.Locked });
            RestoreBoardSnapshot(entry.Snapshot);
            _undoHistoryVersion++;
        }

        private void RedoOneStep()
        {
            if (_redoStack.Count == 0)
            {
                return;
            }
            var current = CaptureBoardSnapshot();
            var entry = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(new UndoEntry { Snapshot = current, Label = entry.Label, Team = entry.Team, CompositeKey = entry.CompositeKey, Locked = entry.Locked });
            RestoreBoardSnapshot(entry.Snapshot);
            _undoHistoryVersion++;
        }

        /// <summary>UndoHistoryDialog의 "조작 리스트" 항목을 클릭했을 때
        /// 부른다 — stackIndex는 그 항목의 _undoStack 안 위치(0-based, 오래된
        /// 것이 0). 그보다 나중(위)에 쌓인 것들도 전부 함께 취소되어 Undo
        /// 리스트로 넘어간다.</summary>
        internal void CancelOperationsDownTo(int stackIndex)
        {
            if (IsUndoBlocked())
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
                ShowUndoCascadeToast(isRedo: false, stepCount, topLabel, topTeam);
            }
        }

        /// <summary>UndoHistoryDialog의 "Undo 리스트" 항목을 클릭했을 때
        /// 부른다 — stackIndex는 그 항목의 _redoStack 안 위치(0-based, 가장
        /// 먼저 취소됐던 것이 0). 그 항목까지(포함, 즉 그보다 나중에 취소된
        /// 것들까지 전부) 복원되어 조작 리스트로 되돌아간다.</summary>
        internal void RestoreOperationsDownTo(int stackIndex)
        {
            if (IsUndoBlocked())
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
                ShowUndoCascadeToast(isRedo: true, stepCount, topLabel, topTeam);
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
            ShowUndoToast(label, team);
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
            ShowUndoToast(label, team);
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
                    ShowUndoCascadeToast(isRedo: true, stepCount, topLabel, topTeam);
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
                    ShowUndoCascadeToast(isRedo: false, stepCount, topLabel, topTeam);
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
                    snapshot.PendingUnits.Add(ParsePendingUnitDefTree(p));
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
                    snapshot.TacticalCards.Add(ParseTacticalCardDefTree(c));
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

        /// <summary>게임 도중 멀티 합류(BoardManager.MidGameHandoff.cs) 전용 —
        /// 지금 내 되돌리기 스택 전체(라벨+스냅샷+Locked, 순서 그대로)를
        /// 상대에게 그대로 넘겨준다. 2026-09-04부터(사용자 요청 — "되돌리기
        /// 리스트를 지울 필욘 없어") 호출부(BroadcastFullStateForMidGameJoin)가
        /// 이 메서드를 부르기 *직전에* 하던 일이 "통째로 비우기"에서 "합류
        /// 이전 항목을 Locked로 표시"로 바뀌었다 — 그래서 이제 이 트리는
        /// 실제 히스토리 전체(과거분은 Locked=true인 채로)를 담아 보낸다.</summary>
        internal Dictionary<string, object> BuildUndoHistoryTree()
        {
            var undoList = new List<object>();
            foreach (var entry in _undoStack)
            {
                undoList.Add(new Dictionary<string, object> { { "label", entry.Label }, { "team", entry.Team }, { "locked", entry.Locked }, { "snapshot", BuildSnapshotTree(entry.Snapshot) } });
            }
            var redoList = new List<object>();
            foreach (var entry in _redoStack)
            {
                redoList.Add(new Dictionary<string, object> { { "label", entry.Label }, { "team", entry.Team }, { "locked", entry.Locked }, { "snapshot", BuildSnapshotTree(entry.Snapshot) } });
            }
            return new Dictionary<string, object> { { "undo_stack", undoList }, { "redo_stack", redoList } };
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
            }

            return snapshot;
        }

        /// <summary>보드 위 유닛/모델/마커를 전부 지우고 관련 보조 상태를
        /// 초기화한다 — RestoreBoardSnapshot이 실제로 되살리기 직전에 호출하는
        /// 공통 첫 단계. _networkedUnitsById도 함께 비운다 — 예전엔 여기
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
            _networkedUnitsById.Clear();
            // 2026-09-02 추가 — 마커도 유닛과 같은 이유로 비워야 한다. 지워지는
            // 마커들을 가리키던 항목을 그대로 두면 파괴된 인스턴스를 참조하게
            // 되고(유닛 쪽과 같은 문제), MarkerSnapshot.NetworkMarkerId를 이제
            // 그대로 되살려서 바로 아래에서 다시 채운다.
            _networkedMarkersById.Clear();
        }

        private void RestoreBoardSnapshot(BoardSnapshot snapshot)
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
                    // ClearLiveBoardState가 이 되살리기 직전에 _networkedUnitsById를
                    // 이미 비워뒀다 — 여기서 다시 채워야 이후 이 유닛을 다시
                    // 옮기거나 범위를 추가할 때(BroadcastUnitIfNetworked 등) 상대와
                    // 같은 id로 계속 짝지어진다.
                    _networkedUnitsById[unit.NetworkUnitId] = unit;
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
                        _networkedMarkersById[markerSnap.NetworkMarkerId] = marker;
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

        private class UnitSnapshot
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
            // 2026-09-02 추가 — 이게 없어서 멀티 중 되돌리기/다시실행을 한 번만
            // 해도 마커 전체가 네트워크 id를 잃어버려(RestoreBoardSnapshot이
            // 마커를 통째로 새로 만들면서 id를 안 이어받았음) 그 뒤 삭제/상태
            // 순환 방송이 전부 씹히는 버그가 있었다(사용자 보고로 발견) —
            // UnitSnapshot.NetworkUnitId와 완전히 같은 이유로 같은 자리에 추가.
            public int NetworkMarkerId = -1;
        }

        private class BoardSnapshot
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

        private class MissionObjectiveStateSnapshot
        {
            public int Number;
            public string RingState;
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

        // ── 되돌리기 토스트(2026-09-02 신설, 사용자 요청) ────────────────
        // 조작이 하나 기록되거나(CommitUndoTransaction) 되돌리기/복원
        // 카스케이드가 일어날 때마다 화면 좌측 하단 구석에 잠깐 띄운다.
        // 실제 GameObject 생성/페이드/쌓기는 ChatController(영구 싱글턴,
        // Multiplayer/ChatController.cs)의 공용 토스트 스택이 담당한다
        // (2026-09-04 재구성 — 채팅 기능이 이 스택을 그대로 재사용하게
        // 되면서 BoardManager 밖으로 옮겼다. 아래 ShowUndoToast 참고).
        // 멀티 연결 중이면 상대 화면에도 똑같이 뜬다(2026-09-02, 사용자
        // 요청으로 확장) — 새 RPC 없이, 이미 스택 동기화를 위해 흐르고 있던
        // 데이터를 재사용한다: 커밋은 ApplyRemoteUndoPush가 받은 label을,
        // 카스케이드는 ApplyRemoteUndoCascade가 (두 스택이 이미 상대와 같은
        // 내용이므로) CancelOperationsDownTo/RestoreOperationsDownTo와
        // 똑같은 방식으로 직접 계산한 stepCount/topLabel을 그대로 써서
        // 로컬에서 한 번 더 ShowUndoToast/ShowUndoCascadeToast를 부른다.
        //
        // 실제 토스트 GameObject 생성/페이드/쌓기는 이제 BoardManager 안이
        // 아니라 ChatController(영구 싱글턴, Multiplayer/ChatController.cs)가
        // 담당한다(2026-09-04 재구성) — 채팅 기능이 CardPrep/CardDraft/
        // TerrainSetup/GameBoard 전 화면에서 떠야 하는데, BoardManager는
        // GameBoard 씬에서만(코드로) 지어지는 컴포넌트라 그 안에 토스트
        // 스택을 계속 두면 채팅과 공유할 수 없었다. 사용자가 "채팅은 토스트
        // 메시지를 그대로 활용"이라고 명시해서, 되돌리기/전술카드 토스트도
        // 채팅 메시지와 완전히 같은 스택에 함께 뜨도록 그쪽으로 옮겼다.

        private void ShowUndoToast(string message, string team = "")
        {
            ChatController.Instance?.PushToast(message, team);
        }

        private void ShowUndoCascadeToast(bool isRedo, int stepCount, string topLabel, string topTeam)
        {
            string verb = isRedo ? "복원" : "되돌림";
            string message = stepCount > 1 ? $"{verb}: {topLabel} 외 {stepCount - 1}건" : $"{verb}: {topLabel}";
            ShowUndoToast(message, topTeam);
        }
    }
}
