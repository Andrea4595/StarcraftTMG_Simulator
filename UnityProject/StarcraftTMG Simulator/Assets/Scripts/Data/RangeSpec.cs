namespace TmgBoard
{
    /// <summary>유닛에 등록된 "범위 표시" 항목 하나 — 거리(인치)와 상시 표시
    /// 여부. Godot판의 {"inch","always_show"} 딕셔너리 포팅. always_show는
    /// 한 번 정해지면 나중에 바꿀 수 없다(지우고 새로 추가해야 함) — 의도된
    /// 동작이다.</summary>
    public class RangeSpec
    {
        public float Inch;
        public bool AlwaysShow;
    }
}
