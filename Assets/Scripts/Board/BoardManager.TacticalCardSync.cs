using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 택티컬 카드(_pendingTacticalCards) 동기화(2026-09-02 신설) ─────
        // 사용자 보고: 멀티 게임 화면에서 택티컬 카드 좌/우클릭(소진/복구,
        // BoardManager.Deployment.cs의 CreateTacticalCardButton)이 상대
        // 화면에 전혀 반영되지 않았다. 예비대 목록(BoardManager.
        // PendingUnitSync.cs)과 완전히 같은 이유로 목록 전체를 다시 보낸다
        // — 카드는 게임 중 추가/삭제 없이 Remaining만 바뀌므로 인덱스가
        // 어긋날 걱정은 없지만, 그래도 이미 검증된 같은 패턴을 그대로
        // 재사용하는 게 새 프로토콜을 만드는 것보다 간단하다.

        private readonly Dictionary<int, string[]> _tacticalCardsTransferChunksInProgress = new();
        private readonly Dictionary<int, int> _tacticalCardsTransferReceivedCountInProgress = new();

        private void BroadcastTacticalCardsIfNetworked()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                return;
            }
            if (BoardNetworkSync.Instance == null)
            {
                Debug.LogError("[BoardManager] BoardNetworkSync.Instance가 없음 — 택티컬 카드 공유를 못 보냄");
                return;
            }
            string json = MiniJson.Write(BuildTacticalCardsTree());
            BoardNetworkSync.Instance.RequestBroadcastTacticalCards(json);
        }

        /// <summary>BoardNetworkSync가 택티컬 카드 목록 JSON 조각을 방송할
        /// 때마다 호출한다(호스트 자신도 포함, 클릭한 쪽도 포함).</summary>
        internal void ReceiveTacticalCardsChunk(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            if (!_tacticalCardsTransferChunksInProgress.TryGetValue(transferId, out var chunks))
            {
                chunks = new string[totalChunks];
                _tacticalCardsTransferChunksInProgress[transferId] = chunks;
                _tacticalCardsTransferReceivedCountInProgress[transferId] = 0;
            }
            if (chunks[chunkIndex] == null)
            {
                chunks[chunkIndex] = chunk;
                _tacticalCardsTransferReceivedCountInProgress[transferId]++;
            }
            if (_tacticalCardsTransferReceivedCountInProgress[transferId] < totalChunks)
            {
                return;
            }
            _tacticalCardsTransferChunksInProgress.Remove(transferId);
            _tacticalCardsTransferReceivedCountInProgress.Remove(transferId);

            string json = string.Concat(chunks);
            if (!(MiniJson.Parse(json) is List<object> tree))
            {
                Debug.LogError("[BoardManager] 택티컬 카드 목록 JSON 파싱 실패");
                return;
            }
            _pendingTacticalCards.Clear();
            foreach (var raw in tree)
            {
                if (raw is Dictionary<string, object> c)
                {
                    _pendingTacticalCards.Add(ParseTacticalCardDefTree(c));
                }
            }
            RefreshTacticalCardList();
        }
    }
}
