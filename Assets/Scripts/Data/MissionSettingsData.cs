namespace TmgBoard
{
    /// <summary>미션 셋업 화면(MissionSetupController)에서 만든 상태를 게임
    /// 화면으로 넘겨주는 핸드오프 저장소 — 미션 파라미터/점수 획득 조건/추가
    /// 조건(텍스트), 서플라이·라운드 공식, 전투 규모. 지도/배치구역/지형은
    /// 별도인 MapData가 담당한다. "완료" 버튼을 누르는 순간 채워진다.</summary>
    public static class MissionSettingsData
    {
        public static bool HasData;

        public static string MissionName = "";
        public static string MissionParameters = "";
        public static string ScoringConditions = "";
        public static string AdditionalConditions = "";

        /// <summary>서플라이 상한 공식의 기준값 — MatchState.Supply는 라운드가
        /// 바뀔 때마다 BaseSupply + SupplyPerRound * (현재 라운드 - 1)로
        /// 자동 계산된다(ScoreboardPanel.SetRoundNumber).</summary>
        public static int BaseSupply = 0;
        public static int SupplyPerRound = 0;

        /// <summary>게임 화면의 라운드 표시기가 그릴 네모 개수(BuildRoundIndicator)
        /// — 예전엔 "최대 라운드"라는 이름으로 맵 셋업 화면에 있었는데, 미션
        /// 셋업 화면으로 옮겨오며 "라운드 길이"로 이름만 바뀌었다(같은 개념).</summary>
        public static int RoundLength = 5;

        public const string EngagementScaleStandard = "STANDARD ENGAGEMENT";
        public const string EngagementScaleSkirmish = "SKIRMISH LEVEL";
        public static string EngagementScale = EngagementScaleStandard;

        public static void Clear()
        {
            HasData = false;
            MissionName = "";
            MissionParameters = "";
            ScoringConditions = "";
            AdditionalConditions = "";
            BaseSupply = 0;
            SupplyPerRound = 0;
            RoundLength = 5;
            EngagementScale = EngagementScaleStandard;
        }
    }
}
