using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 유닛 배치/이동 동기화 ──────────────────────────────────────
        // 마커와 같은 원칙: 유닛/모델(Base) 자체를 NetworkObject로 스폰하지
        // 않는다(UI 계층 재부모화 문제, 이미 마커에서 겪음). 대신 유닛 이동
        // 워크플로우(리딩 모델 배치→팔로워 배치→완료, BoardManager.UnitMove.cs)가
        // "완료"되는 그 순간에만(중간 드래그 과정은 실시간으로 안 보냄 —
        // 사용자 지정 "한 번에 한 사람만 조작" 모델과도 맞음) 그 유닛
        // 전체를 BuildUnitTree로 직렬화해서 전체에 방송하고, 받는 쪽은
        // ApplyUnitTree로 로컬 재구성한다. 로스터 JSON과 같은 이유로 JSON
        // 텍스트를 그대로 청크 단위로 나눠 보낸다(BoardNetworkSync 참고 —
        // 한 RPC에 큰 문자열을 실으면 오버플로우).

        // 멀티플레이어 중 방송된 유닛만 등록됨(id는 첫 방송 시 이 클라이언트가
        // 스스로 발급 — BoardNetworkSync.RequestBroadcastUnit) — 재이동 방송이
        // 왔을 때 "새 유닛"이 아니라 "이 유닛 갱신"임을 알아보는 용도.
        // 필드 자체는 BoardManager.cs의 _networkedUnits(NetworkIdentityRegistry)로
        // 옮겼다(2026-09-02, 리팩토링 Phase 1) — Range.cs/UndoRedo.cs/
        // MidGameHandoff.cs도 직접 쓰던 사실상 전역 상태였기 때문.

        // transferId별로 도착한 유닛 JSON 조각을 모은다.
        private readonly TextChunkAssembler _unitTransferAssembler = new();

        // 이미 아는 유닛의 모델을 갱신할 때, 순간이동 대신 부드럽게 그
        // 자리로 움직이게 하는 진행 중 트윈 목록 — 마커 이동(BoardManager.
        // Markers.cs의 MarkerMoveTween)과 같은 원리, Base용으로 따로 둔다
        // (사용자 요청, 2026-08-30: "유닛 이동 과정도 딱딱 이동시키지 말고
        // 부드럽게 움직여줘").
        private const float PieceMoveTweenDuration = 0.2f;
        private struct PieceMoveTween
        {
            public Base Piece;
            public Vector2 From;
            public Vector2 To;
            public float StartTime;
        }
        private readonly List<PieceMoveTween> _pieceMoveTweens = new();

        /// <summary>매 프레임 BoardManager.Update()에서 호출 — 진행 중인 모델
        /// 이동 트윈을 전진시킨다.</summary>
        internal void UpdatePieceMoveTweens()
        {
            for (int i = _pieceMoveTweens.Count - 1; i >= 0; i--)
            {
                var tween = _pieceMoveTweens[i];
                if (tween.Piece == null)
                {
                    _pieceMoveTweens.RemoveAt(i);
                    continue;
                }
                float t = Mathf.Clamp01((Time.time - tween.StartTime) / PieceMoveTweenDuration);
                float eased = t * t * (3f - 2f * t); // smoothstep
                tween.Piece.Center = Vector2.LerpUnclamped(tween.From, tween.To, eased);
                if (t >= 1f)
                {
                    _pieceMoveTweens.RemoveAt(i);
                }
            }
        }

        private void AnimatePieceMove(Base piece, Vector2 to)
        {
            _pieceMoveTweens.RemoveAll(t => t.Piece == piece);
            _pieceMoveTweens.Add(new PieceMoveTween
            {
                Piece = piece,
                From = piece.Center,
                To = to,
                StartTime = Time.time,
            });
        }

        /// <summary>유닛 이동 워크플로우가 완료될 때(BoardManager.UnitMove.cs의
        /// CompleteUnitMove) 호출한다. 미연결이면 아무 것도 안 한다.</summary>
        internal void BroadcastUnitIfNetworked(Unit unit)
        {
            if (unit == null || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 유닛 배치/이동 요청을 못 보냄");
                return;
            }
            // 처음 방송되는 유닛이면 여기서 식별자를 발급한다 — 이후 같은
            // 유닛을 다시 방송할 때(재이동)는 이 id를 그대로 재사용해서
            // 받는 쪽이 새로 만들지 않고 기존 유닛을 갱신하게 한다.
            if (unit.NetworkUnitId < 0)
            {
                // GetHashCode()는 int 전체 범위(음수 포함)를 돌려주는데,
                // NetworkUnitId는 "-1 = 아직 미배정"을 음수로 구분하므로
                // 항상 0 이상이어야 한다 — 부호 비트를 지워서 강제한다.
                unit.NetworkUnitId = System.Guid.NewGuid().GetHashCode() & 0x7FFFFFFF;
            }
            // 방송이 이 클라이언트 자신에게도(ClientsAndHost) 되돌아오므로,
            // 미리 등록해둔다 — 안 그러면 자기 자신이 방금 배치한 유닛을
            // "모르는 유닛"으로 착각해서 중복으로 새로 만들어버린다(그
            // 경우 ApplyUnitTree가 갱신 분기를 타서 방금 만든 모델을 도로
            // 지웠다가 같은 내용으로 다시 짓는 낭비는 있지만, 최소한 중복
            // 생성은 아니다).
            _networkedUnits.Set(unit.NetworkUnitId, unit);
            string unitJson = MiniJson.Write(BuildUnitTree(unit));
            BoardNetworkSync.Instance.RequestBroadcastUnit(unitJson);
        }

        /// <summary>리딩 모델 재이동 중 웨이포인트가 바뀔 때마다(BoardManager.
        /// UnitMove.cs — StartUnitMove/CommitLeadingWaypoint/HandleUnitMoveRightClick/
        /// FinishLeadingMove/CancelUnitMove) 호출한다. 실제 게임 상태(유닛
        /// 위치)는 이미 각자 로컬로 반영돼 있으므로, 이 방송은 순전히 상대
        /// 화면에 "구경용" 고스트/경로선을 보여주기 위한 것이다(사용자 요청,
        /// 2026-09-08: "이동과 가이드라인이 상대 플레이어에게도 공유됐으면").
        /// active=false는 그 시각 요소를 지우라는 신호. 배치(신규 유닛 최초
        /// 배치)는 웨이포인트 개념이 없으므로 조용히 무시한다.</summary>
        internal void BroadcastUnitMoveGuidelineIfNetworked(bool active)
        {
            if (_unitMoveIsDeployment || _unitMoveUnit == null || _unitMoveLeading == null)
            {
                return;
            }
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (_unitMoveUnit.NetworkUnitId < 0)
            {
                // 한 번도 방송된 적 없는 유닛(상대가 존재 자체를 모름) —
                // network_unit_id로 지목할 방법이 없으므로 조용히 포기한다.
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 이동 가이드라인을 못 보냄");
                return;
            }
            int leadingIndex = _unitMoveUnit.Models.IndexOf(_unitMoveLeading);
            BoardNetworkSync.Instance.RequestSetUnitMoveGuideline(
                    _unitMoveUnit.NetworkUnitId, leadingIndex, _unitMoveStartPoint, _unitMoveWaypoints.ToArray(), active);
        }

        /// <summary>유닛 이동/배치 워크플로우가 우클릭으로 취소될 때
        /// (BoardManager.UnitMove.cs의 CancelUnitMove, 배치 중 취소 분기)
        /// 호출한다 — 로컬 파괴는 CancelUnitMove가 이미 무조건 해뒀고
        /// (오프라인에서도 동작해야 하므로), 여기서는 그 사실을 상대에게
        /// 알리기만 한다. 한 번도 방송된 적 없는 유닛(NetworkUnitId 미배정
        /// — 팔로워 단계까지 못 가고 취소된 경우)은 상대가 애초에 모르므로
        /// 조용히 아무 것도 안 한다.</summary>
        internal void BroadcastDeleteUnitIfNetworked(Unit unit)
        {
            if (unit == null || unit.NetworkUnitId < 0
                    || NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 유닛 삭제 요청을 못 보냄");
                return;
            }
            BoardNetworkSync.Instance.RequestDeleteUnitServerRpc(unit.NetworkUnitId);
        }

        /// <summary>BoardNetworkSync.DeleteUnitRpc가 방송을 받았을 때
        /// 호출한다(요청한 쪽 자신도 루프백으로 포함 — 이미 로컬에서 직접
        /// 지운 뒤라 여기선 대부분 모델이 이미 비어있는 상태라 사실상
        /// 아무 일도 안 한다, _networkedUnits 정리만 남아있음). 모르는
        /// id면(이미 정리됐거나 애초에 몰랐던 id) 조용히 무시한다.</summary>
        internal void DeleteLocalUnit(int networkUnitId)
        {
            if (!_networkedUnits.TryGet(networkUnitId, out var unit))
            {
                return;
            }
            foreach (var model in unit.Models.ToArray())
            {
                _pieces.Remove(model);
                _pieceMoveTweens.RemoveAll(t => t.Piece == model);
                Destroy(model.gameObject);
            }
            unit.Models.Clear();
            _unitRanges.Remove(unit);
            _networkedUnits.Remove(networkUnitId);
        }

        /// <summary>BoardNetworkSync가 유닛 JSON 조각을 방송할 때마다
        /// 호출한다(호스트 자신도 포함, 배치/이동을 요청한 쪽도 포함).</summary>
        internal void ReceiveUnitChunk(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            string unitJson = _unitTransferAssembler.AddChunk(transferId, chunkIndex, totalChunks, chunk);
            if (unitJson == null)
            {
                return;
            }
            if (!(MiniJson.Parse(unitJson) is Dictionary<string, object> tree))
            {
                Debug.LogError("[BoardManager] 유닛 JSON 파싱 실패");
                return;
            }
            ApplyUnitTree(tree);
        }

        /// <summary>network_unit_id로 이미 아는 유닛이면 UpdateUnitModelsFromTree로
        /// 기존 GameObject를 그대로 두고 위치만 부드럽게 옮긴다. 모르는 id면
        /// (처음 보는 유닛) CreateUnitFromTree로 새로 만든다.</summary>
        private void ApplyUnitTree(Dictionary<string, object> u)
        {
            int networkUnitId = GameSaveIO.GetInt(u, "network_unit_id", -1);
            if (networkUnitId >= 0 && _networkedUnits.TryGet(networkUnitId, out var existingUnit))
            {
                UpdateUnitModelsFromTree(existingUnit, u);
                return;
            }

            CreateUnitFromTree(u);
        }

        /// <summary>이미 아는 유닛의 모델들을 기존 GameObject 그대로 트리 값에
        /// 맞춘다 — 위치는 AnimatePieceMove로 부드럽게, 나머지(회전/데미지/
        /// 메모)는 바로 반영한다. 이 단계(배치/이동 완료 시점 스냅샷) 범위
        /// 에서는 같은 유닛의 모델 수가 방송 사이에 바뀌지 않는다고 가정할
        /// 수 있다 — 순서(index)로 짝짓는다. 혹시 안 맞으면(방어적 처리,
        /// 아직 동기화 안 된 다른 액션이 모델 수를 바꾼 경우 등) 통째로
        /// 다시 짓는다.</summary>
        private void UpdateUnitModelsFromTree(Unit unit, Dictionary<string, object> u)
        {
            var modelTrees = GameSaveIO.GetList(u, "models");
            if (modelTrees.Count != unit.Models.Count)
            {
                foreach (var model in unit.Models.ToArray())
                {
                    _pieces.Remove(model);
                    _pieceMoveTweens.RemoveAll(t => t.Piece == model);
                    Destroy(model.gameObject);
                }
                unit.Models.Clear();
                PopulateModelsFromTree(unit, u);
                RefreshRangeOverlays();
                return;
            }

            for (int i = 0; i < modelTrees.Count; i++)
            {
                if (!(modelTrees[i] is Dictionary<string, object> m))
                {
                    continue;
                }
                var piece = unit.Models[i];
                AnimatePieceMove(piece, GameSaveIO.TreeToVec2(GameSaveIO.GetDict(m, "center")));
                piece.RotationDegrees = GameSaveIO.GetFloat(m, "rotation_degrees");
                piece.Damage = GameSaveIO.GetInt(m, "damage");
                piece.Memo = GameSaveIO.GetString(m, "memo");
                piece.Refresh();
            }
        }

        /// <summary>유닛 하나를 트리에서 새로 만든다 — 게임 불러오기
        /// (BoardManager.Load.cs)와 유닛 동기화 양쪽이 쓴다.</summary>
        private void CreateUnitFromTree(Dictionary<string, object> u)
        {
            var unit = new Unit
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
            unit.SupplyTiers.AddRange(GameSaveIO.TreeToSupplyTiers(GameSaveIO.GetList(u, "supply_tiers")));
            if (unit.IsToken)
            {
                _rosterTokenUnits[$"{unit.Team}|{unit.UnitName}"] = unit;
            }
            if (unit.NetworkUnitId >= 0)
            {
                _networkedUnits.Set(unit.NetworkUnitId, unit);
            }

            PopulateModelsFromTree(unit, u);
            RefreshRangeOverlays();
        }

        /// <summary>트리의 "ranges"/"models"를 unit에 채운다 — 새로 만든
        /// 유닛(CreateUnitFromTree)과, 이미 있는 유닛의 모델을 지웠다가
        /// 다시 채우는 경우(ApplyUnitTree의 갱신 분기) 둘 다에서 쓴다.</summary>
        private void PopulateModelsFromTree(Unit unit, Dictionary<string, object> u)
        {
            var ranges = GameSaveIO.TreeToRanges(GameSaveIO.GetList(u, "ranges"));
            if (ranges.Count > 0)
            {
                _unitRanges[unit] = ranges;
            }

            foreach (var rawModel in GameSaveIO.GetList(u, "models"))
            {
                if (!(rawModel is Dictionary<string, object> m))
                {
                    continue;
                }
                var sizeMm = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(m, "size_mm"));
                var fillColor = GameSaveIO.TreeToColor(GameSaveIO.GetDict(m, "fill_color"));
                var piece = CreatePieceObject(unit, sizeMm, fillColor, GameSaveIO.GetBool(m, "is_displacement"));
                piece.Damage = GameSaveIO.GetInt(m, "damage");
                piece.Memo = GameSaveIO.GetString(m, "memo");
                piece.Center = GameSaveIO.TreeToVec2(GameSaveIO.GetDict(m, "center"));
                piece.RotationDegrees = GameSaveIO.GetFloat(m, "rotation_degrees");
                piece.Refresh();
                unit.Models.Add(piece);
            }
        }
    }
}
