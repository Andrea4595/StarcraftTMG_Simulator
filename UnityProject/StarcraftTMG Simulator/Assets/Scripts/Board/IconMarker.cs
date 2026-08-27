using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 아이콘 하나로만 표시되는 간단한 1인치 마커(이동/돌격/전투/버프/디버프
    /// 등). 상태 순환 없음 — 좌클릭 드래그, 우클릭 한 번으로 즉시 삭제
    /// (BoardManager가 처리). Godot판 IconMarker.gd 포팅. Kind는 어떤
    /// 종류인지(되돌리기 스냅샷에 씀)를 나타낸다.
    /// </summary>
    public class IconMarker : MarkerBase
    {
        public const float MarkerSizeMm = 25.4f; // 1"

        public string Kind { get; private set; } = "";

        public void Configure(string kind, Texture2D iconTexture)
        {
            Kind = kind;
            texture = iconTexture;
            RectTransform.sizeDelta = new Vector2(MarkerSizeMm, MarkerSizeMm);
        }
    }
}
