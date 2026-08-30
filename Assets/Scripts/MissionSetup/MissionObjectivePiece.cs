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
    /// 이동은 지원하지만 회전은 의미가 없다(원형).
    ///
    /// 미션 설정 화면과 게임 보드 둘 다에서 쓴다 — 설정 화면에서는 코너
    /// 원점 레이어에 앵커(0,0)로, 게임 보드에서는 중심 원점 baseLayer에
    /// 앵커(0.5,0.5)로 붙는다(호출부가 정한다, 이 컴포넌트는 어느 쪽이든
    /// 상관없이 pivot=(0.5,0.5)만 고정해서 anchoredPosition이 곧 그 좌표계의
    /// 중심점이 되게 한다). 두 화면은 클릭 동작이 다르다(AllowDrag/
    /// RightClickCyclesColor 참고) — 설정 화면: 좌클릭 드래그, 우클릭 즉시
    /// 삭제. 게임 보드: 순수 참고용이라 드래그는 막고, 우클릭은 삭제 대신
    /// 점령 링 색상 순환(RingColorState)만 한다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class MissionObjectivePiece : MaskableGraphic, IPointerDownHandler
    {
        public event Action<MissionObjectivePiece> DragRequested;
        public event Action<MissionObjectivePiece> DeleteRequested;

        /// <summary>게임 보드(RightClickCyclesColor=true)에서 우클릭했을 때
        /// 올라간다 — 미션 설정 화면과 달리 이 컴포넌트가 직접 색을 바꾸지
        /// 않고 호출부(BoardManager.MissionObjectives.cs)에 위임한다. 멀티
        /// 연결 중이면 방송을 거쳐야 상대 화면도 같이 바뀌기 때문(2026-08-31
        /// 추가) — 그 전에는 OnPointerDown이 CycleRingColor()를 직접 불러서
        /// 로컬에서만 바뀌고 끝이었다.</summary>
        public event Action<MissionObjectivePiece> ColorCycleRequested;

        /// <summary>미션 설정 화면(기본값)에서는 좌클릭 드래그 + 우클릭 즉시
        /// 삭제. 게임 보드에서는 순수 참고용이라 드래그를 막고(false) 우클릭은
        /// 삭제 대신 점령 링 색상 순환으로 대체한다(RightClickCyclesColor).
        /// 호출부(BoardManager.MissionObjectives.cs)가 게임 보드 인스턴스에만
        /// 이 두 값을 뒤집는다.</summary>
        public bool AllowDrag = true;
        public bool RightClickCyclesColor;

        /// <summary>점령 범위 링의 색/상태 — CaptureMarker.ColorState와 같은
        /// 개념이지만 별도 필드다: TokenColor는 이미 번호 토큰 자체의 색(팀
        /// 배정)을 뜻하므로 링 색과 혼동하면 안 된다. 우클릭할 때마다 비활성 →
        /// 활성(흰색) → 플레이어 A(빨강) → 플레이어 B(파랑) 순으로 계속
        /// 순환한다(삭제 없이 영원히 반복, 사용자 지정 순서). 기본값은
        /// "inactive" — 마커를 새로 놓으면 항상 비활성 상태로 시작한다.</summary>
        public static readonly string[] RingColorSequence = { "inactive", "white", "red", "blue" };

        public string RingColorState { get; private set; } = "inactive";

        public void CycleRingColor()
        {
            int idx = Array.IndexOf(RingColorSequence, RingColorState);
            RingColorState = RingColorSequence[(idx + 1) % RingColorSequence.Length];
            SetVerticesDirty();
        }

        /// <summary>저장된 게임을 불러올 때 링 상태를 순환이 아니라 곧바로
        /// 특정 값으로 되돌리는 용도(GameSaveIO) — 알 수 없는 값이면 조용히
        /// 무시한다(구버전 세이브 파일 호환).</summary>
        public void SetRingColorState(string state)
        {
            if (Array.IndexOf(RingColorSequence, state) < 0)
            {
                return;
            }
            RingColorState = state;
            SetVerticesDirty();
        }

        /// <summary>CaptureMarker.ResolveColor와 같은 이유로 "red"/"blue"는
        /// 고정 색이 아니라 A/B팀의 현재 색을 그대로 따라간다. "inactive"는
        /// 어느 팀과도 무관한 고정 회색(사용자 지정).</summary>
        private static Color ResolveRingColor(string state)
        {
            switch (state)
            {
                case "inactive":
                    return new Color(0.2f, 0.2f, 0.2f); // "어두운 회색" 느낌이 나도록 더 짙게(사용자 지정).
                case "red":
                    return GameConstants.TeamColors.TryGetValue("A", out var a) ? a : new Color(0.9f, 0.15f, 0.15f);
                case "blue":
                    return GameConstants.TeamColors.TryGetValue("B", out var b) ? b : new Color(0.15f, 0.4f, 0.9f);
                default:
                    return Color.white;
            }
        }

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

            // 비활성 상태는 내부를 짙은 회색(알파도 높여 "어두운 회색"
            // 느낌이 확실히 나도록)으로, 테두리는 점선으로 그려서 "아직
            // 아무 팀도 아니다"가 한눈에 구분되게 한다. 활성(흰색)/A/B는
            // 기존처럼 실선이지만, A/B로 팀이 배정된 상태는 배경 채움을
            // 더 투명하게(기존 0.10 → 0.05) 낮췄고, 테두리도 팀 원색
            // 그대로가 아니라 명도/채도를 살짝 낮춘 톤(Muted — 로스터
            // 토큰/미션 목표 토큰 색과 같은 방식)을 쓴다(둘 다 사용자 지정).
            // 흰색(활성, 아직 팀 없음) 쪽은 그대로 둔다.
            bool isInactive = RingColorState == "inactive";
            bool isTeamAssigned = RingColorState == "red" || RingColorState == "blue";
            var ringColor = ResolveRingColor(RingColorState);
            var outlineColor = isTeamAssigned ? GameConstants.Muted(ringColor, 0.55f, 0.75f) : ringColor;
            float fillAlpha = isInactive ? 0.55f : (RingColorState == "white" ? 0.10f : 0.05f);
            float outlineAlpha = isInactive ? 0.9f : 0.6f;
            AddFilledCircle(vh, captureRadius, new Color(ringColor.r, ringColor.g, ringColor.b, fillAlpha));
            AddCircleOutline(vh, captureRadius, new Color(outlineColor.r, outlineColor.g, outlineColor.b, outlineAlpha), 1.5f, dashed: isInactive);

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

        // 점선 한 칸(온) + 한 칸(오프)의 세그먼트 개수 — VisualSides(48)가
        // DashPeriod(4)의 배수라 원 한 바퀴에 딱 맞아떨어져(12쌍) 이음매가
        // 어긋나지 않는다.
        private const int DashOnSegments = 2;
        private const int DashPeriod = 4;

        private static void AddCircleOutline(VertexHelper vh, float radius, Color color, float thickness, bool dashed = false)
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
                if (dashed && (i % DashPeriod) >= DashOnSegments)
                {
                    continue; // 점선의 "오프" 구간 — 이 변은 그리지 않고 건너뛴다.
                }
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
                if (AllowDrag)
                {
                    DragRequested?.Invoke(this);
                }
                eventData.Use();
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                if (RightClickCyclesColor)
                {
                    ColorCycleRequested?.Invoke(this);
                }
                else
                {
                    DeleteRequested?.Invoke(this);
                }
                eventData.Use();
            }
        }
    }
}
