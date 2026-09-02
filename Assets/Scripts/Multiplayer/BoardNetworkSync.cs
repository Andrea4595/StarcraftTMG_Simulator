using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    /// (MultiplayerConnectDialog 참고).</summary>
    public class BoardNetworkSync : NetworkBehaviour
    {
        public static BoardNetworkSync Instance { get; private set; }

        // 서버(호스트)에서만 증가시키는 카운터 — 마커는 NetworkObject가
        // 아니라 각자 로컬로 만들어지므로, 삭제할 때 "어느 마커인지"
        // 지목하려면 배치 시점에 호스트가 발급한 id가 있어야 한다.
        private int _nextMarkerId;

        /// <summary>RequestPlaceMarkerServerRpc가 실제 배치할 때 쓰는 것과
        /// 같은 카운터를 직접 하나 꺼내 쓴다 — 2026-09-02, 게임 도중 멀티
        /// 시작(BoardManager.MidGameHandoff.cs)이 이미 솔로로 놓여있던
        /// 마커(아직 네트워크 id가 없는, NetworkMarkerId==-1)에 새로 방송
        /// 없이 하나씩 발급할 때 쓴다 — 오직 호스트만 부른다(호출부에서
        /// 이미 확인함).</summary>
        public int AllocateNextMarkerId()
        {
            return ++_nextMarkerId;
        }

        // 지형 조각도 마커와 같은 이유로 NetworkObject가 아니라 로컬 생성 —
        // 배치 시 호스트가 발급하는 id로 이동/회전/삭제를 지목한다.
        private int _nextTerrainId;

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

        /// <summary>큰 문자열을 TextChunkSize 단위로 쪼개 sendChunk 콜백을 그
        /// 개수만큼 부른다 — 8개 전송 파이프라인(로스터/유닛/예비대/택티컬카드/
        /// 카드프렙/다이스상태/undo/게임도중합류)이 전부 이 쪼개기 자체는
        /// 완전히 같은 모양이라 하나로 모았다(2026-09-03). transferId는
        /// 여기서 새로 발급해 매 조각마다 콜백에 같이 넘긴다 — 호출부는 그
        /// 값을 각자의 ...ChunkServerRpc 호출에 그대로 실어 보내면 된다(RPC
        /// 시그니처는 그대로라 각 호출부 이후 로직/받는 쪽은 전혀 안
        /// 바뀐다).</summary>
        private static void SendChunked(string payload, System.Action<int, int, int, string> sendChunk)
        {
            int transferId = System.Guid.NewGuid().GetHashCode();
            int totalChunks = Mathf.Max(1, Mathf.CeilToInt(payload.Length / (float)TextChunkSize));
            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * TextChunkSize;
                int length = Mathf.Min(TextChunkSize, payload.Length - start);
                sendChunk(transferId, i, totalChunks, payload.Substring(start, length));
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            // Entry 화면에서 스폰되는데(MultiplayerConnectDialog), 이후
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
            SendChunked(rosterJson, (transferId, i, totalChunks, chunk) =>
                    RequestImportRosterChunkServerRpc(transferId, team, i, totalChunks, chunk));
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
            SendChunked(unitJson, (transferId, i, totalChunks, chunk) =>
                    RequestBroadcastUnitChunkServerRpc(transferId, i, totalChunks, chunk));
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
            SendChunked(pendingUnitsJson, (transferId, i, totalChunks, chunk) =>
                    RequestBroadcastPendingUnitsChunkServerRpc(transferId, i, totalChunks, chunk));
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

        // ── 택티컬 카드(_pendingTacticalCards) 동기화(2026-09-02 신설) ─────
        // 예비대 목록과 완전히 같은 모양(목록 전체를 다시 보냄) — 사용자
        // 보고: 멀티 게임 화면에서 카드 좌/우클릭이 상대에게 전혀 안 보였다.

        public void RequestBroadcastTacticalCards(string tacticalCardsJson)
        {
            SendChunked(tacticalCardsJson, (transferId, i, totalChunks, chunk) =>
                    RequestBroadcastTacticalCardsChunkServerRpc(transferId, i, totalChunks, chunk));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastTacticalCardsChunkServerRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            BroadcastTacticalCardsChunkRpc(transferId, chunkIndex, totalChunks, chunk);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastTacticalCardsChunkRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 택티컬 카드 조각을 못 받음");
                return;
            }
            board.ReceiveTacticalCardsChunk(transferId, chunkIndex, totalChunks, chunk);
        }

        // ── 범위 표시 동기화(2026-08-31 신설) ─────────────────────────────
        // _unitRanges는 Unit 자체가 아니라 BoardManager 쪽 보조 상태라
        // BuildUnitTree(데미지/복제/제거/메모가 재사용하는 전체-유닛 트리)에
        // 안 실린다 — 그래서 유닛의 NetworkUnitId로 대상을 지목하는 전용
        // 방송이 필요하다(마커/지형과 같은 "id 발급 없이 이미 있는 id로
        // 지목" 패턴 — 유닛은 이미 NetworkUnitId를 갖고 있으므로 새로 id를
        // 발급할 필요가 없다).

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestAddRangeServerRpc(int networkUnitId, float inch, bool alwaysShow)
        {
            AddRangeRpc(networkUnitId, inch, alwaysShow);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AddRangeRpc(int networkUnitId, float inch, bool alwaysShow)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 범위를 못 추가함");
                return;
            }
            board.ApplyAddRangeById(networkUnitId, inch, alwaysShow);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDeleteRangeServerRpc(int networkUnitId, int index)
        {
            DeleteRangeRpc(networkUnitId, index);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DeleteRangeRpc(int networkUnitId, int index)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 범위를 못 지움");
                return;
            }
            board.ApplyDeleteRangeById(networkUnitId, index);
        }

        // ── 미션 목표 마커(점령 링 색) 동기화(2026-08-31 신설) ────────────
        // 목표 번호(1~5)가 이미 양쪽에 동일하게 있으므로(배치 프리셋 자체가
        // 드래프트로 동기화됨) 마커/유닛과 달리 새 id 발급이 필요 없다 —
        // 번호로 바로 지목한다. 절대 상태값을 방송한다(휠 회전과 같은
        // 이유 — 메시지 유실에도 안 어긋나게).

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetMissionObjectiveColorServerRpc(int number, string colorState)
        {
            SetMissionObjectiveColorRpc(number, colorState);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetMissionObjectiveColorRpc(int number, string colorState)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 미션 마커 색을 못 반영함");
                return;
            }
            board.ApplyMissionObjectiveColorByNumber(number, colorState);
        }

        // ── 카드 드래프트(CardPrep/CardDraft, 2026-08-31 신설) ─────────────
        // 미션/배치 프리셋 원본 텍스트는 상대 컴퓨터에 그 파일이 없을 수
        // 있으므로(로스터/유닛과 같은 이유) 청크로 쪼개 원문 그대로 보낸다.

        private readonly TextChunkAssembler _cardPrepAssembler = new();

        /// <summary>CardPrep에서 "준비 완료"를 누르면 부른다 — wrapperJson은
        /// CardPrepController가 MiniJson.Write로 만든, 배치 2장+미션 2장의
        /// 이름+원본 텍스트를 담은 객체. team은 보낸 쪽 자신의 팀(로스터
        /// 청크 전송과 같은 이유로 매 청크마다 같이 실어 보낸다) — 이
        /// 방송은 보낸 쪽 자신에게도 루프백되므로, 받는 쪽이 "이게 내가
        /// 보낸 것의 메아리인지 진짜 상대 데이터인지" 구분하는 데 필요하다
        /// (실제로 이 구분이 없어서 클라이언트 화면에 상대 카드 자리가
        /// 자기 카드로 덮어써지는 버그가 있었다).</summary>
        public void RequestBroadcastCardPrep(string team, string wrapperJson)
        {
            SendChunked(wrapperJson, (transferId, i, totalChunks, chunk) =>
                    RequestBroadcastCardPrepChunkServerRpc(transferId, team, i, totalChunks, chunk));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastCardPrepChunkServerRpc(int transferId, string team, int chunkIndex, int totalChunks, string chunk)
        {
            BroadcastCardPrepChunkRpc(transferId, team, chunkIndex, totalChunks, chunk);
        }

        /// <summary>청크가 다 모이면 DraftState.ApplyRemoteCardPrepJson으로
        /// 곧장 반영한다(씬과 무관하게 항상 — DraftState는 static이라 CardPrep/
        /// CardDraft 어느 화면에 있든 상관없다. 내가 보낸 방송의 루프백이면
        /// ApplyRemoteCardPrepJson 자신이 team을 보고 무시한다). CardDraft
        /// 화면이 지금 떠있으면(상대가 먼저 끝냈고 나는 이미 카드 준비를
        /// 마치고 넘어와 있는 경우) 그 화면의 자리표시자 카드를 실제 내용으로
        /// 다시 그리라고 알려준다 — 화면이 없으면(아직 카드 준비 중이면)
        /// 다음에 그 화면이 지어질 때 DraftState.Pool을 읽는 것만으로 이미
        /// 반영돼 있다.</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastCardPrepChunkRpc(int transferId, string team, int chunkIndex, int totalChunks, string chunk)
        {
            string fullJson = _cardPrepAssembler.AddChunk(transferId, chunkIndex, totalChunks, chunk);
            if (fullJson == null)
            {
                return;
            }

            if (!DraftState.ApplyRemoteCardPrepJson(team, fullJson))
            {
                Debug.LogError("[BoardNetworkSync] 상대 카드 데이터 파싱 실패");
                return;
            }
            Object.FindFirstObjectByType<CardDraftController>()?.RefreshRemoteCards();
        }

        /// <summary>CardDraft에서 카드를 좌클릭(선택)했을 때 부른다 —
        /// cardId가 이미 선택돼 있었으면 해제(빈 문자열)로 보낸다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetDraftSelectionServerRpc(string category, string cardId)
        {
            SetDraftSelectionRpc(category, cardId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetDraftSelectionRpc(string category, string cardId)
        {
            var controller = Object.FindFirstObjectByType<CardDraftController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] CardDraftController를 못 찾음 — 카드 선택을 못 반영함");
                return;
            }
            controller.ApplySetSelection(category, cardId);
        }

        /// <summary>CardDraft에서 카드를 우클릭(밴 토글)했을 때 부른다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestToggleDraftBanServerRpc(string cardId)
        {
            ToggleDraftBanRpc(cardId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ToggleDraftBanRpc(string cardId)
        {
            var controller = Object.FindFirstObjectByType<CardDraftController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] CardDraftController를 못 찾음 — 밴을 못 반영함");
                return;
            }
            controller.ApplyToggleBan(cardId);
        }

        /// <summary>CardDraft에서 누구든 "다음"을 누르면 부른다 — 둘 다 같이
        /// TerrainSetup으로 넘어가야 하므로(사용자 지정), 누른 쪽도 직접
        /// 처리하지 않고 이 방송이 되돌아오는 걸 거친다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestProceedToTerrainServerRpc()
        {
            ProceedToTerrainRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ProceedToTerrainRpc()
        {
            var controller = Object.FindFirstObjectByType<CardDraftController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] CardDraftController를 못 찾음 — 다음 단계로 못 넘어감");
                return;
            }
            controller.ProceedToTerrain();
        }

        // ── 롤오프 동기화(2026-08-31 신설) ─────────────────────────────────
        // RolloffDialog는 원래 완전히 로컬(각자 클릭한 결과만 자기 화면에
        // 보임)이었다 — 멀티에서 재사용하려면 눈이 양쪽에 똑같이 보여야
        // 한다. 서버가 난수를 다시 굴리는 게 아니라, 클릭한 쪽이 이미 굴린
        // 값을 그대로 전달만 한다(이 프로젝트의 "매뉴얼 시뮬레이터" 철학 —
        // 판정은 안 하고 결과 공유만).

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRollDiceServerRpc(string team, int value)
        {
            SetDiceRpc(team, value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetDiceRpc(string team, int value)
        {
            // FindObjectsInactive.Include 필수 — RolloffDialog는 평소 닫혀
            // 있으면(SetActive(false)) 기본 검색(활성 오브젝트만)에 안
            // 잡힌다. 아래 SetRolloffOpenRpc/SetDiceDialogOpenRpc도 같은
            // 이유로 이 오버로드를 쓴다(실제로 이것 때문에 "못 찾음" 에러가
            // 매번 났었다 — 여기서만은 열려 있을 때 굴리는 게 보통이라
            // 지금까지는 우연히 안 걸렸을 뿐).
            var dialog = Object.FindFirstObjectByType<RolloffDialog>(FindObjectsInactive.Include);
            if (dialog == null)
            {
                Debug.LogError("[BoardNetworkSync] RolloffDialog를 못 찾음 — 롤오프 결과를 못 반영함");
                return;
            }
            dialog.ApplyRemoteRoll(team, value);
        }

        // ── 롤오프/다이스 시뮬레이터 창 열기·닫기 동기화(2026-08-31 추가) ──
        // 굴린 눈 값 동기화(위)와는 별개로, "창 자체"를 열고 닫는 것도 양쪽이
        // 같이 보게 한다(사용자 지정 — 한쪽이 열면 둘 다 뜨고, 한쪽이 닫으면
        // 둘 다 닫힘). 두 창 다 이미 열기/닫기 로직이 있으므로, RPC는 그
        // 진입점(RolloffDialog/DiceRollDialog의 Open/Close)이 요청만 보내고
        // 실제 표시 전환은 ApplyRemoteSetOpen을 거치는 같은 패턴.

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetRolloffOpenServerRpc(bool open)
        {
            SetRolloffOpenRpc(open);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetRolloffOpenRpc(bool open)
        {
            // 닫혀 있을 때 여는 방송이라 반드시 Include — 위 SetDiceRpc 주석 참고.
            var dialog = Object.FindFirstObjectByType<RolloffDialog>(FindObjectsInactive.Include);
            if (dialog == null)
            {
                Debug.LogError("[BoardNetworkSync] RolloffDialog를 못 찾음 — 창 상태를 못 반영함");
                return;
            }
            dialog.ApplyRemoteSetOpen(open);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetDiceDialogOpenServerRpc(bool open)
        {
            SetDiceDialogOpenRpc(open);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetDiceDialogOpenRpc(bool open)
        {
            // 닫혀 있을 때 여는 방송이라 반드시 Include — 위 SetDiceRpc 주석 참고.
            var dialog = Object.FindFirstObjectByType<DiceRollDialog>(FindObjectsInactive.Include);
            if (dialog == null)
            {
                Debug.LogError("[BoardNetworkSync] DiceRollDialog를 못 찾음 — 창 상태를 못 반영함");
                return;
            }
            dialog.ApplyRemoteSetOpen(open);
        }

        // ── 다이스 시뮬레이터 굴림 진행 전체 동기화(2026-08-31 추가) ────────
        // 열기/닫기만이 아니라 어택 풀/히트/아머/회피 각 단계와 되돌리기까지
        // 전부 공유한다(사용자 지정) — 상태 전체(주사위 배열 + 정수/불리언
        // 몇 개)가 512자를 넘을 수 있어 청크로 쪼갠다(로스터/유닛/카드 준비와
        // 같은 이유).

        public void RequestBroadcastDiceState(string stateJson)
        {
            SendChunked(stateJson, (transferId, i, totalChunks, chunk) =>
                    RequestBroadcastDiceStateChunkServerRpc(transferId, i, totalChunks, chunk));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastDiceStateChunkServerRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            BroadcastDiceStateChunkRpc(transferId, chunkIndex, totalChunks, chunk);
        }

        private readonly TextChunkAssembler _diceStateAssembler = new();

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastDiceStateChunkRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            string fullJson = _diceStateAssembler.AddChunk(transferId, chunkIndex, totalChunks, chunk);
            if (fullJson == null)
            {
                return;
            }

            // 닫혀 있을 수도 있으므로(방송이 열기 방송보다 먼저 처리되는
            // 극단적 순서 등) Include — 위 SetDiceRpc 주석 참고.
            var dialog = Object.FindFirstObjectByType<DiceRollDialog>(FindObjectsInactive.Include);
            if (dialog == null)
            {
                Debug.LogError("[BoardNetworkSync] DiceRollDialog를 못 찾음 — 굴림 상태를 못 반영함");
                return;
            }
            dialog.ApplyRemoteState(fullJson);
        }

        // ── 게임판 상태 동기화(라운드/페이즈/VP/팀 색, 2026-08-31 신설) ─────
        // 전부 ScoreboardPanel/PhaseBar/BoardManager가 직접 부른다 — 값 자체는
        // 작아서 청크가 필요 없다. 라운드/페이즈는 절대값을 방송한다(휠 회전
        // 절대각과 같은 이유 — 메시지 유실에도 어긋나지 않게).

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetRoundServerRpc(int roundNumber)
        {
            SetRoundRpc(roundNumber);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetRoundRpc(int roundNumber)
        {
            var scoreboard = Object.FindFirstObjectByType<ScoreboardPanel>();
            if (scoreboard == null)
            {
                Debug.LogError("[BoardNetworkSync] ScoreboardPanel을 못 찾음 — 라운드를 못 반영함");
                return;
            }
            scoreboard.ApplyRemoteRoundNumber(roundNumber);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetPhaseServerRpc(int phaseIndex)
        {
            SetPhaseRpc(phaseIndex);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetPhaseRpc(int phaseIndex)
        {
            var phaseBar = Object.FindFirstObjectByType<PhaseBar>();
            if (phaseBar == null)
            {
                Debug.LogError("[BoardNetworkSync] PhaseBar를 못 찾음 — 페이즈를 못 반영함");
                return;
            }
            phaseBar.ApplyRemotePhaseIndex(phaseIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetMissionVpServerRpc(string team, int value)
        {
            SetMissionVpRpc(team, value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetMissionVpRpc(string team, int value)
        {
            var scoreboard = Object.FindFirstObjectByType<ScoreboardPanel>();
            if (scoreboard == null)
            {
                Debug.LogError("[BoardNetworkSync] ScoreboardPanel을 못 찾음 — 미션VP를 못 반영함");
                return;
            }
            scoreboard.ApplyRemoteMissionVp(team, value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetKillVpServerRpc(string team, int value)
        {
            SetKillVpRpc(team, value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetKillVpRpc(string team, int value)
        {
            var scoreboard = Object.FindFirstObjectByType<ScoreboardPanel>();
            if (scoreboard == null)
            {
                Debug.LogError("[BoardNetworkSync] ScoreboardPanel을 못 찾음 — 파괴VP를 못 반영함");
                return;
            }
            scoreboard.ApplyRemoteKillVp(team, value);
        }

        /// <summary>스코어보드에서 플레이어 이름을 클릭해 색을 고르면 부른다
        /// — 보드 전체(배치된 유닛/예비대/마커/미션 목표 마커) 소급 재도색은
        /// BoardManager.ApplyTeamColorLocal이 맡는다(스코어보드 자신의 팀
        /// 이름 라벨 색은 GameConstants.TeamColors를 매 프레임 그대로
        /// 반영하므로 별도 처리가 필요 없다 — ScoreboardPanel.Update() 참고).</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSetTeamColorServerRpc(string team, Color color)
        {
            SetTeamColorRpc(team, color);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SetTeamColorRpc(string team, Color color)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 팀 색을 못 반영함");
                return;
            }
            board.ApplyTeamColorLocal(team, color);
        }

        // ── 지형 배치 동기화(TerrainSetup, 2026-08-31 신설) ────────────────
        // 마커와 완전히 같은 패턴 — 지형 조각도 UI 계층 밑이라 NetworkObject로
        // 스폰할 수 없다. 배치는 호스트가 id를 발급해 방송하고, 이동/회전/
        // 삭제는 그 id로 지목한다.

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPlaceTerrainServerRpc(string moduleId, Vector2 point)
        {
            int id = ++_nextTerrainId;
            PlaceTerrainRpc(moduleId, point, id);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlaceTerrainRpc(string moduleId, Vector2 point, int id)
        {
            var controller = Object.FindFirstObjectByType<TerrainSetupController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] TerrainSetupController를 못 찾음 — 지형을 못 놓음");
                return;
            }
            controller.SpawnLocalTerrainVisual(moduleId, point, id);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestMoveTerrainServerRpc(int id, Vector2 point)
        {
            MoveTerrainRpc(id, point);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MoveTerrainRpc(int id, Vector2 point)
        {
            var controller = Object.FindFirstObjectByType<TerrainSetupController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] TerrainSetupController를 못 찾음 — 지형을 못 옮김");
                return;
            }
            controller.MoveLocalTerrainVisual(id, point);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestRotateTerrainServerRpc(int id, float rotationDeg)
        {
            RotateTerrainRpc(id, rotationDeg);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RotateTerrainRpc(int id, float rotationDeg)
        {
            var controller = Object.FindFirstObjectByType<TerrainSetupController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] TerrainSetupController를 못 찾음 — 지형을 못 돌림");
                return;
            }
            controller.RotateLocalTerrainVisual(id, rotationDeg);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestDeleteTerrainServerRpc(int id)
        {
            DeleteTerrainRpc(id);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void DeleteTerrainRpc(int id)
        {
            var controller = Object.FindFirstObjectByType<TerrainSetupController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] TerrainSetupController를 못 찾음 — 지형을 못 지움");
                return;
            }
            controller.DeleteLocalTerrainVisual(id);
        }

        /// <summary>TerrainSetup의 "게임 시작"을 누르면 부른다 — 양쪽 모두
        /// 지형이 이미 실시간으로 동기화돼 있으므로 스냅샷을 따로 보내지
        /// 않고, 각자 자기 화면의 지형으로 MapData.TerrainPieces를 채운 뒤
        /// GameBoard로 넘어가라는 신호만 방송한다.</summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestStartGameServerRpc()
        {
            StartGameRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void StartGameRpc()
        {
            var controller = Object.FindFirstObjectByType<TerrainSetupController>();
            if (controller == null)
            {
                Debug.LogError("[BoardNetworkSync] TerrainSetupController를 못 찾음 — 게임 시작을 못 반영함");
                return;
            }
            controller.CompleteFromNetwork();
        }

        // ── 되돌리기(undo/redo) 스택 동기화(2026-08-31 추가) ────────────────
        // 처음엔 카스케이드가 끝난 뒤 보드 전체 상태를 통째로 다시 보내는
        // 방식으로 짰는데, 그러면 "상대가 한 조작은 내 되돌리기 스택에
        // 전혀 안 쌓인다"는 더 근본적인 문제가 그대로 남아있었다 — 양쪽이
        // 번갈아 되돌리기/다시 실행을 하면 서로 상대의 스택에 없는 항목을
        // 기준으로 판단해서, 상대가 그 사이 만들거나 지운 걸 도로 되살리거나
        // 없애버리는 일이 생겼다(사용자 보고). 그래서 두 단계로 다시 짰다:
        // (1) 커밋될 때마다(BoardManager.UndoRedo.cs의 CommitUndoTransaction)
        // 그 항목(라벨+이전 스냅샷)을 상대에게도 보내 자기 스택에 똑같이
        // 쌓게 한다(RequestBroadcastUndoPush) — 이제 두 스택 내용이 항상
        // 같은 순서로 같아진다. (2) 카스케이드(여러 단계를 한 번에 취소/
        // 복원)가 끝나면, 보드 상태가 아니라 "네 스택에서도 여기까지
        // 진행해라"는 목표 인덱스만 보낸다(RequestBroadcastUndoCascade) —
        // 상대는 이미 (1) 덕분에 같은 내용을 가진 자기 스택으로 같은
        // 카스케이드를 그대로 재생해서 스스로 같은 보드 상태에 도달한다.
        // 두 RPC 모두, 보낸 쪽은 이미 로컬에서 직접 처리했으므로 이 방송의
        // 루프백은 자기 자신에게는 무시해야 한다 — CardPrep의 team
        // 파라미터로 자기 메아리를 구분하는 것과 같은 방식으로, 보낸
        // 클라이언트 id를 함께 실어 보낸다.

        public void RequestBroadcastUndoPush(string label, string team, string compositeKey, string snapshotJson)
        {
            ulong senderId = NetworkManager.Singleton.LocalClientId;
            SendChunked(snapshotJson, (transferId, i, totalChunks, chunk) =>
                    RequestBroadcastUndoPushChunkServerRpc(transferId, senderId, label, team, compositeKey, i, totalChunks, chunk));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastUndoPushChunkServerRpc(int transferId, ulong senderId, string label, string team, string compositeKey, int chunkIndex, int totalChunks, string chunk)
        {
            BroadcastUndoPushChunkRpc(transferId, senderId, label, team, compositeKey, chunkIndex, totalChunks, chunk);
        }

        private readonly TextChunkAssembler _undoPushAssembler = new();

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastUndoPushChunkRpc(int transferId, ulong senderId, string label, string team, string compositeKey, int chunkIndex, int totalChunks, string chunk)
        {
            // 자기 메아리라도 조각을 계속 흘려보내야 어셈블러가 이 transferId를
            // 정상적으로 완료 처리하고 정리한다 — 완료 여부 확인(fullJson이
            // null인지)까지는 항상 하고, 실제 사용만 메아리일 때 건너뛴다.
            string fullJson = _undoPushAssembler.AddChunk(transferId, chunkIndex, totalChunks, chunk);
            if (fullJson == null)
            {
                return;
            }

            if (senderId == NetworkManager.Singleton.LocalClientId)
            {
                return; // 내가 커밋한 항목의 메아리 — 이미 로컬에서 직접 스택에 쌓았다.
            }

            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 되돌리기 기록을 못 반영함");
                return;
            }
            board.ApplyRemoteUndoPush(label, team, compositeKey, fullJson);
        }

        /// <summary>연속 편집 합치기(2026-09-04 추가, BoardManager.UndoRedo.cs
        /// 의 CommitUndoTransaction 참고) — 스택에 새 항목을 쌓는 대신 맨 위
        /// 항목의 라벨만 바꿀 때 쓴다. 값이 작아(라벨 한 줄) 청크가
        /// 필요없다(카스케이드/이모트와 같은 이유).</summary>
        public void RequestBroadcastUndoRelabel(string label, string team)
        {
            ulong senderId = NetworkManager.Singleton.LocalClientId;
            RequestBroadcastUndoRelabelServerRpc(senderId, label, team);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastUndoRelabelServerRpc(ulong senderId, string label, string team)
        {
            BroadcastUndoRelabelRpc(senderId, label, team);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastUndoRelabelRpc(ulong senderId, string label, string team)
        {
            if (senderId == NetworkManager.Singleton.LocalClientId)
            {
                return; // 내가 커밋한 항목의 메아리 — 이미 로컬에서 직접 라벨을 바꿨다.
            }
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 되돌리기 라벨 갱신을 못 반영함");
                return;
            }
            board.ApplyRemoteUndoRelabel(label, team);
        }

        /// <summary>카스케이드(값이 작아 청크가 필요 없다) — isRedo=false면
        /// _undoStack을 targetIndex까지, true면 _redoStack을 targetIndex까지
        /// 되감는다(BoardManager.CancelOperationsDownTo/RestoreOperationsDownTo와
        /// 같은 의미).</summary>
        public void RequestBroadcastUndoCascade(bool isRedo, int targetIndex)
        {
            ulong senderId = NetworkManager.Singleton.LocalClientId;
            RequestBroadcastUndoCascadeServerRpc(senderId, isRedo, targetIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastUndoCascadeServerRpc(ulong senderId, bool isRedo, int targetIndex)
        {
            BroadcastUndoCascadeRpc(senderId, isRedo, targetIndex);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastUndoCascadeRpc(ulong senderId, bool isRedo, int targetIndex)
        {
            if (senderId == NetworkManager.Singleton.LocalClientId)
            {
                return; // 내가 수행한 카스케이드의 메아리 — 이미 로컬에서 직접 처리했다.
            }
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 되돌리기 진행을 못 반영함");
                return;
            }
            board.ApplyRemoteUndoCascade(isRedo, targetIndex);
        }

        // ── 게임 도중 멀티 시작(2026-09-02 신설) ───────────────────────────
        // MultiplayerConnectDialog를 GameBoard 마커바에서도 열 수 있게 되면서
        // (사용자 요청) 생긴 새 경로 — 호스트가 이미 GameBoard에서 혼자
        // 플레이 중일 때 상대가 접속하면, 예전처럼 무조건 CardPrep으로
        // 보내지 않고 지금 보드 상태를 그대로 넘겨받아 둘 다 같은 GameBoard로
        // 합류한다. 다음 화면 결정 자체를 호스트만 내리고 방송한다 —
        // MultiplayerConnectDialog.OnClientConnected 참고(예전엔 호스트/
        // 클라이언트 양쪽이 "2명이면 CardPrep"을 각자 독립적으로 계산했는데,
        // 그 방식은 호스트가 GameBoard 중일 수 있는 지금은 더 이상 안전하지
        // 않다 — 호스트만 결정해서 방송하고, 클라이언트는 그 방송을 받을
        // 때까지 스스로 씬을 넘어가지 않는다).

        /// <summary>기존 CardPrep 흐름(Entry에서 새로 시작하는 경우)으로
        /// 보낼 때 부른다 — 값이 없어 청크가 필요 없다. ProceedToTerrainRpc와
        /// 같은 모양이지만 대상이 다르다(그건 CardDraftController, 이건
        /// 씬 전환 자체).</summary>
        public void RequestBroadcastProceedToCardPrep()
        {
            RequestBroadcastProceedToCardPrepServerRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastProceedToCardPrepServerRpc()
        {
            ProceedToCardPrepRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ProceedToCardPrepRpc()
        {
            SceneManager.LoadScene(GameConstants.CardPrepSceneName);
        }

        private readonly TextChunkAssembler _midGameStateAssembler = new();

        /// <summary>BoardManager.BroadcastFullStateForMidGameJoin이 부른다 —
        /// fullStateJson은 BoardManager.BuildFullStateTree()(저장 파일과
        /// 완전히 같은 스키마, SaveGame이 쓰는 것 그대로)를 MiniJson으로
        /// 직렬화한 것. 호스트만 이걸 부른다(그 호출부에서 이미 IsServer를
        /// 확인함).</summary>
        public void RequestBroadcastMidGameState(string fullStateJson)
        {
            SendChunked(fullStateJson, (transferId, i, totalChunks, chunk) =>
                    RequestBroadcastMidGameStateChunkServerRpc(transferId, i, totalChunks, chunk));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestBroadcastMidGameStateChunkServerRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            BroadcastMidGameStateChunkRpc(transferId, chunkIndex, totalChunks, chunk);
        }

        /// <summary>호스트 자신에게도 루프백되지만, 호스트는 이미 이 상태
        /// 그대로이므로(자기 자신의 보드에서 막 만든 스냅샷) IsServer 확인
        /// 하나로 무시한다 — 클라이언트만 실제로 GameLoadRequest.PendingData에
        /// 담아 GameBoard로 이동한다(LoadGame 화면이 저장 파일을 불러올 때와
        /// 완전히 같은 경로 — GameFlowBootstrap.BuildGameBoard가
        /// GameSaveIO.ApplyLoadedStaticState를, BoardManager.Start()가
        /// ApplyLoadedLiveState를 그대로 처리해준다).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastMidGameStateChunkRpc(int transferId, int chunkIndex, int totalChunks, string chunk)
        {
            string fullJson = _midGameStateAssembler.AddChunk(transferId, chunkIndex, totalChunks, chunk);
            if (fullJson == null)
            {
                return;
            }

            if (NetworkManager.Singleton.IsServer)
            {
                return; // 호스트 자신의 루프백 — 이미 이 상태 그대로다.
            }

            if (!(MiniJson.Parse(fullJson) is Dictionary<string, object> wrapper))
            {
                Debug.LogError("[BoardNetworkSync] 게임 도중 상태 JSON 파싱 실패");
                return;
            }
            // full_state(저장 파일과 같은 스키마)와 undo_history(되돌리기
            // 스택 시딩, 2026-09-02 추가 — 사용자가 발견한 "합류한 쪽 되돌리기가
            // 안 맞는" 버그 수정)를 한 봉투에 같이 담아 보냈다.
            GameLoadRequest.PendingData = GameSaveIO.GetDict(wrapper, "full_state");
            GameLoadRequest.PendingUndoHistory = GameSaveIO.GetDict(wrapper, "undo_history");
            SceneManager.LoadScene(GameConstants.GameBoardSceneName);
        }

        // ── 맵 이모트(2026-09-02 신설) ──────────────────────────────────
        // 마커와 같은 "요청→방송→각자 로컬로 완전히 새로 만들기" 패턴이지만
        // 훨씬 더 단순하다 — id 발급도, 삭제/이동 방송도 필요 없다(잠깐 떴다
        // 스스로 사라지는 순수 시각 효과라 나중에 다시 지목할 일이 없음).
        // 값이 작아(스프라이트 번호 + 좌표) 청크도 필요 없다.

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestPlaceEmoteServerRpc(int spriteIndex, Vector2 point)
        {
            PlaceEmoteRpc(spriteIndex, point);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlaceEmoteRpc(int spriteIndex, Vector2 point)
        {
            var board = Object.FindFirstObjectByType<BoardManager>();
            if (board == null)
            {
                Debug.LogError("[BoardNetworkSync] BoardManager를 못 찾음 — 이모트를 못 그림");
                return;
            }
            board.SpawnLocalEmote(spriteIndex, point);
        }

        // ── 채팅(2026-09-04 신설) ──────────────────────────────────────────
        // 이모트와 같은 이유로 청크가 필요 없다(짧은 한 줄 텍스트,
        // ChatController가 입력창 글자 수 제한으로 이미 짧게 보장한다). 이걸
        // 받을 ChatController는 이 컴포넌트처럼 앱 시작 시 한 번 만들어져
        // DontDestroyOnLoad로 모든 씬에 걸쳐 존재하는 영구 싱글턴이라
        // (GameFlowBootstrap.EnsureChatController), BoardManager처럼 씬마다
        // 있는지 찾을 필요 없이 바로 Instance로 부른다.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestSendChatServerRpc(string team, string message)
        {
            BroadcastChatRpc(team, message);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BroadcastChatRpc(string team, string message)
        {
            if (ChatController.Instance == null)
            {
                Debug.LogError("[BoardNetworkSync] ChatController.Instance가 없음 — 채팅을 못 띄움");
                return;
            }
            ChatController.Instance.ReceiveChatMessage(team, message);
        }
    }
}
