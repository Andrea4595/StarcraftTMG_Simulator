using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>게임 화면으로 넘겨주는 배치 프리셋 핸드오프 저장소 — 지도
    /// 크기/배치구역/미션 목표, 그리고 TerrainPieces(지형, 아래 참고).
    /// Selection 화면에서 배치 프리셋을 고르는 순간 MapPreset/DeploymentZones/
    /// MissionObjectives가 그 파일 내용으로 채워지고(2026-08-30 재구성 이전엔
    /// MapSetupController의 "완료" 버튼이 라이브 편집 내용을 직접 채웠지만,
    /// 이제 그 화면(MapAuthoringController)은 프리셋 "제작"만 하고 실제
    /// 핸드오프는 Selection이 담당한다), TerrainPieces는 그 뒤 TerrainSetup
    /// 화면에서 매 게임 새로 배치한 결과로 채워진다(지형은 프리셋으로 저장
    /// 하지 않는다는 사용자 지정 — 배치 프리셋 파일 자체엔 항상 빈 지형
    /// 목록만 저장된다). 미션 셋업(파라미터/점수 조건/서플라이 등)의 데이터는
    /// 별도인 MissionSettingsData가 담당한다.</summary>
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
