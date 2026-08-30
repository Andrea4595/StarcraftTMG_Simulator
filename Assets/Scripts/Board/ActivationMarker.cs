using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 활성화 마커 하나. 처음 배치되면 "이동" 면으로 시작하고, 우클릭할 때마다
    /// 이동 → 돌격 → 완료 순으로 계속 순환한다(삭제 없이 영원히 반복) — 삭제는
    /// 별도로 shift+우클릭(BoardManager가 RightClicked에서 처리). Godot판
    /// ActivationMarker.gd 포팅. 세 상태 텍스처와 크기는 프리팹에 미리
    /// 채워져 있다(코드는 상태 전환에 따라 그 중 하나를 고르기만 함).
    /// </summary>
    public class ActivationMarker : MarkerBase
    {
        public static readonly string[] StateSequence = { "movement", "assault", "done" };

        [SerializeField] private Texture2D textureMovement;
        [SerializeField] private Texture2D textureAssault;
        [SerializeField] private Texture2D textureDone;

        public string State { get; private set; } = "movement";

        protected override void Awake()
        {
            base.Awake();
            RefreshTexture();
        }

        public void SetState(string state)
        {
            State = state;
            RefreshTexture();
        }

        private void RefreshTexture()
        {
            texture = State switch
            {
                "assault" => textureAssault,
                "done" => textureDone,
                _ => textureMovement,
            };
        }
    }
}
