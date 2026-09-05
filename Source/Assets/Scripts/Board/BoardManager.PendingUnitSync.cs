using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 예비대(_pendingUnits) 목록 동기화 ────────────────────────────
        // 사용자 요청(2026-08-30): 예비대에서 유닛을 지도로 꺼낼 때 상대
        // 화면의 예비대 목록도 같이 줄어들어야 한다 — "등록/제거 인터페이스
        // 자체에서 방송"해서, 배치 시작/취소/되돌리기 등 어디서 목록이
        // 바뀌든 절대 누락되지 않게 한다. 그래서 _pendingUnits를 직접
        // Add/RemoveAt하지 않고 AddPendingUnitDef/RemovePendingUnitDefAt를
        // 통해서만 바꾼다(로스터 임포트의 AddRange는 예외 — 그건 이미
        // 로스터 JSON 방송 자체로 양쪽에 동일하게 반영되므로 여기 안
        // 걸린다. 걸면 방송이 중복될 뿐이라 굳이 안 건드림).
        //
        // 부분 추가/삭제(인덱스 방송) 대신 목록 전체를 다시 보낸다 —
        // 유닛 동기화와 같은 이유(BoardManager.UnitSync.cs 참고): 인덱스가
        // 양쪽에서 어긋날 걱정이 없고, 예비대 목록은 크지도 자주 바뀌지도
        // 않아 통째로 보내는 비용이 무시할 만하다.

        private readonly TextChunkAssembler _pendingUnitsTransferAssembler = new();

        /// <summary>예비대 목록에 정의 하나를 추가한다 — 이 메서드로만
        /// 추가하면 방송이 자동으로 따라간다.</summary>
        private void AddPendingUnitDef(PendingUnitDef def)
        {
            _pendingUnits.Add(def);
            RefreshPendingList();
            BroadcastPendingUnitsIfNetworked();
        }

        /// <summary>예비대 목록에서 index번째 정의를 뺀다 — 이 메서드로만
        /// 빼면 방송이 자동으로 따라간다.</summary>
        private void RemovePendingUnitDefAt(int index)
        {
            _pendingUnits.RemoveAt(index);
            RefreshPendingList();
            BroadcastPendingUnitsIfNetworked();
        }

        private void BroadcastPendingUnitsIfNetworked()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 예비대 목록 공유를 못 보냄");
                return;
            }
            string json = MiniJson.Write(BuildPendingUnitsTree());
            BoardNetworkSync.Instance.RequestBroadcastPendingUnits(json);
        }

        /// <summary>BoardNetworkSync가 예비대 목록 JSON 조각을 방송할 때마다
        /// 호출한다(호스트 자신도 포함, 목록을 바꾼 쪽도 포함).</summary>
        internal void ReceivePendingUnitsChunk(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            string json = _pendingUnitsTransferAssembler.AddChunk(transferId, chunkIndex, totalChunks, chunk);
            if (json == null)
            {
                return;
            }
            if (!(MiniJson.Parse(json) is List<object> tree))
            {
                Debug.LogError("[BoardManager] 예비대 목록 JSON 파싱 실패");
                return;
            }
            _pendingUnits.Clear();
            foreach (var raw in tree)
            {
                if (raw is Dictionary<string, object> p)
                {
                    _pendingUnits.Add(ParsePendingUnitDefTree(p));
                }
            }
            RefreshPendingList();
        }
    }
}
