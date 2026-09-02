using System.Collections;
using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>보드 위 조각(Base) 전체 목록의 단일 소유자. 예전엔 BoardManager.cs의
    /// _pieces(List&lt;Base&gt;)를 9개 파일이 직접 Add/Remove/Clear/순회했다 —
    /// 형식을 갖춘 클래스로 뺐다(2026-09-02, BoardManager 리팩토링 Phase 2).
    /// List&lt;Base&gt;가 실제로 지원하던 연산(Add/Remove/Clear/foreach)만 그대로
    /// 옮겼다 — 그 이상은 아직 아무도 안 쓰므로 추가하지 않는다.</summary>
    internal sealed class PieceRoster : IEnumerable<Base>
    {
        private readonly List<Base> _pieces = new();

        public void Add(Base piece)
        {
            _pieces.Add(piece);
        }

        public bool Remove(Base piece)
        {
            return _pieces.Remove(piece);
        }

        public void Clear()
        {
            _pieces.Clear();
        }

        public List<Base>.Enumerator GetEnumerator()
        {
            return _pieces.GetEnumerator();
        }

        IEnumerator<Base> IEnumerable<Base>.GetEnumerator()
        {
            return _pieces.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _pieces.GetEnumerator();
        }
    }
}
