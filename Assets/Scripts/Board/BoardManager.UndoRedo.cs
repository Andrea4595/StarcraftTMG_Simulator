using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
        }

        private readonly List<UndoEntry> _undoStack = new List<UndoEntry>();
        private readonly List<UndoEntry> _redoStack = new List<UndoEntry>();
        private bool _undoPendingActive;
        private BoardSnapshot _undoPendingSnapshot;
        private string _undoPendingLabel;

        private void BeginUndoTransaction(string label)
        {
            if (_undoPendingActive)
            {
                return;
            }
            _undoPendingSnapshot = CaptureBoardSnapshot();
            _undoPendingLabel = label;
            _undoPendingActive = true;
        }

        private void CommitUndoTransaction()
        {
            if (!_undoPendingActive)
            {
                return;
            }
            _undoStack.Add(new UndoEntry { Snapshot = _undoPendingSnapshot, Label = _undoPendingLabel });
            _redoStack.Clear();
            // 멀티 연결 중이면 상대의 되돌리기 스택에도 똑같은 항목을
            // 쌓아달라고 방송한다 — 이게 없으면 상대가 한 조작은 내
            // 스택에, 내가 한 조작은 상대 스택에 전혀 안 남아서, 나중에
            // 누구든 카스케이드로 되돌리면 그 사이 상대가 만들거나 지운
            // 것까지 통째로 덮어써버리는 문제가 있었다(사용자 보고 —
            // "undo redo를 복잡하게 조작하면 서로 꼬여서 없던게 생기고
            // 있던게 사라짐"). 아래 참고.
            BroadcastUndoPushIfNetworked(_undoPendingSnapshot, _undoPendingLabel);
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
            _undoPendingLabel = null;
        }

        private void DiscardUndoTransaction()
        {
            _undoPendingActive = false;
            _undoPendingSnapshot = null;
            _undoPendingLabel = null;
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
            _redoStack.Add(new UndoEntry { Snapshot = current, Label = entry.Label });
            RestoreBoardSnapshot(entry.Snapshot);
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
            _undoStack.Add(new UndoEntry { Snapshot = current, Label = entry.Label });
            RestoreBoardSnapshot(entry.Snapshot);
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
            while (_undoStack.Count > stackIndex)
            {
                UndoOneStep();
            }
            BroadcastUndoCascadeIfNetworked(false, stackIndex);
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
            while (_redoStack.Count > stackIndex)
            {
                RedoOneStep();
            }
            BroadcastUndoCascadeIfNetworked(true, stackIndex);
        }

        /// <summary>커밋된 되돌리기 항목 하나를 상대에게도 알린다 — 상대는
        /// 이걸 받아 자기 자신의 _undoStack에 똑같은 항목을 쌓는다
        /// (ApplyRemoteUndoPush). 보드 자체를 여기서 다시 그리지는 않는다 —
        /// 그 변화는 이미 그 액션 전용 방송(마커/유닛/범위 등)으로 따로
        /// 오고 있다. 이렇게 두 클라이언트의 되돌리기 스택 내용을 항상
        /// 같은 순서로 맞춰두면, 나중에 어느 쪽이 카스케이드(되돌리기/다시
        /// 실행)를 하든 서로 어긋나지 않는다.</summary>
        private void BroadcastUndoPushIfNetworked(BoardSnapshot snapshot, string label)
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
            BoardNetworkSync.Instance.RequestBroadcastUndoPush(label, json);
        }

        /// <summary>상대가 커밋한 되돌리기 항목을 받았을 때 호출한다
        /// (BoardNetworkSync.BroadcastUndoPushChunkRpc — 자기 자신이 보낸
        /// 방송의 메아리는 이미 거기서 걸러진다). 로컬에서 직접 커밋한 것과
        /// 똑같은 모양으로 내 스택에 쌓는다 — 보드 자체는 안 건드린다(그
        /// 변화는 별도의 액션 전용 방송이 따로 반영한다).</summary>
        internal void ApplyRemoteUndoPush(string label, string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
            {
                Debug.LogError("[BoardManager] 되돌리기 기록 JSON 파싱 실패");
                return;
            }
            _undoStack.Add(new UndoEntry { Snapshot = ParseSnapshotTree(root), Label = label });
            _redoStack.Clear();
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
            if (isRedo)
            {
                while (_redoStack.Count > targetIndex)
                {
                    RedoOneStep();
                }
            }
            else
            {
                while (_undoStack.Count > targetIndex)
                {
                    UndoOneStep();
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
                });
            }

            return new Dictionary<string, object>
            {
                { "units", units },
                { "pending_units", pendingUnits },
                { "pending_tokens", pendingTokens },
                { "markers", markers },
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
        internal List<(string Label, int StackIndex, bool IsUndone)> GetCombinedHistoryForDisplay()
        {
            var list = new List<(string, int, bool)>();
            for (int i = 0; i < _redoStack.Count; i++)
            {
                list.Add((_redoStack[i].Label, i, true));
            }
            for (int i = _undoStack.Count - 1; i >= 0; i--)
            {
                list.Add((_undoStack[i].Label, i, false));
            }
            return list;
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
