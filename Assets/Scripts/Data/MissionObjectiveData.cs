using UnityEngine;

namespace TmgBoard
{
    /// <summary>미션 설정 화면에서 놓은 미션 목표 마커 하나. Position은
    /// DeploymentZoneData와 같은 관례로 지도 로컬 mm, 코너 원점([0, mapSize])
    /// 기준이다 — 게임 화면(중심 원점)에서 쓸 때는 BoardManager의
    /// CornerToCenterMm()로 변환해야 한다.</summary>
    public class MissionObjectiveData
    {
        public int Number;
        public Vector2 Position;
    }
}
