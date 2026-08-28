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
    }
}
