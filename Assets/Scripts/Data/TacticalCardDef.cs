using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>로스터 "tactical_cards" 배열의 카드 하나. Count는 보유 매수(예:
    /// "군수 공장" x2), Remaining은 그중 지금 안 쓴(탈진 안 된) 매수 — 카드
    /// 버튼을 좌클릭하면 하나씩 줄고 우클릭하면 하나씩 늘어난다. 0에서 좌클릭
    /// 하면 전부 복구, 최대치에서 우클릭하면 전부 소진된다(사용자 요청).</summary>
    public class TacticalCardDef
    {
        public string Name = "";
        public string Team = "neutral";
        public int Count = 1;
        public int Remaining = 1;

        /// <summary>카드 버튼 아래에 이름만 나열되고, 클릭하면 펼쳐지며 정보가
        /// 드러난다(사용자 지정) — 유닛 능력과 정확히 같은 모양(kind/name/
        /// phase/type/cost/rule)이라 RosterImporter.ParseAbilities를 그대로
        /// 재사용해서 읽는다.</summary>
        public List<RosterAbilityEntry> Abilities = new List<RosterAbilityEntry>();
    }
}
