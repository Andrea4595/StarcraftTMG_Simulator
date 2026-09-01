using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 게임 도중 멀티 시작(2026-09-02 신설) ───────────────────────────
        // GameBoard 마커바의 "같이 하기" 버튼(BoardManager.Markers.cs의
        // CreateMultiplayerButton)으로 이미 진행 중인 솔로 게임에 상대를
        // 초대할 때 쓴다 — MultiplayerConnectDialog.OnClientConnected가
        // 호스트 쪽에서(그때 호스트가 GameBoard 중이면) 이 메서드를 부른다.
        // 저장 파일과 완전히 같은 트리(BoardManager.Save.cs의
        // BuildFullStateTree)를 그대로 재사용해서 청크로 상대에게 보내고,
        // 상대는 LoadGame 화면이 저장 파일을 불러올 때와 완전히 같은 경로
        // (GameLoadRequest.PendingData → GameFlowBootstrap.BuildGameBoard의
        // GameSaveIO.ApplyLoadedStaticState → BoardManager.Start()의
        // ApplyLoadedLiveState)로 그 상태를 적용한다 — 새 코드가 필요한 건
        // "보내기"뿐, "받기"는 이미 있던 불러오기 경로를 그대로 탄다.

        /// <summary>MultiplayerConnectDialog.OnClientConnected가 호스트
        /// 쪽에서만 부른다(호출부에서 이미 IsServer/씬 확인함). 보내기 전에
        /// 아직 네트워크 id가 없는(솔로 플레이 중 놓인) 유닛/마커들에 id를
        /// 새로 발급해 등록해두고, 합류 이전 되돌리기 히스토리는 통째로
        /// 지운다(ClearUndoHistoryForMidGameJoin, 2026-09-02, 사용자 지정
        /// — 그 히스토리 안에 박제된 옛 네트워크 id 때문에 생기던 중복 버그를
        /// 실제로 재현해본 뒤 "애초에 문제가 생기지 않게" 하기로 결정).</summary>
        public void BroadcastFullStateForMidGameJoin()
        {
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 게임 도중 상태 전달을 못 보냄");
                return;
            }
            BackfillUnitNetworkIds();
            BackfillMarkerNetworkIds();
            ClearUndoHistoryForMidGameJoin();
            // full_state/undo_history를 한 봉투에 같이 담아 보낸다 —
            // BuildFullStateTree()를 인자 없이(기본값 includeUndoHistory=false)
            // 부르므로 그 안엔 히스토리가 안 실린다(평범한 저장과 동일 —
            // 2026-09-04부터 리플레이 저장만 true로 따로 부른다, BoardManager.
            // Save.cs 참고). 대신 여기서는 undo_history를 그 트리 밖에
            // 별도 키로 담는다. undo_history는 방금 위에서 비웠으니 사실상
            // 항상 빈 트리다.
            var wrapper = new Dictionary<string, object>
            {
                { "full_state", BuildFullStateTree() },
                { "undo_history", BuildUndoHistoryTree() },
            };
            string json = MiniJson.Write(wrapper);
            BoardNetworkSync.Instance.RequestBroadcastMidGameState(json);
        }

        /// <summary>솔로 플레이 중엔 BroadcastUnitIfNetworked이 전혀 안 불려서
        /// (NetworkManager.IsListening이 false) 모든 유닛의 NetworkUnitId가
        /// 아직 -1이다. 상대에게 이 상태를 보내기 전에 여기서 먼저 진짜 id를
        /// 발급해두지 않으면, 나중에 누군가 그 유닛을 처음 옮길 때
        /// BroadcastUnitIfNetworked이 그제서야 새 id를 지연 발급하는데(정상
        /// 동작, 재이동 시 재사용하려는 것) — 그 시점엔 상대방 화면에 이미
        /// "완전 상태 전송"으로 만들어진 같은 유닛이 어떤 id도 없이 존재하고
        /// 있어서, ApplyUnitTree가 "모르는 id"로 착각해 완전히 새로운 중복
        /// 유닛을 만들어버린다(이 프로젝트가 이미 겪었던 것과 같은 종류의
        /// 중복 버그). 그래서 여기서 미리 모든 유닛에 id를 배정하고
        /// _networkedUnitsById에도 등록해, 트리에 실려 나가는 network_unit_id를
        /// 상대가 그대로 받아 자기 쪽에도 똑같이 등록하게 한다(CreateUnitFromTree
        /// 참고 — network_unit_id&gt;=0이면 이미 자동으로 등록해준다).</summary>
        private void BackfillUnitNetworkIds()
        {
            var seen = new HashSet<Unit>();
            for (int i = 0; i < baseLayer.childCount; i++)
            {
                var piece = baseLayer.GetChild(i).GetComponent<Base>();
                if (piece == null || piece.Unit == null)
                {
                    continue;
                }
                var unit = piece.Unit;
                if (!seen.Add(unit))
                {
                    continue;
                }
                if (unit.NetworkUnitId < 0)
                {
                    // BroadcastUnitIfNetworked의 지연 발급과 완전히 같은 계산
                    // (부호 비트 제거 이유도 동일 — -1은 "미배정" 전용).
                    unit.NetworkUnitId = System.Guid.NewGuid().GetHashCode() & 0x7FFFFFFF;
                }
                _networkedUnitsById[unit.NetworkUnitId] = unit;
            }
        }

        /// <summary>아직 한 번도 네트워크로 다뤄진 적 없는(NetworkMarkerId
        /// 기본값 -1인) 마커에 새 id를 발급해 채우고 _networkedMarkersById에
        /// 등록한다 — 실제 배치 방송(RequestPlaceMarkerServerRpc)과 달리
        /// 새로 만들지도, 상대에게 "놓였다"고 알리지도 않는다(이미 두
        /// 화면 모두에 이 마커가 존재하게 될 것이므로 — 상대는 이번 전체
        /// 상태 전송 자체로 마커를 받는다). 배치 미리보기(고스트)는 원래
        /// BuildMarkersTree에서도 빠지므로 여기서도 자연히 건너뛴다.</summary>
        private void BackfillMarkerNetworkIds()
        {
            if (markerLayer == null || BoardNetworkSync.Instance == null)
            {
                return;
            }
            for (int i = 0; i < markerLayer.childCount; i++)
            {
                var markerGo = markerLayer.GetChild(i).gameObject;
                if (_markerPlacementPreview != null && markerGo == _markerPlacementPreview.gameObject)
                {
                    continue;
                }
                var marker = markerGo.GetComponent<MarkerBase>();
                if (marker == null || marker.NetworkMarkerId >= 0)
                {
                    continue;
                }
                int id = BoardNetworkSync.Instance.AllocateNextMarkerId();
                marker.NetworkMarkerId = id;
                _networkedMarkersById[id] = marker;
            }
        }
    }
}
