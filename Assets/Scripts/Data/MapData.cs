using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>맵 셋업 화면(MapSetupController)에서 만든 상태를 게임 화면으로
    /// 넘겨주는 핸드오프 저장소 — 지도 크기/배치구역/미션 목표/지형. "완료"
    /// 버튼을 누르는 순간 스냅샷이 여기 채워진다. 미션 셋업 화면(파라미터/점수
    /// 조건/서플라이 등)의 데이터는 별도인 MissionSettingsData가 담당한다 —
    /// 예전엔 이 둘이 한 화면·한 클래스였다가 사용자 요청으로 "맵 셋업"과
    /// "미션 셋업"으로 화면이 갈라지면서 클래스도 같이 나뉘었다.</summary>
    public static class MapData
    {
        public static bool HasData;
        public static string MapPreset = GameConstants.DefaultMapSizePreset;
        public static readonly List<DeploymentZoneData> DeploymentZones = new List<DeploymentZoneData>();
        public static readonly List<MissionObjectiveData> MissionObjectives = new List<MissionObjectiveData>();
        public static readonly List<TerrainPieceData> TerrainPieces = new List<TerrainPieceData>();

        public static void Clear()
        {
            HasData = false;
            MapPreset = GameConstants.DefaultMapSizePreset;
            DeploymentZones.Clear();
            MissionObjectives.Clear();
            TerrainPieces.Clear();
        }
    }
}
