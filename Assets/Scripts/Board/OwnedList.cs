using System.Collections;
using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>예비대/로스터 토큰/택티컬 카드처럼 "정의(Def) 목록을 그대로
    /// 들고 있다가 Add/RemoveAt/Clear/순회만 하는" 여러 컬렉션이 똑같은
    /// 모양이라 하나로 묶었다(2026-09-02, BoardManager 리팩토링 Phase 4) —
    /// PieceRoster(Phase 2)/NetworkIdentityRegistry(Phase 1)와 같은 이유로,
    /// 컬렉션마다 별 껍데기 클래스를 새로 만들지 않고 List&lt;T&gt;가 실제로
    /// 쓰이던 연산만 그대로 옮겼다. _pendingUnits/_pendingRosterTokens/
    /// _pendingTacticalCards가 이 타입으로 선언된다(BoardManager.cs).</summary>
    internal sealed class OwnedList<T> : IEnumerable<T>
    {
        private readonly List<T> _items = new();

        public int Count => _items.Count;

        public T this[int index] => _items[index];

        public void Add(T item)
        {
            _items.Add(item);
        }

        public void AddRange(IEnumerable<T> items)
        {
            _items.AddRange(items);
        }

        public void RemoveAt(int index)
        {
            _items.RemoveAt(index);
        }

        public void Clear()
        {
            _items.Clear();
        }

        public List<T>.Enumerator GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }
    }
}
