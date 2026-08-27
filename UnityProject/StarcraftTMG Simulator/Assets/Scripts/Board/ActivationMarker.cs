using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 활성화 마커 하나. 처음 배치되면 "이동" 면으로 시작하고, 우클릭할 때마다
    /// 이동 → 돌격 → 완료 순으로 계속 순환한다(삭제 없이 영원히 반복) — 삭제는
    /// 별도로 shift+우클릭(BoardManager가 RightClicked에서 처리). Godot판
    /// ActivationMarker.gd 포팅.
    /// </summary>
    public class ActivationMarker : MarkerBase
    {
        public const float MarkerSizeMm = 25.4f; // 1"
        public static readonly string[] StateSequence = { "movement", "assault", "done" };

        private Texture2D _textureMovement;
        private Texture2D _textureAssault;
        private Texture2D _textureDone;

        public string State { get; private set; } = "movement";

        public void Configure(Texture2D movement, Texture2D assault, Texture2D done)
        {
            _textureMovement = movement;
            _textureAssault = assault;
            _textureDone = done;
            RectTransform.sizeDelta = new Vector2(MarkerSizeMm, MarkerSizeMm);
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
                "assault" => _textureAssault,
                "done" => _textureDone,
                _ => _textureMovement,
            };
        }
    }
}
