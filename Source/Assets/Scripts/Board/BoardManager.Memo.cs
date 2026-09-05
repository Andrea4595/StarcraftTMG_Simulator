using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 메모 호버 표시 ───────────────────────────────────────────────

        internal void UpdateHoveredUnit()
        {
            var previousHoveredUnit = _hoveredUnit;
            _hoveredBase = TryGetLocalMouse(out var mouseLocal) ? FindBaseAtPoint(mouseLocal) : null;
            _hoveredUnit = _hoveredBase != null ? _hoveredBase.Unit : null;

            // 마우스가 올라간 모델이 속한 유닛 전체를 하이라이팅한다(사용자
            // 요청 — 처음엔 그 모델 하나만 켰었다) — 유닛이 바뀐 프레임에만
            // 이전 유닛의 모든 모델을 끄고 새 유닛의 모든 모델을 켠다. 같은
            // 유닛 안에서 모델만 바뀌는 경우(예: 팔로워 사이 이동)는 그대로
            // 켜져 있어야 하므로 다시 손대지 않는다. 모델 하나하나에 대해
            // "그냥 != null"이 아니라 Unity의 오버로드된 bool 변환(if (obj))으로
            // 확인한다 — 지난 프레임 이후 다이얼 메뉴로 모델이 제거됐다면 C#
            // 참조는 남아있어도 네이티브 객체는 이미 파괴된 "가짜 null" 상태라,
            // != null만으로는 못 걸러내고 Highlighted 세터 호출 시 예외가 난다.
            if (previousHoveredUnit != _hoveredUnit)
            {
                if (previousHoveredUnit != null)
                {
                    foreach (var model in previousHoveredUnit.Models)
                    {
                        if (model)
                        {
                            model.Highlighted = false;
                        }
                    }
                }
                if (_hoveredUnit != null)
                {
                    foreach (var model in _hoveredUnit.Models)
                    {
                        if (model)
                        {
                            model.Highlighted = true;
                        }
                    }
                }
            }

            _memoEntries.Clear();
            if (_hoveredUnit != null)
            {
                foreach (var model in _hoveredUnit.Models)
                {
                    if (model == null || string.IsNullOrEmpty(model.Memo))
                    {
                        continue;
                    }
                    // Godot판은 Y가 아래로 증가해 +값이 "아래"였다 — Unity는 Y가
                    // 위로 증가하므로 부호를 뒤집어야 같은 위치(모델 아래)에 뜬다.
                    _memoEntries.Add((model.Memo, model.Center + new Vector2(0f, -(model.BoundingRadius + 10f))));
                }
            }
            if (memoOverlay != null)
            {
                memoOverlay.SetEntries(_memoEntries);
            }

            RefreshRangeOverlays();
            UpdateCombatRowHighlight();
        }

        /// <summary>호버 중인 유닛의 전열/지원열과, 그 유닛과 실제로 인게이지된
        /// (모델 간 1인치 이내인) 적 유닛들의 전열/지원열을 매 프레임 다시
        /// 계산해서 표시한다(사용자 요청 — 룰북 8.8절). 호버 중이 아니면
        /// 아무 것도 안 보인다. 매 프레임 전부 지우고 다시 계산하는 이유는
        /// RefreshRangeOverlays()와 같다 — 호버 중에도 다른 모델이 계속
        /// 움직일 수 있어서(다른 유닛 드래그 등) 매번 다시 봐야 정확하다.</summary>
        private void UpdateCombatRowHighlight()
        {
            foreach (var model in _combatRowHighlighted)
            {
                if (model)
                {
                    model.CombatRowState = Base.CombatRow.None;
                }
            }
            _combatRowHighlighted.Clear();

            if (_hoveredUnit == null)
            {
                return;
            }

            var allUnits = new HashSet<Unit>();
            foreach (var piece in _pieces)
            {
                if (piece != null && piece.Unit != null)
                {
                    allUnits.Add(piece.Unit);
                }
            }

            var relevantUnits = new List<Unit> { _hoveredUnit };
            foreach (var unit in allUnits)
            {
                if (unit == _hoveredUnit || unit.Team == _hoveredUnit.Team || unit.IsToken)
                {
                    continue;
                }
                if (IsUnitEngagedWithUnit(unit, _hoveredUnit))
                {
                    relevantUnits.Add(unit);
                }
            }

            foreach (var unit in relevantUnits)
            {
                HighlightCombatRowForUnit(unit, allUnits);
            }
        }

        private static bool IsUnitEngagedWithUnit(Unit a, Unit b)
        {
            foreach (var modelA in a.Models)
            {
                if (modelA == null)
                {
                    continue;
                }
                foreach (var modelB in b.Models)
                {
                    if (modelB == null)
                    {
                        continue;
                    }
                    float dist = EllipseMath.EllipseToEllipseDistance(
                            modelA.Center, modelA.SizeMm, modelA.RotationRadians,
                            modelB.Center, modelB.SizeMm, modelB.RotationRadians);
                    if (dist <= EngageDistanceMm)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>unit의 모델들을 전열(적 인게이지 거리 이내)/지원열(같은
        /// 유닛의 전열 모델과 베이스 접촉)로 분류해 CombatRowState를 세팅한다.
        /// "적"은 unit과 팀이 다른, 보드 위 모든 유닛(호버 중인 유닛과의
        /// 인게이지 여부와 무관 — 전열 판정 자체는 룰북 정의대로 "아무
        /// 적이든" 기준이다) — 이 함수를 부르는 쪽(UpdateCombatRowHighlight)
        /// 이 "어떤 유닛들을 계산 대상으로 삼을지"만 미리 걸러준다.</summary>
        private void HighlightCombatRowForUnit(Unit unit, HashSet<Unit> allUnits)
        {
            var frontRow = new List<Base>();
            foreach (var model in unit.Models)
            {
                if (model == null)
                {
                    continue;
                }
                bool isFront = false;
                foreach (var otherUnit in allUnits)
                {
                    if (otherUnit == unit || otherUnit.Team == unit.Team || otherUnit.IsToken)
                    {
                        continue;
                    }
                    foreach (var enemyModel in otherUnit.Models)
                    {
                        if (enemyModel == null)
                        {
                            continue;
                        }
                        float dist = EllipseMath.EllipseToEllipseDistance(
                                model.Center, model.SizeMm, model.RotationRadians,
                                enemyModel.Center, enemyModel.SizeMm, enemyModel.RotationRadians);
                        if (dist <= EngageDistanceMm)
                        {
                            isFront = true;
                            break;
                        }
                    }
                    if (isFront)
                    {
                        break;
                    }
                }
                if (isFront)
                {
                    frontRow.Add(model);
                    model.CombatRowState = Base.CombatRow.Front;
                    _combatRowHighlighted.Add(model);
                }
            }

            foreach (var model in unit.Models)
            {
                if (model == null || frontRow.Contains(model))
                {
                    continue;
                }
                foreach (var frontModel in frontRow)
                {
                    float dist = EllipseMath.EllipseToEllipseDistance(
                            model.Center, model.SizeMm, model.RotationRadians,
                            frontModel.Center, frontModel.SizeMm, frontModel.RotationRadians);
                    if (dist <= BaseContactThresholdMm)
                    {
                        model.CombatRowState = Base.CombatRow.Support;
                        _combatRowHighlighted.Add(model);
                        break;
                    }
                }
            }
        }

        /// <summary>마우스 아래(맨 위에 그려진 것부터)의 베이스를 찾는다 —
        /// 회전된 타원 그대로 판정한다. Godot판 _find_base_at_point 포팅.</summary>
        private Base FindBaseAtPoint(Vector2 point)
        {
            for (int i = baseLayer.childCount - 1; i >= 0; i--)
            {
                var piece = baseLayer.GetChild(i).GetComponent<Base>();
                if (piece == null)
                {
                    continue;
                }
                float rot = -piece.RotationRadians;
                Vector2 offset = point - piece.Center;
                float cos = Mathf.Cos(rot);
                float sin = Mathf.Sin(rot);
                Vector2 local = new Vector2(offset.x * cos - offset.y * sin, offset.x * sin + offset.y * cos);
                float rx = piece.SizeMm.x / 2f;
                float ry = piece.SizeMm.y / 2f;
                if (rx < 0.0001f || ry < 0.0001f)
                {
                    continue;
                }
                if ((local.x * local.x) / (rx * rx) + (local.y * local.y) / (ry * ry) <= 1f)
                {
                    return piece;
                }
            }
            return null;
        }

    }
}
