namespace TmgBoard
{
    /// <summary>Entry 씬에서 "맵"/"미션" 중 뭘 먼저 골랐든, 둘 다 끝나야 게임
    /// 화면으로 넘어간다 — 이 두 플래그로 "이미 끝낸 쪽"을 기억해둔다.
    /// GameFlowBootstrap이 각 셋업 화면의 완료 이벤트에서 이 값을 보고 다음
    /// 씬(남은 셋업 화면, 또는 둘 다 끝났으면 게임 화면)을 고른다.</summary>
    public static class GameFlowState
    {
        public static bool MapSetupDone;
        public static bool MissionSetupDone;

        public static void Reset()
        {
            MapSetupDone = false;
            MissionSetupDone = false;
        }
    }
}
