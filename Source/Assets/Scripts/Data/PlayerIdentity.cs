using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>연결된 두 플레이어의 닉네임 — MatchState와 같은 씬-독립
    /// 정적 상태(BoardNetworkSync를 통해 양쪽에 동기화됨, BoardManager.
    /// Nickname.cs 참고). 비어있으면 표시할 때 "플레이어 A"/"플레이어 B"로
    /// 대체한다(DisplayName) — 닉네임 입력이 선택 사항이므로.</summary>
    public static class PlayerIdentity
    {
        public static readonly Dictionary<string, string> Nicknames = new Dictionary<string, string> { { "A", "" }, { "B", "" } };

        public static string DisplayName(string team)
        {
            return Nicknames.TryGetValue(team, out var n) && !string.IsNullOrWhiteSpace(n) ? n : $"플레이어 {team}";
        }
    }
}
