using UnityEngine;

namespace TmgBoard
{
    /// <summary>미션 설정 화면에서 놓은 지형 조각 하나. Position은
    /// DeploymentZoneData/MissionObjectiveData와 같은 관례로 지도 로컬 mm,
    /// 코너 원점([0, mapSize]) 기준이다.</summary>
    public class TerrainPieceData
    {
        public string ModuleId = "";
        public Vector2 Position;
        public float RotationDeg;
    }
}
