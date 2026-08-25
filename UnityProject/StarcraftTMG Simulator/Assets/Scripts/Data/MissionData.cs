using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>미션 생성 화면에서 만든 상태를 게임 화면으로 넘겨주는 핸드오프
    /// 저장소. "게임 시작" 버튼을 누르는 순간 스냅샷이 여기 채워진다. Godot판
    /// autoload 싱글턴 MissionData.gd를 정적 클래스로 포팅 — 플레이 세션 동안
    /// 씬(또는 이 프로젝트처럼 씬 안에서 캔버스를 갈아끼우는 방식)을 넘나들며
    /// 유지된다.</summary>
    public static class MissionData
    {
        public static bool HasData;
        public static string MapPreset = GameConstants.DefaultMapSizePreset;
        public static readonly List<DeploymentZoneData> DeploymentZones = new List<DeploymentZoneData>();

        public static void Clear()
        {
            HasData = false;
            MapPreset = GameConstants.DefaultMapSizePreset;
            DeploymentZones.Clear();
        }
    }
}
