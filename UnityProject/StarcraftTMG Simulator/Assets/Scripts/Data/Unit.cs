using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>
    /// 하나의 유닛(모델 그룹). 다이얼 메뉴의 "복제"는 같은 유닛에 모델을
    /// 추가하고, "유닛 이동"은 이 유닛에 속한 모든 모델을 함께 움직인다.
    /// 모델들은 유닛 이름을 공유한다(개별 모델 이름은 없음).
    /// </summary>
    public class Unit
    {
        public string UnitName = "";
        public string Team = "neutral";
        public float CoherencyInch = GameConstants.DefaultCoherencyInch;
        public float MoveInch = GameConstants.DefaultMoveInch;
        public readonly List<Base> Models = new List<Base>();

        /// <summary>로스터 "tokens" 배열에서 온 토큰 유닛이면 true. 다이얼 메뉴에서
        /// 데미지 기록/유닛 되돌리기/유닛 이동 시작을 감춘다.</summary>
        public bool IsToken;

        /// <summary>로스터 stat.spd가 null(이동 스탯 자체가 없는 유닛)이면 false.
        /// 다이얼 메뉴에서 "유닛 이동 시작"을 감춘다.</summary>
        public bool CanMove = true;

        /// <summary>로스터 "squad_tiers" — 남은 모델 수 구간별 서플라이 값
        /// 단계표. 스코어보드의 팀별 서플라이 표시가 CurrentSupplyCost()로
        /// 참조한다.</summary>
        public readonly List<SupplyTier> SupplyTiers = new List<SupplyTier>();

        /// <summary>지금 남은 모델 수(Models.Count)에 해당하는 단계의 서플라이
        /// 값. 모델이 줄어 하위 단계로 내려가면 그 즉시(매번 다시 계산하므로
        /// 별도 갱신 로직 없이) 반영된다. 해당하는 구간이 없으면(단계표가 없는
        /// 유닛, 또는 모델이 전부 사라진 경우) 0.</summary>
        public int CurrentSupplyCost()
        {
            int count = Models.Count;
            foreach (var tier in SupplyTiers)
            {
                if (count >= tier.ModelMin && count <= tier.ModelMax)
                {
                    return tier.Supply;
                }
            }
            return 0;
        }
    }
}
