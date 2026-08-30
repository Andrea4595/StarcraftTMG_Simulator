using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TmgBoard
{
    /// <summary>
    /// 보드 위 마커(활성화/점령/아이콘) 공통 베이스. 드래그 시작과 우클릭(+shift
    /// 눌림 여부)만 이벤트로 올리고, 실제 위치 갱신/상태 순환/삭제는
    /// BoardManager가 처리한다(되돌리기 대상이므로 한 곳에서 관리 — Godot판
    /// ActivationMarker.gd/CaptureMarker.gd/IconMarker.gd와 같은 설계).
    /// RectTransform.pivot=(0.5,0.5)로 둬서 anchoredPosition이 곧 중심(mm)이
    /// 되게 한다(Base.cs와 같은 관례).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public abstract class MarkerBase : RawImage, IPointerDownHandler
    {
        public event Action<MarkerBase> DragRequested;
        public event Action<MarkerBase, bool> RightClicked; // bool = shift 눌림

        private RectTransform _rectTransform;
        public RectTransform RectTransform => _rectTransform != null ? _rectTransform : (_rectTransform = (RectTransform)transform);

        /// <summary>멀티플레이어 중 BoardNetworkSync가 호스트에서 발급한 id —
        /// 삭제를 방송할 때 어느 마커인지 지목하는 용도(마커 자체는
        /// NetworkObject가 아니라 각자 로컬로 만들어지므로 별도 식별자가
        /// 필요하다). 1인용/미연결 상태에서는 -1.</summary>
        public int NetworkMarkerId { get; set; } = -1;

        public Vector2 Center
        {
            get => RectTransform.anchoredPosition;
            set => RectTransform.anchoredPosition = value;
        }

        protected override void Awake()
        {
            base.Awake();
            RectTransform.pivot = new Vector2(0.5f, 0.5f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                DragRequested?.Invoke(this);
            }
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                RightClicked?.Invoke(this, shiftHeld);
            }
        }
    }
}
