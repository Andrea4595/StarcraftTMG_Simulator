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

        private void UpdateHoveredUnit()
        {
            _hoveredBase = TryGetLocalMouse(out var mouseLocal) ? FindBaseAtPoint(mouseLocal) : null;
            _hoveredUnit = _hoveredBase != null ? _hoveredBase.Unit : null;

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
