using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 범위 표시 ────────────────────────────────────────────────────

        private void OnRangeConfirmed(float value, bool alwaysShow)
        {
            // alwaysShow는 이 범위 항목이 만들어질 때 한 번 정해지면 끝 — 나중에
            // 값을 바꾸는 기능은 의도적으로 제공하지 않는다(Godot판과 동일).
            // 마음에 안 들면 지우고("범위 표시 → 제거") 새로 추가하면 된다.
            if (_rangeTargetUnit == null)
            {
                return;
            }
            BeginUndoTransaction();
            if (!_unitRanges.TryGetValue(_rangeTargetUnit, out var ranges))
            {
                ranges = new List<RangeSpec>();
                _unitRanges[_rangeTargetUnit] = ranges;
            }
            ranges.Add(new RangeSpec { Inch = value, AlwaysShow = alwaysShow });
            _rangeTargetUnit = null;
            RefreshRangeOverlays();
            CommitUndoTransaction();
        }

        private void HandleDeleteRangeRequest(Base piece)
        {
            if (piece == null || piece.Unit == null)
            {
                return;
            }
            var unit = piece.Unit;
            if (!_unitRanges.TryGetValue(unit, out var ranges) || ranges.Count == 0)
            {
                return;
            }

            if (ranges.Count == 1)
            {
                BeginUndoTransaction();
                _unitRanges.Remove(unit);
                RefreshRangeOverlays();
                CommitUndoTransaction();
                return;
            }

            _rangeDeleteTargetUnit = unit;
            var options = new List<RadialMenuOption>();
            for (int i = 0; i < ranges.Count; i++)
            {
                options.Add(new RadialMenuOption($"{ranges[i].Inch:F1}\" 범위 삭제", $"delete_range_{i}"));
            }
            radialMenu.Open(options, _menuScreenPos);
        }

        private void DeleteRangeAtIndex(Unit unit, int idx)
        {
            if (unit == null || !_unitRanges.TryGetValue(unit, out var ranges))
            {
                return;
            }
            if (idx < 0 || idx >= ranges.Count)
            {
                return;
            }
            BeginUndoTransaction();
            ranges.RemoveAt(idx);
            if (ranges.Count == 0)
            {
                _unitRanges.Remove(unit);
            }
            _rangeDeleteTargetUnit = null;
            RefreshRangeOverlays();
            CommitUndoTransaction();
        }

        /// <summary>등록된 사거리마다(상시 표시거나 지금 마우스가 올라간 유닛의
        /// 것이면) 그 유닛 모델들의 오프셋 다각형을 모아 Outline/Fill 레이어에
        /// 넘긴다. 마우스가 올라간 특정 모델 하나는 별도로 강조 채우기도
        /// 더한다. Godot판 _refresh_range_overlays() 포팅 — BoardManager의
        /// Update()가 매 프레임 UpdateHoveredUnit()을 부르므로, 움직이는
        /// 모델을 따라 범위 표시도 실시간으로 갱신된다.</summary>
        private void RefreshRangeOverlays()
        {
            if (rangeOutlineLayer == null || rangeFillLayer == null)
            {
                return;
            }

            var entries = new List<RangeOverlayEntry>();
            var highlightPolygons = new List<Vector2[]>();

            foreach (var kv in _unitRanges)
            {
                var unit = kv.Key;
                bool isHoveredUnit = unit == _hoveredUnit;
                foreach (var spec in kv.Value)
                {
                    if (!spec.AlwaysShow && !isHoveredUnit)
                    {
                        continue;
                    }

                    float offsetMm = spec.Inch * GameConstants.MmPerInch;
                    var entry = new RangeOverlayEntry
                    {
                        LabelText = $"{spec.Inch:F1}\"",
                        Hovered = isHoveredUnit,
                    };
                    Vector2 topPoint = Vector2.zero;
                    bool hasTopPoint = false;
                    foreach (var model in unit.Models)
                    {
                        if (model == null)
                        {
                            continue;
                        }
                        var polygon = EllipseMath.EllipseOffsetPolygonAt(model.Center, model.SizeMm, model.RotationRadians, offsetMm);
                        entry.Polygons.Add(polygon);
                        foreach (var p in polygon)
                        {
                            // 라벨은 다각형 뭉치의 맨 위(화면상 가장 위, Unity는 Y가
                            // 위로 증가하므로 y가 가장 큰 점)에 둔다.
                            if (!hasTopPoint || p.y > topPoint.y)
                            {
                                topPoint = p;
                                hasTopPoint = true;
                            }
                        }
                    }
                    if (entry.Polygons.Count == 0)
                    {
                        continue;
                    }
                    entry.LabelPos = topPoint + new Vector2(0f, 8f);
                    entries.Add(entry);

                    // 마우스가 이 유닛의 특정 모델 하나 위에 있으면, 그 모델 하나만을
                    // 중심으로 한 범위를 별도로 더 진한 채우기로 겹쳐 그린다.
                    if (isHoveredUnit && _hoveredBase != null && _hoveredBase.Unit == unit)
                    {
                        highlightPolygons.Add(EllipseMath.EllipseOffsetPolygonAt(
                                _hoveredBase.Center, _hoveredBase.SizeMm, _hoveredBase.RotationRadians, offsetMm));
                    }
                }
            }

            // rangeFillLayer는 이제 상시 배경 채우기가 없고 호버 강조 채우기만
            // 그리므로(RangeOverlay 참고), entries가 아니라 highlightPolygons만
            // 필요하다.
            rangeOutlineLayer.SetEntries(entries);
            rangeFillLayer.SetHighlightPolygons(highlightPolygons);
        }

    }
}
