using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>네트워크 id(int) ↔ 라이브 오브젝트 매핑을 관리하는 작은 레지스트리.
    /// 예전엔 _networkedUnitsById/_networkedMarkersById로 각각 UnitSync.cs/
    /// Markers.cs 안에 선언돼 있었지만, 실제로는 Range.cs/UndoRedo.cs/Load.cs/
    /// MidGameHandoff.cs에서도 직접 읽고 썼다 — 이미 사실상 전역 서비스였던 걸
    /// 형식을 갖춘 클래스로 뺐다(2026-09-02, BoardManager 리팩토링 Phase 1).
    /// BoardManager가 유닛용/마커용 인스턴스를 하나씩 필드로 갖는다.</summary>
    internal sealed class NetworkIdentityRegistry<T> where T : class
    {
        private readonly Dictionary<int, T> _byId = new();

        public void Set(int id, T value)
        {
            _byId[id] = value;
        }

        public bool TryGet(int id, out T value)
        {
            return _byId.TryGetValue(id, out value);
        }

        public bool Remove(int id)
        {
            return _byId.Remove(id);
        }

        public void Clear()
        {
            _byId.Clear();
        }
    }
}
