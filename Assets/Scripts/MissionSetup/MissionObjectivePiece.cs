using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 미션 목표 마커 하나(1~5번). 32mm 토큰 + 그 바깥으로 3" 점령 범위 링을
    /// 그린다. Godot판 scenes/mission_setup/MissionObjectivePiece.gd 포팅.
    /// 이동은 지원하지만 회전은 의미가 없다(원형). 우클릭하면 바로 삭제된다.
    ///
    /// 미션 설정 화면과 게임 보드 둘 다에서 쓴다 — 설정 화면에서는 코너
    /// 원점 레이어에 앵커(0,0)로, 게임 보드에서는 중심 원점 baseLayer에
    /// 앵커(0.5,0.5)로 붙는다(호출부가 정한다, 이 컴포넌트는 어느 쪽이든
    /// 상관없이 pivot=(0.5,0.5)만 고정해서 anchoredPosition이 곧 그 좌표계의
    /// 중심점이 되게 한다). 게임 보드 쪽은 순수 시각 참고용이라 호출부가
    /// raycastTarget=false로 꺼서 드래그/삭제를 막는다(Godot판 mouse_filter=
    /// IGNORE와 동일한 효과).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class MissionObjectivePiece : MaskableGraphic, IPointerDownHandler
    {
        public event Action<MissionObjectivePiece> DragRequested;
        public event Action<MissionObjectivePiece> DeleteRequested;

        private int _number = 1;

        /// <summary>대입하는 순간 라벨 텍스트도 같이 갱신한다 — Awake()가
        /// 라벨을 만들기 전에 호출부가 Number를 먼저 설정할 수도 있으므로
        /// (AddComponent 직후) null 체크로 방어한다.</summary>
        public int Number
        {
            get => _number;
            set
            {
                _number = value;
                if (_numberLabel != null)
                {
                    _numberLabel.text = value.ToString();
                }
            }
        }

        public Color TokenColor = new Color(0.85f, 0.85f, 0.8f);

        /// <summary>TokenColor를 밖에서 바꾼 뒤(예: 플레이어 색상 변경) 다시
        /// 그리게 한다 — 정점 색은 OnPopulateMesh에서만 매겨지므로 필드 값을
        /// 바꾸는 것만으로는 자동으로 다시 그려지지 않는다.</summary>
        public void Refresh()
        {
            SetVerticesDirty();
        }

        private const int VisualSides = 48;

        private TextMeshProUGUI _numberLabel;

        public RectTransform RectTransform => (RectTransform)transform;

        /// <summary>anchoredPosition을 그대로 노출한다 — pivot이 항상
        /// (0.5,0.5)라서 호출부의 좌표계(코너 원점이든 중심 원점이든)에서
        /// "중심점" 그 자체가 된다.</summary>
        public Vector2 Center
        {
            get => RectTransform.anchoredPosition;
            set => RectTransform.anchoredPosition = value;
        }

        protected override void Awake()
        {
            base.Awake();
            RectTransform.pivot = new Vector2(0.5f, 0.5f);
            color = Color.white; // 실제 색은 정점 색(TokenColor 등)이 담당한다.

            var labelGo = new GameObject("Number", typeof(RectTransform));
            labelGo.transform.SetParent(transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            _numberLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _numberLabel.alignment = TextAlignmentOptions.Center;
            _numberLabel.fontSize = 18f;
            _numberLabel.color = Color.white;
            _numberLabel.raycastTarget = false;
            _numberLabel.text = _number.ToString(); // 보통은 호출부가 Awake 직후 Number를 다시 설정하며 갱신되지만, 기본값도 맞춰둔다.
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            float tokenRadius = GameConstants.MissionObjectiveTokenDiameterMm / 2f;
            float captureRadius = tokenRadius + GameConstants.MissionObjectiveCaptureMarginInch * GameConstants.MmPerInch;

            AddFilledCircle(vh, captureRadius, new Color(1f, 1f, 1f, 0.10f));
            AddCircleOutline(vh, captureRadius, new Color(1f, 1f, 1f, 0.6f), 1.5f);

            AddFilledCircle(vh, tokenRadius, TokenColor);
            AddCircleOutline(vh, tokenRadius, new Color(0.1f, 0.1f, 0.1f, 0.8f), 1.5f);
        }

        private static void AddFilledCircle(VertexHelper vh, float radius, Color color)
        {
            int centerIndex = vh.currentVertCount;
            vh.AddVert(new UIVertex { color = color, position = Vector3.zero });
            for (int i = 0; i < VisualSides; i++)
            {
                float angle = i * Mathf.PI * 2f / VisualSides;
                vh.AddVert(new UIVertex { color = color, position = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f) });
            }
            for (int i = 0; i < VisualSides; i++)
            {
                int next = (i + 1) % VisualSides;
                vh.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + next);
            }
        }

        private static void AddCircleOutline(VertexHelper vh, float radius, Color color, float thickness)
        {
            float half = thickness / 2f;
            var pts = new Vector3[VisualSides];
            for (int i = 0; i < VisualSides; i++)
            {
                float angle = i * Mathf.PI * 2f / VisualSides;
                pts[i] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            }
            for (int i = 0; i < VisualSides; i++)
            {
                int next = (i + 1) % VisualSides;
                Vector3 a = pts[i];
                Vector3 b = pts[next];
                Vector3 dir = (b - a).normalized;
                Vector3 normal = new Vector3(-dir.y, dir.x, 0f) * half;

                int vi = vh.currentVertCount;
                vh.AddVert(new UIVertex { color = color, position = a - normal });
                vh.AddVert(new UIVertex { color = color, position = a + normal });
                vh.AddVert(new UIVertex { color = color, position = b - normal });
                vh.AddVert(new UIVertex { color = color, position = b + normal });
                vh.AddTriangle(vi, vi + 1, vi + 2);
                vh.AddTriangle(vi + 1, vi + 3, vi + 2);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                DragRequested?.Invoke(this);
                eventData.Use();
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                DeleteRequested?.Invoke(this);
                eventData.Use();
            }
        }
    }
}
