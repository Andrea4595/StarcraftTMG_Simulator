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
    }
}
