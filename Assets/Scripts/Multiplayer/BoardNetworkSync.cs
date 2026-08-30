using Unity.Netcode;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>보드 상태 동기화용 RPC 창구. BoardManager 자체는 씬마다
    /// 코드로 새로 지어지는 일반 MonoBehaviour라 NetworkBehaviour가 될 수
    /// 없어서, 안정적인 RPC 대상이 따로 필요하다 — 이 컴포넌트가 그 역할.
    /// 마커 자체는 NetworkObject로 스폰하지 않는다 — NGO는 NetworkObject를
    /// NetworkObject가 아닌 일반 Transform(우리 UI 마커 레이어) 밑으로
    /// 재부모화하는 걸 막아서("Invalid parenting" 에러) 그 방식이 안 통했다.
    /// 대신 "이 자리에 이 종류를 놓아라"는 이벤트만 전체에 방송하고, 각
    /// 클라이언트가 1인용 때와 똑같이 완전히 로컬로 마커를 만든다
    /// (BoardManager.SpawnLocalMarkerVisual). 등록된 네트워크 프리팹으로
    /// 존재해야 클라이언트 쪽에도 복제되므로(Assets/Resources/Multiplayer/
    /// BoardNetworkSync 프리팹, NetworkObject+이 스크립트만 붙은 빈
    /// 오브젝트), 호스트가 StartHost() 성공 직후 한 번 스폰한다
    /// (RelayConnectionTest 참고).</summary>
    public class BoardNetworkSync : NetworkBehaviour
    {
        public static BoardNetworkSync Instance { get; private set; }

        // 서버(호스트)에서만 증가시키는 카운터 — 마커는 NetworkObject가
        // 아니라 각자 로컬로 만들어지므로, 삭제할 때 "어느 마커인지"
        // 지목하려면 배치 시점에 호스트가 발급한 id가 있어야 한다.
        private int _nextMarkerId;

        // 큰 텍스트(로스터 JSON, 유닛 JSON)를 통째로 한 RPC 파라미터에
        // 실으면, 그 RPC를 "보내는" 시점(클라이언트→서버 호출 그 자체)에
        // FastBufferWriter가 OverflowException을 던진다(2026-08-30 실제로
        // 겪음, 두 번 — 처음엔 서버가 받은 뒤 쪼개도록 짰는데 그래도 똑같이
        // 터졌다. 문제는 애초에 "쪼개기 전" RPC 호출 자체가 이미 너무
        // 크다는 것 — 그래서 쪼개기는 반드시 첫 RPC를 보내기 전, 호출하는
        // 쪽에서 해야 한다). 글자 수 기준이라 한글(UTF8 3바이트)이어도
        // 넉넉히 안전하도록 작게 잡았다. 로스터/유닛 두 전송 파이프라인이
        // 공유(둘 다 "그냥 큰 텍스트를 쪼개 보낸다"는 같은 문제라 상수만
        // 공유하고, 나머지 RPC 쌍은 각자 따로 둔다 — 지금은 그게 더
        // 단순하다).
        private const int TextChunkSize = 512;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            // Entry 화면에서 스폰되는데(RelayConnectionTest), 이후
            // Selection/TerrainSetup/GameBoard로 넘어가는 씬 전환은 NGO의
            // NetworkSceneManager가 아니라 일반 SceneManager.LoadScene이라 —
            // DontDestroyOnLoad를 안 걸면 씬 전환 때 파괴돼버린다(호스트/
            // 클라이언트 양쪽 복제본 모두, OnNetworkSpawn은 양쪽 다 탄다).
            Object.DontDestroyOnLoad(gameObject);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>클라이언트가 마커 배치를 요청할 때 부른다. 호스트가
        /// 받아서 검증 없이(아직 지도 경계 클램프도 안 함 — 다음 단계) id를
        /// 하나 발급하고 전체에 방송한다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPlaceMarkerServerRpc(string kind, Vector2 point)
        {
            int id = ++_nextMarkerId;
            PlaceMarkerRpc(kind, point, id);
        }

        /// <summary>호스트를 포함해 연결된 모두에게 도착한다(NGO가 호스트
        /// 자신의 로컬 실행도 처리해줌 — 별도로 직접 호출 안 함). 실제
        /// 마커 생성은 완전히 로컬(1인용과 동일 경로)이라 되돌리기는 아직
        /// 이 경로를 안 탄다 — 다음 단계.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void PlaceMarkerRpc(string kind, Vector2 point, int id)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 마커를 못 그림");
                return;
            }
            board.SpawnLocalMarkerVisual(kind, point, id);
        }

        /// <summary>클라이언트가 마커 삭제를 요청할 때 부른다(우클릭
        /// 삭제 — BoardManager의 OnActivationMarkerRightClicked 등).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDeleteMarkerServerRpc(int id)
        {
            DeleteMarkerRpc(id);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DeleteMarkerRpc(int id)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 마커를 못 지움");
                return;
            }
            board.DeleteLocalMarkerVisual(id);
        }

        /// <summary>활성화 마커(이동→돌격→완료)/점령 마커(색 순환)의
        /// 상태 순환 우클릭을 요청할 때 부른다 — 어느 필드를 바꿀지는
        /// BoardManager.SetLocalMarkerState가 마커 타입으로 판별한다(마커
        /// 자체가 NetworkObject가 아니라 타입 정보를 따로 안 보냄).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetMarkerStateServerRpc(int id, string state)
        {
            SetMarkerStateRpc(id, state);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetMarkerStateRpc(int id, string state)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 마커 상태를 못 바꿈");
                return;
            }
            board.SetLocalMarkerState(id, state);
        }

        /// <summary>드래그를 끝냈을 때(HandleMarkerDragInput) 부른다 — 실시간
        /// 방송이 아니라 최종 위치 하나만(사용자 요청: 실시간일 필요 없음,
        /// 2026-08-30). 받는 쪽은 BoardManager.AnimateLocalMarkerMove로
        /// 순간이동 대신 부드럽게 옮겨간다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestMoveMarkerServerRpc(int id, Vector2 point)
        {
            MoveMarkerRpc(id, point);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MoveMarkerRpc(int id, Vector2 point)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 마커를 못 옮김");
                return;
            }
            board.AnimateLocalMarkerMove(id, point);
        }

        /// <summary>로스터 파일을 로컬에서 고른 클라이언트가 부른다 — 그
        /// 파일은 이 기기에만 있으므로 파싱된 결과가 아니라 JSON 텍스트
        /// 원본을 그대로 보낸다. 일반 메서드(RPC 아님) — 첫 네트워크 호출
        /// 전에 여기서 미리 TextChunkSize 단위로 쪼개서, 작은 조각짜리
        /// RPC를 여러 번 부른다(위 클래스 필드 주석 참고 — 안 쪼개진 큰
        /// 문자열은 RPC로 "보내는" 순간부터 오버플로우).</summary>
        public void RequestImportRoster(string team, string rosterJson)
        {
            // 호스트가 발급하는 게 아니라 호출하는 쪽이 그 자리에서 만든다 —
            // transferId를 호스트에게 받아오려면 그 자체가 또 하나의 왕복
            // RPC라 순서가 더 복잡해진다. 2인용 도구에서 충돌 가능성은
            // 무시할 수준.
            int transferId = System.Guid.NewGuid().GetHashCode();
            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(rosterJson.Length / (float)TextChunkSize));
            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * TextChunkSize;
                int length = Mathf.Min(TextChunkSize, rosterJson.Length - start);
                string chunk = rosterJson.Substring(start, length);
                RequestImportRosterChunkServerRpc(transferId, team, i, totalChunks, chunk);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestImportRosterChunkServerRpc(int transferId, string team, int chunkIndex, int totalChunks, string chunk)
        {
            ImportRosterChunkRpc(transferId, team, chunkIndex, totalChunks, chunk);
        }

        /// <summary>받는 쪽(호스트 자신 포함)은 조각을
        /// BoardManager.ReceiveRosterChunk에 넘긴다 — 그쪽에서 모으고,
        /// 다 모이면 ApplyRosterImport로 로컬 파싱해서 적용한다.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void ImportRosterChunkRpc(int transferId, string team, int chunkIndex, int totalChunks, string chunk)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 로스터 조각을 못 받음");
                return;
            }
            board.ReceiveRosterChunk(transferId, team, chunkIndex, totalChunks, chunk);
        }

        /// <summary>유닛 이동 워크플로우가 완료됐을 때(배치든 재이동이든,
        /// BoardManager.UnitSync.cs의 BroadcastUnitIfNetworked) 부른다 — 같은
        /// 이유로 청크 단위로 미리 쪼개서 보낸다(위 TextChunkSize 주석
        /// 참고).</summary>
        public void RequestBroadcastUnit(string unitJson)
        {
            int transferId = System.Guid.NewGuid().GetHashCode();
            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(unitJson.Length / (float)TextChunkSize));
            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * TextChunkSize;
                int length = Mathf.Min(TextChunkSize, unitJson.Length - start);
                string chunk = unitJson.Substring(start, length);
                RequestBroadcastUnitChunkServerRpc(transferId, i, totalChunks, chunk);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastUnitChunkServerRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            BroadcastUnitChunkRpc(transferId, chunkIndex, totalChunks, chunk);
        }

        /// <summary>받는 쪽(호스트 자신 포함, 요청한 쪽도 포함)은 조각을
        /// BoardManager.ReceiveUnitChunk에 넘긴다 — 다 모이면 ApplyUnitTree로
        /// 로컬 재구성/갱신한다.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastUnitChunkRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 유닛 조각을 못 받음");
                return;
            }
            board.ReceiveUnitChunk(transferId, chunkIndex, totalChunks, chunk);
        }

        /// <summary>유닛 배치/재이동 워크플로우가 우클릭으로 취소됐을 때
        /// (BoardManager.UnitMove.cs의 CancelUnitMove) 부른다 — 팔로워 단계
        /// 도중이라 이미 상대에게 중간 상태가 방송돼있었을 수 있어서, 그걸
        /// 지우라고 알려준다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDeleteUnitServerRpc(int networkUnitId)
        {
            DeleteUnitRpc(networkUnitId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DeleteUnitRpc(int networkUnitId)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 유닛을 못 지움");
                return;
            }
            board.DeleteLocalUnit(networkUnitId);
        }

        /// <summary>예비대(_pendingUnits) 목록이 바뀔 때마다(BoardManager.
        /// PendingUnitSync.cs의 AddPendingUnitDef/RemovePendingUnitDefAt) 부른다
        /// — 목록 전체를 다시 보낸다(위 TextChunkSize 주석과 같은 이유로
        /// 청크 단위).</summary>
        public void RequestBroadcastPendingUnits(string pendingUnitsJson)
        {
            int transferId = System.Guid.NewGuid().GetHashCode();
            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(pendingUnitsJson.Length / (float)TextChunkSize));
            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * TextChunkSize;
                int length = Mathf.Min(TextChunkSize, pendingUnitsJson.Length - start);
                string chunk = pendingUnitsJson.Substring(start, length);
                RequestBroadcastPendingUnitsChunkServerRpc(transferId, i, totalChunks, chunk);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastPendingUnitsChunkServerRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            BroadcastPendingUnitsChunkRpc(transferId, chunkIndex, totalChunks, chunk);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastPendingUnitsChunkRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 예비대 조각을 못 받음");
                return;
            }
            board.ReceivePendingUnitsChunk(transferId, chunkIndex, totalChunks, chunk);
        }
    }
}
