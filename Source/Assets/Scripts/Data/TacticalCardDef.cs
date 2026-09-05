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

        /// <summary>이 카드의 종족별 포인트(테란 CP/저그 BM/프로토스 EN)
        /// 제공량. 로스터 최상위의 "resource_label"({"full","abbr"})이
        /// 어느 포인트 종류인지 정하고, 이 카드 자체의 "resource" 필드가
        /// 그 개수다 — 로스터(팀) 전체가 같은 종족이라 ResourceAbbr는 모든
        /// 카드가 같은 값을 갖지만, 값이 없는 구형 로스터 파일과의 호환을
        /// 위해 카드마다 들고 있다(빈 문자열이면 표시하지 않는다).</summary>
        public string ResourceAbbr = "";
        public int ResourceAmount;

        /// <summary>카드 버튼 아래에 이름만 나열되고, 클릭하면 펼쳐지며 정보가
        /// 드러난다(사용자 지정) — 유닛 능력과 정확히 같은 모양(kind/name/
        /// phase/type/cost/rule)이라 RosterImporter.ParseAbilities를 그대로
        /// 재사용해서 읽는다.</summary>
        public List<RosterAbilityEntry> Abilities = new List<RosterAbilityEntry>();
    }
}
