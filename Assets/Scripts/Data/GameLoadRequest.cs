using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>Entry의 "이어하기" 화면(LoadGameController)에서 저장 파일을
    /// 골랐을 때, 그 파싱된 JSON 트리를 GameBoard 씬이 뜰 때까지 들고
    /// 있는 임시 전달 통로 — 씬 전환 자체는 static 클래스라 데이터가
    /// 자연히 유지되는 MapData/MissionSettingsData 같은 다른 핸드오프와
    /// 같은 이유로 static이다. GameFlowBootstrap.BuildGameBoard()가 맨
    /// 먼저 이 값을 보고 있으면 GameSaveIO.ApplyLoadedStaticState로 정적
    /// 부분(지도/미션/라운드/팀 색)을 채우고, BoardManager.Start()가 그
    /// 다음(자기 자신의 지형/배치구역/미션마커 생성이 끝난 뒤) 라이브
    /// 부분(유닛/마커/예비대/택티컬 카드)을 채운 뒤 반드시 null로
    /// 비운다 — 안 비우면 그다음에 "새 게임"으로 GameBoard에 들어왔을 때도
    /// 이전 저장을 다시 불러와 버린다.</summary>
    public static class GameLoadRequest
    {
        public static Dictionary<string, object> PendingData;

        /// <summary>게임 도중 멀티 합류 전용(2026-09-02 추가) — 호스트의
        /// 되돌리기 스택(BoardManager.UndoRedo.cs의 _undoStack/_redoStack)을
        /// 그대로 이어받을 때 쓴다. 저장 파일 불러오기는 이 값을 안 쓴다
        /// (파일에서 온 되돌리기 히스토리는 대신 PendingData 트리 자체의
        /// "undo_history" 키를 통해서 온다 — 2026-09-04부터, 리플레이 저장
        /// 파일(BoardManager.Save.cs의 BuildFullStateTree(includeUndoHistory:
        /// true))에만 그 키가 실제로 채워지고, 평범한 저장은 여전히 없어서
        /// 이 필드는 파일 불러오기 경로에서는 항상 null로 남는다).
        /// BoardManager.Start()가 ApplyLoadedLiveState 다음에 이걸 확인해서
        /// ApplySeededUndoHistory로 반영한 뒤 반드시 null로 비운다.</summary>
        public static Dictionary<string, object> PendingUndoHistory;

        /// <summary>"같이 하기"→"호스트로 시작"→"이어하기"로 저장 파일을
        /// 골랐을 때만 true로 세팅된다(2026-09-04 추가,
        /// MultiplayerConnectDialog.OnHostContinueClicked → GameFlowBootstrap
        /// 의 LoadGame 화면). 이 값이 true인 채로 GameBoard가 저장 상태를 다
        /// 채우고 나면(PendingData 처리 직후), BoardManager.Start()가 곧바로
        /// MultiplayerConnectDialog.OpenAndStartHosting()을 불러 선택 화면을
        /// 다시 거치지 않고 "새 게임"을 누른 것과 똑같이 곧장 호스팅을
        /// 시작하고 코드를 띄운다. 반드시 다 쓴 뒤 false로 되돌린다(안
        /// 그러면 다음에 순수 "이어하기"로 들어와도 자동으로 호스팅이
        /// 시작돼버린다).</summary>
        public static bool AutoOpenMultiplayerAfterLoad;
    }
}
