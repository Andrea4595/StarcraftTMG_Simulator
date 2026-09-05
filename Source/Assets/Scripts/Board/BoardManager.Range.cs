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
            var unit = _rangeTargetUnit;
            _rangeTargetUnit = null;
            RequestAddRange(unit, value, alwaysShow);
        }

        /// <summary>_unitRanges는 Unit 자체가 아니라 BoardManager 쪽 보조
        /// 상태라(BuildUnitTree — 데미지/복제/제거/메모가 재사용하는 그
        /// 전체-유닛 트리 — 에 안 실린다) 별도로 유닛의 NetworkUnitId로
        /// 대상을 지목하는 전용 방송이 필요하다. 멀티 연결 중이면 방송
        /// 요청만 하고, 실제 추가는 그 방송이 되돌아오는 걸 거쳐서(마커
        /// 배치와 같은 패턴) 일어난다. 되돌리기는 여기서 먼저 커밋해둔다 —
        /// CommitUndoTransaction이 상대에게도 같은 항목을 방송하므로
        /// (BoardManager.UndoRedo.cs), 실제 반영이 살짝 전에 스택에 먼저
        /// 올라가는 셈이지만 그 시차는 무시할 수준이다.</summary>
        private void RequestAddRange(Unit unit, float inch, bool alwaysShow)
        {
            PerformNetworkedMutation(this, $"{DescribeUnit(unit)} 범위 추가 ({inch:F1}\")", "범위 추가",
                    () => BoardNetworkSync.Instance.RequestAddRangeServerRpc(unit.NetworkUnitId, inch, alwaysShow),
                    () => AddRangeToUnit(unit, inch, alwaysShow),
                    unit?.Team);
        }

        private void AddRangeToUnit(Unit unit, float inch, bool alwaysShow)
        {
            if (!_unitRanges.TryGetValue(unit, out var ranges))
            {
                ranges = new List<RangeSpec>();
                _unitRanges[unit] = ranges;
            }
            ranges.Add(new RangeSpec { Inch = inch, AlwaysShow = alwaysShow });
            RefreshRangeOverlays();
        }

        /// <summary>BoardNetworkSync.AddRangeRpc가 방송을 받았을 때(요청한
        /// 쪽 자신도 포함) 호출한다.</summary>
        internal void ApplyAddRangeById(int networkUnitId, float inch, bool alwaysShow)
        {
            if (!_networkedUnits.TryGet(networkUnitId, out var unit))
            {
                Debug.LogError($"[BoardManager] 범위를 추가할 유닛을 못 찾음(id={networkUnitId})");
                return;
            }
            AddRangeToUnit(unit, inch, alwaysShow);
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
                RequestDeleteRange(unit, 0);
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

        /// <summary>OnRangeConfirmed→RequestAddRange와 같은 이유로 방송
        /// 요청/로컬 반영을 분기한다.</summary>
        private void RequestDeleteRange(Unit unit, int idx)
        {
            _rangeDeleteTargetUnit = null;
            PerformNetworkedMutation(this, $"{DescribeUnit(unit)} 범위 삭제", "범위 삭제",
                    () => BoardNetworkSync.Instance.RequestDeleteRangeServerRpc(unit.NetworkUnitId, idx),
                    () => RemoveRangeAtIndex(unit, idx),
                    unit?.Team);
        }

        private void RemoveRangeAtIndex(Unit unit, int idx)
        {
            if (unit == null || !_unitRanges.TryGetValue(unit, out var ranges))
            {
                return;
            }
            if (idx < 0 || idx >= ranges.Count)
            {
                return;
            }
            ranges.RemoveAt(idx);
            if (ranges.Count == 0)
            {
                _unitRanges.Remove(unit);
            }
            RefreshRangeOverlays();
        }

        /// <summary>BoardNetworkSync.DeleteRangeRpc가 방송을 받았을 때(요청한
        /// 쪽 자신도 포함) 호출한다.</summary>
        internal void ApplyDeleteRangeById(int networkUnitId, int idx)
        {
            if (!_networkedUnits.TryGet(networkUnitId, out var unit))
            {
                Debug.LogError($"[BoardManager] 범위를 지울 유닛을 못 찾음(id={networkUnitId})");
                return;
            }
            RemoveRangeAtIndex(unit, idx);
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
