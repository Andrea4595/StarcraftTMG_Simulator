using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    public partial class BoardManager
    {
        // ── 거리 재기(스페이스바) ───────────────────────────────────────

        /// <summary>스페이스바를 누르고 있는 동안 시작점부터 지금 마우스
        /// 위치(또는 그 아래 베이스)까지 거리를 잰다. Godot판 GameBoard.gd의
        /// 스페이스바 처리+_start_measuring/_stop_measuring/_update_measure_line
        /// 포팅. 이름/데미지/범위/메모 입력창에 포커스가 있을 때는 무시한다
        /// (그 필드에 스페이스 문자를 입력하는 것으로 취급).</summary>
        internal void HandleMeasureInput()
        {
            if (measureLayer == null || IsTextFieldFocused())
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                StartMeasuring();
            }
            else if (Input.GetKeyUp(KeyCode.Space))
            {
                StopMeasuring();
            }

            if (_measuring)
            {
                UpdateMeasureLine();
            }
        }

        private static bool IsTextFieldFocused()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() != null;
        }

        private void StartMeasuring()
        {
            if (_measuring)
            {
                return;
            }
            _measuring = true;
            if (TryGetLocalMouse(out var mouseLocal))
            {
                _measureFromBase = FindBaseAtPoint(mouseLocal);
                _measureFromPoint = mouseLocal;
            }
        }

        private void StopMeasuring()
        {
            _measuring = false;
            _measureFromBase = null;
            measureLayer.Hide();
        }

        private void UpdateMeasureLine()
        {
            if (!TryGetLocalMouse(out var mouseLocal))
            {
                return;
            }

            // 재는 도중에도 마우스가 다른 유닛 위로 올라가면 그 유닛의 베이스까지
            // 가장 가까운 거리를 재도록, 매 프레임 다시 찾는다(시작 쪽 베이스는
            // 스페이스바를 누른 순간에 고정).
            var toBase = FindBaseAtPoint(mouseLocal);
            if (toBase == _measureFromBase)
            {
                toBase = null;
            }

            Vector2 fromPos = _measureFromBase != null ? _measureFromBase.Center : _measureFromPoint;
            Vector2 toPos = toBase != null ? toBase.Center : mouseLocal;

            // 양쪽(또는 한쪽)이 타원이면, 서로에게 가장 가까운 점을 번갈아 다시
            // 계산하는 것을 몇 차례 반복해 수렴시킨다(충돌 해소와 같은 방식).
            for (int i = 0; i < 6; i++)
            {
                if (_measureFromBase != null)
                {
                    fromPos = EllipseMath.ClosestPointOnEllipseWorld(_measureFromBase.Center, _measureFromBase.SizeMm, _measureFromBase.RotationRadians, toPos);
                }
                if (toBase != null)
                {
                    toPos = EllipseMath.ClosestPointOnEllipseWorld(toBase.Center, toBase.SizeMm, toBase.RotationRadians, fromPos);
                }
            }

            float distMm = Vector2.Distance(fromPos, toPos);
            measureLayer.SetLine(fromPos, toPos, $"{distMm / GameConstants.MmPerInch:F1}\"");
        }

    }
}
