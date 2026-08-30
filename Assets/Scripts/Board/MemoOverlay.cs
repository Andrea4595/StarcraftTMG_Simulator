using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>
    /// 마우스를 올린 유닛에 메모가 있는 모델이 있으면, 그 모델들 각각 아래에
    /// 메모를 텍스트로 보여준다. Godot판 MemoOverlay.gd 포팅 — BoardManager가
    /// 호버 상태가 바뀔 때마다 SetEntries()로 갱신한다. 커스텀 메시로 직접
    /// 그리는 대신 TextMeshPro 라벨을 재사용(풀링)한다.
    ///
    /// 라벨들은 이 컴포넌트의 자식으로, anchor/pivot을 건드리지 않는다(기본값
    /// (0.5,0.5) 그대로) — GuidelineOverlay의 Label과 동일하게, baseLayer의
    /// 중심-원점 mm 좌표계를 그대로 따라야 anchoredPosition이 Base.Center와
    /// 같은 의미가 된다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MemoOverlay : MonoBehaviour
    {
        private static readonly Color TextColor = new Color(1f, 0.95f, 0.75f, 0.95f);

        private readonly List<TextMeshProUGUI> _pool = new List<TextMeshProUGUI>();

        public void SetEntries(IReadOnlyList<(string Text, Vector2 Pos)> entries)
        {
            EnsurePoolSize(entries.Count);
            for (int i = 0; i < _pool.Count; i++)
            {
                if (i < entries.Count)
                {
                    _pool[i].gameObject.SetActive(true);
                    _pool[i].text = entries[i].Text;
                    ((RectTransform)_pool[i].transform).anchoredPosition = entries[i].Pos;
                }
                else
                {
                    _pool[i].gameObject.SetActive(false);
                }
            }
        }

        private void EnsurePoolSize(int count)
        {
            while (_pool.Count < count)
            {
                var go = new GameObject("MemoLabel", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var rect = (RectTransform)go.transform;
                rect.sizeDelta = new Vector2(220f, 20f);
                var label = go.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.Top;
                label.fontSize = 13f;
                label.color = TextColor;
                label.raycastTarget = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                _pool.Add(label);
            }
        }
    }
}
