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
