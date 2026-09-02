using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>RPC 한 번에 싣기엔 너무 큰 문자열(로스터/유닛 JSON 등)을
    /// TextChunkSize 단위로 쪼개 보낸 뒤, transferId별로 조각을 모아 다시
    /// 이어붙이는 수신측 창구 — 예전엔 이 로직이 두 가지 모양으로 8곳에
    /// 흩어져 있었다(BoardNetworkSync.cs 안의 private TextChunkBuffer 방식
    /// 4곳, BoardManager.UnitSync.cs/.Roster.cs/.PendingUnitSync.cs/
    /// .TacticalCardSync.cs의 병렬 Dictionary&lt;int,string[]&gt;+
    /// Dictionary&lt;int,int&gt; 방식 4곳) — 하나로 통합했다(2026-09-03).
    /// 전송 종류마다 이 클래스의 인스턴스를 하나씩 가진다(전송 종류 사이에
    /// transferId가 서로 안전하게 겹칠 수 있어 공유 인스턴스를 쓰면 안 됨).</summary>
    internal sealed class TextChunkAssembler
    {
        private class Buffer
        {
            public string[] Parts;
            public int ReceivedCount;
        }

        private readonly Dictionary<int, Buffer> _buffers = new();

        /// <summary>조각 하나를 받는다. 아직 다 안 모였으면 null, 이번 호출로
        /// 마지막 조각까지 다 모였으면 이어붙인 전체 문자열을 돌려준다(그
        /// transferId 항목은 이 시점에 정리됨 — 같은 transferId로 다시
        /// 부르면 새 전송으로 취급).</summary>
        internal string AddChunk(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            if (!_buffers.TryGetValue(transferId, out var buf))
            {
                buf = new Buffer { Parts = new string[totalChunks] };
                _buffers[transferId] = buf;
            }
            if (buf.Parts[chunkIndex] == null)
            {
                buf.ReceivedCount++;
            }
            buf.Parts[chunkIndex] = chunk;
            if (buf.ReceivedCount < totalChunks)
            {
                return null;
            }
            _buffers.Remove(transferId);
            return string.Concat(buf.Parts);
        }
    }
}
