using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>진행 중인 한 판의 상태(라운드/서플라이/팀별 VP). Godot판
    /// autoload 싱글턴 MatchState.gd를 정적 클래스로 포팅 — 플레이 세션 동안
    /// 씬(또는 이 프로젝트처럼 캔버스를 갈아끼우는 방식)을 넘나들며
    /// 유지된다.</summary>
    public static class MatchState
    {
        public static int RoundNumber = 1;
        public static int Supply = 0;

        public static readonly string[] PhaseNames = { "Movement Phase", "Assault Phase", "Combat Phase", "Cleanup Phase" };
        public static int PhaseIndex = 0;

        public static readonly Dictionary<string, int> MissionVp = new Dictionary<string, int> { { "A", 0 }, { "B", 0 } };
        public static readonly Dictionary<string, int> KillVp = new Dictionary<string, int> { { "A", 0 }, { "B", 0 } };

        public static int TotalVp(string player)
        {
            int mission = MissionVp.TryGetValue(player, out var m) ? m : 0;
            int kill = KillVp.TryGetValue(player, out var k) ? k : 0;
            return mission + kill;
        }
    }
}
