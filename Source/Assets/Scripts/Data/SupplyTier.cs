namespace TmgBoard
{
    /// <summary>서플라이 능력치의 한 단계. 남은 모델 수가 [ModelMin, ModelMax]
    /// 구간에 들면 이 Supply 값이 적용된다 — 룰북 6.1 "유닛 카드의 서플라이
    /// 능력치는 남은 모델 수와 현재 서플라이 값을 연결한다. 사상자로 모델
    /// 수가 하위 단계로 줄면 즉시 갱신된다." 로스터 JSON의 "squad_tiers"
    /// 배열 항목 하나에 대응한다. Pts(포인트 비용)는 게임 로직에는 안
    /// 쓰이지만 유닛 상세 패널 표시용으로 읽는다.</summary>
    public class SupplyTier
    {
        public int ModelMin;
        public int ModelMax;
        public int Supply;
        public int Pts;
    }
}
