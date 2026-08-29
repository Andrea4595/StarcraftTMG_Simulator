using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 지도 위에 배치된 지형 한 조각 — 텍스처를 그대로 보여주는 사각형이다
    /// (물리/충돌 없음, 순수 시각 참고용 — 고지대 경사로 등 통행 규칙은
    /// 사람이 직접 판정한다는 게 이 프로젝트의 방침). Godot판
    /// scenes/mission_setup/TerrainPiece.gd 포팅. MarkerBase와 같은 패턴으로
    /// RawImage를 직접 상속해서 텍스처 하나짜리 사각형을 표현한다.
    ///
    /// 실제 드래그 추적/회전은 MapSetupController(부모)가 전역 입력으로
    /// 처리한다 — 이 컴포넌트는 시작 신호만 올린다(Godot판과 동일한 역할
    /// 분담). 경계 클램프는 없다 — 지도 밖으로도 자유롭게 나갈 수 있다
    /// (사용자 요청).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class TerrainPiece : RawImage, IPointerDownHandler
    {
        public event Action<TerrainPiece> DragRequested;
        public event Action<TerrainPiece> DeleteRequested;

        private const float RotateStepDeg = 22.5f;

        public string ModuleId { get; private set; } = "";
        public int SizeValue { get; private set; }

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform != null ? _rectTransform : (_rectTransform = (RectTransform)transform);

        public Vector2 Center
        {
            get => RectTransform.anchoredPosition;
            set => RectTransform.anchoredPosition = value;
        }

        public float RotationDegrees
        {
            get => RectTransform.localEulerAngles.z;
            set => RectTransform.localEulerAngles = new Vector3(0f, 0f, value);
        }

        protected override void Awake()
        {
            base.Awake();
            RectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>텍스처를 불러와서 크기를 그대로 mm 크기로 쓴다(원본 이미지가
        /// 1mm = 1px로 제작됨 — Godot판 TerrainCatalog.gd 주석과 동일).</summary>
        public void Setup(TerrainCatalog.Module module)
        {
            ModuleId = module.Id;
            SizeValue = module.SizeValue;
            texture = Resources.Load<Texture2D>($"Terrains/{module.FileName}");
            if (texture != null)
            {
                RectTransform.sizeDelta = new Vector2(texture.width, texture.height);
            }
        }

        public void RotateStep(int direction = 1)
        {
            RotationDegrees = Mathf.Repeat(RotationDegrees + RotateStepDeg * direction, 360f);
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
