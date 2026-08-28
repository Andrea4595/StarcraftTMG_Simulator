namespace TmgBoard
{
    /// <summary>지도 가장자리를 따라 그어진 배치구역 구간 하나. 미션 설정
    /// 화면에서 그린 뒤 MissionData로 넘어와, 게임 화면의 배치 밴드 계산에
    /// 쓰인다. Edge: "left"/"right"/"top"/"bottom". Along 값은 그 변을 따라
    /// 잰 좌표(left/right는 y, top/bottom은 x), 지도 로컬 mm 기준.</summary>
    public class DeploymentZoneData
    {
        public string Edge = "";
        public string Player = "";
        public float StartAlong;
        public float EndAlong;
    }
}
