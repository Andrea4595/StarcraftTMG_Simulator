using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 게임 화면의 베이스(모델) 하나. 원형 또는 타원형이며, 이름/팀 색을 갖는다.
    /// Godot판 Base.gd 포팅 — 실제 드래그 추적/충돌 해소/다이얼 메뉴는 보드
    /// 매니저가 전역으로 처리하고, 이 컴포넌트는 클릭 이벤트만 올린다.
    ///
    /// RectTransform pivot=(0.5,0.5)로 둬서 anchoredPosition이 곧 중심(mm)이
    /// 되게 한다 — Godot판의 center()=position+size/2 계산을 따로 안 해도 된다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(CanvasRenderer))]
    public class Base : MaskableGraphic, IPointerDownHandler, IEllipseBody
    {
        public event Action<Base> DragRequested;
        public event Action<Base, Vector2> MenuRequested; // 화면 좌표

        private const int VisualSides = 48;
        private const float DamageBadgeRadiusMm = 8f;
        private static readonly Color OutlineColor = new Color(0.05f, 0.05f, 0.05f, 0.9f);
        private static readonly Color DisplacementOutlineColor = new Color(0.2f, 0.9f, 0.9f, 0.95f);

        public Unit Unit;
        public string Memo = "";

        private const float CoherencyFlashSpeed = 6f; // 라디안/초

        [SerializeField] private Vector2 sizeMm = new Vector2(32f, 32f);
        [SerializeField] private Color fillColor = new Color(0.6f, 0.6f, 0.6f, 0.85f);
        [SerializeField] private int damage;
        [SerializeField] private bool isDisplacement;
        private bool _coherencyWarning;

        private RectTransform _rectTransform;
        private TextMeshProUGUI _nameLabel;
        private GameObject _damageBadgeGo;
        private TextMeshProUGUI _damageBadgeLabel;
        private Image _damageBadgeImage;

        public Vector2 SizeMm
        {
            get => sizeMm;
            set
            {
                sizeMm = value;
                RectTransform.sizeDelta = value;
            }
        }

        public Color FillColor
        {
            get => fillColor;
            set { fillColor = value; color = value; }
        }

        public int Damage
        {
            get => damage;
            set => damage = value;
        }

        public bool IsDisplacement
        {
            get => isDisplacement;
            set => isDisplacement = value;
        }

        /// <summary>true면 하얗게 반짝이며 코헤런시 이탈을 알린다(유닛 이동 중
        /// BoardManager.UpdateUnitMoveWarning()이 매 프레임 갱신).</summary>
        public bool CoherencyWarning
        {
            get => _coherencyWarning;
            set
            {
                if (_coherencyWarning == value)
                {
                    return;
                }
                _coherencyWarning = value;
                SetVerticesDirty();
            }
        }

        public RectTransform RectTransform => _rectTransform != null ? _rectTransform : (_rectTransform = (RectTransform)transform);

        /// <summary>월드(캔버스 로컬 mm) 기준 중심.</summary>
        public Vector2 Center
        {
            get => RectTransform.anchoredPosition;
            set => RectTransform.anchoredPosition = value;
        }

        public float RotationRadians => RectTransform.localEulerAngles.z * Mathf.Deg2Rad;

        public float RotationDegrees
        {
            get => RectTransform.localEulerAngles.z;
            set => RectTransform.localEulerAngles = new Vector3(0f, 0f, value);
        }

        public float BoundingRadius => EllipseMath.BoundingRadius(sizeMm);

        private const float RotateStepDeg = 22.5f;

        /// <summary>드래그로 옮기는 동안 휠을 굴리면 호출된다 — MissionSetup의
        /// TerrainPiece.RotateStep과 같은 22.5° 단위(원형 베이스는 시각적으로
        /// 차이가 없지만, 타원형 베이스는 방향이 실제 판정(코헤런시/이동거리/
        /// 배치 밴드 스냅)에 영향을 준다).</summary>
        public void RotateStep(int direction = 1)
        {
            RotationDegrees = Mathf.Repeat(RotationDegrees + RotateStepDeg * direction, 360f);
        }

        protected override void Awake()
        {
            base.Awake();
            RectTransform.pivot = new Vector2(0.5f, 0.5f);
            color = fillColor;
            BuildChildren();
        }

        private void BuildChildren()
        {
            var labelGo = new GameObject("NameLabel", typeof(RectTransform));
            labelGo.transform.SetParent(transform, false);
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            _nameLabel = labelGo.AddComponent<TextMeshProUGUI>();
            _nameLabel.alignment = TextAlignmentOptions.Center;
            _nameLabel.fontSize = 12f;
            _nameLabel.color = Color.black;
            _nameLabel.enableWordWrapping = false;
            _nameLabel.raycastTarget = false;

            _damageBadgeGo = new GameObject("DamageBadge", typeof(RectTransform));
            _damageBadgeGo.transform.SetParent(transform, false);
            var badgeRect = (RectTransform)_damageBadgeGo.transform;
            badgeRect.anchorMin = new Vector2(1f, 1f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.pivot = new Vector2(0.5f, 0.5f);
            float badgeSize = DamageBadgeRadiusMm * 2f;
            badgeRect.sizeDelta = new Vector2(badgeSize, badgeSize);
            badgeRect.anchoredPosition = new Vector2(-DamageBadgeRadiusMm, -DamageBadgeRadiusMm);
            _damageBadgeImage = _damageBadgeGo.AddComponent<Image>();
            _damageBadgeImage.color = new Color(0.8f, 0.1f, 0.1f);
            _damageBadgeImage.raycastTarget = false;

            var badgeLabelGo = new GameObject("Label", typeof(RectTransform));
            badgeLabelGo.transform.SetParent(_damageBadgeGo.transform, false);
            var badgeLabelRect = (RectTransform)badgeLabelGo.transform;
            badgeLabelRect.anchorMin = Vector2.zero;
            badgeLabelRect.anchorMax = Vector2.one;
            badgeLabelRect.offsetMin = Vector2.zero;
            badgeLabelRect.offsetMax = Vector2.zero;
            _damageBadgeLabel = badgeLabelGo.AddComponent<TextMeshProUGUI>();
            _damageBadgeLabel.alignment = TextAlignmentOptions.Center;
            _damageBadgeLabel.fontSize = 10f;
            _damageBadgeLabel.color = Color.white;
            _damageBadgeLabel.raycastTarget = false;

            Refresh();
        }

        /// <summary>Godot판의 queue_redraw() 대응 — 크기/데미지/이름 등을 바꾼 뒤
        /// 명시적으로 불러서 메시와 라벨을 갱신한다.</summary>
        public void Refresh()
        {
            RectTransform.sizeDelta = sizeMm;
            color = fillColor;
            SetVerticesDirty();

            if (_nameLabel != null)
            {
                _nameLabel.text = Unit != null ? Unit.UnitName : "";
            }

            if (_damageBadgeGo != null)
            {
                bool show = damage > 0;
                _damageBadgeGo.SetActive(show);
                if (show)
                {
                    _damageBadgeLabel.text = damage.ToString();
                }
            }
        }

        /// <summary>반짝임 애니메이션 — 켜져 있을 때만 매 프레임 메시를 다시
        /// 그리게 한다(꺼져 있으면 아무 비용도 없다).</summary>
        private void Update()
        {
            if (_coherencyWarning)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            float rx = sizeMm.x / 2f;
            float ry = sizeMm.y / 2f;
            var outlineColor = isDisplacement ? DisplacementOutlineColor : OutlineColor;
            float outlineWidth = isDisplacement ? 3f : 1.5f;
            var drawFillColor = fillColor;

            if (_coherencyWarning)
            {
                // 코헤런시를 벗어난 모델은 하얗게 반짝인다 — 사인파로 흰색과
                // 원래 색 사이를 오간다. Update()가 매 프레임 SetVerticesDirty()를
                // 불러 이 메서드가 다시 실행되게 해서 재생된다.
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * CoherencyFlashSpeed);
                drawFillColor = Color.Lerp(fillColor, Color.white, pulse);
                outlineColor = Color.Lerp(outlineColor, Color.white, pulse);
                outlineWidth = Mathf.Max(outlineWidth, 2.5f);
            }

            // 채우기: 중심에서 팬(fan) 삼각분할.
            var center = new UIVertex { color = drawFillColor, position = Vector3.zero };
            vh.AddVert(center);

            var edge = new Vector3[VisualSides];
            for (int i = 0; i < VisualSides; i++)
            {
                float angle = i * Mathf.PI * 2f / VisualSides;
                edge[i] = new Vector3(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry, 0f);
                vh.AddVert(new UIVertex { color = drawFillColor, position = edge[i] });
            }

            for (int i = 0; i < VisualSides; i++)
            {
                int next = (i + 1) % VisualSides;
                vh.AddTriangle(0, i + 1, next + 1);
            }

            // 테두리: 각 변마다 안쪽/바깥쪽 정점 4개로 얇은 사각형.
            float half = outlineWidth / 2f;
            int baseIndex = vh.currentVertCount;
            for (int i = 0; i < VisualSides; i++)
            {
                int next = (i + 1) % VisualSides;
                Vector3 a = edge[i];
                Vector3 b = edge[next];
                Vector3 dir = (b - a).normalized;
                Vector3 normal = new Vector3(-dir.y, dir.x, 0f) * half;

                int vi = vh.currentVertCount;
                vh.AddVert(new UIVertex { color = outlineColor, position = a - normal });
                vh.AddVert(new UIVertex { color = outlineColor, position = a + normal });
                vh.AddVert(new UIVertex { color = outlineColor, position = b - normal });
                vh.AddVert(new UIVertex { color = outlineColor, position = b + normal });
                vh.AddTriangle(vi, vi + 1, vi + 2);
                vh.AddTriangle(vi + 1, vi + 3, vi + 2);
            }
            _ = baseIndex;
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
                MenuRequested?.Invoke(this, eventData.position);
                eventData.Use();
            }
        }
    }
}
