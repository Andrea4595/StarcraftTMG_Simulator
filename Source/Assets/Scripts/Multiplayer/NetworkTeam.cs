using Unity.Netcode;

namespace TmgBoard
{
    /// <summary>멀티플레이어 드래프트 흐름(CardPrep/CardDraft)에서 호스트/
    /// 클라이언트를 팀 A/B로 고정 배정한다(사용자 지정, 2026-08-31 — 호스트=A,
    /// 참가자=B, 화면에서 따로 고르지 않는다). 연결 안 된 상태에서 부르는 건
    /// 의미가 없으므로 호출부가 항상 NetworkManager.Singleton.IsListening을
    /// 먼저 확인해야 한다.</summary>
    public static class NetworkTeam
    {
        public const string Host = "A";
        public const string Client = "B";

        public static string LocalTeam()
        {
            return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost ? Host : Client;
        }

        public static string OpponentOf(string team)
        {
            return team == Host ? Client : Host;
        }
    }
}
