using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>
    /// 로스터 JSON에서 유닛 하나에 들어있는 "모든 정보"(스탯표/태그/능력/무기 등,
    /// 그동안 RosterImporter가 무시해오던 필드들)를 유닛 상세 패널 표시용으로
    /// 구조화한 것 — 게임 로직에는 전혀 쓰이지 않는 순수 표시 데이터다.
    /// PendingUnitDef.Detail / Unit.Detail에 그대로 실려 다닌다(불변이라 undo/redo
    /// 스냅샷·클론 시에도 깊은 복사 없이 참조만 그대로 넘기면 된다). 로스터
    /// 임포트가 아닌 애드혹 유닛/토큰은 null.
    /// </summary>
    public class RosterUnitDetail
    {
        public string NameEn = "";
        public string NameKo = "";
        public string UnitType = "";

        // "stat" 블록 중 move/cohesion(이미 PendingUnitDef.MoveInch/CoherencyInch로
        // 따로 쓰임) 나머지 — 값이 없으면(JSON null) 빈 문자열.
        public string Shield = "";
        public string Evasion = "";
        public string Armor = "";
        public string Hp = "";
        public string Size = "";

        public List<RosterTag> Tags = new List<RosterTag>();
        public List<RosterAbilityEntry> Abilities = new List<RosterAbilityEntry>();

        /// <summary>로스터 작성 시 고른 서플라이 단계(참고용 표시일 뿐 — 인게임
        /// 실제 서플라이는 항상 남은 모델 수로 다시 계산된다).</summary>
        public int? SquadTierIndex;

        /// <summary>전문가 이름(이중언어) — PendingUnitDef.Specialists(한글로 이미
        /// 확정된 문자열, 모델 메모 자동배정용)와는 별개로 상세 패널 전용.</summary>
        public List<RosterSpecialistEntry> Specialists = new List<RosterSpecialistEntry>();
    }

    public class RosterTag
    {
        public string NameEn = "";
        public string NameKo = "";
    }

    public class RosterSpecialistEntry
    {
        public string NameEn = "";
        public string NameKo = "";
    }

    /// <summary>"abilities" 배열의 항목 하나 — kind가 "rule"이면 Rule*/Type/Cost가,
    /// "weapon"이면 Weapon이 채워진다(둘 다 채워지는 경우는 없음).</summary>
    public class RosterAbilityEntry
    {
        public string Kind = "";
        public string Id = "";
        public string NameEn = "";
        public string NameKo = "";
        public string Phase = "";
        public bool IsUpgrade;

        // kind == "rule"
        public string Type = "";
        public int Cost;
        public string RuleEn = "";
        public string RuleKo = "";

        // kind == "weapon" (null이면 rule 항목)
        public RosterWeaponStat Weapon;
    }

    /// <summary>tgt/surge는 이제 tags/keyword와 같은 {"name":{"en","ko"}} 이중언어
    /// 객체로 온다(예전엔 평문자열이라 룰북을 대조해 하드코딩 번역표를 썼는데,
    /// 사용자 지정으로 그 방식을 버리고 로스터가 준 한글을 그대로 표시하는
    /// 쪽으로 바꿨다) — RosterTag를 그대로 재사용한다(모양이 완전히 같음).</summary>
    public class RosterWeaponStat
    {
        public string Range = "";     // rng — 숫자(사거리 인치) 또는 "E"(engagement) 등 문자열
        public RosterTag Target;      // tgt, null이면 정보 없음
        public string Roa = "";       // roa(공격 횟수)
        public string Hit = "";       // hit(명중 요구치, 예: "3+")
        public List<RosterTag> Surge = new List<RosterTag>();
        public string SurgeDie = "";  // sDie
        public string Damage = "";    // dmg
        public List<RosterKeyword> Keywords = new List<RosterKeyword>();
    }

    public class RosterKeyword
    {
        public string NameEn = "";
        public string NameKo = "";
        public string SuffixEn = ""; // 없으면 빈 문자열
        public string SuffixKo = "";
    }
}
