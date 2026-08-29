using System.Collections.Generic;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 서플라이 집계 ────────────────────────────────────────────────
        // 스코어보드(ScoreboardPanel)가 매 프레임 물어보는 용도. 토큰은
        // squad_tiers가 없는 항목이라 CurrentSupplyCost()가 항상 0이지만,
        // 그래도 IsToken은 명시적으로 걸러서 의도를 분명히 한다. 같은 유닛이
        // 모델을 여러 개 갖고 있어도(_pieces에 여러 번 나타나도) 유닛당 한
        // 번만 센다 — Unit.CurrentSupplyCost()가 이미 그 유닛의 "지금 남은
        // 모델 수" 기준 단계 값을 돌려주므로, 모델 단위로 또 더하면 중복이다.

        /// <summary>지금 보드 위에 실제로 배치된(모델이 1개 이상 남아있는)
        /// 해당 팀 유닛들의 서플라이 소비량 합. 각 유닛은 룰북 6.1대로 "지금
        /// 남은 모델 수"에 해당하는 단계 값으로 계산되므로, 사상자로 모델이
        /// 줄면 다음 프레임부터 자동으로 반영된다(별도 갱신 호출 필요 없음).
        /// 예비대(아직 안 놓인 유닛)는 포함하지 않는다.</summary>
        public int GetTeamSupplyUsed(string team)
        {
            var counted = new HashSet<Unit>();
            int total = 0;
            foreach (var piece in _pieces)
            {
                var unit = piece.Unit;
                if (unit == null || unit.IsToken || unit.Team != team)
                {
                    continue;
                }
                if (counted.Add(unit))
                {
                    total += unit.CurrentSupplyCost();
                }
            }
            return total;
        }

        /// <summary>지금 team이 배치하려고 띄워둔 고스트(_pendingDeploymentDef)가
        /// 있으면 그 유닛을 배치할 때 소비될 서플라이, 없으면(또는 다른 팀이
        /// 배치 중이면) 0. 한 번에 한 팀만 배치 드래그를 할 수 있으므로
        /// _pendingDeploymentDef는 전역에 하나뿐이다. 스코어보드의 서플라이
        /// 네모 미리보기 강조(ScoreboardPanel.RefreshSupplyRow)가 매 프레임
        /// 묻는다.</summary>
        public int GetTeamPreviewSupply(string team)
        {
            return _pendingDeploymentDef != null && _pendingDeploymentDef.Team == team
                ? _pendingDeploymentDef.PreviewSupplyCost()
                : 0;
        }
    }
}
