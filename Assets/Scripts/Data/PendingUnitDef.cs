using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 아직 보드 위에 배치되지 않은 유닛 정의. 예비대 목록 패널에 버튼으로
    /// 표시되고, 클릭하면 BoardManager.StartDeployment()가 실제 배치 드래그로
    /// 이어간다. "유닛 되돌리기"로 되돌아온 유닛도 이 형태로 다시 등록된다
    /// (damages 유지). Godot판 GameBoard.gd의 _pending_units 딕셔너리 포팅.
    /// </summary>
    public class PendingUnitDef
    {
        public string Name = "";
        public string Team = "neutral";
        public int ModelCount = 1;
        public Vector2 SizeMm = new Vector2(32f, 32f);
        public Color FillColor = new Color(0.6f, 0.6f, 0.6f, 0.85f);
        public float MoveInch = GameConstants.DefaultMoveInch;
        public float CoherencyInch = GameConstants.DefaultCoherencyInch;
        public bool CanMove = true;
        public bool IsDisplacement;
        public List<SupplyTier> SupplyTiers = new List<SupplyTier>();
        public List<int> Damages = new List<int>();

        /// <summary>배치될 때 그대로 "범위 표시"에 등록될 사거리들(로스터 임포트,
        /// 또는 "유닛 되돌리기"로 되돌아오며 유지된 것).</summary>
        public List<RangeSpec> Ranges = new List<RangeSpec>();

        /// <summary>로스터 "supply_override" — 전문가 모델과 무관한 별개의
        /// 서플라이 "대입"값(단계표 값에 더하는 게 아니라 통째로 대체). null이면
        /// 그런 능력이 없는 것. 배치 시 그대로 Unit.SupplyOverride로 옮겨간다.</summary>
        public int? SupplyOverride;

        /// <summary>로스터 "specialists" — 이름만 담긴 리스트(예: "AGG-12 전문가",
        /// {"en","ko"} 중 한글을 골라 문자열로 미리 변환해둔 것). 어떤 모델이
        /// 어느 전문가인지는 로스터가 아니라 시뮬레이터 쪽에서 정한다: 배치
        /// 순서대로(리더가 0번째, 그 다음 팔로워가 생성되는 순서) 앞에서부터
        /// 하나씩 그 모델의 메모에 자동으로 채워 넣는다(BoardManager.Roster.cs의
        /// BeginDeploymentDrag / BoardManager.UnitMove.cs의 SpawnDeploymentFollowers).</summary>
        public List<string> Specialists = new List<string>();

        /// <summary>유닛 상세 패널 표시용 — 로스터 JSON의 나머지 모든 필드(스탯표/
        /// 태그/능력/무기 등). 로스터 임포트가 아닌 애드혹 유닛은 null.</summary>
        public RosterUnitDetail Detail;

        /// <summary>이 유닛을 지금 배치하면 소비될 서플라이 — Unit.
        /// CurrentSupplyCost()와 같은 규칙(SupplyOverride 우선, 없으면 단계표)
        /// 이지만 아직 실제 모델이 없으므로 "지금 남은 모델 수" 대신 정의된
        /// ModelCount를 그대로 쓴다. 스코어보드가 배치 고스트 미리보기 강조에
        /// 쓴다(BoardManager.GetTeamPreviewSupply).</summary>
        public int PreviewSupplyCost()
        {
            if (SupplyOverride.HasValue)
            {
                return SupplyOverride.Value;
            }
            foreach (var tier in SupplyTiers)
            {
                if (ModelCount >= tier.ModelMin && ModelCount <= tier.ModelMax)
                {
                    return tier.Supply;
                }
            }
            return 0;
        }
    }
}
