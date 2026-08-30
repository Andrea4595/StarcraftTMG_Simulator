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
    }
}
