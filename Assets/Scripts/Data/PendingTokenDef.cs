using System.Collections.Generic;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 로스터 JSON의 "tokens" 배열에서 온 토큰 정의. 유닛 정의(PendingUnitDef)와
    /// 달리 예비대 목록에서 지워지지 않는다 — 같은 토큰을 몇 번이든 다시 배치할
    /// 수 있어야 하므로. 같은 팀+이름의 토큰은 배치될 때마다 이미 만들어진
    /// 같은 Unit에 모델만 늘어난다(BoardManager._rosterTokenUnits). Godot판
    /// GameBoard.gd의 _pending_roster_tokens 딕셔너리 포팅.
    /// </summary>
    public class PendingTokenDef
    {
        public string Name = "";
        public string Team = "neutral";
        public Vector2 SizeMm = new Vector2(32f, 32f);
        public bool IsDisplacement;
        public List<RangeSpec> Ranges = new List<RangeSpec>();
    }
}
