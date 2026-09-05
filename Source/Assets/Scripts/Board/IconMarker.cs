using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 아이콘 하나로만 표시되는 간단한 1인치 마커(이동/돌격/전투/버프/디버프
    /// 등). 상태 순환 없음 — 좌클릭 드래그, 우클릭 한 번으로 즉시 삭제
    /// (BoardManager가 처리). Godot판 IconMarker.gd 포팅. kind별로 프리팹이
    /// 하나씩 있고, Kind/텍스처/크기는 그 프리팹에 미리 채워져 있다(코드는
    /// 인스턴스화만 함). Kind는 되돌리기 스냅샷에 쓰인다.
    /// </summary>
    public class IconMarker : MarkerBase
    {
        [SerializeField] private string kind = "";

        public string Kind => kind;
    }
}
